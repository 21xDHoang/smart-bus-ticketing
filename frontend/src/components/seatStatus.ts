import { theme } from 'antd';
import type { SeatMapSeat, SeatStatus } from '../api/seatMapApi';
import { palette } from './ui/theme';
import type { ColorMode } from './ui/theme';

// -----------------------------------------------------------------------------
// Logic thuần + bộ màu cho trạng thái ghế — task "Component hiển thị trạng thái ghế
// (trống / đang giữ / đã bán)" (User Story 2, Sprint 3 — Hoàng Văn Thịnh). Tách khỏi component
// để kiểm chứng được mà không cần dựng React, cùng lối ticketStatus.ts / monthlyPassStatus.ts.
//
// Trạng thái ghế (Available/Held/Paid) là SUY RA theo chuyến từ SeatHolds + Tickets — không
// phải cột của bảng Seats (xem seatMapApi.ts và docs/26-csdl-so-do-ghe.md). Ở đây chỉ có nhãn
// hiển thị và bộ màu; việc vẽ nằm ở SeatStatusCell / SeatStatusLegend.
//
// 11/10/2026 — làm lại theo hệ thống "Sơ đồ mạng lưới": mỗi trạng thái một HÌNH DẠNG riêng
// (ô đặc / sọc chéo / viền đứt / nền trắng viền liền) để người mù màu vẫn phân biệt được, màu
// chỉ là lớp thông tin thứ hai. Ghế VIP là LOẠI ghế nên đánh dấu bằng viền đôi, không tốn
// thêm màu nào — cam để dành cho "đang giữ / sắp hết".
// -----------------------------------------------------------------------------

/** Token theme AntD — viết gọn kiểu này thay vì lặp `ReturnType<typeof theme.useToken>['token']`. */
export type SeatMapToken = ReturnType<typeof theme.useToken>['token'];

/**
 * Nhãn + câu giải thích cho từng trạng thái ghế — nguồn duy nhất cho cả ô ghế, chú giải và
 * aria-label. Muốn đổi chữ hiển thị thì đổi ở đây, không phải tìm khắp các màn hình.
 */
export const SEAT_STATUS_META: Record<SeatStatus, { label: string; description: string }> = {
  Available: { label: 'Trống', description: 'Còn trống — có thể chọn' },
  Held: { label: 'Đang giữ', description: 'Đang được giữ chỗ — tạm thời không chọn được' },
  Paid: { label: 'Đã bán', description: 'Đã bán — không chọn được' },
};

/**
 * Ghế có chọn được không — chỉ ghế còn trống. Hai trạng thái bị khoá vì lý do khác nhau:
 * "đang giữ" có thể mở lại (phiên giữ hết hạn thì ghế về trống), "đã bán" thì không.
 */
export function isSeatSelectable(status: SeatStatus): boolean {
  return status === 'Available';
}

/** Tên ghế để hiển thị — "A1", "A1 (VIP)" — dùng cho aria-label và các thông báo về ghế. */
export function seatDisplayName(seat: Pick<SeatMapSeat, 'seatNumber' | 'seatType'>): string {
  return `${seat.seatNumber}${seat.seatType === 'Vip' ? ' (VIP)' : ''}`;
}

/** Bộ màu của một ô ghế — đủ để vẽ ô ghế lẫn ô màu nhỏ trong chú giải. */
export interface SeatAppearance {
  background: string;
  border: string;
  color: string;
  cursor: 'pointer' | 'not-allowed';
  /** Viền đôi của ghế VIP (nếu có) — SeatStatusCell và chú giải cùng vẽ từ đây. */
  boxShadow?: string;
}

/**
 * Bộ màu cho một ghế theo trạng thái. Thứ tự ưu tiên quan trọng: trạng thái thắng "đang chọn"
 * (ghế đã bán / đang giữ thì không bao giờ được chọn nên không có chuyện vừa giữ vừa chọn).
 *
 * `selected` chỉ có nghĩa với ghế trống; `vip` là LOẠI ghế, độc lập với trạng thái. Dùng chung
 * cho cả ô ghế lẫn các mục của chú giải để hai chỗ không bao giờ lệch nhau.
 *
 * `mode` (sáng/tối) quyết định viền VIP và nền ghế "đã bán" — nền màu chức năng giữ nguyên ở
 * cả hai chế độ, chỉ mực viền đổi theo `palette(mode)`.
 */
export function seatAppearance(
  status: SeatStatus,
  token: SeatMapToken,
  mode: ColorMode,
  options: { selected?: boolean; vip?: boolean } = {},
): SeatAppearance {
  const { selected = false, vip = false } = options;
  const p = palette(mode);

  // Viền đôi của ghế VIP: một khe hở màu nền rồi một vòng mực bên trong. Đọc được cả
  // trạng thái (màu nền) lẫn loại ghế (viền) mà không cần thêm màu thứ năm.
  const vipRing = (base: string) => `inset 0 0 0 2px ${base}, inset 0 0 0 3.5px ${p.neutral.ink}`;

  // Đã bán — ô ĐẶC: kín màu, chữ trắng. Nhìn từ xa cũng thấy "kín chỗ", không nhầm với trống.
  if (status === 'Paid') {
    return {
      background: p.neutral.seatSold,
      border: `1px solid ${p.neutral.seatSold}`,
      color: token.colorTextLightSolid,
      cursor: 'not-allowed',
      boxShadow: vip ? vipRing(p.neutral.seatSold) : undefined,
    };
  }

  // Đang giữ — SỌC CHÉO + viền đứt: đọc ra ngay "tạm thời", khác hẳn "đã bán" là vĩnh viễn.
  if (status === 'Held') {
    return {
      background: `repeating-linear-gradient(135deg, ${token.colorWarningBg} 0 5px, ${token.colorWarningBorder} 5px 10px)`,
      border: `1px dashed ${token.colorWarning}`,
      color: token.colorWarningText,
      cursor: 'not-allowed',
      // Ghế đang giữ đã có sọc chéo + viền đứt; thêm viền VIP nữa sẽ rối nên cố ý bỏ.
    };
  }

  // Đang chọn — teal "của bạn" (colorInfo = secondary): cùng màu với mọi dấu hiệu "thuộc về
  // mình" trong app. Teal chỉ 2.49:1 với chữ trắng nên chữ ở đây là chữ tối `onBase`.
  if (selected) {
    return {
      background: p.secondary.base,
      border: `1px solid ${p.secondary.base}`,
      color: p.secondary.onBase,
      cursor: 'pointer',
      boxShadow: vip ? vipRing(p.secondary.base) : undefined,
    };
  }

  // Trống — nền trắng viền liền, sẵn sàng để bấm.
  return {
    background: token.colorBgContainer,
    border: `1px solid ${token.colorBorder}`,
    color: token.colorText,
    cursor: 'pointer',
    boxShadow: vip ? vipRing(token.colorBgContainer) : undefined,
  };
}
