import axiosClient from './axiosClient';

// -----------------------------------------------------------------------------
// API "Web Push (PWA)" — thiết bị trình duyệt nhận thông báo khi tab đã đóng (US 10
// "Thông báo trạm", Sprint 4 dòng 42 — Trần Trung Hiếu).
//
//   GET    /web-push/vapid-public-key   → { publicKey }
//   GET    /web-push/devices            → WebPushDevice[]   (mảng trần, mới nhất trước)
//   POST   /web-push/devices            → WebPushDevice     (201 mới / 200 đăng ký lại)
//   POST   /web-push/devices/{id}/test  → 204               (gửi tin thử tới đúng thiết bị)
//   DELETE /web-push/devices/{id}       → 204               (gỡ thiết bị = XOÁ dòng)
//
// Hình dạng khớp mục "Web Push — /web-push" của docs/api-contract.md và controller
// WebPushController của backend. Endpoint dài ~150 ký tự là khoá nghiệp vụ, không phải
// chuỗi để đọc — màn cài đặt chỉ hiện phần host.
// -----------------------------------------------------------------------------

/** Một thiết bị đã đăng ký nhận Web Push — khớp `WebPushDeviceResponse` bên backend. */
export interface WebPushDevice {
  /** Khoá của lượt đăng ký — dùng cho `DELETE /web-push/devices/{id}` và gửi thử. */
  id: string;

  /** Push endpoint do trình duyệt cấp (FCM/Mozilla/…). */
  endpoint: string;

  /** Ngày hết hạn trình duyệt báo; `null` = không hẹn giờ. */
  expirationTime: string | null;

  /** User-Agent lúc đăng ký — màn cài đặt dùng để phân biệt thiết bị. */
  userAgent: string;

  /** Thời điểm đăng ký/đăng ký lại gần nhất (ISO 8601, UTC). */
  createdAt: string;
}

/** Body của `POST /web-push/devices` — nguyên hình `PushSubscription.toJSON()` của trình duyệt. */
export interface RegisterWebPushDeviceRequest {
  endpoint: string;
  p256Dh: string;
  auth: string;
  expirationTime: string | null;
}

interface VapidPublicKeyResponse {
  publicKey: string;
}

export interface WebPushApi {
  /** Khoá công khai VAPID — truyền làm `applicationServerKey` cho `PushManager.subscribe()`. */
  getVapidPublicKey: () => Promise<string>;

  /** Thiết bị đã đăng ký của chính người gọi. Chưa có → mảng rỗng. */
  listDevices: () => Promise<WebPushDevice[]>;

  /** Đăng ký hoặc đăng ký lại thiết bị (trùng endpoint → backend ghi đè, trả 200). */
  registerDevice: (request: RegisterWebPushDeviceRequest) => Promise<WebPushDevice>;

  /** Gửi tin thử tới thiết bị; 204 nghĩa là đã chuyển cho push service, không hứa đã hiện. */
  sendTest: (id: string) => Promise<void>;

  /** Gỡ thiết bị — 404 nếu không tồn tại hoặc của người khác. */
  deleteDevice: (id: string) => Promise<void>;
}

const webPushApi: WebPushApi = {
  getVapidPublicKey: async () =>
    (
      await axiosClient.get<VapidPublicKeyResponse, VapidPublicKeyResponse>(
        '/web-push/vapid-public-key',
      )
    ).publicKey,

  listDevices: () => axiosClient.get<WebPushDevice[], WebPushDevice[]>('/web-push/devices'),

  registerDevice: (request) =>
    axiosClient.post<WebPushDevice, WebPushDevice>('/web-push/devices', request),

  sendTest: (id) => axiosClient.post<never, void>(`/web-push/devices/${id}/test`),

  deleteDevice: (id) => axiosClient.delete<never, void>(`/web-push/devices/${id}`),
};

export default webPushApi;
