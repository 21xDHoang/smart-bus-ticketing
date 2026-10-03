import axiosClient from './axiosClient';
import type { Trip } from './tripApi';

// -----------------------------------------------------------------------------
// API sinh chuyến hàng loạt theo tần suất — nối với RouteTripsController (story 13).
//
// Hợp đồng endpoint — xem docs/api-contract.md mục "Lịch trình chạy xe —
// /routes/{routeId}/trips", tiểu mục "POST /routes/{routeId}/trips/generate":
//   POST /routes/{routeId}/trips/generate → GenerateTrips → { items: Trip[], total }
//
// Đây là API "lập lịch trình" theo quy ước A8.3: ngày áp dụng là phần ngày của
// startTime, giờ khởi hành là phần giờ của startTime rồi cộng dần frequencyMinutes,
// chuyến cuối không vượt quá endTime.
//
// Task "UI cấu hình tần suất chạy xe theo khung giờ trong ngày" (Sprint 2) —
// Hoàng Văn Thịnh. Tách module riêng, KHÔNG sửa tripScheduleApi.ts (file của Hạnh —
// phần thêm/sửa/huỷ chuyến lẻ) hay tripApi.ts (phần đọc) — quy ước E1.
//
// Phần đọc danh sách tuyến/xe cho hai ô chọn không viết lại ở đây: dùng
// fetchRouteOptions của tripApi.ts và fetchActiveBusOptions của busApi.ts.
// -----------------------------------------------------------------------------

/**
 * Body của POST /routes/{routeId}/trips/generate — khớp `GenerateTripsRequest` của backend,
 * xem docs/api-contract.md.
 */
export interface GenerateTripsPayload {
  /** Xe chạy lịch trình — GUID xe có thật và đang `Active`. */
  busId: string;
  /**
   * Mốc bắt đầu — ngày áp dụng + giờ khởi hành của chuyến đầu tiên.
   * ISO 8601 CÓ KÈM múi giờ (ví dụ `2026-10-01T05:00:00+07:00`) — server quy về UTC khi lưu.
   */
  startTime: string;
  /** Mốc kết thúc — chuyến cuối được sinh không vượt quá mốc này. Phải sau `startTime`. */
  endTime: string;
  /** Tần suất chạy xe (phút) — khoảng cách giữa hai chuyến liên tiếp, từ 1 đến 1440. */
  frequencyMinutes: number;
}

/**
 * Kết quả của POST .../trips/generate — danh sách chuyến VỪA SINH (không gồm chuyến cũ
 * của tuyến). `total` = số chuyến vừa sinh, dùng để hiện câu "đã sinh N chuyến" mà không
 * phải tự đếm mảng.
 */
export interface GenerateTripsResult {
  items: Trip[];
  total: number;
}

/**
 * POST /api/routes/{routeId}/trips/generate — sinh chuyến hàng loạt theo tần suất.
 *
 * Thao tác là NGUYÊN TỬ: một chuyến trong dải bị trùng khung giờ với chuyến hiện có thì
 * không chuyến nào được tạo — server trả 409 kèm thông báo, màn hình chỉ cần hiện toast.
 */
export function generateTrips(
  routeId: string,
  payload: GenerateTripsPayload,
): Promise<GenerateTripsResult> {
  return axiosClient.post<GenerateTripsResult, GenerateTripsResult>(
    `/routes/${routeId}/trips/generate`,
    payload,
  );
}
