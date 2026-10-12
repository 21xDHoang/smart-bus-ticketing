import dayjs from 'dayjs';
import type { TicketRequestStatus, TicketRequestType } from './ticketRequestApi';

// -----------------------------------------------------------------------------
// API GỬI yêu cầu huỷ / đổi vé — màn hình "Gửi yêu cầu huỷ / đổi vé" (US 5 "Hủy & Đổi vé",
// Sprint 4, task của Dương Thị Hạnh).
//
// Đây là nửa GỬI của story, nửa ĐỌC đã có ở ticketRequestApi.ts (màn "Hủy/đổi vé của tôi",
// task của Nguyễn Đình Băng). Hai bảng đã chốt trong CSDL là `CancellationRequests` (huỷ) và
// `TicketChanges` (đổi chuyến) — hai nghiệp vụ TÁCH BIỆT:
//   • Huỷ vé  → `POST /cancellation-requests`  { ticketId, reason }
//   • Đổi chuyến → `POST /ticket-changes`      { ticketId, newTripId, newSeatId, reason }
// (hình dạng endpoint dự kiến, xem docs/32-csdl-huy-doi-ve.md — chờ Hiếu/Kiên chốt vào
// api-contract.md rồi mới nối axiosClient).
//
// Backend chưa có hai endpoint đó nên file này chạy dữ liệu giả (USE_MOCK_DATA = true), cùng
// lối ticketRequestApi.ts đã làm. Khi backend + contract có, chỉ cần viết thật nhánh `api` rồi
// lật cờ. Mọi type/trạng thái/meta dùng CHUNG với ticketRequestApi.ts — không khai báo lại
// một bản thứ hai để khỏi lệch nhãn/màu với màn theo dõi.
// -----------------------------------------------------------------------------

/** Body khi gửi một yêu cầu huỷ/đổi vé — hai hình dạng tuỳ loại, khớp hai bảng trong CSDL. */
export type SubmitTicketChangePayload =
  | { type: 'Cancel'; ticketId: string; reason: string }
  | {
      type: 'Change';
      ticketId: string;
      /** Chuyến mới muốn đổi sang — `TicketChanges.NewTripId`. */
      newTripId: string;
      /** Ghế muốn ngồi ở chuyến mới — `TicketChanges.NewSeatId`. */
      newSeatId: string;
      reason: string;
    };

/** Yêu cầu vừa gửi, trả về bởi POST /cancellation-requests hoặc /ticket-changes. */
export interface SubmittedTicketChange {
  id: string;

  type: TicketRequestType;

  /** Luôn là `Pending` ngay sau khi gửi — nhà xe duyệt sau. */
  status: TicketRequestStatus;

  createdAt: string;
}

/** Một chuyến mới trong ô chọn "đổi sang chuyến" — dữ liệu giả cho bước chọn của luồng đổi. */
export interface TicketChangeNewTripOption {
  /** Khoá chuyến — đóng vai `TicketChanges.NewTripId`. */
  id: string;

  routeCode: string;

  routeName: string;

  /** Giờ khởi hành (ISO 8601). */
  departureTime: string;

  /** Giá vé chuyến mới (VND) — để tính chênh lệch với vé cũ. */
  price: number;

  /** Khoá ghế — đóng vai `TicketChanges.NewSeatId`. */
  seatId: string;

  /** Số ghế hiển thị — "A03". */
  seatNumber: string;
}

/**
 * Phí huỷ (VND) theo quy định thời gian — bản GIẢ của service tính phí của Kiên
 * (docs/32-csdl-huy-doi-ve.md §6): trước ≥24h miễn phí, 12–24h phí 30%, dưới 12h phí 50%.
 * Màn hình dùng hàm này để hiện phí/hoàn TRƯỚC khi gửi; khi nối API thật thì thay bằng giá trị
 * backend trả về, không tự tính lại.
 */
