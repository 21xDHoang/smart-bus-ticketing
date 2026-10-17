# 54 — Tích hợp Web Push (PWA) để nhận thông báo trên trình duyệt

> Task: *"Tích hợp Web Push (PWA) để nhận thông báo trên trình duyệt"* — US 10 "Thông báo trạm",
> **Sprint 4 dòng 42**, 5 SP, chủ task **Trần Trung Hiếu** (Chủ API).
>
> Quy ước A8.5 ghi *"Thông báo: In-app qua SignalR + bảng Notifications. Không email/SMS/Web Push"*.
> Task dòng 42 yêu cầu ngược lại; nhóm chốt **làm theo task** — Web Push là **kênh phụ trợ**, không
> thay thế thông báo trong ứng dụng. Mọi tin vẫn ghi bảng `Notifications` và hiện qua SignalR;
> Web Push chỉ giúp khách thấy thông báo hệ điều hành khi tab đã đóng.

---

## 1. Tổng quan kiến trúc

```
Trình duyệt                         Backend (ASP.NET Core)                Push service
───────────                         ──────────────────────                ────────────
[1] GET /api/web-push/vapid-public-key ─────────► trả khoá công khai VAPID
[2] xin quyền Notification (bấm nút)
[3] đăng ký service worker /sw.js
[4] PushManager.subscribe(applicationServerKey)
[5] POST /api/web-push/devices ────────────────► lưu endpoint + p256dh + auth
                                                        │
nghiệp vụ gọi IWebPushSender.SendToUserAsync ───────────┤
                                                        ▼
                                                Lib.Net.Http.WebPush
                                                mã hoá RFC 8291 + ký VAPID
                                                        │
                                                        ▼  HTTPS POST (payload mã hoá)
                                                        ├──────────────────────► FCM / Mozilla / …
                                                        ▼
[6] service worker nhận sự kiện `push` ◄────────────── push service
    hiện notification hệ điều hành
[7] bấm thông báo → mở /focus tab đúng `url`
```

**Backend** (đã xong):

| File | Vai trò |
|---|---|
| `Services/WebPushOptions.cs` | Đọc/kiểm cấu hình `WebPush:*`; tách số mũ thô 32 byte cho thư viện |
| `Services/IWebPushSender.cs` + `WebPushSender.cs` | Cửa duy nhất để nghiệp vụ đẩy tin; **không bao giờ ném lỗi** |
| `Services/WebPushClient.cs` | Gói mỏng quanh `Lib.Net.Http.WebPush`; ánh xạ 404/410 → `Stale`, còn lại → `Failed` |
| `Services/IWebPushSubscriptionStore.cs` + `InMemoryWebPushSubscriptionStore.cs` | Lưu thiết bị (tạm trong bộ nhớ — §6) |
| `Services/WebPushDeviceService.cs` | Kiểm hình dạng endpoint/khoá, đăng ký/ghi đè, gỡ, gửi thử |
| `Controllers/WebPushController.cs` | Năm endpoint REST — hợp đồng ở `docs/api-contract.md` mục "Web Push — /web-push" |

**Frontend** (đã xong):

| File | Vai trò |
|---|---|
| `public/sw.js` | Service worker: nhận `push`, hiện notification, xử lý `notificationclick` (không cache) |
| `public/manifest.webmanifest` + `public/icons/*` | PWA: cài ra Màn hình chính, icon 192/512/maskable |
| `src/components/webPush.ts` | Logic trình duyệt: hỗ trợ, đăng ký SW, subscribe/unsubscribe, đổi User-Agent → tên |
| `src/api/webPushApi.ts` | Client năm endpoint |
| `src/pages/NotificationSettingsPage.tsx` | Màn `/notification-settings`: bật/tắt, danh sách thiết bị, gửi thử |
| `index.html`, `App.tsx`, `layout/UserMenu.tsx` | Manifest link + route + lối vào từ thực đơn tài khoản |

---

## 2. Sinh khoá VAPID và cấu hình

VAPID là cặp khoá **P-256** định danh máy chủ gửi push. Một cặp khoá dùng cho **toàn bộ ứng dụng**;
mọi thiết bị đăng ký cùng khoá công khai đó.

### 2.1. Sinh khoá (chọn một cách)

**Cách khuyến nghị — gói `web-push` của Node** (đã có npm trong dự án frontend):

```bash
npx web-push generate-vapid-keys
```

Kết quả:

