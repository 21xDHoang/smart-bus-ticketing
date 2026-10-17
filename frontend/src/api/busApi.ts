import axiosClient from './axiosClient';

// -----------------------------------------------------------------------------
// API đội xe — nối với BusesController (story 14, Trần Trung Hiếu).
//
// Hợp đồng endpoint — xem docs/api-contract.md mục "Xe buýt — /buses":
//   GET /buses?search=&status=&page=&pageSize=
//     → { items: Bus[], total, page, pageSize }
//
// BusesController ĐÃ có thật nên file này KHÔNG có nhánh dữ liệu giả: gọi thẳng API.
// -----------------------------------------------------------------------------

/** Trạng thái xe — khớp enum BusStatus của backend, đúng thứ tự khai báo. */
export type BusStatus = 'Active' | 'Maintenance' | 'Inactive';

/** Trạng thái chuyến — khớp enum TripStatus của backend. */
export type BusTripStatus = 'Scheduled' | 'Running' | 'Completed' | 'Cancelled';

/**
 * Một tuyến xe đang chạy hoặc sẽ chạy — khớp `BusRouteAssignmentResponse`.
 *
 * Hệ thống không có bảng gán xe ↔ tuyến: quan hệ suy ra từ các chuyến trong 7 ngày tới, nên
 * `tripCount` là số chuyến trong cửa sổ đó chứ không phải một phân công cố định.
 */
export interface BusRouteAssignment {
  routeId: string;
  /** Mã tuyến hiển thị — "01", "B10". */
  routeCode: string;
  routeName: string;
  /** Số chuyến của xe trên tuyến trong cửa sổ 7 ngày (kể cả chuyến đang chạy). */
  tripCount: number;
  /** Chuyến Scheduled gần nhất của tuyến; null khi tuyến chỉ còn chuyến đang chạy. */
  nextDepartureTime: string | null;
}

/** Tóm tắt một chuyến của xe — dùng cho `currentTrip` và `nextTrip`. */
export interface BusTripSummary {
  tripId: string;
  routeId: string;
  routeCode: string;
  routeName: string;
  /** Giờ khởi hành — ISO 8601 UTC. */
  departureTime: string;
  /** null khi chuyến chưa được phân công tài xế. */
  driverId: string | null;
  driverName: string | null;
  status: BusTripStatus;
  /** Trạm gần nhất đã qua — chỉ có nghĩa với chuyến đang chạy. */
  currentStopName: string | null;
}

/** Hình dạng sơ đồ ghế của loại xe — mỗi loại xe đúng một sơ đồ. */
export interface BusSeatLayoutSummary {
  numberOfFloors: number;
  rowsPerFloor: number;
  columnsPerRow: number;
  totalSeats: number;
  vipSeatCount: number;
}

/** Tình trạng ghế thật của xe so với sức chứa khai báo. */
export interface BusSeatSummary {
  seatsCreated: number;
  capacity: number;
  /** false = xe chưa được sinh đủ ghế, cần cấu hình lại sơ đồ. */
  isComplete: boolean;
  /** null khi loại xe chưa cấu hình sơ đồ ghế. */
  layout: BusSeatLayoutSummary | null;
}

/** Một xe buýt — khớp `BusResponse` của backend, xem docs/api-contract.md. */
export interface Bus {
  id: string;
  /** Biển số xe — "29B-123.45". Duy nhất toàn hệ thống. */
  licensePlate: string;
  /** Loại xe — "Xe buýt 45 chỗ", "Xe buýt điện". */
  busType: string;
  /** Sức chứa theo số ghế. */
  capacity: number;
  status: BusStatus;
  /** Tuyến xe đang chạy / sẽ chạy trong 7 ngày tới. Mảng rỗng khi chưa xếp chuyến. */
  assignedRoutes: BusRouteAssignment[];
  /** Chuyến đang chạy; null khi xe không chạy. */
  currentTrip: BusTripSummary | null;
  /** Chuyến Scheduled gần nhất trong 7 ngày tới; null khi không có. */
  nextTrip: BusTripSummary | null;
  seatSummary: BusSeatSummary;
  createdAt: string;
  /** null khi xe chưa được sửa lần nào. */
  updatedAt: string | null;
}

export interface BusListParams {
  /** Tìm theo biển số hoặc loại xe — không phân biệt hoa thường. */
  search?: string;
  /** Bỏ trống = lấy cả ba trạng thái. */
  status?: BusStatus;
  page: number;
  pageSize: number;
}

export interface BusListResult {
  /** Chỉ là trang hiện tại, không phải toàn bộ. */
  items: Bus[];
  /** Tổng số dòng KHỚP BỘ LỌC (không phải số dòng trong `items`) — dùng để vẽ phân trang. */
  total: number;
  page: number;
  pageSize: number;
}

