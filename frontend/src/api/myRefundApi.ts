import type { Refund, RefundStatus } from './refundAdminApi';

// -----------------------------------------------------------------------------
// API THEO DÕI HOÀN TIỀN CỦA TÔI — phục vụ màn hình "Màn hình theo dõi trạng thái hoàn tiền
// của hành khách" (story 8 "Hoàn tiền tự động", Sprint 4, task của Dương Thị Hạnh).
//
// Đây là nửa HÀNH KHÁCH của story — khách chỉ thấy khoản hoàn của CHÍNH MÌNH và chỉ xem trạng
// thái, không được can thiệp. Nửa QUẢN TRỊ (xem mọi người + thử lại khoản lỗi) đã có ở
// refundAdminApi.ts (màn "Quản lý hoàn tiền" của Băng). Hai màn dùng CHUNG một dòng `Refunds`
// nên cùng tái dùng type `Refund` / `RefundStatus` và nhãn-màu trạng thái từ refundAdminApi.ts,
// không khai báo trùng để khỏi lệch.
//
// Hình dạng endpoint DỰ KIẾN (chờ Hiếu/Hoàng chốt vào api-contract.md — CHƯA cam kết):
//   GET /refunds/mine?status=…   → danh sách khoản hoàn của người đang đăng nhập (backend
//                                  lọc theo token), mới nhất trước.
//
// Backend chưa có bảng Refunds / RefundLogs nên file này chạy dữ liệu giả (USE_MOCK_DATA =
// true), cùng lối refundAdminApi.ts đã làm. Khi backend + contract có, chỉ cần viết thật nhánh
// `api` rồi lật cờ.
// -----------------------------------------------------------------------------

export interface MyRefundApi {
  /** Danh sách khoản hoàn của chính người đang đăng nhập. `status` bỏ trống = tất cả. */
  listMine: (status?: RefundStatus) => Promise<Refund[]>;
}

// ---------------------------------------------------------------------------
// Cờ chuyển giữa dữ liệu giả và API thật. Đang để `true` — backend Refunds chưa có.
const USE_MOCK_DATA = true;

const delay = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms));

// -------- Gọi API thật (dùng khi USE_MOCK_DATA = false) --------
// Backend chưa có GET /refunds/mine nên nhánh này tạm chưa viết: nối axiosClient vào endpoint
// dự kiến ở đầu file khi api-contract.md đã chốt. Giữ lỗi rõ ràng để màn hình không lặng lẽ
// chạy trên dữ liệu bịa ở chỗ hiển thị tiền.
const api: MyRefundApi = {
  async listMine() {
    throw new Error('Backend hoàn tiền chưa có — chưa thể tải danh sách hoàn tiền của bạn.');
  },
};

// -------- Dữ liệu giả (dùng khi USE_MOCK_DATA = true) --------
/** Mốc giờ khởi hành giả — `offsetDays` âm là quá khứ, dương là tương lai, `hourOfDay` là giờ. */
function mockDeparture(offsetDays: number, hourOfDay: number): string {
  const date = new Date();
  date.setDate(date.getDate() + offsetDays);
  date.setHours(hourOfDay, 30, 0, 0);
  return date.toISOString();
}

/** Mốc thời gian giả cách đây `hoursAgo` giờ. */
function hoursAgo(hoursAgo: number): string {
  return new Date(Date.now() - hoursAgo * 3_600_000).toISOString();
}

/**
 * Kho hoàn tiền giả của NGƯỜI ĐANG ĐĂNG NHẬP — phủ đủ 2 nguồn gốc (Giao dịch lỗi / Hủy vé)
 * và 3 trạng thái (Chờ / Đã hoàn / Thất bại) để demo đủ bộ lọc. Chỉ để XEM nên khai `const`
 * chứ không `let` như refundAdminApi.ts — màn hành khách không có thao tác sửa dòng nào.
 *
 * `passengerName` không hiển thị ở màn hành khách (toàn bộ dòng đều là "của tôi"), nên đặt
 * tên cố định cho đủ kiểu `Refund` mà không dùng ở UI.
 */
