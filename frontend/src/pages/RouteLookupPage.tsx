import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import {
  Button,
  Card,
  DatePicker,
  Form,
  Input,
  List,
  Space,
  Tag,
  Typography,
  message,
  theme,
} from 'antd';
import { EnvironmentOutlined, SearchOutlined } from '@ant-design/icons';
import dayjs, { type Dayjs } from 'dayjs';
import routeLookupApi from '../api/routeLookupApi';
import type { RouteLookupResult } from '../api/routeLookupApi';
import type { AppError } from '../api/axiosClient';
import { EmptyState, PageCard, PageHeader } from '../components/ui';
import { formatVnd } from '../components/ui/format';

const { Text } = Typography;

/** Giá trị của form tìm tuyến — `date` là Dayjs để DatePicker xử lý trực tiếp. */
interface SearchFormValues {
  origin: string;
  destination: string;
  date: Dayjs;
}

// Màn hình tra cứu tuyến (User Story 1): form "điểm đi - điểm đến - ngày" + kết quả tuyến.
// Đây là màn hình cho HÀNH KHÁCH — không giới hạn vai trò như các màn hình quản trị.
// Phần "Component autocomplete chọn trạm dừng" (Hoàng Văn Thịnh) là task riêng, nên ở đây
// điểm đi/điểm đến dùng Input thường và kết quả hiển thị gọn ở mức tuyến. Mỗi tuyến có nút
// "Xem chuyến" mở màn "Kết quả tìm kiếm" (Nguyễn Đình Băng) — mang theo routeId để màn đó
// gọi thẳng GET /trips/search (công khai, không cần đăng nhập).
export default function RouteLookupPage() {
  const [form] = Form.useForm<SearchFormValues>();
  const navigate = useNavigate();
  // Màu lấy từ token của theme chung — không viết hex tay trong màn hình.
  const { token } = theme.useToken();

  const [results, setResults] = useState<RouteLookupResult[]>([]);
  const [loading, setLoading] = useState(false);
  // Chưa từng tìm kiếm thì hiện lời mời nhập; đã tìm mà rỗng thì hiện "không tìm thấy".
  const [hasSearched, setHasSearched] = useState(false);

  // Tiêu chí của lần tìm gần nhất (đã chuẩn hoá về chuỗi) — nguyên liệu để mở màn
  // "Kết quả tìm kiếm" từ nút "Xem chuyến" trên từng tuyến.
  const [lastCriteria, setLastCriteria] = useState<{ destination: string; date: string } | null>(null);

  const handleSearch = async ({ origin, destination, date }: SearchFormValues) => {
    // Hai đầu mút giống nhau thì không có chuyến hợp lệ — chặn sớm thay vì trả kết quả rỗng.
    if (origin.trim().toLowerCase() === destination.trim().toLowerCase()) {
      message.warning('Điểm đi và điểm đến không được trùng nhau.');
      return;
    }

    setLoading(true);
    try {
      const found = await routeLookupApi.search({
        origin,
        destination,
        date: date.format('YYYY-MM-DD'),
      });
      setResults(found);
      setLastCriteria({ destination: destination.trim(), date: date.format('YYYY-MM-DD') });
      setHasSearched(true);
    } catch (error) {
      const appError = error as AppError;
      message.error(appError.customMessage || 'Không thể tra cứu tuyến.');
      setResults([]);
      setHasSearched(true);
    } finally {
      setLoading(false);
    }
  };

  // Mở màn "Kết quả tìm kiếm" cho một tuyến. Điểm đi/điểm đến lấy theo tuyến (đúng chuẩn
  // hiển thị), routeId để màn kết quả gọi thẳng GET /trips/search của tuyến đó.
  const handleViewTrips = (route: RouteLookupResult) => {
    const params = new URLSearchParams({
      origin: route.origin,
      destination: route.destination,
      routeId: route.routeId,
    });
    if (lastCriteria) params.set('date', lastCriteria.date);

    navigate(`/trip-results?${params.toString()}`);
  };

  return (
    <div>
      <PageHeader
        title="Tra cứu tuyến"
        subtitle="Nhập điểm đi, điểm đến và ngày đi để tìm tuyến xe buýt phù hợp."
      />

      <PageCard>
        <Form<SearchFormValues>
          form={form}
          layout="vertical"
          initialValues={{ date: dayjs() }}
          onFinish={handleSearch}
        >
          <Space size="large" wrap align="start" style={{ display: 'flex', width: '100%' }}>
            <Form.Item
              name="origin"
              label="Điểm đi"
              style={{ flex: 1, minWidth: 220 }}
              rules={[{ required: true, whitespace: true, message: 'Vui lòng nhập điểm đi.' }]}
            >
              <Input placeholder="Ví dụ: Bến xe Mỹ Đình" prefix={<EnvironmentOutlined />} />
            </Form.Item>

            <Form.Item
              name="destination"
              label="Điểm đến"
              style={{ flex: 1, minWidth: 220 }}
              rules={[{ required: true, whitespace: true, message: 'Vui lòng nhập điểm đến.' }]}
            >
              <Input placeholder="Ví dụ: Bến xe Gia Lâm" prefix={<EnvironmentOutlined />} />
            </Form.Item>

            <Form.Item name="date" label="Ngày đi" style={{ minWidth: 180 }}>
              <DatePicker style={{ width: '100%' }} format="DD/MM/YYYY" allowClear={false} />
            </Form.Item>

            <Form.Item label=" " style={{ marginBottom: 24 }}>
              <Button type="primary" htmlType="submit" icon={<SearchOutlined />} loading={loading}>
                Tìm tuyến
              </Button>
            </Form.Item>
          </Space>
        </Form>
      </PageCard>

      {hasSearched && !loading && results.length > 0 && (
        <div style={{ marginTop: 20 }}>
          <Text type="secondary" style={{ display: 'block', marginBottom: 12 }}>
            Tìm thấy <Text strong>{results.length}</Text> tuyến phù hợp
          </Text>
          <List<RouteLookupResult>
            dataSource={results}
            loading={loading}
            grid={{ gutter: 16, xs: 1, sm: 1, md: 2, lg: 2, xl: 3, xxl: 3 }}
            renderItem={(route) => (
              <List.Item>
                <Card
                  hoverable
                  style={{ borderRadius: 16, height: '100%' }}
                  title={
                    <Space>
                      <Tag color="blue">{route.routeCode}</Tag>
                      <Text strong>{route.routeName}</Text>
                    </Space>
                  }
                >
                  <Space direction="vertical" size={4} style={{ width: '100%' }}>
                    <Text type="secondary" style={{ fontSize: 12 }}>
                      {route.origin} → {route.destination}
                    </Text>
                    <Space size="large">
                      <Text style={{ fontSize: 13 }}>{route.distanceKm} km</Text>
                      <Text style={{ fontSize: 13 }}>{route.stops.length} trạm</Text>
                    </Space>
                    {route.minPrice !== null && (
                      <Text style={{ color: token.colorPrimary, fontWeight: 600, fontSize: 15 }}>
                        Giá từ {formatVnd(route.minPrice)}
                      </Text>
                    )}
                    <Button type="primary" block onClick={() => handleViewTrips(route)}>
                      Xem chuyến
                    </Button>
                  </Space>
                </Card>
              </List.Item>
            )}
          />
        </div>
      )}

      {hasSearched && !loading && results.length === 0 && (
        <div style={{ marginTop: 40 }}>
          <EmptyState description="Không tìm thấy tuyến phù hợp. Vui lòng thử điểm đi/điểm đến khác." />
        </div>
      )}

      {!hasSearched && (
        <div style={{ marginTop: 40 }}>
          <EmptyState description="Nhập điểm đi, điểm đến và ngày đi rồi bấm “Tìm tuyến”." />
        </div>
      )}
    </div>
  );
}
