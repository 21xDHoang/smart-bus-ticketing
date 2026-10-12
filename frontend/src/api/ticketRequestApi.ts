import type { TagProps } from 'antd';

// -----------------------------------------------------------------------------
// API "Yêu cầu hủy / đổi vé" — phục vụ màn hình "Theo dõi trạng thái yêu cầu hủy - đổi"
// (story 5 "Hủy & Đổi vé", Sprint 4, task của Nguyễn Đình Băng).
//
// Backend CHƯA có bảng TicketChanges / CancellationRequests (migration Sprint 4 của Dăm) và
// chưa có endpoint nào trong docs/api-contract.md, nên file này chạy dữ liệu giả
// (USE_MOCK_DATA = true) — cùng lối màn "Vé của tôi" (ticketApi.ts) và "Gửi phản ánh"
// (feedbackSubmitApi.ts) đã làm khi backend chưa sẵn sàng. Khi backend + contract có, chỉ
// cần viết thật nhánh `api` rồi lật cờ.
//
// Hình dạng endpoint DỰ KIẾN (để Hiếu chốt vào api-contract.md — CHƯA cam kết):
//   GET /ticket-requests/me?status=…  → danh sách yêu cầu hủy/đổi của chính người gọi.
//
// Hai bảng gộp về một màn (CancellationRequests + TicketChanges) vì hành khách chỉ quan tâm
// "tôi đã gửi yêu cầu gì, giờ ra sao" — cột `type` phân biệt Hủy vé hay Đổi chuyến.
// -----------------------------------------------------------------------------

/** Loại yêu cầu — HỦY vé (CancellationRequests) hay ĐỔI chuyến (TicketChanges). */
export type TicketRequestType = 'Cancel' | 'Change';

/** Trạng thái xử lý — vòng đời từ lúc gửi tới khi nhà xe duyệt / từ chối. */
export type TicketRequestStatus = 'Pending' | 'Approved' | 'Rejected';

/** Một yêu cầu hủy / đổi vé của hành khách — dòng trong GET /ticket-requests/me (dự kiến). */
export interface TicketRequest {
  id: string;

  type: TicketRequestType;

  // --- Vé / chuyến gốc (bị hủy hoặc bị đổi) ---
  /** Mã vé gốc — mã in trên vé điện tử. */
  ticketCode: string;

  /** Mã tuyến hiển thị — "01", "B10". */
  routeCode: string;

  routeName: string;

  origin: string;

  destination: string;

  /** Giờ khởi hành của chuyến gốc (ISO 8601). */
  departureTime: string;

  seatNumber: string;

  /** Giá vé gốc đã thanh toán (VND). */
  price: number;

  // --- Chỉ có khi `type = 'Change'` — chuyến mới khách muốn đổi sang ---
  newRouteCode: string | null;

  newRouteName: string | null;

  newDepartureTime: string | null;

  newSeatNumber: string | null;

  // --- Tiền ---
  /**
   * Với `type = 'Cancel'`: phí hủy (VND) theo quy định thời gian (trước 24h/12h/2h).
   * Với `type = 'Change'`: chênh lệch giá so với vé cũ — dương là trả thêm, âm là hoàn lại.
   */
  fee: number;

  /** Số tiền hoàn lại (VND) = `price - fee`. Chỉ có với `type = 'Cancel'`; đổi chuyến thì null. */
  refundAmount: number | null;

  /** Lý do khách gửi kèm. `null` khi khách không nhập. */
  reason: string | null;

  status: TicketRequestStatus;

  createdAt: string;

  /** Chỉ đổi khi nhà xe duyệt / từ chối; null khi vẫn còn chờ duyệt. */
  updatedAt: string | null;

  /** Ghi chú của nhà xe khi duyệt / từ chối (ví dụ lý do từ chối). null khi chưa xử lý. */
  reviewNote: string | null;
}

interface TicketRequestMeta {
  label: string;
  /** Tên màu preset của Tag AntD. */
  color: TagProps['color'];
}

export const TICKET_REQUEST_TYPE_META: Record<TicketRequestType, TicketRequestMeta> = {
  Cancel: { label: 'Hủy vé', color: 'volcano' },
  Change: { label: 'Đổi chuyến', color: 'geekblue' },
};

export const TICKET_REQUEST_STATUS_META: Record<TicketRequestStatus, TicketRequestMeta> = {
  Pending: { label: 'Chờ duyệt', color: 'processing' },
  Approved: { label: 'Đã duyệt', color: 'success' },
  Rejected: { label: 'Từ chối', color: 'error' },
};

