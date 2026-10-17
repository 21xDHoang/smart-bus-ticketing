import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import {
  Alert,
  Button,
  Col,
  DatePicker,
  Form,
  InputNumber,
  Popconfirm,
  Row,
  Segmented,
  Select,
  Space,
  Table,
  Tag,
  TimePicker,
  Timeline,
  Tooltip,
  Typography,
  message,
  theme,
} from 'antd';
import type { TableProps } from 'antd';
import { SyncOutlined, ThunderboltOutlined } from '@ant-design/icons';
import dayjs, { type Dayjs } from 'dayjs';
import { useNavigate } from 'react-router-dom';
import { TRIP_STATUS_META, fetchRouteOptions } from '../api/tripApi';
import type { Trip, TripRouteOption, TripStatus } from '../api/tripApi';
import { fetchActiveBusOptions } from '../api/busApi';
import type { BusOption } from '../api/busApi';
import { fetchRouteStops } from '../api/routeStopApi';
import type { RouteStop } from '../api/routeStopApi';
import { generateTrips } from '../api/tripGenerateApi';
import type { GenerateTripsPayload, GenerateTripsResult } from '../api/tripGenerateApi';
import type { AppError } from '../api/axiosClient';
import {
  EmptyState,
  PageCard,
  PageHeader,
  StatusTag,
} from '../components/ui';
import { formatDateTime, formatTime } from '../components/ui/format';

// -----------------------------------------------------------------------------
// Màn hình cấu hình tần suất chạy xe theo khung giờ trong ngày — story 13
// ("Là quản lý, tôi muốn thiết lập thời gian biểu và tần suất chạy xe cho từng tuyến
// theo ngày"), task Sprint 2 — Hoàng Văn Thịnh.
//
// Một lần cấu hình = một lần gọi POST /routes/{routeId}/trips/generate: chọn tuyến +
// ngày áp dụng + giờ bắt đầu/kết thúc + tần suất + xe → xem trước số chuyến sẽ sinh →
// bấm sinh. Khác màn hình "Quản lý lịch trình" (/trip-schedule — của Hạnh, thêm/sửa/huỷ
// TỪNG chuyến lẻ) ở chỗ đây là thao tác theo lô sinh cả dải chuyến cách đều nhau.
//
// Hợp đồng — docs/api-contract.md mục "Lịch trình chạy xe — /routes/{routeId}/trips",
// tiểu mục "POST .../trips/generate". Hai luật của hợp đồng được phản ánh ngay trên
// màn hình: tối đa 500 chuyến mỗi lần (khối xem trước chặn trước khi bấm) và thao tác
// nguyên tử — một chuyến trùng khung giờ thì không chuyến nào được tạo (ghi trong
// hộp xác nhận).
//
// Điểm đến của chuyến là TRẠM CUỐI theo `stopOrder` của tuyến (A8.6), không phải chuỗi
// `Route.Destination` — chuỗi đó chỉ là nhãn hành chính, có thể lệch với trạm thật đã gán.
// Mỗi dòng kết quả mở rộng được thành dòng thời gian các trạm: km luỹ kế chia theo km
// từng đoạn, giờ của từng trạm suy từ tổng thời gian chạy THẬT của chuyến (arrivalTime -
// departureTime). Trạm cuối ghim đúng `arrivalTime`, không làm tròn lại.
// -----------------------------------------------------------------------------

const { Text } = Typography;

/** Múi giờ Việt Nam cố định — cùng lối TripSchedulePage / TripScheduleFormModal. */
const VIETNAM_OFFSET = '+07:00';

/** Trần số chuyến một lần sinh — hợp đồng ghi rõ "tối đa 500 chuyến", vượt là 400. */
const MAX_TRIPS_PER_CALL = 500;

/** Tần suất gợi ý nhanh (phút) — bấm một nút thay vì gõ số. */
const FREQUENCY_PRESETS = [10, 15, 20, 30, 60];

/**
 * Ghép ngày áp dụng + giờ trong ngày thành ISO 8601 kèm offset Việt Nam để gửi lên server.
 * Không dùng toISOString() — giờ gửi lên phải kèm múi giờ (hợp đồng), và người dùng nhìn
 * thấy đúng giờ mình chọn, không phụ thuộc múi giờ của trình duyệt người xem.
 */
function toVietnamIso(date: Dayjs, time: Dayjs): string {
  return `${date.format('YYYY-MM-DD')}T${time.format('HH:mm')}:00${VIETNAM_OFFSET}`;
}

/**
 * Tốc độ / cách làm tròn / sàn thời gian chạy — KHỚP công thức backend
 * `ArrivalEstimatingRouteTripsService.EstimateTravelMinutes`: 25 km/h, làm tròn LÊN bội số 5
 * phút, tối thiểu 15 phút. Chép công thức thay vì thêm endpoint riêng: backend đã trả giờ đến
 * của từng chuyến, số ở đây chỉ để giải thích và cảnh báo tần suất, không gửi lên server.
 */
const ESTIMATED_SPEED_KM_PER_HOUR = 25;
const TRAVEL_ROUNDING_MINUTES = 5;
const MINIMUM_TRAVEL_MINUTES = 15;

