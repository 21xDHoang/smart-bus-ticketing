import { useEffect, useState } from 'react';
import { useLocation, useNavigate } from 'react-router-dom';
import {
  Alert,
  Button,
  Card,
  DatePicker,
  Form,
  List,
  Space,
  Tag,
  Typography,
  message,
  theme,
} from 'antd';
import { SearchOutlined, SwapOutlined } from '@ant-design/icons';
import dayjs, { type Dayjs } from 'dayjs';
import routeSuggestionsApi from '../api/routeSuggestionsApi';
import type { RouteSuggestion, RouteSuggestionsResponse } from '../api/routeSuggestionsApi';
import stopSuggestionsApi from '../api/stopSuggestionsApi';
import type { StopSuggestion } from '../api/stopSuggestionsApi';
import type { AppError } from '../api/axiosClient';
import StopSuggestionPicker from '../components/StopSuggestionPicker';
import type { SelectedStop } from '../components/StopSuggestionPicker';
import { EmptyState, PageCard, PageHeader, RouteBadge } from '../components/ui';
import { formatVnd } from '../components/ui/format';
import { rememberRecentRoute } from '../components/ui/recentRoutes';

const { Text } = Typography;

const ISO_DATE = 'YYYY-MM-DD';

/** Giá trị của form tìm tuyến — `date` là Dayjs để DatePicker xử lý trực tiếp. */
interface SearchFormValues {
  origin: SelectedStop | null;
  destination: SelectedStop | null;
  date: Dayjs;
}

/** Dữ liệu điều hướng do trang chủ gửi sang (chip "tuyến gần đây") — có thể thiếu hoặc sai kiểu. */
interface RouteLookupPrefill {
  origin?: string;
  destination?: string;
}

function readPrefill(state: unknown): RouteLookupPrefill {
  if (typeof state !== 'object' || state === null) return {};
  const value = state as Record<string, unknown>;
  return {
    origin: typeof value.origin === 'string' ? value.origin : undefined,
    destination: typeof value.destination === 'string' ? value.destination : undefined,
  };
}

function formatDay(iso: string | null | undefined): string {
  return iso ? dayjs(iso).format('DD/MM/YYYY') : '';
}

function formatDayShort(iso: string): string {
  return dayjs(iso).format('DD/MM');
}

function formatTime(iso: string | null | undefined): string {
  return iso ? dayjs(iso).format('HH:mm') : '';
}

/** Cùng thuật toán bỏ dấu với backend — chỉ dùng để nhận ra trạm được điền sẵn từ "tuyến gần đây". */
function normalizeName(text: string): string {
  return text
    .toLowerCase()
    .normalize('NFD')
    .replace(/[̀-ͯ]/g, '')
    .replace(/đ/g, 'd')
    .trim();
}

function pickStop(stops: StopSuggestion[], name: string): SelectedStop | null {
  const needle = normalizeName(name);
  const found = stops.find((stop) => normalizeName(stop.name) === needle) ?? stops[0];

  return found ? { id: found.id, name: found.name } : null;
}

/**
 * Dòng tuyến trong kết quả chính. Ngày không còn quyết định tuyến có hiện hay không: tuyến nối
 * được hai trạm luôn hiện, kèm nhãn "Không có chuyến ngày…" và ngày gần nhất có chuyến — đúng
 * yêu cầu "nếu ngày đó không có tuyến thì gợi ý tuyến gần nhất".
 */
