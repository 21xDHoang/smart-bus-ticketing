import { useEffect, useState } from 'react';
import { Alert } from 'antd';
import dayjs from 'dayjs';
import type { Dayjs } from 'dayjs';
import { SEAT_HOLD_DURATION_SECONDS } from '../api/seatHoldApi';
import type { SeatHoldSession } from '../api/seatHoldApi';
import { formatHoldCountdown, getSeatHoldTimeInfo } from './seatHoldReminder';

interface SeatHoldCountdownProps {
  /** Phiên giữ chỗ đang hiển thị. null = chưa giữ ghế nào (không hiện gì). */
  session: SeatHoldSession | null;
  /** Mốc "bây giờ" — chỉ để kiểm chứng; bỏ trống thì component tự đếm giờ. */
  now?: Dayjs;
}

// -----------------------------------------------------------------------------
// Component đếm ngược thời gian giữ chỗ trên UI — task Sprint 3 dòng 20
// (Hoàng Văn Thịnh), phục vụ US 3 "Tự động giữ chỗ trong 10 phút".
//
// Banner đứng THƯỜNG TRỰC suốt lượt giữ chỗ: "Ghế A1, A2 đang được giữ — còn 9:41",
// tự chuyển sang cảnh báo khi còn ≤ SEAT_HOLD_WARN_SECONDS giây rồi báo hết hạn. Khác
// với SeatHoldExpiryModal (Nguyễn Đình Băng) — modal chỉ bật lên lúc sắp hết hạn để mời
// gia hạn; banner này đứng suốt để khách luôn biết mình còn bao nhiêu thời gian.
//
// Thuần hiển thị, KHÔNG gọi API: gia hạn nằm ở modal, tự động nhả ghế nằm ở
// useSeatHoldAutoRelease (Dương Thị Hạnh). Pha + số giây dùng chung
// getSeatHoldTimeInfo / formatHoldCountdown trong seatHoldReminder.ts nên banner, modal
// và hook không bao giờ lệch nhau về luật thời gian.
//
// Component tự đứng được: màn hình cha (màn chọn ghế — luồng giữ chỗ của Băng đang chờ
// API) chỉ cần truyền phiên đang giữ. `now` chỉ để kiểm chứng (bơm mốc cố định) — chỗ
// gọi thật không truyền.
// -----------------------------------------------------------------------------
export default function SeatHoldCountdown({ session, now }: SeatHoldCountdownProps) {
  // Đồng hồ nội bộ: tick mỗi giây để số còn lại tự chạy. Bơm `now` cố định (kiểm chứng)
  // thì không tick — mốc thời gian do bên ngoài quyết.
  const [nowState, setNowState] = useState(() => dayjs());

  // Điều kiện tick là "đang có phiên", không phải từng object `session` — màn cha cập
  // nhật session mới mỗi lần gọi API cũng không dựng lại đồng hồ (đổi phiên này sang
  // phiên khác vẫn một nhịp đếm).
  const ticking = session !== null && !now;

  useEffect(() => {
    if (!ticking) return;
    const timer = setInterval(() => setNowState(dayjs()), 1000);
    return () => clearInterval(timer);
  }, [ticking]);

  if (!session) return null;

  // Phiên khép lại theo cách khác hết hạn — đã thanh toán xong (Confirmed) hoặc khách tự
  // nhả (Released) — thì đếm ngược không còn nghĩa, màn cha đang chuyển bước. Chỉ hiển
  // thị khi còn đang giữ, hoặc đã hết hạn (Expired: báo để khách biết ghế đã mất).
  if (session.status === 'Confirmed' || session.status === 'Released') return null;

  const info = getSeatHoldTimeInfo(session, now ?? nowState);
  const seatLabels = session.seatNumbers.join(', ');
  const remaining = formatHoldCountdown(info.secondsLeft);
  const extendMinutes = SEAT_HOLD_DURATION_SECONDS / 60;

  if (info.phase === 'Expired') {
    return (
      <Alert
        type="error"
        showIcon
        message={`Ghế ${seatLabels} đã hết thời gian giữ`}
        description="Ghế đã được nhả để người khác chọn. Hãy quay lại chọn ghế để đặt lại."
      />
    );
  }

  if (info.phase === 'ExpiringSoon') {
    return (
      <Alert
        type="warning"
        showIcon
        message={`Ghế ${seatLabels} sắp hết thời gian giữ — còn ${remaining}`}
        description={`Hết giờ, ghế sẽ được nhả tự động. Gia hạn để giữ thêm ${extendMinutes} phút (tối đa 1 lần).`}
      />
    );
  }

  return (
    <Alert
      type="info"
      showIcon
      message={`Ghế ${seatLabels} đang được giữ — còn ${remaining}`}
    />
  );
}
