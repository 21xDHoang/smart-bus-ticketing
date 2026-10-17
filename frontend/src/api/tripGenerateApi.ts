import axiosClient from './axiosClient';
import type { Trip } from './tripApi';

// -----------------------------------------------------------------------------
// API sinh chuyến hàng loạt theo tần suất cho CẢ ĐỘI XE — nối với
// FleetTripsController (nợ của đợt mở rộng đội xe 17/10/2026, Phùng Duy Hoàng).
//
// Hợp đồng endpoint — xem docs/api-contract.md mục "Lịch trình chạy xe —
// /routes/{routeId}/trips", tiểu mục "POST /routes/{routeId}/trips/generate-fleet":
//   POST /routes/{routeId}/trips/generate-fleet
//     → GenerateFleetTripsResponse → { items: Trip[], total, busLoads: FleetBusLoad[] }
//
// Vì sao không dùng endpoint cũ `.../trips/generate`: endpoint đó gắn CẢ dải chuyến
// vào MỘT xe, nên tuyến chạy 15 phút/chuyến sẽ dồn hàng chục chuyến lên một xe trong
// khi xe khác nằm không. Endpoint mới nhận danh sách xe và chia chuyến vòng tròn:
// mỗi chuyến đi tới xe đầu tiên (theo thứ tự ưu tiên) đang rảnh trong khung giờ của
// chuyến. Endpoint cũ vẫn còn nhưng đã đánh dấu ngừng dùng trong hợp đồng.
//
// Task "UI cấu hình tần suất chạy xe theo khung giờ trong ngày" (Sprint 2) —
// Hoàng Văn Thịnh. Tách module riêng, KHÔNG sửa tripScheduleApi.ts (file của Hạnh —
// phần thêm/sửa/huỷ chuyến lẻ) hay tripApi.ts (phần đọc) — quy ước E1.
//
// Phần đọc danh sách tuyến/xe cho các ô chọn không viết lại ở đây: dùng fetchRoutes
// (lọc Active) của routeApi.ts và fetchActiveBusOptions của busApi.ts.
// -----------------------------------------------------------------------------

/**
 * Body của POST /routes/{routeId}/trips/generate-fleet — khớp `GenerateFleetTripsRequest`
 * của backend, xem docs/api-contract.md.
 */
export interface GenerateFleetTripsPayload {
  /**
   * Danh sách xe tham gia, từ 1 đến 100 GUID có thật và đang `Active`.
   * THỨ TỰ LÀ THỨ TỰ ƯU TIÊN: chuyến đầu tiên đi tới xe đầu danh sách còn rảnh, chuyến
   * sau đi tiếp vòng tròn. Xe không có chuyến nào vẫn được trả về trong `busLoads`.
   */
  busIds: string[];
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

/** Số chuyến mỗi xe nhận trong lượt sinh — đủ để hiện bảng phân công theo xe. */
export interface FleetBusLoad {
  busId: string;
  busLicensePlate: string;
  tripCount: number;
}

/**
 * Kết quả của POST .../trips/generate-fleet — danh sách chuyến VỪA SINH (không gồm chuyến cũ
 * của tuyến), tổng số chuyến, và số chuyến từng xe nhận. `busLoads` giữ ĐÚNG thứ tự xe gửi
 * lên và gồm cả xe 0 chuyến, nên màn hình hiện được "xe nào chưa được chia chuyến".
 */
export interface GenerateFleetTripsResult {
  items: Trip[];
  total: number;
  busLoads: FleetBusLoad[];
}

/**
 * POST /api/routes/{routeId}/trips/generate-fleet — sinh chuyến hàng loạt và chia cho đội xe.
 *
 * Thao tác là NGUYÊN TỬ: một chuyến trùng ĐÚNG giờ khởi hành với chuyến đã có trên tuyến,
 * hoặc không còn xe rảnh cho một chuyến trong dải, thì không chuyến nào được tạo — server
 * trả 400/409 kèm thông báo (lỗi chọn xe nằm ở `errors.busIds`), màn hình hiện thẳng lên ô.
 */
export function generateFleetTrips(
  routeId: string,
  payload: GenerateFleetTripsPayload,
): Promise<GenerateFleetTripsResult> {
  return axiosClient.post<GenerateFleetTripsResult, GenerateFleetTripsResult>(
    `/routes/${routeId}/trips/generate-fleet`,
    payload,
  );
}
