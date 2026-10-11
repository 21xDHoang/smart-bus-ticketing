import { useEffect, useRef, useState } from 'react';
import seatHoldApi from '../api/seatHoldApi';
import type { SeatHoldSession } from '../api/seatHoldApi';

// -----------------------------------------------------------------------------
// Xử lý lỗi khi mất kết nối trong lúc giữ chỗ — task Sprint 3 dòng 23
// (Hoàng Văn Thịnh), phục vụ US 3 "Tự động giữ chỗ trong 10 phút". Đi kèm
// SeatHoldConnectionNotice.tsx: hook lo logic, component lo hiển thị (cùng lối tách
// như seatHoldReminder.ts + SeatHoldCountdown).
//
// Vì sao cần: đồng hồ đếm ngược chạy bằng giờ MÁY KHÁCH nên mất mạng nó vẫn tick,
// còn trạng thái thật của phiên nằm ở máy chủ (SeatHolds.ExpiresAt — job quét của
// Kiên nhả ghế theo giờ máy chủ). Mất mạng giữa chừng là hai bên có thể lệch nhau:
// phiên đã hết hạn phía máy chủ trong khi màn hình vẫn tưởng còn giữ ghế. Task này
// xử lý đúng khoảng lệch đó:
//   1. Đang giữ chỗ mà mất mạng → pha 'Offline': báo khách phiên VẪN đang tính giờ.
//   2. Có mạng trở lại → tự gọi GET /seat-holds/{sessionCode} (endpoint "kiểm tra
//      trạng thái theo mã phiên" của Hiếu) để đối chiếu; kết quả luôn đẩy ra ngoài
//      qua `onResync` — màn cha cập nhật state phiên của mình (kể cả expiresAt mới,
//      hoặc Expired để hook tự nhả ghế của Hạnh phản ứng theo).
//   3. Có mạng nhưng máy chủ chưa trả lời được → thử lại mỗi 5 giây (pha
//      'ResyncFailed'), không để màn hình treo mãi ở trạng thái "đang kiểm tra".
//   4. Đối chiếu xong thấy phiên đã Expired/Released (chết trong lúc mất mạng) →
//      pha 'Lost'. Khác useSeatHoldAutoRelease (nuốt lỗi nhả ghế vì job máy chủ lo),
//      ở đây KHÔNG được nuốt: im lặng để khách tưởng còn ghế mới là lỗi thật.
//
// Ngoài sự kiện online/offline của trình duyệt còn kiểm tra ngầm khi tab quay lại
// hiện (visibilitychange): điện thoại khoá màn / tab ngủ đông có thể bỏ lọt sự kiện
// mạng. Lượt kiểm tra ngầm hỏng thì im lặng — nó chỉ là cơ hội làm mới, không phải
// lời hứa, còn lượt do sự kiện mạng thì LUÔN báo cho khách biết kết quả.
//
// Đang chạy dữ liệu giả (USE_MOCK_DATA = true trong seatHoldApi.ts): getSession giả
// luôn trả phiên 'Holding' nên thử tay bằng cách tắt mạng trong DevTools sẽ thấy pha
// 'Reconnected' khi bật lại; muốn thấy 'Lost' phải chạy với API thật.
// -----------------------------------------------------------------------------

/** Nhịp thử lại khi mạng đã trở lại nhưng máy chủ chưa trả lời được. */
const RESYNC_RETRY_MS = 5_000;

/** Thời gian giữ dòng "Đã kết nối lại" trước khi tự ẩn. */
const RECONNECT_FLASH_MS = 5_000;

/**
 * Pha xử lý mất kết nối — SeatHoldConnectionNotice dịch từng pha thành một Alert.
 * `null` = không có gì để nói (chưa có phiên, phiên khép lại vì lý do khác, hoặc mọi
 * thứ bình thường).
 */
export type SeatHoldConnectionPhase =
  | 'Offline' // đang giữ chỗ mà trình duyệt mất mạng
  | 'Reconnecting' // vừa có mạng lại, đang đối chiếu phiên với máy chủ
  | 'ResyncFailed' // có mạng nhưng chưa gọi được máy chủ — sẽ tự thử lại
  | 'Reconnected' // đối chiếu xong, phiên vẫn còn hiệu lực (hiện vài giây rồi ẩn)
  | 'Lost'; // đối chiếu xong, phiên đã kết thúc trong lúc mất mạng

type SyncState = 'idle' | 'syncing' | 'failed' | 'confirmed';

