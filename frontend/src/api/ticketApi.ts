// -----------------------------------------------------------------------------
// API "Vé của tôi" — vé điện tử (story 4 "Vé điện tử QR", Sprint 3), phục vụ task
// "Xử lý vé hết hạn / vé đã sử dụng trên UI" (Dương Thị Hạnh).
//
// Backend CHƯA có bảng Tickets (migration Sprint 3 của Dăm) và chưa có endpoint nào trong
// docs/api-contract.md, nên file này chạy dữ liệu giả (USE_MOCK_DATA = true) — cùng lối màn
// "Gửi phản ánh" (feedbackSubmitApi.ts) và màn cấu hình sơ đồ ghế (seatLayoutApi.ts) đã làm
// khi backend chưa sẵn sàng. Khi backend + contract có, chỉ cần viết thật nhánh `api` rồi lật cờ.
//
// Hình dạng endpoint DỰ KIẾN (để Hiếu chốt vào api-contract.md — CHƯA cam kết):
//   GET /tickets/me → vé điện tử của chính người gọi (đã thanh toán, dùng rồi, huỷ…)
// -----------------------------------------------------------------------------

/** Trạng thái LƯU của vé — do các nghiệp vụ đặt: thanh toán → Paid, soát vé → Used, huỷ → Cancelled. */
export type TicketStatus = 'Paid' | 'Used' | 'Cancelled';

/** Một vé điện tử — khớp hình dạng dự kiến của bảng Tickets (A9 #14, Sprint 3). */
export interface Ticket {
  id: string;

  /** Mã vé = nội dung mã QR soát vé. Duy nhất toàn hệ thống. */
  code: string;

  /** Mã tuyến hiển thị — "01", "B10". */
  routeCode: string;

  routeName: string;

  origin: string;

  destination: string;

  /** Giờ khởi hành của chuyến (ISO 8601). Căn cứ suy "hết hạn": chuyến đi rồi mà chưa soát vé. */
  departureTime: string;

  seatNumber: string;

  /** Tên trạm lên — lưu để hiển thị, không phải khoá nghiệp vụ (quy ước A8.1). */
  boardingStopName: string;

  /** Tên trạm xuống — lưu để hiển thị, không phải khoá nghiệp vụ. */
  alightingStopName: string;

  /** Giá đã thanh toán (VND). */
  price: number;

  status: TicketStatus;

  createdAt: string;

  /** Thời điểm soát vé — chỉ có khi `status = 'Used'`. */
  usedAt: string | null;
}

export interface TicketApi {
  /** Vé điện tử của chính người gọi. Không vé nào → mảng rỗng, không phải 404. */
  list: () => Promise<Ticket[]>;
}

// ---------------------------------------------------------------------------
// Cờ chuyển giữa dữ liệu giả và API thật. Đang để `true` — backend Tickets chưa có.
const USE_MOCK_DATA = true;

const delay = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms));

// -------- Gọi API thật (dùng khi USE_MOCK_DATA = false) --------
// Backend chưa có bảng Tickets nên nhánh này tạm chưa viết: nối axiosClient vào endpoint dự
// kiến ở đầu file khi api-contract.md đã chốt.
const api: TicketApi = {
  async list() {
    throw new Error('Backend Tickets chưa có — chưa thể tải vé điện tử thật.');
  },
};

// -------- Dữ liệu giả (dùng khi USE_MOCK_DATA = true) --------
/** Mốc thời gian giả — `offsetDays` âm là quá khứ, dương là tương lai, `hourOfDay` là giờ khởi hành. */
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

