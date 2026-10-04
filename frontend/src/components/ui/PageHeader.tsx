import type { ReactNode } from 'react';
import { Button, Flex, Typography } from 'antd';
import { ArrowLeftOutlined } from '@ant-design/icons';

const { Title, Text } = Typography;

/**
 * Đầu trang chuẩn cho mọi màn: tiêu đề + mô tả phụ, tuỳ chọn nút quay lại và khu vực hành
 * động bên phải. Thay cho khối `Title level={4}` + `Text type="secondary"` mà ~17 màn đang
 * tự chép lại (mỗi màn một kiểu lề).
 */
export default function PageHeader({
  title,
  subtitle,
  extra,
  onBack,
}: {
  title: string;
  subtitle?: ReactNode;
  /** Hành động bên phải tiêu đề (nút, ô chọn, bộ lọc…). */
  extra?: ReactNode;
  /** Có thì hiện nút mũi tên quay lại bên trái tiêu đề. */
  onBack?: () => void;
}) {
  return (
    <Flex justify="space-between" align="center" wrap gap={12} style={{ marginBottom: 16 }}>
      <div>
        <Flex align="center" gap={8}>
          {onBack && (
            <Button
              type="text"
              size="small"
              icon={<ArrowLeftOutlined />}
              onClick={onBack}
              aria-label="Quay lại"
            />
          )}
          <Title level={4} style={{ margin: 0 }}>
            {title}
          </Title>
        </Flex>
        {subtitle && <Text type="secondary">{subtitle}</Text>}
      </div>
      {extra && <div>{extra}</div>}
    </Flex>
  );
}
