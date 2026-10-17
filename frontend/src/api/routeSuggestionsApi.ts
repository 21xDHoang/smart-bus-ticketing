import axiosClient from './axiosClient';

// -----------------------------------------------------------------------------
// Gợi ý tuyến cho màn Tra cứu tuyến (GET /routes/suggestions) — endpoint CÔNG KHAI.
//
// Khác routeLookupApi.ts (GET /routes/search): ngày KHÔNG lọc tuyến. Tuyến nối được hai trạm
// luôn có trong `matches`, kèm lịch chạy và ngày gần nhất. Nhờ vậy màn hình không còn rơi vào
// ô "Không tìm thấy tuyến phù hợp" chỉ vì ngày đó chưa có chuyến — đúng lỗi cần sửa.
//
// Hợp đồng đầy đủ ở mục "Gợi ý tuyến cho ngày đã chọn — /routes/suggestions" của
// docs/api-contract.md.
// -----------------------------------------------------------------------------

export interface RouteSuggestionStop {
  stopId: string;
  stopName: string;
  stopOrder: number;
}

/** Một tuyến nối đúng hai trạm, kèm lịch chạy quanh ngày đã chọn. */
export interface RouteSuggestion {
  routeId: string;
  routeCode: string;
  routeName: string;
  origin: string;
  destination: string;
  distanceKm: number;
  stops: RouteSuggestionStop[];
  minPrice: number | null;

  /** Có chuyến trong trọn ngày đã chọn (giờ Việt Nam). */
  hasTripsOnDate: boolean;

  /** Có chuyến mỗi ngày từ hôm nay đến `bookingHorizonEnd`. */
  runsDaily: boolean;

  /** Các ngày trong cửa sổ có chuyến — `yyyy-MM-dd`, tăng dần. */
  availableDates: string[];

  /** Ngày gần ngày đã chọn nhất có chuyến; hoà khoảng cách chọn ngày tương lai. */
  nearestAvailableDate: string | null;

  /** Giờ khởi hành sớm nhất trong ngày gần nhất — ISO 8601 kèm offset +07:00. */
  nextDeparture: string | null;

  /** Số chuyến trong ngày gần nhất. */
  departureCountOnNearestDate: number;
}

export interface StopSuggestionInfo {
  id: string;
  name: string;
  address: string;
  onActiveRoute: boolean;
}

export type RouteSuggestionEmptyReason =
  | 'noActiveRoute'
  | 'originNotServed'
  | 'destinationNotServed'
  | 'noRouteBetweenStops'
  | 'dateBeyondHorizon'
  | 'noTripsThatDay';

export interface RouteSuggestionsResponse {
  selectedDate: string;
  bookingHorizonEnd: string;
  origin: StopSuggestionInfo;
  destination: StopSuggestionInfo;
  matches: RouteSuggestion[];
  alternatives: {
    fromOrigin: RouteSuggestion[];
    toDestination: RouteSuggestion[];
  };
  emptyReason: RouteSuggestionEmptyReason | null;
}

export interface RouteSuggestionsParams {
  /** Điểm đi — khoá trạm; ưu tiên hơn `origin`. */
  originStopId?: string;
  /** Điểm đến — khoá trạm; ưu tiên hơn `destination`. */
  destinationStopId?: string;
  /** Tên điểm đi — chỉ dùng khi không có id. */
  origin?: string;
  /** Tên điểm đến — cùng lối. */
  destination?: string;
  /** Ngày đi `yyyy-MM-dd`; bỏ trống = hôm nay (giờ Việt Nam). */
  date?: string;
  /** Số ngày nhìn tới, 1..14; bỏ trống = 7. */
  windowDays?: number;
}

export interface RouteSuggestionsApi {
  search: (params: RouteSuggestionsParams) => Promise<RouteSuggestionsResponse>;
}

const api: RouteSuggestionsApi = {
  async search(params) {
    const query: Record<string, string | number> = {};

    if (params.originStopId) query.originStopId = params.originStopId;
    else if (params.origin?.trim()) query.origin = params.origin.trim();

    if (params.destinationStopId) query.destinationStopId = params.destinationStopId;
    else if (params.destination?.trim()) query.destination = params.destination.trim();

    if (params.date) query.date = params.date;
    if (params.windowDays !== undefined) query.windowDays = params.windowDays;

    return axiosClient.get<RouteSuggestionsResponse, RouteSuggestionsResponse>(
      '/routes/suggestions',
      { params: query },
    );
  },
};

export default api;