function RouteSuggestionCard({
  route,
  selectedDate,
  onViewTrips,
  onPickDate,
}: {
  route: RouteSuggestion;
  selectedDate: string;
  onViewTrips: (route: RouteSuggestion, date: string) => void;
  onPickDate: (date: string) => void;
}) {
  const { token } = theme.useToken();
  const dateLabel = formatDay(selectedDate);
  const nearestLabel = formatDay(route.nearestAvailableDate);
  const departureTime = formatTime(route.nextDeparture);

  return (
    <Card
      hoverable
      style={{ borderRadius: 16, height: '100%' }}
      title={
        <Space wrap>
          <RouteBadge code={route.routeCode} />
          <Text strong>{route.routeName}</Text>
          {route.runsDaily && (
            <Tag color="green" style={{ marginInlineEnd: 0 }}>
              Chạy hằng ngày
            </Tag>
          )}
        </Space>
      }
    >
      <Space orientation="vertical" size={6} style={{ width: '100%' }}>
        <Text type="secondary" style={{ fontSize: 12 }}>
          {route.origin} → {route.destination}
        </Text>
        <Space size="large">
          <Text style={{ fontSize: 13 }}>{route.distanceKm} km</Text>
          <Text style={{ fontSize: 13 }}>{route.stops.length} trạm</Text>
        </Space>
        {route.minPrice !== null && (
          // colorPrimaryText (không phải colorPrimary) — màu primary làm CHỮ trên nền tối chỉ đạt
          // ~2.85:1; token *Text là biến thể đã tính đủ tương phản hai chế độ.
          <Text style={{ color: token.colorPrimaryText, fontWeight: 600, fontSize: 15 }}>
            Giá từ {formatVnd(route.minPrice)}
          </Text>
        )}

        {route.hasTripsOnDate ? (
          <Text type="success" style={{ fontSize: 13 }}>
            Có chuyến ngày {dateLabel}
            {departureTime ? ` · sớm nhất ${departureTime}` : ''}
            {route.departureCountOnNearestDate > 0
              ? ` · ${route.departureCountOnNearestDate} chuyến`
              : ''}
          </Text>
        ) : route.nearestAvailableDate ? (
          <>
            <Tag color="orange" style={{ whiteSpace: 'normal', marginInlineEnd: 0 }}>
              Không có chuyến ngày {dateLabel}
            </Tag>
            <Text type="secondary" style={{ fontSize: 12 }}>
              Gần nhất: <Text strong>{nearestLabel}</Text>
              {departureTime ? ` · ${departureTime}` : ''}
              {route.departureCountOnNearestDate > 0
                ? ` · ${route.departureCountOnNearestDate} chuyến`
                : ''}
            </Text>
            {route.availableDates.length > 0 && (
              // Ngày nào cũng chạy thì `runsDaily` đã nói thay; ở đây chỉ bày các ngày có chuyến
              // trong cửa sổ bán vé để chọn nhanh, không bắt hành khách mở DatePicker.
              <Space size={4} wrap>
                <Text type="secondary" style={{ fontSize: 12 }}>
                  Chọn nhanh:
                </Text>
                {route.availableDates.slice(0, 7).map((day) => (
                  <Tag
                    key={day}
                    style={{ cursor: 'pointer', marginInlineEnd: 0 }}
                    onClick={() => onPickDate(day)}
                  >
                    {formatDayShort(day)}
                  </Tag>
                ))}
              </Space>
            )}
          </>
        ) : (
          <Text type="secondary" style={{ fontSize: 13 }}>
            Chưa có lịch chạy trong 7 ngày tới.
          </Text>
        )}

        {/* Nút này KHÔNG dùng type="primary": màn chỉ có một hành động nổi bật là "Tìm tuyến";
            để primary ở đây sẽ thành mỗi thẻ một nút xanh tranh nhau. */}
        <Button
          block
          disabled={!route.hasTripsOnDate && !route.nearestAvailableDate}
          onClick={() =>
            onViewTrips(
              route,
              route.hasTripsOnDate ? selectedDate : (route.nearestAvailableDate as string),
            )
          }
        >
          {route.hasTripsOnDate
            ? 'Xem chuyến'
            : route.nearestAvailableDate
              ? `Xem chuyến ngày ${nearestLabel}`
              : 'Chưa có lịch chạy'}
        </Button>
      </Space>
    </Card>
  );
}

