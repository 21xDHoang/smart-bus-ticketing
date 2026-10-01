import { useEffect, useMemo, useState } from 'react';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { Button, Card, Empty, List, Segmented, Space, Tag, Typography } from 'antd';
import { ArrowLeftOutlined, CarOutlined, EnvironmentOutlined } from '@ant-design/icons';
import dayjs from 'dayjs';
import tripSearchApi from '../api/tripSearchApi';
import type { TripSearchResult } from '../api/tripSearchApi';

const { Title, Text } = Typography;

/** Khoá sắp xếp — "giờ" mặc định là giờ đi sớm nhất, "giá" có cả hai chiều. */
type SortKey = 'time' | 'priceAsc' | 'priceDesc';

const SORT_OPTIONS: { label: string; value: SortKey }[] = [
  { label: 'Giờ đi sớm nhất', value: 'time' },
  { label: 'Giá thấp → cao', value: 'priceAsc' },
  { label: 'Giá cao → thấp', value: 'priceDesc' },
];

/** Định dạng tiền VND — ví dụ 8000 → "8.000 đ". */
function formatVnd(price: number): string {
  return `${price.toLocaleString('vi-VN')} đ`;
}

/** Nhãn + màu cho số ghế còn trống — càng ít ghế màu càng gắt. */
function seatsMeta(seatsRemaining: number): { label: string; color: string } {
  if (seatsRemaining === 0) return { label: 'Hết chỗ', color: 'red' };
  if (seatsRemaining <= 5) return { label: `Còn ${seatsRemaining} ghế`, color: 'orange' };
  return { label: `Còn ${seatsRemaining} ghế`, color: 'green' };
}

/** Sắp xếp một bản sao của kết quả theo khoá đang chọn, không đổi mảng gốc. */
function sortResults(results: TripSearchResult[], sortKey: SortKey): TripSearchResult[] {
  const copy = [...results];

  if (sortKey === 'priceAsc') {
    copy.sort((a, b) => a.price - b.price);
  } else if (sortKey === 'priceDesc') {
    copy.sort((a, b) => b.price - a.price);
  } else {
    // Cùng ngày, cùng múi giờ (+07:00) nên so chuỗi ISO là so đúng thứ tự giờ.
    copy.sort((a, b) => a.departureTime.localeCompare(b.departureTime));
  }

  return copy;
}

