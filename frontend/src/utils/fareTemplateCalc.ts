// Công thức tính giá theo "bảng giá mẫu theo km" (US 12) — file logic THUẦN: không import
// React/antd/axios nên có thể test độc lập. Mọi quy ước làm tròn và áp ưu đãi đều CHỐT ở đây,
// màn hình và test cùng dùng một nguồn, khỏi lệch nhau.

/** Cách ưu đãi: giảm theo phần trăm giá người lớn, hay giảm một số tiền cố định trên mỗi km. */
export type DiscountKind = 'percent' | 'amount';

/** Một ưu đãi cho một đối tượng hành khách. */
export interface FareDiscount {
  kind: DiscountKind;
  /** Với `percent`: 0..100 (%). Với `amount`: số tiền VND giảm trên mỗi km. */
  value: number;
}

/**
 * Giá/km sau ưu đãi của một đối tượng.
 * - percent: `base × (1 - value/100)`.
 * - amount : `base - value`.
 * Kết quả không bao giờ âm (giá trị ưu đãi vượt quá giá gốc thì coi như 0).
 */
export function applyDiscount(basePricePerKm: number, discount: FareDiscount): number {
  if (!Number.isFinite(basePricePerKm) || !Number.isFinite(discount.value)) return 0;
  if (discount.kind === 'percent') {
    return Math.max(0, basePricePerKm * (1 - discount.value / 100));
  }
  return Math.max(0, basePricePerKm - discount.value);
}

/**
 * Giá vé một tuyến = giá/km sau ưu đãi × số km tuyến, làm tròn về đồng nguyên.
 * Dự án chưa có quy ước làm tròn bội số 500đ nên chỉ làm tròn về đồng (Math.round).
 * Số km thiếu/không hợp lệ (undefined → NaN) thì trả 0 thay vì lan truyền NaN ra màn hình.
 */
export function routeFare(pricePerKm: number, distanceKm: number): number {
  if (!Number.isFinite(pricePerKm) || !Number.isFinite(distanceKm)) return 0;
  return Math.round(pricePerKm * distanceKm);
}
