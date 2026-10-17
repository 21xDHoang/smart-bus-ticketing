import axiosClient from './axiosClient';

// -----------------------------------------------------------------------------
// API vé tháng — nối với nhóm endpoint MonthlyPasses (story 16 "Đăng ký vé tháng").
//
// Backend ĐÃ có:
//   GET  /pass-types         → danh mục loại vé (tên, thời hạn, giá) — đọc thẳng từ bảng PassTypes
//   POST /monthly-passes     → đăng ký vé tháng mới → MonthlyPass (200)
//   GET  /monthly-passes/me  → vé tháng đang hoạt động của chính người gọi (không dùng ở đây,
//                              màn "Vé tháng của tôi" dùng myMonthlyPassApi.ts)
//
// KHÔNG có dữ liệu mẫu trong file này: danh mục loại vé lấy từ GET /pass-types, tuyến lấy từ
// cặp API công khai GET /stops/search + GET /routes/search. Trước đây file giữ một bảng
// PASS_TYPES hằng ở client — đã bỏ, vì sửa giá gói trong CSDL mà client vẫn hiện số cũ là
// hiển thị sai dữ liệu bán hàng.
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

/**
 * Mã loại vé tháng — khoá nghiệp vụ của bảng PassTypes. Danh sách mã do CSDL quyết định
 * (quản trị viên thêm/sửa qua màn "Vé tháng"), nên đây là chuỗi tự do chứ không phải union
 * của bốn mã cố định như trước.
 */
export type PassTypeCode = string;

/** Một loại vé tháng đọc từ GET /pass-types. */
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
export type MonthlyPassStatus = 'Active' | 'Expired' | 'Cancelled';

/** Một vé tháng đã đăng ký, trả về từ POST /monthly-passes, /renew và GET /me. */
export interface MonthlyPass {
  id: string;

  /** Mã vé tháng (QR) — duy nhất, dùng khi soát vé lên xe. */
  code: string;

  routeId: string;

  /** Mã và tên tuyến do backend trả kèm — hiển thị thẳng, không gọi GET /routes (403 với hành khách). */
  routeCode: string;
  routeName: string;

  passTypeCode: PassTypeCode;

  /** Tên loại vé đọc từ bảng PassTypes — hiển thị thẳng, không tra bảng hằng ở client. */
  passTypeName: string;

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
  /** Danh mục loại vé thật trong CSDL — nguồn duy nhất cho tên/thời hạn/giá. */
  listPassTypes: () => Promise<PassType[]>;
  /** Tuyến đang khai thác khớp cặp điểm đi/điểm đến — nguồn công khai cho hành khách. */
  searchRoutes: (origin: string, destination: string) => Promise<MonthlyPassRoute[]>;
  register: (payload: RegisterMonthlyPassPayload) => Promise<MonthlyPass>;
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

const monthlyPassApi: MonthlyPassApi = {
  // GET /pass-types — chỉ cần đăng nhập (hợp đồng mục "Loại vé tháng — /pass-types"). Trả mảng
  // trần, đã sắp theo thời hạn tăng dần rồi mã.
  listPassTypes: () => axiosClient.get<PassType[], PassType[]>('/pass-types'),

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

export default monthlyPassApi;