/** Kho vé giả — đủ bốn trạng thái hiển thị để kiểm tra việc "xử lý hết hạn / đã sử dụng". */
const MOCK_TICKETS: Ticket[] = [
  {
    id: 'ticket-valid-1',
    code: 'TK-01-A1B2C3',
    routeCode: '01',
    routeName: 'Bến xe Mỹ Đình — Bến xe Gia Lâm',
    origin: 'Bến xe Mỹ Đình',
    destination: 'Bến xe Gia Lâm',
    departureTime: mockDeparture(1, 8),
    seatNumber: 'A12',
    boardingStopName: 'Bến xe Mỹ Đình',
    alightingStopName: 'Bến xe Gia Lâm',
    price: 12_000,
    status: 'Paid',
    createdAt: mockCreatedAt(2),
    usedAt: null,
  },
  {
    id: 'ticket-valid-2',
    code: 'TK-08-D4E5F6',
    routeCode: '08',
    routeName: 'Cầu Giấy — Bờ Hồ Hoàn Kiếm',
    origin: 'Cầu Giấy',
    destination: 'Bờ Hồ Hoàn Kiếm',
    departureTime: mockDeparture(2, 17),
    seatNumber: 'B03',
    boardingStopName: 'Cầu Giấy',
    alightingStopName: 'Bờ Hồ Hoàn Kiếm',
    price: 9_000,
    status: 'Paid',
    createdAt: mockCreatedAt(1),
    usedAt: null,
  },
  {
    id: 'ticket-used',
    code: 'TK-01-11A2B3',
    routeCode: '01',
    routeName: 'Bến xe Mỹ Đình — Bến xe Gia Lâm',
    origin: 'Bến xe Mỹ Đình',
    destination: 'Bến xe Gia Lâm',
    departureTime: mockDeparture(-1, 8),
    seatNumber: 'A05',
    boardingStopName: 'Bến xe Mỹ Đình',
    alightingStopName: 'Bến xe Gia Lâm',
    price: 12_000,
    status: 'Used',
    createdAt: mockCreatedAt(3),
    usedAt: mockDeparture(-1, 8),
  },
  {
    id: 'ticket-expired-1',
    code: 'TK-02-77C8D9',
    routeCode: '02',
    routeName: 'Bến xe Mỹ Đình — Bến xe Nước Ngầm',
    origin: 'Bến xe Mỹ Đình',
    destination: 'Bến xe Nước Ngầm',
    departureTime: mockDeparture(-2, 9),
    seatNumber: 'C11',
    boardingStopName: 'Bến xe Mỹ Đình',
    alightingStopName: 'Bến xe Nước Ngầm',
    price: 10_000,
    status: 'Paid',
    createdAt: mockCreatedAt(4),
    usedAt: null,
  },
  {
    id: 'ticket-cancelled',
    code: 'TK-B10-3E4F5A',
    routeCode: 'B10',
    routeName: 'Bến xe Yên Nghĩa — Bến xe Mỹ Đình',
    origin: 'Bến xe Yên Nghĩa',
    destination: 'Bến xe Mỹ Đình',
    departureTime: mockDeparture(1, 6),
    seatNumber: 'D02',
    boardingStopName: 'Bến xe Yên Nghĩa',
    alightingStopName: 'Bến xe Mỹ Đình',
    price: 15_000,
    status: 'Cancelled',
    createdAt: mockCreatedAt(2),
    usedAt: null,
  },
  {
    id: 'ticket-expired-2',
    code: 'TK-08-9B0C1D',
    routeCode: '08',
    routeName: 'Cầu Giấy — Bờ Hồ Hoàn Kiếm',
    origin: 'Cầu Giấy',
    destination: 'Bờ Hồ Hoàn Kiếm',
    departureTime: mockDeparture(-3, 18),
    seatNumber: 'A01',
    boardingStopName: 'Cầu Giấy',
    alightingStopName: 'Bờ Hồ Hoàn Kiếm',
    price: 9_000,
    status: 'Paid',
    createdAt: mockCreatedAt(5),
    usedAt: null,
  },
];

const mock: TicketApi = {
  async list() {
    await delay(400);
    return MOCK_TICKETS.map((ticket) => ({ ...ticket }));
  },
};

const ticketApi: TicketApi = USE_MOCK_DATA ? mock : api;

export default ticketApi;