```json
{
  "publicKey": "B…65-byte-uncompressed-point-base64url…",
  "privateKey": "MIGHAgEAMBMGByqGSM49AgEGCCqGSM49AwEHBG0wawIBAQQg…PKCS#8-DER-base64url…"
}
```

**Cách dự phòng — openssl + Python** (khi máy không có Node):

```bash
openssl ecparam -name prime256v1 -genkey -noout -out vapid-private.pem
openssl pkcs8 -topk8 -nocrypt -in vapid-private.pem -outform DER -out vapid-private.der
openssl ec -in vapid-private.pem -pubout -outform DER -out vapid-public.der
# rồi base64url (không padding) hai file DER; public key DER của ec -pubout
# cần lấy 65 byte cuối (điểm uncompressed 0x04‖x‖y) — xem ghi chú dưới.
```

> Định dạng cấu hình của dự án (chuẩn chung của hệ sinh thái Web Push):
> - `PublicKey`: điểm công khai **uncompressed 65 byte** (0x04 ‖ x ‖ y), base64url.
> - `PrivateKey`: khoá riêng **PKCS#8 DER**, base64url.
>
> `Lib.Net.Http.WebPush` 2.2.0 nhận khoá riêng dạng **số mũ thô 32 byte**, nên
> `WebPushOptions.KhoaRiengThoBase64Url()` tách `d` khỏi PKCS#8 ngay trước khi ký. Giữ nguyên
> PKCS#8 trong cấu hình để đổi thư viện/phiên bản không phải sinh lại khoá.

### 2.2. Điền cấu hình

Máy local — `backend/SmartBus.Api/appsettings.Development.json` (file **đã bị gitignore**):

```json
"WebPush": {
  "Subject": "mailto:smartbus-ticketing@example.com",
  "PublicKey": "…",
  "PrivateKey": "…"
}
```

Deploy — biến môi trường: `WebPush__PublicKey`, `WebPush__PrivateKey`, `WebPush__Subject`.

- `Subject` phải bắt đầu bằng `mailto:` (hoặc URL `https:`) — claim `sub` của JWT VAPID; FCM có
  thể từ chối JWT thiếu claim này.
- **LUẬT 2 — repo PUBLIC**: không commit khoá thật. File mẫu
  `appsettings.Development.json.example` chỉ có chuỗi placeholder.
- `WebPushOptions.KiemTra` chạy lúc khởi động (trừ môi trường `Testing`): thiếu/sai định dạng là
  app dừng ngay kèm hướng dẫn, không đợi tới lúc khách bấm "Bật thông báo".
- **Đổi khoá VAPID = mọi subscription cũ hỏng** (push service từ chối JWT ký bằng khoá khác
  `applicationServerKey` của subscription). Phải báo nhóm và để người dùng bật lại.

---

## 3. Luồng đăng ký và gửi tin

### 3.1. Đăng ký (frontend)

1. Người dùng mở `/notification-settings` (thực đơn tài khoản → **Cài đặt thông báo**).
2. Màn hình đăng ký `/sw.js` (không xin quyền) và tải danh sách thiết bị hiện có.
3. Người dùng bấm **"Bật thông báo trên thiết bị này"** — PHẢI là thao tác bấm:
   - `Notification.requestPermission()`;
   - `navigator.serviceWorker.register('/sw.js')` + `ready`;
   - `GET /api/web-push/vapid-public-key` → giải base64url thành 65 byte;
   - `pushManager.subscribe({ userVisibleOnly: true, applicationServerKey })`;
   - `POST /api/web-push/devices` với `endpoint`, `p256Dh`, `auth`, `expirationTime`.
4. Backend lưu thiết bị theo `UserId` trong token; đăng ký lại cùng endpoint thì ghi đè, giữ `id`.

`webPush.ts` dùng lại subscription cũ nếu trình duyệt đã có và khoá VAPID không đổi; nếu khoá đổi
thì `unsubscribe()` rồi subscribe lại.

### 3.2. Gửi tin (backend)

```csharp
// Service nghiệp vụ (ví dụ service báo xe cách trạm < 500 m — Kiên, Sprint 5 dòng 15)
await _webPushSender.SendToUserAsync(userId, new WebPushMessage
{
    Title = "Xe 08 sắp đến trạm",
    Body = "Xe còn khoảng 500 m tới trạm Bờ Hồ.",
    Url = "/notifications",          // tab mở ra khi bấm
    Tag = $"route-08-stop-{stopId}", // tin cùng tag thay thế tin cũ
});
```

