import axiosClient from './axiosClient';

// -----------------------------------------------------------------------------
// API thanh toán — nối với nhóm endpoint /payments của backend (US 6 "Cổng thanh toán").
//
// Luồng thật (docs/api-contract.md, mục "Thanh toán — /payments"):
//   1. POST /payments              → tạo giao dịch Pending và trả `payUrl` của cổng.
//   2. Cả trang chuyển sang `payUrl` → khách trả tiền trên trang của cổng.
//   3. Cổng trả khách về /payment-waiting?paymentCode=… (ReturnUrl do backend dựng —
//      PaymentService.cs).
//   4. GET /payments/{paymentCode} → màn chờ poll 3 giây/lần tới Success/Failed.
//
// Vé CHỈ được phát hành khi giao dịch Success: IPN của cổng (hoặc job đối soát) chốt trạng
// thái rồi gọi IPaymentTicketIssuer. Vì vậy `pay()` trả về KHÔNG có nghĩa là đã mua xong vé —
// đừng chuyển khách sang "Vé của tôi" ngay sau bước 1.
//
// GET /payment-methods CHƯA được dựng (api-contract.md ghi "Chưa làm"), nên danh sách phương
// thức là hằng số phía FE — đúng bốn mã đã chốt ở mục "Enum chốt" của hợp đồng. Khi endpoint
// đó có thật thì chỉ cần thay phần thân `listMethods`.
// -----------------------------------------------------------------------------

/**
 * Mã phương thức thanh toán — hợp đồng chốt đúng bốn mã này (api-contract.md, mục "Enum chốt").
 * `BankCard` không phải cổng riêng: nó là kênh thẻ nội địa của VNPay.
 */
export type PaymentMethod = 'MoMo' | 'VNPay' | 'ZaloPay' | 'BankCard';

/**
 * Tên cũ của `PaymentMethod`, giữ lại cho các màn đã import (màn chọn phương thức). Hai tên
 * cùng một kiểu — đừng để chúng lệch nhau như hai bản nháp trước đây.
 */
export type PaymentMethodCode = PaymentMethod;

/** Trạng thái một giao dịch thanh toán — khớp enum PaymentStatus của backend (lưu chuỗi, A3). */
export type PaymentStatus = 'Pending' | 'Success' | 'Failed';

/**
 * Kết quả kiểm tra trạng thái một giao dịch — GET /payments/{paymentCode}.
 * `amount`/`method` đi kèm để màn chờ hiển thị đúng giao dịch đang chờ, không phải gọi thêm
 * endpoint nào.
 */
export interface PaymentStatusResult {
  /**
   * Mã thanh toán nội bộ do API tạo giao dịch sinh ra — mọi lần poll đều hỏi theo mã này.
   * Đây cũng là "khoá chống trùng" của bảng Payments (A9): callback trùng không trừ tiền 2 lần.
   */
  paymentCode: string;

  /** Số tiền phải trả (VND) — con số đã gửi lên cổng, numeric(12,2) theo quy ước A3. */
  amount: number;

  method: PaymentMethod;

  status: PaymentStatus;

  /** Thời điểm thanh toán thành công (ISO 8601) — null khi chưa Success. */
  paidAt: string | null;

  /** Mã giao dịch do CỔNG sinh ra — null khi chưa Success. */
  gatewayTransactionId: string | null;

  /** Lý do thất bại khi `status === 'Failed'` — để màn hình hiển thị nguyên nhân. */
  message: string | null;
}

/** Một phương thức thanh toán màn hình liệt kê để hành khách chọn. */
export interface PaymentMethodOption {
  code: PaymentMethodCode;

  /** Tên hiển thị — "VNPay", "Ví MoMo"… */
  name: string;

  /** Một câu mô tả ngắn để hành khách biết phương thức này thanh toán kiểu gì. */
  description: string;

  /** Biểu tượng hiển thị — emoji ngắn gọn, đồng bộ với lối biểu tượng của app. */
  icon: string;
}

