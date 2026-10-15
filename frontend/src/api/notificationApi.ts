// -----------------------------------------------------------------------------
// API "Thông báo trong ứng dụng" — phục vụ task "Hiển thị danh sách thông báo trong ứng
// dụng (chuông + badge)" (US 10 "Thông báo trạm", task của Nguyễn Đình Băng).
//
// Backend CHƯA có endpoint REST lấy danh sách thông báo (task "API lịch sử thông báo đã
// nhận của hành khách" — Vàng Thị Dăm, chưa làm) và NotificationHub chưa được nối vào
// Program.cs (Phùng Duy Hoàng — docs/33 §3.4). Nên file này chạy dữ liệu giả
// (USE_MOCK_DATA = true) — cùng lối ticketRequestApi.ts. Hình dạng dòng thông báo khớp
// entity Notification (docs/33-thong-bao-ket-qua-huy-doi-ve.md §2).
//
// Hình dạng endpoint DỰ KIẾN (để chủ API chốt vào api-contract.md — CHƯA cam kết):
//   GET /notifications/me → danh sách thông báo của chính người gọi, mới nhất trước.
// -----------------------------------------------------------------------------

/** Loại thông báo — bốn giá trị đầu của NotificationType (docs/33 §6.3), lưu chuỗi trong CSDL. */
export type NotificationType =
  | 'CancellationApproved'
  | 'CancellationRejected'
  | 'TicketChangeApproved'
  | 'TicketChangeRejected';

/** Bảng mà `relatedEntityId` trỏ tới — cột RelatedEntityType (docs/33 §2). */
export type NotificationTarget = 'CancellationRequest' | 'TicketChange';

/** Một thông báo trong ứng dụng — khớp entity Notification (A9 #15). */
export interface AppNotification {
  id: string;

  /** Loại thông báo — quyết định biểu tượng ở màn danh sách. */
  type: NotificationType;

  /** Tiêu đề ngắn — dòng đầu của mỗi mục trong danh sách. */
  title: string;

  /** Nội dung đầy đủ — hiển thị được xuống dòng, độ dài bất kỳ (docs/33 §8). */
  body: string;

  /** Bảng mà `relatedEntityId` trỏ tới; null với thông báo không gắn bản ghi nào. */
  relatedEntityType: NotificationTarget | null;

  /** Id bản ghi mà thông báo nói về; null cùng lý do trên. */
  relatedEntityId: string | null;

  /** Thời điểm sinh (ISO 8601) — cột sắp xếp của danh sách. */
  createdAt: string;
}

export interface NotificationApi {
  /** Danh sách thông báo của chính người gọi, mới nhất trước. Không có → mảng rỗng. */
  listMine: () => Promise<AppNotification[]>;
}

// ---------------------------------------------------------------------------
// Cờ chuyển giữa dữ liệu giả và API thật. Đang để `true` — endpoint REST chưa có.
const USE_MOCK_DATA = true;

const delay = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms));

// -------- Gọi API thật (dùng khi USE_MOCK_DATA = false) --------
// Backend chưa có GET /notifications/me nên nhánh này tạm chưa viết: nối axiosClient vào
// endpoint dự kiến ở đầu file khi api-contract.md đã chốt.
const api: NotificationApi = {
  async listMine() {
    throw new Error('Backend thông báo chưa có — chưa thể tải danh sách thông báo thật.');
  },
};

// -------- Dữ liệu giả (dùng khi USE_MOCK_DATA = true) --------
/** Mốc thời gian giả — `hoursAgo` là số giờ trước "bây giờ". */
function mockCreatedAt(hoursAgo: number): string {
  const date = new Date();
  date.setHours(date.getHours() - hoursAgo);
  return date.toISOString();
}

/**
 * Kho thông báo giả — phủ đủ 4 loại (duyệt/từ chối × huỷ/đổi) với tiêu đề và nội dung đúng
 * bốn câu ở docs/33 §6.3; hai dòng "bị từ chối" có lý do xuống dòng để kiểm việc vẽ Body
 * nhiều dòng. Trộn thứ tự cố tình không theo giờ — màn danh sách sắp mới nhất trước.
 */
const MOCK_NOTIFICATIONS: AppNotification[] = [
  {
    id: 'ntf-cancel-approved',
    type: 'CancellationApproved',
    title: 'Đã duyệt yêu cầu huỷ vé',
    body: 'Yêu cầu huỷ vé của bạn đã được duyệt. Nhà xe sẽ xử lý hoàn tiền theo quy định.',
    relatedEntityType: 'CancellationRequest',
    relatedEntityId: 'req-cancel-approved',
    createdAt: mockCreatedAt(2),
  },
  {
    id: 'ntf-change-approved',
    type: 'TicketChangeApproved',
    title: 'Đã duyệt yêu cầu đổi chuyến',
    body: 'Yêu cầu đổi chuyến của bạn đã được duyệt. Vé mới của bạn đã được phát hành.',
    relatedEntityType: 'TicketChange',
    relatedEntityId: 'req-change-approved',
    createdAt: mockCreatedAt(5),
  },
  {
    id: 'ntf-cancel-rejected',
    type: 'CancellationRejected',
    title: 'Từ chối yêu cầu huỷ vé',
    body: 'Yêu cầu huỷ vé của bạn đã bị từ chối.\nLý do: Chuyến khởi hành dưới 2 giờ nên không đủ điều kiện huỷ.',
    relatedEntityType: 'CancellationRequest',
    relatedEntityId: 'req-cancel-rejected',
    createdAt: mockCreatedAt(26),
  },
  {
    id: 'ntf-change-rejected',
    type: 'TicketChangeRejected',
    title: 'Từ chối yêu cầu đổi chuyến',
    body: 'Yêu cầu đổi chuyến của bạn đã bị từ chối.\nLý do: Chuyến mới đã hết ghế trống, mời chọn chuyến khác.',
    relatedEntityType: 'TicketChange',
    relatedEntityId: 'req-change-rejected',
    createdAt: mockCreatedAt(49),
  },
];

const mock: NotificationApi = {
  async listMine() {
    await delay(400);

    // Trả bản sao để màn hình không vô tình sửa mảng gốc, sắp mới nhất trước như contract.
    return [...MOCK_NOTIFICATIONS]
      .sort((a, b) => new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime())
      .map((item) => ({ ...item }));
  },
};

const notificationApi: NotificationApi = USE_MOCK_DATA ? mock : api;

export default notificationApi;
