import type { Stop } from '../api/stopApi';

// -----------------------------------------------------------------------------
// Gợi ý trạm dừng theo từ khoá — logic thuần, tách khỏi component để chạy thử được.
//
// VÌ SAO LỌC Ở PHÍA CLIENT: hợp đồng KHÔNG có endpoint "gợi ý trạm theo từ khoá". Mục
// "Trạm dừng — /stops" của docs/api-contract.md chỉ có 5 route CRUD, và mục "Tra cứu tuyến"
// (docs/api-contract.md:644) ghi rõ frontend tra cứu tuyến "đang ghép tạm từ GET /routes +
// /stops". Nên cách đúng theo hợp đồng là lấy trọn danh sách trạm một lượt rồi tự lọc, chứ
// không gọi thêm endpoint nào. Đổi lại: mỗi phím gõ không tốn request nào.
// -----------------------------------------------------------------------------

/** Số gợi ý hiện tối đa trong dropdown. */
export const STOP_SUGGESTION_LIMIT = 10;

/**
 * Chuẩn hoá chuỗi để so khớp: bỏ dấu, thường hoá, gộp khoảng trắng.
 *
 * Hành khách gõ "my dinh" hay "Mỹ Đình" phải ra cùng kết quả — không ai gõ dấu khi tra cứu
 * trên điện thoại. Đây cũng là lý do chính component này tồn tại: danh sách trạm lưu tên CÓ
 * dấu, nên gõ không dấu mà vẫn chọn được trạm thì giá trị gửi lên backend mới đúng chính tả.
 *
 * `đ` phải thay tay: NFD không tách được nó (đ là ký tự riêng U+0111, không phải d ghép dấu),
 * nên nếu quên thì "đồng" không khớp "dong" trong khi "Đống" lại khớp "Dong" — sai lệch rất khó
 * lần.
 */
export function normalizeForSearch(text: string): string {
  return text
    .normalize('NFD')
    .replace(/[̀-ͯ]/g, '')
    .toLowerCase()
    .replace(/đ/g, 'd')
    .replace(/\s+/g, ' ')
    .trim();
}

// Xếp hạng khớp — số nhỏ đứng trước. Hằng số thường chứ không phải `const enum`: esbuild của
// Vite không hỗ trợ const enum, và `isolatedModules` cũng chặn.
/** Tên trạm BẮT ĐẦU bằng từ khoá — gần như chắc chắn là trạm người dùng đang tìm. */
const RANK_NAME_PREFIX = 0;
/** Tên trạm CHỨA từ khoá ở giữa ("Giấy" → "Trạm Cầu Giấy"). */
const RANK_NAME_CONTAINS = 1;
/** Chỉ địa chỉ chứa từ khoá ("Hai Bà Trưng") — gợi ý yếu nhất. */
const RANK_ADDRESS_CONTAINS = 2;

function rankOf(stop: Stop, needle: string): number | null {
  const name = normalizeForSearch(stop.name);

  if (name.startsWith(needle)) return RANK_NAME_PREFIX;
  if (name.includes(needle)) return RANK_NAME_CONTAINS;
  if (normalizeForSearch(stop.address).includes(needle)) return RANK_ADDRESS_CONTAINS;

  return null;
}

/**
 * Danh sách trạm gợi ý cho từ khoá, xếp hạng rồi cắt còn `limit` dòng.
 *
 * Từ khoá RỖNG (hoặc toàn khoảng trắng) trả về đầu danh sách theo tên: người dùng vừa bấm vào
 * ô đã thấy ngay vài trạm để chọn, không phải nghĩ ra từ khoá trước. Đây là ô "chọn trạm dừng",
 * không phải ô tìm kiếm — bắt gõ trước mới hiện gì là chặn đường người dùng.
 *
 * Không sửa mảng đầu vào, và tự sắp xếp lại nên kết quả không phụ thuộc thứ tự backend trả về.
 */
export function suggestStops(
  stops: Stop[],
  keyword: string,
  limit: number = STOP_SUGGESTION_LIMIT,
): Stop[] {
  const needle = normalizeForSearch(keyword);

  if (!needle) {
    return [...stops]
      .sort((a, b) => a.name.localeCompare(b.name, 'vi'))
      .slice(0, Math.max(limit, 0));
  }

  const scored: { stop: Stop; rank: number }[] = [];

  for (const stop of stops) {
    const rank = rankOf(stop, needle);
    if (rank !== null) scored.push({ stop, rank });
  }

  scored.sort(
    (a, b) => a.rank - b.rank || a.stop.name.localeCompare(b.stop.name, 'vi'),
  );

  return scored.slice(0, Math.max(limit, 0)).map((entry) => entry.stop);
}
