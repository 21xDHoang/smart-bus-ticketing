import type { Dayjs } from 'dayjs';

/**
 * Múi giờ Việt Nam cố định (UTC+7).
 *
 * Hợp đồng docs/api-contract.md bắt buộc chuỗi thời gian gửi lên server phải kèm offset.
 * Cố định thay vì lấy theo trình duyệt: người dùng nhìn thấy đúng giờ mình chọn, không lệch
 * khi mở máy ở múi giờ khác. Cùng lối TripScheduleFormModal và TripFrequencyPage.
 */
export const VIETNAM_OFFSET = '+07:00';

/**
 * Ghép nửa "ngày" và nửa "giờ" thành một mốc khởi hành, cắt về độ chính xác phút.
 *
 * Cắt giây/mili giây vì chuyến xe chỉ lên lịch theo phút. Để nguyên giây của ô giờ sẽ sinh ra
 * mốc lệch kiểu 05:00:37, làm câu so trùng khung giờ phía backend trượt một cách vô cớ.
 */
export function mergeDateAndTime(date: Dayjs, time: Dayjs): Dayjs {
  return date.hour(time.hour()).minute(time.minute()).second(0).millisecond(0);
}

/** Tách một mốc khởi hành thành hai nửa để đưa vào DatePicker và TimePicker. */
export function splitDeparture(value: Dayjs | null | undefined): {
  date: Dayjs | null;
  time: Dayjs | null;
} {
  if (!value) return { date: null, time: null };
  return { date: value.startOf('day'), time: value };
}

/**
 * Giữ nguyên tham chiếu cũ khi hai mốc bằng nhau.
 *
 * Dùng trong setState của component chọn ngày/giờ: cha lỡ tạo Dayjs mới mỗi lần render thì
 * setState vẫn bail out thay vì đẩy component vào vòng lặp render vô tận.
 */
export function keepIfSame(next: Dayjs | null, prev: Dayjs | null): Dayjs | null {
  if (next && prev && next.isSame(prev)) return prev;
  return next;
}

/** Dayjs -> ISO 8601 kèm offset Việt Nam — dạng backend nhận. */
export function toVietnamIso(value: Dayjs): string {
  return `${value.format('YYYY-MM-DDTHH:mm:ss')}${VIETNAM_OFFSET}`;
}
