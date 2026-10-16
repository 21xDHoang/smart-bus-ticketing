import dayjs from 'dayjs';

// -----------------------------------------------------------------------------
// API CẢNH BÁO CHUYẾN QUÁ TẢI — phục vụ màn hình "Cảnh báo chuyến quá tải" (US 20
// "Tỷ lệ lấp đầy", Sprint 4, task *"Hiển thị cảnh báo các chuyến thường xuyên quá
// tải"* của Nguyễn Đình Băng). Màn quản trị/vận hành: Admin và Manager xem những
// chuyến định kỳ (tuyến + giờ khởi hành) bị quá tải LẶP LẠI trong nhiều ngày, để
// điều chỉnh quy mô xe (câu hỏi của story 20).
//
// Khác với fillRateReportApi.ts (Dương Thị Hạnh) — màn đó xem tỷ lệ lấp đầy của MỘT
// ngày; màn này gộp NHIỀU ngày để bắt chuyến "thường xuyên" quá tải.
//
// Backend CHƯA có endpoint "tỷ lệ lấp đầy / cảnh báo quá tải" — task "API thống kê tỷ
// lệ lấp đầy theo chuyến/tuyến/khung giờ" (dòng 66) là của Kiên, docs/api-contract.md
// §4 mới ĐỀ XUẤT `GET /reports/fill-rate`. Nên file này chạy dữ liệu giả
// (USE_MOCK_DATA = true) — cùng lối fillRateReportApi.ts đã làm.
//
// Hình dạng endpoint DỰ KIẾN (CHƯA cam kết, để Kiên chốt):
//   GET /api/reports/fill-rate/overloaded?days=30&thresholdPct=100&frequentDays=5
// -----------------------------------------------------------------------------

/** Ngưỡng quá tải: lấp đầy ≥ 100% sức chứa ghế (có khách đứng). */
export const OVERLOAD_THRESHOLD_PCT = 100;

/** Ngưỡng "thường xuyên": quá tải ≥ 5 ngày trong kỳ theo dõi. */
export const FREQUENT_THRESHOLD_DAYS = 5;

/** Số ngày theo dõi mặc định (30 ngày gần nhất). */
export const PERIOD_DAYS = 30;

/** Một chuyến định kỳ bị quá tải lặp lại — khoá là tuyến + giờ khởi hành hằng ngày. */
export interface OverloadedTrip {
  /** Khoá duy nhất của chuyến định kỳ (routeCode + giờ) — dùng làm `rowKey`. */
  tripKey: string;
  routeCode: string;
  routeName: string;
  /** Giờ khởi hành hằng ngày, dạng "HH:mm". */
  departureTime: string;
  /** Sức chứa ghế của xe — mẫu số của tỷ lệ lấp đầy. */
  capacity: number;
  /** Tỷ lệ lấp đầy trung bình cả kỳ (%), có thể vượt 100 khi khách đứng. */
  averageFillRatePct: number;
  /** Số khách cao nhất từng ghi nhận trong kỳ. */
  maxPassengers: number;
  /** Số ngày quá tải (lấp đầy ≥ 100%) trong kỳ. */
  overloadedDays: number;
}

/** Toàn bộ dữ liệu một lượt tải báo cáo cảnh báo. */
export interface OverloadAlertReport {
  /** Ngày bắt đầu kỳ theo dõi (yyyy-MM-dd, lịch Việt Nam). */
  from: string;
  /** Ngày kết thúc kỳ theo dõi (yyyy-MM-dd), TÍNH CẢ. */
  to: string;
  /** Tổng số ngày trong kỳ. */
  periodDays: number;
  /** Ngưỡng quá tải đã áp dụng (%). */
  overloadThresholdPct: number;
  /** Ngưỡng "thường xuyên" đã áp dụng (số ngày). */
  frequentThresholdDays: number;
  /** Các chuyến cảnh báo, sắp theo số ngày quá tải GIẢM DẦN (nặng nhất trên cùng). */
  items: OverloadedTrip[];
  /** Tổng số chuyến cảnh báo. */
  total: number;
}

export interface OverloadAlertApi {
  /** Tải báo cáo cảnh báo các chuyến thường xuyên quá tải. */
  getReport: () => Promise<OverloadAlertReport>;
}

// ---------------------------------------------------------------------------
// Cờ chuyển giữa dữ liệu giả và API thật. Đang để `true` — backend fill-rate chưa có.
const USE_MOCK_DATA = true;

const delay = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms));

