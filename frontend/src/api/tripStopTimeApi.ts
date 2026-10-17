import axiosClient from './axiosClient';

// -----------------------------------------------------------------------------
// Giờ đến TỪNG TRẠM của một chuyến — nối với TripStopTimesController.
//
// Hợp đồng đầy đủ: docs/api-contract.md mục
// "Giờ đến từng trạm — /routes/{routeId}/trips/{id}/stop-times".
//
//   GET  → dòng thời gian đầy đủ: giờ dự kiến suy ra từ RouteStops.DistanceKm
//          + các mốc quản trị viên đã sửa tay.
//   PUT  → ghi đè giờ các TRẠM TRUNG GIAN, thay thế toàn phần:
//          mảng rỗng = xoá hết mốc ghi đè, quay về giờ dự kiến.
//
// Trạm đầu / bến cuối là mốc neo của chuyến (DepartureTime / ArrivalTime) nên
// không bao giờ gửi lên ở đây — sửa hai mốc đó ở form Sửa chuyến.
// -----------------------------------------------------------------------------

/** Một trạm trong dòng thời gian của chuyến — khớp TripStopTimeResponse của backend. */
export interface TripStopTime {
  stopId: string;
  stopName: string;
  stopAddress: string;
  stopOrder: number;
  /** Khoảng cách từ trạm liền trước; trạm đầu luôn 0. */
  distanceKm: number;
  /** Khoảng cách cộng dồn từ trạm đầu. */
  cumulativeDistanceKm: number;
  /** Giờ đến đang có hiệu lực — giờ sửa tay nếu có, ngược lại là giờ dự kiến. */
  arrivalTime: string | null;
  /** Giờ suy ra từ km; vẫn có kể cả khi đã bị ghi đè. */
  derivedArrivalTime: string | null;
  /** True khi giờ do quản trị viên đặt tay cho đúng chuyến này. */
  isManual: boolean;
  isFirst: boolean;
  isLast: boolean;
}

/** GET /api/routes/{routeId}/trips/{id}/stop-times. */
export interface TripStopTimesResponse {
  tripId: string;
  routeId: string;
  departureTime: string;
  /** Giờ đến bến cuối đang có hiệu lực (đã lưu hoặc ước lượng). */
  arrivalTime: string | null;
  /** True khi Trips.ArrivalTime trống và bến cuối được ước lượng từ km. */
  arrivalTimeIsEstimated: boolean;
  /** False khi tuyến chưa đủ km để suy ra giờ trung gian — khi đó backend trả null, không bịa giờ. */
  hasDistanceData: boolean;
  totalDistanceKm: number;
  stops: TripStopTime[];
}

/** Một mốc ghi đè gửi lên — arrivalTime null = bỏ ghi đè của trạm đó. */
export interface UpdateTripStopTimeItem {
  stopId: string;
  arrivalTime: string | null;
}

export interface UpdateTripStopTimesPayload {
  /**
   * Danh sách ghi đè, THAY THẾ TOÀN PHẦN. Mảng rỗng = xoá hết mốc ghi đè.
   * Chỉ gửi trạm trung gian; backend trả 400 nếu có trạm đầu / bến cuối.
   */
  items: UpdateTripStopTimeItem[];
}

/** GET /api/routes/{routeId}/trips/{id}/stop-times — dòng thời gian đầy đủ của chuyến. */
export function fetchTripStopTimes(
  routeId: string,
  tripId: string,
): Promise<TripStopTimesResponse> {
  return axiosClient.get<TripStopTimesResponse, TripStopTimesResponse>(
    `/routes/${routeId}/trips/${tripId}/stop-times`,
  );
}

/** PUT /api/routes/{routeId}/trips/{id}/stop-times — ghi đè giờ trạm trung gian. */
export function updateTripStopTimes(
  routeId: string,
  tripId: string,
  items: UpdateTripStopTimeItem[],
): Promise<TripStopTimesResponse> {
  return axiosClient.put<TripStopTimesResponse, TripStopTimesResponse>(
    `/routes/${routeId}/trips/${tripId}/stop-times`,
    { items } satisfies UpdateTripStopTimesPayload,
  );
}
