import dayjs from 'dayjs';
import type { Dayjs } from 'dayjs';
import type { MonthlyPass } from '../api/monthlyPassApi';

/**
 * Trạng thái HIỂN THỊ của một vé tháng — task "Component hiển thị trạng thái vé tháng
 * (còn hạn / sắp hết hạn / hết hạn)" (story 16, Nguyễn Đình Băng).
 *
 * Khác với `MonthlyPassStatus` ('Active' | 'Expired') lưu trong cột Status của bảng
 * MonthlyPasses: "Sắp hết hạn" KHÔNG phải một giá trị lưu — nó được suy ra từ `validTo`
 * so với hôm nay. Ba trạng thái này chỉ để vẽ giao diện, không đổi dữ liệu.
 */
export type MonthlyPassDisplayStatus = 'Valid' | 'ExpiringSoon' | 'Expired';

/**
 * Ngưỡng "sắp hết hạn": vé còn hiệu lực nhưng `validTo` còn trong vòng N ngày tới thì
 * coi là sắp hết hạn. Chọn 7 ngày để hành khách kịp gia hạn trước khi hết hẳn.
 */
export const EXPIRING_SOON_DAYS = 7;

/** Nhãn + màu Tag AntD cho từng trạng thái hiển thị. */
export const MONTHLY_PASS_STATUS_META: Record<
  MonthlyPassDisplayStatus,
  { label: string; color: string }
> = {
  Valid: { label: 'Còn hạn', color: 'success' },
  ExpiringSoon: { label: 'Sắp hết hạn', color: 'warning' },
  Expired: { label: 'Hết hạn', color: 'error' },
};

/** Kết quả tra trạng thái — gồm cả số ngày còn lại để giao diện kèm câu nhắc. */
export interface MonthlyPassStatusResult {
  status: MonthlyPassDisplayStatus;
  /** Số ngày còn lại, tính theo ngày trọn vẹn. Âm = đã hết hạn, 0 = hết hạn hôm nay. */
  daysLeft: number;
}

/**
 * Suy trạng thái hiển thị của vé tháng từ `status` + `validTo`.
 *
 * Luật:
 * - `status === 'Expired'` HOẶC `validTo` đã qua hôm nay → `Expired`. Kiểm tra cả hai vì
 *   BackgroundService (task của Kiên) chuyển cột Status theo lịch, có thể chưa kịp chạy —
 *   vé đã quá `validTo` thì dù cột Status còn 'Active' vẫn phải hiện là hết hạn.
 * - Còn hạn nhưng `validTo` trong vòng `EXPIRING_SOON_DAYS` ngày tới → `ExpiringSoon`.
 * - Còn lại → `Valid`.
 *
 * `now` chỉ để kiểm chứng (bơm mốc thời gian cố định); chỗ gọi thật không truyền.
 */
export function getMonthlyPassStatus(
  pass: Pick<MonthlyPass, 'status' | 'validTo'>,
  now: Dayjs = dayjs(),
): MonthlyPassStatusResult {
  const validTo = dayjs(pass.validTo).startOf('day');
  const today = now.startOf('day');
  const daysLeft = validTo.diff(today, 'day');

  let status: MonthlyPassDisplayStatus;
  if (pass.status === 'Expired' || daysLeft < 0) {
    status = 'Expired';
  } else if (daysLeft <= EXPIRING_SOON_DAYS) {
    status = 'ExpiringSoon';
  } else {
    status = 'Valid';
  }

  return { status, daysLeft };
}
