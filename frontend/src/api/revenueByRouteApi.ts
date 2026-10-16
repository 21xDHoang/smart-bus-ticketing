// -----------------------------------------------------------------------------
// API BẢNG DOANH THU THEO TUYẾN — phục vụ màn hình "Doanh thu theo tuyến" (US 19
// "Doanh thu", Sprint 4, task *"Bảng chi tiết doanh thu theo tuyến + sắp xếp, phân
// trang"* của Nguyễn Đình Băng). Màn quản trị/vận hành: Admin và Manager xem doanh
// thu bán vé gom theo từng tuyến, sắp xếp và phân trang phía server.
//
// Backend CHƯA có endpoint "theo tuyến" trong docs/api-contract.md — task *"API
// doanh thu theo tuyến / theo xe / theo phương thức"* của Nguyễn Duy Kiên chưa làm,
// nên file này chạy dữ liệu giả (USE_MOCK_DATA = true), cùng lối revenueDashboardApi.ts
// / refundAdminApi.ts đã làm. Khi backend + contract có, chỉ cần viết thật nhánh `api`
// rồi lật cờ.
//
// Hình dạng endpoint DỰ KIẾN (theo ghi chú ở mục "Báo cáo doanh thu — /reports/revenue"
// của docs/api-contract.md — CHƯA cam kết, để Kiên chốt):
//   GET /api/reports/revenue/by-route?from=&to=&sortBy=revenue&sortOrder=descend&page=1&pageSize=10
//      → { items, total, totalRevenue, totalPayments }
// -----------------------------------------------------------------------------

/** Trường có thể sắp xếp của bảng — giá trị khớp tham số query `sortBy` dự kiến. */
export type RouteRevenueSortField = 'routeName' | 'payments' | 'revenue';

/** Chiều sắp xếp — dùng lại giá trị `order` của AntD Table (`ascend`/`descend`). */
export type RouteRevenueSortOrder = 'ascend' | 'descend';

/** Một dòng doanh thu của một tuyến trong khoảng đã chọn. */
export interface RouteRevenueRow {
  /** Khóa chính của tuyến (Guid) — chỉ để `rowKey`, không hiển thị. */
  routeId: string;
  /** Mã tuyến (ví dụ "01", "B10"). */
  routeCode: string;
  /** Tên tuyến (điểm đầu — điểm cuối). */
  routeName: string;
  /** Tổng doanh thu bán vé của tuyến trong khoảng (VND), chỉ giao dịch Success. */
  revenue: number;
  /** Số giao dịch Success của tuyến trong khoảng. */
  payments: number;
  /** Tỉ trọng doanh thu của tuyến so với tổng toàn kỳ (%). */
  sharePct: number;
}

/** Kết quả một trang của bảng — phân trang phía server nên trả cả tổng để tính số trang. */
export interface RouteRevenuePage {
  /** Các dòng thuộc trang hiện tại (đã sắp xếp). */
  items: RouteRevenueRow[];
  /** Tổng số tuyến (trước khi cắt trang). */
  total: number;
  /** Tổng doanh thu toàn kỳ (VND) — dùng cho thẻ số liệu. */
  totalRevenue: number;
  /** Tổng giao dịch Success toàn kỳ. */
  totalPayments: number;
}

/** Tham số truy vấn của bảng. */
export interface RouteRevenueQuery {
  /** Trang hiện tại, đếm từ 1. */
  page: number;
  /** Cỡ trang. */
  pageSize: number;
  /** Trường sắp xếp — bỏ trống = không sắp xếp. */
  sortBy?: RouteRevenueSortField;
  /** Chiều sắp xếp — mặc định `descend` khi có `sortBy`. */
  sortOrder?: RouteRevenueSortOrder;
}

export interface RevenueByRouteApi {
  /** Tải một trang doanh thu theo tuyến đã sắp xếp. */
  getByRoute: (query: RouteRevenueQuery) => Promise<RouteRevenuePage>;
}

// ---------------------------------------------------------------------------
// Cờ chuyển giữa dữ liệu giả và API thật. Đang để `true` — backend "theo tuyến" chưa có.
const USE_MOCK_DATA = true;

const delay = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms));

// -------- Gọi API thật (dùng khi USE_MOCK_DATA = false) --------
// Backend chưa có GET /api/reports/revenue/by-route nên nhánh này tạm chưa viết: nối
// axiosClient vào endpoint dự kiến ở đầu file khi api-contract.md đã chốt.
const api: RevenueByRouteApi = {
  async getByRoute() {
    throw new Error('Backend báo cáo doanh thu theo tuyến chưa có — chưa thể tải bảng thật.');
  },
};

// -------- Dữ liệu giả (dùng khi USE_MOCK_DATA = true) --------
/** Giá vé trung bình giả (~12,5k) — cùng hằng số của revenueDashboardApi để hai màn khớp nhau. */
const AVG_TICKET_PRICE = 12_500;