/**
 * Một chuyến đang hoạt động trùng khung giờ với tài xế vừa phân công — khớp
 * `ConflictingTripResponse` của backend.
 *
 * Đây là CẢNH BÁO, không phải lỗi: backend vẫn lưu phân công và trả 200. Người điều hành
 * được phép cố ý chấp nhận trùng lịch (ví dụ tài xế chạy nối chuyến).
 */
export interface ConflictingTrip {
  id: string;
  routeId: string;
  routeCode: string;
  routeName: string;
  busId: string;
  driverId: string | null;
  departureTime: string;
  arrivalTime: string | null;
  status: BusTripStatus;
}

/** Kết quả `PATCH /trips/{id}/assignment` — khớp `TripAssignmentResponse` của backend. */
export interface TripDriverAssignmentResult {
  id: string;
  driverId: string | null;
  driverName: string | null;
  conflicts: {
    busConflicts: ConflictingTrip[];
    driverConflicts: ConflictingTrip[];
  };
}

/**
 * PATCH /api/trips/{id}/assignment — gán tài xế cho MỘT chuyến (luồng sự cố/điều hành).
 *
 * Dùng endpoint này thay vì `PATCH /routes/{routeId}/trips/driver-assignment` vì màn hình đội xe
 * đã có sẵn `tripId` của chuyến đang chạy / chuyến kế tiếp, không cần biết tuyến, và response
 * kèm `conflicts` để cảnh báo trùng lịch ngay.
 *
 * API cố ý KHÔNG có cách bỏ phân công (truyền `driverId: null`): muốn đổi thì chọn tài xế khác.
 */
export function assignTripDriver(
  tripId: string,
  driverId: string,
): Promise<TripDriverAssignmentResult> {
  return axiosClient.patch<TripDriverAssignmentResult, TripDriverAssignmentResult>(
    `/trips/${tripId}/assignment`,
    { driverId },
  );
}

interface BusStatusMeta {
  label: string;
  /** Tên màu preset của Tag AntD. */
  color: string;
}

export const BUS_STATUS_META: Record<BusStatus, BusStatusMeta> = {
  Active: { label: 'Đang khai thác', color: 'success' },
  Maintenance: { label: 'Bảo dưỡng', color: 'warning' },
  Inactive: { label: 'Ngừng khai thác', color: 'default' },
};

/**
 * Trần `pageSize` của GET /buses là 100 — `[Range(1, 100)]` trong ListBusesRequest,
 * xem docs/api-contract.md. Đây là số lớn nhất lấy được trong một lượt gọi.
 */
const BUS_PICKER_PAGE_SIZE = 100;

/** Một xe trong ô chọn — rút gọn từ Bus, chỉ đủ để hiển thị. */
export interface BusOption {
  id: string;
  licensePlate: string;
  busType: string;
  capacity: number;
}

/**
 * GET /api/buses — danh sách xe + tìm kiếm + lọc trạng thái + phân trang.
 *
 * Chỉ gửi tham số lọc khi thực sự có giá trị: backend coi `status` không khớp mã nào là
 * "lọc ra rỗng" chứ KHÔNG báo lỗi, nên lỡ gửi `status=` rỗng là bảng âm thầm trắng.
 */
export function fetchBuses(params: BusListParams): Promise<BusListResult> {
  const query: Record<string, string | number> = { page: params.page, pageSize: params.pageSize };

  if (params.search) query.search = params.search;
  if (params.status) query.status = params.status;

  return axiosClient.get<BusListResult, BusListResult>('/buses', { params: query });
}

/**
 * Danh sách xe ĐANG KHAI THÁC cho ô "chọn xe" — lấy hết từ GET /buses?status=Active.
 *
 * Chỉ xe `Active` mới gán được vào chuyến: điều kiện `busId` của POST/PUT
 * /routes/{routeId}/trips kiểm tra trạng thái `Active` (docs/api-contract.md), nên ô chọn
 * không cần loại xe bảo dưỡng hay ngừng khai thác.
 */
export async function fetchActiveBusOptions(): Promise<BusOption[]> {
  const options: BusOption[] = [];

  // Lặp theo trang cho tới khi đủ `total`: một trang tối đa 100 xe, nếu chỉ lấy một trang
  // thì xe thứ 101 trở đi âm thầm biến mất khỏi ô chọn mà không báo lỗi.
  for (let page = 1; ; page += 1) {
    const { items, total } = await fetchBuses({
      status: 'Active',
      page,
      pageSize: BUS_PICKER_PAGE_SIZE,
    });

    options.push(
      ...items.map(({ id, licensePlate, busType, capacity }) => ({
        id,
        licensePlate,
        busType,
        capacity,
      })),
    );

    // `items.length === 0` là chốt chặn để không lặp vô hạn nếu `total` sai.
    if (options.length >= total || items.length === 0) break;
  }

  return options;
}
