import axiosClient from './axiosClient';

// -----------------------------------------------------------------------------
// API tra cứu tuyến — màn hình "Tra cứu tuyến" (User Story 1).
//
// Dùng endpoint CÔNG KHAI GET /routes/search (RouteSearchController — Trần Trung Hiếu,
// story 1): điểm đi/điểm đến khớp theo TÊN TRẠM trên tuyến, kèm danh sách trạm và giá thấp
// nhất. Không gắn [Authorize] nên ai cũng gọi được, kể cả khách chưa đăng nhập — hợp đồng ở
// mục "Tra cứu tuyến — /routes/search" của docs/api-contract.md.
//
// Trước đây màn này ghép tạm từ GET /routes + GET /routes/{id}/stops + GET /routes/{id}/fares,
// ba endpoint đều sau policy ManagerOrAbove nên hành khách bị 403. Đổi sang /routes/search thì
// màn "Tra cứu tuyến" thành công khai hoàn toàn (xem api-contract.md mục 747).
// -----------------------------------------------------------------------------

/** Tham số tìm tuyến từ form "điểm đi - điểm đến - ngày". */
export interface RouteLookupParams {
  /** Điểm đi — tên trạm, khớp `Stop.name` trên tuyến. */
  origin: string;

  /** Điểm đến — khớp `Stop.name` trên tuyến. */
  destination: string;

  /**
   * Ngày đi (yyyy-MM-dd). Có ngày thì endpoint chỉ trả tuyến có ít nhất một chuyến Scheduled
   * khởi hành trong trọn ngày đó (giờ Việt Nam) — truyền thẳng vào query `date`.
   */
  date?: string;
}

/** Một trạm trên tuyến — rút gọn để hiển thị trong kết quả tra cứu. */
export interface RouteLookupStop {
  name: string;
  /** Thứ tự trên tuyến, tính từ 1 — khớp `RouteStop.stopOrder`. */
  order: number;
}

/** Một tuyến khớp điểm đi/điểm đến, kèm trạm và giá để hành khách lựa chọn. */
export interface RouteLookupResult {
  routeId: string;
  routeCode: string;
  routeName: string;
  origin: string;
  destination: string;
  distanceKm: number;

  /** Trạm theo đúng thứ tự chạy. Rỗng nếu tuyến chưa gán trạm nào. */
  stops: RouteLookupStop[];

  /** Giá vé thấp nhất (VND) — dùng hiển thị "giá từ …". `null` khi tuyến chưa cấu hình giá. */
  minPrice: number | null;
}

export interface RouteLookupApi {
  search: (params: RouteLookupParams) => Promise<RouteLookupResult[]>;
}

// Hình dạng thật của GET /routes/search (docs/api-contract.md mục 747) — camelCase như backend
// trả về. Chỉ khai báo phần màn hình cần; không khai báo thừa để ai đọc cũng thấy đúng phần
// được dùng.
interface RouteSearchStop {
  stopId: string;
  stopName: string;
  stopOrder: number;
}

interface RouteSearchResult {
  routeId: string;
  routeCode: string;
  routeName: string;
  origin: string;
  destination: string;
  distanceKm: number;
  stops: RouteSearchStop[];
  minPrice: number | null;
}

// ---------------------------------------------------------------------------
// Cờ chuyển giữa dữ liệu giả và API thật. Đã bật API thật — GET /routes/search có trên main
// (xem ghi chú đầu file). Nhánh giả giữ làm đường lùi: đổi cờ này thành `true` là quay lại
// được, không phải chạm phần nào khác.
const USE_MOCK_DATA = false;

const delay = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms));

// -------- Gọi API thật (dùng khi USE_MOCK_DATA = false) --------
const api: RouteLookupApi = {
  async search({ origin, destination, date }) {
    // Trim trước khi gửi: backend tự bỏ khoảng trắng thừa, nhưng trùng nhau sau khi trim bị 400
    // — gửi sạch ngay từ đây để lỗi (nếu có) hiện sớm và rõ ràng hơn.
    const params: Record<string, string> = {
      origin: origin.trim(),
      destination: destination.trim(),
    };
    if (date) params.date = date;

    const results = await axiosClient.get<RouteSearchResult[], RouteSearchResult[]>(
      '/routes/search',
      { params },
    );

    // Ánh xạ stops: endpoint trả { stopId, stopName, stopOrder }, màn hình dùng { name, order }.
    return results.map((route) => ({
      routeId: route.routeId,
      routeCode: route.routeCode,
      routeName: route.routeName,
      origin: route.origin,
      destination: route.destination,
      distanceKm: route.distanceKm,
      stops: route.stops.map((stop) => ({ name: stop.stopName, order: stop.stopOrder })),
      minPrice: route.minPrice,
    }));
  },
};

