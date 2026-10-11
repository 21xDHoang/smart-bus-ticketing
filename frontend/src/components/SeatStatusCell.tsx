import type { CSSProperties } from 'react';
import { theme } from 'antd';
import type { SeatMapSeat } from '../api/seatMapApi';
import { SEAT_STATUS_META, isSeatSelectable, seatAppearance, seatDisplayName } from './seatStatus';
import { useColorMode } from './ui/colorMode';

interface SeatStatusCellProps {
  /** Ghế cần vẽ — dùng id, seatNumber, seatType, status. */
  seat: SeatMapSeat;
  /** Ghế đang được chọn (chỉ có nghĩa với ghế trống). */
  selected?: boolean;
  /** Chuột đang nằm trên ô ghế này — chỉ để đổi nền cho dễ thấy. */
  hovered?: boolean;
  /** Đưa / rời chuột khỏi ghế — nhận null khi rời đi. */
  onHover?: (seatId: string | null) => void;
  /** Bấm vào ghế trống để chọn / bỏ chọn. Ghế bị khoá thì không gọi. */
  onToggle?: (seat: SeatMapSeat) => void;
}

/**
 * Một ô ghế trong sơ đồ — trạng thái quyết định màu (trống / đang giữ / đã bán), bấm để
 * chọn / bỏ chọn, ghế không trống thì khoá.
 *
 * Task "Component hiển thị trạng thái ghế (trống / đang giữ / đã bán)" (US 2, Sprint 3 — Hoàng
 * Văn Thịnh). Tách khỏi SeatMapPage để màn sơ đồ ghế và các chỗ sau này (đồng bộ realtime
 * trạng thái ghế, màn hình khác cần hiện trạng thái ghế) dùng lại thay vì tự vẽ màu riêng —
 * màu và nhãn nằm ở seatStatus.ts.
 *
 * Component KHÔNG tự giữ state: trạng thái ghế nằm ở props, nên khi trạng thái đổi (ví dụ
 * qua SignalR) chỉ cần truyền prop mới xuống là ô ghế tự vẽ lại.
 *
 * 11/10/2026: ghế VIP không còn chấm cam ở góc nữa — chuyển sang viền đôi (seatAppearance)
 * để cam chỉ còn mang nghĩa "đang giữ / sắp hết", không lẫn với loại ghế.
 */
export default function SeatStatusCell({
  seat,
  selected = false,
  hovered = false,
  onHover,
  onToggle,
}: SeatStatusCellProps) {
  const { token } = theme.useToken();
  const { mode } = useColorMode();
  const meta = SEAT_STATUS_META[seat.status];

  const style: CSSProperties = {
    position: 'relative',
    width: 44,
    height: 44,
    borderRadius: 8,
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    fontSize: 13,
    fontWeight: 600,
    padding: 0,
    transition: 'background-color 0.15s, border-color 0.15s, color 0.15s',
    ...seatAppearance(seat.status, token, mode, { selected, vip: seat.seatType === 'Vip' }),
  };

  // Chỉ ghế còn trống mới có hiệu ứng rê chuột; ghế đang chọn giữ nguyên màu "đang chọn".
  if (hovered && isSeatSelectable(seat.status) && !selected) {
    style.background = token.colorBgTextHover;
  }

  return (
    <button
      type="button"
      disabled={!isSeatSelectable(seat.status)}
      onClick={() => onToggle?.(seat)}
      onMouseEnter={() => onHover?.(seat.id)}
      onMouseLeave={() => onHover?.(null)}
      aria-pressed={selected}
      aria-label={`Ghế ${seatDisplayName(seat)} — ${meta.label}`}
      title={meta.description}
      style={style}
    >
      {seat.seatNumber}
    </button>
  );
}
