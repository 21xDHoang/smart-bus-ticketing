import axiosClient from './axiosClient';

// Hợp đồng API /routes — xem docs/api-contract.md mục "Tuyến đường — /routes".
// Backend đã có thật (RoutesController — Trần Trung Hiếu, story 12) nên file này KHÔNG
// có nhánh dữ liệu giả như stopApi.ts / adminUserApi.ts: gọi thẳng API.

/** Trạng thái tuyến — khớp enum RouteStatus và cột varchar dưới CSDL (quy ước A3). */
export type RouteStatus = 'Active' | 'Inactive';

export interface Route {
  id: string;

  /** Mã tuyến hiển thị cho hành khách — "01", "B10"… Duy nhất toàn hệ thống. */
  code: string;

  name: string;

  /** Điểm đầu — tên địa danh, KHÔNG phải khoá ngoại tới Stops. */
  origin: string;

  /** Điểm cuối. */
  destination: string;

  /** Tổng chiều dài tuyến (km). Backend trả decimal(6,2), không phải float. */
  distanceKm: number;

  status: RouteStatus;

  createdAt: string;

  /** null khi tuyến chưa được sửa lần nào. */
  updatedAt: string | null;
}

export interface RouteListParams {
  /** Tìm theo mã, tên, điểm đầu hoặc điểm cuối — không phân biệt hoa thường. */
  search?: string;

  /** Bỏ trống = lấy cả hai trạng thái. */
  status?: RouteStatus;

  page: number;
  pageSize: number;
}

export interface RouteListResult {
  /** Chỉ là trang hiện tại, không phải toàn bộ. */
  items: Route[];

  /** Tổng số dòng KHỚP BỘ LỌC (không phải số dòng trong `items`) — dùng để vẽ phân trang. */
  total: number;

  page: number;
  pageSize: number;
}

interface RouteStatusMeta {
  label: string;
  /** Tên màu preset của Tag AntD. */
  color: string;
}

export const ROUTE_STATUS_META: Record<RouteStatus, RouteStatusMeta> = {
  Active: { label: 'Đang khai thác', color: 'success' },
  Inactive: { label: 'Ngừng khai thác', color: 'default' },
};

/** Danh sách trạng thái cho bộ lọc, theo thứ tự hiển thị mong muốn. */
export const ROUTE_STATUS_OPTIONS = (Object.keys(ROUTE_STATUS_META) as RouteStatus[]).map(
  (value) => ({ value, label: ROUTE_STATUS_META[value].label }),
);

/**
 * GET /routes — danh sách + tìm kiếm + lọc trạng thái + phân trang.
 *
 * Chỉ gửi tham số lọc khi thực sự có giá trị. Lý do: backend coi `status` không khớp
 * `Active`/`Inactive` là "lọc ra rỗng" chứ KHÔNG báo lỗi (docs/api-contract.md). Nên chỉ
 * cần lỡ gửi `status=` rỗng là bảng âm thầm trắng, rất khó lần ra.
 */
export function fetchRoutes({
  search,
  status,
  page,
  pageSize,
}: RouteListParams): Promise<RouteListResult> {
  const params: Record<string, string | number> = { page, pageSize };

  if (search) params.search = search;
  if (status) params.status = status;

  return axiosClient.get<RouteListResult, RouteListResult>('/routes', { params });
}
