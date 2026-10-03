import axiosClient from './axiosClient';
import routeLookupApi from './routeLookupApi';

// -----------------------------------------------------------------------------
// API kết quả tìm kiếm chuyến — màn hình "Kết quả tìm kiếm" (User Story 1, Sprint 2).
//
// Endpoint chính đã có trên main: GET /trips/search — "API trả về kết quả gồm giá vé, giờ
// chạy, số ghế còn trống" (Phùng Duy Hoàng), CÔNG KHAI không cần đăng nhập; hợp đồng ở mục
// "GET /trips/search" của docs/api-contract.md.
//
// Việc tìm RA TUYẾN từ điểm đi/điểm đến vẫn chưa có endpoint công khai (task của Trần Trung
// Hiếu), nên khi thiếu `routeId` module nhờ routeLookupApi ghép tạm từ GET /routes — đường
// đó cần vai trò Manager/Admin. Có `routeId` (đi từ màn hình tra cứu tuyến) thì gọi thẳng
// endpoint công khai, không cần quyền gì.
// -----------------------------------------------------------------------------

/** Tham số tìm chuyến — được truyền từ màn hình "Tra cứu tuyến" (form của Dương Thị Hạnh). */
export interface TripSearchParams {
  /** Điểm đi — khớp `Route.origin`. */
  origin: string;

  /** Điểm đến — khớp `Route.destination`. */
  destination: string;

  /** Ngày đi (yyyy-MM-dd). Ghép thành `from`/`to` của TRỌN ngày đó (giờ Việt Nam) khi gọi API. */
  date?: string;

  /** Tuyến đã chọn — nếu có thì chỉ liệt kê chuyến của tuyến đó. */
  routeId?: string;
}

/** Một chuyến trong kết quả tìm kiếm — đủ để hành khách so giờ và giá. */
export interface TripSearchResult {
  id: string;
  routeId: string;

  /** Mã tuyến hiển thị cho hành khách — "01", "E01". */
  routeCode: string;

  routeName: string;

  /** Giờ khởi hành — ISO 8601 kèm múi giờ (+07:00). */
  departureTime: string;

  /** Giờ dự kiến tới bến cuối. null khi chưa chốt. */
  arrivalTime: string | null;

  /** Giá vé phổ thông (VND) — "giá vé" trong task. null khi tuyến chưa cấu hình giá. */
  price: number | null;

  /** Số ghế còn trống — "số ghế còn trống" trong task. */
  seatsRemaining: number;

  /** Sức chứa theo số ghế — để hiển thị "còn X/Y ghế". */
  capacity: number;

  /** Loại xe. */
  busType: string;
}

export interface TripSearchApi {
  search: (params: TripSearchParams) => Promise<TripSearchResult[]>;
}

// ---------------------------------------------------------------------------
// Cờ chuyển giữa dữ liệu giả và API thật. Đã bật API thật — GET /trips/search có trên main
// (xem ghi chú đầu file). Nhánh giả giữ lại làm đường lùi khi cần dựng giao diện lúc mất
// mạng; đổi cờ này thành `true` là quay lại được.
const USE_MOCK_DATA = false;

const delay = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms));

/**
 * Khoảng thời gian của TRỌN một ngày (giờ Việt Nam) — cùng quy ước "lọc theo ngày = gửi
 * from/to của trọn ngày" của GET /trips/search và GET /trips (docs/api-contract.md).
 * Không có ngày thì không gửi khoảng nào — endpoint trả mọi chuyến Scheduled của tuyến.
 */
function searchRange(date: string | undefined): Record<string, string> {
  if (!date) return {};

  return { from: `${date}T00:00:00+07:00`, to: `${date}T23:59:59+07:00` };
}

