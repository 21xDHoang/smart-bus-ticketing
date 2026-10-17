# Component bật/tắt nhận thông báo theo trạm

> Task **"Component bật/tắt nhận thông báo theo trạm"** — US 10 *"Thông báo trạm"*, Sprint 5 dòng 20
> của bảng phân công (người làm ghi: Hoàng Văn Thịnh), nhánh
> `feature/10-component-bat-tat-thong-bao-theo-tram`.
>
> ⚠️ **Bảng phân công không khớp với yêu cầu phiên làm việc:** `Product_Backlog_Smart_Bus.xlsx`
> ghi task này ở **Sprint 5**, không phải Sprint 4, và giao cho **Hoàng Văn Thịnh** — không phải
> Vàng Thị Dăm. Nhánh này được tạo theo lựa chọn *"nhánh mới từ main"* của Dăm; người merge cần
> đối chiếu lại việc phân công trước khi nhận. Hai file được thêm **mới hoàn toàn**, không sửa file
> của ai khác.
>
> 🟢 **Trạng thái:** component + API client đã xong, build và lint sạch. **Chưa nối vào trang nào**
> (việc của người sở hữu trang — xem §5) và **chưa chạy được thật** cho tới khi API của Hiếu merge
> (xem §2).

## 1. Hai file được thêm

| File | Vai trò |
|---|---|
| `frontend/src/api/subscriptionApi.ts` | Client ba endpoint `GET/POST/DELETE /subscriptions` — kiểu `Subscription`, `CreateSubscriptionRequest` |
| `frontend/src/components/StopNotificationToggle.tsx` | Công tắc tự khép kín: tải trạng thái → bật (POST) → tắt (DELETE) |

Không sửa `App.tsx`, `package.json`, `src/api/` cũ, hay bất kỳ page/component nào khác.

## 2. Phụ thuộc: API `/subscriptions` CHƯA merge

Hợp đồng và ba endpoint nằm trên nhánh
`feature/43-dang-ky-huy-theo-doi-thong-bao` của **Trần Trung Hiếu** (task Sprint 5 dòng 17):

| Method | Endpoint | Trả về |
|---|---|---|
| GET | `/subscriptions` | `Subscription[]`, mới nhất trước, mảng trần |
| POST | `/subscriptions` | `201` + `Subscription`; trùng → `409`; trạm không thuộc tuyến → `400 errors.stopId` |
| DELETE | `/subscriptions/{id}` | `204`; không tồn tại/của người khác → `404` |

Hình dạng đầy đủ: `docs/44-api-dang-ky-huy-theo-doi-thong-bao.md` và mục *"Theo dõi thông báo —
`/subscriptions`"* của `docs/api-contract.md` **trên nhánh đó**. Nhánh này **không** chép mục hợp
đồng sang `main` để tránh xung đột khi Hiếu merge; `subscriptionApi.ts` bám đúng hợp đồng đã chốt,
không tự đổi request/response.

🔴 **Hệ quả:** trên `main` hiện tại, gọi ba endpoint này sẽ **404** vì controller chưa merge. Cần
Hiếu merge trước khi công tắc chạy thật.

## 3. Component xử lý gì

### Ba trạng thái tải

| Trạng thái | Giao diện |
|---|---|
| `loading` | Công tắc quay, chưa bấm được |
| `ready` | Công tắc bật/tắt bình thường |
| `error` | Công tắc khoá + "Không tải được trạng thái theo dõi." + nút **Thử lại** |

### Bốn trạng thái nghiệp vụ

| Tình huống | Hành vi |
|---|---|
| Chưa đăng nhập | Công tắc khoá, tooltip *"Đăng nhập để nhận thông báo…"* |
| Chưa theo dõi đúng trạm | Công tắc tắt → bật gọi `POST { routeId, stopId }` |
| Đang theo dõi đúng trạm | Công tắc bật → tắt gọi `DELETE /subscriptions/{id}` |
| Chỉ có lượt theo dõi CẢ TUYẾN (`stopId = null`) | Công tắc hiện **bật và khoá** — trạm đã được phủ; tắt ở đây sẽ xoá lượt theo dõi của mọi trạm trên tuyến (docs/37 §3.1) |

### Hai ca đặc biệt

- **`POST` trả `409`** (tab khác vừa bật): không báo lỗi, tải lại danh sách để lấy đúng `id` —
  kết quả cuối cùng vẫn là "đang bật".
- **`DELETE` trả `404`** (dòng đã bị xoá ở nơi khác): coi như đã tắt, không báo lỗi.

Không có cờ `IsActive`: bật là **ghi dòng mới**, tắt là **xoá dòng**, đúng docs/37 §3.2.

## 4. Cách dùng

```tsx
import StopNotificationToggle from '../components/StopNotificationToggle';

<StopNotificationToggle
  routeId={route.id}
  stopId={stop.id}
  onChanged={(subscribed) => {
    // Trang cha cập nhật danh sách theo dõi của mình nếu có (không bắt buộc).
  }}
/>
```

Props: `routeId`, `stopId` (bắt buộc); `label`, `disabled`, `onChanged` (tuỳ chọn). Component tự
gọi API, trang cha không phải giữ state theo dõi.

## 5. Việc còn lại

1. **Hiếu** — merge nhánh `feature/43-dang-ky-huy-theo-doi-thong-bao` (API + hợp đồng). Trước đó
   công tắc chỉ build được, chưa chạy thật.
2. **Người sở hữu trang** — nhúng component vào nơi hành khách xem trạm (ví dụ trang chi tiết
   tuyến/chuyến). Việc này **sửa file page của người khác**, nên phải hỏi chủ file.
   ⚠️ Nếu trang hiển thị **nhiều trạm cùng lúc**, đừng dựng một component cho mỗi trạm: mỗi
   instance gọi `GET /subscriptions` một lần. Khi đó nâng việc tải danh sách lên trang cha (một
   lần) rồi truyền trạng thái xuống — cần đổi API của component, ghi ở đây để lần tích hợp đó
   không bỏ sót.
3. **Băng** — nếu công tắc cần nằm trong `App.tsx`/route mới, nhờ Băng thêm route (file của Băng).
4. **Kiên** (dòng 15, 29) — đọc bảng `Subscriptions`; nhớ khử trùng khi khách vừa theo dõi cả
   tuyến vừa theo dõi một trạm (docs/37 §7).

## 6. Đã kiểm

| Phép kiểm | Kết quả |
|---|---|
| `npm run build` (`tsc -b` + `vite build`) | ✅ pass |
| `npm run lint` (`oxlint`) | ✅ không có cảnh báo ở hai file mới; các cảnh báo còn lại nằm ở file khác, có từ trước |
| GitNexus `impact` | ⛔ không chạy được — repo không có `.gitnexus/` (cùng giới hạn đã ghi ở docs/40 §8.5); bù bằng rà soát text: hai file mới chưa được file nào import, nên blast radius hiện bằng 0 |
