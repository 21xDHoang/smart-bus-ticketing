import { useEffect, useMemo, useState } from 'react';
import { AutoComplete, Space, Typography } from 'antd';
import { EnvironmentOutlined } from '@ant-design/icons';
import stopApi from '../api/stopApi';
import type { Stop } from '../api/stopApi';
import { suggestStops } from './stopSuggest';

const { Text } = Typography;

// -----------------------------------------------------------------------------
// Ô nhập có gợi ý trạm dừng — task "Component autocomplete chọn trạm dừng"
// (Sprint 2 dòng 33, US 1.0 "Tra cứu tuyến xe", Hoàng Văn Thịnh).
//
// Dùng cho hai ô "Điểm đi" / "Điểm đến" của màn Tra cứu tuyến
// (pages/RouteLookupPage.tsx — màn đó hiện dùng Input thường và ghi chú sẵn rằng component
// này là task riêng, xem chú thích tại dòng 38 của file đó).
//
// GIÁ TRỊ PHÁT RA LÀ TÊN TRẠM (chuỗi), không phải id: endpoint tra cứu tuyến nhận điểm đi/điểm
// đến là chuỗi tự do rồi tự giải từ khoá ra trạm theo TÊN (xem RouteSearchService của backend).
// Trả về id sẽ buộc phải đổi hình dạng API — việc mà quy ước cấm tự làm.
//
// KHÔNG CHẶN NGƯỜI DÙNG: đây là AutoComplete chứ không phải Select, nên gõ tay giá trị bất kỳ
// vẫn gửi được như trước. Gợi ý chỉ là lớp cộng thêm. Điều này là bắt buộc chứ không phải cho
// đẹp: GET /stops gắn [Authorize(Policy = ManagerOrAbove)], nên khách chưa đăng nhập sẽ nhận
// 403 và không có gợi ý nào — nếu là Select thì ô nhập thành không điền được và cả form chết.
//
// Nạp trọn danh sách trạm MỘT lần rồi lọc tại chỗ (lý do ở đầu components/stopSuggest.ts).
// -----------------------------------------------------------------------------

/**
 * Promise dùng chung cho cả phiên, ở mức module.
 *
 * Giữ Promise chứ không giữ mảng: màn Tra cứu tuyến có tới hai ô cùng cần danh sách này, và cả
 * hai mount gần như cùng lúc — giữ Promise thì lượt gọi thứ hai bám vào lượt đang bay, chỉ tốn
 * một request. Danh sách trạm là dữ liệu gần như không đổi trong một phiên làm việc.
 */
let stopsPromise: Promise<Stop[]> | null = null;

function loadStops(): Promise<Stop[]> {
  if (!stopsPromise) {
    stopsPromise = stopApi.list().catch((error: unknown) => {
      // Xoá Promise hỏng để lần gõ sau còn thử lại. Giữ lại nó là khoá tính năng gợi ý cho tới
      // khi tải lại cả trang — hỏng mạng một nhịp mà mất luôn tính năng thì quá đắt.
      stopsPromise = null;
      throw error;
    });
  }

  return stopsPromise;
}

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
  const [loading, setLoading] = useState(true);
  const [failed, setFailed] = useState(false);
  // Từ khoá đang gõ. Cố ý KHÔNG suy từ `value`: người dùng gõ dở dang thì `value` đã đổi theo
  // từng ký tự, nhưng lúc họ chọn một gợi ý thì gợi ý phải biến mất ngay chứ không lọc lại
  // theo đúng tên trạm vừa chọn rồi hiện ra một dòng lẻ.
  const [keyword, setKeyword] = useState('');

  useEffect(() => {
    let cancelled = false;

    loadStops()
      .then((list) => {
        if (!cancelled) setStops(list);
      })
      .catch(() => {
        if (!cancelled) setFailed(true);
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });

    return () => {
      cancelled = true;
    };
  }, []);

  const options = useMemo(
    () =>
      suggestStops(stops, keyword).map((stop) => ({
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
      })),
    [stops, keyword],
  );

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
      // nên các gợi ý khớp qua ĐỊA CHỈ do suggestStops tìm ra sẽ bị chính nó loại mất.
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