- `IWebPushSender` **không bao giờ ném lỗi**: thiết bị hỏng/mạng chết chỉ ghi log, nghiệp vụ chính
  không bị kéo theo.
- Gửi cho **mọi thiết bị** của người dùng (`SendToUserAsync`); tin thử dùng
  `SendToDeviceAsync(userId, deviceId, …)`.
- Subscription bị push service báo **404/410** được dọn khỏi store ngay; lỗi mạng/5xx thì **giữ**
  để lần sau thử lại (nhầm hai ca này là xoá sạch thiết bị sau một cú nghẽn mạng).
- Payload JSON camelCase, service worker đọc `event.data.json()`:
  `{ "title", "body", "url", "tag" }`.

### 3.3. Hiển thị và bấm thông báo

- `sw.js` gọi `registration.showNotification(title, { body, icon, badge, tag, data: { url } })`.
- Bấm thông báo: focus tab cùng origin nếu có (giữ phiên đăng nhập) rồi `navigate(url)`;
  không có tab nào thì `clients.openWindow(url)`.
- Thiếu `url` → mở `/`. Thiếu `tag` → mỗi tin một thông báo riêng.

---

## 4. Chạy thử ở máy local

1. Điền `WebPush` trong `appsettings.Development.json` (§2.2).
2. Chạy backend (cổng 5080) và frontend (cổng 5173):

   ```bash
   dotnet run --project backend/SmartBus.Api
   npm --prefix frontend run dev
   ```

3. Mở `http://localhost:5173` — **localhost được tính là ngữ cảnh an toàn** nên service worker và
   Notification chạy được dù không có HTTPS. Chạy qua IP LAN bằng HTTP thì **không** chạy.
4. Đăng nhập → thực đơn tài khoản → **Cài đặt thông báo** → **Bật thông báo trên thiết bị này**.
5. Kiểm tra:
   - DevTools → **Application → Service workers**: `/sw.js` ở trạng thái *activated*;
   - DevTools → **Application → Manifest**: thấy tên, icon 192/512, không lỗi;
   - bấm **Gửi thử** trên dòng thiết bị → thông báo hệ điều hành hiện; bấm thông báo → mở
     `/notifications`.
6. Muốn thử khi tab đã đóng: đóng tab (đừng tắt trình duyệt), dùng nút gửi thử từ tab khác hoặc
   gọi API bằng curl với token của chính mình.

```bash
# Gửi thử bằng curl — thay $TOKEN và $ID
curl -X POST "http://localhost:5080/api/web-push/devices/$ID/test" \
  -H "Authorization: Bearer $TOKEN" -i
```

> Chrome/Edge trên Windows cần bật thông báo cho trình duyệt trong **Cài đặt Windows → Hệ thống →
> Thông báo**. Firefox có thể cần bật "Thông báo" trong cài đặt trang.

---

## 5. Việc còn lại / giới hạn đã biết

| # | Việc | Ghi chú |
|---|---|---|
| 1 | **TTL tường minh** | `PushMessage.TimeToLive` của thư viện có sẵn nhưng `WebPushClient` chưa đặt — push service dùng mặc định (thường giữ 4 tuần). Đặt TTL ngắn cho tin "xe sắp đến" để tin cũ không hiện muộn. |
| 2 | **`pushsubscriptionchange`** | SW chưa tự đăng ký lại khi trình duyệt xoay subscription. Cần token người dùng (SW không có) — sẽ xử lý bằng lần mở app kế tiếp hoặc màn "đăng ký lại". |
| 3 | **Lưu CSDL** | Đang là `InMemoryWebPushSubscriptionStore` — khởi động lại API là mất hết đăng ký. Chờ migration của Dăm (§6). |
| 4 | **Nối vào nghiệp vụ** | Chưa có service nào gọi `IWebPushSender` (dòng 15/29 của Kiên). Hiện chỉ nút "Gửi thử" dùng. |
| 5 | **VAPID token cache** | `VapidAuthentication.TokenCache` đang để mặc định (không cache) — mỗi tin ký JWT mới. Chấp nhận được ở quy mô demo; nếu gửi hàng loạt thì gắn cache. |
| 6 | **Audit Target "Devices"** | `AuditLogMiddleware` ghi Target `Devices:{id}` dù bảng chưa tồn tại — sẽ khớp khi bảng được tạo. |
| 7 | **iOS** | Safari 16.4+ chỉ cho Web Push với ứng dụng đã **thêm vào Màn hình chính**. Màn cài đặt đã ghi hướng dẫn. |
| 8 | **Nhiều tab** | Mỗi tab có thể gọi đăng ký; backend ghi đè theo endpoint nên không sinh dòng trùng. |
| 9 | **Rate limit 429** | `PushServiceClient.AutoRetryAfter` mặc định tự chờ `Retry-After` một lần — hành vi mong muốn, không cần đổi. |

