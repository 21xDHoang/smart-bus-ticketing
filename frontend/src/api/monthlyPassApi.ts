import axiosClient from './axiosClient';

// -----------------------------------------------------------------------------
// API vé tháng — nối với nhóm endpoint MonthlyPasses (story 16 "Đăng ký vé tháng").
//
// Backend ĐÃ có (MonthlyPassRegistrationController / MonthlyPassLookupController):
//   POST /monthly-passes     → đăng ký vé tháng mới → MonthlyPass (200)
//   GET  /monthly-passes/me  → vé tháng đang hoạt động của chính người gọi (không dùng ở đây,
//                              màn "Vé tháng của tôi" dùng myMonthlyPassApi.ts)
//
// Nguồn tuyến cho ô chọn: cặp API CÔNG KHAI của story 1 — GET /stops/search (gợi ý trạm, xem
// stopSearchApi.ts) rồi GET /routes/search (tuyến khớp cặp điểm đi/điểm đến). CỐ Ý không dùng
// GET /routes: endpoint đó nằm sau policy ManagerOrAbove (màn quản lý tuyến, story 12), hành
// khách gọi vào nhận 403 "Bạn không có quyền truy cập tính năng này." — đúng lỗi từng thấy ở
// màn đăng ký vé tháng. /routes/search bắt buộc đủ điểm đi + điểm đến (mục "/routes/search"
// của docs/api-contract.md), nên màn đăng ký chọn tuyến bằng cặp điểm đi/điểm đến — đúng luồng
// hành khách mà hợp đồng đã vạch.
//
// Vé tháng (MonthlyPass) theo quy ước: là quyền đi lại trên MỘT tuyến trong khoảng
// thời gian — có Code (mã QR unique) + RouteId + ValidFrom/ValidTo + Status. KHÔNG kèm
// ghế (khách vé tháng không đảm bảo có chỗ ngồi).
// -----------------------------------------------------------------------------

/** Mã loại vé tháng — giữ chỗ cho bảng PassTypes của backend, chưa có giá trị chính thức. */
export type PassTypeCode = 'OneMonth' | 'ThreeMonths' | 'SixMonths' | 'TwelveMonths';

/** Một loại vé tháng (thời hạn + giá gói). */
export interface PassType {
  code: PassTypeCode;

  /** Tên hiển thị cho hành khách, ví dụ "Vé tháng 1 tháng". */
  name: string;

  /** Thời hạn hiệu lực, tính bằng tháng. */
  durationMonths: number;

  /** Giá gói (VND) cho toàn bộ thời hạn — mua dài hạn được chiết khấu. */
  price: number;
}

/** Một tuyến trong danh sách chọn — rút gọn từ kết quả GET /routes/search, chỉ đủ để hiển thị. */
export interface MonthlyPassRoute {
  id: string;
  /** Mã tuyến, ví dụ "01", "B10". */
  code: string;
  name: string;
  origin: string;
  destination: string;
}

/**
 * Một dòng của GET /routes/search — rút gọn còn các trường màn đăng ký dùng.
 * Backend còn trả `distanceKm`, `stops`, `minPrice` (màn tra cứu dùng) — ở đây không cần.
 */
interface RouteSearchResult {
  routeId: string;
  routeCode: string;
  routeName: string;
  origin: string;
  destination: string;
}

/** Trạng thái vé tháng — khớp cột varchar Status của bảng MonthlyPasses. */
export type MonthlyPassStatus = 'Active' | 'Expired';

/** Một vé tháng đã đăng ký, trả về sau khi gọi POST /monthly-passes. */
export interface MonthlyPass {
  id: string;

  /** Mã vé tháng (QR) — duy nhất, dùng khi soát vé lên xe. */
  code: string;

  routeId: string;

  passTypeCode: PassTypeCode;

  /** Giá đã thanh toán khi đăng ký (VND). */
  price: number;

  /** Ngày bắt đầu hiệu lực (ISO 8601). */
  validFrom: string;

