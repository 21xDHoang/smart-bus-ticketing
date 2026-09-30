import axiosClient from './axiosClient';
import { fetchRoutes } from './routeApi';

// -----------------------------------------------------------------------------
// API vé tháng — nối với nhóm endpoint MonthlyPasses (story 16 "Đăng ký vé tháng").
//
// Backend CHƯA có endpoint vé tháng: migration MonthlyPasses/PassTypes và API đăng ký
// là task của Vàng Thị Dăm + Trần Trung Hiếu (Sprint 5). Vì vậy file này dựng theo
// đúng mẫu của routeLookupApi.ts — có nhánh dữ liệu giả để màn hình chạy được ngay,
// và nhánh API thật đã viết sẵn để khi backend xong chỉ cần đổi cờ `USE_MOCK_DATA`.
//
// Hợp đồng endpoint dự kiến (kebab-case, danh từ số nhiều — quy ước A của nhóm):
//   GET  /pass-types        → PassType[]   (bảng tham chiếu loại vé, task của Dăm)
//   POST /monthly-passes    → đăng ký vé tháng mới → MonthlyPass (201)
//   GET  /monthly-passes/me → vé tháng đang hoạt động của hành khách
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

/** Một tuyến trong ô chọn — rút gọn từ Route của routeApi, chỉ đủ để hiển thị. */
export interface MonthlyPassRoute {
  id: string;
  /** Mã tuyến, ví dụ "01", "B10". */
  code: string;
  name: string;
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
  /** Danh sách tuyến đang khai thác cho ô chọn. */
  listRoutes: () => Promise<MonthlyPassRoute[]>;
  register: (payload: RegisterMonthlyPassPayload) => Promise<MonthlyPass>;
}

// ---------------------------------------------------------------------------
// Bảng loại vé tháng — dữ liệu THAM CHIẾU. Backend chưa có bảng PassTypes nên tạm giữ
// ở client làm hằng số; khi Dăm migrate xong, chuyển sang GET /pass-types.
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
// Cờ chuyển giữa dữ liệu giả và API thật. Đang để `true` vì endpoint vé tháng chưa có
// (xem ghi chú đầu file). Đổi thành `false` khi Hiếu xong API đăng ký vé tháng.
const USE_MOCK_DATA = true;

const delay = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms));

/** Trần `pageSize` của GET /routes là 100 — số tuyến tối đa lấy trong một lượt gọi. */
const ROUTE_PAGE_SIZE = 100;

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

/**
 * Lấy toàn bộ tuyến đang khai thác bằng cách lặp theo trang, giống routeLookupApi.
 * Không lọc phía server vì GET /routes chỉ nhận MỘT tham số `search` chung.
 */
async function fetchActiveRoutes(): Promise<MonthlyPassRoute[]> {
  const routes: MonthlyPassRoute[] = [];

  for (let page = 1; ; page += 1) {
    const { items, total } = await fetchRoutes({
      page,
      pageSize: ROUTE_PAGE_SIZE,
      status: 'Active',
    });

    routes.push(
      ...items.map(({ id, code, name, origin, destination }) => ({
        id,
        code,
        name,
        origin,
        destination,
      })),
    );

    // `items.length === 0` là chốt chặn để không lặp vô hạn nếu `total` sai.
    if (routes.length >= total || items.length === 0) break;
  }

  return routes;
}

// -------- Gọi API thật (dùng khi USE_MOCK_DATA = false) --------
const api: MonthlyPassApi = {
  // GET /routes — chỉ lấy tuyến còn khai thác cho hành khách chọn.
  listRoutes: async () => fetchActiveRoutes(),

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
  async listRoutes() {
    await delay(300);
    return MOCK_ROUTES.map((route) => ({ ...route }));
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
