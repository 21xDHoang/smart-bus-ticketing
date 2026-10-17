// -----------------------------------------------------------------------------
// API THỐNG KÊ LƯỢT SOÁT THEO CHUYẾN — phục vụ màn hình "Thống kê lượt soát theo
// chuyến" (US 15 "Soát vé QR", Sprint 4, dòng 24, task của Dương Thị Hạnh). Màn
// quản trị/vận hành: Admin và Manager chọn một chuyến rồi xem lượt soát vé của chuyến
// đó phân bố thế nào (tổng / hợp lệ / từng lý do từ chối).
//
// Backend CHƯA có endpoint này — bảng TicketValidations đã có (PR #188) nhưng endpoint
// "thống kê lượt soát theo chuyến" chưa ai chốt trong api-contract.md. Nên file này chạy
// dữ liệu giả (USE_MOCK_DATA = true) — cùng lối fillRateReportApi.ts. Khi backend +
// contract có, chỉ cần viết thật nhánh `api` rồi lật cờ.
//
// Nguồn dữ liệu thật: bảng TicketValidations gộp theo TripId, đếm theo Result. Xem docs/34
// §6: dòng 24 đếm LƯỢT SOÁT (mọi dòng nhật ký, kể cả lượt bị từ chối), không đếm vé — lọc
// Result = 'Valid' mới ra số khách thật lên xe.
// -----------------------------------------------------------------------------

import type { TicketValidationReason } from './ticketValidationApi';

/** Một chuyến trong ô chọn chuyến. */
export interface TripOption {
  tripId: string;

  /** Mã chuyến hiển thị, ví dụ "C01-0530". */
  tripCode: string;

  routeCode: string;

  routeName: string;

  /** Giờ khởi hành (ISO 8601 kèm offset +07:00 — cùng lối dayBounds của TripListByDayPage). */
  departureTime: string;

  /** Biển số xe. */
  busPlate: string;
}

/** Mọi kết quả một lượt soát có thể nhận — `Valid` cộng năm lý do từ chối đã chốt (docs/34 §3). */
export type TicketValidationResult = 'Valid' | TicketValidationReason;

/** Số lượt soát của một kết quả — một dòng trong bảng phân bổ. */
export interface ResultCount {
  result: TicketValidationResult;

  /** Số lượt soát rơi vào kết quả này. */
  count: number;

  /** Phần trăm trên tổng số lượt soát — 0..100, làm tròn 1 chữ số thập phân. */
  pct: number;
}

/** Toàn bộ số liệu một chuyến đã chọn. */
export interface TripValidationStats {
  trip: TripOption;

  /** Tổng lượt soát — mọi dòng nhật ký, kể cả lượt bị từ chối. */
  totalScans: number;

  /** Số lượt hợp lệ = số khách thật lên xe (Result = 'Valid'). */
  validScans: number;

  /** Số lượt bị từ chối = tổng trừ hợp lệ. */
  rejectedScans: number;

  /** Tỷ lệ hợp lệ trên tổng lượt soát (%). */
  validRatePct: number;

  /** Phân bổ theo từng kết quả — `Valid` đứng đầu, rồi tới năm lý do từ chối. */
  byResult: ResultCount[];
}

export interface TicketValidationStatsApi {
  /** Danh sách chuyến hôm nay để chọn. */
  getTrips: () => Promise<TripOption[]>;

  /** Thống kê lượt soát của một chuyến. */
  getStats: (tripId: string) => Promise<TripValidationStats>;
}

// ---------------------------------------------------------------------------
// Cờ chuyển giữa dữ liệu giả và API thật. Đang để `true` — backend chưa có endpoint.
const USE_MOCK_DATA = true;

const delay = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms));

// -------- Gọi API thật (dùng khi USE_MOCK_DATA = false) --------
// Backend chưa có endpoint thống kê lượt soát nên nhánh này tạm chưa viết: nối axiosClient
// vào endpoint dự kiến khi api-contract.md đã chốt.
const api: TicketValidationStatsApi = {
  async getTrips() {
    throw new Error('Backend thống kê lượt soát chưa có — chưa thể tải dữ liệu thật.');
  },
  async getStats() {
    throw new Error('Backend thống kê lượt soát chưa có — chưa thể tải dữ liệu thật.');
  },
};

// -------- Dữ liệu giả (dùng khi USE_MOCK_DATA = true) --------
const VN_OFFSET_MS = 7 * 60 * 60 * 1000;

const pad = (n: number) => String(n).padStart(2, '0');

/** Định dạng epoch (ms) thành ISO 8601 kèm offset +07:00 — giờ Việt Nam dùng chung trong app. */
function vietnamIso(epochMs: number): string {
  const date = new Date(epochMs + VN_OFFSET_MS);
  return (
    `${date.getUTCFullYear()}-${pad(date.getUTCMonth() + 1)}-${pad(date.getUTCDate())}T` +
    `${pad(date.getUTCHours())}:${pad(date.getUTCMinutes())}:00+07:00`
  );
}