// -------- Gọi API thật (dùng khi USE_MOCK_DATA = false) --------
// Backend chưa có GET /api/reports/fill-rate/overloaded nên nhánh này tạm chưa viết:
// nối axiosClient vào endpoint dự kiến ở đầu file khi api-contract.md đã chốt.
const api: OverloadAlertApi = {
  async getReport() {
    throw new Error('Backend cảnh báo chuyến quá tải chưa có — chưa thể tải báo cáo thật.');
  },
};

// -------- Dữ liệu giả (dùng khi USE_MOCK_DATA = true) --------
/** Các tuyến + sức chứa xe — trùng tuyến ở fillRateReportApi.ts để số liệu nhất quán. */
const ROUTES = [
  { routeCode: '01', routeName: 'Bến xe Mỹ Đình — Bến xe Gia Lâm', capacity: 45 },
  { routeCode: '08', routeName: 'Cầu Giấy — Bờ Hồ Hoàn Kiếm', capacity: 30 },
  { routeCode: '02', routeName: 'Bến xe Mỹ Đình — Bến xe Nước Ngầm', capacity: 50 },
  { routeCode: 'B10', routeName: 'Bến xe Yên Nghĩa — Bến xe Mỹ Đình', capacity: 45 },
  { routeCode: '16', routeName: 'Bến xe Gia Lâm — Bến xe Yên Nghĩa', capacity: 50 },
  { routeCode: '05', routeName: 'Bến xe Giáp Bát — Bến xe Mỹ Đình', capacity: 30 },
];

/** Giờ khởi hành hằng ngày — tập trung giờ cao điểm sáng/chiều, nơi hay quá tải nhất. */
const SLOTS = [
  '05:30', '06:20', '07:10', '07:50', '08:30', '16:30', '17:15', '17:50', '18:30', '19:10',
];

/**
 * Số ngẫu nhiên TẤT ĐỊNH trong [0, 1) theo `seed` — cùng lối "không dùng Math.random" của
 * fillRateReportApi.ts: mỗi lần mở màn hình, danh sách cảnh báo vẫn ổn định.
 */
function seeded01(seed: number): number {
  const x = Math.sin(seed * 127.1 + 311.7) * 43758.5453;
  return x - Math.floor(x);
}

/**
 * Dựng danh sách cảnh báo. Mỗi cặp (tuyến, giờ) có "số ngày quá tải" tất định, LỆCH VỀ THẤP
 * (luỹ thừa 2.8) để chỉ một phần nhỏ chuyến vượt ngưỡng "thường xuyên" — đúng ý "cảnh báo",
 * không phải liệt kê cả mạng lưới.
 */
function buildItems(): OverloadedTrip[] {
  const items: OverloadedTrip[] = [];

  ROUTES.forEach((route, routeIndex) => {
    SLOTS.forEach((slot, slotIndex) => {
      const seed = routeIndex * SLOTS.length + slotIndex + 1;
      const overloadedDays = Math.round(Math.pow(seeded01(seed), 2.8) * 16);

      if (overloadedDays < FREQUENT_THRESHOLD_DAYS) return;

      // Lấp đầy trung bình tăng theo số ngày quá tải — chuyến quá tải nhiều thì bình quân cao,
      // và có thể vượt 100 (khách đứng).
      const averageFillRatePct =
        Math.round((0.84 + overloadedDays * 0.017 + (seeded01(seed * 7) - 0.5) * 0.06) * 1000) / 10;
      // Đỉnh khách cao hơn bình quân một chút — chuyến "nghiêm trọng" phải vượt sức chứa thật.
      const maxPassengers = Math.round(
        route.capacity * (averageFillRatePct / 100 + 0.06 + seeded01(seed * 11) * 0.08),
      );

      items.push({
        tripKey: `${route.routeCode}-${slot}`,
        routeCode: route.routeCode,
        routeName: route.routeName,
        departureTime: slot,
        capacity: route.capacity,
        averageFillRatePct,
        maxPassengers,
        overloadedDays,
      });
    });
  });

  return items.sort(
    (a, b) => b.overloadedDays - a.overloadedDays || b.averageFillRatePct - a.averageFillRatePct,
  );
}

function buildReport(): OverloadAlertReport {
  const to = dayjs();
  const from = to.subtract(PERIOD_DAYS - 1, 'day');
  const items = buildItems();

  return {
    from: from.format('YYYY-MM-DD'),
    to: to.format('YYYY-MM-DD'),
    periodDays: PERIOD_DAYS,
    overloadThresholdPct: OVERLOAD_THRESHOLD_PCT,
    frequentThresholdDays: FREQUENT_THRESHOLD_DAYS,
    items,
    total: items.length,
  };
}

const mock: OverloadAlertApi = {
  async getReport() {
    await delay(450);
    return buildReport();
  },
};

const overloadAlertApi: OverloadAlertApi = USE_MOCK_DATA ? mock : api;

export default overloadAlertApi;
