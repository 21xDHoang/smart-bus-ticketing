import { Tag } from 'antd';
import { CheckCircleFilled, ClockCircleFilled, CloseCircleFilled } from '@ant-design/icons';
import type { ReactNode } from 'react';
import dayjs from 'dayjs';
import type { Dayjs } from 'dayjs';
import type { Ticket } from '../api/ticketApi';
import { getTicketStatus, TICKET_STATUS_META } from './ticketStatus';
import type { TicketDisplayStatus } from './ticketStatus';

/** Icon cho từng trạng thái — cùng thứ tự với TICKET_STATUS_META. */
const STATUS_ICONS: Record<TicketDisplayStatus, ReactNode> = {
  Valid: <CheckCircleFilled />,
  Used: <CheckCircleFilled />,
  Expired: <ClockCircleFilled />,
  Cancelled: <CloseCircleFilled />,
};

/**
 * Câu chú thích (tooltip) giải thích chính xác vì sao vé ở trạng thái này — chỗ nhạy cảm
 * nhất với hành khách: vé hết hạn / đã dùng thì phải nói rõ để khỏi thắc mắc "vé tôi sao rồi".
 */
function buildTooltip(
  status: TicketDisplayStatus,
  departureTime: string,
  usedAt: string | null,
): string {
  const departure = dayjs(departureTime).format('HH:mm DD/MM/YYYY');

  switch (status) {
    case 'Used':
      return usedAt ? `Đã soát vé lúc ${dayjs(usedAt).format('HH:mm DD/MM/YYYY')}` : 'Vé đã được soát vé';
    case 'Expired':
      return `Chuyến đã khởi hành lúc ${departure} — vé không còn dùng được`;
    case 'Cancelled':
      return 'Vé đã bị huỷ';
    default:
      return `Lên xe trước ${departure}`;
  }
}

interface TicketStatusTagProps {
  /** Vé cần hiển thị trạng thái. Chỉ cần `status` + `departureTime` là đủ suy ra bốn trạng thái. */
  ticket: Pick<Ticket, 'status' | 'departureTime' | 'usedAt'>;
  /** Mốc "bây giờ" — chỉ để kiểm chứng; bỏ trống thì dùng thời điểm hiện tại. */
  now?: Dayjs;
}

/**
 * Tag trạng thái vé điện tử — task "Xử lý vé hết hạn / vé đã sử dụng trên UI" (story 4).
 *
 * Thành phần dùng lại: màn hình "Vé của tôi" nhúng tag này thay vì tự đoán nhãn/màu — cùng lối
 * MonthlyPassStatusTag của vé tháng. Khi cần đổi nhãn hay luật hết hạn thì chỉ sửa trong
 * ticketStatus.ts, không phải tìm khắp các màn hình.
 */
export default function TicketStatusTag({ ticket, now }: TicketStatusTagProps) {
  const status = getTicketStatus(ticket, now);
  const meta = TICKET_STATUS_META[status];

  return (
    <Tag
      icon={STATUS_ICONS[status]}
      color={meta.color}
      title={buildTooltip(status, ticket.departureTime, ticket.usedAt)}
    >
      {meta.label}
    </Tag>
  );
}
