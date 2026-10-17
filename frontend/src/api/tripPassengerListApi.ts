// -----------------------------------------------------------------------------
// API DANH SÁCH HÀNH KHÁCH ĐÃ LÊN XE THEO CHUYẾN — phục vụ màn hình "Hành khách đã
// lên xe" (US 15 "Soát vé QR", Sprint 4, dòng 22, task của Dương Thị Hạnh). Màn của
// tài xế: chọn chuyến đang chạy rồi xem ai đã lên xe — tức các lượt soát vé có
// Result = 'Valid' của chuyến đó.
//
// Backend CHƯA có endpoint này — bảng TicketValidations đã có (PR #188) nhưng endpoint
// "danh sách khách đã lên xe theo chuyến" chưa ai chốt trong api-contract.md. Nên file
// này chạy dữ liệu giả (USE_MOCK_DATA = true) — cùng lối fillRateReportApi.ts. Khi
// backend + contract có, chỉ cần viết thật nhánh `api` rồi lật cờ.
//
// Nguồn dữ liệu thật: bảng TicketValidations lọc Result = 'Valid' theo TripId, nối sang
// Tickets (số ghế, tên khách qua UserId) và Stops (trạm khách thực sự lên). Xem docs/34
// §2 và §6: dòng 24 "đếm lượt soát, không đếm vé — lọc Result = 'Valid' nếu muốn số
// khách thật lên xe", nên màn này chỉ giữ các lượt Valid.
// -----------------------------------------------------------------------------

/** Một chuyến trong ô chọn chuyến của tài xế. */
export interface DriverTripOption {
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

/** Một hành khách đã lên xe — tương ứng một lượt soát vé hợp lệ (Result = 'Valid'). */
export interface BoardedPassenger {
  /** Id lượt soát (TicketValidations.Id) — làm khoá dòng. */
  validationId: string;

  /** Họ tên hành khách chủ vé — null khi vé không tra được tài khoản. */
  passengerName: string | null;

  seatNumber: string;

  /** Mã vé (nội dung mã QR đã soát). */
  ticketCode: string;

  /** Trạm khách thực sự lên xe — null khi lượt soát không ghi trạm. */
  boardingStopName: string | null;

  /** Giờ máy quét ghi nhận lượt lên xe (TicketValidations.ScannedAt). */
  boardedAt: string;
}

/** Toàn bộ dữ liệu một chuyến đã chọn. */
export interface TripBoardingList {
  trip: DriverTripOption;

  /** Số khách đã lên xe = số lượt soát hợp lệ. */
  boardedCount: number;

  /** Các khách đã lên xe, sắp theo giờ lên xe tăng dần. */
  passengers: BoardedPassenger[];
}

export interface TripPassengerListApi {
  /** Danh sách chuyến của tài xế hôm nay để chọn. */
  getDriverTrips: () => Promise<DriverTripOption[]>;

  /** Danh sách khách đã lên xe của một chuyến. */
  getBoardedPassengers: (tripId: string) => Promise<TripBoardingList>;
}

// ---------------------------------------------------------------------------
// Cờ chuyển giữa dữ liệu giả và API thật. Đang để `true` — backend chưa có endpoint.
const USE_MOCK_DATA = true;

const delay = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms));

