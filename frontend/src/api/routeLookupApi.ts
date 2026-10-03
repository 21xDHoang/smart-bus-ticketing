import { fetchRoutes } from './routeApi';
import type { Route } from './routeApi';
import { fetchRouteStops } from './routeStopApi';
import fareApi from './fareApi';
import type { Fare } from './fareApi';

// -----------------------------------------------------------------------------
// API tra cứu tuyến — màn hình "Tra cứu tuyến" (User Story 1).
//
// Endpoint "tìm tuyến theo điểm đi - điểm đến" vẫn CHƯA có (task của Trần Trung Hiếu,
// Sprint 2), nên nhánh API THẬT bên dưới ghép từ 3 endpoint đã có sẵn ở Sprint 1:
//   GET /routes                → lọc tuyến theo điểm đi/điểm đến (Route.origin/destination)
//   GET /routes/{id}/stops     → danh sách trạm của tuyến
//   GET /routes/{id}/fares     → giá vé thấp nhất
//
// ⚠️ Giới hạn hiện tại: 3 endpoint trên đều đòi vai trò Admin/Manager (docs/api-contract.md),
// nên màn "Tra cứu tuyến" phải đăng nhập bằng tài khoản Manager/Admin mới xem được kết quả.
// Phần TÌM CHUYẾN của hành khách thì đã có đường công khai: từ kết quả tra cứu bấm "Xem
// chuyến" → màn "Kết quả tìm kiếm" gọi GET /trips/search (công khai, không cần đăng nhập).
// Khi Hiếu có API tìm tuyến công khai thì thay ruột hàm `search` dưới đây là xong.
// -----------------------------------------------------------------------------

/** Tham số tìm tuyến từ form "điểm đi - điểm đến - ngày". */
export interface RouteLookupParams {
  /** Điểm đi — tên địa danh, khớp `Route.origin`. */
  origin: string;

  /** Điểm đến — khớp `Route.destination`. */
  destination: string;

  /**
   * Ngày đi (yyyy-MM-dd). Backend chưa có bảng Trips nên tạm thời chưa dùng để lọc —
   * giữ trường này sẵn để khi API tìm chuyến xong chỉ cần truyền thẳng vào.
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
// Cờ chuyển giữa dữ liệu giả và API thật. Đã bật API thật — màn "Tra cứu tuyến" cần đăng
// nhập bằng tài khoản Manager/Admin (xem ghi chú đầu file). Nhánh giả giữ làm đường lùi:
// đổi cờ này thành `true` là quay lại được, không phải chạm phần nào khác.
const USE_MOCK_DATA = false;

const delay = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms));

/** Trần `pageSize` của GET /routes là 100 — số tuyến tối đa lấy trong một lượt gọi. */
const ROUTE_PAGE_SIZE = 100;

/** So khớp chuỗi không phân biệt hoa thường, đã bỏ khoảng trắng thừa hai đầu. */
function matches(value: string, keyword: string): boolean {
  return value.toLowerCase().includes(keyword.toLowerCase());
}

/** Giá vé thấp nhất trong bảng giá — màn hình hiển thị là "giá từ …". */
function minPriceOf(fares: Fare[]): number | null {
  if (fares.length === 0) return null;
  return Math.min(...fares.map((fare) => fare.price));
}

/**
 * Lấy toàn bộ tuyến đang khai thác bằng cách lặp theo trang, giống `fareApi.listRoutes`.
 * Không thể lọc origin/destination ngay tại GET /routes vì endpoint chỉ nhận MỘT tham số
 * `search` chung, không tách riêng hai chiều — phải lấy về rồi lọc phía client.
 */
async function fetchActiveRoutes(): Promise<Route[]> {
  const routes: Route[] = [];

  for (let page = 1; ; page += 1) {
    const { items, total } = await fetchRoutes({
      page,
      pageSize: ROUTE_PAGE_SIZE,
      status: 'Active',
    });

    routes.push(...items);

    // `items.length === 0` là chốt chặn để không lặp vô hạn nếu `total` sai.
    if (routes.length >= total || items.length === 0) break;
  }

  return routes;
}

/** Sắp xếp kết quả theo mã tuyến — "01" đứng trước "B10" nhờ cờ numeric của localeCompare. */
function sortByCode(results: RouteLookupResult[]): RouteLookupResult[] {
  return [...results].sort((a, b) => a.routeCode.localeCompare(b.routeCode, 'vi', { numeric: true }));
}

// -------- Gọi API thật (dùng khi USE_MOCK_DATA = false) --------
const api: RouteLookupApi = {
  async search({ origin, destination }) {
    const keywordOrigin = origin.trim();
    const keywordDestination = destination.trim();

    const routes = await fetchActiveRoutes();
    const matched = routes.filter(
      (route) => matches(route.origin, keywordOrigin) && matches(route.destination, keywordDestination),
    );

    const results = await Promise.all(
      matched.map(async (route): Promise<RouteLookupResult> => {
        const [stops, fares] = await Promise.all([
          fetchRouteStops(route.id),
          fareApi.list(route.id),
        ]);

        return {
          routeId: route.id,
          routeCode: route.code,
          routeName: route.name,
          origin: route.origin,
          destination: route.destination,
          distanceKm: route.distanceKm,
          stops: stops.map((stop) => ({ name: stop.stopName, order: stop.stopOrder })),
          minPrice: minPriceOf(fares),
        };
      }),
    );

    return sortByCode(results);
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
