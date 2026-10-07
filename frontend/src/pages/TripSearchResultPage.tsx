import { useEffect, useMemo, useState } from 'react';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { Button, Card, Empty, List, Result, Segmented, Space, Spin, Tag, Typography } from 'antd';
import {
  ArrowLeftOutlined,
  CarOutlined,
  EnvironmentOutlined,
  ReloadOutlined,
} from '@ant-design/icons';
import dayjs from 'dayjs';
import tripSearchApi from '../api/tripSearchApi';
import type { TripSearchResult } from '../api/tripSearchApi';
import type { AppError } from '../api/axiosClient';

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

/**
 * So sánh theo giá — chuyến CHƯA có giá (`price` null) luôn xếp cuối ở cả hai chiều,
 * không bị coi là 0 đồng. `direction` 1 = thấp trước, -1 = cao trước.
 */
function comparePrice(a: number | null, b: number | null, direction: 1 | -1): number {
  if (a === null && b === null) return 0;
  if (a === null) return 1;
  if (b === null) return -1;
  return (a - b) * direction;
}

/** Sắp xếp một bản sao của kết quả theo khoá đang chọn, không đổi mảng gốc. */
function sortResults(results: TripSearchResult[], sortKey: SortKey): TripSearchResult[] {
  const copy = [...results];

  if (sortKey === 'priceAsc') {
    copy.sort((a, b) => comparePrice(a.price, b.price, 1));
  } else if (sortKey === 'priceDesc') {
    copy.sort((a, b) => comparePrice(a.price, b.price, -1));
  } else {
    // Cùng ngày, cùng múi giờ (+07:00) nên so chuỗi ISO là so đúng thứ tự giờ.
    copy.sort((a, b) => a.departureTime.localeCompare(b.departureTime));
  }

  return copy;
}

/**
 * Kết quả của MỘT lượt gọi API, gắn kèm bộ tiêu chí đã hỏi (`key`). Nhờ cặp khoá–giá trị này
 * mà đổi tiêu chí là kết quả cũ tự hết giá trị: không cần xoá state, cũng không có đường nào
 * để danh sách của lượt trước hiện dưới spinner của lượt sau.
 */
type SearchOutcome =
  | { status: 'loading' }
  | { status: 'error'; message: string }
  | { status: 'done'; results: TripSearchResult[] };

/** Bốn trạng thái hiển thị của màn kết quả — đúng bốn nhánh, không nhánh nào kiêm nhánh nào. */
type ResultView =
  | { kind: 'loading' }
  | { kind: 'error'; message: string }
  | { kind: 'no-match' }
  | { kind: 'results'; trips: TripSearchResult[] };

/** Mảng rỗng dùng chung — giữ nguyên tham chiếu để `useMemo` bên dưới không chạy lại vô ích. */
const NO_TRIPS: TripSearchResult[] = [];

/**
 * Luật chuyển trạng thái, tách khỏi JSX để bốn nhánh không lồng vào nhau.
 *
 * `done` mà không có chuyến nào là "không tìm thấy" — im lặng, không phải lỗi. Còn gọi API
 * hỏng là trạng thái RIÊNG: trước đây lượt gọi hỏng cũng đổ về `[]` nên người dùng đọc được
 * câu "Không tìm thấy chuyến phù hợp", tức là màn hình nói sai sự thật.
 */
function viewOf(outcome: SearchOutcome | null): ResultView {
  if (outcome === null || outcome.status === 'loading') return { kind: 'loading' };
  if (outcome.status === 'error') return { kind: 'error', message: outcome.message };
  return outcome.results.length === 0
    ? { kind: 'no-match' }
    : { kind: 'results', trips: outcome.results };
}