/** Danh sách tuyến giả — đủ dài để phân trang (mặc định 10 dòng/trang) có ý nghĩa. */
const ROUTES: { routeCode: string; routeName: string }[] = [
  { routeCode: '01', routeName: 'Bến xe Mỹ Đình — Bến xe Gia Lâm' },
  { routeCode: '02', routeName: 'Bến xe Mỹ Đình — Bến xe Nước Ngầm' },
  { routeCode: '03', routeName: 'Bến xe Giáp Bát — Bến xe Gia Lâm' },
  { routeCode: '05', routeName: 'Bến xe Giáp Bát — Bến xe Mỹ Đình' },
  { routeCode: '07', routeName: 'Cầu Giấy — Sân bay Nội Bài' },
  { routeCode: '08', routeName: 'Cầu Giấy — Bờ Hồ Hoàn Kiếm' },
  { routeCode: '09', routeName: 'Bến xe Mỹ Đình — Hồ Tây' },
  { routeCode: '11', routeName: 'Bến xe Yên Nghĩa — Bến xe Gia Lâm' },
  { routeCode: '12', routeName: 'Bến xe Mỹ Đình — Bến xe Sơn Tây' },
  { routeCode: '16', routeName: 'Bến xe Gia Lâm — Bến xe Yên Nghĩa' },
  { routeCode: '17', routeName: 'Long Biên — Sân bay Nội Bài' },
  { routeCode: '20', routeName: 'Bến xe Giáp Bát — Bến xe Sơn Tây' },
  { routeCode: '21', routeName: 'Bến xe Giáp Bát — Bến xe Yên Nghĩa' },
  { routeCode: '22', routeName: 'Bến xe Gia Lâm — Bến xe Mỹ Đình' },
  { routeCode: '25', routeName: 'Bến xe Mỹ Đình — Sóc Sơn' },
  { routeCode: '27', routeName: 'Bến xe Yên Nghĩa — Bến xe Mỹ Đình' },
  { routeCode: '29', routeName: 'Bến xe Giáp Bát — Hồ Tây' },
  { routeCode: '30', routeName: 'Bến xe Mỹ Đình — Khu đô thị Linh Đàm' },
  { routeCode: '32', routeName: 'Bến xe Giáp Bát — Bến xe Nước Ngầm' },
  { routeCode: '34', routeName: 'Bến xe Mỹ Đình — Bến xe Hà Đông' },
  { routeCode: 'B10', routeName: 'Bến xe Yên Nghĩa — Bến xe Mỹ Đình' },
  { routeCode: 'B20', routeName: 'Bến xe Yên Nghĩa — Sân bay Nội Bài' },
  { routeCode: 'E01', routeName: 'Bến xe Mỹ Đình — Khu công nghệ cao Hòa Lạc' },
  { routeCode: 'E05', routeName: 'Long Biên — Khu đô thị Ecopark' },
];

/**
 * Số ngẫu nhiên TẤT ĐỊNH trong [0, 1) theo `seed` — cùng lối "không dùng Math.random" của
 * revenueDashboardApi: mỗi lần mở màn hình, thứ hạng doanh thu các tuyến vẫn ổn định.
 */
function seeded01(seed: number): number {
  const x = Math.sin(seed * 127.1 + 311.7) * 43758.5453;
  return x - Math.floor(x);
}

/** Toàn bộ dòng doanh thu theo tuyến (chưa sắp xếp, chưa cắt trang) — nền của mọi trang. */
function buildAllRows(): RouteRevenueRow[] {
  const base = ROUTES.map((route, index) => {
    const frac = seeded01(index + 1);
    const revenue = Math.round(15_000_000 + frac * 85_000_000);
    return {
      routeId: `route-${route.routeCode}`,
      routeCode: route.routeCode,
      routeName: route.routeName,
      revenue,
      payments: Math.round(revenue / AVG_TICKET_PRICE),
    };
  });

  const totalRevenue = base.reduce((sum, row) => sum + row.revenue, 0);
  return base.map((row) => ({
    ...row,
    sharePct: Math.round((row.revenue / totalRevenue) * 1000) / 10,
  }));
}

/** So sánh hai dòng theo trường sắp xếp — dùng `localeCompare` cho tên tuyến (có dấu tiếng Việt). */
function compareRows(a: RouteRevenueRow, b: RouteRevenueRow, sortBy: RouteRevenueSortField): number {
  switch (sortBy) {
    case 'revenue':
      return a.revenue - b.revenue;
    case 'payments':
      return a.payments - b.payments;
    case 'routeName':
      return a.routeName.localeCompare(b.routeName, 'vi');
  }
}

const mock: RevenueByRouteApi = {
  async getByRoute(query) {
    await delay(400);

    const rows = buildAllRows();
    const totalRevenue = rows.reduce((sum, row) => sum + row.revenue, 0);
    const totalPayments = rows.reduce((sum, row) => sum + row.payments, 0);

    // Sắp xếp phía "server" rồi mới cắt trang — để khi lật USE_MOCK_DATA = false, màn hình
    // không phải đổi gì (phân trang + sắp xếp do backend lo, giống hợp đồng dự kiến).
    const sorted = query.sortBy
      ? [...rows].sort((a, b) => {
          const dir = query.sortOrder === 'ascend' ? 1 : -1;
          return compareRows(a, b, query.sortBy!) * dir;
        })
      : rows;

    const start = (query.page - 1) * query.pageSize;
    const items = sorted.slice(start, start + query.pageSize);

    return { items, total: rows.length, totalRevenue, totalPayments };
  },
};

const revenueByRouteApi: RevenueByRouteApi = USE_MOCK_DATA ? mock : api;

export default revenueByRouteApi;
