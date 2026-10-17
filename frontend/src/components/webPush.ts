import webPushApi from '../api/webPushApi';
import type { RegisterWebPushDeviceRequest, WebPushDevice } from '../api/webPushApi';
import {
  isNotificationSupported,
  requestNotificationPermission,
} from './notificationPermission';

// -----------------------------------------------------------------------------
// Đăng ký nhận Web Push phía trình duyệt — phần logic thuần cho màn Cài đặt thông báo
// (US 10 "Thông báo trạm", Sprint 4 dòng 42 — Trần Trung Hiếu).
//
// Ba mảnh phải khớp nhau để một tin push tới được máy khách:
//   1. service worker `/sw.js` đã đăng ký — nơi trình duyệt giao sự kiện `push`;
//   2. PushSubscription của trình duyệt (`endpoint` + hai khoá p256dh/auth) — backend
//      mã hoá RFC 8291 bằng đúng hai khoá này;
//   3. backend đã lưu subscription (POST /web-push/devices) — nếu chỉ có 1+2 mà quên 3
//      thì máy chủ không biết gửi cho ai.
//
// `PushManager.subscribe()` cần `applicationServerKey` là khoá công khai VAPID của máy
// chủ dạng BYTE (65 byte), trong khi backend trả base64url — hàm dưới tự giải mã.
//
// ⚠️ Quyền thông báo phải xin từ thao tác bấm của người dùng (xem notificationPermission.ts);
// hàm `enableWebPush` gọi `requestNotificationPermission()` trước nên màn cài đặt phải gọi nó
// trực tiếp trong onClick, không được gọi tự động lúc mount.
// -----------------------------------------------------------------------------

/** Địa chỉ service worker — file nằm trong `public/` nên phục vụ nguyên xi tại gốc. */
const SERVICE_WORKER_URL = '/sw.js';

/**
 * Trình duyệt có đủ API để nhận Web Push không. Gộp cả điều kiện ngữ cảnh an toàn:
 * service worker và Notification chỉ chạy trên HTTPS (localhost cũng tính).
 */
export function isWebPushSupported(): boolean {
  return (
    typeof window !== 'undefined' &&
    isNotificationSupported() &&
    'serviceWorker' in navigator &&
    'PushManager' in window
  );
}

/** base64url (không padding) → mảng byte — dạng trình duyệt chờ cho `applicationServerKey`. */
function base64UrlToBytes(value: string): Uint8Array<ArrayBuffer> {
  const padded = value + '='.repeat((4 - (value.length % 4)) % 4);
  const base64 = padded.replace(/-/g, '+').replace(/_/g, '/');
  const raw = atob(base64);
  const bytes = new Uint8Array(raw.length);

  for (let i = 0; i < raw.length; i += 1) {
    bytes[i] = raw.charCodeAt(i);
  }

  return bytes;
}

/** Khoá cũ trình duyệt đang giữ có trùng khoá máy chủ trả về không (đổi khoá VAPID phải subscribe lại). */
function sameKey(existing: ArrayBuffer | null, expected: Uint8Array): boolean {
  // Trình duyệt không cho đọc khoá cũ (null) — cứ dùng lại subscription, việc đổi khoá
  // sẽ lộ ra thành lỗi 401/403 của push service và người dùng đăng ký lại.
  if (!existing) return true;

  const current = new Uint8Array(existing);
  return current.length === expected.length && current.every((byte, index) => byte === expected[index]);
}

/**
 * Đăng ký (hoặc lấy lại) service worker. Không xin quyền thông báo — gọi được lúc mở màn
 * cài đặt, trước khi người dùng bấm nút.
 */
export async function ensurePushServiceWorker(): Promise<ServiceWorkerRegistration> {
  const registration = await navigator.serviceWorker.register(SERVICE_WORKER_URL);

  // `ready` chờ tới khi có service worker đang hoạt động — subscribe ngay sau `register`
  // trên lần tải đầu có thể gặp registration chưa active.
  await navigator.serviceWorker.ready;

  return registration;
}

/** Subscription hiện tại của trình duyệt (nếu đã từng đăng ký), không tự tạo mới. */
export async function getCurrentPushSubscription(): Promise<PushSubscription | null> {
  if (!isWebPushSupported()) return null;

  const registration = await navigator.serviceWorker.getRegistration();
  if (!registration) return null;

  return registration.pushManager.getSubscription();
}

/** Endpoint của subscription hiện tại — màn cài đặt dùng để biết "thiết bị này" là dòng nào. */
export async function getCurrentPushEndpoint(): Promise<string | null> {
  try {
    const subscription = await getCurrentPushSubscription();
    return subscription?.endpoint ?? null;
  } catch {
    return null;
  }
}

