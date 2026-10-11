import type { ReactNode } from 'react';
import { Breadcrumb, Button, Flex, Typography } from 'antd';
import { ArrowLeftOutlined } from '@ant-design/icons';
import { Link, useLocation } from 'react-router-dom';
import { navTrail } from './navItems';

const { Title, Text } = Typography;

/**
 * Đầu trang chuẩn cho mọi màn: breadcrumb (tự suy từ đường dẫn) + tiêu đề + mô tả phụ,
 * tuỳ chọn nút quay lại và khu vực hành động bên phải. Thay cho khối `Title level={4}` +
 * `Text type="secondary"` mà nhiều màn đang tự chép lại (mỗi màn một kiểu lề).
 */
export default function PageHeader({
  title,
  subtitle,
  extra,
  onBack,
  breadcrumb = true,
}: {
  title: string;
  subtitle?: ReactNode;
  /** Hành động bên phải tiêu đề (nút, ô chọn, bộ lọc…). */
  extra?: ReactNode;
  /** Có thì hiện nút mũi tên quay lại bên trái tiêu đề. */
  onBack?: () => void;
  /** Đường dẫn có "cha" (nhóm menu hoặc màn trước trong luồng) thì hiện breadcrumb; tắt bằng false. */
  breadcrumb?: boolean;
}) {
  const { pathname } = useLocation();
  const trail = breadcrumb ? navTrail(pathname) : [];

  return (
    <div>
      {trail.length > 0 && (
        <Breadcrumb
          style={{ marginBottom: 4 }}
          items={trail.map((crumb) => ({
            title: crumb.to ? <Link to={crumb.to}>{crumb.label}</Link> : crumb.label,
          }))}
        />
      )}

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
    </div>
  );
}
