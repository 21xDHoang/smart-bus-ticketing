import { CheckCircleFilled, ClockCircleOutlined, CloseCircleFilled } from '@ant-design/icons';
import type { ReactNode } from 'react';
import StatusTag from './ui/StatusTag';
import type { RefundStatus } from '../api/refundAdminApi';
import { getRefundStatusMeta } from '../api/refundAdminApi';

/** Icon cho từng trạng thái hoàn tiền — cùng thứ tự với REFUND_STATUS_META. */
const STATUS_ICONS: Record<RefundStatus, ReactNode> = {
  Pending: <ClockCircleOutlined />,
  Succeeded: <CheckCircleFilled />,
  Failed: <CloseCircleFilled />,
};

interface RefundStatusTagProps {
  /** Trạng thái xử lý của một khoản hoàn tiền. */
  status: RefundStatus;
}

/**
 * Tag trạng thái xử lý của một khoản hoàn tiền — task "Màn hình Admin quản lý yêu cầu
 * hoàn tiền" (story 8, Nguyễn Đình Băng).
 *
 * Thành phần dùng lại: cột "Trạng thái" của bảng và khối trạng thái trong modal chi tiết
 * đều nhúng tag này thay vì tự đoán nhãn/màu. Đổi nhãn hay màu thì chỉ sửa trong
 * refundAdminApi.ts, không phải tìm khắp các màn hình.
 */
export default function RefundStatusTag({ status }: RefundStatusTagProps) {
  const meta = getRefundStatusMeta(status);

  return (
    <StatusTag icon={STATUS_ICONS[status]} color={meta.color}>
      {meta.label}
    </StatusTag>
  );
}
