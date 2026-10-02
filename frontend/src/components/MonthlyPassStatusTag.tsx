import { Tag } from 'antd';
import { CheckCircleFilled, ClockCircleFilled, CloseCircleFilled } from '@ant-design/icons';
import type { ReactNode } from 'react';
import dayjs from 'dayjs';
import type { Dayjs } from 'dayjs';
import type { MonthlyPass } from '../api/monthlyPassApi';
import { getMonthlyPassStatus, MONTHLY_PASS_STATUS_META } from './monthlyPassStatus';
import type { MonthlyPassDisplayStatus } from './monthlyPassStatus';

/** Icon cho từng trạng thái — cùng thứ tự với MONTHLY_PASS_STATUS_META. */
const STATUS_ICONS: Record<MonthlyPassDisplayStatus, ReactNode> = {
  Valid: <CheckCircleFilled />,
  ExpiringSoon: <ClockCircleFilled />,
  Expired: <CloseCircleFilled />,
};

/**
 * Câu chú thích (tooltip) giải thích chính xác ngày hết hiệu lực, kèm số ngày còn lại cho
 * trạng thái "sắp hết hạn" — chỗ nhạy cảm nhất với hành khách.
 */
function buildTooltip(status: MonthlyPassDisplayStatus, daysLeft: number, validTo: string): string {
  const date = dayjs(validTo).format('DD/MM/YYYY');

  switch (status) {
    case 'Expired':
      return `Đã hết hạn từ ${date}`;
    case 'ExpiringSoon':
      return daysLeft <= 0 ? `Hết hạn hôm nay (${date})` : `Còn ${daysLeft} ngày — hết hạn ${date}`;
    default:
      return `Còn hạn đến ${date}`;
  }
}

interface MonthlyPassStatusTagProps {
  /** Vé tháng cần hiển thị trạng thái. Chỉ cần hai trường là đủ suy ra cả ba trạng thái. */
  pass: Pick<MonthlyPass, 'status' | 'validTo'>;
  /** Mốc "hôm nay" — chỉ để kiểm chứng; bỏ trống thì dùng ngày hiện tại. */
  now?: Dayjs;
}

/**
 * Tag trạng thái vé tháng — task "Component hiển thị trạng thái vé tháng
 * (còn hạn / sắp hết hạn / hết hạn)" (story 16, Nguyễn Đình Băng).
 *
 * Thành phần dùng lại: màn hình "quản lý vé tháng của tôi" (Thịnh) và "đăng ký vé tháng"
 * (Hạnh) đều nhúng tag này thay vì tự đoán nhãn/màu. Khi cần đổi nhãn hay ngưỡng "sắp hết
 * hạn" thì chỉ sửa trong monthlyPassStatus.ts, không phải tìm khắp các màn hình.
 */
export default function MonthlyPassStatusTag({ pass, now }: MonthlyPassStatusTagProps) {
  const { status, daysLeft } = getMonthlyPassStatus(pass, now);
  const meta = MONTHLY_PASS_STATUS_META[status];

  return (
    <Tag icon={STATUS_ICONS[status]} color={meta.color} title={buildTooltip(status, daysLeft, pass.validTo)}>
      {meta.label}
    </Tag>
  );
}