/** Thời gian chạy ước lượng (phút) từ chiều dài tuyến — cùng công thức với backend. */
function estimateTravelMinutes(distanceKm: number): number {
  const rawMinutes = (distanceKm / ESTIMATED_SPEED_KM_PER_HOUR) * 60;
  const roundedUp = Math.ceil(rawMinutes / TRAVEL_ROUNDING_MINUTES) * TRAVEL_ROUNDING_MINUTES;
  return Math.max(MINIMUM_TRAVEL_MINUTES, roundedUp);
}

/** Giá trị mặc định: hôm nay, khung 05:00–22:00, tần suất 15 phút — ví dụ của hợp đồng. */
function makeDefaultValues(): Partial<FrequencyFormValues> {
  const today = dayjs();
  return {
    date: today,
    startTime: today.hour(5).minute(0).second(0),
    endTime: today.hour(22).minute(0).second(0),
    frequencyMinutes: 15,
  };
}

/** Trạng thái khối "xem trước" — tính theo đúng luật sinh chuyến của hợp đồng. */
type PreviewState =
  | { kind: 'incomplete' }
  | { kind: 'invalid'; message: string }
  | { kind: 'over'; count: number }
  | { kind: 'ok'; count: number; first: string; last: string };

/**
 * Xem trước lịch trình sẽ sinh từ các giá trị đang hiển thị trên form.
 *
 * Công thức khớp hợp đồng: chuyến đầu đúng `startTime`, mỗi chuyến sau cách đều
 * `frequencyMinutes`, chuyến cuối KHÔNG vượt quá `endTime` → số chuyến =
 * floor(khoảng cách / tần suất) + 1. Ví dụ hợp đồng 05:00→22:00 tần suất 15 → 69 chuyến.
 */
function previewSchedule(
  date: Dayjs | undefined,
  start: Dayjs | undefined,
  end: Dayjs | undefined,
  frequency: number | null | undefined,
): PreviewState {
  if (!date || !start || !end || frequency === null || frequency === undefined) {
    return { kind: 'incomplete' };
  }

  // Khung giờ trong NGÀY: hai mốc chung một ngày áp dụng, giờ kết thúc phải sau giờ bắt
  // đầu — đúng luật 400 errors.endTime của backend, chặn trước ở đây cho dễ hiểu.
  const minutes = end.diff(start, 'minute');
  if (minutes <= 0) {
    return { kind: 'invalid', message: 'Giờ kết thúc phải sau giờ bắt đầu.' };
  }

  if (!Number.isInteger(frequency) || frequency < 1 || frequency > 1440) {
    return { kind: 'invalid', message: 'Tần suất phải là số nguyên từ 1 đến 1440 phút.' };
  }

  const count = Math.floor(minutes / frequency) + 1;
  if (count > MAX_TRIPS_PER_CALL) {
    return { kind: 'over', count };
  }

  const last = start.add((count - 1) * frequency, 'minute');
  return { kind: 'ok', count, first: start.format('HH:mm'), last: last.format('HH:mm') };
}

/** Ảnh chụp một trạm tại thời điểm sinh chuyến — đủ để dựng dòng thời gian về sau. */
interface TripStopSnapshot {
  stopId: string;
  stopName: string;
  stopOrder: number;
  distanceKm: number;
}

/**
 * Thời gian chạy THẬT của một chuyến (phút): `arrivalTime - departureTime`.
 * Chuyến chưa có giờ đến (dữ liệu cũ hoặc sinh lỗi) thì rơi về con số ước lượng theo
 * chiều dài tuyến — người dùng vẫn thấy một mốc thay vì ô trống, và hộp thoại đã ghi rõ
 * đó là ước lượng.
 */
function resolveTripTravelMinutes(trip: Trip, fallbackMinutes: number): number {
  if (!trip.arrivalTime) return fallbackMinutes;

  const actual = dayjs(trip.arrivalTime).diff(dayjs(trip.departureTime), 'minute');
  return actual > 0 ? actual : fallbackMinutes;
}

/**
 * Dòng thời gian các trạm của MỘT chuyến — chỉ render khi người dùng mở rộng dòng, nên
 * 500 chuyến không phải tính trước 500 dòng thời gian.
 *
 * Giờ từng trạm chia theo km luỹ kế: `round(km_i / tổng km × thời gian chạy thật)`. Trạm
 * đầu đúng giờ khởi hành, trạm cuối GHIM đúng `arrivalTime` (không làm tròn lại) — nhờ vậy
 * dòng cuối luôn khớp cột "Giờ đến" và không tự bịa một mốc lệch với lịch thật.
 */
