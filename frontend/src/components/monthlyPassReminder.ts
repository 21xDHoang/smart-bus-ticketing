import dayjs from 'dayjs';
import type { Dayjs } from 'dayjs';
import type { MonthlyPass } from '../api/monthlyPassApi';
import { getMonthlyPassStatus } from './monthlyPassStatus';

/** Một vé tháng đang ở trạng thái "sắp hết hạn" — kèm số ngày còn lại. */
export interface ExpiringSoonPass {
  pass: MonthlyPass;
  /** Số ngày còn lại, tính theo ngày trọn vẹn. 0 = hết hạn hôm nay. */
  daysLeft: number;
}

/**
 * Lọc các vé tháng "sắp hết hạn" từ danh sách vé của một hành khách.
 *
 * Dùng chung `getMonthlyPassStatus` nên định nghĩa "sắp hết hạn" (ngưỡng
 * `EXPIRING_SOON_DAYS`) nằm ở đúng MỘT chỗ — tag trạng thái và reminder không bao giờ nói
 * hai chuyện khác nhau. Kết quả xếp theo số ngày còn lại tăng dần: vé gần hết nhất lên đầu.
 *
 * `now` chỉ để kiểm chứng (bơm mốc thời gian cố định); chỗ gọi thật không truyền.
 */
export function getExpiringSoonPasses(
  passes: MonthlyPass[],
  now: Dayjs = dayjs(),
): ExpiringSoonPass[] {
  return passes
    .map((pass) => ({ pass, ...getMonthlyPassStatus(pass, now) }))
    .filter((item) => item.status === 'ExpiringSoon')
    .sort((a, b) => a.daysLeft - b.daysLeft)
    .map(({ pass, daysLeft }) => ({ pass, daysLeft }));
}
