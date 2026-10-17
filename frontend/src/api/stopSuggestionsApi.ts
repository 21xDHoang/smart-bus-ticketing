import axiosClient from './axiosClient';

// -----------------------------------------------------------------------------
// Gợi ý trạm cho ô điểm đi/điểm đến của màn Tra cứu tuyến (GET /stops/suggestions).
//
// Khác stopSearchApi.ts (GET /stops/search, ô chọn trạm dùng chung, trả MỌI trạm trong CSDL):
// endpoint này CHỈ trả trạm thuộc tuyến đang khai thác. Đây là đường sửa lỗi "ô gợi ý hiện cả
// trạm của tuyến đã xoá" — trạm mồ côi không bao giờ xuất hiện.
//
// Có `originStopId` thì chỉ gợi ý trạm đứng sau điểm đi trên cùng tuyến (dùng cho ô điểm đến);
// có `destinationStopId` thì chỉ gợi ý trạm đứng trước điểm đến (dùng cho ô điểm đi). Nhờ vậy
// dropdown không đầy trạm ngược chiều.
//
// Endpoint CÔNG KHAI, hợp đồng ở mục "Gợi ý trạm cho tra cứu tuyến — /stops/suggestions" của
// docs/api-contract.md.
// -----------------------------------------------------------------------------

/** Một tuyến Active đi qua trạm — đủ để hiện badge "01TL-PY". */
export interface StopSuggestionRoute {
  routeId: string;
  routeCode: string;
  routeName: string;
}

/** Trạm gợi ý — kèm các tuyến đi qua để hành khách phân biệt trạm trùng tên. */
export interface StopSuggestion {
  id: string;
  name: string;
  address: string;
  latitude: number;
  longitude: number;
  routes: StopSuggestionRoute[];
}

export interface StopSuggestionsParams {
  /** Từ khoá — khớp tên hoặc địa chỉ, không phân biệt hoa thường và không phân biệt dấu. */
  keyword?: string;
  /** Số gợi ý tối đa, 1..50; bỏ trống = 10. */
  limit?: number;
  /** Điểm đi đã chọn — chỉ gợi ý trạm đứng sau nó trên cùng tuyến. */
  originStopId?: string;
  /** Điểm đến đã chọn — chỉ gợi ý trạm đứng trước nó trên cùng tuyến. */
  destinationStopId?: string;
}

export interface StopSuggestionsApi {
  search: (params: StopSuggestionsParams) => Promise<StopSuggestion[]>;
}

const api: StopSuggestionsApi = {
  async search({ keyword, limit, originStopId, destinationStopId }) {
    const params: Record<string, string | number> = {};
    const trimmed = keyword?.trim();

    if (trimmed) params.keyword = trimmed;
    if (limit !== undefined) params.limit = limit;
    if (originStopId) params.originStopId = originStopId;
    if (destinationStopId) params.destinationStopId = destinationStopId;

    return axiosClient.get<StopSuggestion[], StopSuggestion[]>('/stops/suggestions', { params });
  },
};

export default api;
