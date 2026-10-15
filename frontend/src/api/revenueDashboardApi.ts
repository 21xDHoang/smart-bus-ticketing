// -----------------------------------------------------------------------------
// API DASHBOARD DOANH THU — phục vụ màn hình "Dashboard doanh thu (thẻ số liệu + biểu đồ)"
// (US 19 "Doanh thu", Sprint 5, task của Dương Thị Hạnh). Màn quản trị/vận hành: Admin và
// Manager xem tổng quan doanh thu theo khoảng thời gian, gồm dải thẻ số liệu (tổng doanh thu,
// vé đã bán, chuyến hoạt động, tiền đã hoàn) và hai biểu đồ (doanh thu theo ngày + theo tuyến).
//
// Backend CHƯA có bảng thống kê/điểm cuối doanh thu nào trong docs/api-contract.md (mới có
// `/admin/feedbacks/statistics` và `/vouchers/statistics`), nên file này chạy dữ liệu giả
// (USE_MOCK_DATA = true) — cùng lối refundAdminApi.ts / ticketRequestApi.ts đã làm. Khi
// backend + contract có, chỉ cần viết thật nhánh `api` rồi lật cờ.
//
// Hình dạng endpoint DỰ KIẾN (để Hiếu/Hoàng chốt vào api-contract.md — CHƯA cam kết):
//   GET /admin/reports/revenue?range=7d|30d|90d
//      → { summary, byDay, byRoute }  — một lần gọi trả đủ cho cả dải thẻ lẫn hai biểu đồ,
//        để mọi số liệu trên màn hình cùng khớp MỘT lát dữ liệu.
// -----------------------------------------------------------------------------

/** Khoảng thời gian của dashboard — giá trị khớp tham số query `range` dự kiến. */
export type RevenueRange = '7d' | '30d' | '90d';

/** Một ngày trong chuỗi doanh thu theo ngày. */
export interface DailyRevenue {
  /** ISO 8601 (UTC) — hợp đồng ngày giờ dùng chung của app. */
  date: string;
  /** Doanh thu ngày (VND). */
  revenue: number;
  /** Số vé bán trong ngày. */
  tickets: number;
}

/** Doanh thu của một tuyến trong khoảng đã chọn — phục vụ biểu đồ so sánh. */
export interface RouteRevenue {
  routeCode: string;
  routeName: string;
  revenue: number;
}

/** Dải số liệu tóm tắt cho các thẻ trên đầu dashboard. */
export interface RevenueSummary {
  /** Tổng doanh thu trong khoảng (VND). */
  totalRevenue: number;
  /** Tổng vé đã bán. */
  ticketsSold: number;
  /** Tổng chuyến hoạt động — suy từ vé (trung bình ~24 khách/chuyến), chỉ để minh hoạ. */
  tripsCount: number;
  /** Tổng tiền đã hoàn trong khoảng (VND) — đối trọng để thấy doanh thu thực. */
  refundAmount: number;

  /** Chênh lệch % so với khoảng liền trước (dương = tăng). */
  revenueDeltaPct: number;
  ticketsDeltaPct: number;
  refundDeltaPct: number;
}

/** Toàn bộ dữ liệu một lượt tải dashboard. */
export interface RevenueDashboard {
  range: RevenueRange;
  summary: RevenueSummary;
  byDay: DailyRevenue[];
  byRoute: RouteRevenue[];
}

export interface RevenueDashboardApi {
  /** Tải dashboard cho một khoảng thời gian. */
  getDashboard: (range: RevenueRange) => Promise<RevenueDashboard>;
}

// ---------------------------------------------------------------------------
// Cờ chuyển giữa dữ liệu giả và API thật. Đang để `true` — backend doanh thu chưa có.
const USE_MOCK_DATA = true;

const delay = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms));

// -------- Gọi API thật (dùng khi USE_MOCK_DATA = false) --------
// Backend chưa có GET /admin/reports/revenue nên nhánh này tạm chưa viết: nối axiosClient vào
// endpoint dự kiến ở đầu file khi api-contract.md đã chốt.
const api: RevenueDashboardApi = {
  async getDashboard() {
    throw new Error('Backend thống kê doanh thu chưa có — chưa thể tải dashboard thật.');
  },
};

// -------- Dữ liệu giả (dùng khi USE_MOCK_DATA = true) --------
/** Giá vé trung bình giả (~12,5k) — chỉ để đổi doanh thu ↔ số vé cho khớp nhau. */
const AVG_TICKET_PRICE = 12_500;

/** Số ngày của mỗi khoảng. */
const DAYS_OF: Record<RevenueRange, number> = { '7d': 7, '30d': 30, '90d': 90 };