// Màn hình kết quả tìm kiếm chuyến (User Story 1, Sprint 2 — task của Nguyễn Đình Băng):
// danh sách chuyến cho một cặp điểm đi/điểm đến + ngày, kèm bộ sắp xếp theo giờ/giá.
//
// Nhận tiêu chí qua query string (origin, destination, date, routeId) — màn hình này là
// "phần kết quả", còn form nhập điểm đi/điểm đến/ngày là task riêng của màn hình "Tra cứu
// tuyến" (RouteLookupPage, Dương Thị Hạnh). Không có tiêu chí thì mời quay lại đó, không
// dựng form thứ hai để tránh đè lên task của Hạnh.
//
// Bốn trạng thái rỗng / đang tải / không tìm thấy / lỗi là task Sprint 2 dòng 35 — Hoàng Văn
// Thịnh (một dòng backlog riêng, tách khỏi dòng 34 dựng màn hình của Băng). Phần dưới đây là
// chỗ duy nhất trong repo mà Hoàng Văn Thịnh sửa vào file của người khác; đã báo Băng xem
// trong PR.
export default function TripSearchResultPage() {
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();

  const origin = searchParams.get('origin')?.trim() ?? '';
  const destination = searchParams.get('destination')?.trim() ?? '';
  const date = searchParams.get('date')?.trim() ?? '';
  const routeId = searchParams.get('routeId')?.trim() || undefined;

  const [outcome, setOutcome] = useState<{ key: string; value: SearchOutcome } | null>(null);
  const [sortKey, setSortKey] = useState<SortKey>('time');
  const [reloadToken, setReloadToken] = useState(0);

  const hasCriteria = origin !== '' && destination !== '';

  // Bộ tiêu chí hiện tại, làm khoá cho kết quả. JSON.stringify để hai bộ khác nhau không thể
  // ghép ra cùng một chuỗi (dấu "|" hay "," đều có thể nằm trong tên điểm đi/điểm đến).
  const requestKey = JSON.stringify([origin, destination, date, routeId ?? '']);

  // Kết quả chỉ được coi là của lượt hỏi hiện tại khi khoá trùng; lệch khoá = chưa có gì.
  const currentOutcome = outcome?.key === requestKey ? outcome.value : null;
  const view = viewOf(currentOutcome);
  const trips = view.kind === 'results' ? view.trips : NO_TRIPS;

  useEffect(() => {
    if (!hasCriteria) return;

    let cancelled = false;

    // Bật trạng thái đang tải khi bắt đầu gọi API. Đây là lần tải thực sự (không phải "đồng
    // bộ state dẫn xuất") nên tắt cảnh báo react/set-state-in-effect cho đúng ngữ cảnh.
    // oxlint-disable-next-line react/set-state-in-effect
    setOutcome({ key: requestKey, value: { status: 'loading' } });

    tripSearchApi
      .search({ origin, destination, date, routeId })
      .then((found) => {
        if (cancelled) return;
        setOutcome({ key: requestKey, value: { status: 'done', results: found } });
      })
      .catch((err: unknown) => {
        if (cancelled) return;
        const appError = err as AppError;
        setOutcome({
          key: requestKey,
          value: {
            status: 'error',
            message: appError.customMessage || 'Không thể tải kết quả tìm kiếm chuyến.',
          },
        });
      });

    return () => {
      cancelled = true;
    };
  }, [origin, destination, date, routeId, hasCriteria, requestKey, reloadToken]);

  const sorted = useMemo(() => sortResults(trips, sortKey), [trips, sortKey]);

  // Sang màn hình sơ đồ ghế (US 2, Sprint 3) cho một chuyến — truyền kèm ngữ cảnh để đầu
  // trang hiển thị tuyến/giờ/loại xe mà không phải gọi thêm endpoint chi tiết chuyến.
  const openSeatMap = (trip: TripSearchResult) => {
    const params = new URLSearchParams({ tripId: trip.id });
    params.set('routeName', trip.routeName);
    params.set('departureTime', trip.departureTime);
    params.set('busType', trip.busType);

    navigate(`/seat-map?${params.toString()}`);
  };

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
            {view.kind === 'results' && (
              <>
                Tìm thấy <Text strong>{view.trips.length}</Text> chuyến
              </>
            )}
          </Text>
          <Segmented
            value={sortKey}
            onChange={(value) => setSortKey(value as SortKey)}
            options={SORT_OPTIONS}
            disabled={view.kind !== 'results'}
          />
        </Space>

        {/* Đang tải — spinner riêng, không mượn danh sách của lượt trước và không hiện số đếm. */}
        {view.kind === 'loading' && (
          <div style={{ padding: '56px 0', textAlign: 'center' }}>
            <Spin size="large" />
            <div style={{ marginTop: 12 }}>
              <Text type="secondary">Đang tải danh sách chuyến…</Text>
            </div>
          </div>
        )}

        {/* Gọi API hỏng — trạng thái riêng, có lối thoát, KHÔNG mạo nhận là "không có chuyến". */}
        {view.kind === 'error' && (
          <Result
            status="warning"
            title="Không tải được kết quả tìm kiếm"
            subTitle={view.message}
            extra={
              <Button icon={<ReloadOutlined />} onClick={() => setReloadToken((n) => n + 1)}>
                Thử lại
              </Button>
            }
          />
        )}

        {/* Đã tìm xong nhưng không có chuyến nào khớp. */}
        {view.kind === 'no-match' && (
          <Empty description="Không tìm thấy chuyến phù hợp. Vui lòng thử điểm đi/điểm đến hoặc ngày khác." />
        )}

        {view.kind === 'results' && (
          <List<TripSearchResult>
            dataSource={sorted}
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

                    {/* Giá + số ghế còn trống — tuyến chưa cấu hình giá thì ghi rõ thay vì hiện 0 đ */}
                    <div style={{ textAlign: 'right', minWidth: 128 }}>
                      {trip.price === null ? (
                        <Text type="secondary" style={{ fontWeight: 600 }}>
                          Chưa có giá
                        </Text>
                      ) : (
                        <div style={{ fontSize: 16, fontWeight: 700, color: '#4361ee' }}>
                          {formatVnd(trip.price)}
                        </div>
                      )}
                      <Tag color={seats.color} style={{ marginTop: 4, marginInlineEnd: 0 }}>
                        {seats.label}
                      </Tag>
                    </div>

                    {/* Vào màn hình chọn ghế — hết chỗ thì khoá nút. */}
                    <Button
                      type="primary"
                      disabled={trip.seatsRemaining === 0}
                      onClick={() => openSeatMap(trip)}
                    >
                      Chọn ghế
                    </Button>
                  </div>
                </List.Item>
              );
            }}
          />
        )}
      </Card>
    </div>
  );
}
