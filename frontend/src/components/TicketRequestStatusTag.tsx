import { Tag } from 'antd';
import { CheckCircleFilled, ClockCircleOutlined, CloseCircleFilled } from '@ant-design/icons';
import type { ReactNode } from 'react';
import type { TicketRequestStatus } from '../api/ticketRequestApi';
import { getTicketRequestStatusMeta } from '../api/ticketRequestApi';

/** Icon cho từng trạng thái xử lý — cùng thứ tự với TICKET_REQUEST_STATUS_META. */
const STATUS_ICONS: Record<TicketRequestStatus, ReactNode> = {
  Pending: <ClockCircleOutlined />,
  Approved: <CheckCircleFilled />,
  Rejected: <CloseCircleFilled />,
};

interface TicketRequestStatusTagProps {
  /** Trạng thái xử lý của yêu cầu hủy/đổi vé. */
  status: TicketRequestStatus;
}

/**
 * Tag trạng thái xử lý của một yêu cầu hủy/đổi vé — task "Màn hình theo dõi trạng thái
 * yêu cầu hủy - đổi" (story 5, Nguyễn Đình Băng).
 *
 * Thành phần dùng lại: màn hình "Hủy/đổi vé của tôi" (Băng) và sau này màn hình Admin duyệt
 * yêu cầu (Hạnh) đều nhúng tag này thay vì tự đoán nhãn/màu. Khi cần đổi nhãn hay màu thì
 * chỉ sửa trong ticketRequestApi.ts, không phải tìm khắp các màn hình.
 */
export default function TicketRequestStatusTag({ status }: TicketRequestStatusTagProps) {
  const meta = getTicketRequestStatusMeta(status);

  return (
    <Tag icon={STATUS_ICONS[status]} color={meta.color}>
      {meta.label}
    </Tag>
  );
}
