import axiosClient from './axiosClient';

// -----------------------------------------------------------------------------
// API "Theo dõi thông báo theo tuyến / trạm" — ba endpoint phục vụ component bật/tắt
// nhận thông báo theo trạm (US 10 "Thông báo trạm", Sprint 5 dòng 20).
//
//   GET    /subscriptions       → Subscription[]  (mảng trần, mới nhất trước)
//   POST   /subscriptions       → Subscription    (201 + dòng vừa tạo)
//   DELETE /subscriptions/{id}  → 204             (huỷ theo dõi = XOÁ dòng)
//
// Hình dạng theo hợp đồng mục "Theo dõi thông báo — /subscriptions" của
// docs/api-contract.md, viết cùng nhánh backend `feature/43-dang-ky-huy-theo-doi-thong-bao`
// của Trần Trung Hiếu (task Sprint 5 dòng 17 — docs/44). Nhánh đó CHƯA merge vào main,
// nên ba endpoint này chưa chạy được trên môi trường chung; frontend vẫn code theo đúng
// hợp đồng đã chốt, không tự đổi hình dạng request/response.
//
// "Tắt" là DELETE, không phải gửi cờ IsActive — bảng Subscriptions không có cột đó
// (docs/37 §3.2). Bật lại sau khi tắt là POST tạo dòng mới.
// -----------------------------------------------------------------------------

/** Một lượt theo dõi của chính người gọi — khớp `Subscription` bên backend. */
export interface Subscription {
  /** Khoá của lượt theo dõi — dùng cho `DELETE /subscriptions/{id}`. */
  id: string;

  /** Tuyến được theo dõi — luôn có. */
  routeId: string;

  /** Mã/tên tuyến là bản chép để hiển thị, không phải khoá nghiệp vụ. */
  routeCode: string;
  routeName: string;

  /** Trạm được theo dõi; `null` = theo dõi CẢ TUYẾN (mọi trạm + sự cố của tuyến). */
  stopId: string | null;

  /** Tên trạm; `null` cùng lúc với `stopId`. */
  stopName: string | null;

  /** Thời điểm bấm theo dõi (ISO 8601, UTC) — danh sách sắp mới nhất trước. */
  createdAt: string;
}

/** Body của `POST /subscriptions` — `stopId` bỏ trống/null = theo dõi cả tuyến. */
export interface CreateSubscriptionRequest {
  routeId: string;
  stopId?: string | null;
}

export interface SubscriptionApi {
  /** Danh sách lượt theo dõi của chính người gọi. Chưa theo dõi gì → mảng rỗng. */
  listMine: () => Promise<Subscription[]>;

  /** Đăng ký theo dõi. Trùng đúng tuyến/trạm đã theo dõi → lỗi 409 (AppError.status). */
  subscribe: (request: CreateSubscriptionRequest) => Promise<Subscription>;

  /** Huỷ theo dõi — xoá dòng. Dòng không tồn tại/của người khác → lỗi 404. */
  unsubscribe: (id: string) => Promise<void>;
}

const subscriptionApi: SubscriptionApi = {
  listMine: () => axiosClient.get<Subscription[], Subscription[]>('/subscriptions'),

  subscribe: (request) =>
    axiosClient.post<Subscription, Subscription>('/subscriptions', request),

  unsubscribe: (id) => axiosClient.delete<never, void>(`/subscriptions/${id}`),
};

export default subscriptionApi;
