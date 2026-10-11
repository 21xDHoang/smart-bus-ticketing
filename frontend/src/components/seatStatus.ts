import { theme } from 'antd';
import type { SeatMapSeat, SeatStatus } from '../api/seatMapApi';

// -----------------------------------------------------------------------------
// Logic thuần + bộ màu cho trạng thái ghế — task "Component hiển thị trạng thái ghế
// (trống / đang giữ / đã bán)" (User Story 2, Sprint 3 — Hoàng Văn Thịnh). Tách khỏi component
// để kiểm chứng được mà không cần dựng React, cùng lối ticketStatus.ts / monthlyPassStatus.ts.
//
// Trạng thái ghế (Available/Held/Paid) là SUY RA theo chuyến từ SeatHolds + Tickets — không
// phải cột của bảng Seats (xem seatMapApi.ts và docs/26-csdl-so-do-ghe.md). Ở đây chỉ có nhãn
// hiển thị và bộ màu; việc vẽ nằm ở SeatStatusCell / SeatStatusLegend.
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
}

/**
 * Bộ màu cho một ghế theo trạng thái. Thứ tự ưu tiên quan trọng: trạng thái thắng "đang chọn"
 * (ghế đã bán / đang giữ thì không bao giờ được chọn nên không có chuyện vừa giữ vừa chọn).
 *
 * `selected` chỉ có nghĩa với ghế trống. Dùng chung cho cả ô ghế lẫn mục "Đang chọn" của chú
 * giải để hai chỗ không bao giờ lệch màu.
 */
export function seatAppearance(
  status: SeatStatus,
  token: SeatMapToken,
  selected = false,
): SeatAppearance {
  if (status === 'Paid') {
    return {
      background: token.colorFillQuaternary,
      border: `1px solid ${token.colorBorderSecondary}`,
      color: token.colorTextDisabled,
      cursor: 'not-allowed',
    };
  }

  if (status === 'Held') {
    return {
      background: token.colorWarningBg,
      border: `1px dashed ${token.colorWarning}`,
      color: token.colorWarning,
      cursor: 'not-allowed',
    };
  }

  if (selected) {
    return {
      background: token.colorPrimary,
      border: `1px solid ${token.colorPrimary}`,
      color: token.colorTextLightSolid,
      cursor: 'pointer',
    };
  }

  return {
    background: token.colorBgContainer,
    border: `1px solid ${token.colorBorder}`,
    color: token.colorText,
    cursor: 'pointer',
  };
}
