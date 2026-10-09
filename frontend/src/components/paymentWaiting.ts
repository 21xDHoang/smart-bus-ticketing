import type { PaymentStatusResult } from '../api/paymentApi';

// -----------------------------------------------------------------------------
// Logic thuần cho màn "chờ kết quả thanh toán + xử lý timeout" (US 6, Nguyễn Đình Băng) —
// tách khỏi component để kiểm chứng được mà không cần dựng React, cùng lối
// seatHoldReminder.ts / monthlyPassReminder.ts.
//
// Chỉ trả về DỮ LIỆU (pha chờ, số giây còn lại trước khi hết thời gian chờ); việc poll liên
// tục và đếm ngược nằm ở component (PaymentWaitingPage).
// -----------------------------------------------------------------------------

/**
 * Thời gian tối đa chờ kết quả thanh toán trước khi tuyên bố "hết thời gian chờ" (giây).
 *
 * Sau khi khách quay về từ cổng thanh toán, callback có thể chưa kịp về backend. Frontend poll
 * trong khoảng thời gian này; hết mà giao dịch vẫn Pending thì ngừng poll và mời khách kiểm tra
 * lại — KHÔNG tự coi là thành công cũng không tự coi là thất bại, vì giao dịch có thể vẫn đang
 * được xử lý phía cổng.
 */
export const PAYMENT_TIMEOUT_SECONDS = 60;

/** Khoảng cách giữa hai lần hỏi trạng thái giao dịch (giây). */
export const PAYMENT_POLL_INTERVAL_SECONDS = 3;

/**
 * Pha hiển thị của màn chờ:
 * - Pending: giao dịch chưa có kết quả cuối, vẫn trong hạn chờ.
 * - Success: cổng đã xác nhận thanh toán thành công.
 * - Failed: giao dịch thất bại (cổng từ chối hoặc khách huỷ).
 * - Timeout: hết `PAYMENT_TIMEOUT_SECONDS` mà vẫn chưa có kết quả cuối.
 */
export type PaymentWaitPhase = 'Pending' | 'Success' | 'Failed' | 'Timeout';

export interface PaymentWaitInfo {
  phase: PaymentWaitPhase;
  /** Số giây đã chờ kể từ khi mở màn (để log / hiển thị nếu cần). */
  secondsElapsed: number;
  /** Số giây còn lại trước khi hết hạn chờ — 0 khi đã ở pha cuối (Success/Failed/Timeout). */
  secondsLeft: number;
}

/**
 * Suy pha + thời gian còn lại của màn chờ từ trạng thái giao dịch mới nhất + số giây đã chờ.
 *
 * Luật:
 * - `status` là 'Success' hoặc 'Failed' → pha cuối ngay, không quan tâm đồng hồ.
 * - Còn lại (Pending hoặc chưa có kết quả nào) mà `secondsElapsed` đã chạm hạn → `Timeout`.
 * - Còn lại → `Pending`.
 *
 * `result` là null khi lần poll đầu tiên chưa về — vẫn tính là đang chờ, không nhầm thành lỗi.
 */
export function getPaymentWaitInfo(
  result: Pick<PaymentStatusResult, 'status'> | null,
  secondsElapsed: number,
): PaymentWaitInfo {
  if (result?.status === 'Success') {
    return { phase: 'Success', secondsElapsed, secondsLeft: 0 };
  }

  if (result?.status === 'Failed') {
    return { phase: 'Failed', secondsElapsed, secondsLeft: 0 };
  }

  if (secondsElapsed >= PAYMENT_TIMEOUT_SECONDS) {
    return { phase: 'Timeout', secondsElapsed, secondsLeft: 0 };
  }

  return {
    phase: 'Pending',
    secondsElapsed,
    secondsLeft: PAYMENT_TIMEOUT_SECONDS - secondsElapsed,
  };
}

/** Định dạng số giây còn lại thành "M:SS" để hiển thị — 0 giây trở xuống đều thành "0:00". */
export function formatWaitCountdown(secondsLeft: number): string {
  const safe = Math.max(0, secondsLeft);
  const minutes = Math.floor(safe / 60);
  const seconds = safe % 60;
  return `${minutes}:${String(seconds).padStart(2, '0')}`;
}
