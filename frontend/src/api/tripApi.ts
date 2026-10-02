import dayjs from 'dayjs';
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

/**
 * Body của PUT /routes/{routeId}/trips/{id} — sửa một chuyến (đổi xe/giờ chạy/trạng thái).
 * Khớp `UpdateTripRequest` của backend, xem docs/api-contract.md.
 */
export interface UpdateTripPayload {
  /** Xe chạy chuyến — bắt buộc, GUID xe có thật và đang `Active`. */
  busId: string;
  /** Giờ khởi hành — bắt buộc, ISO 8601 có kèm múi giờ. */
  departureTime: string;
  /** Giờ dự kiến tới bến cuối. null = bỏ hẳn. */
  arrivalTime: string | null;
  /** Trạng thái — bỏ trống = giữ nguyên trạng thái hiện tại. */
  status?: TripStatus;
}

/**
 * PUT /api/routes/{routeId}/trips/{id} — sửa xe / giờ chạy / trạng thái.
 *
 * PUT là sửa TOÀN PHẦN đối với `busId` + `departureTime` (cả hai bắt buộc) và `arrivalTime`
 * (null = bỏ hẳn), nên khi chỉ muốn đổi xe, caller phải gửi kèm đủ giờ chạy hiện tại của
 * chuyến — nếu không `departureTime` bị thiếu sẽ 400, `arrivalTime` bị thiếu sẽ thành null.
 */
export function updateTrip(
  routeId: string,
  tripId: string,
  payload: UpdateTripPayload,
): Promise<Trip> {
  return axiosClient.put<Trip, Trip>(`/routes/${routeId}/trips/${tripId}`, payload);
}

// -----------------------------------------------------------------------------
// Cảnh báo trùng lịch xe/tài xế — task "Cảnh báo trực quan khi trùng lịch xe hoặc
// tài xế trên UI" (story 14, Nguyễn Đình Băng).
//
// Đây là phép kiểm tra chạy PHÍA FRONTEND, chỉ để hiện cảnh báo trực quan — không
// thay thế phép kiểm tra của backend. Backend chỉ chặn trùng khung giờ khi TẠO chuyến
// (POST/generate); PUT /routes/{routeId}/trips/{id} (thao tác gán xe của màn hình này)
// cố ý KHÔNG kiểm tra trùng (xem api-contract.md mục "Hai kiểm tra khi tạo lịch trình"),
// nên cảnh báo dưới đây là chốt chặn duy nhất cho luồng đổi xe.
//
// Luật trùng khớp ĐÚNG với RouteTripsService.CreateAsync: khung giờ một chuyến là khoảng
// [giờ khởi hành, giờ đến]; chuyến chưa có giờ đến thì coi là một mốc. Chuyến nối đuôi
// (chuyến này đến đúng giờ chuyến kia khởi hành) KHÔNG tính là trùng. Chỉ chuyến đang
// chiếm chỗ trên thời gian biểu (Scheduled/Running) mới tính — Cancelled/Completed không
// chặn chỗ nữa.
// -----------------------------------------------------------------------------

/** Khung giờ của một chuyến — đủ cho phép so trùng, rút từ chính `Trip`. */
export interface TripTimeWindow {
  departureTime: string;
  arrivalTime: string | null;
}

/**
 * Hai chuyến có trùng khung giờ không — khớp luật kiểm tra trùng của backend.
 *
 * `aDep.isSame(bDep)` chặn trường hợp hai chuyến "mốc" (chưa có giờ đến) cùng giờ khởi
 * hành: khoảng của cả hai đều rỗng nên phép so khoảng bên dưới không tự bắt được.
 */
export function tripsOverlap(a: TripTimeWindow, b: TripTimeWindow): boolean {
  const aDep = dayjs(a.departureTime);
  const aEnd = a.arrivalTime ? dayjs(a.arrivalTime) : aDep;
  const bDep = dayjs(b.departureTime);
  const bEnd = b.arrivalTime ? dayjs(b.arrivalTime) : bDep;

  return aDep.isSame(bDep) || (aDep.isBefore(bEnd) && bDep.isBefore(aEnd));
}

/**
 * Tìm các chuyến trong `trips` trùng lịch với một tài nguyên (xe hoặc tài xế) đang xét.
 *
 * `resourceIdOf` cho biết mỗi chuyến khoá tài nguyên ở đâu — hiện dùng `t => t.busId`;
 * khi backend trả thêm `driverId` trên `TripResponse` thì chỉ cần đổi thành
 * `t => t.driverId` để có cảnh báo trùng tài xế mà không phải viết lại phép so trùng.
 *
 * @param excludeTripId id của chính chuyến đang gán — không bao giờ tự trùng với nó.
 */
export function findConflictingTrips(
  trips: Trip[],
  resourceId: string,
  window: TripTimeWindow,
  excludeTripId: string,
  resourceIdOf: (trip: Trip) => string | null,
): Trip[] {
  return trips.filter(
    (other) =>
      other.id !== excludeTripId &&
      resourceIdOf(other) === resourceId &&
      (other.status === 'Scheduled' || other.status === 'Running') &&
      tripsOverlap(other, window),
  );
}

/**
 * Trần `pageSize` của GET /routes/{routeId}/trips là 100 — `[Range(1, 100)]` trong
 * ListTripsRequest, xem docs/api-contract.md. Cùng trần với các ô chọn tuyến/xe.
 */
const TRIP_CONFLICT_PAGE_SIZE = 100;

/**
 * Toàn bộ chuyến của tuyến trong một ngày — dùng để dò trùng lịch, không dùng để vẽ bảng.
 *
 * Bảng chính phân trang phía server nên chỉ giữ ~10 dòng một trang; phép dò trùng cần nhìn
 * ĐỦ mọi chuyến trong ngày (tối đa 200/ngày theo trần backend) mới không bỏ sót chuyến trùng
 * nằm ở trang khác. Lặp theo trang cho tới khi đủ `total`, cùng lối `fetchRouteOptions`.
 */
export async function fetchAllTripsForDay(
  routeId: string,
  from: string,
  to: string,
): Promise<Trip[]> {
  const all: Trip[] = [];

  for (let page = 1; ; page += 1) {
    const { items, total } = await fetchTrips(routeId, {
      from,
      to,
      page,
      pageSize: TRIP_CONFLICT_PAGE_SIZE,
    });

    all.push(...items);

    // `items.length === 0` là chốt chặn để không lặp vô hạn nếu `total` sai.
    if (all.length >= total || items.length === 0) break;
  }

  return all;
}