export function mockCancellationFee(departureTime: string, price: number): number {
  const hoursUntil = dayjs(departureTime).diff(dayjs(), 'hour', true);

  if (hoursUntil >= 24) return 0;
  if (hoursUntil >= 12) return Math.round(price * 0.3);
  return Math.round(price * 0.5);
}

/** Mốc giờ khởi hành giả cho các chuyến mới — `offsetDays` dương là tương lai. */
function mockDeparture(offsetDays: number, hourOfDay: number): string {
  const date = new Date();
  date.setDate(date.getDate() + offsetDays);
  date.setHours(hourOfDay, 30, 0, 0);
  return date.toISOString();
}

/**
 * Kho chuyến mới giả cho luồng "đổi chuyến" — đứng tạm chỗ cho component "chọn chuyến mới"
 * của Thịnh (dòng 9 Sprint 4). Khi component đó có, thay ô chọn ở màn hình bằng component,
 * không sửa hợp đồng submit (vẫn gửi `newTripId` + `newSeatId`).
 */
export const MOCK_NEW_TRIPS: TicketChangeNewTripOption[] = [
  {
    id: 'newtrip-01-late',
    routeCode: '01',
    routeName: 'Bến xe Mỹ Đình — Bến xe Gia Lâm',
    departureTime: mockDeparture(2, 9),
    price: 12_000,
    seatId: 'seat-new-01-A03',
    seatNumber: 'A03',
  },
  {
    id: 'newtrip-08-morning',
    routeCode: '08',
    routeName: 'Cầu Giấy — Bờ Hồ Hoàn Kiếm',
    departureTime: mockDeparture(2, 7),
    price: 9_000,
    seatId: 'seat-new-08-B01',
    seatNumber: 'B01',
  },
  {
    id: 'newtrip-02-noon',
    routeCode: '02',
    routeName: 'Bến xe Mỹ Đình — Bến xe Nước Ngầm',
    departureTime: mockDeparture(2, 12),
    price: 10_000,
    seatId: 'seat-new-02-C05',
    seatNumber: 'C05',
  },
];

export interface TicketChangeRequestApi {
  /** Gửi một yêu cầu huỷ hoặc đổi chuyến mới. */
  submit: (payload: SubmitTicketChangePayload) => Promise<SubmittedTicketChange>;
}

// ---------------------------------------------------------------------------
// Cờ chuyển giữa dữ liệu giả và API thật. Đang để `true` — backend huỷ/đổi chưa có.
const USE_MOCK_DATA = true;

const delay = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms));

// -------- Gọi API thật (dùng khi USE_MOCK_DATA = false) --------
// Backend chưa có POST /cancellation-requests và POST /ticket-changes nên nhánh này tạm chưa
// viết: nối axiosClient vào hai endpoint dự kiến ở đầu file khi api-contract.md đã chốt.
const api: TicketChangeRequestApi = {
  async submit() {
    throw new Error('Backend huỷ/đổi vé chưa có — chưa thể gửi yêu cầu thật.');
  },
};

// -------- Dữ liệu giả (dùng khi USE_MOCK_DATA = true) --------
const mock: TicketChangeRequestApi = {
  async submit(payload) {
    await delay(600);

    // Giả lập lỗi "không tìm thấy vé" như backend sẽ trả khi ticketId không có thật. Màn hình
    // luôn gửi id lấy từ ticketApi.get nên nhánh này gần như không chạy — chỉ để giữ hợp đồng
    // lỗi rõ ràng khi sau này nối API thật.
    if (!payload.ticketId) {
      throw new Error('Không tìm thấy vé đã chọn.');
    }

    return {
      id: `tcr-${Date.now()}-${Math.random().toString(36).slice(2, 8)}`,
      type: payload.type,
      status: 'Pending',
      createdAt: new Date().toISOString(),
    };
  },
};

const ticketChangeRequestApi: TicketChangeRequestApi = USE_MOCK_DATA ? mock : api;

export default ticketChangeRequestApi;