/** Tuyến chỉ đi qua một đầu — gợi ý để hành khách ghép chặng, không phải tuyến nối trực tiếp. */
function AlternativeRouteCard({ route }: { route: RouteSuggestion }) {
  return (
    <Card
      style={{ borderRadius: 16, height: '100%' }}
      title={
        <Space wrap>
          <RouteBadge code={route.routeCode} />
          <Text strong>{route.routeName}</Text>
          {route.runsDaily && (
            <Tag color="green" style={{ marginInlineEnd: 0 }}>
              Chạy hằng ngày
            </Tag>
          )}
        </Space>
      }
    >
      <Space orientation="vertical" size={4}>
        <Text type="secondary" style={{ fontSize: 12 }}>
          {route.origin} → {route.destination}
        </Text>
        <Text style={{ fontSize: 13 }}>
          {route.stops.length} trạm · {route.distanceKm} km
        </Text>
        {route.minPrice !== null && <Text>Giá từ {formatVnd(route.minPrice)}</Text>}
        {route.nearestAvailableDate ? (
          <Text type="secondary" style={{ fontSize: 12 }}>
            Chuyến gần nhất: {formatDay(route.nearestAvailableDate)}
            {formatTime(route.nextDeparture) ? ` · ${formatTime(route.nextDeparture)}` : ''}
          </Text>
        ) : (
          <Text type="secondary" style={{ fontSize: 12 }}>
            Chưa có lịch chạy trong 7 ngày tới.
          </Text>
        )}
      </Space>
    </Card>
  );
}

function describeEmpty(suggestions: RouteSuggestionsResponse): string {
  const { emptyReason, origin, destination } = suggestions;

  switch (emptyReason) {
    case 'noActiveRoute':
      return 'Hiện chưa có tuyến nào đang khai thác. Vui lòng quay lại sau.';
    case 'originNotServed':
      return `Điểm đi “${origin.name}” hiện không có tuyến nào đang khai thác. Vui lòng chọn trạm khác.`;
    case 'destinationNotServed':
      return `Điểm đến “${destination.name}” hiện không có tuyến nào đang khai thác. Vui lòng chọn trạm khác.`;
    case 'noRouteBetweenStops':
      return `Chưa có tuyến nối trực tiếp ${origin.name} với ${destination.name}, và cũng chưa có tuyến nào đi qua một trong hai trạm.`;
    default:
      return `Chưa có tuyến nối ${origin.name} với ${destination.name}.`;
  }
}

