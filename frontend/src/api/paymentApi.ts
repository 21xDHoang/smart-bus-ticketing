import axiosClient from './axiosClient';

// -----------------------------------------------------------------------------
// API thanh toán — nối với nhóm endpoint của User Story 6 "Cổng thanh toán" (Sprint 3).
//
// Backend CHƯA có bảng Payments và CHƯA có endpoint thanh toán nào trong docs/api-contract.md,
// nên file này chạy dữ liệu giả (USE_MOCK_DATA = true) — cùng lối seatMapApi.ts / ticketApi.ts.
// Khi Dăm migrate xong bảng Payments và Hiếu chốt hợp đồng, chỉ cần viết thật nhánh `api` rồi
// lật cờ.
//
// File này gộp HAI màn của US 6 "Cổng thanh toán" (đều chưa có backend nên dùng chung cờ):
//   1. "Chọn phương thức thanh toán" (task của Dương Thị Hạnh):
//        GET  /payment-methods        → danh sách phương thức đang mở (listMethods)
//        POST /payments               → tạo một giao dịch thanh toán (pay)
//   2. "Chờ kết quả thanh toán" (task của Nguyễn Đình Băng):
//        GET  /payments/{paymentCode} → trạng thái giao dịch theo mã thanh toán (getStatus)
//
// Hợp đồng DỰ KIẾN — chưa chốt, đặt kebab-case theo quy ước D1.
// -----------------------------------------------------------------------------

/**
 * Cổng thanh toán — khớp danh sách US 6 (MoMo, VNPay, ZaloPay, thẻ ngân hàng).
 *
 * Kiểu này do màn "chờ kết quả thanh toán" dùng để gán nhãn cho `PaymentStatusResult.method`.
 * LƯU Ý: màn "chọn phương thức thanh toán" đang dùng một kiểu khác (`PaymentMethodCode`) vì hai
 * task viết song song trước khi backend có enum — hai kiểu cùng chỉ "cổng thanh toán". Khi Dăm
 * chốt enum backend thì gộp `PaymentMethod` và `PaymentMethodCode` thành một, đừng để lệch lâu.
 */
export type PaymentMethod = 'MoMo' | 'VNPay' | 'ZaloPay' | 'BankCard';

/** Trạng thái một giao dịch thanh toán — khớp enum PaymentStatus của backend (lưu chuỗi, A3). */
export type PaymentStatus = 'Pending' | 'Success' | 'Failed';

/**
 * Kết quả kiểm tra trạng thái một giao dịch thanh toán — trả về khi hỏi theo mã thanh toán.
 * `amount`/`method` đi kèm để màn hình chờ hiển thị đúng giao dịch đang chờ, không phải gọi
 * thêm endpoint nào.
 */
export interface PaymentStatusResult {
  /**
   * Mã thanh toán nội bộ do API tạo giao dịch sinh ra — mọi lần poll đều hỏi theo mã này.
   * Đây cũng là "khoá chống trùng" của bảng Payments (A9): callback trùng không trừ tiền 2 lần.
   */
  paymentCode: string;

  /** Số tiền thanh toán (VND) — numeric(12,2) theo quy ước A3. */
  amount: number;

  /** Cổng đã chọn ở bước "chọn phương thức thanh toán" (task của Dương Thị Hạnh). */
  method: PaymentMethod;

  status: PaymentStatus;

  /** Thời điểm thanh toán thành công (ISO 8601) — null khi chưa Success. */
  paidAt: string | null;

  /** Mã giao dịch do CỔNG sinh ra — null khi chưa Success. */
  gatewayTransactionId: string | null;

  /** Lý do thất bại khi `status === 'Failed'` — để màn hình hiển thị nguyên nhân. */
  message: string | null;
}

/**
 * Mã phương thức thanh toán — màn "chọn phương thức thanh toán" dùng để định danh lựa chọn.
 * Lưu chuỗi theo quy ước A3, khớp enum của backend sau này (xem ghi chú ở `PaymentMethod`).
 */
export type PaymentMethodCode = 'VnPay' | 'Momo' | 'ZaloPay' | 'BankTransfer' | 'Cash';

/** Một phương thức thanh toán màn hình liệt kê để hành khách chọn. */
export interface PaymentMethodOption {
  code: PaymentMethodCode;

  /** Tên hiển thị — "VNPay", "Ví MoMo"… */
  name: string;

  /** Một câu mô tả ngắn để hành khách biết phương thức này thanh toán kiểu gì. */
  description: string;

  /** Biểu tượng hiển thị — emoji ngắn gọn, cùng lối 🚌 của app. */
  icon: string;
}

/** Kết quả một giao dịch thanh toán thành công. */
export interface PaymentResult {
  /** Mã giao dịch do cổng thanh toán sinh — để đối soát về sau. */
  transactionId: string;

  methodCode: PaymentMethodCode;

  status: 'Paid';

  /** Thời điểm thanh toán (ISO 8601). */
  paidAt: string;
}

/** Dữ liệu gửi đi khi tạo giao dịch thanh toán. */
export interface PaymentRequest {
  methodCode: PaymentMethodCode;

  tripId: string;

  /** Các số ghế đang thanh toán, ví dụ ["A1", "A2"] — chỉ để lưu vết, không phải khoá nghiệp vụ. */
  seatNumbers: string[];

  /** Tổng tiền cần thanh toán (VND). */
  total: number;
}

export interface PaymentApi {
  /** Danh sách phương thức thanh toán đang mở (màn chọn phương thức). */
  listMethods: () => Promise<PaymentMethodOption[]>;