/**
 * Kết quả POST /payments — khớp `CreatePaymentResult` của backend.
 *
 * `amount` là số tiền PHẢI TRẢ, đã trừ giảm giá; `originalAmount` là tổng trước giảm. Server
 * tự tính lại cả hai từ Fares + voucher, không tin con số nào FE gửi lên.
 */
export interface CreatePaymentResult {
  /** Mã giao dịch nội bộ ("PM-xxxxxxxx") — khoá poll trạng thái ở màn chờ. */
  paymentCode: string;

  /** Tổng tiền TRƯỚC giảm giá — đúng bằng `total` gửi lên. */
  originalAmount: number;

  /** Số tiền được giảm từ voucher; 0 khi không dùng mã. */
  discountAmount: number;

  /** Mã voucher đã dùng; null khi không dùng mã. */
  voucherCode: string | null;

  /** Số tiền phải trả — con số gửi lên cổng. */
  amount: number;

  method: PaymentMethod;

  /** Lúc vừa tạo luôn là "Pending"; Success/Failed chỉ lật qua callback hoặc job đối soát. */
  status: PaymentStatus;

  /** URL chuyển khách sang trang trả tiền của cổng — mở bằng `window.location.assign`. */
  payUrl: string;
}

/** Dữ liệu gửi đi khi tạo giao dịch thanh toán. */
export interface PaymentRequest {
  methodCode: PaymentMethodCode;

  tripId: string;

  /** Các số ghế đang thanh toán, ví dụ ["A1", "A2"] — chỉ để lưu vết, không phải khoá nghiệp vụ. */
  seatNumbers: string[];

  /** Tổng tiền TRƯỚC giảm giá (VND). Server so khớp lại với giá ghế trong Fares. */
  total: number;

  /** Mã voucher khách đã bấm xác nhận; vắng = không dùng mã. */
  voucherCode?: string;
}

export interface PaymentApi {
  /** Danh sách phương thức thanh toán đang mở (màn chọn phương thức). */
  listMethods: () => Promise<PaymentMethodOption[]>;

  /** Tạo giao dịch thanh toán — trả `payUrl` để chuyển khách sang cổng. */
  pay: (request: PaymentRequest) => Promise<CreatePaymentResult>;

  /** Kiểm tra trạng thái giao dịch theo mã thanh toán (màn chờ kết quả). */
  getStatus: (paymentCode: string) => Promise<PaymentStatusResult>;
}

// ---------------------------------------------------------------------------
// Bốn phương thức đã chốt — cùng thứ tự và nhãn với danh sách US 6. `GET /payment-methods`
// chưa có (xem ghi chú đầu file) nên đây là nguồn duy nhất phía FE.
const PAYMENT_METHODS: PaymentMethodOption[] = [
  {
    code: 'MoMo',
    name: 'Ví MoMo',
    description: 'Quét mã QR hoặc thanh toán bằng ví MoMo trên điện thoại.',
    icon: '📱',
  },
  {
    code: 'VNPay',
    name: 'VNPay',
    description: 'Thanh toán qua cổng VNPay — QR, thẻ ATM hoặc internet banking.',
    icon: '💳',
  },
  {
    code: 'ZaloPay',
    name: 'Ví ZaloPay',
    description: 'Thanh toán nhanh qua ví ZaloPay.',
    icon: '💬',
  },
  {
    code: 'BankCard',
    name: 'Thẻ ngân hàng',
    description: 'Thẻ ATM nội địa hoặc tài khoản ngân hàng, qua kênh thẻ của VNPay.',
    icon: '🏦',
  },
];

const paymentApi: PaymentApi = {
  async listMethods() {
    // Bản sao để màn hình không vô tình sửa hằng số dùng chung.
    return PAYMENT_METHODS.map((method) => ({ ...method }));
  },

  pay: (request) =>
    axiosClient.post<CreatePaymentResult, CreatePaymentResult>('/payments', request),

  getStatus: (paymentCode) =>
    axiosClient.get<PaymentStatusResult, PaymentStatusResult>(`/payments/${paymentCode}`),
};

export default paymentApi;
