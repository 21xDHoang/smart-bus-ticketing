// -----------------------------------------------------------------------------
// API THỐNG KÊ TỶ LỆ LẤP ĐẦY THEO CHUYẾN — phục vụ màn hình "Thống kê tỷ lệ lấp đầy
// theo chuyến" (US 20 "Tỷ lệ lấp đầy", Sprint 4/5, task của Dương Thị Hạnh). Màn
// quản trị/vận hành: Admin và Manager xem từng chuyến trong một ngày đầy/trống tới đâu.
//
// Backend CHƯA có endpoint này — task "API thống kê tỷ lệ lấp đầy theo chuyến/tuyến/khung
// giờ" (dòng 66) là của Kiên, docs/38 §4 chỉ mới ĐỀ XUẤT endpoint `GET /reports/fill-rate`.
// Nên file này chạy dữ liệu giả (USE_MOCK_DATA = true) — cùng lối revenueDashboardApi.ts
// đã làm. Khi backend + contract có, chỉ cần viết thật nhánh `api` rồi lật cờ.
//
// Tỷ lệ lấp đầy = vé đã bán/sử dụng (Paid/Used) chia cho SỨC CHỨA ghế của xe — đơn vị
// phần trăm, khác hẳn mật độ "khách/chuyến" của /reports/peak-hours (docs/38 §4).
// -----------------------------------------------------------------------------

/** Một chuyến trong bảng thống kê — tỷ lệ lấp đầy tính từ sức chứa ghế của xe. */
export interface TripFillRate {
  /** Mã chuyến — để làm khoá dòng và đối chiếu nhanh. */
  tripId: string;

  /** Mã hiển thị của chuyến, ví dụ "C01-0530". */
  tripCode: string;

  routeCode: string;

  routeName: string;

  /** Giờ khởi hành (ISO 8601 kèm offset +07:00 — cùng lối `dayBounds` của TripListByDayPage). */
  departureTime: string;

  /** Biển số xe. */
  busPlate: string;

  /** Sức chứa ghế của xe — mẫu số của tỷ lệ lấp đầy. */
  capacity: number;

  /** Số vé đã bán/sử dụng (khách thực tế lên xe). */
  passengers: number;

  /** Tỷ lệ lấp đầy, phần trăm 0–100, làm tròn 1 chữ số thập phân. */
  fillRatePct: number;
}

/** Dải số liệu tóm tắt cho các thẻ trên đầu màn hình. */
export interface FillRateSummary {
  /** Tổng chuyến khởi hành trong ngày đã chọn. */
  totalTrips: number;

  /** Tỷ lệ lấp đầy trung bình cả ngày (%). */
  averageFillRatePct: number;

  /** Số chuyến "đầy" — lấp đầy ≥ 80%. */
  fullTrips: number;

  /** Số chuyến "trống" — lấp đầy < 50%. */
  emptyTrips: number;
}

/** Toàn bộ dữ liệu một lượt tải báo cáo. */
export interface FillRateReport {
  /** Ngày báo cáo (yyyy-MM-dd, lịch Việt Nam). */
  date: string;

  summary: FillRateSummary;

  /** Các chuyến trong ngày, sắp theo giờ khởi hành tăng dần. */
  trips: TripFillRate[];
}

export interface FillRateReportApi {
  /** Tải báo cáo tỷ lệ lấp đầy của một ngày. */
  getReport: (date: string) => Promise<FillRateReport>;
}

// ---------------------------------------------------------------------------
// Cờ chuyển giữa dữ liệu giả và API thật. Đang để `true` — backend fill-rate chưa có.
const USE_MOCK_DATA = true;

const delay = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms));

// -------- Gọi API thật (dùng khi USE_MOCK_DATA = false) --------
// Backend chưa có GET /reports/fill-rate nên nhánh này tạm chưa viết: nối axiosClient vào
// endpoint dự kiến ở đầu file khi api-contract.md đã chốt.
const api: FillRateReportApi = {
  async getReport() {
    throw new Error('Backend thống kê tỷ lệ lấp đầy chưa có — chưa thể tải báo cáo thật.');
  },
};

