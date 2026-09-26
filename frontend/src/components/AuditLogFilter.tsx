import type { ReactNode } from 'react';
import { Button, DatePicker, Select, Space, Typography } from 'antd';
import { ClearOutlined } from '@ant-design/icons';
import { AUDIT_ACTION_META } from '../api/auditLogApi';
import type { AuditAction } from '../api/auditLogApi';
import AuditLogUserSelect from './AuditLogUserSelect';
import {
  AUDIT_LOG_DATE_FORMAT,
  AUDIT_LOG_RANGE_PRESETS,
  defaultAuditLogFilter,
  filterToRange,
  isDefaultAuditLogFilter,
} from './auditLogFilterValue';
import type { AuditLogFilterValue } from './auditLogFilterValue';

const { RangePicker } = DatePicker;
const { Text } = Typography;

/**
 * Sáu loại hành động dựng thẳng từ `AUDIT_ACTION_META` thay vì chép lại nhãn.
 *
 * Nhờ vậy nhãn và màu của bộ lọc, của cột "Hành động" trong bảng và của modal chi tiết
 * vẫn chỉ có MỘT nguồn. Backend thêm mã hành động thứ bảy thì ô lọc tự có, không phải
 * sửa hai chỗ rồi quên một.
 */
const ACTION_OPTIONS = (Object.keys(AUDIT_ACTION_META) as AuditAction[]).map((code) => ({
  value: code,
  label: AUDIT_ACTION_META[code].label,
}));

interface AuditLogFilterProps {
  /** Giá trị lọc hiện tại — CHA giữ state, component này không tự giữ. */
  value: AuditLogFilterValue;

  /** Gọi mỗi khi một ô đổi. Cha quyết định gọi API và có quay về trang 1 hay không. */
  onChange: (next: AuditLogFilterValue) => void;

  /** Khoá cả bộ lọc trong lúc cha đang tải, tránh người dùng đổi tiếp khi chưa có kết quả. */
  disabled?: boolean;

  /** Chỗ cắm thêm nút của trang vào cùng hàng — ví dụ nút "Xuất Excel". */
  extra?: ReactNode;
}

/**
 * Bộ lọc nhật ký kiểm toán: khoảng thời gian + loại hành động + người thao tác.
 *
 * Component thuần "trình bày": nó KHÔNG gọi API và KHÔNG giữ state nghiệp vụ, chỉ phát ra
 * giá trị lọc qua `onChange` (đúng khuôn `RouteStopBoard.tsx`). Nhờ vậy màn hình danh sách
 * — task của Dương Thị Hạnh — giữ state, tự gọi `GET /audit-logs`, tự ghép với phân trang,
 * mà không phải luồn gì ngược xuống đây.
 *
 * `value` có hình dạng khớp thẳng tham số query của `GET /audit-logs`, nên trang chỉ việc
 * truyền nguyên object làm `params`.
 *
 * Về MÚI GIỜ: hợp đồng chốt `from`/`to` là ngày UTC trọn ngày, và ghi rõ đánh đổi là chọn
 * theo giờ Việt Nam sẽ lệch tối đa 7 giờ ở hai đầu mút. Giữ UTC để bộ lọc và file Excel
 * xuất ra không bao giờ nói hai chuyện khác nhau — tính chất quan trọng nhất với một
 * chứng từ kiểm toán. Nên ở đây KHÔNG quy đổi múi giờ, chỉ ghi nhãn cho người dùng biết.
 */
export default function AuditLogFilter({
  value,
  onChange,
  disabled,
  extra,
}: AuditLogFilterProps) {
  return (
    <div style={{ marginBottom: 16 }}>
      <Space wrap size="middle">
        <RangePicker
          // Ép định dạng để `dateStrings` chắc chắn là `yyyy-MM-dd` — đúng thứ hợp đồng
          // nhận — và không phụ thuộc vào locale của antd.
          format={AUDIT_LOG_DATE_FORMAT}
          presets={AUDIT_LOG_RANGE_PRESETS}
          value={filterToRange(value)}
          disabled={disabled}
          // TẮT nút xoá nhanh của antd (`allowClear` mặc định là BẬT). Để nó bật thì xoá
          // khoảng sẽ cho ra ô ngày TRỐNG trong khi backend vẫn trả 30 ngày gần nhất —
          // giao diện nói sai về đúng cái truy vấn đang chạy. Nút "Xoá lọc" là lối duy nhất
          // để đặt lại, và nó đặt lại về khoảng 30 ngày TƯỜNG MINH.
          allowClear={false}
          onChange={(_dates, dateStrings) => {
            // Lấy ngày từ `dateStrings` chứ không tự `dayjs(...).format()`: đây là chuỗi
            // người dùng NHÌN THẤY trên lịch, nên không có đường nào lệch mất một ngày.
            const [from, to] = dateStrings;
            // Chốt chặn: `allowClear` đã tắt nên antd không gọi với chuỗi rỗng, nhưng nếu
            // có thì giữ nguyên khoảng cũ — thà không đổi còn hơn phát ra khoảng rỗng.
            if (!from || !to) return;
            onChange({ ...value, from, to });
          }}
        />

        <Select<AuditAction>
          allowClear
          placeholder="Loại hành động"
          style={{ width: 200 }}
          value={value.action}
          disabled={disabled}
          options={ACTION_OPTIONS}
          onChange={(action) => onChange({ ...value, action: action ?? undefined })}
        />

        <AuditLogUserSelect
          value={value.userId}
          disabled={disabled}
          onChange={(userId) => onChange({ ...value, userId })}
        />

        <Button
          icon={<ClearOutlined />}
          disabled={disabled || isDefaultAuditLogFilter(value)}
          onClick={() => onChange(defaultAuditLogFilter())}
        >
          Xoá lọc
        </Button>

        {extra}
      </Space>

      <div style={{ marginTop: 8 }}>
        <Text type="secondary" style={{ fontSize: 12 }}>
          Ngày lọc tính theo giờ UTC và tính trọn ngày — cùng khung giờ với cột “Thời gian”
          trong file Excel xuất ra. Bản ghi không xác định được người thao tác thì phải bỏ
          trống ô “Người thao tác” mới thấy.
        </Text>
      </div>
    </div>
  );
}
