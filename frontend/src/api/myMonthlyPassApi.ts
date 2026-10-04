import axiosClient from './axiosClient';
import type { MonthlyPass, PassTypeCode } from './monthlyPassApi';

// -----------------------------------------------------------------------------
// API "Vé tháng của tôi" — hai endpoint phục vụ màn hình quản lý vé tháng của hành khách
// (story 16, task Sprint 2 dòng 45 của Hoàng Văn Thịnh):
//
//   GET  /monthly-passes/me          → MonthlyPass[]  (mảng trần, vé ĐANG có hiệu lực)
//   POST /monthly-passes/{id}/renew  → MonthlyPass    (dòng MỚI của kỳ kế tiếp)
//
// Khác monthlyPassApi.ts (màn hình ĐĂNG KÝ của Dương Thị Hạnh, còn chạy dữ liệu giả), hai
// endpoint ở đây backend đã có thật và đã đo được: GET .../me → 200, POST .../renew → 200
// cho vai trò hành khách (docs/bao-cao-kiem-thu-cheo-ve-thang.md). Vì vậy file này
// KHÔNG có nhánh dữ liệu giả và không có cờ USE_MOCK_DATA: dựng thêm một nhánh giả cho
// endpoint đã chạy được chỉ tạo thêm một đường im lặng để màn hình chạy trên số liệu bịa.
//
// Kiểu MonthlyPass / PassTypeCode dùng lại từ monthlyPassApi.ts để hai component của
// Nguyễn Đình Băng (MonthlyPassStatusTag, MonthlyPassExpiryReminder) nhận thẳng props mà
// không phải đổi gì — chúng nhận `MonthlyPass` của chính file kia.
// -----------------------------------------------------------------------------

export interface MyMonthlyPassApi {
  /** Vé tháng đang có hiệu lực của chính người gọi. Không vé nào → mảng rỗng, không phải 404. */
  list: () => Promise<MonthlyPass[]>;

  /**
   * Gia hạn một vé của chính người gọi. Server ghi thêm MỘT dòng mới và trả về dòng mới đó
   * (id mới), vé cũ giữ nguyên làm lịch sử.
   *
   * `passTypeCode` bỏ trống = giữ nguyên loại vé của vé đang gia hạn (hợp đồng: "Bỏ trống /
   * chuỗi rỗng = giữ nguyên loại vé của vé đang gia hạn") — nút "Gia hạn" của màn hình đi
   * theo lối này.
   */
  renew: (id: string, passTypeCode?: PassTypeCode) => Promise<MonthlyPass>;
}

const myMonthlyPassApi: MyMonthlyPassApi = {
  list: () => axiosClient.get<MonthlyPass[], MonthlyPass[]>('/monthly-passes/me'),

  renew: (id, passTypeCode) =>
    axiosClient.post<MonthlyPass, MonthlyPass>(
      `/monthly-passes/${id}/renew`,
      // Gửi `{}` chứ không bỏ hẳn body: hợp đồng nhận cả hai, nhưng gửi body rỗng tường minh
      // thì không phụ thuộc vào việc axios có đặt Content-Type hay không khi không có body.
      passTypeCode ? { passTypeCode } : {},
    ),
};

export default myMonthlyPassApi;
