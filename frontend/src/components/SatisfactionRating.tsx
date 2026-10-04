import { Rate, Space, Typography } from 'antd';
import { SATISFACTION_MAX, SATISFACTION_TOOLTIPS, ratingLabel } from './satisfactionScale';

const { Text } = Typography;

interface SatisfactionRatingProps {
  /** Số sao 1..5; `null`/`undefined` = chưa chấm. antd Form truyền vào qua chính prop này. */
  value?: number | null;

  /**
   * Trả về số sao, hoặc `null` khi hành khách xoá chấm. Đã chuẩn hoá từ số `0` mà antd `Rate`
   * trả về lúc xoá — chỗ gọi không phải tự nhớ cái bẫy đó nữa.
   */
  onChange?: (value: number | null) => void;

  /** Chế độ chỉ đọc (bảng, màn xác nhận): hiện số sao đã chấm, không chấm được. */
  readOnly?: boolean;
}

/**
 * Component chọn / hiển thị mức độ hài lòng (rating sao) — task Sprint 2 dòng 56, story 24,
 * Hoàng Văn Thịnh. Thay ba chỗ tự nhúng `<Rate>` rải rác trong repo (mục F8 của báo cáo kiểm
 * thử chéo phản ánh):
 *
 *   - `FeedbackSubmitPage` — ô nhập trong form gửi phản ánh, và thẻ xác nhận sau khi gửi;
 *   - `MyFeedbackPage` — cột "Mức độ hài lòng" của bảng phản ánh của tôi.
 *
 * Gom về một chỗ hai thứ mà trước đây mỗi nơi làm một kiểu:
 *
 * 1. **"Chưa chấm sao" hiển thị là gì.** Trước đây bảng phản ánh in `—` còn thẻ xác nhận để
 *    trống; nay cả hai dùng `—` (xem nhánh `readOnly` bên dưới). Để trống thì người đọc không
 *    phân biệt được "không chấm sao" với "lỗi hiển thị".
 * 2. **Ý nghĩa của thang 1–5.** Lấy từ `satisfactionScale.ts`, dùng làm nhãn gợi ý khi rê
 *    chuột qua từng ngôi sao và làm nhãn mức đang chọn.
 *
 * Ở chế độ nhập, component cố ý KHÔNG tự kiểm tra giá trị: `count` đã chặn không cho chấm ra
 * ngoài 1..5, còn ràng buộc thật là của backend (ngoài khoảng → 400 `errors.rating`).
 */
export default function SatisfactionRating({
  value,
  onChange,
  readOnly = false,
}: SatisfactionRatingProps) {
  // Chỉ đọc: chưa chấm sao thì chỉ hiện `—`, không hiện thêm năm ngôi sao rỗng — giữ đúng
  // hình dạng cột của bảng phản ánh như trước, và `—` là quy ước duy nhất cho mọi nơi.
  if (readOnly) {
    if (value === null || value === undefined) {
      return <Text type="secondary">—</Text>;
    }

    return <Rate disabled count={SATISFACTION_MAX} value={value} tooltips={SATISFACTION_TOOLTIPS} />;
  }

  // Nhãn của mức đang chọn, hiện ngay cạnh các ngôi sao: con số 4 không tự nói lên điều gì,
  // còn "Hài lòng" thì có. Chưa chọn gì thì không hiện nhãn nào để ô nhập vẫn là ô trống.
  const label = ratingLabel(value);

  return (
    <Space size={8} wrap>
      <Rate
        count={SATISFACTION_MAX}
        allowClear
        value={value ?? 0}
        tooltips={SATISFACTION_TOOLTIPS}
        // antd Rate báo 0 khi hành khách bấm vào ngôi sao đang chọn để xoá; hợp đồng chỉ nhận
        // 1..5 hoặc null nên đổi 0 thành null tại đây, một lần, cho mọi chỗ gọi.
        onChange={(next) => onChange?.(next === 0 ? null : next)}
      />
      {label && <Text type="secondary">{label}</Text>}
    </Space>
  );
}