/** Danh sách trạng thái cho bộ lọc, theo đúng thứ tự vòng đời yêu cầu. */
export const TICKET_REQUEST_STATUS_OPTIONS = (
  Object.keys(TICKET_REQUEST_STATUS_META) as TicketRequestStatus[]
).map((value) => ({ value, label: TICKET_REQUEST_STATUS_META[value].label }));

/** Lấy nhãn + màu cho một mã loại/trạng thái. Mã lạ (backend bổ sung sau) vẫn hiển thị được. */
export function getTicketRequestTypeMeta(type: string): TicketRequestMeta {
  return TICKET_REQUEST_TYPE_META[type as TicketRequestType] ?? { label: type, color: 'default' };
}

export function getTicketRequestStatusMeta(status: string): TicketRequestMeta {
  return TICKET_REQUEST_STATUS_META[status as TicketRequestStatus] ?? {
    label: status,
    color: 'default',
  };
}

export interface TicketRequestApi {
  /** Danh sách yêu cầu hủy/đổi của chính người gọi, mới nhất trước. `status` bỏ trống = tất cả. */
  listMine: (status?: TicketRequestStatus) => Promise<TicketRequest[]>;
}

// ---------------------------------------------------------------------------
// Cờ chuyển giữa dữ liệu giả và API thật. Đang để `true` — backend TicketChanges /
// CancellationRequests chưa có.
const USE_MOCK_DATA = true;

const delay = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms));

// -------- Gọi API thật (dùng khi USE_MOCK_DATA = false) --------
// Backend chưa có bảng TicketChanges / CancellationRequests nên nhánh này tạm chưa viết: nối
// axiosClient vào endpoint dự kiến ở đầu file khi api-contract.md đã chốt.
const api: TicketRequestApi = {
  async listMine() {
    throw new Error('Backend hủy/đổi vé chưa có — chưa thể tải danh sách yêu cầu thật.');
  },
};

// -------- Dữ liệu giả (dùng khi USE_MOCK_DATA = true) --------
/** Mốc thời gian giả — `offsetDays` âm là quá khứ, dương là tương lai, `hourOfDay` là giờ. */
function mockDeparture(offsetDays: number, hourOfDay: number): string {
  const date = new Date();
  date.setDate(date.getDate() + offsetDays);
  date.setHours(hourOfDay, 30, 0, 0);
  return date.toISOString();
}

function mockCreatedAt(daysAgo: number): string {
  const date = new Date();
  date.setDate(date.getDate() - daysAgo);
  return date.toISOString();
}

