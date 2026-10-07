import dayjs from 'dayjs';
import type { Dayjs } from 'dayjs';
import type { SeatHoldSession } from '../api/seatHoldApi';

// -----------------------------------------------------------------------------
// Logic thuần cho modal "thông báo hết hạn giữ chỗ" (US 3, Nguyễn Đình Băng) — tách khỏi
// component để kiểm chứng được mà không cần dựng React, cùng lối monthlyPassStatus.ts /
// monthlyPassReminder.ts.
//
// Chỉ trả về DỮ LIỆU (pha, số giây còn lại, còn gia hạn được không); việc hiển thị và đếm
// ngược liên tục nằm ở component (SeatHoldExpiryModal). Component đếm ngược riêng là task
// của Hoàng Văn Thịnh — không nằm ở đây, modal chỉ cần pha + số giây để báo gấp.
// -----------------------------------------------------------------------------

/** Ngưỡng cảnh báo: còn trong vòng N giây nữa thì hết hạn thì coi là "sắp hết hạn". */
export const SEAT_HOLD_WARN_SECONDS = 60;

/**
 * Pha của một phiên giữ chỗ theo đồng hồ:
 * - Holding: vẫn còn nhiều thời gian, chưa cần cảnh báo.
 * - ExpiringSoon: sắp hết hạn (còn trong vòng `SEAT_HOLD_WARN_SECONDS` giây).
 * - Expired: đã hết hạn (quá `expiresAt`) hoặc phiên đã kết thúc (không còn Holding).
 */
export type SeatHoldPhase = 'Holding' | 'ExpiringSoon' | 'Expired';

export interface SeatHoldTimeInfo {
  phase: SeatHoldPhase;
  /** Số giây còn lại tới `expiresAt`. Âm = đã quá hạn. */
  secondsLeft: number;
  /** Còn gia hạn được không — đã hết hạn thì gia hạn không còn nghĩa. */
  canExtend: boolean;
}

/**
 * Suy pha + thời gian còn lại của một phiên giữ chỗ từ `status` + `expiresAt`.
 *
 * Luật:
 * - `status` khác 'Holding' HOẶC `expiresAt` đã qua → `Expired`. Kiểm tra cả hai vì job quét
 *   của BackgroundService (task của Kiên) chuyển Status theo lịch, có thể chưa kịp chạy — dù
 *   cột Status còn 'Holding' nhưng đã quá `expiresAt` thì vẫn phải coi là hết hạn.
 * - Còn Holding nhưng `secondsLeft` trong ngưỡng cảnh báo → `ExpiringSoon`.
 * - Còn lại → `Holding`.
 *
 * `now` chỉ để kiểm chứng (bơm mốc thời gian cố định); chỗ gọi thật không truyền.
 */
export function getSeatHoldTimeInfo(
  session: Pick<SeatHoldSession, 'status' | 'expiresAt' | 'canExtend'>,
  now: Dayjs = dayjs(),
): SeatHoldTimeInfo {
  const secondsLeft = dayjs(session.expiresAt).diff(now, 'second');
  const expired = session.status !== 'Holding' || secondsLeft <= 0;

  const phase: SeatHoldPhase = expired
    ? 'Expired'
    : secondsLeft <= SEAT_HOLD_WARN_SECONDS
      ? 'ExpiringSoon'
      : 'Holding';

  return { phase, secondsLeft, canExtend: session.canExtend && !expired };
}

/** Định dạng số giây còn lại thành "M:SS" để hiển thị — 0 giây trở xuống đều thành "0:00". */
export function formatHoldCountdown(secondsLeft: number): string {
  const safe = Math.max(0, secondsLeft);
  const minutes = Math.floor(safe / 60);
  const seconds = safe % 60;
  return `${minutes}:${String(seconds).padStart(2, '0')}`;
}
