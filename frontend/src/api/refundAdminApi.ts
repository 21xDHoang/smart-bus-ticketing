import type { TagProps } from 'antd';

// -----------------------------------------------------------------------------
// API quản trị hoàn tiền — phục vụ màn hình "Admin quản lý yêu cầu hoàn tiền" (story 8
// "Hoàn tiền tự động", Sprint 4, task của Nguyễn Đình Băng).
//
// Backend CHƯA có bảng Refunds / RefundLogs (migration Sprint 4 của Vàng Thị Dăm) và chưa
// có endpoint nào trong docs/api-contract.md, nên file này chạy dữ liệu giả
// (USE_MOCK_DATA = true) — cùng lối "Yêu cầu hủy/đổi vé" (ticketRequestApi.ts) đã làm khi
// backend chưa sẵn sàng. Khi backend + contract có, chỉ cần viết thật nhánh `api` rồi lật cờ.
//
// Hình dạng endpoint DỰ KIẾN (để Hiếu chốt vào api-contract.md — CHƯA cam kết):
//   GET    /admin/refunds?status=&source=&page=&pageSize=  → danh sách + phân trang.
//   POST   /admin/refunds/{id}/retry                       → gọi lại cổng hoàn cho dòng thất bại.
//   GET    /admin/refunds/export?status=&source=           → xuất toàn bộ danh sách khớp lọc.
//
// Vì sao "hoàn tiền" là bảng riêng, tách hẳn khỏi "hủy/đổi vé": xem doc-comment của
// backend ở CancellationRequestStatus.cs — bảng hủy vé trả lời "có cho hủy không", bảng
// Refunds trả lời "đã trả được tiền chưa". Hoàn tiền có vòng đời riêng (chờ → thành công →
// thất bại, kèm retry theo lịch) vì gọi cổng thanh toán có thể lỗi rồi phải thử lại.
// -----------------------------------------------------------------------------

/**
 * Nguồn gốc một khoản hoàn tiền — hai tình huống sinh ra dòng trong bảng `Refunds` (US 8):
 * - giao dịch thanh toán thất bại nhưng tiền đã bị giữ → hệ thống hoàn lại;
 * - yêu cầu hủy vé hợp lệ được duyệt → hoàn phần còn lại sau khi trừ phí hủy.
 */
export type RefundSource = 'PaymentFailure' | 'TicketCancellation';

/** Trạng thái một khoản hoàn — vòng đời "chờ → thành công → thất bại, kèm retry" của US 8. */
export type RefundStatus = 'Pending' | 'Succeeded' | 'Failed';

/** Một khoản hoàn tiền — dòng trong `GET /admin/refunds` (dự kiến). */
export interface Refund {
  id: string;

  /** Mã hoàn tiền nội bộ, dạng "RF-xxxxxxxx" — duy nhất, dùng để tra và đối soát. */
  refundCode: string;

  source: RefundSource;

  // --- Vé / chuyến được hoàn (chỉ để hiển thị, không phải khoá nghiệp vụ) ---
  ticketCode: string;
  routeCode: string;
  routeName: string;
  origin: string;
  destination: string;
  departureTime: string;
  seatNumber: string;

  /** Người nhận lại tiền — họ tên hiển thị của hành khách. */
  passengerName: string;

  /** Số tiền hoàn (VND). */
  amount: number;

  /** Cổng hoàn tiền — "MoMo" / "VNPay" / "ZaloPay" (chỉ để hiển thị). */
  methodName: string;

  /** Mã giao dịch gốc (`Payments.PaymentCode`) — để truy ngược giao dịch. */
  paymentCode: string;

  /** Mã giao dịch phía cổng hoàn; null khi cổng chưa trả về. */
  gatewayTransactionId: string | null;

  status: RefundStatus;

  /** Lý do cổng trả về khi thất bại; null khi không có. */
  failReason: string | null;

  /** Số lần hệ thống đã thử gọi lại cổng. */
  retryCount: number;

  createdAt: string;

  /** Thời điểm cổng xác nhận kết quả cuối (thành công/thất bại); null khi còn chờ. */
  processedAt: string | null;

  /** Chỉ đổi khi dòng có thay đổi (thử lại, lật trạng thái); null khi chưa từng đổi. */
  updatedAt: string | null;
}

/** Bộ lọc của màn hình Admin quản lý hoàn tiền — khớp tham số query dự kiến. */
export interface RefundFilterValue {
  /** Chỉ lấy một trạng thái. Bỏ trống = mọi trạng thái. */
  status?: RefundStatus;

  /** Chỉ lấy một nguồn gốc. Bỏ trống = mọi nguồn gốc. */
  source?: RefundSource;
}

/** Tham số `GET /admin/refunds` (dự kiến) — bộ lọc cộng phân trang. */
export interface RefundListParams extends RefundFilterValue {
  page?: number;
  pageSize?: number;
}

