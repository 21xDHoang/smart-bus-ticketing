import axiosClient from './axiosClient';

// -----------------------------------------------------------------------------
// API trạm trên tuyến — nối với RouteStopsController (Nguyễn Duy Kiên, story 12).
//
// Hợp đồng đầy đủ — docs/api-contract.md mục "Trạm trên tuyến — /routes/{routeId}/stops":
//   GET    /routes/{routeId}/stops        → RouteStop[]  (đã xếp theo stopOrder)
//   POST   /routes/{routeId}/stops        → RouteStop    (201, nối vào CUỐI tuyến)
//   PUT    /routes/{routeId}/stops/order  → RouteStop[]  (thay TOÀN BỘ thứ tự)
//   DELETE /routes/{routeId}/stops/{id}   → 204 No Content
//
// Backend đã chạy thật trên CSDL nên file này KHÔNG có nhánh dữ liệu giả như
// stopApi.ts / fareApi.ts — cùng lối với routeApi.ts.
// -----------------------------------------------------------------------------

/** Một dòng bảng nối RouteStops — một trạm nằm trên một tuyến, kèm thứ tự xe chạy. */
export interface RouteStop {
  /**
   * Khoá chính của DÒNG bảng nối. Đây là giá trị dùng cho
   * `DELETE /routes/{routeId}/stops/{id}` — KHÔNG phải `stopId`.
   *
   * Ba nguồn xác nhận: `docs/api-contract.md` mục
   * `DELETE /routes/{routeId}/stops/{id}`, chú thích tham số `id` ở
   * `RouteStopsController.Remove`, và câu truy vấn của `RouteStopService.RemoveAsync`:
   * `rs => rs.RouteId == routeId && rs.Id == id`.
   * Gửi nhầm `stopId` vào DELETE sẽ ăn 404 ở mọi lần gỡ trạm.
   */
  id: string;

  routeId: string;

  /**
   * Khoá của TRẠM. Dùng cho `POST /routes/{routeId}/stops` và `PUT /routes/{routeId}/stops/order`
   * (hai endpoint này nhận `stopId`), và để đối chiếu trạm nào đã nằm trên tuyến.
   * KHÔNG dùng cho DELETE — xem chú thích ở `id`.
   */
  stopId: string;

  /** Tên trạm — backend trả kèm để màn hình khỏi phải gọi thêm /stops rồi tự ghép. */
  stopName: string;

  stopAddress: string;

  latitude: number;
  longitude: number;

  /** Thứ tự trên tuyến, tính từ 1 và liên tục. Do server suy ra từ vị trí trong mảng. */
  stopOrder: number;

  /** Khoảng cách từ trạm liền trước tới trạm này (km). Trạm đầu tiên = 0. */
  distanceKm: number;
}

/**
 * Một trạm dừng lấy từ `GET /stops`, dùng cho ô chọn "gán trạm vào tuyến".
 *
 * Cố ý KHÔNG import từ `stopApi.ts`: file đó còn `USE_MOCK_DATA = true` và id giả kiểu
 * `"stop-1"` — không phải GUID. Backend đòi `stopId` là GUID của trạm có thật
 * (docs/api-contract.md), gửi id giả lên là 404.
 */
export interface StopOption {
  id: string;
  name: string;
  address: string;
  latitude: number;
  longitude: number;
}

/** Body của `POST /routes/{routeId}/stops`. */
export interface AssignStopPayload {
  stopId: string;

  /** Bỏ trống = 0. Trần 9999.99 (cột numeric(6,2) của CSDL). */
  distanceKm?: number;
}

/** Một phần tử trong `items` của `PUT /routes/{routeId}/stops/order`. */
export interface ReorderRouteStopItem {
  stopId: string;

  /**
   * Bỏ trống = GIỮ NGUYÊN khoảng cách đang có, KHÔNG phải gán 0. Thao tác này là
   * "sắp xếp lại thứ tự", không phải "viết lại khoảng cách" — gửi 0 sẽ xoá sạch
   * khoảng cách quản lý đã nhập (docs/api-contract.md).
   */
  distanceKm?: number;
}

/** GET /routes/{routeId}/stops — trả mảng đã xếp theo `stopOrder`, rỗng nếu tuyến chưa gán trạm. */
export function fetchRouteStops(routeId: string): Promise<RouteStop[]> {
  return axiosClient.get<RouteStop[], RouteStop[]>(`/routes/${routeId}/stops`);
}

/** GET /stops — danh sách trạm dừng có thật, để chọn mà gán vào tuyến. */
export function fetchStopOptions(): Promise<StopOption[]> {
  return axiosClient.get<StopOption[], StopOption[]>('/stops');
}

/** POST /routes/{routeId}/stops — gán một trạm vào CUỐI tuyến. Trạm đã có trên tuyến → 409. */
export function assignStop(routeId: string, payload: AssignStopPayload): Promise<RouteStop> {
  return axiosClient.post<RouteStop, RouteStop>(`/routes/${routeId}/stops`, payload);
}

/**
 * PUT /routes/{routeId}/stops/order — thay thứ tự TOÀN PHẦN.
 *
 * Phải gửi ĐỦ và ĐÚNG tập trạm hiện có của tuyến: thiếu hay thừa một `stopId` đều bị
 * 400. Đây là lưới an toàn cho thao tác kéo-thả — gửi thiếu một dòng mà server cứ ghi
 * đè thì trạm đó bị gỡ khỏi tuyến mà không ai chủ ý. Muốn gỡ trạm thì gọi `removeRouteStop`.
 */
export function reorderRouteStops(
  routeId: string,
  items: ReorderRouteStopItem[],
): Promise<RouteStop[]> {
  return axiosClient.put<RouteStop[], RouteStop[]>(`/routes/${routeId}/stops/order`, { items });
}

/** DELETE /routes/{routeId}/stops/{id} — `id` là khoá DÒNG bảng nối, xem chú thích ở `RouteStop.id`. */
export function removeRouteStop(routeId: string, id: string): Promise<void> {
  return axiosClient.delete<never, void>(`/routes/${routeId}/stops/${id}`);
}