  /** Ngày hết hiệu lực (ISO 8601). */
  validTo: string;

  status: MonthlyPassStatus;

  createdAt: string;
}

/** Body khi đăng ký một vé tháng mới — POST /monthly-passes. */
export interface RegisterMonthlyPassPayload {
  routeId: string;
  passTypeCode: PassTypeCode;
}

export interface MonthlyPassApi {
  /** Tuyến đang khai thác khớp cặp điểm đi/điểm đến — nguồn công khai cho hành khách. */
  searchRoutes: (origin: string, destination: string) => Promise<MonthlyPassRoute[]>;
  register: (payload: RegisterMonthlyPassPayload) => Promise<MonthlyPass>;
}

// ---------------------------------------------------------------------------
// Bảng loại vé tháng — dữ liệu THAM CHIẾU để vẽ ô chọn. Chưa có endpoint GET /pass-types
// nên vẫn giữ ở client; backend tra mã trong bảng PassTypes và trả 404 nếu mã không khớp,
// nên BỐN MÃ dưới đây phải tồn tại trong bảng PassTypes (mã là khoá nghiệp vụ, cùng lối
// Routes.Code). Giá hiển thị ở đây chỉ là bản xem trước — giá THẬT do backend chụp từ
// PassTypes lúc đăng ký; lệch nhau thì vé vẫn tạo theo giá backend.
//
// Giá gói tạm thời theo quy ước "mua càng dài càng rẻ" — tổng giá 3/6/12 tháng thấp hơn
// tích luỹ 1 tháng nhân đúng số tháng. Số liệu chỉ để dựng giao diện, chưa phải giá thật.
// ---------------------------------------------------------------------------
export const PASS_TYPES: PassType[] = [
  { code: 'OneMonth', name: 'Vé tháng 1 tháng', durationMonths: 1, price: 200_000 },
  { code: 'ThreeMonths', name: 'Vé tháng 3 tháng', durationMonths: 3, price: 550_000 },
  { code: 'SixMonths', name: 'Vé tháng 6 tháng', durationMonths: 6, price: 1_000_000 },
  { code: 'TwelveMonths', name: 'Vé tháng 12 tháng', durationMonths: 12, price: 1_800_000 },
];

/** Danh sách loại vé cho ô chọn, theo đúng thứ tự khai báo ở PASS_TYPES. */
export const PASS_TYPE_OPTIONS: { value: PassTypeCode; label: string }[] = PASS_TYPES.map(
  ({ code, name }) => ({ value: code, label: name }),
);

/** Tra cứu một loại vé theo mã — trả về `undefined` nếu mã không hợp lệ. */
export function findPassType(code: PassTypeCode | undefined): PassType | undefined {
  return PASS_TYPES.find((passType) => passType.code === code);
}

// ---------------------------------------------------------------------------
// Cờ chuyển giữa dữ liệu giả và API thật. Đang để `false` — endpoint vé tháng đã có.
const USE_MOCK_DATA = false;

const delay = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms));

/** Cộng `months` tháng vào một ngày, giữ nguyên giờ phút hiện tại. */
function addMonths(date: Date, months: number): Date {
  const result = new Date(date);
  result.setMonth(result.getMonth() + months);
  return result;
}

/** Sinh mã vé tháng (QR) dễ đọc — ví dụ "MP-01-A1B2C3". */
function generatePassCode(routeCode: string): string {
  const suffix = Array.from({ length: 6 }, () =>
    'ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789'.charAt(
      Math.floor(Math.random() * 36),
    ),
  ).join('');

  return `MP-${routeCode}-${suffix}`;
}

/** Rút gọn kết quả /routes/search về đúng những gì danh sách chọn cần. */
function toMonthlyPassRoute(result: RouteSearchResult): MonthlyPassRoute {
  return {
    id: result.routeId,
    code: result.routeCode,
    name: result.routeName,
    origin: result.origin,
    destination: result.destination,
  };
}

