import type { TicketRequest, TicketRequestStatus, TicketRequestType } from './ticketRequestApi';

// -----------------------------------------------------------------------------
// API HÀNG ĐỢI DUYỆT yêu cầu huỷ / đổi vé — phục vụ màn hình "Duyệt yêu cầu huỷ - đổi vé"
// (story 5 "Hủy & Đổi vé", Sprint 4, task của Dương Thị Hạnh).
//
// Đây là nửa Admin của story, nửa HÀNH KHÁCH đã có ở ticketRequestApi.ts (màn "Hủy/đổi vé
// của tôi" của Băng) và nửa GỬI ở ticketChangeRequestApi.ts (màn "Gửi yêu cầu" của Hạnh).
// Hai bảng đã chốt trong CSDL là `CancellationRequests` (huỷ) và `TicketChanges` (đổi chuyến) —
// hai nghiệp vụ TÁCH BIỆT, nhưng màn duyệt là MỘT màn cho cả hai loại (docs/32-csdl-huy-doi-ve.md
// §3.4), nên file này gộp hai hàng đợi về một danh sách có cột `type` phân biệt.
//
// Hình dạng endpoint DỰ KIẾN (chờ Hiếu/Hoàng chốt vào api-contract.md — CHƯA cam kết):
//   GET   /cancellation-requests?status=…      → hàng đợi yêu cầu huỷ  (task duyệt của Hoàng)
//   GET   /ticket-changes?status=…             → hàng đợi yêu cầu đổi   (task duyệt của Hoàng)
//   PATCH /cancellation-requests/{id}          → { status: 'Approved'|'Rejected', rejectReason? }
//   PATCH /ticket-changes/{id}                 → { status: 'Approved'|'Rejected', rejectReason? }
//
// Backend chưa có bốn endpoint đó nên file này chạy dữ liệu giả (USE_MOCK_DATA = true), cùng
// lối ticketRequestApi.ts / ticketChangeRequestApi.ts đã làm. Khi backend + contract có, chỉ
// cần viết thật nhánh `api` rồi lật cờ. Mọi type/trạng thái/meta dùng CHUNG với ticketRequestApi.ts.
// -----------------------------------------------------------------------------

/** Một yêu cầu hủy / đổi vé trong hàng đợi duyệt của Admin — thêm tên người gửi vào TicketRequest. */
export interface AdminTicketRequest extends TicketRequest {
  /** Tên hành khách gửi yêu cầu — màn duyệt cần biết yêu cầu của ai để xử lý. */
  passengerName: string;
}

export interface TicketRequestAdminApi {
  /** Hàng đợi yêu cầu cho màn duyệt. `status` bỏ trống = tất cả, mới nhất trước. */
  list: (status?: TicketRequestStatus) => Promise<AdminTicketRequest[]>;

  /** Duyệt một yêu cầu. `type` chọn đúng endpoint PATCH khi nối API thật (huỷ vs đổi). */
  approve: (id: string, type: TicketRequestType) => Promise<void>;

  /** Từ chối một yêu cầu kèm lý do — lý do này chính là `RejectReason` gửi về khách. */
  reject: (id: string, type: TicketRequestType, reason: string) => Promise<void>;
}

// ---------------------------------------------------------------------------
// Cờ chuyển giữa dữ liệu giả và API thật. Đang để `true` — backend huỷ/đổi chưa có.
const USE_MOCK_DATA = true;

const delay = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms));

// -------- Gọi API thật (dùng khi USE_MOCK_DATA = false) --------
// Backend chưa có GET/PATCH cho cancellation-requests và ticket-changes nên nhánh này tạm chưa
// viết: nối axiosClient vào bốn endpoint dự kiến ở đầu file khi api-contract.md đã chốt.
const api: TicketRequestAdminApi = {
  async list() {
    throw new Error('Backend huỷ/đổi vé chưa có — chưa thể tải hàng đợi duyệt thật.');
  },
  async approve() {
    throw new Error('Backend huỷ/đổi vé chưa có — chưa thể duyệt yêu cầu thật.');
  },
  async reject() {
    throw new Error('Backend huỷ/đổi vé chưa có — chưa thể từ chối yêu cầu thật.');
  },
};

// -------- Dữ liệu giả (dùng khi USE_MOCK_DATA = true) --------
/** Mốc giờ khởi hành giả — `offsetDays` âm là quá khứ, dương là tương lai. */
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

/**
 * Kho yêu cầu giả cho hàng đợi duyệt. Là mảng CÓ THỂ SỬA (không đông cứng như ticketRequestApi.ts)
 * để thao tác duyệt/từ chối lật được trạng thái ngay trong phiên, mô phỏng hàng đợi thật: duyệt
 * xong thì yêu cầu rời khỏi nhóm "Chờ duyệt" khi màn hình gọi lại `list`.
 *
 * Phủ đủ 2 loại (Hủy/Đổi) và 3 trạng thái; đa số Pending để demo thao tác duyệt.
 */
