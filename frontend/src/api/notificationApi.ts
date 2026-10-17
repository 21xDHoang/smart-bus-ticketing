import axiosClient from './axiosClient';

// -----------------------------------------------------------------------------
// API "Thông báo trong ứng dụng" — phục vụ task "Hiển thị danh sách thông báo trong ứng
// dụng (chuông + badge)" (US 10 "Thông báo trạm", task của Nguyễn Đình Băng).
//
// Endpoint thật đã có: GET /notifications/me — task "API lịch sử thông báo đã nhận của
// hành khách" (dòng 19 Sprint 5, Vàng Thị Dăm). Trả MẢNG TRẦN, mới nhất trước, không
// phân trang; hợp đồng đầy đủ ở docs/api-contract.md mục "Thông báo trong ứng dụng —
// /notifications".
//
// Kênh ĐẨY vẫn là NotificationHub ở /hubs/notifications (SignalR): sự kiện
// NotificationReceived(notificationId) chỉ đánh thức màn hình, nội dung luôn lấy lại từ
// endpoint này — hub không phải nguồn dữ liệu thứ hai.
// -----------------------------------------------------------------------------

/**
 * Loại thông báo — SÁU giá trị của `NotificationType`, lưu chuỗi trong CSDL
 * (backend/SmartBus.Api/Entities/NotificationType.cs). Backend trả chuỗi chứ không trả số
 * (dự án không bật `JsonStringEnumConverter`), nên union này khớp thẳng JSON.
 */
export type NotificationType =
  | 'CancellationApproved'
  | 'CancellationRejected'
  | 'TicketChangeApproved'
  | 'TicketChangeRejected'
  | 'RefundSucceeded'
  | 'RefundFailed';

/**
 * Bảng mà `relatedEntityType` trỏ tới — BA giá trị của `NotificationTarget`. Hai loại
 * `Refund…` trỏ vào `Refund`; `Refund` tương ứng màn "Hoàn tiền của tôi" (/my-refunds)
 * khi màn danh sách nối điều hướng theo target.
 */
export type NotificationTarget = 'CancellationRequest' | 'TicketChange' | 'Refund';

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

  /** Thời điểm sinh (ISO 8601, UTC) — cột sắp xếp của danh sách. */
  createdAt: string;
}

export interface NotificationApi {
  /** Danh sách thông báo của chính người gọi, mới nhất trước. Không có → mảng rỗng. */
  listMine: () => Promise<AppNotification[]>;
}

const notificationApi: NotificationApi = {
  // Mảng trần, không phân trang — đúng hợp đồng "GET /notifications/me". Phạm vi "của tôi"
  // nằm trong truy vấn phía server (theo UserId trong token), không có tham số userId.
  listMine: () => axiosClient.get<AppNotification[], AppNotification[]>('/notifications/me'),
};

export default notificationApi;