/** Epoch (ms) của 00:00 theo giờ Việt Nam của ngày chứa `nowMs`. */
function startOfVietnamDay(nowMs: number): number {
  const date = new Date(nowMs + VN_OFFSET_MS);
  return Date.UTC(date.getUTCFullYear(), date.getUTCMonth(), date.getUTCDate()) - VN_OFFSET_MS;
}

/** Băm một chuỗi thành số không âm — cùng một chuyến luôn ra cùng một bảng thống kê. */
function hashString(value: string): number {
  let hash = 0;
  for (let i = 0; i < value.length; i++) hash = (hash * 31 + value.charCodeAt(i)) >>> 0;
  return hash;
}

/** Các chuyến cố định chạy trong ngày — trùng tuyến ở màn tỷ lệ lấp đầy để nhất quán. */
const TRIP_DEFS = [
  { routeCode: '01', routeName: 'Bến xe Mỹ Đình — Bến xe Gia Lâm', busPlate: '29B-12345', h: 6, m: 0 },
  { routeCode: '08', routeName: 'Cầu Giấy — Bờ Hồ Hoàn Kiếm', busPlate: '29B-23456', h: 8, m: 30 },
  { routeCode: '02', routeName: 'Bến xe Mỹ Đình — Bến xe Nước Ngầm', busPlate: '29B-34567', h: 12, m: 0 },
  { routeCode: '16', routeName: 'Bến xe Gia Lâm — Bến xe Yên Nghĩa', busPlate: '29B-45678', h: 17, m: 0 },
];

/** Năm lý do từ chối, đúng thứ tự của TICKET_VALIDATION_REASON_META (docs/34 §3). */
const REJECTION_REASONS: TicketValidationReason[] = [
  'NotFound',
  'Cancelled',
  'AlreadyUsed',
  'WrongTrip',
  'Expired',
];

function buildTrips(): TripOption[] {
  const dayStart = startOfVietnamDay(Date.now());
  const date = vietnamIso(dayStart).slice(0, 10);

  return TRIP_DEFS.map((def) => {
    const time = `${pad(def.h)}${pad(def.m)}`;
    return {
      tripId: `${date}-${time}-${def.routeCode}`,
      tripCode: `C${def.routeCode}-${time}`,
      routeCode: def.routeCode,
      routeName: def.routeName,
      departureTime: vietnamIso(dayStart + (def.h * 60 + def.m) * 60000),
      busPlate: def.busPlate,
    };
  });
}

const MOCK_TRIPS = buildTrips();

/** Làm tròn 1 chữ số thập phân — cùng lối fillRateReportApi. */
function round1(value: number): number {
  return Math.round(value * 10) / 10;
}

/**
 * Dựng bảng thống kê của một chuyến. Số liệu TẤT ĐỊNH (băm từ tripId) chứ không `Math.random`
 * — để mỗi lần mở cùng một chuyến vẫn ra cùng một bảng.
 */
function buildStats(trip: TripOption): TripValidationStats {
  const seed = hashString(trip.tripId);
  const validScans = 6 + (seed % 9); // 6..14 khách lên xe.
  const rejectedScans = seed % 6; // 0..5 lượt bị từ chối.
  const totalScans = validScans + rejectedScans;

  // Phân bổ `rejectedScans` vào 5 lý do bằng phép chia trải đều — tổng luôn khớp `rejectedScans`,
  // rồi xoay vòng theo seed để không chuyến nào giống chuyến nào.
  const rotate = seed % REJECTION_REASONS.length;
  const rejectionCounts = REJECTION_REASONS.map((_, index) => {
    const i = (index + rotate) % REJECTION_REASONS.length;
    return (
      Math.floor((rejectedScans * (i + 1)) / REJECTION_REASONS.length) -
      Math.floor((rejectedScans * i) / REJECTION_REASONS.length)
    );
  });

  const byResult: ResultCount[] = [
    { result: 'Valid', count: validScans, pct: round1((validScans / totalScans) * 100) },
    ...REJECTION_REASONS.map((result, index) => {
      const count = rejectionCounts[index];
      return { result, count, pct: round1((count / totalScans) * 100) };
    }),
  ];

  return {
    trip,
    totalScans,
    validScans,
    rejectedScans,
    validRatePct: round1((validScans / totalScans) * 100),
    byResult,
  };
}

const mock: TicketValidationStatsApi = {
  async getTrips() {
    await delay(400);
    return [...MOCK_TRIPS];
  },

  async getStats(tripId) {
    await delay(500);
    const trip = MOCK_TRIPS.find((item) => item.tripId === tripId);
    if (!trip) throw new Error('Không tìm thấy chuyến.');
    return buildStats(trip);
  },
};

const ticketValidationStatsApi: TicketValidationStatsApi = USE_MOCK_DATA ? mock : api;

export default ticketValidationStatsApi;