const MOCK_MY_REFUNDS: Refund[] = [
  {
    id: 'my-refund-pf-pending',
    refundCode: 'RF-7c2d9e4a',
    source: 'PaymentFailure',
    ticketCode: 'TK-01-A1B2C3',
    routeCode: '01',
    routeName: 'Bến xe Mỹ Đình — Bến xe Gia Lâm',
    origin: 'Bến xe Mỹ Đình',
    destination: 'Bến xe Gia Lâm',
    departureTime: mockDeparture(1, 8),
    seatNumber: 'A12',
    passengerName: 'Nguyễn Văn An',
    amount: 12_000,
    methodName: 'MoMo',
    paymentCode: 'PM-8f2b1d64',
    gatewayTransactionId: null,
    status: 'Pending',
    failReason: null,
    retryCount: 0,
    createdAt: hoursAgo(2),
    processedAt: null,
    updatedAt: null,
  },
  {
    id: 'my-refund-tc-pending',
    refundCode: 'RF-3b8f1c5d',
    source: 'TicketCancellation',
    ticketCode: 'TK-02-77C8D9',
    routeCode: '02',
    routeName: 'Bến xe Mỹ Đình — Bến xe Nước Ngầm',
    origin: 'Bến xe Mỹ Đình',
    destination: 'Bến xe Nước Ngầm',
    departureTime: mockDeparture(1, 6),
    seatNumber: 'C11',
    passengerName: 'Nguyễn Văn An',
    amount: 10_000,
    methodName: 'ZaloPay',
    paymentCode: 'PM-0f2a7c3b',
    gatewayTransactionId: null,
    status: 'Pending',
    failReason: null,
    retryCount: 0,
    createdAt: hoursAgo(5),
    processedAt: null,
    updatedAt: null,
  },
  {
    id: 'my-refund-pf-succeeded',
    refundCode: 'RF-9d5f1e2c',
    source: 'PaymentFailure',
    ticketCode: 'TK-08-D4E5F6',
    routeCode: '08',
    routeName: 'Cầu Giấy — Bờ Hồ Hoàn Kiếm',
    origin: 'Cầu Giấy',
    destination: 'Bờ Hồ Hoàn Kiếm',
    departureTime: mockDeparture(-1, 17),
    seatNumber: 'B03',
    passengerName: 'Nguyễn Văn An',
    amount: 9_000,
    methodName: 'VNPay',
    paymentCode: 'PM-2d7e9f01',
    gatewayTransactionId: 'VN-99182734',
    status: 'Succeeded',
    failReason: null,
    retryCount: 0,
    createdAt: hoursAgo(30),
    processedAt: hoursAgo(29),
    updatedAt: hoursAgo(29),
  },
  {
    id: 'my-refund-tc-succeeded',
    refundCode: 'RF-5c1e8a4d',
    source: 'TicketCancellation',
    ticketCode: 'TK-08-9B0C1D',
    routeCode: '08',
    routeName: 'Cầu Giấy — Bờ Hồ Hoàn Kiếm',
    origin: 'Cầu Giấy',
    destination: 'Bờ Hồ Hoàn Kiếm',
    departureTime: mockDeparture(-2, 18),
    seatNumber: 'A01',
    passengerName: 'Nguyễn Văn An',
    amount: 7_200,
    methodName: 'MoMo',
    paymentCode: 'PM-9d3e6a0b',
    gatewayTransactionId: 'MM-99182220',
    status: 'Succeeded',
    failReason: null,
    retryCount: 0,
    createdAt: hoursAgo(70),
    processedAt: hoursAgo(68),
    updatedAt: hoursAgo(68),
  },
  {
    id: 'my-refund-pf-failed',
    refundCode: 'RF-4d9a0e6c',
    source: 'PaymentFailure',
    ticketCode: 'TK-B10-3E4F5A',
    routeCode: 'B10',
    routeName: 'Bến xe Yên Nghĩa — Bến xe Mỹ Đình',
    origin: 'Bến xe Yên Nghĩa',
    destination: 'Bến xe Mỹ Đình',
    departureTime: mockDeparture(2, 8),
    seatNumber: 'D02',
    passengerName: 'Nguyễn Văn An',
    amount: 15_000,
    methodName: 'VNPay',
    paymentCode: 'PM-5b8c0d2e',
    gatewayTransactionId: null,
    status: 'Failed',
    failReason: 'Cổng VNPay hoàn tiền quá hạn chờ phản hồi (timeout).',
    retryCount: 1,
    createdAt: hoursAgo(20),
    processedAt: hoursAgo(19),
    updatedAt: hoursAgo(19),
  },
  {
    id: 'my-refund-tc-failed',
    refundCode: 'RF-6b0d3f8a',
    source: 'TicketCancellation',
    ticketCode: 'TK-02-C7E1F2',
    routeCode: '02',
    routeName: 'Bến xe Mỹ Đình — Bến xe Nước Ngầm',
    origin: 'Bến xe Mỹ Đình',
    destination: 'Bến xe Nước Ngầm',
    departureTime: mockDeparture(3, 16),
    seatNumber: 'A02',
    passengerName: 'Nguyễn Văn An',
    amount: 8_000,
    methodName: 'MoMo',
    paymentCode: 'PM-3c7e9b1d',
    gatewayTransactionId: null,
    status: 'Failed',
    failReason: 'Sai mã định danh tài khoản nhận hoàn.',
    retryCount: 2,
    createdAt: hoursAgo(45),
    processedAt: hoursAgo(44),
    updatedAt: hoursAgo(44),
  },
];

const mock: MyRefundApi = {
  async listMine(status) {
    await delay(400);

    const list = status ? MOCK_MY_REFUNDS.filter((item) => item.status === status) : MOCK_MY_REFUNDS;

    // Trả bản sao để màn hình không vô tình sửa mảng gốc, sắp mới nhất trước như contract.
    return [...list]
      .sort((a, b) => new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime())
      .map((item) => ({ ...item }));
  },
};

const myRefundApi: MyRefundApi = USE_MOCK_DATA ? mock : api;

export default myRefundApi;
