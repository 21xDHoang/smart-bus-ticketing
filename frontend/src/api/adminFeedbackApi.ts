import axiosClient from './axiosClient';
import type { FeedbackReply, FeedbackStatus, FeedbackType } from './feedbackApi';

// -----------------------------------------------------------------------------
// API quản trị phản ánh — phục vụ màn hình "Admin xử lý phản ánh + phản hồi"
// (story 24, task Sprint 2 dòng 58, Hoàng Văn Thịnh).
//
// Nhóm `/admin/feedbacks` trong docs/api-contract.md yêu cầu vai trò **Manager hoặc Admin**;
// không đủ quyền nhận 403. Khác hẳn feedbackApi.ts — file đó là hành khách tự xem phản ánh
// của chính mình qua `/feedbacks/me`; ở đây nhà xe thấy phản ánh của MỌI hành khách.
//
// Bốn endpoint chính:
//   GET   /admin/feedbacks               → { items, total, page, pageSize } — mới nhất trước
//   GET   /admin/feedbacks/{id}          → Feedback đầy đủ, kèm `replies` sắp cũ → mới
//   POST  /admin/feedbacks/{id}/replies  → ghi thêm MỘT phản hồi, trả Feedback đầy đủ
//   PATCH /admin/feedbacks/{id}          → đổi trạng thái, trả Feedback đầy đủ
//
// Cộng hai endpoint phụ trợ:
//   GET   /admin/feedbacks/statistics    → số liệu theo loại/tuyến (không tham số, không 400/404)
//   GET   /trips/{id}                    → tra mã/tên tuyến + giờ chạy của chuyến bị phản ánh
//
// Hai luật nền của nhóm này (mục "Phản ánh" của hợp đồng):
//   - `tripId` được phép `null` (A9 #20): phản ánh về tuyến/giá vé/ứng dụng không gắn chuyến.
//   - Phản hồi là bảng CHỈ GHI THÊM: một phản ánh nhận nhiều phản hồi, không sửa được cái cũ —
//     sửa tại chỗ thì mất lịch sử đối thoại.
//
// Cố ý KHÔNG có nhánh dữ liệu giả và không có cờ USE_MOCK_DATA: cả bốn endpoint chính lẫn
// endpoint thống kê đều đã có thật (docs/bao-cao-kiem-thu-cheo-phan-anh.md xác nhận API sẵn
// sàng, chỉ thiếu giao diện). Một đường im lặng chạy trên số liệu bịa ở màn hình quản trị là
// nguy hiểm nhất trong các màn hình: người xử lý sẽ tưởng nhà xe đã trả lời xong những phản
// ánh chưa ai đụng tới.
// -----------------------------------------------------------------------------

/**
 * Một dòng trong `GET /admin/feedbacks` — entity `Feedback` KHÔNG kèm `replies`, thay bằng
 * `replyCount`. Hợp đồng cố ý không mang cả luồng phản hồi theo từng dòng: một trang 10 dòng
 * có thể kéo theo hàng trăm phản hồi mà bảng không hiển thị.
 *
 * Chú ý phần không có: khác `/feedbacks/me`, dòng ở đây KHÔNG có `routeCode`/`routeName`/
 * `departureTime` — chỉ có `tripId` (GUID). Muốn hiện tên tuyến thì phải tra thêm
 * `GET /trips/{id}` (xem `getTripInfo`).
 */
export interface AdminFeedbackListItem {
  id: string;

  /** Hành khách gửi phản ánh — `userFullName` chỉ để hiển thị, null khi không tìm thấy tài khoản. */
  userId: string;
  userFullName: string | null;

  /** Chuyến bị phản ánh. **null là hợp lệ** — phản ánh về tuyến/giá vé/ứng dụng. */
  tripId: string | null;

  type: FeedbackType;
  content: string;

  /** Ảnh đính kèm do hành khách gửi. null khi không đính kèm. */
  attachmentUrl: string | null;