// -------- Dữ liệu giả (dùng khi USE_MOCK_DATA = true) --------
// Vài tuyến quanh Hà Nội để nhìn giao diện cho thật — gõ "Mỹ Đình" là ra vài tuyến.
const MOCK_RESULTS: RouteLookupResult[] = [
  {
    routeId: 'route-01',
    routeCode: '01',
    routeName: 'Bến xe Mỹ Đình — Bến xe Gia Lâm',
    origin: 'Bến xe Mỹ Đình',
    destination: 'Bến xe Gia Lâm',
    distanceKm: 18.4,
    stops: [
      { name: 'Bến xe Mỹ Đình', order: 1 },
      { name: 'Cầu Giấy', order: 2 },
      { name: 'Bách Khoa', order: 3 },
      { name: 'Long Biên', order: 4 },
      { name: 'Bến xe Gia Lâm', order: 5 },
    ],
    minPrice: 8000,
  },
  {
    routeId: 'route-02',
    routeCode: '02',
    routeName: 'Bến xe Mỹ Đình — Bến xe Nước Ngầm',
    origin: 'Bến xe Mỹ Đình',
    destination: 'Bến xe Nước Ngầm',
    distanceKm: 14.2,
    stops: [
      { name: 'Bến xe Mỹ Đình', order: 1 },
      { name: 'Ngã Tư Sở', order: 2 },
      { name: 'Giáp Bát', order: 3 },
      { name: 'Bến xe Nước Ngầm', order: 4 },
    ],
    minPrice: 7000,
  },
  {
    routeId: 'route-08',
    routeCode: '08',
    routeName: 'Cầu Giấy — Bờ Hồ Hoàn Kiếm',
    origin: 'Cầu Giấy',
    destination: 'Bờ Hồ Hoàn Kiếm',
    distanceKm: 6.8,
    stops: [
      { name: 'Cầu Giấy', order: 1 },
      { name: 'Kim Mã', order: 2 },
      { name: 'Bờ Hồ Hoàn Kiếm', order: 3 },
    ],
    minPrice: 5000,
  },
  {
    routeId: 'route-09',
    routeCode: '09',
    routeName: 'Bến xe Giáp Bát — Bến xe Mỹ Đình',
    origin: 'Bến xe Giáp Bát',
    destination: 'Bến xe Mỹ Đình',
    distanceKm: 13.5,
    stops: [
      { name: 'Bến xe Giáp Bát', order: 1 },
      { name: 'Ngã Tư Sở', order: 2 },
      { name: 'Cầu Giấy', order: 3 },
      { name: 'Bến xe Mỹ Đình', order: 4 },
    ],
    minPrice: 7000,
  },
  {
    routeId: 'route-B10',
    routeCode: 'B10',
    routeName: 'Bến xe Yên Nghĩa — Bến xe Mỹ Đình',
    origin: 'Bến xe Yên Nghĩa',
    destination: 'Bến xe Mỹ Đình',
    distanceKm: 12.1,
    stops: [
      { name: 'Bến xe Yên Nghĩa', order: 1 },
      { name: 'Ngã Tư Sở', order: 2 },
      { name: 'Cầu Giấy', order: 3 },
      { name: 'Bến xe Mỹ Đình', order: 4 },
    ],
    minPrice: 7000,
  },
];

const mock: RouteLookupApi = {
  async search({ origin, destination }) {
    await delay(500);
    const keywordOrigin = origin.trim().toLowerCase();
    const keywordDestination = destination.trim().toLowerCase();

    return MOCK_RESULTS.filter(
      (route) =>
        route.origin.toLowerCase().includes(keywordOrigin) &&
        route.destination.toLowerCase().includes(keywordDestination),
    );
  },
};

const routeLookupApi: RouteLookupApi = USE_MOCK_DATA ? mock : api;

export default routeLookupApi;