export interface RefundListResult {
  items: Refund[];
  total: number;
  page: number;
  pageSize: number;
}

interface RefundMeta {
  label: string;
  /** Tên màu preset của AntD — StatusTag hiểu được. */
  color: TagProps['color'];
}

export const REFUND_STATUS_META: Record<RefundStatus, RefundMeta> = {
  Pending: { label: 'Chờ xử lý', color: 'processing' },
  Succeeded: { label: 'Đã hoàn', color: 'success' },
  Failed: { label: 'Thất bại', color: 'error' },
};

export const REFUND_SOURCE_META: Record<RefundSource, RefundMeta> = {
  PaymentFailure: { label: 'Giao dịch lỗi', color: 'orange' },
  TicketCancellation: { label: 'Hủy vé', color: 'volcano' },
};

/** Lựa chọn cho bộ lọc trạng thái — dựng từ meta để nhãn không lệch với cột bảng. */
export const REFUND_STATUS_OPTIONS = (
  Object.keys(REFUND_STATUS_META) as RefundStatus[]
).map((value) => ({ value, label: REFUND_STATUS_META[value].label }));

/** Lựa chọn cho bộ lọc nguồn gốc — dựng từ meta, cùng lý do trên. */
export const REFUND_SOURCE_OPTIONS = (Object.keys(REFUND_SOURCE_META) as RefundSource[]).map(
  (value) => ({ value, label: REFUND_SOURCE_META[value].label }),
);

/** Lấy nhãn + màu cho một mã trạng thái. Mã lạ (backend bổ sung sau) vẫn hiển thị được. */
export function getRefundStatusMeta(status: string): RefundMeta {
  return REFUND_STATUS_META[status as RefundStatus] ?? { label: status, color: 'default' };
}

/** Lấy nhãn + màu cho một mã nguồn gốc. Mã lạ vẫn hiển thị được, không crash. */
export function getRefundSourceMeta(source: string): RefundMeta {
  return REFUND_SOURCE_META[source as RefundSource] ?? { label: source, color: 'default' };
}

export interface RefundAdminApi {
  /** Danh sách khoản hoàn khớp bộ lọc, mới nhất trước. Không khớp dòng nào → 200 với `items: []`. */
  list: (params: RefundListParams) => Promise<RefundListResult>;

  /** Gọi lại cổng hoàn cho một dòng đang `Failed`. Trả dòng đã cập nhật. */
  retry: (id: string) => Promise<Refund>;

  /** Toàn bộ danh sách khớp bộ lọc (không phân trang) — nguồn cho nút xuất file. */
  exportList: (filter: RefundFilterValue) => Promise<Refund[]>;
}

// ---------------------------------------------------------------------------
// Cờ chuyển giữa dữ liệu giả và API thật. Đang để `true` — backend Refunds chưa có.
const USE_MOCK_DATA = true;

const delay = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms));

