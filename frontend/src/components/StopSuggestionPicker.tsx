import { useEffect, useState } from 'react';
import { Select, Space, Tag, Typography } from 'antd';
import type { SelectProps } from 'antd';
import { EnvironmentOutlined } from '@ant-design/icons';
import stopSuggestionsApi from '../api/stopSuggestionsApi';
import type { StopSuggestion } from '../api/stopSuggestionsApi';

const { Text } = Typography;

// -----------------------------------------------------------------------------
// Ô chọn trạm cho màn Tra cứu tuyến — chỉ gợi ý trạm CÓ TUYẾN ĐANG KHAI THÁC.
//
// Vì sao không dùng lại components/StopAutocomplete.tsx: ô đó gọi GET /stops/search, endpoint
// trả MỌI trạm trong CSDL, kể cả trạm mồ côi của tuyến đã xoá. Đây chính là lỗi hành khách báo:
// "gợi ý ra toàn tuyến tôi đã xoá trong cơ sở dữ liệu". Endpoint /stops/suggestions lọc theo
// RouteStatus.Active trước rồi mới tìm, nên trạm không còn tuyến nào chạy qua không bao giờ hiện.
//
// Khác biệt thứ hai: ô này phát ra CẢ id VÀ tên trạm ({ id, name }), không chỉ tên. Id là thứ
// /routes/suggestions nhận (originStopId/destinationStopId) — nhờ vậy backend không phải đoán
// lại trạm từ chuỗi tên, và màn hình không còn phụ thuộc vào việc hành khách gõ đúng dấu.
//
// Lọc chéo hai đầu: truyền `originStopId` cho ô điểm đến thì chỉ trạm đứng sau điểm đi được gợi
// ý; truyền `destinationStopId` cho ô điểm đi thì chỉ trạm đứng trước điểm đến. Người dùng không
// phải cuộn qua một nửa danh sách trạm chạy ngược chiều.
//
// Vẫn là Select có `showSearch` + gõ tự do để lọc — hành khách gõ "tan long" ra "Phường Tân
// Long" (backend bỏ dấu), chọn một dòng là xong, không cần gõ đúng chính tả.
// -----------------------------------------------------------------------------

/** Chờ người dùng ngừng gõ rồi mới hỏi backend — một request cho một cụm gõ. */
const DEBOUNCE_MS = 300;

/** Giá trị phát ra ngoài — đủ để gọi /routes/suggestions bằng id, đủ để hiển thị bằng tên. */
export interface SelectedStop {
  id: string;
  name: string;
}

interface StopSuggestionPickerProps {
  /** Trạm đã chọn. Khớp hợp đồng value/onChange để cắm thẳng vào Form.Item. */
  value?: SelectedStop | null;
  onChange?: (stop: SelectedStop | null) => void;
  /** Điểm đi đã chọn — chỉ gợi ý trạm đứng sau nó (dùng cho ô điểm đến). */
  originStopId?: string;
  /** Điểm đến đã chọn — chỉ gợi ý trạm đứng trước nó (dùng cho ô điểm đi). */
  destinationStopId?: string;
  placeholder?: string;
  disabled?: boolean;
  /** Form.Item truyền vào để `<label htmlFor>` trỏ đúng ô nhập. */
  id?: string;
}

export default function StopSuggestionPicker({
  value,
  onChange,
  originStopId,
  destinationStopId,
  placeholder,
  disabled,
  id,
}: StopSuggestionPickerProps) {
  const [stops, setStops] = useState<StopSuggestion[]>([]);
  // Khởi tạo `true`: lượt hỏi đầu tiên còn nằm trong cửa sổ debounce, lúc đó "đang tải" mới đúng.
  const [loading, setLoading] = useState(true);
  const [failed, setFailed] = useState(false);
  // Chỉ hỏi backend khi dropdown thật sự mở: màn tra cứu có HAI ô trạm, nạp sẵn cả hai ngay khi
  // vào trang là hai truy vấn không ai dùng. Supabase của nhóm còn giới hạn 15 kết nối phiên.
  const [opened, setOpened] = useState(false);
  // Từ khoá đang gõ, tách khỏi `value`: chọn xong một trạm thì xoá từ khoá để lần mở sau thấy
  // lại danh sách đầy đủ, không bị lọc đúng bằng tên vừa chọn.
  const [keyword, setKeyword] = useState('');

  useEffect(() => {
    if (!opened) return;

    let cancelled = false;
    const timer = setTimeout(() => {
      // Đổi trạng thái bên trong hẹn giờ: cửa sổ debounce giữ nguyên danh sách cũ trên màn hình
      // (không nhấp nháy), và effect không setState đồng bộ.
      setLoading(true);
      setFailed(false);

      stopSuggestionsApi
        .search({ keyword, limit: 20, originStopId, destinationStopId })
        .then((list) => {
          if (!cancelled) setStops(list);
        })
        .catch(() => {
          // Gợi ý hỏng không được chặn luồng chính — chỉ đổi lời nhắn trong dropdown.
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
  }, [opened, keyword, originStopId, destinationStopId]);

  // Backend đã xếp hạng sẵn (tên-đầu → tên-chứa → địa-chỉ) nên ở đây chỉ còn việc bày ra.
  const options: SelectProps['options'] = stops.map((stop) => ({
    value: stop.id,
    // `optionLabelProp="title"` khiến ô đã chọn hiện đúng tên trạm thay vì cả khối JSX.
    title: stop.name,
    label: (
      <div style={{ padding: '2px 0' }}>
        <Space size={6} wrap>
          <Text strong>{stop.name}</Text>
          {stop.routes.map((route) => (
            <Tag key={route.routeId} color="blue" style={{ marginInlineEnd: 0 }}>
              {route.routeCode}
            </Tag>
          ))}
        </Space>
        <div>
          <Text type="secondary" style={{ fontSize: 12 }}>
            {stop.address}
          </Text>
        </div>
      </div>
    ),
  }));

  // Giá trị đang chọn có thể không nằm trong lượt gợi ý hiện tại (vừa đổi từ khoá, hoặc trạm
  // được điền sẵn từ "tuyến gần đây"). Chèn lại một dòng để Select không hiện id trần.
  if (value && !stops.some((stop) => stop.id === value.id)) {
    options.unshift({ value: value.id, title: value.name, label: value.name });
  }

  const handleChange = (nextId?: string) => {
    if (!nextId) {
      setKeyword('');
      onChange?.(null);
      return;
    }

    const picked = stops.find((stop) => stop.id === nextId);
    const next: SelectedStop | null = picked
      ? { id: picked.id, name: picked.name }
      : value?.id === nextId
        ? value
        : null;

    setKeyword('');
    onChange?.(next);
  };

  const notFoundContent = (() => {
    if (loading) return 'Đang tìm trạm…';
    if (failed) return 'Không tải được gợi ý trạm — thử lại sau.';
    return 'Không có trạm nào đang có tuyến khai thác khớp từ khoá.';
  })();

  return (
    <Select
      id={id}
      showSearch
      // BẮT BUỘC: mặc định Select tự lọc lại theo label của từng option, nên các trạm mà backend
      // tìm ra nhờ ĐỊA CHỈ sẽ bị chính nó loại mất.
      filterOption={false}
      value={value?.id}
      options={options}
      optionLabelProp="title"
      onSearch={setKeyword}
      onOpenChange={setOpened}
      onChange={handleChange}
      onClear={() => setKeyword('')}
      notFoundContent={notFoundContent}
      suffixIcon={<EnvironmentOutlined />}
      disabled={disabled}
      allowClear
      placeholder={placeholder}
      style={{ width: '100%' }}
    />
  );
}
