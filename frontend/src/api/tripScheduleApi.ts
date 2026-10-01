import axiosClient from './axiosClient';
import type { Trip, TripStatus } from './tripApi';

// -----------------------------------------------------------------------------
// API quản lý lịch trình — nối với RouteTripsController (story 13, màn hình của Hạnh).
//
// Hợp đồng endpoint — xem docs/api-contract.md mục "Lịch trình chạy xe — /routes/{routeId}/trips":
//   POST   /routes/{routeId}/trips        → CreateTrip → Trip (201)
//   PUT    /routes/{routeId}/trips/{id}   → UpdateTrip → Trip
//   DELETE /routes/{routeId}/trips/{id}   → huỷ chuyến (chuyển Cancelled) → Trip
//
// Phần ĐỌC (danh sách chuyến theo ngày + danh sách tuyến cho ô chọn) đã nằm ở
// tripApi.ts — file của Hoàng/Băng. Phần thêm/sửa/xoá tách riêng ở đây, không sửa
// file của người khác — cùng lối routeCrudApi.ts tách khỏi routeApi.ts.
//
// Ô chọn xe lấy từ GET /buses?status=Active: đây là màn hình đầu tiên cần danh sách
// xe nên hàm fetchActiveBuses đặt tạm ở đây cho gọn; khi màn hình quản lý đội xe ra
// đời thì tách thành module busApi riêng.
// -----------------------------------------------------------------------------

/** Body khi thêm chuyến — POST /routes/{routeId}/trips (không có status, backend mặc định Scheduled). */
export interface CreateTripPayload {
  busId: string;
  /** ISO 8601 CÓ KÈM múi giờ (ví dụ +07:00) — server quy về UTC khi lưu. */
  departureTime: string;
  /** Giờ dự kiến tới bến cuối. Bỏ trống = null. */
  arrivalTime?: string | null;
}

/** Body khi sửa chuyến — PUT /routes/{routeId}/trips/{id}: thêm status để đổi trạng thái. */
export interface UpdateTripPayload extends CreateTripPayload {
  /** Bỏ trống = giữ nguyên trạng thái hiện tại. */
  status?: TripStatus;
}

/** Một xe trong ô chọn — rút gọn từ Bus, chỉ đủ để hiển thị và gửi busId. */
export interface BusOption {
  id: string;
  licensePlate: string;
  busType: string;
  capacity: number;
}

/** Hình dạng trang của GET /buses — chỉ khai báo đúng trường ta đọc. */
interface BusListResult {
  items: {
    id: string;
    licensePlate: string;
    busType: string;
    capacity: number;
  }[];
  total: number;
}

/** Trần pageSize của GET /buses là 100 — xem docs/api-contract.md mục "Xe buýt". */
const BUS_PICKER_PAGE_SIZE = 100;

/**
 * GET /buses?status=Active — danh sách xe đang khai thác cho ô chọn xe.
 *
 * Chỉ lấy xe Active vì POST /routes/{routeId}/trips kiểm tra xe phải đang khai thác;
 * đưa xe bảo dưỡng/ngừng vào ô chọn chỉ để bấm xong nhận 409.
 */
export async function fetchActiveBuses(): Promise<BusOption[]> {
  const buses: BusOption[] = [];

  // Lặp theo trang cho tới khi đủ total: một trang tối đa 100 xe, chỉ lấy một trang thì
  // xe thứ 101 trở đi âm thầm biến mất khỏi ô chọn mà không báo lỗi.
  for (let page = 1; ; page += 1) {
    const result = await axiosClient.get<BusListResult, BusListResult>('/buses', {
      params: { status: 'Active', page, pageSize: BUS_PICKER_PAGE_SIZE },
    });

    buses.push(
      ...result.items.map(({ id, licensePlate, busType, capacity }) => ({
        id,
        licensePlate,
        busType,
        capacity,
      })),
    );

    // items.length === 0 là chốt chặn để không lặp vô hạn nếu total sai.
    if (buses.length >= result.total || result.items.length === 0) break;
  }

  return buses;
}

/** POST /api/routes/{routeId}/trips — thêm một chuyến lẻ cho tuyến. */
export function createTrip(routeId: string, payload: CreateTripPayload): Promise<Trip> {
  return axiosClient.post<Trip, Trip>(`/routes/${routeId}/trips`, payload);
}

/** PUT /api/routes/{routeId}/trips/{id} — sửa xe / giờ chạy / trạng thái. */
export function updateTrip(
  routeId: string,
  id: string,
  payload: UpdateTripPayload,
): Promise<Trip> {
  return axiosClient.put<Trip, Trip>(`/routes/${routeId}/trips/${id}`, payload);
}

/** DELETE /api/routes/{routeId}/trips/{id} — huỷ chuyến (chuyển Cancelled), trả Trip đã huỷ. */
export function cancelTrip(routeId: string, id: string): Promise<Trip> {
  return axiosClient.delete<Trip, Trip>(`/routes/${routeId}/trips/${id}`);
}
