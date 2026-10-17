import axiosClient from './axiosClient';
import { fetchRoutes } from './routeApi';

// -----------------------------------------------------------------------------
// API quản trị vé tháng — bổ sung ngoài story 16 (màn "Quản lý vé tháng" cho Admin/Quản lý).
//
// Hợp đồng endpoint — xem docs/api-contract.md mục "Vé tháng — /monthly-passes":
//   GET   /monthly-passes?search=&status=&routeId=&passTypeCode=&page=&pageSize=
//     → { items, total, page, pageSize }
//   GET   /monthly-passes/statistics → MonthlyPassStatistics
//   GET   /monthly-passes/{id}       → AdminMonthlyPass
//   PATCH /monthly-passes/{id}/status { status } → AdminMonthlyPass
//
// Và mục "Loại vé tháng — /pass-types":
//   GET    /pass-types        → PassTypeRecord[]
//   POST   /pass-types        → PassTypeRecord (201)
//   PUT    /pass-types/{id}   → PassTypeRecord
//   DELETE /pass-types/{id}   → PassTypeRecord
//
// Bốn endpoint vé tháng ở trên đều sau policy ManagerOrAbove — chỉ Admin và Quản lý gọi được; vai
// trò khác nhận 403. Riêng GET /pass-types chỉ cần đăng nhập (màn đăng ký vé tháng của hành khách
// đọc danh mục thật); POST/PUT/DELETE vẫn sau ManagerOrAbove.
// -----------------------------------------------------------------------------

/** Trạng thái LƯU của vé tháng — khớp enum MonthlyPassStatus của backend. */
export type MonthlyPassStoredStatus = 'Active' | 'Expired' | 'Cancelled';

/**
 * Một dòng vé tháng của màn quản trị — khớp `MonthlyPassAdminResponse`. Khác `MonthlyPass` của
 * màn hành khách ở chỗ kèm thông tin hành khách, tuyến và loại vé để bảng không phải ghép ở client.
 */
export interface AdminMonthlyPass {
  id: string;
  /** Mã vé tháng (= nội dung mã QR soát vé). */
  code: string;
  userId: string;
  userFullName: string;
  userPhoneNumber: string;
  routeId: string;
  routeCode: string;
  routeName: string;
  passTypeCode: string;
  passTypeName: string;
  /** Số tiền thực trả — ảnh chụp lúc đăng ký/gia hạn. */
  price: number;
  validFrom: string;
  validTo: string;
  /** Trạng thái lưu. Active/Expired có độ trễ; Cancelled là chặn cứng do quản trị viên đặt. */
  status: MonthlyPassStoredStatus;
  createdAt: string;
  updatedAt: string | null;
}

export interface AdminMonthlyPassList {
  items: AdminMonthlyPass[];
  /** Tổng số dòng KHỚP BỘ LỌC — không phải số dòng trong `items`. */
  total: number;
  page: number;
  pageSize: number;
}

export interface MonthlyPassListParams {
  /** Tìm theo mã vé, họ tên hoặc SĐT hành khách. */
  search?: string;
  /** Lọc theo cột trạng thái lưu; bỏ trống = cả ba. */
  status?: MonthlyPassStoredStatus;
  routeId?: string;
  passTypeCode?: string;
  page: number;
  pageSize: number;
}

/** Khớp `MonthlyPassStatisticsResponse`. */
export interface MonthlyPassStatistics {
  totalCount: number;
  /** Còn hiệu lực thật: chưa huỷ và validFrom <= bây giờ <= validTo. */
  activeCount: number;
  /** Vé mua trước cho kỳ sau. */
  upcomingCount: number;
  /** Đã quá validTo — đếm theo ngày, không theo cột Status. */
  expiredCount: number;
  cancelledCount: number;
  /** Tổng tiền đã thu của mọi vé, tính cả vé đã huỷ (huỷ không hoàn tiền). */
  totalRevenue: number;
  revenueThisMonth: number;
}

/** Một loại vé tháng — khớp `PassTypeResponse`. */
export interface PassTypeRecord {
  id: string;
  /** Mã nghiệp vụ (nội dung `passTypeCode` khi đăng ký). Không sửa được sau khi tạo. */
  code: string;
  name: string;
  durationMonths: number;
  price: number;
  /** Số vé tháng đang tham chiếu — lớn hơn 0 thì không xoá được. */
  monthlyPassCount: number;
  createdAt: string;
  updatedAt: string | null;
}

/** Body tạo loại vé — POST /pass-types. */
export interface CreatePassTypePayload {
  code: string;
  name: string;
  durationMonths: number;
  price: number;
}

/** Body sửa loại vé — PUT /pass-types/{id}. Mã là bất biến nên không gửi. */
export type UpdatePassTypePayload = Omit<CreatePassTypePayload, 'code'>;

