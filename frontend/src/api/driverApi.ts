import axiosClient from './axiosClient';
import type { TripStatus } from './tripApi';

// -----------------------------------------------------------------------------
// API hồ sơ tài xế — nối với DriversController (story 14, task Sprint 2 dòng 21
// "Màn hình hồ sơ tài xế/phụ xe + trạng thái ca làm việc" — Hoàng Văn Thịnh).
//
// Hợp đồng endpoint — xem docs/api-contract.md mục "Hồ sơ tài xế — /drivers":
//   GET /drivers?search=&isActive=&page=&pageSize=
//     → { items: Driver[], total, page, pageSize }
//   GET /drivers/{id}/trips?from=&to=&status=&page=&pageSize=
//     → { items: DriverTrip[], total, page, pageSize }   ← đây chính là "ca làm việc"
//
// Tài xế KHÔNG phải một bảng riêng: đây là Users mang vai trò Driver, và Trips.DriverId
// trỏ thẳng vào Users (quy ước A8.4/A9). Hệ thống KHÔNG có phụ xe — A8.4 ghi rõ
// "Không có Phụ xe", nên màn hình này chỉ có tài xế.
//
// DriversController ĐÃ có thật nên file này KHÔNG có nhánh dữ liệu giả: gọi thẳng API.
//
// Chưa có trường bằng lái: licenseNumber/licenseType/licenseExpiry còn chờ migration của
// Vàng Thị Dăm và CHƯA nằm trong api-contract.md, nên màn hình không hiển thị. Thêm được
// khi nào hợp đồng có — xem docs/api-contract.md:1187-1191.
// -----------------------------------------------------------------------------

/** Một tài xế — khớp `DriverResponse` của backend, xem docs/api-contract.md. */
export interface Driver {
  id: string;
  fullName: string;
  phoneNumber: string;
  /** null khi tài xế không có email. */
  email: string | null;
  /** false = tài khoản đã bị khoá (xoá mềm) — vẫn xem được hồ sơ nhưng không phân công được. */
  isActive: boolean;
  createdAt: string;
}

export interface DriverListParams {
  /** Tìm theo tên hoặc số điện thoại — không phân biệt hoa thường. */
  search?: string;
  /** Bỏ trống = lấy cả tài khoản đang làm lẫn đã khoá. */
  isActive?: boolean;
  page: number;
  pageSize: number;
}

export interface DriverListResult {
  /** Chỉ là trang hiện tại, không phải toàn bộ. */
  items: Driver[];
  /** Tổng số dòng KHỚP BỘ LỌC (không phải số dòng trong `items`) — dùng để vẽ phân trang. */
  total: number;
  page: number;
  pageSize: number;
}

/**
 * Một chuyến trong ca làm việc — khớp `DriverTripResponse` của backend.
 *
 * Backend CỐ Ý bỏ các trường vị trí (currentStopId/currentLat/currentLng/positionUpdatedAt)
 * khỏi kiểu này (docs/api-contract.md:1341-1343), nên màn hình ca làm việc chỉ hiển thị
 * dữ liệu lịch trình, không có vị trí trực tiếp.
 */
export interface DriverTrip {
  id: string;
  routeId: string;
  /** Mã tuyến hiển thị cho hành khách, ví dụ "01", "B10". */
  routeCode: string;
  routeName: string;
  busId: string;
  /** Biển số xe — backend kèm sẵn để hiển thị mà không phải gọi thêm API xe. */
  busLicensePlate: string;
  /** Giờ khởi hành thực tế — ISO 8601 UTC (hậu tố Z). */
  departureTime: string;
  /** Giờ dự kiến tới bến cuối. null khi chưa chốt. */
  arrivalTime: string | null;
  status: TripStatus;
  createdAt: string;
  updatedAt: string | null;
}

export interface DriverTripListParams {
  /** ISO 8601 CÓ KÈM múi giờ — xem docs/api-contract.md mục "Giờ gửi lên phải kèm múi giờ". */
  from?: string;
  to?: string;
  status?: TripStatus;
  page: number;
  pageSize: number;
}

export interface DriverTripListResult {
  items: DriverTrip[];
  total: number;
  page: number;
  pageSize: number;
}

/**
 * Trần `pageSize` khi lấy chuyến của một tài xế.
 *
 * Một tài xế chạy trong ngày hiếm khi quá vài chuyến, nên 100 là thừa sức cho một ngày.
 * Chọn 100 (mức trần của các endpoint danh sách khác trong hệ thống) để nếu có lấy rộng
 * hơn một ngày thì vẫn không bị cắt âm thầm.
 */
export const DRIVER_TRIP_PAGE_SIZE = 100;

/**
 * GET /api/drivers — danh sách tài xế + tìm kiếm + lọc trạng thái tài khoản + phân trang.
 *
 * Chỉ gửi tham số lọc khi thực sự có giá trị: backend coi `isActive` không khớp mã nào là
 * "lọc ra rỗng" chứ KHÔNG báo lỗi, nên lỡ gửi `isActive=` rỗng là bảng âm thầm trắng.
 */
export function fetchDrivers(params: DriverListParams): Promise<DriverListResult> {
  const query: Record<string, string | number | boolean> = {
    page: params.page,
    pageSize: params.pageSize,
  };

  if (params.search) query.search = params.search;
  if (params.isActive !== undefined) query.isActive = params.isActive;

  return axiosClient.get<DriverListResult, DriverListResult>('/drivers', { params: query });
}

/**
 * GET /api/drivers/{id}/trips — ca làm việc của một tài xế, sắp xếp theo giờ khởi hành tăng dần.
 *
 * Truyền `from`/`to` để giới hạn về đúng một ngày; bỏ trống là lấy toàn bộ lịch sử chạy xe,
 * vốn không phải thứ màn hình này cần.
 */
export function fetchDriverTrips(
  driverId: string,
  params: DriverTripListParams,
): Promise<DriverTripListResult> {
  const query: Record<string, string | number> = { page: params.page, pageSize: params.pageSize };

  if (params.from) query.from = params.from;
  if (params.to) query.to = params.to;
  if (params.status) query.status = params.status;

  return axiosClient.get<DriverTripListResult, DriverTripListResult>(
    `/drivers/${driverId}/trips`,
    { params: query },
  );
}
