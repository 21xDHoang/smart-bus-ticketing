import axiosClient from './axiosClient';
import { fetchRoutes } from './routeApi';

// -----------------------------------------------------------------------------
// API danh sách chuyến theo ngày — nối với RouteTripsController (story 13).
//
// Hợp đồng endpoint — xem docs/api-contract.md mục "Lịch trình chạy xe — /routes/{routeId}/trips":
//   GET /routes/{routeId}/trips?from=&to=&status=&page=&pageSize=
//     → { items: Trip[], total, page, pageSize }
//
// Đây là phần ĐỌC danh sách (màn hình "danh sách chuyến theo ngày + bộ lọc
// tuyến/trạng thái" — Nguyễn Đình Băng). Phần thêm/sửa/xoá lịch trình là task riêng
// của màn hình quản lý lịch trình, tách ra không gộp vào đây.
//
// Cả RouteTripsController (nguồn chuyến) lẫn RoutesController (nguồn cho ô chọn
// tuyến) đều đã có thật, nên file này KHÔNG có nhánh dữ liệu giả như stopApi.ts:
// gọi thẳng API.
// -----------------------------------------------------------------------------

/** Trạng thái chuyến — khớp enum TripStatus của backend, đúng thứ tự khai báo. */
export type TripStatus = 'Scheduled' | 'Running' | 'Completed' | 'Cancelled';

/** Một chuyến xe — khớp `TripResponse` của backend, xem docs/api-contract.md. */
export interface Trip {
  id: string;
  routeId: string;
  busId: string;
  /** Biển số xe — backend kèm sẵn để hiển thị mà không phải gọi thêm API xe. */
  busLicensePlate: string;
  /** Giờ khởi hành thực tế — ISO 8601 UTC (hậu tố Z). */
  departureTime: string;
  /** Giờ dự kiến tới bến cuối. null khi chưa chốt. */
  arrivalTime: string | null;
  status: TripStatus;
  currentStopId: string | null;
  currentLat: number | null;
  currentLng: number | null;
  positionUpdatedAt: string | null;
  createdAt: string;
  updatedAt: string | null;
}

export interface TripListParams {
  /** Chỉ lấy chuyến khởi hành từ mốc này — ISO 8601 có kèm múi giờ. */
  from?: string;
  /** Chỉ lấy chuyến khởi hành tới mốc này — ISO 8601 có kèm múi giờ. */
  to?: string;
  status?: TripStatus;
  page: number;
  pageSize: number;
}

export interface TripListResult {
  /** Chỉ là trang hiện tại, không phải toàn bộ. */
  items: Trip[];
  /** Tổng số dòng KHỚP BỘ LỌC (không phải số dòng trong `items`) — dùng để vẽ phân trang. */
  total: number;
  page: number;
  pageSize: number;
}

interface TripStatusMeta {
  label: string;
  /** Tên màu preset của Tag AntD. */
  color: string;
}

/** Nhãn + màu cho từng trạng thái, theo đúng thứ tự enum (Scheduled → Cancelled). */
export const TRIP_STATUS_META: Record<TripStatus, TripStatusMeta> = {
  Scheduled: { label: 'Đã lên lịch', color: 'blue' },
  Running: { label: 'Đang chạy', color: 'processing' },
  Completed: { label: 'Đã hoàn thành', color: 'success' },
  Cancelled: { label: 'Đã huỷ', color: 'default' },
};

/** Danh sách trạng thái cho bộ lọc, theo thứ tự hiển thị mong muốn. */
export const TRIP_STATUS_OPTIONS = (Object.keys(TRIP_STATUS_META) as TripStatus[]).map(
  (value) => ({ value, label: TRIP_STATUS_META[value].label }),
);

/** Một tuyến trong ô chọn — rút gọn từ Route, chỉ đủ để hiển thị. */
export interface TripRouteOption {
  id: string;
  /** Mã tuyến hiển thị cho hành khách, ví dụ "01", "B10". */
  code: string;
  /** Tên tuyến, ví dụ "Bến Thành — Chợ Lớn". */
  name: string;
}

/**
 * Trần `pageSize` của GET /routes là 100 — `[Range(1, 100)]` trong ListRoutesRequest,
 * xem docs/api-contract.md. Đây là số lớn nhất lấy được trong một lượt gọi.
 */
const ROUTE_PICKER_PAGE_SIZE = 100;

/**
 * Danh sách tuyến cho ô chọn — lấy hết từ GET /routes, cả tuyến ngừng khai thác (vẫn có
 * thể cần xem chuyến đã huỷ hay đã chạy xong của tuyến đó).
 */
export async function fetchRouteOptions(): Promise<TripRouteOption[]> {
  const routes: TripRouteOption[] = [];

  // Lặp theo trang cho tới khi đủ `total`: một trang tối đa 100 tuyến, nếu chỉ lấy một
  // trang thì tuyến thứ 101 trở đi âm thầm biến mất khỏi ô chọn mà không báo lỗi.
  for (let page = 1; ; page += 1) {
    const { items, total } = await fetchRoutes({ page, pageSize: ROUTE_PICKER_PAGE_SIZE });

    routes.push(...items.map(({ id, code, name }) => ({ id, code, name })));

    // `items.length === 0` là chốt chặn để không lặp vô hạn nếu `total` sai.
    if (routes.length >= total || items.length === 0) break;
  }

  return routes;
}

/**
 * GET /api/routes/{routeId}/trips — danh sách chuyến theo ngày + lọc trạng thái + phân trang.
 *
 * Chỉ gửi tham số lọc khi thực sự có giá trị, cùng lối `fetchRoutes`: backend coi `status`
 * không khớp mã nào là "lọc ra rỗng" chứ KHÔNG báo lỗi, nên lỡ gửi `status=` rỗng là bảng
 * âm thầm trắng, rất khó lần ra.
 */
export function fetchTrips(routeId: string, params: TripListParams): Promise<TripListResult> {
  const query: Record<string, string | number> = { page: params.page, pageSize: params.pageSize };

  if (params.from) query.from = params.from;
  if (params.to) query.to = params.to;
  if (params.status) query.status = params.status;

  return axiosClient.get<TripListResult, TripListResult>(`/routes/${routeId}/trips`, {
    params: query,
  });
}
