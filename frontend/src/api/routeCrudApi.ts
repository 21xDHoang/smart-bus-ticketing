import axiosClient from './axiosClient';
import type { Route, RouteStatus } from './routeApi';

// -----------------------------------------------------------------------------
// API tạo / sửa / xoá tuyến đường — nối với RoutesController (story 12).
//
// Hợp đồng endpoint — xem docs/api-contract.md mục "Tuyến đường — /routes":
//   POST   /routes        → CreateRoute  → Route (201)
//   PUT    /routes/{id}   → UpdateRoute  → Route
//   DELETE /routes/{id}   → xoá mềm      → Route (200)
//
// File riêng của Hạnh: `routeApi.ts` (của Thịnh) giữ GET /routes cùng các kiểu Route/
// RouteStatus — phần thêm/sửa/xoá tách riêng ở đây, không sửa file của Thịnh.
// -----------------------------------------------------------------------------

/** Body khi thêm tuyến — POST /routes (không có status, backend mặc định Active). */
export interface RoutePayload {
  code: string;
  name: string;
  origin: string;
  destination: string;
  /** Tổng chiều dài (km), từ 0 đến 9999.99. */
  distanceKm: number;
}

/** Body khi sửa tuyến — PUT /routes/{id}: thêm status để bật/tắt khai thác. */
export interface UpdateRoutePayload extends RoutePayload {
  status: RouteStatus;
}

export interface RouteCrudApi {
  create: (payload: RoutePayload) => Promise<Route>;
  update: (id: string, payload: UpdateRoutePayload) => Promise<Route>;
  /** Xoá mềm — chuyển tuyến sang Inactive, trả về tuyến đã ngừng khai thác. */
  remove: (id: string) => Promise<Route>;
}

// Backend RoutesController ĐÃ có nên gọi thẳng API, không cần nhánh dữ liệu giả.
const routeCrudApi: RouteCrudApi = {
  // POST /api/routes
  create: (payload) => axiosClient.post<Route, Route>('/routes', payload),

  // PUT /api/routes/{id}
  update: (id, payload) => axiosClient.put<Route, Route>(`/routes/${id}`, payload),

  // DELETE /api/routes/{id} — xoá mềm, trả 200 kèm Route (khác 204 như /stops).
  remove: (id) => axiosClient.delete<Route, Route>(`/routes/${id}`),
};

export default routeCrudApi;
