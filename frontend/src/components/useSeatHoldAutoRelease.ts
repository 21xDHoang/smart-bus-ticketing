import { useEffect, useRef } from 'react';
import type { SeatHoldSession } from '../api/seatHoldApi';
import seatHoldReleaseApi from '../api/seatHoldReleaseApi';
import { getSeatHoldTimeInfo } from './seatHoldReminder';

// -----------------------------------------------------------------------------
// Hook "tự động nhả ghế và quay về trang chọn ghế khi hết hạn" (US 3, task của Dương Thị Hạnh).
//
// Nhận một phiên giữ chỗ và theo dõi tới khi nó hết hạn: lúc đó TỰ ĐỘNG gọi
// POST /seat-holds/{sessionCode}/release (nhả ghế về sơ đồ ngay, không đợi job nền) rồi gọi
// `onReleased` để màn hình quay về chọn ghế. Khác với SeatHoldExpiryModal (Băng) vốn chờ người
// dùng BẤM "Quay về chọn ghế": hook này làm việc đó tự động khi đồng hồ về 0.
//
// Luật quan trọng:
// - `onReleased` gọi ĐÚNG MỘT LẦN cho mỗi phiên, kể cả khi phiên đã hết hạn từ trước lúc hook
//   gắn vào (màn hình mở lên là đã quá hạn).
// - Việc nhả là best-effort: endpoint release idempotent (200 kể cả khi đã Expired/Released),
//   và job nền cũng tự nhả — nên dù gọi có hỏng, vẫn phải gọi `onReleased` vì ghế đã không còn
//   giữ được nữa. Không chặn việc quay về vì một lượt gọi nhả hỏng.
//
// Cách dùng (khi Băng nối luồng giữ chỗ vào màn sơ đồ ghế / thanh toán): màn hình cha giữ
// `session` của phiên đang giữ, rồi:
//   useSeatHoldAutoRelease(session, () => navigate('/seat-map', { replace: true }));
// Hook không vẽ gì lên màn hình — phần đếm ngược hiển thị là component của Thịnh, phần modal
// cảnh báo là SeatHoldExpiryModal của Băng.
// -----------------------------------------------------------------------------

export function useSeatHoldAutoRelease(
  session: SeatHoldSession | null,
  onReleased: () => void,
): void {
  // Giữ callback mới nhất trong ref để đồng hồ không phải dựng lại mỗi lần cha render —
  // `onReleased` thường là arrow nội tuyến nên không đưa thẳng vào deps của effect dưới.
  const onReleasedRef = useRef(onReleased);

  useEffect(() => {
    onReleasedRef.current = onReleased;
  }, [onReleased]);

  // Mã phiên đã nhả rồi — chặn gọi nhả hai lần cho cùng một phiên.
  const releasedCodeRef = useRef<string | null>(null);

  useEffect(() => {
    if (!session) return;
    if (releasedCodeRef.current === session.sessionCode) return;

    let timer: ReturnType<typeof setInterval> | undefined;

    const releaseIfExpired = () => {
      if (releasedCodeRef.current === session.sessionCode) return;
      if (getSeatHoldTimeInfo(session).phase !== 'Expired') return;

      // Đánh dấu TRƯỚC khi nhả để một nhịp không gọi hai lần.
      releasedCodeRef.current = session.sessionCode;
      if (timer) clearInterval(timer);

      // Nhả best-effort: không await, không chặn việc quay về nếu gọi hỏng (xem ghi chú đầu
      // file). Lỗi nuốt có chủ đích — job nền sẽ tự nhả, còn người dùng vẫn quay về chọn ghế.
      seatHoldReleaseApi.release(session.sessionCode).catch(() => {
        // Bỏ qua: việc nhả là best-effort, thất bại không đổi kết cục của người dùng.
      });

      onReleasedRef.current();
    };

    // Kiểm tra ngay khi gắn: phiên có thể đã hết hạn từ trước, không đợi nhịp đầu tiên.
    releaseIfExpired();
    timer = setInterval(releaseIfExpired, 1000);

    return () => {
      if (timer) clearInterval(timer);
    };
  }, [session]);
}