// Màn hình kết quả tìm kiếm chuyến (User Story 1, Sprint 2 — task của Nguyễn Đình Băng):
// danh sách chuyến cho một cặp điểm đi/điểm đến + ngày, kèm bộ sắp xếp theo giờ/giá.
//
// Nhận tiêu chí qua query string (origin, destination, date, routeId) — màn hình này là
// "phần kết quả", còn form nhập điểm đi/điểm đến/ngày là task riêng của màn hình "Tra cứu
// tuyến" (RouteLookupPage, Dương Thị Hạnh). Không có tiêu chí thì mời quay lại đó, không
// dựng form thứ hai để tránh đè lên task của Hạnh.
export default function TripSearchResultPage() {
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();

  const origin = searchParams.get('origin')?.trim() ?? '';
  const destination = searchParams.get('destination')?.trim() ?? '';
  const date = searchParams.get('date')?.trim() ?? '';
  const routeId = searchParams.get('routeId')?.trim() || undefined;

  const [results, setResults] = useState<TripSearchResult[]>([]);
  const [loading, setLoading] = useState(false);
  const [sortKey, setSortKey] = useState<SortKey>('time');

  const hasCriteria = origin !== '' && destination !== '';

  useEffect(() => {
    if (!hasCriteria) return;

    let cancelled = false;

    // Bật spinner khi bắt đầu tải. Đây là lần tải thực sự từ API (không phải "đồng bộ
    // state dẫn xuất") nên tắt cảnh báo react/set-state-in-effect cho đúng ngữ cảnh.
    // oxlint-disable-next-line react/set-state-in-effect
    setLoading(true);

    tripSearchApi
      .search({ origin, destination, date, routeId })
      .then((found) => {
        if (cancelled) return;
        setResults(found);
      })
      .catch(() => {
        if (cancelled) return;
        setResults([]);
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });

    return () => {
      cancelled = true;
    };
  }, [origin, destination, date, routeId, hasCriteria]);

  const sorted = useMemo(() => sortResults(results, sortKey), [results, sortKey]);

  // Chưa có tiêu chí tìm kiếm → mời quay lại màn hình tra cứu tuyến, không hiển thị kết quả.
  if (!hasCriteria) {
    return (
      <Empty
        style={{ marginTop: 40 }}
        description="Chưa có thông tin tìm kiếm. Hãy nhập điểm đi, điểm đến và ngày đi để xem các chuyến phù hợp."
      >
        <Button type="primary" icon={<ArrowLeftOutlined />} onClick={() => navigate('/route-lookup')}>
          Quay lại tra cứu tuyến
        </Button>
      </Empty>
    );
  }

  return (
    <div>
      <div style={{ marginBottom: 16 }}>
        <Space align="start" size={8}>
          <Button
            type="text"
            icon={<ArrowLeftOutlined />}
            onClick={() => navigate('/route-lookup')}
            aria-label="Quay lại tra cứu tuyến"
            style={{ marginTop: 2 }}
          />
          <div>
            <Title level={4} style={{ margin: 0 }}>
              Kết quả tìm kiếm chuyến
            </Title>
            <Text type="secondary">
              <EnvironmentOutlined /> {origin} → {destination}
              {date && ` · ${dayjs(date).format('DD/MM/YYYY')}`}
            </Text>
          </div>
        </Space>
      </div>

      <Card variant="borderless" style={{ borderRadius: 16, boxShadow: '0 4px 12px rgba(0,0,0,0.03)' }}>
        <Space
          style={{ width: '100%', justifyContent: 'space-between', marginBottom: 16 }}
          wrap
        >
          <Text type="secondary">
            Tìm thấy <Text strong>{results.length}</Text> chuyến
          </Text>
          <Segmented
            value={sortKey}
            onChange={(value) => setSortKey(value as SortKey)}
            options={SORT_OPTIONS}
          />
        </Space>

        <List<TripSearchResult>
          dataSource={sorted}
          loading={loading}
          locale={{
            emptyText: 'Không tìm thấy chuyến phù hợp. Vui lòng thử điểm đi/điểm đến hoặc ngày khác.',
          }}
          renderItem={(trip) => {
            const seats = seatsMeta(trip.seatsRemaining);

            return (
              <List.Item style={{ padding: '14px 0', borderBlockEnd: '1px solid #f1f5f9' }}>
                <div
                  style={{
                    display: 'flex',
                    alignItems: 'center',
                    gap: 16,
                    width: '100%',
                    flexWrap: 'wrap',
                  }}
                >
                  {/* Giờ khởi hành → giờ đến */}
                  <div style={{ minWidth: 96 }}>
                    <div style={{ fontSize: 18, fontWeight: 700, color: '#1e293b' }}>
                      {dayjs(trip.departureTime).format('HH:mm')}
                    </div>
                    <Text type="secondary" style={{ fontSize: 13 }}>
                      {trip.arrivalTime ? dayjs(trip.arrivalTime).format('HH:mm') : '—'}
                    </Text>
                  </div>

                  {/* Tuyến + loại xe */}
                  <div style={{ flex: 1, minWidth: 220 }}>
                    <Space size={8} wrap>
                      <Tag color="blue">{trip.routeCode}</Tag>
                      <Text strong>{trip.routeName}</Text>
                    </Space>
                    <div>
                      <Text type="secondary" style={{ fontSize: 12 }}>
                        <CarOutlined /> {trip.busType}
                      </Text>
                    </div>
                  </div>

                  {/* Giá + số ghế còn trống */}
                  <div style={{ textAlign: 'right', minWidth: 128 }}>
                    <div style={{ fontSize: 16, fontWeight: 700, color: '#4361ee' }}>
                      {formatVnd(trip.price)}
                    </div>
                    <Tag color={seats.color} style={{ marginTop: 4, marginInlineEnd: 0 }}>
                      {seats.label}
                    </Tag>
                  </div>
                </div>
              </List.Item>
            );
          }}
        />
      </Card>
    </div>
  );
}
