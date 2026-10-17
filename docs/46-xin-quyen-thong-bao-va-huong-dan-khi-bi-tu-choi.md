# Xin quyền thông báo và hướng dẫn khi người dùng từ chối

> Task **"Xin quyền thông báo và hướng dẫn khi người dùng từ chối"** — US 10 *"Thông báo
> trạm"*, **Sprint 5 dòng 23** của bảng phân công (người làm ghi: **Hoàng Văn Thịnh**),
> nhánh `feature/10-xin-quyen-thong-bao`.
>
> ⚠️ **Bảng phân công không khớp với yêu cầu phiên làm việc:** `Product_Backlog_Smart_Bus.xlsx`
> ghi task này ở **Sprint 5**, không phải Sprint 4, và giao cho **Hoàng Văn Thịnh** — không phải
> Vàng Thị Dăm. Nhánh được tạo theo lựa chọn *"nhánh mới từ main"* của Dăm; người merge cần đối
> chiếu lại việc phân công trước khi nhận. Hai file được thêm **mới hoàn toàn**, không sửa file
> của ai khác.
>
> 🟢 **Trạng thái:** component + helper đã xong, build và lint sạch. **Chưa nhúng vào layout**
> (file của Băng — xem §4) nên hiện chưa ai nhìn thấy. Đây **không phải** phần đăng ký Web Push
> (dòng 16 của Hiếu) — xem §2.

## 1. Hai file được thêm

| File | Vai trò |
|---|---|
| `frontend/src/components/notificationPermission.ts` | Logic thuần: đọc quyền, xin quyền (có bọc API callback cũ của Safari), theo dõi quyền đổi từ bên ngoài, ghi nhớ trạng thái đã ẩn |
| `frontend/src/components/NotificationPermissionPrompt.tsx` | Banner bốn trạng thái: mời bật quyền / hướng dẫn mở lại quyền theo trình duyệt |

Không sửa `App.tsx`, `main.tsx`, `package.json`, layout, page hay component nào khác. Không
thêm thư viện.

## 2. Quyền trình duyệt ≠ Web Push — đọc trước khi merge

Đây là chỗ dễ hiểu nhầm nhất của dòng 23:

| Việc | Ai làm | Trạng thái |
|---|---|---|
| **Xin quyền** để trang được phép hiện thông báo hệ điều hành | Dòng 23 — nhánh này | ✅ Đã làm |
| **Đăng ký Web Push / PWA**, nhận thông báo khi tab đã đóng | Dòng 16 — Trần Trung Hiếu | ❌ Chưa làm, chưa có trong repo |
| **Đẩy thông báo trong ứng dụng** qua SignalR + bảng `Notifications` | Kiến trúc đã chốt (A8.5) | ✅ Backend có; frontend nhận qua `NotificationHub` |

Component này chỉ xin **quyền của trình duyệt**. Nó không tạo service worker, không đăng ký
push subscription, không gửi thông báo nào. Khi quyền đã `granted`, ứng dụng *có thể* hiện
thông báo hệ điều hành bằng `new Notification(...)` — nhưng chỉ khi tab còn mở, và việc nối
vào sự kiện `NotificationReceived` vẫn còn ở phía trước (§5).

### Xung đột với quyết định A8.5

A8.5 chốt: *"Thông báo — in-app qua SignalR… Không làm email/SMS/**Web Push** trong phạm vi đồ
án"*. Trong khi đó Sprint 5 có **hai dòng** về thông báo trình duyệt: dòng 16 (Web Push/PWA) và
dòng 23 (task này). Đây là xung đột **đã ghi nhận** ở docs/02 §6 và docs/33, không phải do nhánh
này tạo ra.

Cách nhánh này xử lý: **không làm Web Push**, chỉ làm đúng chữ của dòng 23 — xin quyền và hướng
dẫn. Hệ quả trung thực:

- Nếu nhóm giữ A8.5 và không dùng thông báo hệ điều hành, component này là **phần dự phòng**:
  quyền có thể được bật nhưng chưa ai dùng. Nó không phá gì và không gửi gì.
- Nếu nhóm muốn hiện thông báo hệ điều hành khi tab mở, cần nối sự kiện SignalR với
  `new Notification(...)` (§5) — vẫn **không cần** Web Push.
- Chỉ khi muốn nhận thông báo lúc tab đã đóng mới cần dòng 16 (service worker + VAPID). Việc đó
  phải chốt lại A8.5 trước khi làm.

## 3. Component xử lý gì

### Bốn trạng thái quyền

| Trạng thái | Giao diện |
|---|---|
| `unsupported` | Không hiện gì — trình duyệt không có API `Notification`, hoặc trang chạy ngoài ngữ cảnh an toàn (HTTP thường; `localhost` vẫn tính là an toàn) |
| `default` | Lời mời + **Bật thông báo** (gọi `Notification.requestPermission()` đúng lúc người dùng bấm) + **Để sau** |
| `granted` | Không hiện gì — đã bật |
| `denied` | Cảnh báo + hướng dẫn mở lại quyền cho **Chrome/Edge/Cốc Cốc**, **Firefox**, **Safari** + nút **Kiểm tra lại** và **Đã hiểu** |

