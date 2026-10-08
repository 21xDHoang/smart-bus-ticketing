import axiosClient from './axiosClient';

// -----------------------------------------------------------------------------
// API thanh toán — nối với nhóm endpoint của User Story 6 "Cổng thanh toán" (Sprint 4).
//
// Backend CHƯA có bảng Payments và chưa có endpoint thanh toán nào trong docs/api-contract.md,
// nên file này chạy dữ liệu giả (USE_MOCK_DATA = true) — cùng lối seatHoldApi.ts / ticketApi.ts
// đã làm khi backend chưa sẵn sàng. Khi Dăm migrate xong bảng Payments và Hiếu chốt hợp đồng,
// chỉ cần viết thật nhánh `api` rồi lật cờ.
//
// Hình dạng endpoint DỰ KIẾN (để Hiếu chốt vào api-contract.md — CHƯA cam kết):
//   GET  /payment-methods  → PaymentMethod[]   (các phương thức đang mở cho hành khách)
//   POST /payments         → PaymentResult     (tạo một giao dịch thanh toán)
// -----------------------------------------------------------------------------

/** Mã phương thức thanh toán — lưu chuỗi theo quy ước A3, khớp enum của backend sau này. */
export type PaymentMethodCode = 'VnPay' | 'Momo' | 'ZaloPay' | 'BankTransfer' | 'Cash';

/** Một phương thức thanh toán màn hình liệt kê để hành khách chọn. */
export interface PaymentMethod {
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
  /** Danh sách phương thức thanh toán đang mở. */
  listMethods: () => Promise<PaymentMethod[]>;

  /** Tạo giao dịch thanh toán — backend chưa có nên nhánh giả chỉ bắt chước thành công. */
  pay: (request: PaymentRequest) => Promise<PaymentResult>;
}

// ---------------------------------------------------------------------------
// Cờ chuyển giữa dữ liệu giả và API thật. Đang để `true` vì endpoint chưa có (xem ghi chú
// đầu file); sau khi Hiếu chốt hợp đồng thì đổi xuống `false` — nhánh `api` đã viết sẵn.
const USE_MOCK_DATA = true;

const delay = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms));

// -------- Gọi API thật (dùng khi USE_MOCK_DATA = false) --------
const api: PaymentApi = {
  listMethods: () =>
    axiosClient.get<PaymentMethod[], PaymentMethod[]>('/payment-methods'),

  pay: (request) => axiosClient.post<PaymentResult, PaymentResult>('/payments', request),
};

// -------- Dữ liệu giả (dùng khi USE_MOCK_DATA = true) --------
// Năm phương thức đại diện cho các kênh phổ biến ở thị trường Việt Nam. Tập mã cố ý đặt
// PascalCase giống enum backend sẽ khai báo, để sau lật cờ không phải đổi dữ liệu phía UI.

const MOCK_METHODS: PaymentMethod[] = [
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
};

const paymentApi: PaymentApi = USE_MOCK_DATA ? mock : api;

export default paymentApi;