// -------- Gọi API thật (dùng khi USE_MOCK_DATA = false) --------
// Backend chưa có endpoint danh sách khách đã lên xe nên nhánh này tạm chưa viết: nối
// axiosClient vào endpoint dự kiến khi api-contract.md đã chốt.
const api: TripPassengerListApi = {
  async getDriverTrips() {
    throw new Error('Backend danh sách khách đã lên xe chưa có — chưa thể tải dữ liệu thật.');
  },
  async getBoardedPassengers() {
    throw new Error('Backend danh sách khách đã lên xe chưa có — chưa thể tải dữ liệu thật.');
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

/** Băm một chuỗi thành số không âm — cùng một chuyến luôn ra cùng một danh sách khách. */
function hashString(value: string): number {
  let hash = 0;
  for (let i = 0; i < value.length; i++) hash = (hash * 31 + value.charCodeAt(i)) >>> 0;
  return hash;
}

/** Các chuyến cố định tài xế chạy trong ngày — trùng tuyến ở màn tỷ lệ lấp đầy để nhất quán. */
const TRIP_DEFS = [
  { routeCode: '01', routeName: 'Bến xe Mỹ Đình — Bến xe Gia Lâm', busPlate: '29B-12345', h: 6, m: 0 },
  { routeCode: '08', routeName: 'Cầu Giấy — Bờ Hồ Hoàn Kiếm', busPlate: '29B-23456', h: 8, m: 30 },
  { routeCode: '02', routeName: 'Bến xe Mỹ Đình — Bến xe Nước Ngầm', busPlate: '29B-34567', h: 12, m: 0 },
  { routeCode: '16', routeName: 'Bến xe Gia Lâm — Bến xe Yên Nghĩa', busPlate: '29B-45678', h: 17, m: 0 },
];

const PASSENGER_NAMES = [
  'Nguyễn Văn An', 'Trần Thị Bích', 'Lê Minh Châu', 'Phạm Quốc Dũng',
  'Hoàng Thị Em', 'Vũ Văn Phúc', 'Đặng Thị Hà', 'Bùi Minh Khôi',
  'Ngô Thu Lan', 'Đỗ Đức Mạnh', 'Phan Thị Nga', 'Trịnh Văn Quân',
  'Lý Thu Trang', 'Đinh Văn Sơn',
];

const BOARDING_STOPS = [
  'Bến xe Mỹ Đình', 'Cầu Giấy', 'Kim Mã', 'Bờ Hồ Hoàn Kiếm',
  'Bến xe Gia Lâm', 'Bến xe Nước Ngầm', 'Bến xe Giáp Bát', 'Bến xe Yên Nghĩa',
];

const SEAT_ROWS = ['A', 'B', 'C', 'D'];
const SEATS_PER_ROW = 6;

/** Số ghế theo thứ tự lên xe — A1, A2, … A6, B1, … — để mỗi khách một ghế khác nhau. */
function seatAt(index: number): string {
  const row = SEAT_ROWS[Math.floor(index / SEATS_PER_ROW) % SEAT_ROWS.length];
  return `${row}${(index % SEATS_PER_ROW) + 1}`;
}

/** Dựng danh sách chuyến của ngày hôm nay — chạy một lần lúc tải module để chuyến ổn định. */
function buildTrips(): DriverTripOption[] {
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

/**
 * Dựng danh sách khách đã lên xe của một chuyến. Số khách và thứ tự TẤT ĐỊNH (băm từ tripId)
 * chứ không `Math.random` — để mỗi lần mở cùng một chuyến danh sách vẫn ổn định.
 */
function buildPassengers(trip: DriverTripOption): BoardedPassenger[] {
  const seed = hashString(trip.tripId);
  const count = 6 + (seed % 9); // 6..14 khách — không quá cỡ danh sách tên để khỏi lặp.
  const departureEpoch = new Date(trip.departureTime).getTime();

  return Array.from({ length: count }, (_, index) => ({
    validationId: `${trip.tripId}-v${index + 1}`,
    passengerName: PASSENGER_NAMES[(seed + index) % PASSENGER_NAMES.length],
    seatNumber: seatAt(index),
    ticketCode: `SBT1:${trip.tripCode}.${pad(index + 1)}`,
    boardingStopName: BOARDING_STOPS[(seed + index * 3) % BOARDING_STOPS.length],
    // Khách lên dần trước giờ khởi hành: người đầu tiên lên sớm nhất, người cuối sát giờ đi.
    boardedAt: vietnamIso(departureEpoch - (count - index) * 3 * 60000),
  }));
}

const mock: TripPassengerListApi = {
  async getDriverTrips() {
    await delay(400);
    return [...MOCK_TRIPS];
  },

  async getBoardedPassengers(tripId) {
    await delay(500);
    const trip = MOCK_TRIPS.find((item) => item.tripId === tripId);
    if (!trip) throw new Error('Không tìm thấy chuyến.');
    const passengers = buildPassengers(trip);
    return { trip, boardedCount: passengers.length, passengers };
  },
};

const tripPassengerListApi: TripPassengerListApi = USE_MOCK_DATA ? mock : api;

export default tripPassengerListApi;
