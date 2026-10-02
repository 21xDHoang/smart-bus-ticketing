import axiosClient from './axiosClient';
import { BUS_STATUS_META } from './busApi';
import type { Bus, BusStatus } from './busApi';

// -----------------------------------------------------------------------------
// API tạo / sửa / xoá xe buýt — nối với BusesController (story 14).
//
// Hợp đồng endpoint — xem docs/api-contract.md mục "Xe buýt — /buses":
//   POST   /buses        → CreateBus  → Bus (201)
//   PUT    /buses/{id}   → UpdateBus  → Bus
//   DELETE /buses/{id}   → xoá mềm     → Bus (200)
//
// File riêng của Hạnh: `busApi.ts` (của Băng) giữ GET /buses cùng các kiểu Bus/
// BusStatus — phần thêm/sửa/xoá tách riêng ở đây, không sửa file của Băng.
// -----------------------------------------------------------------------------

/** Body khi thêm xe — POST /buses (không có status, backend mặc định Active). */
export interface BusPayload {
  licensePlate: string;
  busType: string;
  capacity: number;
}

/** Body khi sửa xe — PUT /buses/{id}: thêm status để chuyển trạng thái bảo dưỡng/khai thác. */
export interface UpdateBusPayload extends BusPayload {
  status: BusStatus;
}

export interface BusCrudApi {
  create: (payload: BusPayload) => Promise<Bus>;
  update: (id: string, payload: UpdateBusPayload) => Promise<Bus>;
  /** Xoá mềm — chuyển xe sang Inactive, trả về xe đã ngừng khai thác. */
  remove: (id: string) => Promise<Bus>;
}

/**
 * Danh sách trạng thái cho bộ lọc + form — sinh từ BUS_STATUS_META để nhãn không bị lệch
 * với cột Trạng thái. Thứ tự giữ nguyên thứ tự khai báo của meta (Active, Maintenance, Inactive).
 */
export const BUS_STATUS_OPTIONS = (Object.keys(BUS_STATUS_META) as BusStatus[]).map(
  (value) => ({ value, label: BUS_STATUS_META[value].label }),
);

// Backend BusesController ĐÃ có nên gọi thẳng API, không cần nhánh dữ liệu giả.
const busCrudApi: BusCrudApi = {
  // POST /api/buses
  create: (payload) => axiosClient.post<Bus, Bus>('/buses', payload),

  // PUT /api/buses/{id}
  update: (id, payload) => axiosClient.put<Bus, Bus>(`/buses/${id}`, payload),

  // DELETE /api/buses/{id} — xoá mềm, trả 200 kèm Bus (khác 204 như /stops).
  remove: (id) => axiosClient.delete<Bus, Bus>(`/buses/${id}`),
};

export default busCrudApi;