const MOCK_REQUESTS: AdminTicketRequest[] = [
  {
    id: 'admin-cancel-pending-1',
    type: 'Cancel',
    passengerName: 'Nguyễn Văn An',
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
    id: 'admin-change-pending-1',
    type: 'Change',
    passengerName: 'Trần Thị Bình',
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
    id: 'admin-cancel-pending-2',
    type: 'Cancel',
    passengerName: 'Lê Văn Cường',
    ticketCode: 'TK-02-77C8D9',
    routeCode: '02',
    routeName: 'Bến xe Mỹ Đình — Bến xe Nước Ngầm',
    origin: 'Bến xe Mỹ Đình',
    destination: 'Bến xe Nước Ngầm',
    departureTime: mockDeparture(2, 17),
    seatNumber: 'C11',
    price: 10_000,
    newRouteCode: null,
    newRouteName: null,
    newDepartureTime: null,
    newSeatNumber: null,
    fee: 3_000,
    refundAmount: 7_000,
    reason: 'Đổi kế hoạch, không đi nữa.',
    status: 'Pending',
    createdAt: mockCreatedAt(0),
    updatedAt: null,
    reviewNote: null,
  },
  {
    id: 'admin-change-pending-2',
    type: 'Change',
    passengerName: 'Phạm Thị Dung',
    ticketCode: 'TK-08-9B0C1D',
    routeCode: '08',
    routeName: 'Cầu Giấy — Bờ Hồ Hoàn Kiếm',
    origin: 'Cầu Giấy',
    destination: 'Bờ Hồ Hoàn Kiếm',
    departureTime: mockDeparture(1, 18),
    seatNumber: 'A01',
    price: 9_000,
    newRouteCode: '02',
    newRouteName: 'Bến xe Mỹ Đình — Bến xe Nước Ngầm',
    newDepartureTime: mockDeparture(2, 20),
    newSeatNumber: 'B07',
    fee: 1_000,
    refundAmount: null,
    reason: 'Muốn đổi sang chuyến tối muộn hơn.',
    status: 'Pending',
    createdAt: mockCreatedAt(0),
    updatedAt: null,
    reviewNote: null,
  },
  {
    id: 'admin-cancel-pending-3',
    type: 'Cancel',
    passengerName: 'Hoàng Văn Em',
    ticketCode: 'TK-08-D4E5F6',
    routeCode: '08',
    routeName: 'Cầu Giấy — Bờ Hồ Hoàn Kiếm',
    origin: 'Cầu Giấy',
    destination: 'Bờ Hồ Hoàn Kiếm',
    departureTime: mockDeparture(0, 9),
    seatNumber: 'B03',
    price: 9_000,
    newRouteCode: null,
    newRouteName: null,
    newDepartureTime: null,
    newSeatNumber: null,
    fee: 9_000,
    refundAmount: 0,
    reason: null,
    status: 'Pending',
    createdAt: mockCreatedAt(0),
    updatedAt: null,
    reviewNote: null,
  },
  {
    id: 'admin-change-approved',
    type: 'Change',
    passengerName: 'Vũ Thị Phương',
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
    createdAt: mockCreatedAt(1),
    updatedAt: mockCreatedAt(1),
    reviewNote: 'Đã duyệt yêu cầu đổi chuyến.',
  },
  {
    id: 'admin-cancel-rejected',
    type: 'Cancel',
    passengerName: 'Đặng Văn Giang',
    ticketCode: 'TK-02-44D5E6',
    routeCode: '02',
    routeName: 'Bến xe Mỹ Đình — Bến xe Nước Ngầm',
    origin: 'Bến xe Mỹ Đình',
    destination: 'Bến xe Nước Ngầm',
    departureTime: mockDeparture(0, 7),
    seatNumber: 'A10',
    price: 10_000,
    newRouteCode: null,
    newRouteName: null,
    newDepartureTime: null,
    newSeatNumber: null,
    fee: 10_000,
    refundAmount: 0,
    reason: 'Không đi nữa.',
    status: 'Rejected',
    createdAt: mockCreatedAt(1),
    updatedAt: mockCreatedAt(1),
    reviewNote: 'Chuyến khởi hành dưới 2 giờ nên không đủ điều kiện hủy.',
  },
];

const mock: TicketRequestAdminApi = {
  async list(status) {
    await delay(400);

    const list = status
      ? MOCK_REQUESTS.filter((item) => item.status === status)
      : MOCK_REQUESTS;

    // Trả bản sao để màn hình không vô tình sửa mảng gốc, sắp mới nhất trước như contract.
    return [...list]
      .sort((a, b) => new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime())
      .map((item) => ({ ...item }));
  },

  async approve(id, type) {
    await delay(500);

    const item = MOCK_REQUESTS.find((entry) => entry.id === id);
    if (!item) throw new Error('Không tìm thấy yêu cầu.');

    // Giả lập nhánh đua "hai quản lý cùng duyệt" mà backend chặn bằng xmin
    // (docs/32-csdl-huy-doi-ve.md §4.3): yêu cầu đã rời khỏi Pending thì lượt duyệt sau thất bại.
    if (item.status !== 'Pending') {
      throw new Error('Yêu cầu đã được người khác xử lý.');
    }

    item.status = 'Approved';
    item.updatedAt = new Date().toISOString();
    // Ghi chú gợi ý cho khách xem ở màn "Hủy/đổi vé của tôi". Không hứa số tiền — việc hoàn
    // tiền do bảng Refunds (US 8) lo sau (docs/33-thong-bao-ket-qua-huy-doi-ve.md §"Không chứa số tiền").
    item.reviewNote = type === 'Cancel' ? 'Đã duyệt yêu cầu huỷ vé.' : 'Đã duyệt yêu cầu đổi chuyến.';
  },

  async reject(id, _type, reason) {
    await delay(500);

    const item = MOCK_REQUESTS.find((entry) => entry.id === id);
    if (!item) throw new Error('Không tìm thấy yêu cầu.');

    if (item.status !== 'Pending') {
      throw new Error('Yêu cầu đã được người khác xử lý.');
    }

    item.status = 'Rejected';
    item.updatedAt = new Date().toISOString();
    item.reviewNote = reason.trim();
  },
};

const ticketRequestAdminApi: TicketRequestAdminApi = USE_MOCK_DATA ? mock : api;

export default ticketRequestAdminApi;