function TripStopTimeline({
  trip,
  stops,
  fallbackTravelMinutes,
}: {
  trip: Trip;
  stops: TripStopSnapshot[];
  fallbackTravelMinutes: number;
}) {
  const { token } = theme.useToken();
  const totalKm = stops.reduce((sum, stop) => sum + (Number(stop.distanceKm) || 0), 0);

  if (stops.length < 2 || totalKm <= 0) return null;

  const departure = dayjs(trip.departureTime);
  const travelMinutes = resolveTripTravelMinutes(trip, fallbackTravelMinutes);
  const arrival = trip.arrivalTime
    ? dayjs(trip.arrivalTime)
    : departure.add(travelMinutes, 'minute');
  const lastIndex = stops.length - 1;

  let cumulativeKm = 0;
  const items = stops.map((stop, index) => {
    const segmentKm = index === 0 ? 0 : Number(stop.distanceKm) || 0;
    cumulativeKm = Math.round((cumulativeKm + segmentKm) * 100) / 100;

    const isLast = index === lastIndex;
    const minutesFromStart = isLast
      ? travelMinutes
      : Math.max(
          0,
          Math.min(travelMinutes, Math.round((cumulativeKm / totalKm) * travelMinutes)),
        );
    // Trạm cuối lấy thẳng `arrival` (có thể lệch giây so với DepartureTime), không cộng
    // lại số phút — nếu không, dòng cuối có thể hiện lệch một phút so với cột "Giờ đến".
    const time = index === 0 ? departure : isLast ? arrival : departure.add(minutesFromStart, 'minute');

    return {
      color: isLast ? token.colorPrimary : token.colorTextTertiary,
      children: (
        <div
          style={{
            display: 'flex',
            justifyContent: 'space-between',
            alignItems: 'baseline',
            gap: 12,
            flexWrap: 'wrap',
          }}
        >
          <span>
            <Text type="secondary" style={{ marginRight: 8 }}>
              {index + 1}.
            </Text>
            <Text strong={isLast}>{stop.stopName}</Text>
            {isLast && (
              <Tag color="blue" style={{ marginLeft: 8 }}>
                Bến cuối
              </Tag>
            )}
          </span>
          <Text type="secondary" style={{ fontVariantNumeric: 'tabular-nums' }}>
            {cumulativeKm.toFixed(2)} km · {time.format('HH:mm')}
          </Text>
        </div>
      ),
    };
  });

  return (
    <div>
      <Timeline items={items} style={{ marginTop: 4, marginBottom: 4 }} />
      <Text type="secondary" style={{ fontSize: 12 }}>
        Giờ từng trạm chia theo km luỹ kế trên {travelMinutes} phút chạy thực tế của chuyến;
        trạm cuối ghim đúng giờ đến {arrival.format('HH:mm')}.
      </Text>
    </div>
  );
}

/** Giá trị các ô của form — tên ô đặt TRÙNG tên trường API để map lỗi server về thẳng ô. */
interface FrequencyFormValues {
  routeId: string;
  date: Dayjs;
  startTime: Dayjs;
  endTime: Dayjs;
  frequencyMinutes: number;
  busId: string;
}

/**
 * Cách áp dụng lịch: đúng ngày đã chọn, hay "vĩnh viễn" — bắt đầu từ ngày đã chọn rồi để job
 * nền (TripGenerationBackgroundService) tự nhân bản sang các ngày tiếp theo. Cả hai chế độ
 * gửi CÙNG một payload một ngày; nút chỉ đổi cách hiểu và lời giải thích trên màn hình,
 * không đổi hình dạng hợp đồng API.
 */
type ApplyMode = 'once' | 'forever';

