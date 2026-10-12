import { useEffect, useState } from 'react';
import { AutoComplete, Space, Typography } from 'antd';
import { EnvironmentOutlined } from '@ant-design/icons';
import stopSearchApi from '../api/stopSearchApi';
import type { Stop } from '../api/stopApi';

const { Text } = Typography;

// -----------------------------------------------------------------------------
// Ô nhập có gợi ý trạm dừng — task "Component autocomplete chọn trạm dừng"
// (Sprint 2 dòng 33, US 1.0 "Tra cứu tuyến xe", Hoàng Văn Thịnh).
//
// Dùng cho hai ô "Điểm đi" / "Điểm đến" của màn Tra cứu tuyến
// (pages/RouteLookupPage.tsx — đã nối vào ngày 2026-10-12).
//
// GIÁ TRỊ PHÁT RA LÀ TÊN TRẠM (chuỗi), không phải id: endpoint tra cứu tuyến nhận điểm đi/điểm
// đến là chuỗi tự do rồi tự giải từ khoá ra trạm theo TÊN (xem RouteSearchService của backend).
// Trả về id sẽ buộc phải đổi hình dạng API — việc mà quy ước cấm tự làm.
//
// KHÔNG CHẶN NGƯỜI DÙNG: đây là AutoComplete chứ không phải Select, nên gõ tay giá trị bất kỳ
// vẫn gửi được như trước. Gợi ý chỉ là lớp cộng thêm.
//
// VÌ SAO LẤY DỮ LIỆU THEO TỪNG TỪ KHOÁ (không nạp trọn danh sách rồi lọc tại chỗ như bản đầu):
//  1. Bản đầu gọi GET /stops — endpoint của màn quản lý trạm, [Authorize(ManagerOrAbove)]. Hành
//     khách gọi vào là 403, tức ô gợi ý câm đúng với người cần nó nhất. GET /stops/search mới là
//     đường công khai (mục "Tra cứu trạm dừng — /stops/search" của docs/api-contract.md).
//  2. Endpoint đó đã khớp sẵn KHÔNG PHÂN BIỆT DẤU ("ben thanh" ra "Bến Thành") — đúng thứ hành
//     khách gõ trên điện thoại. Nó cũng xếp hạng sẵn (tên-đầu → tên-chứa → địa-chỉ), nên giữ
//     thêm một lớp lọc phía client chỉ tạo ra hai định nghĩa "bỏ dấu" dễ lệch nhau.
//  3. Nạp trọn danh sách có trần: endpoint trả tối đa `limit` dòng (mặc định 10), nên "nạp hết
//     rồi lọc" sẽ âm thầm chỉ lọc được 10 trạm đầu khi bảng trạm lớn lên.
// Đổi lại: mỗi cụm gõ tốn một request — đã chặn bằng debounce bên dưới.
//
// Khớp cả ĐỊA CHỈ là chủ ý của endpoint, và ở màn này nó đặc biệt đắt: hành khách nhớ khu vực dễ
// hơn nhớ tên trạm. Gõ "quan 5" ra "Chợ Lớn" và "Đại học Y Dược", gõ "quan 1" ra "Bến Thành" và
// "Công viên 23/9" — mà GET /routes/search chỉ khớp theo TÊN trạm. Chọn xong thì giá trị gửi đi
// là tên trạm, đúng thứ endpoint kia cần.
//
// (Ghi chú dữ liệu, không phải lỗi của ô này: hiện 10 trạm trong CSDL mới có 4 trạm nằm trong
// tuyến — Bến Thành, Công viên 23/9, Đại học Y Dược, Chợ Lớn. Chọn 6 trạm còn lại vẫn ra "không
// tìm thấy tuyến", vì chưa seeder nào xếp chúng vào tuyến.)
// -----------------------------------------------------------------------------

/** Chờ người dùng ngừng gõ rồi mới hỏi backend — một request cho một cụm gõ, không phải mỗi ký tự. */
const DEBOUNCE_MS = 300;