  /** Tạo giao dịch thanh toán — backend chưa có nên nhánh giả chỉ bắt chước thành công. */
  pay: (request: PaymentRequest) => Promise<PaymentResult>;

  /** Kiểm tra trạng thái giao dịch theo mã thanh toán (màn chờ kết quả). */
  getStatus: (paymentCode: string) => Promise<PaymentStatusResult>;
}

// ---------------------------------------------------------------------------
// Cờ chuyển giữa dữ liệu giả và API thật. Đang để `true` vì endpoint chưa có (xem ghi chú
// đầu file); sau khi Hiếu chốt hợp đồng thì đổi xuống `false` — nhánh `api` đã viết sẵn.
const USE_MOCK_DATA = true;

const delay = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms));

// -------- Gọi API thật (dùng khi USE_MOCK_DATA = false) --------
const api: PaymentApi = {
  listMethods: () =>
    axiosClient.get<PaymentMethodOption[], PaymentMethodOption[]>('/payment-methods'),

  pay: (request) => axiosClient.post<PaymentResult, PaymentResult>('/payments', request),

  getStatus: (paymentCode) =>
    axiosClient.get<PaymentStatusResult, PaymentStatusResult>(`/payments/${paymentCode}`),
};

// -------- Dữ liệu giả (dùng khi USE_MOCK_DATA = true) --------

// --- Phương thức thanh toán (màn "chọn phương thức") ---
// Năm phương thức đại diện cho các kênh phổ biến ở thị trường Việt Nam. Tập mã cố ý đặt
// PascalCase giống enum backend sẽ khai báo, để sau lật cờ không phải đổi dữ liệu phía UI.
const MOCK_METHODS: PaymentMethodOption[] = [
  {
    code: 'VnPay',
    name: 'VNPay',
    description: 'Thanh toán qua cổng VNPay — quét QR, thẻ ATM hoặc internet banking.',
    icon: '💳',
  },
  {
    code: 'Momo',
    name: 'Ví MoMo',
    description: 'Quét mã QR bằng ví MoMo trên điện thoại.',
    icon: '📱',
  },
  {
    code: 'ZaloPay',
    name: 'Ví ZaloPay',
    description: 'Thanh toán nhanh qua ví ZaloPay.',
    icon: '💬',
  },
  {
    code: 'BankTransfer',
    name: 'Chuyển khoản ngân hàng',
    description: 'Chuyển khoản tới tài khoản nhà xe rồi xác nhận biên lai.',
    icon: '🏦',
  },
  {
    code: 'Cash',
    name: 'Tiền mặt khi lên xe',
    description: 'Thanh toán trực tiếp cho nhân viên soát vé khi lên xe.',
    icon: '💵',
  },
];

// --- Trạng thái giao dịch (màn "chờ kết quả") ---
// Một giao dịch giả để màn "chờ kết quả thanh toán" có gì để demo. Mã thanh toán quyết định
// kết cục, để vừa demo được đường vui (thành công) vừa demo được đường lỗi:
//   `demo-fail…`  → Failed sau 5 giây (thấy màn báo "thanh toán thất bại").
//   `demo-stuck…` → Pending mãi mãi (thấy màn xử lý timeout — chờ hết PAYMENT_TIMEOUT_SECONDS).
//   còn lại       → Success sau 8 giây (đường vui, khách quay về từ cổng là thấy thành công).
//
// `startedAt` lưu mốc lần poll ĐẦU TIÊN của mỗi mã để đếm thời gian trôi — mô phỏng việc
// callback của cổng về backend sau vài giây, thay vì trả kết quả ngay.

/** Mốc bắt đầu "giao dịch" của từng mã — để trạng thái đổi dần theo thời gian thật. */
const mockStartedAt = new Map<string, number>();

function mockStatus(paymentCode: string): PaymentStatus {
  if (paymentCode.startsWith('demo-stuck')) return 'Pending';

  const startedAt = mockStartedAt.get(paymentCode) ?? Date.now();
  mockStartedAt.set(paymentCode, startedAt);

  const elapsedSeconds = (Date.now() - startedAt) / 1000;

  if (paymentCode.startsWith('demo-fail')) {
    return elapsedSeconds >= 5 ? 'Failed' : 'Pending';
  }

  return elapsedSeconds >= 8 ? 'Success' : 'Pending';
}

const mock: PaymentApi = {
  async listMethods() {
    await delay(300);
    return MOCK_METHODS.map((method) => ({ ...method }));
  },

  async pay(request) {
    // Giả lập cổng thanh toán xử lý — chờ một nhịp rồi trả về mã giao dịch thành công.
    await delay(900);

    const transactionId = `PAY-${Date.now().toString(36).toUpperCase()}`;

    return {
      transactionId,
      methodCode: request.methodCode,
      status: 'Paid' as const,
      paidAt: new Date().toISOString(),
    };
  },

  async getStatus(paymentCode) {
    await delay(400);

    const status = mockStatus(paymentCode);
    return {
      paymentCode,
      amount: 24000,
      method: 'MoMo',
      status,
      paidAt: status === 'Success' ? new Date().toISOString() : null,
      gatewayTransactionId: status === 'Success' ? `gw-${paymentCode}` : null,
      message: status === 'Failed' ? 'Giao dịch bị từ chối bởi cổng thanh toán.' : null,
    };
  },
};

const paymentApi: PaymentApi = USE_MOCK_DATA ? mock : api;

export default paymentApi;
