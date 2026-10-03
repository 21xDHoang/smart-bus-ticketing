import { Tag } from 'antd';
import { CheckCircleFilled, ClockCircleOutlined, CloseCircleFilled, SyncOutlined } from '@ant-design/icons';
import type { ReactNode } from 'react';
import type { FeedbackStatus } from '../api/feedbackApi';
import { getFeedbackStatusMeta } from '../api/feedbackApi';

/** Icon cho từng trạng thái xử lý — cùng thứ tự với FEEDBACK_STATUS_META. */
const STATUS_ICONS: Record<FeedbackStatus, ReactNode> = {
  Pending: <ClockCircleOutlined />,
  Processing: <SyncOutlined />,
  Resolved: <CheckCircleFilled />,
  Rejected: <CloseCircleFilled />,
};

interface FeedbackStatusTagProps {
  /** Trạng thái xử lý của phản ánh. */
  status: FeedbackStatus;
}

/**
 * Tag trạng thái xử lý của một phản ánh — task "Màn hình danh sách phản ánh của tôi
 * + trạng thái xử lý" (story 24, Nguyễn Đình Băng).
 *
 * Thành phần dùng lại: màn hình "phản ánh của tôi" (Băng) và sau này màn hình Admin
 * xử lý phản ánh (Thịnh) đều nhúng tag này thay vì tự đoán nhãn/màu. Khi cần đổi nhãn
 * hay màu thì chỉ sửa trong feedbackApi.ts, không phải tìm khắp các màn hình.
 */
export default function FeedbackStatusTag({ status }: FeedbackStatusTagProps) {
  const meta = getFeedbackStatusMeta(status);

  return (
    <Tag icon={STATUS_ICONS[status]} color={meta.color}>
      {meta.label}
    </Tag>
  );
}