  /** Mức độ hài lòng 1..5. null khi khách không chấm sao. */
  rating: number | null;

  status: FeedbackStatus;

  createdAt: string;

  /** Chỉ đổi khi `status` đổi; thêm phản hồi KHÔNG chạm dòng này. null khi chưa từng đổi. */
  updatedAt: string | null;

  /** Số phản hồi của nhà xe — nội dung nằm ở chi tiết, danh sách chỉ có con đếm. */
  replyCount: number;
}

/** Chi tiết một phản ánh — `GET /admin/feedbacks/{id}`: mang `replies` THAY CHO `replyCount`. */
export type AdminFeedbackDetail = Omit<AdminFeedbackListItem, 'replyCount'> & {
  /** Toàn bộ luồng phản hồi, sắp **cũ → mới**. Rỗng khi nhà xe chưa trả lời lần nào. */
  replies: FeedbackReply[];
};

export interface AdminFeedbackListParams {
  /** Chỉ lấy một trạng thái. Bỏ trống = mọi trạng thái. */
  status?: FeedbackStatus;

  /** Chỉ lấy một loại. Bỏ trống = mọi loại. */
  type?: FeedbackType;

  /** Trang, tính từ 1. Mặc định backend là 1. */
  page?: number;

  /** Số dòng mỗi trang, 1..100. Mặc định backend là 10. */
  pageSize?: number;
}

export interface AdminFeedbackListResult {
  items: AdminFeedbackListItem[];
  total: number;
  page: number;
  pageSize: number;
}

/** Một dòng của `byType` trong số liệu thống kê. */
export interface FeedbackTypeCount {
  type: FeedbackType;
  count: number;
}

/** Một dòng của `byRoute` trong số liệu thống kê. */
export interface FeedbackRouteCount {
  routeId: string;
  routeCode: string;
  routeName: string;
  count: number;
}

/**
 * `GET /admin/feedbacks/statistics` — số liệu của TOÀN BỘ phản ánh trong hệ thống, không có
 * tham số lọc nào (hợp đồng cố ý chưa có lọc theo thời gian/trạng thái/phân trang).
 */
export interface FeedbackStatistics {
  total: number;

  /** LUÔN đủ ba loại, kể cả loại chưa có phản ánh nào (`count: 0`), thứ tự cố định
   *  Complaint → Compliment → Suggestion — biểu đồ theo loại có trục cố định ba giá trị. */
  byType: FeedbackTypeCount[];

  /** Chỉ những tuyến ĐÃ có phản ánh, sắp theo `count` giảm dần rồi `routeCode` tăng dần.
   *  Rỗng khi chưa phản ánh nào gắn với chuyến. */
  byRoute: FeedbackRouteCount[];

  /** Số phản ánh KHÔNG gắn chuyến (`tripId` null) nên không quy được về tuyến nào. */
  withoutTrip: number;
}

/** Phần cần dùng của `TripDetail` (`GET /trips/{id}`) — chỉ để hiển thị chuyến bị phản ánh. */
export interface FeedbackTripInfo {
  id: string;
  routeCode: string;
  routeName: string;
  origin: string;
  destination: string;
  departureTime: string;
}

/**
 * Phần cần dùng của response `PATCH /admin/feedbacks/{id}`.
 *
 * Hợp đồng hứa response là `Feedback` đầy đủ, nhưng bảng trường của entity ghi rõ `replies`
 * **chỉ có ở chi tiết** và ở response của `POST .../replies` — nên ở đây chỉ khai báo đúng hai
 * trường mà màn hình thực sự đọc sau khi đổi trạng thái, rồi ghép vào chi tiết đang mở. Khai
 * báo cả cục rồi ghi đè sẽ có ngày xoá mất luồng phản hồi đang hiển thị.
 */
export interface FeedbackStatusChange {
  id: string;
  status: FeedbackStatus;
  updatedAt: string | null;
}