interface StopAutocompleteProps {
  /** Tên trạm đã chọn. Khớp hợp đồng value/onChange để cắm thẳng vào Form.Item. */
  value?: string;
  onChange?: (value: string) => void;
  placeholder?: string;
  disabled?: boolean;
  allowClear?: boolean;
  /** Form.Item truyền vào để `<label htmlFor>` trỏ đúng ô nhập. */
  id?: string;
}

export default function StopAutocomplete({
  value,
  onChange,
  placeholder,
  disabled,
  allowClear = true,
  id,
}: StopAutocompleteProps) {
  const [stops, setStops] = useState<Stop[]>([]);
  // Khởi tạo `true`: lượt hỏi đầu tiên nằm trong cửa sổ debounce, chưa có kết quả nào — lúc đó
  // "đang tải" mới đúng sự thật, còn "không có trạm nào khớp" là nói sai.
  const [loading, setLoading] = useState(true);
  const [failed, setFailed] = useState(false);
  // Từ khoá đang gõ. Cố ý KHÔNG suy từ `value`: người dùng gõ dở dang thì `value` đã đổi theo
  // từng ký tự, nhưng lúc họ chọn một gợi ý thì gợi ý phải biến mất ngay chứ không lọc lại
  // theo đúng tên trạm vừa chọn rồi hiện ra một dòng lẻ.
  const [keyword, setKeyword] = useState('');

  useEffect(() => {
    let cancelled = false;
    const trimmed = keyword.trim();

    const timer = setTimeout(() => {
      // Đổi trạng thái bên trong hẹn giờ chứ không ở thân effect: cửa sổ debounce giữ nguyên
      // danh sách cũ trên màn hình (không nhấp nháy), và effect không setState đồng bộ.
      setLoading(true);
      setFailed(false);

      stopSearchApi
        .search(trimmed)
        .then((list) => {
          if (!cancelled) setStops(list);
        })
        .catch(() => {
          // Gợi ý hỏng không được chặn gõ tay — im lặng bỏ gợi ý, chỉ đổi lời nhắn trong dropdown.
          if (!cancelled) {
            setStops([]);
            setFailed(true);
          }
        })
        .finally(() => {
          if (!cancelled) setLoading(false);
        });
    }, DEBOUNCE_MS);

    return () => {
      cancelled = true;
      clearTimeout(timer);
    };
  }, [keyword]);

  // Backend đã xếp hạng sẵn (tên-đầu → tên-chứa → địa-chỉ) nên ở đây chỉ còn việc bày ra.
  const options = stops.map((stop) => ({
    // value chính là thứ AutoComplete đổ vào ô nhập khi chọn, và cũng là thứ phát ra ngoài.
    value: stop.name,
    label: (
      <Space direction="vertical" size={0}>
        <Text>{stop.name}</Text>
        <Text type="secondary" style={{ fontSize: 12 }}>
          {stop.address}
        </Text>
      </Space>
    ),
  }));

  const notFoundContent = (() => {
    if (loading) return 'Đang tải danh sách trạm…';
    if (failed) return 'Không tải được gợi ý trạm — bạn vẫn gõ tên trạm bình thường được.';
    return 'Không có trạm nào khớp — bạn vẫn gõ tên trạm bình thường được.';
  })();

  return (
    <AutoComplete
      id={id}
      value={value}
      options={options}
      // BẮT BUỘC: mặc định AutoComplete tự lọc lại theo `value` của từng option (tức tên trạm),
      // nên các gợi ý mà backend tìm ra nhờ ĐỊA CHỈ sẽ bị chính nó loại mất.
      filterOption={false}
      onSearch={setKeyword}
      onChange={(next: string) => onChange?.(next)}
      notFoundContent={notFoundContent}
      disabled={disabled}
      allowClear={allowClear}
      placeholder={placeholder}
      prefix={<EnvironmentOutlined />}
      style={{ width: '100%' }}
    />
  );
}
