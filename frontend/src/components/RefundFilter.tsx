import type { ReactNode } from 'react';
import { Button, Select, Space } from 'antd';
import { ClearOutlined } from '@ant-design/icons';
import {
  REFUND_SOURCE_OPTIONS,
  REFUND_STATUS_OPTIONS,
} from '../api/refundAdminApi';
import type { RefundFilterValue, RefundSource, RefundStatus } from '../api/refundAdminApi';

interface RefundFilterProps {
  /** Giá trị lọc hiện tại — CHA giữ state, component này không tự giữ. */
  value: RefundFilterValue;

  /** Gọi mỗi khi một ô đổi. Cha quyết định gọi API và có quay về trang 1 hay không. */
  onChange: (next: RefundFilterValue) => void;

  /** Khoá cả bộ lọc trong lúc cha đang tải, tránh người dùng đổi tiếp khi chưa có kết quả. */
  disabled?: boolean;

  /** Chỗ cắm thêm nút của trang vào cùng hàng — ví dụ nút "Xuất danh sách". */
  extra?: ReactNode;
}

/**
 * Bộ lọc danh sách hoàn tiền: trạng thái + nguồn gốc.
 *
 * Component thuần "trình bày": KHÔNG gọi API, KHÔNG giữ state nghiệp vụ, chỉ phát giá trị
 * lọc qua `onChange` (đúng khuôn `AuditLogFilter.tsx` / `RouteStopBoard.tsx`). Nhờ vậy màn
 * hình quản lý hoàn tiền giữ state, tự gọi `GET /admin/refunds`, tự ghép với phân trang mà
 * không phải luồn gì ngược xuống đây.
 *
 * `value` có hình dạng khớp thẳng tham số query của `GET /admin/refunds`, nên trang chỉ
 * việc truyền nguyên object làm `params` cho `refundAdminApi.list`.
 */
export default function RefundFilter({ value, onChange, disabled, extra }: RefundFilterProps) {
  const hasFilter = value.status !== undefined || value.source !== undefined;

  return (
    <Space wrap size="middle">
      <Select<RefundStatus>
        allowClear
        placeholder="Tất cả trạng thái"
        style={{ width: 200 }}
        value={value.status}
        disabled={disabled}
        options={REFUND_STATUS_OPTIONS}
        onChange={(status) => onChange({ ...value, status: status ?? undefined })}
      />

      <Select<RefundSource>
        allowClear
        placeholder="Tất cả nguồn gốc"
        style={{ width: 200 }}
        value={value.source}
        disabled={disabled}
        options={REFUND_SOURCE_OPTIONS}
        onChange={(source) => onChange({ ...value, source: source ?? undefined })}
      />

      <Button
        icon={<ClearOutlined />}
        disabled={disabled || !hasFilter}
        onClick={() => onChange({})}
      >
        Xoá lọc
      </Button>

      {extra}
    </Space>
  );
}
