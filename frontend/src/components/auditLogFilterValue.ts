import dayjs from 'dayjs';
import type { Dayjs } from 'dayjs';
import type { AuditAction } from '../api/auditLogApi';

/**
 * Giá trị của bộ lọc nhật ký kiểm toán — hợp đồng giữa component bộ lọc và màn hình
 * danh sách (task của Dương Thị Hạnh) nhúng nó vào.
 *
 * Hình dạng này khớp THẲNG tham số query của `GET /audit-logs`
 * (docs/api-contract.md, mục "Nhật ký kiểm toán") nên màn hình chỉ việc truyền nguyên
 * object làm `params`, không phải đổi tên trường. Mỗi lần đổi tên là một lần thêm chỗ
 * để hai người hiểu sai nhau.
 */
export interface AuditLogFilterValue {
  /** Ngày bắt đầu, `yyyy-MM-dd`, tính TRỌN NGÀY theo UTC. */
  from?: string;

  /** Ngày kết thúc, `yyyy-MM-dd`, tính TRỌN NGÀY theo UTC. */
  to?: string;

  /** Một trong 6 mã hành động. Bỏ trống = lấy mọi loại hành động. */
  action?: AuditAction;

  /**
   * GUID người thao tác. Bỏ trống = lấy mọi người.
   *
   * Lưu ý: bản ghi có `userId` là null (đăng nhập thất bại với SĐT không tồn tại, hoặc
   * hành động do hệ thống tự làm) KHÔNG lọc được theo trường này — muốn thấy chúng thì
   * phải để trống bộ lọc người dùng. Đây là giới hạn của hợp đồng, không phải lỗi.
   */
  userId?: string;
}

/** Định dạng ngày của hợp đồng API — cả `from`/`to` lẫn cột thời gian trong file Excel. */
export const AUDIT_LOG_DATE_FORMAT = 'YYYY-MM-DD';

/**
 * Số ngày của khoảng mặc định.
 *
 * Backend cũng mặc định 30 ngày gần nhất khi bỏ trống cả `from` lẫn `to`. Ta khởi tạo
 * tường minh thay vì gửi rỗng: nếu để trống, ô chọn ngày hiện trống trong khi bảng vẫn
 * hiện 30 ngày dữ liệu — giao diện nói SAI về truy vấn đang chạy.
 */
export const AUDIT_LOG_DEFAULT_DAYS = 30;

/**
 * Khoảng mặc định: 30 ngày gần nhất tính cả hôm nay.
 *
 * `from` lùi 29 ngày chứ không phải 30 để khoảng là 30 ngày TRỌN VẸN gồm cả hai đầu mút
 * — đúng cách backend tính "29 ngày trước hôm nay".
 *
 * Tham số `now` chỉ để kiểm chứng (bơm mốc thời gian cố định); chỗ gọi thật không truyền.
 */
export function defaultAuditLogFilter(now: Dayjs = dayjs()): AuditLogFilterValue {
  return {
    from: now.subtract(AUDIT_LOG_DEFAULT_DAYS - 1, 'day').format(AUDIT_LOG_DATE_FORMAT),
    to: now.format(AUDIT_LOG_DATE_FORMAT),
  };
}

/**
 * Đang ở đúng khoảng mặc định và chưa lọc gì thêm hay chưa.
 *
 * Màn hình dùng để quyết định có hiện nút "Xoá lọc" hay không — hiện một nút bấm vào
 * không đổi gì là cách chắc chắn nhất để người dùng mất tin vào bộ lọc.
 */
export function isDefaultAuditLogFilter(
  value: AuditLogFilterValue,
  now: Dayjs = dayjs(),
): boolean {
  const fallback = defaultAuditLogFilter(now);

  return (
    value.from === fallback.from && value.to === fallback.to && !value.action && !value.userId
  );
}

/** Một mốc chọn nhanh trên lịch. */
export interface AuditLogRangePreset {
  label: string;
  /**
   * Cặp ngày, trả về từ HÀM chứ không phải giá trị dựng sẵn.
   *
   * `presets` của antd nhận `value: DateType | (() => DateType)`. Nếu viết thẳng
   * `[dayjs().subtract(6, 'day'), dayjs()]` thì mốc được tính MỘT LẦN lúc nạp module —
   * để app mở qua nửa đêm là mốc "Hôm nay" trỏ sai ngày. Dạng hàm được gọi lúc mở lịch.
   */
  value: () => [Dayjs, Dayjs];
}

/** Các mốc chọn nhanh — câu hỏi kiểm toán hay gặp nhất là "tuần đó có ai xoá gì không?". */
export const AUDIT_LOG_RANGE_PRESETS: AuditLogRangePreset[] = [
  { label: 'Hôm nay', value: () => [dayjs(), dayjs()] },
  { label: '7 ngày gần nhất', value: () => [dayjs().subtract(6, 'day'), dayjs()] },
  { label: '30 ngày gần nhất', value: () => [dayjs().subtract(29, 'day'), dayjs()] },
  { label: '90 ngày gần nhất', value: () => [dayjs().subtract(89, 'day'), dayjs()] },
];

/**
 * Đổi giá trị bộ lọc sang cặp ngày cho `RangePicker`.
 *
 * Trả `null` khi thiếu một trong hai đầu mút — đó cũng là giá trị mà antd hiểu là "ô
 * chọn ngày đang trống".
 *
 * KHÔNG quy đổi múi giờ, và đó là chủ ý: hợp đồng chốt `from`/`to` là ngày UTC trọn ngày
 * để bộ lọc và file Excel xuất ra không bao giờ nói hai chuyện khác nhau. Quy đổi ngầm
 * sang giờ Việt Nam sẽ làm giao diện lệch với chứng từ kiểm toán.
 */
export function filterToRange(value: AuditLogFilterValue): [Dayjs, Dayjs] | null {
  if (!value.from || !value.to) return null;

  // Parse thẳng, KHÔNG truyền chuỗi định dạng: `dayjs(s, format)` chỉ có tác dụng khi đã
  // `extend(customParseFormat)`, mà extend là thay đổi có tác dụng phụ lên instance dayjs
  // dùng chung toàn app. `yyyy-MM-dd` vốn là ISO nên dayjs hiểu đúng mà không cần plugin.
  const from = dayjs(value.from);
  const to = dayjs(value.to);

  if (!from.isValid() || !to.isValid()) return null;

  return [from, to];
}