// -------- Gọi API thật (dùng khi USE_MOCK_DATA = false) --------
const api: MonthlyPassApi = {
  // GET /routes/search — API công khai, chỉ trả tuyến Active có trạm đi đứng trước trạm đến.
  searchRoutes: async (origin, destination) => {
    const results = await axiosClient.get<RouteSearchResult[], RouteSearchResult[]>(
      '/routes/search',
      { params: { origin, destination } },
    );

    return results.map(toMonthlyPassRoute);
  },

  // POST /monthly-passes — body { routeId, passTypeCode }. Backend tính giá + thời hạn.
  register: (payload) => axiosClient.post<MonthlyPass, MonthlyPass>('/monthly-passes', payload),
};

// -------- Dữ liệu giả (dùng khi USE_MOCK_DATA = true) --------
// Vài tuyến quanh Hà Nội, khớp với routeLookupApi để cảm giác nhất quán khi demo.
const MOCK_ROUTES: MonthlyPassRoute[] = [
  {
    id: 'route-01',
    code: '01',
    name: 'Bến xe Mỹ Đình — Bến xe Gia Lâm',
    origin: 'Bến xe Mỹ Đình',
    destination: 'Bến xe Gia Lâm',
  },
  {
    id: 'route-02',
    code: '02',
    name: 'Bến xe Mỹ Đình — Bến xe Nước Ngầm',
    origin: 'Bến xe Mỹ Đình',
    destination: 'Bến xe Nước Ngầm',
  },
  {
    id: 'route-08',
    code: '08',
    name: 'Cầu Giấy — Bờ Hồ Hoàn Kiếm',
    origin: 'Cầu Giấy',
    destination: 'Bờ Hồ Hoàn Kiếm',
  },
  {
    id: 'route-B10',
    code: 'B10',
    name: 'Bến xe Yên Nghĩa — Bến xe Mỹ Đình',
    origin: 'Bến xe Yên Nghĩa',
    destination: 'Bến xe Mỹ Đình',
  },
];

const mock: MonthlyPassApi = {
  async searchRoutes(origin, destination) {
    await delay(300);

    // Bản giả khớp thô theo tên trong mã/tên/điểm đầu/điểm cuối của tuyến — đủ để dựng giao
    // diện; bản thật khớp theo danh sách trạm của tuyến nên còn ra cả tuyến đi ngang qua trạm.
    const needle = (text: string) => text.trim().toLowerCase();
    const from = needle(origin);
    const to = needle(destination);

    return MOCK_ROUTES.filter((route) =>
      [route.origin, route.destination, route.name].some((text) =>
        text.toLowerCase().includes(from),
      ) &&
      [route.origin, route.destination, route.name].some((text) =>
        text.toLowerCase().includes(to),
      ),
    ).map((route) => ({ ...route }));
  },

  async register({ routeId, passTypeCode }) {
    await delay(600);

    const route = MOCK_ROUTES.find((item) => item.id === routeId);
    const passType = findPassType(passTypeCode);

    // Lỗi dạng "không tìm thấy" giả lập như backend — màn hình hiển thị chung một kiểu.
    if (!route) throw new Error('Không tìm thấy tuyến đã chọn.');
    if (!passType) throw new Error('Không tìm thấy loại vé đã chọn.');

    const validFrom = new Date();
    const validTo = addMonths(validFrom, passType.durationMonths);

    return {
      id: `pass-${Date.now()}-${Math.random().toString(36).slice(2, 8)}`,
      code: generatePassCode(route.code),
      routeId,
      passTypeCode,
      price: passType.price,
      validFrom: validFrom.toISOString(),
      validTo: validTo.toISOString(),
      status: 'Active',
      createdAt: validFrom.toISOString(),
    };
  },
};

const monthlyPassApi: MonthlyPassApi = USE_MOCK_DATA ? mock : api;

export default monthlyPassApi;