// Màn hình tra cứu tuyến (User Story 1): form "điểm đi - điểm đến - ngày" + gợi ý tuyến.
// Đây là màn hình cho HÀNH KHÁCH — không giới hạn vai trò như các màn hình quản trị.
//
// Hai ô điểm đi/điểm đến dùng StopSuggestionPicker (components/StopSuggestionPicker.tsx):
// gợi ý từ GET /stops/suggestions, endpoint CHỈ trả trạm thuộc tuyến đang khai thác. Ô cũ
// (StopAutocomplete) gọi /stops/search nên hiện cả trạm mồ côi của tuyến đã xoá — đúng lỗi
// hành khách báo. Form giữ CẢ id lẫn tên trạm; id là thứ gửi cho /routes/suggestions.
//
// Kết quả gọi GET /routes/suggestions chứ không phải /routes/search: ngày KHÔNG lọc tuyến.
// Tuyến nối được hai trạm luôn hiện kèm lịch chạy, nên màn hình không còn rơi vào ô "Không tìm
// thấy tuyến phù hợp" chỉ vì background job chưa sinh chuyến cho ngày đã chọn. Khi ngày đó
// trống, thẻ tuyến nói rõ và đưa ra ngày gần nhất có chuyến.
//
// Trang chủ gửi sang cặp điểm đi – điểm đến qua `location.state` (chip "tuyến gần đây") → điền
// sẵn form (tên được giải lại thành trạm thật qua /stops/suggestions), người dùng chỉ cần bấm
// "Tìm tuyến". Mỗi lượt tìm CÓ tuyến nối trực tiếp được ghi nhớ lại (recentRoutes.ts).
export default function RouteLookupPage() {
  const [form] = Form.useForm<SearchFormValues>();
  const navigate = useNavigate();
  const location = useLocation();
  const prefill = readPrefill(location.state);

  const [suggestions, setSuggestions] = useState<RouteSuggestionsResponse | null>(null);
  const [loading, setLoading] = useState(false);

  // Form KHÔNG tự render lại cả trang khi một ô đổi giá trị. `useWatch` là thứ khiến hai ô trạm
  // thấy được lựa chọn của nhau để lọc chéo: chọn điểm đi xong thì ô điểm đến chỉ gợi ý trạm
  // đứng sau nó, và ngược lại.
  const watchedOrigin = Form.useWatch('origin', form);
  const watchedDestination = Form.useWatch('destination', form);

  // Tiêu chí của lần tìm gần nhất — nguyên liệu để mở màn "Kết quả tìm kiếm" từ nút "Xem chuyến".
  const [lastCriteria, setLastCriteria] = useState<{
    origin: SelectedStop;
    destination: SelectedStop;
    date: string;
  } | null>(null);

  // "Tuyến gần đây" chỉ lưu tên trạm. Giải lại ra trạm thật (có id) trước khi điền form; trạm đã
  // bị xoá hoặc không còn tuyến nào chạy qua thì để trống, hành khách chọn lại.
  useEffect(() => {
    const originName = prefill.origin;
    const destinationName = prefill.destination;
    if (!originName && !destinationName) return;

    let cancelled = false;

    void (async () => {
      try {
        const [originStops, destinationStops] = await Promise.all([
          originName
            ? stopSuggestionsApi.search({ keyword: originName, limit: 5 })
            : Promise.resolve<StopSuggestion[]>([]),
          destinationName
            ? stopSuggestionsApi.search({ keyword: destinationName, limit: 5 })
            : Promise.resolve<StopSuggestion[]>([]),
        ]);

        if (cancelled) return;

        form.setFieldsValue({
          origin: originName ? pickStop(originStops, originName) : null,
          destination: destinationName ? pickStop(destinationStops, destinationName) : null,
        });
      } catch {
        // Điền sẵn là tiện ích, không phải luồng chính — hỏng thì để hành khách tự chọn trạm.
      }
    })();

    return () => {
      cancelled = true;
    };
  }, [form, prefill.origin, prefill.destination]);

  const runSearch = async (origin: SelectedStop, destination: SelectedStop, date: Dayjs) => {
    const isoDate = date.format(ISO_DATE);
    setLoading(true);

    try {
      const found = await routeSuggestionsApi.search({
        originStopId: origin.id,
        destinationStopId: destination.id,
        date: isoDate,
      });

      setSuggestions(found);
      setLastCriteria({ origin, destination, date: isoDate });

      // Chỉ ghi nhớ lượt tìm CÓ tuyến nối trực tiếp — cặp trạm không nối được mà gợi ý lại ở
      // trang chủ là vô nghĩa.
      if (found.matches.length > 0) rememberRecentRoute(origin.name, destination.name);
    } catch (error) {
      const appError = error as AppError;
      message.error(appError.customMessage || 'Không thể tra cứu tuyến.');
      setSuggestions(null);
    } finally {
      setLoading(false);
    }
  };

  const handleSearch = async ({ origin, destination, date }: SearchFormValues) => {
    if (!origin || !destination) return;

    // Hai đầu mút giống nhau thì không có chuyến hợp lệ — chặn sớm thay vì trả kết quả rỗng.
    if (origin.id === destination.id) {
      message.warning('Điểm đi và điểm đến không được trùng nhau.');
      return;
    }

    await runSearch(origin, destination, date);
  };

  const handleSwap = () => {
    const { origin, destination } = form.getFieldsValue();
    form.setFieldsValue({ origin: destination ?? null, destination: origin ?? null });
  };

  // Bấm chip ngày gần nhất trên một thẻ tuyến: đổi ngày trên form rồi tra lại, để mọi thẻ cùng
  // nhìn về một ngày thay vì mỗi thẻ một ngày khác nhau.
  const handlePickDate = (isoDate: string) => {
    const { origin, destination } = form.getFieldsValue();
    if (!origin || !destination) return;

    const picked = dayjs(isoDate);
    form.setFieldsValue({ date: picked });
    void runSearch(origin, destination, picked);
  };

  // Mở màn "Kết quả tìm kiếm" cho một tuyến. Điểm đi/điểm đến lấy theo trạm hành khách đã chọn,
  // routeId để màn kết quả gọi thẳng GET /trips/search của tuyến đó. Ngày đã chọn không có chuyến
  // thì mở luôn ngày gần nhất — nút đã ghi rõ ngày, không lén đổi sau lưng người dùng.
  const handleViewTrips = (route: RouteSuggestion, date: string) => {
    if (!lastCriteria) return;

    const params = new URLSearchParams({
      origin: lastCriteria.origin.name,
      destination: lastCriteria.destination.name,
      routeId: route.routeId,
      date,
    });

    navigate(`/trip-results?${params.toString()}`);
  };

  const matches = suggestions?.matches ?? [];
  const alternatives = suggestions?.alternatives;
  const hasAlternatives =
    matches.length === 0 &&
    ((alternatives?.fromOrigin.length ?? 0) > 0 || (alternatives?.toDestination.length ?? 0) > 0);
  const selectedDateLabel = suggestions ? formatDay(suggestions.selectedDate) : '';
  const horizonLabel = suggestions ? formatDay(suggestions.bookingHorizonEnd) : '';

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
          initialValues={{
            date: dayjs(),
            origin: null,
            destination: null,
          }}
          onFinish={handleSearch}
        >
          <Space size="large" wrap align="start" style={{ display: 'flex', width: '100%' }}>
            <Form.Item
              name="origin"
              label="Điểm đi"
              style={{ flex: 1, minWidth: 220 }}
              rules={[{ required: true, message: 'Vui lòng chọn điểm đi.' }]}
            >
              <StopSuggestionPicker
                placeholder="Gõ tên hoặc địa chỉ trạm, ví dụ: Tân Long"
                destinationStopId={watchedDestination?.id}
              />
            </Form.Item>

            <Form.Item label=" " style={{ marginBottom: 24 }}>
              <Button
                icon={<SwapOutlined />}
                onClick={handleSwap}
                title="Đổi chiều điểm đi – điểm đến"
                aria-label="Đổi chiều điểm đi – điểm đến"
              />
            </Form.Item>

            <Form.Item
              name="destination"
              label="Điểm đến"
              style={{ flex: 1, minWidth: 220 }}
              rules={[{ required: true, message: 'Vui lòng chọn điểm đến.' }]}
            >
              <StopSuggestionPicker
                placeholder="Gõ tên hoặc địa chỉ trạm, ví dụ: Phổ Yên"
                originStopId={watchedOrigin?.id}
              />
            </Form.Item>

            <Form.Item name="date" label="Ngày đi" style={{ minWidth: 180 }}>
              <DatePicker
                style={{ width: '100%' }}
                format="DD/MM/YYYY"
                allowClear={false}
                disabledDate={(current) => current.isBefore(dayjs().startOf('day'))}
              />
            </Form.Item>

            <Form.Item label=" " style={{ marginBottom: 24 }}>
              <Button type="primary" htmlType="submit" icon={<SearchOutlined />} loading={loading}>
                Tìm tuyến
              </Button>
            </Form.Item>
          </Space>
        </Form>
      </PageCard>

      {suggestions && !loading && (
        <div style={{ marginTop: 20 }}>
          {suggestions.emptyReason === 'dateBeyondHorizon' && (
            <Alert
              type="warning"
              showIcon
              style={{ marginBottom: 16 }}
              message={`Lịch bán vé hiện đến ${horizonLabel}`}
              description={`Ngày ${selectedDateLabel} chưa mở bán. Các tuyến bên dưới vẫn đang chạy — chọn ngày gần nhất được gợi ý để xem chuyến.`}
            />
          )}

          {suggestions.emptyReason === 'noTripsThatDay' && matches.length > 0 && (
            <Alert
              type="warning"
              showIcon
              style={{ marginBottom: 16 }}
              message={`Ngày ${selectedDateLabel} chưa có chuyến`}
              description="Các tuyến bên dưới vẫn đang khai thác. Chọn ngày gần nhất được gợi ý trên từng tuyến."
            />
          )}

          {matches.length > 0 && (
            <>
              <Text type="secondary" style={{ display: 'block', marginBottom: 12 }}>
                Tìm thấy <Text strong>{matches.length}</Text> tuyến phù hợp
                {suggestions.emptyReason === 'dateBeyondHorizon'
                  ? ` · ngày ${selectedDateLabel} nằm ngoài lịch bán vé`
                  : ''}
              </Text>
              <List<RouteSuggestion>
                dataSource={matches}
                grid={{ gutter: 16, xs: 1, sm: 1, md: 2, lg: 2, xl: 3, xxl: 3 }}
                renderItem={(route) => (
                  <List.Item>
                    <RouteSuggestionCard
                      route={route}
                      selectedDate={suggestions.selectedDate}
                      onViewTrips={handleViewTrips}
                      onPickDate={handlePickDate}
                    />
                  </List.Item>
                )}
              />
            </>
          )}

          {hasAlternatives && alternatives && (
            <div>
              <Alert
                type="info"
                showIcon
                message={`Chưa có tuyến nối trực tiếp ${suggestions.origin.name} → ${suggestions.destination.name}`}
                description="Dưới đây là các tuyến đi qua một trong hai đầu. Bạn có thể đi một tuyến trong số này rồi đổi tuyến ở trạm trung chuyển."
              />

              {alternatives.fromOrigin.length > 0 && (
                <>
                  <Text strong style={{ display: 'block', margin: '16px 0 8px' }}>
                    Đi từ {suggestions.origin.name}
                  </Text>
                  <List<RouteSuggestion>
                    dataSource={alternatives.fromOrigin}
                    grid={{ gutter: 16, xs: 1, sm: 1, md: 2, lg: 2, xl: 3, xxl: 3 }}
                    renderItem={(route) => (
                      <List.Item>
                        <AlternativeRouteCard route={route} />
                      </List.Item>
                    )}
                  />
                </>
              )}

              {alternatives.toDestination.length > 0 && (
                <>
                  <Text strong style={{ display: 'block', margin: '16px 0 8px' }}>
                    Đi đến {suggestions.destination.name}
                  </Text>
                  <List<RouteSuggestion>
                    dataSource={alternatives.toDestination}
                    grid={{ gutter: 16, xs: 1, sm: 1, md: 2, lg: 2, xl: 3, xxl: 3 }}
                    renderItem={(route) => (
                      <List.Item>
                        <AlternativeRouteCard route={route} />
                      </List.Item>
                    )}
                  />
                </>
              )}
            </div>
          )}

          {matches.length === 0 && !hasAlternatives && (
            <div style={{ marginTop: 40 }}>
              <EmptyState description={describeEmpty(suggestions)} />
            </div>
          )}
        </div>
      )}

      {!suggestions && !loading && (
        <div style={{ marginTop: 40 }}>
          <EmptyState description="Nhập điểm đi, điểm đến và ngày đi rồi bấm “Tìm tuyến”." />
        </div>
      )}
    </div>
  );
}