/**
 * Doanh thu + số vé của một ngày cách hôm nay `offset` ngày. Dùng công thức TẤT ĐỊNH (theo
 * thứ ngày + sóng sin theo offset) chứ không `Math.random` — để mỗi lần mở màn hình số vẫn
 * ổn định trong phiên, và chênh lệch giữa hai khoảng là có thật chứ không phải nhiễu.
 */
function dayPoint(offset: number): { revenue: number; tickets: number } {
  const date = new Date();
  date.setDate(date.getDate() - offset);
  const dow = date.getDay();
  const weekendBoost = dow === 0 || dow === 6 ? 1.35 : 1;
  const wave = 1 + 0.1 * Math.sin(offset / 2.2);
  const revenue = Math.round(8_600_000 * weekendBoost * wave);
  return { revenue, tickets: Math.round(revenue / AVG_TICKET_PRICE) };
}

/**
 * Chuỗi `days` ngày, ngày cũ nhất đứng đầu. `startOffset` dịch cửa sổ về quá khứ để lấy
 * "khoảng liền trước" dùng tính delta.
 */
function buildByDay(days: number, startOffset = 0): DailyRevenue[] {
  const result: DailyRevenue[] = [];
  for (let i = 0; i < days; i++) {
    const offset = startOffset + (days - 1 - i);
    const { revenue, tickets } = dayPoint(offset);
    const date = new Date();
    date.setDate(date.getDate() - offset);
    result.push({ date: date.toISOString(), revenue, tickets });
  }
  return result;
}

function sum(list: DailyRevenue[], key: 'revenue' | 'tickets'): number {
  return list.reduce((total, item) => total + item[key], 0);
}

/** Chênh lệch phần trăm giữa hai số, làm tròn 1 chữ số thập phân. */
function pctChange(current: number, previous: number): number {
  if (!previous) return 0;
  return Math.round(((current - previous) / previous) * 1000) / 10;
}

/** Tỉ trọng doanh thu của 6 tuyến top — cộng lại xấp xỉ 1 (phần còn lại thuộc các tuyến khác). */
const TOP_ROUTE_SHARES = [
  { routeCode: '01', routeName: 'Bến xe Mỹ Đình — Bến xe Gia Lâm', share: 0.22 },
  { routeCode: '08', routeName: 'Cầu Giấy — Bờ Hồ Hoàn Kiếm', share: 0.19 },
  { routeCode: '02', routeName: 'Bến xe Mỹ Đình — Bến xe Nước Ngầm', share: 0.17 },
  { routeCode: 'B10', routeName: 'Bến xe Yên Nghĩa — Bến xe Mỹ Đình', share: 0.14 },
  { routeCode: '16', routeName: 'Bến xe Gia Lâm — Bến xe Yên Nghĩa', share: 0.11 },
  { routeCode: '05', routeName: 'Bến xe Giáp Bát — Bến xe Mỹ Đình', share: 0.09 },
];

function buildDashboard(range: RevenueRange): RevenueDashboard {
  const days = DAYS_OF[range];
  const byDay = buildByDay(days);
  const prevByDay = buildByDay(days, days);

  const totalRevenue = sum(byDay, 'revenue');
  const ticketsSold = sum(byDay, 'tickets');
  const prevRevenue = sum(prevByDay, 'revenue');
  const prevTickets = sum(prevByDay, 'tickets');

  const refundAmount = Math.round(totalRevenue * 0.032);
  const prevRefund = Math.round(prevRevenue * 0.032);

  const summary: RevenueSummary = {
    totalRevenue,
    ticketsSold,
    tripsCount: Math.round(ticketsSold / 24),
    refundAmount,
    revenueDeltaPct: pctChange(totalRevenue, prevRevenue),
    ticketsDeltaPct: pctChange(ticketsSold, prevTickets),
    refundDeltaPct: pctChange(refundAmount, prevRefund),
  };

  // Doanh thu theo tuyến = tỉ trọng cố định trên tổng doanh thu của khoảng, sắp giảm dần để
  // biểu đồ ngang hiển thị tuyến lớn nhất ở trên cùng.
  const byRoute: RouteRevenue[] = TOP_ROUTE_SHARES.map((route) => ({
    routeCode: route.routeCode,
    routeName: route.routeName,
    revenue: Math.round(totalRevenue * route.share),
  })).sort((a, b) => b.revenue - a.revenue);

  return { range, summary, byDay, byRoute };
}

const mock: RevenueDashboardApi = {
  async getDashboard(range) {
    await delay(450);
    return buildDashboard(range);
  },
};

const revenueDashboardApi: RevenueDashboardApi = USE_MOCK_DATA ? mock : api;

export default revenueDashboardApi;
