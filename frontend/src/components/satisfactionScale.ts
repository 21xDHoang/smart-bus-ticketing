/**
 * Thang đo mức độ hài lòng 1–5 sao — NGUỒN SỰ THẬT DUY NHẤT cho ý nghĩa của từng mức.
 *
 * Task Sprint 2 dòng 56 "Component chọn mức độ hài lòng (rating sao)" (story 24, Hoàng Văn
 * Thịnh). Trước file này, ba chỗ trong repo tự nhúng `<Rate>` của antd và **không chỗ nào**
 * định nghĩa thang 1–5 nghĩa là gì — báo cáo kiểm thử chéo phản ánh gọi đó là lý do dòng 56
 * tồn tại (mục F8). Nay mọi chỗ dùng chung bảng dưới đây.
 *
 * Hợp đồng (`docs/api-contract.md`): `rating` là number | null — "1–5 sao, mức độ hài lòng",
 * `null` với phản ánh không chấm điểm; giá trị ngoài khoảng 1..5 bị backend trả 400
 * (`errors.rating`). Vì vậy `null` là giá trị HỢP LỆ, không phải lỗi thiếu dữ liệu.
 */

/** Một mức trong thang: số sao và nhãn diễn giải. */
export interface SatisfactionLevel {
  value: number;
  label: string;
}

/**
 * Năm mức, từ thấp tới cao. Nhãn chọn theo lối nói thường của hành khách khi đánh giá
 * chuyến đi, không dùng thang "1/5 … 5/5" vì con số không tự nói lên điều gì.
 */
export const SATISFACTION_LEVELS: SatisfactionLevel[] = [
  { value: 1, label: 'Rất không hài lòng' },
  { value: 2, label: 'Không hài lòng' },
  { value: 3, label: 'Bình thường' },
  { value: 4, label: 'Hài lòng' },
  { value: 5, label: 'Rất hài lòng' },
];

/**
 * Số mức tối đa = số ngôi sao. Lấy từ độ dài bảng trên để thêm/bớt một mức chỉ phải sửa một
 * chỗ, không phải đi tìm các `<Rate count={5}>` rải rác.
 */
export const SATISFACTION_MAX = SATISFACTION_LEVELS.length;

/** Nhãn từng mức theo đúng thứ tự sao — để antd `Rate` hiện khi rê chuột qua từng ngôi sao. */
export const SATISFACTION_TOOLTIPS: string[] = SATISFACTION_LEVELS.map((level) => level.label);

/**
 * Nhãn diễn giải của một số sao.
 *
 * Trả `null` khi chưa chấm (`null`/`undefined`) **hoặc** khi số sao không khớp mức nào — dữ
 * liệu lạ từ API thì hiện trạng thái trung tính, không đoán bừa ra một nhãn.
 */
export function ratingLabel(value: number | null | undefined): string | null {
  if (value === null || value === undefined) return null;
  return SATISFACTION_LEVELS.find((level) => level.value === value)?.label ?? null;
}