/**
 * Nhãn cho trạng thái LƯU — dùng cho bộ lọc. Nhãn kèm nghĩa ngay trong ngoặc vì cột `status`
 * là trạng thái lưu, KHÔNG phải trạng thái hiệu lực hiển thị trên bảng (xem `effectiveStatusOf`
 * ở MonthlyPassManagePage).
 */
export const MONTHLY_PASS_STORED_STATUS_OPTIONS: {
  value: MonthlyPassStoredStatus;
  label: string;
}[] = [
  { value: 'Active', label: 'Active (chưa hết hạn)' },
  { value: 'Expired', label: 'Expired (đã hết hạn)' },
  { value: 'Cancelled', label: 'Đã huỷ' },
];

/** Một tuyến trong ô lọc — rút gọn từ GET /routes. */
export interface RouteOption {
  id: string;
  code: string;
  name: string;
}

/** Trần pageSize của GET /routes — cùng con số các màn khác đang dùng. */
const ROUTE_PICKER_PAGE_SIZE = 100;

/**
 * GET /monthly-passes — danh sách + lọc + phân trang.
 *
 * Chỉ gửi tham số khi thực sự có giá trị: backend coi `status` không khớp mã nào là "lọc ra
 * rỗng" chứ KHÔNG báo lỗi, nên lỡ gửi `status=` rỗng là bảng âm thầm trắng.
 */
export function fetchMonthlyPasses(params: MonthlyPassListParams): Promise<AdminMonthlyPassList> {
  const query: Record<string, string | number> = { page: params.page, pageSize: params.pageSize };

  if (params.search) query.search = params.search;
  if (params.status) query.status = params.status;
  if (params.routeId) query.routeId = params.routeId;
  if (params.passTypeCode) query.passTypeCode = params.passTypeCode;

  return axiosClient.get<AdminMonthlyPassList, AdminMonthlyPassList>('/monthly-passes', {
    params: query,
  });
}

/** GET /monthly-passes/statistics. */
export function fetchMonthlyPassStatistics(): Promise<MonthlyPassStatistics> {
  return axiosClient.get<MonthlyPassStatistics, MonthlyPassStatistics>(
    '/monthly-passes/statistics',
  );
}

/** GET /monthly-passes/{id} — chi tiết một vé bất kỳ. */
export function fetchMonthlyPass(id: string): Promise<AdminMonthlyPass> {
  return axiosClient.get<AdminMonthlyPass, AdminMonthlyPass>(`/monthly-passes/${id}`);
}

/**
 * PATCH /monthly-passes/{id}/status — huỷ ("Cancelled") hoặc mở lại ("Active").
 * Backend trả 409 kèm câu giải thích khi vé đã hết hạn / đang ở trạng thái đích.
 */
export function updateMonthlyPassStatus(
  id: string,
  status: 'Active' | 'Cancelled',
): Promise<AdminMonthlyPass> {
  return axiosClient.patch<AdminMonthlyPass, AdminMonthlyPass>(`/monthly-passes/${id}/status`, {
    status,
  });
}

/** GET /pass-types — danh mục loại vé, sắp theo thời hạn rồi mã. */
export function fetchPassTypes(): Promise<PassTypeRecord[]> {
  return axiosClient.get<PassTypeRecord[], PassTypeRecord[]>('/pass-types');
}

/** POST /pass-types — thêm loại vé. Trùng mã → 409. */
export function createPassType(payload: CreatePassTypePayload): Promise<PassTypeRecord> {
  return axiosClient.post<PassTypeRecord, PassTypeRecord>('/pass-types', payload);
}

/** PUT /pass-types/{id} — sửa tên/thời hạn/giá. */
export function updatePassType(
  id: string,
  payload: UpdatePassTypePayload,
): Promise<PassTypeRecord> {
  return axiosClient.put<PassTypeRecord, PassTypeRecord>(`/pass-types/${id}`, payload);
}

/** DELETE /pass-types/{id} — chỉ xoá được loại vé chưa từng được dùng. */
export function deletePassType(id: string): Promise<PassTypeRecord> {
  return axiosClient.delete<PassTypeRecord, PassTypeRecord>(`/pass-types/${id}`);
}

/**
 * Toàn bộ tuyến cho ô lọc. GET /routes trả tối đa 100 dòng mỗi trang nên lặp theo trang — cùng
 * lối `fetchActiveBusOptions` của busApi.ts.
 */
export async function fetchRouteOptions(): Promise<RouteOption[]> {
  const options: RouteOption[] = [];

  for (let page = 1; ; page += 1) {
    const { items, total } = await fetchRoutes({ page, pageSize: ROUTE_PICKER_PAGE_SIZE });

    options.push(...items.map(({ id, code, name }) => ({ id, code, name })));

    // items.length === 0 là chốt chặn để không lặp vô hạn nếu `total` sai.
    if (options.length >= total || items.length === 0) break;
  }

  return options;
}