### Vì sao nhánh `denied` không gọi xin quyền lần nữa

Theo đặc tả Notification API, sau khi người dùng chọn **Chặn**, trình duyệt **không hiện lại hộp
thoại** cho trang đó; `requestPermission()` trả `denied` ngay. Cách duy nhất là người dùng tự mở
cài đặt trang web. Vì vậy nút **Kiểm tra lại** chỉ đọc lại `Notification.permission`; nếu người
dùng đã đổi sang *Cho phép*, trạng thái cập nhật và banner biến mất.

Ngoài ra, trình duyệt chỉ cho xin quyền từ **thao tác bấm của người dùng** — component không tự
gọi khi trang vừa tải.

### Ẩn trong phiên

- **Để sau** (`default`) và **Đã hiểu** (`denied`) ẩn banner bằng `sessionStorage`, khoá
  `smartbus.notifications.permissionDismissed`, giá trị là đúng trạng thái đã ẩn.
- Chỉ ẩn trong **phiên trình duyệt**, không ẩn vĩnh viễn: người từng từ chối vẫn cần thấy lại
  hướng dẫn ở lần mở sau, nhưng không bị nhắc liên tục trong cùng phiên.
- Khoá lưu trạng thái đã ẩn, nên khi quyền đổi (`default` ↔ `denied`) banner tự hiện lại đúng
  loại. Private mode/chặn lưu trữ: không nhớ được, banner hiện sớm hơn — không lỗi.

### Đồng bộ khi người dùng sửa cài đặt trình duyệt

`watchNotificationPermission()` nghe hai nguồn bù nhau:

1. **Permissions API** (`navigator.permissions.query({ name: 'notifications' })`) — báo ngay khi
   quyền đổi ở Chrome/Edge/Firefox.
2. **`visibilitychange`** — khi người dùng quay lại tab, cho Safari và trình duyệt không hỗ trợ
   truy vấn `'notifications'`.

Nhờ vậy không bắt người dùng tải lại trang sau khi mở khoá quyền.

### Callback cho trang cha

```tsx
<NotificationPermissionPrompt
  onPermissionChange={(state) => {
    // 'granted' | 'default' | 'denied' | 'unsupported'
  }}
/>
```

Chỉ gọi khi trạng thái **thực sự đổi** (hoặc khi người dùng bấm xin quyền và kết quả khác trước).
Component không tự gửi thông báo.

## 4. Cách nhúng — cần chủ file layout

Component **chưa được đặt vào đâu**. `App.tsx`, `main.tsx`, `AppLayout`… là file của Băng, nên
không tự sửa. Khi Băng đồng ý, chèn một lần trong layout dành cho người đã đăng nhập, phía trên
nội dung trang:

```tsx
import NotificationPermissionPrompt from '../NotificationPermissionPrompt';

// trong phần render của layout:
<NotificationPermissionPrompt />
```

Khuyến nghị:

- Đặt trong layout **sau khi đăng nhập** — khách chưa có tài khoản không có thông báo nào để
  nhận; hỏi quyền trên trang đăng nhập là làm phiền.
- Chỉ dựng **một** instance cho cả ứng dụng. Quyền là của trình duyệt, không phải của từng
  trang; nhiều instance sẽ cùng hiện banner và cùng ghi `sessionStorage`.
- Trên điện thoại, banner chiếm ngang màn hình; đặt trong vùng nội dung, không đặt trong header
  hẹp.

## 5. Việc còn lại

1. **Băng** — nhúng component vào layout (file của Băng), theo §4.
2. **Nối với SignalR (việc của người làm màn thông báo):** khi `NotificationReceived` báo có
   thông báo mới và quyền đã `granted`, gọi `new Notification(title, { body })`. Nội dung vẫn
   lấy từ `GET /notifications/me` như hiện nay — hub chỉ đánh thức, không phải nguồn dữ liệu thứ
   hai (docs/41 §5.2).
3. **Chốt lại A8.5** nếu nhóm thực sự muốn thông báo khi tab đã đóng — khi đó mới cần dòng 16
   (Web Push/PWA) của Hiếu.
4. **Thịnh** (người được phân công dòng 23) — rà lại component này nếu nhận việc; nó được viết
   theo lựa chọn *"nhánh mới từ main"* của Dăm.

## 6. Đã kiểm

| Phép kiểm | Kết quả |
|---|---|
| `npm run build` (`tsc -b` + `vite build`) | ✅ pass |
| `npm run lint` (`oxlint`) | ✅ không có cảnh báo ở hai file mới |
| GitNexus `impact` / `detect-changes` | ⛔ không chạy được — repo không có `.gitnexus/` (cùng giới hạn đã ghi ở docs/40 §8.5, docs/45 §6); bù bằng rà soát text: hai file mới chưa được file nào import nên blast radius hiện bằng 0 |
| Thử tay trên trình duyệt | ⏳ chưa — component chưa được nhúng (§4), và cần HTTPS hoặc `localhost` để API quyền hoạt động |
