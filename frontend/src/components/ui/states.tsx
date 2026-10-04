import type { ReactNode } from 'react';
import { Button, Empty, Result, Spin, Typography } from 'antd';
import { ReloadOutlined } from '@ant-design/icons';

const { Text } = Typography;

// Ba trạng thái hiển thị dùng chung cho mọi màn có gọi API. Ba trạng thái là BA chuyện
// khác nhau, không gộp: "đang tải" ≠ "gọi hỏng" ≠ "không có dữ liệu". Gộp lỗi mạng vào
// "không có dữ liệu" là màn hình nói sai sự thật với người dùng.

/** Đang tải dữ liệu — spinner giữa khối, kèm một câu mô tả. */
export function LoadingState({ description = 'Đang tải dữ liệu…' }: { description?: string }) {
  return (
    <div style={{ padding: '56px 0', textAlign: 'center' }}>
      <Spin size="large" />
      <div style={{ marginTop: 12 }}>
        <Text type="secondary">{description}</Text>
      </div>
    </div>
  );
}

/** Gọi API hỏng — trạng thái riêng, có lối thoát (nút Thử lại), không mạo nhận là rỗng. */
export function ErrorState({
  title,
  description,
  onRetry,
}: {
  title: string;
  description?: string;
  onRetry?: () => void;
}) {
  return (
    <Result
      status="warning"
      title={title}
      subTitle={description}
      extra={
        onRetry && (
          <Button icon={<ReloadOutlined />} onClick={onRetry}>
            Thử lại
          </Button>
        )
      }
    />
  );
}

/** Tải xong nhưng không có dữ liệu — mảng rỗng là câu trả lời hợp lệ, không phải lỗi. */
export function EmptyState({
  description,
  action,
}: {
  description: ReactNode;
  /** Nút gợi ý hành động tiếp theo (ví dụ "Đăng ký vé tháng"). */
  action?: ReactNode;
}) {
  return (
    <Empty style={{ padding: '24px 0' }} description={description}>
      {action}
    </Empty>
  );
}