// -------- Gọi API thật (dùng khi USE_MOCK_DATA = false) --------
const api: TripSearchApi = {
  async search({ origin, destination, date, routeId }) {
    // Có routeId (đi từ màn hình tra cứu tuyến) → gọi thẳng endpoint công khai.
    // Thiếu routeId → nhờ màn tra cứu tuyến tìm các tuyến khớp điểm đi/điểm đến trước;
    // đó là đường Manager/Admin vì GET /routes còn sau policy (xem ghi chú đầu file).
    const routeIds = routeId
      ? [routeId]
      : (await routeLookupApi.search({ origin, destination })).map((route) => route.routeId);

    const range = searchRange(date);

    const lists = await Promise.all(
      routeIds.map((id) =>
        axiosClient.get<TripSearchResult[], TripSearchResult[]>('/trips/search', {
          params: { routeId: id, ...range },
        }),
      ),
    );

    // Gộp nhiều tuyến rồi sắp theo giờ khởi hành tăng dần — cùng thứ tự backend trả cho
    // một tuyến. Màn hình còn sắp lại theo lựa chọn của hành khách (giờ/giá).
    return lists.flat().sort((a, b) => a.departureTime.localeCompare(b.departureTime));
  },
};

// -------- Dữ liệu giả (dùng khi USE_MOCK_DATA = true) --------
// Vài chuyến trong ngày trên hai tuyến Mỹ Đình → Gia Lâm và Cầu Giấy → Bờ Hồ để giao diện
// có đủ giờ/giá/ghế cho việc sắp xếp. Thứ tự trong mảng cố tình KHÔNG theo giờ — trang kết
// quả sẽ sắp xếp lại theo lựa chọn của hành khách.
interface MockTrip {
  routeId: string;
  routeCode: string;
  routeName: string;
  origin: string;
  destination: string;
  /** Giờ khởi hành trong ngày (HH:mm). */
  departure: string;
  /** Giờ dự kiến tới bến cuối (HH:mm). */
  arrival: string;
  price: number;
  seatsRemaining: number;
  capacity: number;
  busType: string;
}

