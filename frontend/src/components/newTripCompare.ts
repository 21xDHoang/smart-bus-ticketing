import dayjs from 'dayjs';
import { formatVnd } from './ui/format';

// So sánh GIỜ & GIÁ giữa chuyến mới và chuyến hiện tại (vé cũ) — logic thuần của component
// "chọn chuyến mới khi đổi vé" (story 5 "Hủy & Đổi vé", Sprint 4, task của Nguyễn Đình Băng).
//
// Tách khỏi .tsx để test độc lập (cùng lối satisfactionScale.ts, ticketStatus.ts). Con số
// chênh lệch giá chỉ cần `NewPrice - Ticket.Price`, không cần trường mới từ API
// (docs/32-csdl-huy-doi-ve.md §6); quy ước dấu: dương = khách trả thêm, âm = nhà xe hoàn
// lại, 0 = đổi ngang giá.

/** Sắc thái chênh lệch giá — để component tô màu chip: 'more' = trả thêm, 'less' = hoàn lại. */
export type PriceDiffTone = 'more' | 'less' | 'same';

/** Kết quả so sánh GIÁ giữa chuyến mới (`newPrice`) và vé cũ (`oldPrice`). */
export interface PriceDiff {
  /** `newPrice - oldPrice` — dương trả thêm, âm hoàn lại, 0 ngang giá. */
  diff: number;
  label: string;
  tone: PriceDiffTone;
}

/** So sánh giá hai chuyến, trả kèm nhãn ngắn + sắc thái để hiển thị. */
export function priceDiffOf(newPrice: number, oldPrice: number): PriceDiff {
  const diff = newPrice - oldPrice;

  if (diff > 0) return { diff, label: `Trả thêm ${formatVnd(diff)}`, tone: 'more' };
  if (diff < 0) return { diff, label: `Hoàn lại ${formatVnd(-diff)}`, tone: 'less' };
  return { diff, label: 'Ngang giá', tone: 'same' };
}

/**
 * Nhãn so sánh GIỜ khởi hành: chuyến mới muộn hơn / sớm hơn bao lâu so với chuyến hiện tại,
 * hay cùng giờ. Khoảng thời gian bỏ đơn vị thừa (1 giờ 0 phút → "1 giờ").
 */
export function timeDiffLabel(newTime: string, oldTime: string): string {
  const diffMinutes = dayjs(newTime).diff(dayjs(oldTime), 'minute');

  if (diffMinutes === 0) return 'Cùng giờ';

  const abs = Math.abs(diffMinutes);
  const hours = Math.floor(abs / 60);
  const minutes = abs % 60;

  const parts: string[] = [];
  if (hours > 0) parts.push(`${hours} giờ`);
  if (minutes > 0) parts.push(`${minutes} phút`);

  return `${diffMinutes > 0 ? 'Muộn hơn' : 'Sớm hơn'} ${parts.join(' ')}`;
}