export default function TripFrequencyPage() {
  const [form] = Form.useForm<FrequencyFormValues>();
  const navigate = useNavigate();

  const [initialValues] = useState(makeDefaultValues);
  // Giá trị đang hiển thị trên form, giữ song song trong state để tính khối xem trước.
  // onValuesChange bắt mọi thay đổi người dùng; các chỗ đặt giá trị bằng code
  // (tuyến mặc định, nút tần suất gợi ý) tự cập nhật thêm vì setFieldValue không phát sự kiện.
  const [watched, setWatched] = useState<Partial<FrequencyFormValues>>(initialValues);

  const [routes, setRoutes] = useState<TripRouteOption[]>([]);
  const [buses, setBuses] = useState<BusOption[]>([]);
  const [loadingOptions, setLoadingOptions] = useState(true);

  // Trạm của tuyến đang chọn — nguồn cho điểm đến (trạm cuối) và dòng thời gian giờ đến.
  // `routeStopsForId` giữ tuyến đã nạp: đổi tuyến xong mà response cũ về muộn thì không
  // được hiển thị trạm của tuyến trước.
  const [routeStops, setRouteStops] = useState<RouteStop[]>([]);
  const [routeStopsForId, setRouteStopsForId] = useState<string>();
  const [loadingRouteStops, setLoadingRouteStops] = useState(false);
  const [routeStopsError, setRouteStopsError] = useState<string>();
  const routeStopsRequestId = useRef(0);

  const [submitting, setSubmitting] = useState(false);
  // Chế độ áp dụng của lần cấu hình hiện tại — mặc định chỉ ngày đã chọn.
  const [applyMode, setApplyMode] = useState<ApplyMode>('once');
  // Kết quả lần sinh gần nhất — giữ cả thông tin tuyến/ngày/chế độ để tiêu đề khỏi mơ hồ.
  const [result, setResult] = useState<GenerateTripsResult | null>(null);
  const [resultInfo, setResultInfo] = useState<{
    routeLabel: string;
    /** Trạm cuối theo thứ tự trạm; chỉ rơi về `Route.Destination` khi tuyến chưa gán trạm. */
    destination: string;
    destinationFromStops: boolean;
    /** Chiều dài tuyến (km) — giữ lại để giải thích con số ước lượng trên bảng kết quả. */
    distanceKm: number;
    /** Thời gian chạy ước lượng (phút) đã dùng cho lần sinh này. */
    travelMinutes: number;
    /** Ảnh chụp trạm tại thời điểm sinh — tuyến bị sửa sau đó không làm sai lịch đã sinh. */
    stops: { stopId: string; stopName: string; stopOrder: number; distanceKm: number }[];
    date: Dayjs;
    permanent: boolean;
  } | null>(null);

  // Tải danh sách tuyến + xe đang khai thác cho hai ô chọn.
  const loadOptions = useCallback(async () => {
    try {
      const [routeList, busList] = await Promise.all([fetchRouteOptions(), fetchActiveBusOptions()]);
      setRoutes(routeList);
      setBuses(busList);

      // Tự chọn tuyến đầu tiên cho tiện — người dùng vẫn đổi được qua ô chọn.
      if (routeList[0]) {
        form.setFieldValue('routeId', routeList[0].id);
        // setFieldValue không phát onValuesChange — đồng bộ state song song để khối xem trước
        // biết tuyến mặc định ngay từ lần tải đầu (điểm đến + thời gian chạy ước lượng).
        setWatched((current) => ({ ...current, routeId: routeList[0].id }));
      }
    } catch (error) {
      message.error((error as Error).message || 'Không tải được dữ liệu ban đầu.');
    } finally {
      setLoadingOptions(false);
    }
  }, [form]);

  useEffect(() => {
    // oxlint-disable-next-line react/set-state-in-effect
    void loadOptions();
  }, [loadOptions]);

  /**
   * Nạp trạm của một tuyến. Dùng số thứ tự request (`routeStopsRequestId`) thay vì chỉ
   * một cờ "còn sống": đổi tuyến nhanh sẽ có hai request chồng nhau, request cũ về sau
   * không được ghi đè trạm của tuyến mới.
   */
  const loadRouteStops = useCallback(async (routeId?: string) => {
    const requestId = routeStopsRequestId.current + 1;
    routeStopsRequestId.current = requestId;

    if (!routeId) {
      setRouteStops([]);
      setRouteStopsForId(undefined);
      setRouteStopsError(undefined);
      setLoadingRouteStops(false);
      return;
    }

    setLoadingRouteStops(true);
    setRouteStopsError(undefined);

    try {
      const list = await fetchRouteStops(routeId);
      if (routeStopsRequestId.current !== requestId) return;

      // Backend đã xếp theo stopOrder; sắp lại phòng thủ để trạm cuối luôn đúng.
      setRouteStops([...list].sort((a, b) => a.stopOrder - b.stopOrder));
      setRouteStopsForId(routeId);
    } catch (error) {
      if (routeStopsRequestId.current !== requestId) return;

      setRouteStops([]);
      setRouteStopsForId(routeId);
      setRouteStopsError(
        (error as AppError).customMessage || 'Không tải được danh sách trạm của tuyến.',
      );
    } finally {
      if (routeStopsRequestId.current === requestId) setLoadingRouteStops(false);
    }
  }, []);

  useEffect(() => {
    // oxlint-disable-next-line react/set-state-in-effect
    void loadRouteStops(watched.routeId);
  }, [loadRouteStops, watched.routeId]);

  const preview = previewSchedule(
    watched.date,
    watched.startTime,
    watched.endTime,
    watched.frequencyMinutes,
  );

  const forever = applyMode === 'forever';

  // Tuyến đang chọn — điểm đến và chiều dài lấy thẳng từ RouteResponse, không suy từ tên tuyến.
  const selectedRoute = routes.find((route) => route.id === watched.routeId);
  const estimatedTravelMinutes = selectedRoute
    ? estimateTravelMinutes(selectedRoute.distanceKm)
    : null;

  // Chỉ dùng trạm khi chúng thật sự thuộc tuyến đang chọn — response của tuyến cũ về muộn
  // không được lẫn sang đây (xem `routeStopsRequestId`).
  const routeStopsReady = routeStopsForId === watched.routeId;
  const selectedRouteStops = routeStopsReady ? routeStops : [];
  const stopTotalKm = selectedRouteStops.reduce(
    (sum, stop) => sum + (Number(stop.distanceKm) || 0),
    0,
  );
  const hasStopDistanceData = selectedRouteStops.length >= 2 && stopTotalKm > 0;
  const lastStop = selectedRouteStops[selectedRouteStops.length - 1];
  // Điểm đến thật của chuyến là TRẠM CUỐI (A8.6). Chuỗi Route.Destination chỉ là nhãn hành
  // chính nên chỉ dùng khi tuyến chưa gán trạm nào.
  const previewDestination = lastStop?.stopName ?? selectedRoute?.destination ?? '';
  const previewDestinationFromStops = Boolean(lastStop);

  // Tổng km của ảnh chụp trạm trong kết quả — quyết định có mở rộng được dòng thời gian không.
  const resultStopTotalKm = useMemo(
    () =>
      (resultInfo?.stops ?? []).reduce((sum, stop) => sum + (Number(stop.distanceKm) || 0), 0),
    [resultInfo],
  );

  // Tần suất ngắn hơn thời gian chạy: một xe chưa kịp về bến đã có chuyến kế tiếp. Backend chỉ
  // kiểm tra trùng khung giờ với chuyến CÓ SẴN, không kiểm tra chéo các chuyến vừa sinh trong
  // cùng lượt — nên cảnh báo ở đây là lưới an toàn cho điều độ, không chặn thao tác.
  const frequencyTooShort =
    preview.kind === 'ok' &&
    estimatedTravelMinutes !== null &&
    (watched.frequencyMinutes ?? 0) < estimatedTravelMinutes;

  const applyFrequency = (value: number) => {
    form.setFieldValue('frequencyMinutes', value);
    setWatched((current) => ({ ...current, frequencyMinutes: value }));
  };

  const handleSubmit = async () => {
    let values: FrequencyFormValues;
    try {
      values = await form.validateFields();
    } catch {
      // validateFields rejects khi form chưa hợp lệ — AntD đã hiển thị lỗi từng ô.
      return;
    }

    const route = routes.find((item) => item.id === values.routeId);
    // Chốt chế độ tại thời điểm bấm sinh — người dùng có thể đổi nút trong lúc chờ API.
    const permanent = applyMode === 'forever';
    const payload: GenerateTripsPayload = {
      busId: values.busId,
      startTime: toVietnamIso(values.date, values.startTime),
      endTime: toVietnamIso(values.date, values.endTime),
      frequencyMinutes: values.frequencyMinutes,
    };

    setSubmitting(true);
    try {
      // Chụp luôn danh sách trạm tại thời điểm sinh: tuyến bị sửa sau đó (đổi thứ tự, nhập
      // lại km) không được làm sai dòng thời gian của lịch đã sinh. Lỗi tải trạm KHÔNG được
      // làm hỏng thao tác sinh chuyến — rơi về danh sách đang có trên màn hình, có thể rỗng.
      const [generated, fetchedStops] = await Promise.all([
        generateTrips(values.routeId, payload),
        fetchRouteStops(values.routeId)
          .then((list) => [...list].sort((a, b) => a.stopOrder - b.stopOrder))
          .catch(() => null),
      ]);

      const resultStops =
        fetchedStops ?? (routeStopsForId === values.routeId ? routeStops : []);
      const resultLastStop = resultStops[resultStops.length - 1];

      setResult(generated);
      setResultInfo({
        routeLabel: route ? `${route.code} — ${route.name}` : '',
        destination: resultLastStop?.stopName ?? route?.destination ?? '',
        destinationFromStops: Boolean(resultLastStop),
        distanceKm: route?.distanceKm ?? 0,
        travelMinutes: route ? estimateTravelMinutes(route.distanceKm) : 0,
        stops: resultStops.map((stop) => ({
          stopId: stop.stopId,
          stopName: stop.stopName,
          stopOrder: stop.stopOrder,
          distanceKm: Number(stop.distanceKm) || 0,
        })),
        date: values.date,
        permanent,
      });
      message.success(
        permanent
          ? `Đã sinh ${generated.total} chuyến — lịch sẽ tự lặp lại hằng ngày.`
          : `Đã sinh ${generated.total} chuyến.`,
      );
    } catch (error) {
      const appError = error as AppError;
      if (appError.errors) {
        // Lỗi theo từng trường (endTime, frequencyMinutes, busId…) → gắn thẳng vào ô input;
        // tên ô trùng tên trường API nên map thẳng, không cần bảng dịch.
        form.setFields(
          Object.entries(appError.errors).map(([name, fieldErrors]) => ({
            name: name as keyof FrequencyFormValues,
            errors: fieldErrors,
          })),
        );
      } else {
        // 409 trùng khung giờ (không chuyến nào được tạo), 404 tuyến/xe, 403… — hiện toast.
        message.error(appError.customMessage || 'Sinh chuyến thất bại.');
      }
    } finally {
      setSubmitting(false);
    }
  };

  const columns: TableProps<Trip>['columns'] = [
    {
      title: 'Khởi hành',
      dataIndex: 'departureTime',
      key: 'departureTime',
      width: 160,
      render: (departureTime: string) => (
        <span style={{ fontWeight: 600 }}>{formatDateTime(departureTime)}</span>
      ),
    },
    {
      title: 'Điểm đến (bến cuối)',
      key: 'destination',
      width: 200,
      // Vé bán theo CẢ CHUYẾN (A8.1) — bến cuối là một phần của lịch trình, hiện thẳng ở đây
      // thay vì bắt người lập lịch mở màn hình tuyến để tra.
      render: () => resultInfo?.destination || '—',
    },
    {
      title: 'Giờ đến (dự kiến)',
      dataIndex: 'arrivalTime',
      key: 'arrivalTime',
      width: 140,
      // Backend điền ArrivalTime cho chuyến mới; nếu vẫn trống (dữ liệu cũ / lỗi nhất thời)
      // thì hiện ước lượng và nói rõ đây là số ước lượng, không giả làm giờ thật.
      render: (arrivalTime: string | null, trip: Trip) => {
        if (arrivalTime) return formatTime(arrivalTime);
        if (!resultInfo) return '—';

        return (
          <Tooltip title="Chuyến chưa có giờ đến thật — hiển thị ước lượng theo chiều dài tuyến">
            <Text type="secondary">
              {dayjs(trip.departureTime).add(resultInfo.travelMinutes, 'minute').format('HH:mm')}
            </Text>
          </Tooltip>
        );
      },
    },
    {
      title: 'Biển số xe',
      dataIndex: 'busLicensePlate',
      key: 'busLicensePlate',
      width: 140,
      render: (plate: string) => (plate ? <StatusTag>{plate}</StatusTag> : '—'),
    },
    {
      title: 'Trạng thái',
      dataIndex: 'status',
      key: 'status',
      width: 150,
      render: (tripStatus: TripStatus) => {
        // Backend chỉ trả bốn mã trong TRIP_STATUS_META; giá trị lạ thì hiện nguyên văn,
        // không để màn hình vỡ vì tra meta không thấy — cùng lối TripSchedulePage.
        const meta = TRIP_STATUS_META[tripStatus];
        return meta ? (
          <StatusTag color={meta.color}>{meta.label}</StatusTag>
        ) : (
          <StatusTag>{tripStatus}</StatusTag>
        );
      },
    },
  ];

  return (
    <div>
      <PageHeader
        title="Cấu hình tần suất chạy xe"
        subtitle="Chọn tuyến, ngày áp dụng, khung giờ và tần suất — hệ thống sinh các chuyến cách đều nhau; bật Vĩnh viễn để lịch tự lặp lại hằng ngày."
      />

      <PageCard>
        {!loadingOptions && routes.length === 0 ? (
          <EmptyState description="Chưa có tuyến nào để cấu hình tần suất." />
        ) : (
          <>
            {!loadingOptions && buses.length === 0 && (
              <Alert
                type="warning"
                showIcon
                style={{ marginBottom: 16 }}
                message="Chưa có xe nào đang khai thác — cần ít nhất một xe ở trạng thái Đang khai thác để sinh chuyến."
              />
            )}

            <Form
              form={form}
              layout="vertical"
              initialValues={initialValues}
              onValuesChange={(_, allValues) => setWatched(allValues)}
            >
              <Row gutter={16}>
                <Col xs={24} md={12}>
                  <Form.Item
                    name="routeId"
                    label="Tuyến"
                    rules={[{ required: true, message: 'Vui lòng chọn tuyến' }]}
                  >
                    <Select
                      showSearch
                      optionFilterProp="label"
                      placeholder="Chọn tuyến"
                      loading={loadingOptions}
                      options={routes.map((route) => ({
                        value: route.id,
                        label: `${route.code} — ${route.name}`,
                      }))}
                    />
                  </Form.Item>
                </Col>

                <Col xs={24} md={6}>
                  <Form.Item
                    name="date"
                    label={forever ? 'Bắt đầu áp dụng' : 'Ngày áp dụng'}
                    rules={[{ required: true, message: 'Vui lòng chọn ngày áp dụng' }]}
                    extra={
                      <Space
                        size={8}
                        style={{ width: '100%', justifyContent: 'space-between' }}
                      >
                        <Text type="secondary" style={{ fontSize: 12 }}>
                          {forever ? 'Tự lặp lại hằng ngày về sau.' : 'Sinh lịch cho ngày này.'}
                        </Text>
                        <Button
                          size="small"
                          type={forever ? 'primary' : 'default'}
                          icon={<SyncOutlined />}
                          aria-pressed={forever}
                          htmlType="button"
                          onClick={() => setApplyMode(forever ? 'once' : 'forever')}
                        >
                          Vĩnh viễn
                        </Button>
                      </Space>
                    }
                  >
                    <DatePicker format="DD/MM/YYYY" style={{ width: '100%' }} />
                  </Form.Item>
                </Col>

                <Col xs={24} md={6}>
                  <Form.Item
                    name="busId"
                    label="Xe buýt"
                    rules={[{ required: true, message: 'Vui lòng chọn xe buýt' }]}
                  >
                    <Select
                      showSearch
                      optionFilterProp="label"
                      placeholder="Chọn xe đang khai thác"
                      notFoundContent="Không có xe nào đang khai thác"
                      options={buses.map((bus) => ({
                        value: bus.id,
                        label: `${bus.licensePlate} · ${bus.busType} · ${bus.capacity} chỗ`,
                      }))}
                    />
                  </Form.Item>
                </Col>
              </Row>

              <Row gutter={16}>
                <Col xs={24} md={6}>
                  <Form.Item
                    name="startTime"
                    label="Giờ bắt đầu"
                    rules={[{ required: true, message: 'Vui lòng chọn giờ bắt đầu' }]}
                  >
                    <TimePicker format="HH:mm" style={{ width: '100%' }} placeholder="05:00" />
                  </Form.Item>
                </Col>

                <Col xs={24} md={6}>
                  <Form.Item
                    name="endTime"
                    label="Giờ kết thúc"
                    dependencies={['startTime']}
                    rules={[
                      { required: true, message: 'Vui lòng chọn giờ kết thúc' },
                      {
                        validator: (_, value: Dayjs | undefined) => {
                          if (!value) return Promise.resolve();
                          const start = form.getFieldValue('startTime') as Dayjs | undefined;
                          if (start && !value.isAfter(start)) {
                            return Promise.reject(
                              new Error('Giờ kết thúc phải sau giờ bắt đầu'),
                            );
                          }
                          return Promise.resolve();
                        },
                      },
                    ]}
                  >
                    <TimePicker format="HH:mm" style={{ width: '100%' }} placeholder="22:00" />
                  </Form.Item>
                </Col>

                <Col xs={24} md={12}>
                  <Form.Item
                    name="frequencyMinutes"
                    label="Tần suất (khoảng cách giữa hai chuyến liên tiếp)"
                    rules={[
                      { required: true, message: 'Vui lòng nhập tần suất' },
                      {
                        type: 'integer',
                        min: 1,
                        max: 1440,
                        message: 'Tần suất phải là số nguyên từ 1 đến 1440 phút',
                      },
                    ]}
                    extra={
                      <Segmented
                        size="small"
                        options={FREQUENCY_PRESETS.map((minutes) => ({
                          label: `${minutes} phút`,
                          value: minutes,
                        }))}
                        value={
                          FREQUENCY_PRESETS.includes(watched.frequencyMinutes ?? 0)
                            ? watched.frequencyMinutes
                            : undefined
                        }
                        onChange={(value) => applyFrequency(value as number)}
                      />
                    }
                  >
                    <InputNumber
                      min={1}
                      max={1440}
                      style={{ width: '100%' }}
                      addonAfter="phút / chuyến"
                    />
                  </Form.Item>
                </Col>
              </Row>
            </Form>

            <div style={{ marginTop: 8 }}>
              {preview.kind === 'incomplete' && (
                <Alert
                  type="info"
                  showIcon
                  message="Chọn đủ ngày, khung giờ và tần suất để xem trước số chuyến sẽ sinh."
                />
              )}
              {preview.kind === 'invalid' && (
                <Alert type="error" showIcon message={preview.message} />
              )}
              {preview.kind === 'over' && (
                <Alert
                  type="warning"
                  showIcon
                  message={`Dự kiến ${preview.count} chuyến${forever ? ' mỗi ngày' : ''} — vượt trần ${MAX_TRIPS_PER_CALL} chuyến mỗi lần sinh.`}
                  description="Thu hẹp khung giờ hoặc tăng tần suất rồi thử lại."
                />
              )}
              {preview.kind === 'ok' && (
                <Alert
                  type="success"
                  showIcon
                  message={`Dự kiến sinh ${preview.count} chuyến${forever ? ' mỗi ngày' : ''}`}
                  description={
                    <>
                      <div>
                        {forever && watched.date
                          ? `Bắt đầu ${watched.date.format('DD/MM/YYYY')} và tự lặp lại hằng ngày. Chuyến đầu ${preview.first}, chuyến cuối ${preview.last}, cách nhau mỗi ${watched.frequencyMinutes} phút.`
                          : `Chuyến đầu ${preview.first}, chuyến cuối ${preview.last}, cách nhau mỗi ${watched.frequencyMinutes} phút.`}
                      </div>
                      {selectedRoute && estimatedTravelMinutes !== null && (
                        <div style={{ marginTop: 4 }}>
                          Điểm đến: <Text strong>{previewDestination || '—'}</Text>
                          {previewDestinationFromStops
                            ? ' (bến cuối theo thứ tự trạm)'
                            : ' (nhãn tuyến — tuyến chưa gán trạm)'}{' '}
                          · thời gian chạy ước lượng ~{estimatedTravelMinutes} phút (
                          {selectedRoute.distanceKm} km, 25 km/h) — giờ đến mỗi chuyến = giờ khởi
                          hành + {estimatedTravelMinutes} phút.
                        </div>
                      )}
                      {selectedRoute && hasStopDistanceData && (
                        <div style={{ marginTop: 4 }}>
                          Giờ đến từng trạm chia theo km luỹ kế của {selectedRouteStops.length}{' '}
                          trạm; trạm cuối ghim đúng giờ đến của chuyến.
                        </div>
                      )}
                    </>
                  }
                />
              )}
              {selectedRoute && routeStopsError && (
                <Alert
                  type="error"
                  showIcon
                  style={{ marginTop: 8 }}
                  message="Không tải được danh sách trạm của tuyến"
                  description={routeStopsError}
                  action={
                    <Button size="small" onClick={() => void loadRouteStops(watched.routeId)}>
                      Thử lại
                    </Button>
                  }
                />
              )}
              {selectedRoute && routeStopsReady && !loadingRouteStops && !routeStopsError && !previewDestinationFromStops && (
                <Alert
                  type="warning"
                  showIcon
                  style={{ marginTop: 8 }}
                  message="Tuyến chưa gán trạm nào — điểm đến đang lấy theo nhãn của tuyến"
                  description="Gán trạm cho tuyến để xác định bến cuối thật và có giờ đến từng trạm."
                  action={
                    <Button size="small" onClick={() => navigate('/route-stops')}>
                      Gán trạm
                    </Button>
                  }
                />
              )}
              {selectedRoute &&
                routeStopsReady &&
                !loadingRouteStops &&
                !routeStopsError &&
                previewDestinationFromStops &&
                selectedRouteStops.length < 2 && (
                  <Alert
                    type="warning"
                    showIcon
                    style={{ marginTop: 8 }}
                    message="Tuyến chỉ có một trạm — chưa dựng được giờ đến từng trạm"
                    description="Cần ít nhất hai trạm để chia thời gian chạy theo từng đoạn."
                    action={
                      <Button size="small" onClick={() => navigate('/route-stops')}>
                        Gán trạm
                      </Button>
                    }
                  />
                )}
              {selectedRoute &&
                routeStopsReady &&
                !loadingRouteStops &&
                !routeStopsError &&
                selectedRouteStops.length >= 2 &&
                !hasStopDistanceData && (
                  <Alert
                    type="warning"
                    showIcon
                    style={{ marginTop: 8 }}
                    message="Chưa nhập khoảng cách giữa các trạm — chưa tính được giờ đến từng trạm"
                    description="Mở màn Gán trạm vào tuyến, nhập km từng đoạn hoặc bấm “Ước lượng từ toạ độ” rồi lưu."
                    action={
                      <Button size="small" onClick={() => navigate('/route-stops')}>
                        Nhập khoảng cách
                      </Button>
                    }
                  />
                )}
              {frequencyTooShort && estimatedTravelMinutes !== null && (
                <Alert
                  type="warning"
                  showIcon
                  style={{ marginTop: 8 }}
                  message="Tần suất ngắn hơn thời gian chạy ước lượng"
                  description={`Mỗi chuyến mất khoảng ${estimatedTravelMinutes} phút, nhưng cứ ${watched.frequencyMinutes} phút lại có chuyến kế tiếp — một xe không kịp về bến để chạy chuyến sau. Tăng tần suất lên ít nhất ${estimatedTravelMinutes} phút hoặc bố trí thêm xe.`}
                />
              )}
            </div>

            <div style={{ marginTop: 16 }}>
              <Popconfirm
                title={
                  preview.kind === 'ok'
                    ? `Sinh ${preview.count} chuyến${forever ? ' mỗi ngày' : ''}?`
                    : 'Sinh chuyến?'
                }
                description={
                  forever
                    ? 'Lịch bắt đầu từ ngày đã chọn và tự lặp lại hằng ngày. Nếu một chuyến trong dải trùng khung giờ với chuyến đã có, KHÔNG chuyến nào được tạo — thao tác nguyên tử.'
                    : 'Nếu một chuyến trong dải trùng khung giờ với chuyến đã có, KHÔNG chuyến nào được tạo — thao tác nguyên tử.'
                }
                okText="Sinh chuyến"
                cancelText="Xem lại"
                onConfirm={handleSubmit}
                disabled={preview.kind !== 'ok'}
              >
                <Button
                  type="primary"
                  icon={<ThunderboltOutlined />}
                  loading={submitting}
                  disabled={preview.kind !== 'ok'}
                >
                  {preview.kind === 'ok'
                    ? `Sinh ${preview.count} chuyến${forever ? ' mỗi ngày' : ''}`
                    : 'Sinh chuyến'}
                </Button>
              </Popconfirm>
            </div>
          </>
        )}
      </PageCard>

      {result && resultInfo && (
        // PageCard không có prop style — bọc ngoài để giữ khoảng cách với khối trên.
        <div style={{ marginTop: 24 }}>
          <PageCard
            title={`Đã sinh ${result.total} chuyến${resultInfo.permanent ? ' mỗi ngày' : ''}`}
            extra={
              <Text type="secondary">
                {resultInfo.routeLabel ? `${resultInfo.routeLabel} · ` : ''}
                {resultInfo.destination ? `đến ${resultInfo.destination} · ` : ''}
                {resultInfo.permanent
                  ? `vĩnh viễn từ ${resultInfo.date.format('DD/MM/YYYY')}`
                  : `ngày ${resultInfo.date.format('DD/MM/YYYY')}`}
              </Text>
            }
          >
            <Alert
              type="info"
              showIcon
              style={{ marginBottom: 16 }}
              message={`Giờ đến là ước lượng: giờ khởi hành + ${resultInfo.travelMinutes} phút (${
                resultInfo.distanceKm > 0
                  ? `tuyến ${resultInfo.distanceKm} km`
                  : 'chiều dài tuyến chưa cập nhật'
              }, tốc độ bình quân 25 km/h, làm tròn lên 5 phút).`}
              description={
                resultStopTotalKm > 0 && resultInfo.stops.length >= 2
                  ? 'Bấm mũi tên đầu dòng để xem giờ đến từng trạm. Cần chốt giờ thực tế thì sửa từng chuyến ở màn Quản lý lịch trình.'
                  : 'Cần chốt giờ thực tế thì sửa từng chuyến ở màn Quản lý lịch trình.'
              }
            />
            {resultInfo.stops.length >= 2 && resultStopTotalKm <= 0 && (
              <Alert
                type="warning"
                showIcon
                style={{ marginBottom: 16 }}
                message="Chưa nhập khoảng cách giữa các trạm — chưa tính được giờ đến từng trạm"
                description="Lịch đã sinh vẫn dùng được; nhập khoảng cách cho tuyến để lần sinh sau có dòng thời gian từng trạm."
                action={
                  <Button size="small" onClick={() => navigate('/route-stops')}>
                    Nhập khoảng cách
                  </Button>
                }
              />
            )}
            {resultInfo.stops.length < 2 && (
              <Alert
                type="info"
                showIcon
                style={{ marginBottom: 16 }}
                message="Tuyến chưa gán đủ trạm — chỉ hiển thị giờ đến của cả chuyến"
                description={
                  resultInfo.destinationFromStops
                    ? 'Tuyến cần ít nhất hai trạm để chia thời gian chạy theo từng đoạn.'
                    : 'Điểm đến đang lấy theo nhãn của tuyến; gán trạm để có bến cuối thật và giờ đến từng trạm.'
                }
                action={
                  <Button size="small" onClick={() => navigate('/route-stops')}>
                    Gán trạm
                  </Button>
                }
              />
            )}
            <Table<Trip>
              rowKey="id"
              columns={columns}
              dataSource={result.items}
              scroll={{ x: 760 }}
              expandable={{
                rowExpandable: () => resultInfo.stops.length >= 2 && resultStopTotalKm > 0,
                expandedRowRender: (trip) => (
                  <TripStopTimeline
                    trip={trip}
                    stops={resultInfo.stops}
                    fallbackTravelMinutes={resultInfo.travelMinutes}
                  />
                ),
              }}
              pagination={{
                pageSize: 20,
                showSizeChanger: false,
                showTotal: (total, range) =>
                  `Hiển thị ${range[0]}–${range[1]} trên ${total} chuyến`,
              }}
            />
          </PageCard>
        </div>
      )}
    </div>
  );
}
