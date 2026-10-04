import type { ReactNode } from 'react';
import { Flex } from 'antd';

/**
 * Hàng lọc chuẩn của các màn quản trị: bộ lọc bên trái, nút hành động bên phải — cùng một
 * kiểu xếp và khoảng cách, thay cho các biến thể `Space wrap` lệch nhau ở ~10 màn.
 */
export default function FilterBar({
  children,
  actions,
}: {
  /** Bộ lọc: ô tìm kiếm, ô chọn… */
  children: ReactNode;
  /** Nút hành động: thêm mới, làm mới… */
  actions?: ReactNode;
}) {
  return (
    <Flex justify="space-between" align="center" wrap gap={12}>
      <Flex align="center" wrap gap={12}>
        {children}
      </Flex>
      {actions && (
        <Flex align="center" wrap gap={12}>
          {actions}
        </Flex>
      )}
    </Flex>
  );
}