const MOCK_TRIPS: MockTrip[] = [
  {
    routeId: 'route-01',
    routeCode: '01',
    routeName: 'Bến xe Mỹ Đình — Bến xe Gia Lâm',
    origin: 'Bến xe Mỹ Đình',
    destination: 'Bến xe Gia Lâm',
    departure: '17:20',
    arrival: '18:05',
    price: 8000,
    seatsRemaining: 22,
    capacity: 29,
    busType: 'Hyundai County 29 chỗ',
  },
  {
    routeId: 'route-01',
    routeCode: '01',
    routeName: 'Bến xe Mỹ Đình — Bến xe Gia Lâm',
    origin: 'Bến xe Mỹ Đình',
    destination: 'Bến xe Gia Lâm',
    departure: '06:15',
    arrival: '07:00',
    price: 8000,
    seatsRemaining: 5,
    capacity: 29,
    busType: 'Hyundai County 29 chỗ',
  },
  {
    routeId: 'route-01',
    routeCode: '01',
    routeName: 'Bến xe Mỹ Đình — Bến xe Gia Lâm',
    origin: 'Bến xe Mỹ Đình',
    destination: 'Bến xe Gia Lâm',
    departure: '09:00',
    arrival: '09:45',
    price: 8000,
    seatsRemaining: 12,
    capacity: 29,
    busType: 'Hyundai County 29 chỗ',
  },
  {
    routeId: 'route-01',
    routeCode: '01',
    routeName: 'Bến xe Mỹ Đình — Bến xe Gia Lâm',
    origin: 'Bến xe Mỹ Đình',
    destination: 'Bến xe Gia Lâm',
    departure: '12:45',
    arrival: '13:30',
    price: 8000,
    seatsRemaining: 0,
    capacity: 29,
    busType: 'Hyundai County 29 chỗ',
  },
  {
    routeId: 'route-01',
    routeCode: '01',
    routeName: 'Bến xe Mỹ Đình — Bến xe Gia Lâm',
    origin: 'Bến xe Mỹ Đình',
    destination: 'Bến xe Gia Lâm',
    departure: '07:30',
    arrival: '08:15',
    price: 8000,
    seatsRemaining: 28,
    capacity: 29,
    busType: 'Hyundai County 29 chỗ',
  },
  {
    routeId: 'route-E01',
    routeCode: 'E01',
    routeName: 'Bến xe Mỹ Đình — Bến xe Gia Lâm (E)',
    origin: 'Bến xe Mỹ Đình',
    destination: 'Bến xe Gia Lâm',
    departure: '08:00',
    arrival: '08:40',
    price: 12000,
    seatsRemaining: 30,
    capacity: 45,
    busType: 'Xe buýt điện 45 chỗ',
  },
  {
    routeId: 'route-E01',
    routeCode: 'E01',
    routeName: 'Bến xe Mỹ Đình — Bến xe Gia Lâm (E)',
    origin: 'Bến xe Mỹ Đình',
    destination: 'Bến xe Gia Lâm',
    departure: '18:30',
    arrival: '19:10',
    price: 12000,
    seatsRemaining: 3,
    capacity: 45,
    busType: 'Xe buýt điện 45 chỗ',
  },
  {
    routeId: 'route-E01',
    routeCode: 'E01',
    routeName: 'Bến xe Mỹ Đình — Bến xe Gia Lâm (E)',
    origin: 'Bến xe Mỹ Đình',
    destination: 'Bến xe Gia Lâm',
    departure: '11:15',
    arrival: '11:55',
    price: 12000,
    seatsRemaining: 15,
    capacity: 45,
    busType: 'Xe buýt điện 45 chỗ',
  },
  {
    routeId: 'route-08',
    routeCode: '08',
    routeName: 'Cầu Giấy — Bờ Hồ Hoàn Kiếm',
    origin: 'Cầu Giấy',
    destination: 'Bờ Hồ Hoàn Kiếm',
    departure: '07:45',
    arrival: '08:15',
    price: 5000,
    seatsRemaining: 18,
    capacity: 29,
    busType: 'Hyundai County 29 chỗ',
  },
  {
    routeId: 'route-08',
    routeCode: '08',
    routeName: 'Cầu Giấy — Bờ Hồ Hoàn Kiếm',
    origin: 'Cầu Giấy',
    destination: 'Bờ Hồ Hoàn Kiếm',
    departure: '16:10',
    arrival: '16:40',
    price: 5000,
    seatsRemaining: 2,
    capacity: 29,
    busType: 'Hyundai County 29 chỗ',
  },
];

/** Ngày mặc định khi form không truyền `date` — để kết quả giả vẫn có giờ khởi hành hợp lệ. */
const MOCK_DATE = '2026-10-01';

/** So khớp chuỗi không phân biệt hoa thường, đã bỏ khoảng trắng thừa hai đầu. */
function matches(value: string, keyword: string): boolean {
  return value.toLowerCase().includes(keyword.toLowerCase());
}

/** Ghép ngày tìm kiếm với giờ chạy thành chuỗi ISO kèm múi giờ Việt Nam. */
function isoAt(date: string, time: string): string {
  return `${date}T${time}:00+07:00`;
}

const mock: TripSearchApi = {
  async search({ origin, destination, date, routeId }) {
    await delay(600);

    const keywordOrigin = origin.trim().toLowerCase();
    const keywordDestination = destination.trim().toLowerCase();
    const day = date ?? MOCK_DATE;

    return MOCK_TRIPS.filter((trip) => {
      if (!matches(trip.origin, keywordOrigin)) return false;
      if (!matches(trip.destination, keywordDestination)) return false;
      if (routeId && trip.routeId !== routeId) return false;
      return true;
    }).map((trip) => ({
      id: `${trip.routeId}-${trip.departure.replace(':', '')}`,
      routeId: trip.routeId,
      routeCode: trip.routeCode,
      routeName: trip.routeName,
      departureTime: isoAt(day, trip.departure),
      arrivalTime: isoAt(day, trip.arrival),
      price: trip.price,
      seatsRemaining: trip.seatsRemaining,
      capacity: trip.capacity,
      busType: trip.busType,
    }));
  },
};

const tripSearchApi: TripSearchApi = USE_MOCK_DATA ? mock : api;

export default tripSearchApi;
