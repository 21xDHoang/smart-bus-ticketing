import type { TagProps } from 'antd';

// -----------------------------------------------------------------------------
// API gửi yêu cầu huỷ / đổi vé — nối với story 5 "Hủy & đổi vé" (Sprint 4), màn hình
// "Gửi yêu cầu huỷ / đổi vé" (task của Dương Thị Hạnh).
//
// Backend CHƯA có endpoint nào cho yêu cầu huỷ/đổi vé — docs/api-contract.md mới chỉ có
// GET /tickets/me và GET /tickets/{id} (tra cứu vé). Vì vậy file này chạy dữ liệu giả
// (USE_MOCK_DATA = true) — cùng lối màn "Vé của tôi" (ticketApi.ts) và màn cấu hình sơ đồ
// ghế (seatLayoutApi.ts) đã làm khi backend chưa sẵn sàng. Khi backend + contract có, chỉ
// cần viết thật nhánh `api` rồi lật cờ.
//
// Hình dạng endpoint DỰ KIẾN (để Hiếu chốt vào api-contract.md — CHƯA cam kết):
//   POST /ticket-change-requests → hành khách gửi yêu cầu huỷ hoặc đổi một vé → 201
//   (status khởi tạo `Pending`; body { ticketId, type, reason }).
// -----------------------------------------------------------------------------

/** Loại yêu cầu — hành khách muốn huỷ vé (hoàn tiền) hay đổi sang chuyến khác. */
export type TicketChangeType = 'Cancel' | 'Change';

/** Trạng thái xử lý — vòng đời từ lúc gửi tới khi nhà xe duyệt hoặc từ chối. */
export type TicketChangeStatus = 'Pending' | 'Approved' | 'Rejected';

/** Body khi gửi một yêu cầu huỷ/đổi vé — POST /ticket-change-requests (dự kiến). */
export interface SubmitTicketChangePayload {
  /** Vé muốn huỷ/đổi — khoá vé trong danh sách "Vé của tôi". */
  ticketId: string;

  type: TicketChangeType;

  /** Lý do — text tự do, bắt buộc. */
  reason: string;
}

/** Yêu cầu vừa gửi, trả về bởi POST /ticket-change-requests — chỉ cần đủ để hiện màn thành công. */
export interface SubmittedTicketChange {
  id: string;

  ticketId: string;

  type: TicketChangeType;

  reason: string;

  /** Luôn là `Pending` ngay sau khi gửi — nhà xe duyệt sau. */
  status: TicketChangeStatus;

  createdAt: string;
}

interface TicketChangeMeta {
  label: string;
  /** Tên màu preset của Tag AntD. */
  color: TagProps['color'];
}

/** Nhãn + màu cho hai loại yêu cầu — cùng lối FEEDBACK_TYPE_META của màn "Gửi phản ánh". */
export const TICKET_CHANGE_TYPE_META: Record<TicketChangeType, TicketChangeMeta> = {
  Cancel: { label: 'Huỷ vé', color: 'red' },
  Change: { label: 'Đổi vé', color: 'geekblue' },
};

/** Danh sách loại yêu cầu cho ô chọn, theo đúng thứ tự khai báo ở TICKET_CHANGE_TYPE_META. */
export const TICKET_CHANGE_TYPE_OPTIONS: { value: TicketChangeType; label: string }[] = (
  Object.keys(TICKET_CHANGE_TYPE_META) as TicketChangeType[]
).map((value) => ({ value, label: TICKET_CHANGE_TYPE_META[value].label }));

/** Nhãn + màu trạng thái — chỉ cần `Pending` để vẽ thẻ "Chờ duyệt" trên màn hình thành công. */
export const TICKET_CHANGE_STATUS_META: Record<TicketChangeStatus, TicketChangeMeta> = {
  Pending: { label: 'Chờ duyệt', color: 'processing' },
  Approved: { label: 'Đã duyệt', color: 'success' },
  Rejected: { label: 'Từ chối', color: 'error' },
};

/** Lấy nhãn + màu cho một mã loại yêu cầu; mã lạ vẫn hiển thị được thay vì crash. */
export function getTicketChangeTypeMeta(type: string): TicketChangeMeta {
  return TICKET_CHANGE_TYPE_META[type as TicketChangeType] ?? { label: type, color: 'default' };
}

export interface TicketChangeRequestApi {
  /** Gửi một yêu cầu huỷ/đổi vé mới. */
  submit: (payload: SubmitTicketChangePayload) => Promise<SubmittedTicketChange>;
}

// ---------------------------------------------------------------------------
// Cờ chuyển giữa dữ liệu giả và API thật. Đang để `true` — backend yêu cầu huỷ/đổi chưa có.
const USE_MOCK_DATA = true;

const delay = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms));

// -------- Gọi API thật (dùng khi USE_MOCK_DATA = false) --------
// Backend chưa có endpoint yêu cầu huỷ/đổi vé nên nhánh này tạm chưa viết: nối axiosClient vào
// endpoint dự kiến ở đầu file khi api-contract.md đã chốt.
const api: TicketChangeRequestApi = {
  async submit() {
    throw new Error('Backend yêu cầu huỷ/đổi vé chưa có — chưa thể gửi yêu cầu thật.');
  },
};

// -------- Dữ liệu giả (dùng khi USE_MOCK_DATA = true) --------
const mock: TicketChangeRequestApi = {
  async submit(payload) {
    await delay(600);

    // Giả lập lỗi "không tìm thấy vé" như backend sẽ trả khi ticketId không có thật.
    // Màn hình luôn gửi id lấy từ ticketApi.get nên nhánh này gần như không chạy — chỉ để
    // giữ hợp đồng lỗi rõ ràng khi sau này nối API thật.
    if (!payload.ticketId) {
      throw new Error('Không tìm thấy vé đã chọn.');
    }

    return {
      id: `tcr-${Date.now()}-${Math.random().toString(36).slice(2, 8)}`,
      ticketId: payload.ticketId,
      type: payload.type,
      reason: payload.reason,
      status: 'Pending',
      createdAt: new Date().toISOString(),
    };
  },
};

const ticketChangeRequestApi: TicketChangeRequestApi = USE_MOCK_DATA ? mock : api;

export default ticketChangeRequestApi;