export function useSeatHoldConnection(
  session: SeatHoldSession | null,
  onResync: (session: SeatHoldSession) => void,
): SeatHoldConnectionPhase | null {
  // Luôn trỏ tới bản onResync mới nhất — máy đồng bộ chỉ dựng lại theo mã phiên, không
  // dựng lại mỗi lần màn cha render (cùng lối với useSeatMapRealtime).
  const onResyncRef = useRef(onResync);

  useEffect(() => {
    onResyncRef.current = onResync;
  });

  // Cờ mạng của trình duyệt — theo dõi SUỐT vòng đời hook, tách khỏi máy đồng bộ (chỉ
  // sống khi đang giữ chỗ) để cờ luôn đúng dù phiên đang ở trạng thái nào.
  const [isOffline, setIsOffline] = useState(() => !navigator.onLine);
  const [sync, setSync] = useState<SyncState>('idle');
  const [endedCode, setEndedCode] = useState<string | null>(null);

  useEffect(() => {
    const goOffline = () => setIsOffline(true);
    const goOnline = () => setIsOffline(false);

    window.addEventListener('offline', goOffline);
    window.addEventListener('online', goOnline);

    return () => {
      window.removeEventListener('offline', goOffline);
      window.removeEventListener('online', goOnline);
    };
  }, []);

  const sessionCode = session?.sessionCode ?? null;
  const isHolding = session?.status === 'Holding';

  // Đổi sang phiên khác thì bỏ mọi trạng thái đồng bộ của phiên cũ. Điều chỉnh state
  // NGAY LÚC RENDER theo khuyến nghị của React (như SeatHoldExpiryModal), không dùng
  // effect reset — tránh cảnh báo react/set-state-in-effect.
  const [prevCode, setPrevCode] = useState(sessionCode);
  if (prevCode !== sessionCode) {
    setPrevCode(sessionCode);
    setSync('idle');
    setEndedCode(null);
  }

  // Máy đồng bộ: chỉ sống khi đang thực sự giữ chỗ — phiên khép lại theo cách khác
  // (Confirmed/Released/Expired tự nhiên) là việc của modal và hook tự nhả, không phải
  // chuyện mất kết nối.
  useEffect(() => {
    if (sessionCode === null || !isHolding) return;

    let cancelled = false;
    let inFlight = false;
    let retryTimer: ReturnType<typeof setTimeout> | undefined;
    let flashTimer: ReturnType<typeof setTimeout> | undefined;

    // Ba kiểu lượt đồng bộ:
    // - 'announced': do sự kiện mạng (vừa có mạng lại) — có báo tiến trình và kết quả.
    // - 'retry':     thử lại sau khi máy chủ chưa trả lời — im tiến trình, không nhấp nháy.
    // - 'silent':    kiểm tra ngầm khi tab quay lại hiện — hỏng thì im lặng hoàn toàn.
    type ResyncMode = 'announced' | 'retry' | 'silent';

    const resync = async (mode: ResyncMode): Promise<void> => {
      if (cancelled || (inFlight && mode === 'silent')) return;
      inFlight = true;

      if (mode === 'announced') setSync('syncing');

      let renewed: SeatHoldSession | null = null;
      try {
        renewed = await seatHoldApi.getSession(sessionCode);
      } catch {
        renewed = null;
      } finally {
        inFlight = false;
      }

      if (cancelled) return;

      if (renewed === null) {
        if (mode === 'silent') return;
        setSync('failed');
        retryTimer = setTimeout(() => void resync('retry'), RESYNC_RETRY_MS);
        return;
      }

      onResyncRef.current(renewed);

      if (renewed.status === 'Expired' || renewed.status === 'Released') {
        // Chết thật trong lúc mất mạng — ghi nhớ theo mã phiên để banner nói thẳng,
        // dù màn cha có cập nhật state phiên hay không.
        setEndedCode(sessionCode);
        setSync('idle');
        return;
      }

      if (mode === 'silent' || renewed.status === 'Confirmed') {
        // Ngầm: không cần khoe. Confirmed: khách đã trả tiền ở luồng khác — việc của
        // màn thanh toán, không phải chuyện mất kết nối.
        setSync('idle');
        return;
      }

      setSync('confirmed');
      flashTimer = setTimeout(() => {
        if (!cancelled) setSync('idle');
      }, RECONNECT_FLASH_MS);
    };

    const handleOffline = () => {
      // Mất mạng lại trong lúc chờ thử lại — bỏ hẹn giờ, để sự kiện online kế tiếp
      // khởi động lại, không gọi API trong vô vọng.
      if (retryTimer !== undefined) {
        clearTimeout(retryTimer);
        retryTimer = undefined;
      }
    };

    const handleOnline = () => {
      void resync('announced');
    };

    const handleVisible = () => {
      if (document.visibilityState !== 'visible' || !navigator.onLine) return;
      // Tab ngủ đông / điện thoại khoá màn có thể đã bỏ lỡ sự kiện mạng — nhân lúc
      // khách quay lại thì đối chiếu ngầm một lượt.
      void resync('silent');
    };

    window.addEventListener('offline', handleOffline);
    window.addEventListener('online', handleOnline);
    document.addEventListener('visibilitychange', handleVisible);

    return () => {
      cancelled = true;
      if (retryTimer !== undefined) clearTimeout(retryTimer);
      if (flashTimer !== undefined) clearTimeout(flashTimer);
      window.removeEventListener('offline', handleOffline);
      window.removeEventListener('online', handleOnline);
      document.removeEventListener('visibilitychange', handleVisible);
    };
  }, [sessionCode, isHolding]);

  // ---- Suy ra pha hiển thị ----
  if (session === null) return null;

  // Đã đối chiếu và biết chắc phiên chết trong lúc mất mạng — ưu tiên trên mọi pha
  // khác, kể cả khi màn cha đã cập nhật phiên thành Expired.
  if (endedCode !== null && endedCode === session.sessionCode) return 'Lost';

  if (session.status !== 'Holding') return null;

  if (isOffline) return 'Offline';
  if (sync === 'syncing') return 'Reconnecting';
  if (sync === 'failed') return 'ResyncFailed';
  if (sync === 'confirmed') return 'Reconnected';

  return null;
}
