import type { ReactNode } from 'react';
import { Card } from 'antd';

/**
 * Khung thẻ chuẩn của mọi màn — bo góc 16 + bóng đổ rất nhẹ (đúng chuỗi style mà ~21 chỗ
 * đang tự chép tay). Độ bo lấy từ token `borderRadiusLG` trong components/ui/theme.ts, bóng
 * đổ là mặc định của Card dạng borderless.
 *
 * `toolbar` là hàng nằm trên nội dung — chỗ đặt bộ lọc + nút hành động (thường là FilterBar).
 */
export default function PageCard({
  title,
  extra,
  toolbar,
  children,
}: {
  title?: ReactNode;
  extra?: ReactNode;
  toolbar?: ReactNode;
  children: ReactNode;
}) {
  return (
    <Card variant="borderless" title={title} extra={extra}>
      {toolbar && <div style={{ marginBottom: 16 }}>{toolbar}</div>}
      {children}
    </Card>
  );
}
