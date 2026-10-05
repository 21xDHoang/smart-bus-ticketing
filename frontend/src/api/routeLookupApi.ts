import axiosClient from './axiosClient';

// -----------------------------------------------------------------------------
// API tra cứu tuyến — màn hình "Tra cứu tuyến" (User Story 1).
//
// Gọi GET /routes/search — endpoint CÔNG KHAI của story 1 (RouteSearchController, Trần Trung
// Hiếu): không gắn [Authorize] nên ai cũng gọi được, kể cả khách chưa đăng nhập và hành khách.
// Backend khớp theo TÊN trạm (kể cả trạm giữa tuyến) và lọc theo ngày đi ngay phía server.
//
// Trước đây (khi endpoint công khai chưa có) màn này ghép tạm từ 3 endpoint quản lý
// GET /routes, /routes/{id}/stops, /routes/{id}/fares — cả ba đều đòi vai trò Admin/Manager
// nên hành khách bấm "Tìm tuyến" nhận 403 "Bạn không có quyền truy cập tính năng này".
// Nhánh giả bên dưới giữ làm đường lùi: đổi cờ USE_MOCK_DATA thành `true` là quay lại được.
// -----------------------------------------------------------------------------

/** Tham số tìm tuyến từ form "điểm đi - điểm đến - ngày". */
export interface RouteLookupParams {
  /** Điểm đi — tên địa danh, khớp `Route.origin`. */
  origin: string;

  /** Điểm đến — khớp `Route.destination`. */
  destination: string;

  /**
   * Ngày đi (yyyy-MM-dd) — truyền thẳng cho GET /routes/search; backend chỉ giữ tuyến có ít
   * nhất một chuyến Scheduled khởi hành trong trọn ngày đó giờ Việt Nam.
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

// ---------------------------------------------------------------------------
// Cờ chuyển giữa dữ liệu giả và API thật. Đang để `false` — endpoint tìm tuyến công khai
// GET /routes/search đã có. Nhánh giả giữ làm đường lùi: đổi cờ này thành `true` là quay
// lại được, không phải chạm phần nào khác.
const USE_MOCK_DATA = false;

const delay = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms));

/** Sắp xếp kết quả theo mã tuyến — "01" đứng trước "B10" nhờ cờ numeric của localeCompare. */
function sortByCode(results: RouteLookupResult[]): RouteLookupResult[] {
  return [...results].sort((a, b) => a.routeCode.localeCompare(b.routeCode, 'vi', { numeric: true }));
}

// Hình dạng response của GET /routes/search (camelCase mặc định của ASP.NET Core) — khớp mục
// "Tra cứu tuyến — /routes/search" của docs/api-contract.md. Đổi tên trường ở đây là đổi hình
// dạng API: phải sửa api-contract.md trước rồi báo người viết backend (Hiếu).
interface RouteSearchResultDto {
  routeId: string;
  routeCode: string;
  routeName: string;
  origin: string;
  destination: string;
  distanceKm: number;
  stops: { stopId: string; stopName: string; stopOrder: number }[];
  minPrice: number | null;
}

// -------- Gọi API thật (dùng khi USE_MOCK_DATA = false) --------
const api: RouteLookupApi = {
  async search({ origin, destination, date }) {
    const found = await axiosClient.get<RouteSearchResultDto[], RouteSearchResultDto[]>(
      '/routes/search',
      // `date` để undefined thì axios bỏ tham số — backend hiểu là "không lọc theo ngày đi".
      { params: { origin, destination, date } },
    );

    // Backend trả trạm dạng { stopId, stopName, stopOrder } — màn hình dùng { name, order }.
    return sortByCode(
      found.map((route) => ({
        routeId: route.routeId,
        routeCode: route.routeCode,
        routeName: route.routeName,
        origin: route.origin,
        destination: route.destination,
        distanceKm: route.distanceKm,
        stops: route.stops.map((stop) => ({ name: stop.stopName, order: stop.stopOrder })),
        minPrice: route.minPrice,
      })),
    );
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