---

## 6. Đề xuất bảng `WebPushSubscriptions` (việc của Dăm)

Store hiện tại đã tách sau interface `IWebPushSubscriptionStore`; thay bằng store EF là thay một
đăng ký DI, không đổi controller/service. Đề xuất schema theo đúng quy ước CSDL của nhóm:

| Cột | Kiểu | Ràng buộc | Ghi chú |
|---|---|---|---|
| `Id` | `uuid` | PK | `Guid.NewGuid()` trong entity |
| `UserId` | `uuid` | NOT NULL, FK `Users(Id)`, `Restrict` | thiết bị thuộc một người |
| `Endpoint` | `varchar(500)` | NOT NULL, **UNIQUE** | push endpoint; unique để đăng ký lại = ghi đè |
| `P256Dh` | `varchar(120)` | NOT NULL | khoá công khai thiết bị (65 byte base64url) |
| `Auth` | `varchar(60)` | NOT NULL | bí mật xác thực 16 byte base64url |
| `ExpirationTime` | `timestamptz` | NULL | trình duyệt báo; null = không hẹn giờ |
| `UserAgent` | `varchar(512)` | NOT NULL, mặc định `""` | để phân biệt thiết bị |
| `CreatedAt` | `timestamptz` | NOT NULL | lần đăng ký ĐẦU tiên |
| `UpdatedAt` | `timestamptz` | NULL | lần đăng ký lại gần nhất (ghi đè khoá/UA) |

Index: `UNIQUE (Endpoint)`, `INDEX (UserId)`.

- **Unique trên `Endpoint`, không phải `(UserId, Endpoint)`**: cùng một endpoint là cùng một
  subscription trình duyệt; hai tài khoản đăng nhập trên cùng trình duyệt sẽ ghi đè `UserId` —
  đúng ý "trình duyệt này thuộc người đăng nhập gần nhất".
- Dọn thiết bị chết bằng 404/410 đã có ở `WebPushSender`; cân nhắc job dọn `ExpirationTime < now`.
- `Endpoint` là capability URL (ai có endpoint + khoá mới gửi được) — không trả ra ngoài cho người
  khác; controller hiện chỉ trả cho chủ sở hữu.

---

## 7. Kiểm thử

Backend (28 test riêng cho Web Push, toàn bộ 2025 test xanh):

```bash
dotnet test backend/SmartBus.Tests/SmartBus.Tests.csproj --filter "FullyQualifiedName~WebPush"
```

- `WebPushApiTests` — 401, hình dạng VAPID, đăng ký 201/200, kiểm hình dạng khoá, phạm vi "của
  tôi" (404), gỡ, gửi thử, audit log.
- `WebPushSenderTests` — 2xx → `Sent`, 404/410 → `Stale` + dọn, 5xx/mạng chết → `Failed` + giữ.
- `WebPushWiringTests` — đăng ký DI đúng một lần, store singleton, client resolve được.

Frontend:

```bash
npm --prefix frontend run build   # tsc -b + vite build
npm --prefix frontend run lint    # oxlint
```

---

## 8. Definition of Done — đối chiếu

- [x] Backend: 5 endpoint `/api/web-push`, phân quyền `[Authorize]`, lỗi đúng định dạng chung.
- [x] Mã hoá/gửi push bằng thư viện NuGet `Lib.Net.Http.WebPush` (không tự viết crypto).
- [x] Service worker + PWA manifest + icon; bấm thông báo mở đúng màn.
- [x] Màn cài đặt: bật/tắt, danh sách thiết bị, gửi thử, hướng dẫn khi bị chặn/không hỗ trợ.
- [x] Route + lối vào thực đơn tài khoản.
- [x] `docs/api-contract.md` cập nhật; tài liệu này.
- [x] Test backend xanh, frontend build + lint sạch (cảnh báo lint còn lại là file có từ trước).
- [ ] Bảng `WebPushSubscriptions` + migration (Dăm — §6).
- [ ] Nối `IWebPushSender` vào nghiệp vụ thông báo thật (Kiên — §5 mục 4).