export interface AdminFeedbackApi {
  /** Danh sách phản ánh khớp bộ lọc, mới nhất trước. Không khớp dòng nào → 200 với `items: []`. */
  list: (params: AdminFeedbackListParams) => Promise<AdminFeedbackListResult>;

  /** Chi tiết một phản ánh kèm toàn bộ luồng phản hồi. Không có → 404 "Không tìm thấy phản ánh". */
  getDetail: (id: string) => Promise<AdminFeedbackDetail>;

  /** Ghi thêm một phản hồi. Trả Feedback ĐẦY ĐỦ (đã có dòng mới) để chi tiết cập nhật ngay. */
  addReply: (id: string, content: string) => Promise<AdminFeedbackDetail>;

  /** Đổi trạng thái xử lý. Không có máy trạng thái — mọi chiều chuyển đều hợp lệ. */
  changeStatus: (id: string, status: FeedbackStatus) => Promise<FeedbackStatusChange>;

  /** Số liệu theo loại/tuyến của toàn hệ thống. */
  getStatistics: () => Promise<FeedbackStatistics>;

  /** Tra chuyến bị phản ánh để hiện mã/tên tuyến và giờ chạy thay vì một GUID trần. */
  getTripInfo: (tripId: string) => Promise<FeedbackTripInfo>;
}

const adminFeedbackApi: AdminFeedbackApi = {
  // Tham số bỏ trống thì không gửi: `status`/`type` gõ sai KHÔNG gây 400 (hợp đồng trả danh
  // sách rỗng), chỉ `page` < 1 hoặc `pageSize` ngoài 1..100 mới là 400 errors.page/pageSize —
  // màn hình đã khoá hai giá trị này bằng phân trang của bảng và PAGE_SIZE_OPTIONS.
  list: (params) =>
    axiosClient.get<AdminFeedbackListResult, AdminFeedbackListResult>('/admin/feedbacks', {
      params,
    }),

  // GUID sai định dạng không khớp route nên cũng ra 404, không phải 500 — màn hình chỉ cần
  // hiện đúng câu của backend.
  getDetail: (id) =>
    axiosClient.get<AdminFeedbackDetail, AdminFeedbackDetail>(`/admin/feedbacks/${id}`),

  // Endpoint KHÔNG tự đổi trạng thái: trả lời xong mà còn chờ khách phản hồi lại là ca có
  // thật, nên muốn chuyển trạng thái thì phải gọi `changeStatus` riêng.
  addReply: (id, content) =>
    axiosClient.post<AdminFeedbackDetail, AdminFeedbackDetail>(`/admin/feedbacks/${id}/replies`, {
      content,
    }),

  // Không có máy trạng thái: mở lại `Resolved` → `InProgress` khi khách phản hồi thêm vẫn được.
  changeStatus: (id, status) =>
    axiosClient.patch<FeedbackStatusChange, FeedbackStatusChange>(`/admin/feedbacks/${id}`, {
      status,
    }),

  // Không tham số nên không có nhánh 400/404: đúng quyền là luôn 200, kể cả khi hệ thống chưa
  // có phản ánh nào (mọi con đếm bằng 0, `byType` vẫn đủ ba dòng).
  getStatistics: () =>
    axiosClient.get<FeedbackStatistics, FeedbackStatistics>('/admin/feedbacks/statistics'),

  // Bước "có thì tốt": hợp đồng của nhóm /admin/feedbacks chỉ trả `tripId`, không kèm mã/tên
  // tuyến như /feedbacks/me, nên muốn hiện chuyến cho người xử lý thì phải tra thêm. Hỏng
  // bước này (chuyến đã xoá → 404, hoặc mất mạng) không được làm hỏng màn hình chi tiết —
  // chỗ gọi tự lùi về chính mã chuyến.
  getTripInfo: (tripId) =>
    axiosClient.get<FeedbackTripInfo, FeedbackTripInfo>(`/trips/${tripId}`),
};

export default adminFeedbackApi;