// -------- Gọi API thật (dùng khi USE_MOCK_DATA = false) --------
// Backend chưa có bảng Refunds / RefundLogs nên nhánh này tạm chưa viết: nối axiosClient vào
// ba endpoint dự kiến ở đầu file khi api-contract.md đã chốt. Giữ lỗi rõ ràng để không có
// đường nào lặng lẽ chạy trên dữ liệu bịa ở màn quản trị tiền.
const api: RefundAdminApi = {
  async list() {
    throw new Error('Backend hoàn tiền chưa có — chưa thể tải danh sách hoàn tiền thật.');
  },
  async retry() {
    throw new Error('Backend hoàn tiền chưa có — chưa thể thử lại hoàn tiền thật.');
  },
  async exportList() {
    throw new Error('Backend hoàn tiền chưa có — chưa thể xuất danh sách hoàn tiền thật.');
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
 * Kho hoàn tiền giả — phủ đủ 2 nguồn gốc (Giao dịch lỗi / Hủy vé) và 3 trạng thái
 * (Chờ / Đã hoàn / Thất bại) để demo đủ bộ lọc và nút "Thử lại".
 *
 * Khai `let` chứ không `const`: thao tác "Thử lại" SỬA ngay kho này (lật Failed → Pending,
 * tăng số lần thử) để lượt tải sau phản ánh đúng thay đổi — không phải dữ liệu chết.
 */
let MOCK_REFUNDS: Refund[] = [
  {
    id: 'refund-pf-failed',
    refundCode: 'RF-4c1e7a2b',
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
    status: 'Failed',
    failReason: 'Cổng MoMo báo lỗi "insufficient balance" khi hoàn.',
    retryCount: 2,
    createdAt: hoursAgo(5),
    processedAt: hoursAgo(3),
    updatedAt: hoursAgo(3),
  },
  {
    id: 'refund-pf-pending',
    refundCode: 'RF-1b9c3d8e',
    source: 'PaymentFailure',
    ticketCode: 'TK-08-D4E5F6',
    routeCode: '08',
    routeName: 'Cầu Giấy — Bờ Hồ Hoàn Kiếm',
    origin: 'Cầu Giấy',
    destination: 'Bờ Hồ Hoàn Kiếm',
    departureTime: mockDeparture(0, 17),
    seatNumber: 'B03',
    passengerName: 'Trần Thị Bình',
    amount: 9_000,
    methodName: 'VNPay',
    paymentCode: 'PM-2d7e9f01',
    gatewayTransactionId: null,
    status: 'Pending',
    failReason: null,
    retryCount: 0,
    createdAt: hoursAgo(1),
    processedAt: null,
    updatedAt: null,
  },
  {
    id: 'refund-tc-pending',
    refundCode: 'RF-7e3a5c9d',
    source: 'TicketCancellation',
    ticketCode: 'TK-02-77C8D9',
    routeCode: '02',
    routeName: 'Bến xe Mỹ Đình — Bến xe Nước Ngầm',
    origin: 'Bến xe Mỹ Đình',
    destination: 'Bến xe Nước Ngầm',
    departureTime: mockDeparture(1, 6),
    seatNumber: 'C11',
    passengerName: 'Lê Văn Cường',
    amount: 12_000,
    methodName: 'ZaloPay',
    paymentCode: 'PM-0f2a7c3b',
    gatewayTransactionId: null,
    status: 'Pending',
    failReason: null,
    retryCount: 0,
    createdAt: hoursAgo(2),
    processedAt: null,
    updatedAt: null,
  },
  {
    id: 'refund-pf-succeeded',
    refundCode: 'RF-9d5f1e2c',
    source: 'PaymentFailure',
    ticketCode: 'TK-01-11A2B3',
    routeCode: '01',
    routeName: 'Bến xe Mỹ Đình — Bến xe Gia Lâm',
    origin: 'Bến xe Mỹ Đình',
    destination: 'Bến xe Gia Lâm',
    departureTime: mockDeparture(0, 9),
    seatNumber: 'A05',
    passengerName: 'Phạm Thị Dung',
    amount: 12_000,
    methodName: 'MoMo',
    paymentCode: 'PM-6c8a1e4f',
    gatewayTransactionId: 'MM-99182734',
    status: 'Succeeded',
    failReason: null,
    retryCount: 0,
    createdAt: hoursAgo(30),
    processedAt: hoursAgo(29),
    updatedAt: hoursAgo(29),
  },
  {
    id: 'refund-tc-succeeded-1',
    refundCode: 'RF-3a7b9d1e',
    source: 'TicketCancellation',
    ticketCode: 'TK-08-9B0C1D',
    routeCode: '08',
    routeName: 'Cầu Giấy — Bờ Hồ Hoàn Kiếm',
    origin: 'Cầu Giấy',
    destination: 'Bờ Hồ Hoàn Kiếm',
    departureTime: mockDeparture(-1, 18),
    seatNumber: 'A01',
    passengerName: 'Hoàng Văn Em',
    amount: 7_200,
    methodName: 'MoMo',
    paymentCode: 'PM-9d3e6a0b',
    gatewayTransactionId: 'MM-99182220',
    status: 'Succeeded',
    failReason: null,
    retryCount: 0,
    createdAt: hoursAgo(52),
    processedAt: hoursAgo(50),
    updatedAt: hoursAgo(50),
  },
  {
    id: 'refund-tc-failed-1',
    refundCode: 'RF-5c1e8a4d',
    source: 'TicketCancellation',
    ticketCode: 'TK-B10-3E4F5A',
    routeCode: 'B10',
    routeName: 'Bến xe Yên Nghĩa — Bến xe Mỹ Đình',
    origin: 'Bến xe Yên Nghĩa',
    destination: 'Bến xe Mỹ Đình',
    departureTime: mockDeparture(2, 8),
    seatNumber: 'D02',
    passengerName: 'Vũ Thị Giang',
    amount: 12_000,
    methodName: 'VNPay',
    paymentCode: 'PM-5b8c0d2e',
    gatewayTransactionId: null,
    status: 'Failed',
    failReason: 'VNPay hoàn tiền quá hạn chờ phản hồi (timeout).',
    retryCount: 1,
    createdAt: hoursAgo(70),
    processedAt: hoursAgo(68),
    updatedAt: hoursAgo(68),
  },
  {
    id: 'refund-tc-succeeded-2',
    refundCode: 'RF-8e2d7b3f',
    source: 'TicketCancellation',
    ticketCode: 'TK-01-A9F0D1',
    routeCode: '01',
    routeName: 'Bến xe Mỹ Đình — Bến xe Gia Lâm',
    origin: 'Bến xe Mỹ Đình',
    destination: 'Bến xe Gia Lâm',
    departureTime: mockDeparture(-3, 7),
    seatNumber: 'B08',
    passengerName: 'Đặng Văn Hùng',
    amount: 9_000,
    methodName: 'ZaloPay',
    paymentCode: 'PM-1f6b3a7c',
    gatewayTransactionId: 'ZP-77219834',
    status: 'Succeeded',
    failReason: null,
    retryCount: 0,
    createdAt: hoursAgo(100),
    processedAt: hoursAgo(99),
    updatedAt: hoursAgo(99),
  },
  {
    id: 'refund-tc-failed-2',
    refundCode: 'RF-4d9a0e6c',
    source: 'TicketCancellation',
    ticketCode: 'TK-02-C7E1F2',
    routeCode: '02',
    routeName: 'Bến xe Mỹ Đình — Bến xe Nước Ngầm',
    origin: 'Bến xe Mỹ Đình',
    destination: 'Bến xe Nước Ngầm',
    departureTime: mockDeparture(-2, 16),
    seatNumber: 'A02',
    passengerName: 'Bùi Thị Kiều',
    amount: 8_000,
    methodName: 'MoMo',
    paymentCode: 'PM-3c7e9b1d',
    gatewayTransactionId: null,
    status: 'Failed',
    failReason: 'Sai mã định danh tài khoản nhận hoàn.',
    retryCount: 3,
    createdAt: hoursAgo(120),
    processedAt: hoursAgo(118),
    updatedAt: hoursAgo(118),
  },
  {
    id: 'refund-pf-succeeded-2',
    refundCode: 'RF-6b0d3f8a',
    source: 'PaymentFailure',
    ticketCode: 'TK-B10-5D6E7F',
    routeCode: 'B10',
    routeName: 'Bến xe Yên Nghĩa — Bến xe Mỹ Đình',
    origin: 'Bến xe Yên Nghĩa',
    destination: 'Bến xe Mỹ Đình',
    departureTime: mockDeparture(-1, 19),
    seatNumber: 'C03',
    passengerName: 'Ngô Văn Long',
    amount: 15_000,
    methodName: 'VNPay',
    paymentCode: 'PM-8a2f5c0e',
    gatewayTransactionId: 'VN-55192010',
    status: 'Succeeded',
    failReason: null,
    retryCount: 0,
    createdAt: hoursAgo(140),
    processedAt: hoursAgo(139),
    updatedAt: hoursAgo(139),
  },
];

/** Lọc kho theo bộ lọc (không phân trang), sắp mới nhất trước — dùng chung cho list và export. */
function filterRefunds(filter: RefundFilterValue): Refund[] {
  return MOCK_REFUNDS.filter(
    (item) =>
      (filter.status === undefined || item.status === filter.status) &&
      (filter.source === undefined || item.source === filter.source),
  ).sort((a, b) => new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime());
}

const mock: RefundAdminApi = {
  async list(params) {
    await delay(400);

    const page = params.page ?? 1;
    const pageSize = params.pageSize ?? 10;
    const filtered = filterRefunds({ status: params.status, source: params.source });
    const start = (page - 1) * pageSize;

    // Trả bản sao để màn hình không vô tình sửa mảng gốc.
    return {
      items: filtered.slice(start, start + pageSize).map((item) => ({ ...item })),
      total: filtered.length,
      page,
      pageSize,
    };
  },

  async retry(id) {
    await delay(500);

    const index = MOCK_REFUNDS.findIndex((item) => item.id === id);
    if (index === -1) throw new Error('Không tìm thấy khoản hoàn tiền.');

    const current = MOCK_REFUNDS[index];
    if (current.status !== 'Failed') throw new Error('Chỉ thử lại được khoản hoàn đang thất bại.');

    // Thử lại = đưa dòng về trạng thái chờ và tăng bộ đếm — đúng việc service retry theo lịch
    // sẽ làm thật, nhưng ở đây Admin bấm tay để chạy ngay.
    const updated: Refund = {
      ...current,
      status: 'Pending',
      failReason: null,
      gatewayTransactionId: null,
      processedAt: null,
      retryCount: current.retryCount + 1,
      updatedAt: new Date().toISOString(),
    };
    MOCK_REFUNDS[index] = updated;

    return { ...updated };
  },

  async exportList(filter) {
    await delay(300);

    // Xuất toàn bộ danh sách khớp lọc, không cắt theo trang — cùng nghĩa với GET .../export.
    return filterRefunds(filter).map((item) => ({ ...item }));
  },
};

const refundAdminApi: RefundAdminApi = USE_MOCK_DATA ? mock : api;

export default refundAdminApi;