/** Kho yêu cầu giả — phủ đủ 2 loại (Hủy/Đổi) và 3 trạng thái (Chờ/Duyệt/Từ chối) để demo. */
const MOCK_TICKET_REQUESTS: TicketRequest[] = [
  {
    id: 'req-change-pending',
    type: 'Change',
    ticketCode: 'TK-01-A1B2C3',
    routeCode: '01',
    routeName: 'Bến xe Mỹ Đình — Bến xe Gia Lâm',
    origin: 'Bến xe Mỹ Đình',
    destination: 'Bến xe Gia Lâm',
    departureTime: mockDeparture(1, 8),
    seatNumber: 'A12',
    price: 12_000,
    newRouteCode: '01',
    newRouteName: 'Bến xe Mỹ Đình — Bến xe Gia Lâm',
    newDepartureTime: mockDeparture(2, 9),
    newSeatNumber: 'A03',
    fee: 0,
    refundAmount: null,
    reason: 'Có việc đột xuất, xin chuyển sang chuyến sáng hôm sau.',
    status: 'Pending',
    createdAt: mockCreatedAt(0),
    updatedAt: null,
    reviewNote: null,
  },
  {
    id: 'req-cancel-approved',
    type: 'Cancel',
    ticketCode: 'TK-08-D4E5F6',
    routeCode: '08',
    routeName: 'Cầu Giấy — Bờ Hồ Hoàn Kiếm',
    origin: 'Cầu Giấy',
    destination: 'Bờ Hồ Hoàn Kiếm',
    departureTime: mockDeparture(2, 17),
    seatNumber: 'B03',
    price: 9_000,
    newRouteCode: null,
    newRouteName: null,
    newDepartureTime: null,
    newSeatNumber: null,
    fee: 1_800,
    refundAmount: 7_200,
    reason: 'Đổi kế hoạch, không đi nữa.',
    status: 'Approved',
    createdAt: mockCreatedAt(1),
    updatedAt: mockCreatedAt(1),
    reviewNote: 'Đã hoàn tiền về ví trong 1–3 ngày làm việc.',
  },
  {
    id: 'req-cancel-rejected',
    type: 'Cancel',
    ticketCode: 'TK-02-77C8D9',
    routeCode: '02',
    routeName: 'Bến xe Mỹ Đình — Bến xe Nước Ngầm',
    origin: 'Bến xe Mỹ Đình',
    destination: 'Bến xe Nước Ngầm',
    departureTime: mockDeparture(0, 9),
    seatNumber: 'C11',
    price: 10_000,
    newRouteCode: null,
    newRouteName: null,
    newDepartureTime: null,
    newSeatNumber: null,
    fee: 10_000,
    refundAmount: 0,
    reason: null,
    status: 'Rejected',
    createdAt: mockCreatedAt(0),
    updatedAt: mockCreatedAt(0),
    reviewNote: 'Chuyến khởi hành dưới 2 giờ nên không đủ điều kiện hủy.',
  },
  {
    id: 'req-change-approved',
    type: 'Change',
    ticketCode: 'TK-01-11A2B3',
    routeCode: '01',
    routeName: 'Bến xe Mỹ Đình — Bến xe Gia Lâm',
    origin: 'Bến xe Mỹ Đình',
    destination: 'Bến xe Gia Lâm',
    departureTime: mockDeparture(-1, 8),
    seatNumber: 'A05',
    price: 12_000,
    newRouteCode: '08',
    newRouteName: 'Cầu Giấy — Bờ Hồ Hoàn Kiếm',
    newDepartureTime: mockDeparture(3, 18),
    newSeatNumber: 'D02',
    fee: 2_000,
    refundAmount: null,
    reason: 'Điểm đến mới gần chỗ làm hơn.',
    status: 'Approved',
    createdAt: mockCreatedAt(4),
    updatedAt: mockCreatedAt(3),
    reviewNote: 'Chênh lệch đã thanh toán thành công.',
  },
  {
    id: 'req-cancel-pending',
    type: 'Cancel',
    ticketCode: 'TK-B10-3E4F5A',
    routeCode: 'B10',
    routeName: 'Bến xe Yên Nghĩa — Bến xe Mỹ Đình',
    origin: 'Bến xe Yên Nghĩa',
    destination: 'Bến xe Mỹ Đình',
    departureTime: mockDeparture(1, 6),
    seatNumber: 'D02',
    price: 15_000,
    newRouteCode: null,
    newRouteName: null,
    newDepartureTime: null,
    newSeatNumber: null,
    fee: 3_000,
    refundAmount: 12_000,
    reason: 'Không có nhu cầu đi nữa.',
    status: 'Pending',
    createdAt: mockCreatedAt(0),
    updatedAt: null,
    reviewNote: null,
  },
  {
    id: 'req-change-rejected',
    type: 'Change',
    ticketCode: 'TK-08-9B0C1D',
    routeCode: '08',
    routeName: 'Cầu Giấy — Bờ Hồ Hoàn Kiếm',
    origin: 'Cầu Giấy',
    destination: 'Bờ Hồ Hoàn Kiếm',
    departureTime: mockDeparture(0, 18),
    seatNumber: 'A01',
    price: 9_000,
    newRouteCode: '02',
    newRouteName: 'Bến xe Mỹ Đình — Bến xe Nước Ngầm',
    newDepartureTime: mockDeparture(1, 20),
    newSeatNumber: 'B07',
    fee: 1_000,
    refundAmount: null,
    reason: 'Muốn đổi sang chuyến tối muộn hơn.',
    status: 'Rejected',
    createdAt: mockCreatedAt(0),
    updatedAt: mockCreatedAt(0),
    reviewNote: 'Chuyến mới đã hết ghế trống, mời chọn chuyến khác.',
  },
];

const mock: TicketRequestApi = {
  async listMine(status) {
    await delay(400);

    const list = status
      ? MOCK_TICKET_REQUESTS.filter((item) => item.status === status)
      : MOCK_TICKET_REQUESTS;

    // Trả bản sao để màn hình không vô tình sửa mảng gốc, sắp mới nhất trước như contract.
    return [...list]
      .sort((a, b) => new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime())
      .map((item) => ({ ...item }));
  },
};

const ticketRequestApi: TicketRequestApi = USE_MOCK_DATA ? mock : api;

export default ticketRequestApi;