// -------- Dữ liệu giả (dùng khi USE_MOCK_DATA = true) --------
/** Các tuyến + sức chứa xe — trùng tuyến ở dashboard doanh thu để số liệu nhất quán. */
const ROUTES = [
  { routeCode: '01', routeName: 'Bến xe Mỹ Đình — Bến xe Gia Lâm', capacity: 45 },
  { routeCode: '08', routeName: 'Cầu Giấy — Bờ Hồ Hoàn Kiếm', capacity: 30 },
  { routeCode: '02', routeName: 'Bến xe Mỹ Đình — Bến xe Nước Ngầm', capacity: 50 },
  { routeCode: 'B10', routeName: 'Bến xe Yên Nghĩa — Bến xe Mỹ Đình', capacity: 45 },
  { routeCode: '16', routeName: 'Bến xe Gia Lâm — Bến xe Yên Nghĩa', capacity: 50 },
  { routeCode: '05', routeName: 'Bến xe Giáp Bát — Bến xe Mỹ Đình', capacity: 30 },
];

/** Lịch chạy cố định trong ngày — 20 chuyến cách nhau ~50 phút, từ 05:00. */
const SLOTS = [
  { h: 5, m: 0 }, { h: 5, m: 50 }, { h: 6, m: 40 }, { h: 7, m: 30 },
  { h: 8, m: 20 }, { h: 9, m: 10 }, { h: 10, m: 0 }, { h: 10, m: 50 },
  { h: 11, m: 40 }, { h: 12, m: 30 }, { h: 13, m: 20 }, { h: 14, m: 10 },
  { h: 15, m: 0 }, { h: 15, m: 50 }, { h: 16, m: 40 }, { h: 17, m: 30 },
  { h: 18, m: 20 }, { h: 19, m: 10 }, { h: 20, m: 0 }, { h: 21, m: 0 },
];

const clamp = (value: number, min: number, max: number) => Math.min(max, Math.max(min, value));

const pad = (n: number) => String(n).padStart(2, '0');

/** Băm ngày thành một số 0..999 — để cùng một ngày luôn ra cùng một báo cáo. */
function dateSeed(date: string): number {
  let hash = 0;
  for (let i = 0; i < date.length; i++) hash = (hash * 31 + date.charCodeAt(i)) % 1000;
  return hash;
}

/**
 * Dựng danh sách chuyến của một ngày. Tỷ lệ lấp đầy dùng công thức TẤT ĐỊNH (sóng sin theo
 * ngày + thứ tự chuyến + đỉnh giờ cao điểm + cuối tuần) chứ không `Math.random` — để mỗi lần
 * mở cùng một ngày số vẫn ổn định, và giờ cao điểm đầy hơn giờ vắng là có thật chứ không nhiễu.
 */
function buildTrips(date: string): TripFillRate[] {
  const seed = dateSeed(date);
  const dow = new Date(`${date}T00:00:00+07:00`).getDay();
  const weekendBoost = dow === 0 || dow === 6 ? 0.06 : 0;

  return SLOTS.map((slot, index) => {
    const route = ROUTES[index % ROUTES.length];
    const base = 0.52 + 0.28 * Math.sin(seed / 1000 + index * 1.7) + 0.1 * Math.sin(index * 0.53);
    const isPeak = (slot.h >= 6 && slot.h <= 8) || (slot.h >= 16 && slot.h <= 19);
    const fill = clamp(base + (isPeak ? 0.16 : 0) + weekendBoost, 0.04, 0.97);

    const passengers = Math.round(fill * route.capacity);
    const fillRatePct = Math.round((passengers / route.capacity) * 1000) / 10;
    const time = `${pad(slot.h)}${pad(slot.m)}`;

    return {
      tripId: `${date}-${time}`,
      tripCode: `C${route.routeCode}-${time}`,
      routeCode: route.routeCode,
      routeName: route.routeName,
      departureTime: `${date}T${pad(slot.h)}:${pad(slot.m)}:00+07:00`,
      busPlate: `29B-${10000 + ((seed + index * 137) % 90000)}`,
      capacity: route.capacity,
      passengers,
      fillRatePct,
    };
  });
}

function buildReport(date: string): FillRateReport {
  const trips = buildTrips(date);
  const totalTrips = trips.length;
  const sumPct = trips.reduce((total, trip) => total + trip.fillRatePct, 0);

  return {
    date,
    summary: {
      totalTrips,
      averageFillRatePct: totalTrips ? Math.round((sumPct / totalTrips) * 10) / 10 : 0,
      fullTrips: trips.filter((trip) => trip.fillRatePct >= 80).length,
      emptyTrips: trips.filter((trip) => trip.fillRatePct < 50).length,
    },
    trips,
  };
}

const mock: FillRateReportApi = {
  async getReport(date) {
    await delay(450);
    return buildReport(date);
  },
};

const fillRateReportApi: FillRateReportApi = USE_MOCK_DATA ? mock : api;

export default fillRateReportApi;
