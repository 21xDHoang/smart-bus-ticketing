import axiosClient from './axiosClient';

// -----------------------------------------------------------------------------
// API thanh toán — nối với nhóm endpoint của User Story 6 "Cổng thanh toán"
// (Sprint 3 trong Product_Backlog_Smart_Bus.xlsx).
//
// Backend CHƯA có endpoint "kiểm tra trạng thái giao dịch + đối soát tự động" — task của
// Trần Trung Hiếu, hợp đồng chưa vào docs/api-contract.md. Bảng Payments cũng CHƯA migrate
// (task "Migrate bảng Payments, Transactions, PaymentLogs" của Vàng Thị Dăm). Vì vậy kiểu
// dưới đây là DỰ KIẾN, bám theo tinh thần A9 ("mã giao dịch cổng + khoá chống trùng") để sau
// chỉ đổi cờ USE_MOCK_DATA (và tên endpoint nếu hợp đồng chốt khác).
//
// Hợp đồng DỰ KIẾN — chưa chốt, đặt kebab-case theo quy ước D1:
//   GET /payments/{paymentCode} → PaymentStatus  (kiểm tra trạng thái giao dịch theo mã thanh toán)
//
// Màn "chờ kết quả thanh toán" gọi liên tục endpoint này để biết giao dịch đã về Success/Failed
// chưa: callback của cổng thanh toán về backend là BẤT ĐỒNG BỘ, nên sau khi khách quay về từ
// cổng, frontend phải poll thay vì tin vào kết quả hiển thị ngay. "Timeout" là khái niệm PHÍA
// FRONTEND (xem components/paymentWaiting.ts) — không phải trạng thái lưu trong CSDL.
// -----------------------------------------------------------------------------

/** Cổng thanh toán — khớp danh sách US 6 (MoMo, VNPay, ZaloPay, thẻ ngân hàng). */
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

export interface PaymentApi {
  /** Kiểm tra trạng thái giao dịch theo mã thanh toán (task của Hiếu). */
  getStatus: (paymentCode: string) => Promise<PaymentStatusResult>;
}

// ---------------------------------------------------------------------------
// Cờ chuyển giữa dữ liệu giả và API thật. Đang để `true` vì endpoint chưa có (xem ghi chú
// đầu file); sau khi Hiếu chốt hợp đồng thì đổi xuống `false` — nhánh `api` đã viết sẵn.
const USE_MOCK_DATA = true;

const delay = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms));

// -------- Gọi API thật (dùng khi USE_MOCK_DATA = false) --------
const api: PaymentApi = {
  getStatus: (paymentCode) =>
    axiosClient.get<PaymentStatusResult, PaymentStatusResult>(`/payments/${paymentCode}`),
};

// -------- Dữ liệu giả (dùng khi USE_MOCK_DATA = true) --------
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
