import dayjs from 'dayjs';
import { formatDateTime } from './ui/format';

// -----------------------------------------------------------------------------
// Tiện ích hiển thị cho thông báo — dùng chung cho màn danh sách và màn chi tiết.
// -----------------------------------------------------------------------------

/**
 * Loại thông báo "tích cực" (được duyệt / hoàn tiền thành công) — quyết định biểu tượng xanh
 * hay đỏ. Nhận `string` chứ không phải union `NotificationType` để khi backend bổ sung loại
 * mới (ví dụ `RefundSucceeded` / `RefundFailed` đã có trong api-contract) màn hình vẫn vẽ được.
 */
export function isPositiveNotification(type: string): boolean {
  return (
    type === 'CancellationApproved' ||
    type === 'TicketChangeApproved' ||
    type === 'RefundSucceeded'
  );
}

/**
 * Giờ tương đối gọn cho một mục thông báo — "Vừa xong" / "2 giờ trước" / "3 ngày trước".
 * Giữ khớp với bản đang viết trong NotificationBell.tsx (file của Băng) — khi gom lại thì đưa
 * về đây, không chép thêm bản thứ ba.
 */
export function formatRelativeTime(iso: string): string {
  const diffMinutes = dayjs().diff(dayjs(iso), 'minute');
  if (diffMinutes < 1) return 'Vừa xong';
  if (diffMinutes < 60) return `${diffMinutes} phút trước`;

  const diffHours = Math.floor(diffMinutes / 60);
  if (diffHours < 24) return `${diffHours} giờ trước`;

  const diffDays = Math.floor(diffHours / 24);
  if (diffDays < 30) return `${diffDays} ngày trước`;

  return formatDateTime(iso);
}