/** Đổi PushSubscription của trình duyệt sang body backend chờ. */
function toRegisterRequest(subscription: PushSubscription): RegisterWebPushDeviceRequest {
  const json = subscription.toJSON();
  const endpoint = json.endpoint ?? subscription.endpoint;
  const p256Dh = json.keys?.p256dh;
  const auth = json.keys?.auth;

  if (!endpoint || !p256Dh || !auth) {
    throw new Error('Trình duyệt không trả đủ khoá đăng ký Web Push. Hãy thử lại.');
  }

  return {
    endpoint,
    p256Dh,
    auth,
    expirationTime: subscription.expirationTime
      ? new Date(subscription.expirationTime).toISOString()
      : null,
  };
}

/**
 * Bật thông báo trên thiết bị đang dùng — PHẢI gọi từ thao tác bấm.
 *
 * Thứ tự cố ý: xin quyền trước (còn trong "cử chỉ" người dùng), rồi mới đăng ký service
 * worker, lấy khoá VAPID và subscribe. Nếu trình duyệt đã có subscription cũ thì dùng lại
 * (không tạo endpoint mới), nhưng chỉ khi khoá VAPID không đổi.
 */
export async function enableWebPush(): Promise<WebPushDevice> {
  if (!isWebPushSupported()) {
    throw new Error(
      'Trình duyệt này chưa hỗ trợ Web Push. Cần Chrome, Edge, Firefox hoặc Safari bản mới và trang chạy HTTPS.',
    );
  }

  const permission = await requestNotificationPermission();
  if (permission !== 'granted') {
    throw new Error(
      permission === 'denied'
        ? 'Trình duyệt đang chặn thông báo của trang này. Hãy mở lại quyền trong cài đặt trang web rồi thử lại.'
        : 'Bạn chưa cho phép thông báo. Hãy bấm "Cho phép" khi trình duyệt hỏi.',
    );
  }

  const registration = await ensurePushServiceWorker();
  const vapidKey = base64UrlToBytes(await webPushApi.getVapidPublicKey());

  let subscription = await registration.pushManager.getSubscription();

  // Khoá VAPID đổi (máy chủ sinh lại khoá) thì subscription cũ không dùng được nữa.
  if (subscription && !sameKey(subscription.options.applicationServerKey, vapidKey)) {
    await subscription.unsubscribe();
    subscription = null;
  }

  if (!subscription) {
    try {
      subscription = await registration.pushManager.subscribe({
        userVisibleOnly: true,
        applicationServerKey: vapidKey,
      });
    } catch (error) {
      // iOS Safari chỉ cho đăng ký khi app đã được thêm vào Màn hình chính.
      if (error instanceof DOMException && error.name === 'NotAllowedError') {
        throw new Error(
          'Trình duyệt từ chối đăng ký thông báo. Trên iPhone/iPad, hãy thêm ứng dụng vào Màn hình chính rồi thử lại.',
        );
      }

      throw error;
    }
  }

  return webPushApi.registerDevice(toRegisterRequest(subscription));
}

/** Huỷ subscription của trình duyệt này (không đụng dòng nào của thiết bị khác). */
export async function unsubscribeCurrentPushSubscription(): Promise<void> {
  try {
    const subscription = await getCurrentPushSubscription();
    await subscription?.unsubscribe();
  } catch {
    // Trình duyệt đã gỡ subscription từ trước — coi như đã huỷ xong.
  }
}

/**
 * Tên thân thiện cho một User-Agent — bảng thiết bị không thể hiện chuỗi UA thô 120 ký tự.
 * Chỉ là phép đoán để phân biệt các dòng; sai sót ở đây không ảnh hưởng chức năng.
 */
export function describeBrowser(userAgent: string): string {
  const ua = userAgent || '';

  const browser = /Edg\//.test(ua)
    ? 'Edge'
    : /OPR\//.test(ua)
      ? 'Opera'
      : /Firefox\/|FxiOS/.test(ua)
        ? 'Firefox'
        : /CriOS/.test(ua)
          ? 'Chrome'
          : /Chrome\//.test(ua)
            ? 'Chrome'
            : /Safari\//.test(ua)
              ? 'Safari'
              : 'Trình duyệt';

  const os = /Android/.test(ua)
    ? 'Android'
    : /iPhone|iPad|iPod/.test(ua)
      ? 'iOS'
      : /Windows/.test(ua)
        ? 'Windows'
        : /Mac OS X/.test(ua)
          ? 'macOS'
          : /Linux/.test(ua)
            ? 'Linux'
            : '';

  return os ? `${browser} trên ${os}` : browser;
}
