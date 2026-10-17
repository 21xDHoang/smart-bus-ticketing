import { Button, DatePicker, Select, Space } from 'antd';
import { ClearOutlined } from '@ant-design/icons';
import dayjs, { type Dayjs } from 'dayjs';
import {
  ROUTE_OPTIONS,
  TIME_SLOT_OPTIONS,
  defaultFillRateFilter,
} from '../api/fillRateReportApi';
import type { FillRateFilterValue, TimeSlot } from '../api/fillRateReportApi';

const { RangePicker } = DatePicker;

interface FillRateFilterProps {
  /** Giá trị lọc hiện tại — CHA giữ state, component này không tự giữ. */
  value: FillRateFilterValue;

  /** Gọi mỗi khi một ô đổi. Cha quyết định gọi API. */
  onChange: (next: FillRateFilterValue) => void;

  /** Khoá cả bộ lọc trong lúc cha đang tải. */
  disabled?: boolean;
}

/** Đổi `{ from, to }` (yyyy-MM-dd) sang cặp Dayjs cho RangePicker; thiếu đầu mút thì trả null. */
function toRange(value: FillRateFilterValue): [Dayjs, Dayjs] | null {
  const from = dayjs(value.from);
  const to = dayjs(value.to);
  if (!from.isValid() || !to.isValid()) return null;
  return [from, to];
}

/** Các mốc chọn nhanh — `value` là hàm để "Hôm nay" luôn tính đúng lúc mở lịch. */
const RANGE_PRESETS: { label: string; value: () => [Dayjs, Dayjs] }[] = [
  { label: 'Hôm nay', value: () => [dayjs(), dayjs()] },
  { label: '7 ngày gần nhất', value: () => [dayjs().subtract(6, 'day'), dayjs()] },
  { label: '30 ngày gần nhất', value: () => [dayjs().subtract(29, 'day'), dayjs()] },
];

/**
 * Bộ lọc tỷ lệ lấp đầy: tuyến + khung giờ + khoảng ngày.
 *
 * Component thuần "trình bày": KHÔNG gọi API, KHÔNG giữ state nghiệp vụ, chỉ phát giá trị
 * lọc qua `onChange` (đúng khuôn `RefundFilter.tsx` / `AuditLogFilter.tsx`). Nhờ vậy màn
 * hình tỷ lệ lấp đầy giữ state, tự gọi `getReport`, tự ghép với dải thẻ + bảng.
 */
export default function FillRateFilter({ value, onChange, disabled }: FillRateFilterProps) {
  const hasFilter = value.routeId !== undefined || value.timeSlot !== undefined;

  return (
    <Space wrap size="middle">
      <Select
        allowClear
        placeholder="Tất cả tuyến"
        style={{ width: 280 }}
        value={value.routeId}
        disabled={disabled}
        options={ROUTE_OPTIONS}
        onChange={(routeId) => onChange({ ...value, routeId: routeId ?? undefined })}
      />

      <Select<TimeSlot>
        allowClear
        placeholder="Tất cả khung giờ"
        style={{ width: 200 }}
        value={value.timeSlot}
        disabled={disabled}
        options={TIME_SLOT_OPTIONS}
        onChange={(timeSlot) => onChange({ ...value, timeSlot: timeSlot ?? undefined })}
      />

      <RangePicker
        value={toRange(value)}
        format="DD/MM/YYYY"
        allowClear={false}
        presets={RANGE_PRESETS}
        disabled={disabled}
        onChange={(range) => {
          if (!range || !range[0] || !range[1]) return;
          onChange({
            ...value,
            from: range[0].format('YYYY-MM-DD'),
            to: range[1].format('YYYY-MM-DD'),
          });
        }}
      />

      <Button
        icon={<ClearOutlined />}
        disabled={disabled || !hasFilter}
        onClick={() => onChange(defaultFillRateFilter())}
      >
        Xoá lọc
      </Button>
    </Space>
  );
}
