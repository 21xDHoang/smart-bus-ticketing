import dayjs from 'dayjs';
import type { Dayjs } from 'dayjs';
import type { Ticket } from '../api/ticketApi';

/**
 * Trạng thái HIỂN THỊ của một vé điện tử — task "Xử lý vé hết hạn / vé đã sử dụng trên UI"
 * (story 4, Dương Thị Hạnh).
 *
 * Khác với `TicketStatus` ('Paid' | 'Used' | 'Cancelled') lưu trong cột Status của bảng
 * Tickets: "Hết hạn" KHÔNG phải một giá trị lưu — nó được suy ra từ `departureTime` so với
 * bây giờ. Vé một chuyến không có cột `validTo` như vé tháng: hết hiệu lực = chuyến đã khởi
 * hành mà vé chưa được soát. Bốn trạng thái này chỉ để vẽ giao diện, không đổi dữ liệu.
 */
export type TicketDisplayStatus = 'Valid' | 'Used' | 'Expired' | 'Cancelled';

/** Nhãn + màu Tag AntD cho từng trạng thái hiển thị. */
export const TICKET_STATUS_META: Record<
  TicketDisplayStatus,
  { label: string; color: string }
> = {
  Valid: { label: 'Còn hiệu lực', color: 'success' },
  Used: { label: 'Đã sử dụng', color: 'processing' },
  Expired: { label: 'Hết hạn', color: 'error' },
  Cancelled: { label: 'Đã huỷ', color: 'default' },
};

/**
 * Suy trạng thái hiển thị của vé từ `status` + `departureTime`.
 *
 * Luật (thứ tự quan trọng — trạng thái huỷ/đã dùng thắng việc chuyến đã đi hay chưa):
 * - `status === 'Cancelled'` → `Cancelled`.
 * - `status === 'Used'` → `Used`.
 * - `status === 'Paid'` và `departureTime` đã qua → `Expired` (chuyến đi rồi, vé không dùng
 *   được nữa dù cột Status vẫn còn 'Paid').
 * - Còn lại (`Paid` + chuyến chưa khởi hành) → `Valid`.
 *
 * `now` chỉ để kiểm chứng (bơm mốc thời gian cố định); chỗ gọi thật không truyền.
 */
export function getTicketStatus(
  ticket: Pick<Ticket, 'status' | 'departureTime'>,
  now: Dayjs = dayjs(),
): TicketDisplayStatus {
  if (ticket.status === 'Cancelled') return 'Cancelled';
  if (ticket.status === 'Used') return 'Used';

  // status === 'Paid'
  return dayjs(ticket.departureTime).isBefore(now) ? 'Expired' : 'Valid';
}

/** Vé có còn dùng để lên xe được không — chỉ vé "Còn hiệu lực" mới quét được mã QR. */
export function isTicketUsable(status: TicketDisplayStatus): boolean {
  return status === 'Valid';
}
