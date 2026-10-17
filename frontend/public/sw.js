/* -----------------------------------------------------------------------------
 * Service worker Web Push — US 10 "Thông báo trạm", Sprint 4 dòng 42 (Trần Trung Hiếu).
 *
 * Chỉ làm hai việc, cố ý KHÔNG cache gì (không có handler `fetch`):
 *   1. nhận tin đẩy từ backend khi tab đã đóng → hiện thông báo hệ điều hành;
 *   2. bấm thông báo → mở/focus tab đúng đường dẫn trong `data.url`.
 *
 * Payload do backend gửi là JSON camelCase (JsonSerializerDefaults.Web):
 *   { "title": "...", "body": "...", "url": "/notifications", "tag": "..." }
 * Thiếu `url` thì mở trang chủ; thiếu `tag` thì mỗi tin là một thông báo riêng.
 *
 * File này nằm trong `public/` nên được phục vụ nguyên xi tại `/sw.js` — không qua
 * Vite/TypeScript. Service worker bắt buộc chạy trên HTTPS hoặc localhost.
 * --------------------------------------------------------------------------- */

self.addEventListener('install', () => {
  // Bản mới thay bản cũ ngay, không chờ đóng hết tab — đúng nhịp phát triển của dự án.
  self.skipWaiting();
});

self.addEventListener('activate', (event) => {
  // Nhận quyền điều khiển các tab đang mở để tin push đầu tiên không bị rơi.
  event.waitUntil(self.clients.claim());
});

self.addEventListener('push', (event) => {
  let payload = {};

  try {
    payload = event.data ? event.data.json() : {};
  } catch {
    // Push service cho phép payload không phải JSON — vẫn phải hiện một thông báo,
    // vì trình duyệt có thể thu hồi subscription nếu im lặng nhiều lần.
    payload = { body: event.data ? event.data.text() : '' };
  }

  const title = payload.title || 'Smart Bus Ticket';
  const options = {
    body: payload.body || 'Bạn có thông báo mới từ Smart Bus Ticket.',
    icon: '/icons/icon-192.png',
    badge: '/icons/icon-192.png',
    // `tag` giúp tin cùng loại thay thế tin cũ thay vì xếp chồng; `renotify` báo lại
    // (rung/âm) khi thay thế. Không có tag thì không bật renotify.
    tag: payload.tag || undefined,
    renotify: Boolean(payload.tag),
    data: { url: payload.url || '/' },
  };

  event.waitUntil(self.registration.showNotification(title, options));
});

self.addEventListener('notificationclick', (event) => {
  event.notification.close();

  const url = new URL(event.notification.data?.url || '/', self.location.origin).href;

  event.waitUntil(
    (async () => {
      const windows = await self.clients.matchAll({ type: 'window', includeUncontrolled: true });

      // Ưu tiên tab cùng origin đang mở: focus rồi điều hướng, giữ nguyên phiên đăng nhập.
      const existing = windows.find((client) => client.url.startsWith(self.location.origin));
      if (existing) {
        await existing.focus();

        try {
          await existing.navigate(url);
        } catch {
          // Một số trình duyệt không cho navigate client đã mất quyền điều khiển — mở tab mới.
          await self.clients.openWindow(url);
        }

        return;
      }

      await self.clients.openWindow(url);
    })(),
  );
});

/* Việc còn lại (docs/54 §5): lắng nghe `pushsubscriptionchange` để tự đăng ký lại khi
 * trình duyệt xoay subscription. Hiện chưa làm vì cần gọi API kèm token người dùng —
 * service worker không có token; sẽ xử lý bằng một màn "đăng ký lại" khi mở app. */
