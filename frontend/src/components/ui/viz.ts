// Helpers dùng chung cho biểu đồ SVG tự vẽ (dashboard doanh thu — không dùng thư viện biểu đồ
// ngoài để không phải đụng package.json). Chỉ chứa hàm thuần: định dạng nhãn trục + sinh mốc trục.

/** Định dạng tiền gọn cho nhãn trục: ≥ 1 tỷ → "x,y tỷ", ≥ 1 triệu → "x,y tr", ≥ 1 nghìn → "xK". */
export function compactVnd(value: number): string {
  const abs = Math.abs(value);
  if (abs >= 1_000_000_000) {
    return `${(value / 1_000_000_000).toLocaleString('vi-VN', { maximumFractionDigits: 1 })} tỷ`;
  }
  if (abs >= 1_000_000) {
    return `${(value / 1_000_000).toLocaleString('vi-VN', { maximumFractionDigits: 1 })} tr`;
  }
  if (abs >= 1_000) {
    return `${Math.round(value / 1_000)}K`;
  }
  return String(value);
}

/**
 * Các mốc trục "đẹp" từ 0 tới một trần >= `max`, tối đa `count` mốc. Bước nhảy theo chuỗi
 * 1/2/5 × 10ⁿ để nhãn trục tròn trịa thay vì số lẻ như 1.173.456.
 */
export function niceTicks(max: number, count: number): number[] {
  if (max <= 0) return [0];

  const rough = max / (count - 1);
  const magnitude = 10 ** Math.floor(Math.log10(rough));
  const normalized = rough / magnitude;
  const step =
    (normalized <= 1 ? 1 : normalized <= 2 ? 2 : normalized <= 5 ? 5 : 10) * magnitude;

  const ceiling = Math.ceil(max / step) * step;
  const ticks: number[] = [];
  for (let value = 0; value <= ceiling; value += step) {
    ticks.push(Math.round(value));
  }
  return ticks;
}

/**
 * Các chỉ số (trong mảng dữ liệu) cần vẽ nhãn trục x — trải đều, tối đa `count` nhãn, luôn có
 * điểm đầu và điểm cuối. Dùng cho trục ngày khi dữ liệu 30/90 ngày không thể ghi hết từng ngày.
 */
export function sparseIndices(length: number, count: number): number[] {
  if (length <= count) {
    return Array.from({ length }, (_, i) => i);
  }

  const indices = new Set<number>();
  for (let i = 0; i < count; i++) {
    indices.add(Math.round((i / (count - 1)) * (length - 1)));
  }
  return [...indices];
}
