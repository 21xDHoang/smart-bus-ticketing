# API đăng ký / huỷ theo dõi thông báo theo tuyến và trạm

> Viết cho task **Sprint 5, dòng 17** *"API đăng ký / huỷ theo dõi thông báo theo tuyến và trạm"*
> (bảng tính Sprint 4 ghi dòng 43) — **Trần Trung Hiếu** (Chủ API), US 10 *"Là hành khách, tôi muốn
> nhận thông báo khi xe sắp đến trạm đón/trạm xuống để chuẩn bị di chuyển."*
>
> Đây là endpoint đầu tiên GHI vào bảng `Subscriptions` (docs/37-csdl-mau-thong-bao-va-theo-doi.md —
> migration đã merge, PR #195). Người đọc bảng này sau đó: service bắn tin khi xe cách trạm <500m
> (Kiên, dòng 15), service đẩy sự cố theo tuyến (Kiên, dòng 29), component công tắc bật/tắt theo
> trạm (Thịnh, dòng 20).
>
> 🟢 **Trạng thái:** đã dựng, đã kiểm 18 ca test mới + toàn bộ bộ test 1560/1560 xanh (xem §4).
>
> ⚠️ **Vì sao số 44 mà không phải 43:** nhánh `feature/29-api-goi-cong-hoan-tien` (API gọi cổng hoàn
> tiền, đã push chưa merge) giữ `docs/43-api-goi-cong-hoan-tien.md` — người viết sau nhường số
> (cùng lối đã ghi ở docs/40 §đầu).

## 1. Ba endpoint — hình dạng đã chốt trong hợp đồng

Mục **"Theo dõi thông báo — `/subscriptions`"** của `docs/api-contract.md` (viết cùng nhánh này,
đúng luật 5: sửa hợp đồng trước rồi mới code):

| Method | Endpoint | Làm gì |
|---|---|---|
| GET | `/subscriptions` | danh sách theo dõi của tôi, mới nhất trước — nền để công tắc (dòng 20) vẽ |
| POST | `/subscriptions` | đăng ký theo dõi một trạm; `stopId` bỏ trống = cả tuyến. 201 + dòng vừa tạo |
| DELETE | `/subscriptions/{id}` | huỷ theo dõi = XOÁ dòng, không lật cờ (docs/37 §3.2). 204 |

Cả ba `[Authorize]` trần, phạm vi "của tôi" nằm ngay trong truy vấn theo `Subscriptions.UserId` —
lượt của người khác và lượt không tồn tại cùng một câu 404 (cùng lối `/refunds`, `/tickets/me`).

## 2. Ba việc docs/37 §7 đã giao cho task này — và làm thế nào

| Việc | Cách làm |
|---|---|
| Ghi / xoá dòng `Subscriptions` | POST ghi dòng mới; DELETE xoá có theo dõi (`Remove` + `SaveChanges`) — không dùng `ExecuteDeleteAsync` vì provider InMemory không hỗ trợ xoá hàng loạt |
| **Kiểm trạm có thuộc tuyến** — khoá ngoại không lo hộ (docs/37 §3.1) | Kiểm `RouteStops` trong service; sai → **400** theo trường `stopId` (trạm CÓ tồn tại, cái sai nằm ở dữ liệu gửi lên — cùng lối `PUT /trips/{id}/location`) |
| **409 khi bấm theo dõi hai lần**, không để nổi thành 500 | Hai tầng: kiểm-trước trong truy vấn (lời rõ: "đã theo dõi tuyến này rồi" / "đã theo dõi trạm này rồi") + bắt `DbUpdateException` từ chỉ mục duy nhất bộ phận (docs/37 §3.3) dịch thành 409 — chốt cho cuộc đua thật trên PostgreSQL |

Hai nhánh so trùng tách bạch `StopId IS NULL` / `= stopId`: so `== null` bằng tham số nullable sẽ
dịch thành `= @p` (NULL) và không khớp dòng nào trong SQL — phải viết đúng nghĩa.

## 3. Nhật ký kiểm toán

`POST /subscriptions` và `DELETE /subscriptions/{id}` đi qua `AuditLogMiddleware` (POST → Create với
Target `Subscriptions:{id}` đọc từ body trả về; DELETE → Delete với Target từ đường dẫn) — đã khai
vào `BangPhuSong` của `AuditLogCoverageTests.cs` kèm test ghim hành vi ở `SubscriptionApiTests`
(`Dang_ky_va_huy_deu_duoc_ghi_nhat_ky_dung_doi_tuong`).

## 4. Đã kiểm và ĐẠT

| Phép kiểm | Kết quả |
|---|---|
| `dotnet build backend/SmartBus.Api` | ✅ 0 warning, 0 error |
| 18 ca test mới (`SubscriptionApiTests`) | ✅ 18/18 |
| `dotnet test backend/SmartBus.Tests` (toàn bộ) | ✅ **1560/1560** — kể cả `AuditLogCoverageTests` đối chiếu BangPhuSong với routing thật |
| 401 cả ba endpoint | ✅ không token → 401 |
| 404/400/409 theo hợp đồng | ✅ tuyến lạ → 404 · trạm lạ → 404 · trạm không thuộc tuyến → 400 `errors.stopId` · đăng ký trùng (cả theo trạm lẫn cả tuyến) → 409 không tạo dòng thứ hai |
| Vừa theo dõi cả tuyến vừa theo dõi một trạm | ✅ hai dòng, không xung đột (docs/37 §7) |
| Phạm vi "của tôi" | ✅ danh sách chỉ của mình; xoá lượt của người khác → 404 cùng câu với không tồn tại |
| Nhánh dịch `DbUpdateException` → 409 | ✅ bằng DbContext con ném lỗi ở `SaveChanges` |
| Hai chỉ mục duy nhất bộ phận | ✅ ghim trên MÔ HÌNH (metadata, không cần CSDL) — `WHERE "StopId" IS NULL` và `WHERE "StopId" IS NOT NULL`, đều unique — ai xoá `HasFilter` là test đỏ ngay |

⚠️ **Giới hạn** (giữ nguyên cảnh báo docs/37 §7): InMemory KHÔNG cưỡng chế chỉ mục duy nhất lẫn
khoá ngoại — nhánh thua cuộc đua thật chỉ sống ở PostgreSQL; test mô phỏng lỗi chỉ mục thay vì chờ
một cuộc đua theo thời điểm.

⚠️ **Phân tích đồ thị GitNexus KHÔNG chạy được trên máy này** (repo không có `.gitnexus/` — cùng
giới hạn đã ghi ở docs/40 §8.5). Bù bằng: đọc trực tiếp `Entities/Subscription.cs`,
`AppDbContext.Subscription.cs`, docs/37 trước khi dựng + build + toàn bộ bộ test ở trên. Ai có máy
chạy được GitNexus nên chạy `impact` cho `Program.cs` trước khi merge.

## 5. Việc còn lại của người khác

- **Hoàng** — xem lại 1 dòng đăng ký `ISubscriptionService` trong `Program.cs` (file của Hoàng).
- **Thịnh (dòng 20)** — component công tắc bật/tắt theo trạm: đọc `GET /subscriptions` để vẽ, bật
  gọi `POST`, tắt gọi `DELETE` — không gửi cờ `IsActive` nào (không có cột đó, docs/37 §3.2).
- **Kiên (dòng 15, 29)** — hai service đọc bảng này: lọc theo `UserId`, và nhớ khử trùng khi một
  khách vừa theo dõi cả tuyến vừa theo dõi một trạm (docs/37 §7).
- **Dăm (dòng 19)** — `GET /notifications/me` (lịch sử thông báo) không thuộc bề mặt này; mục hợp
  đồng mới đã ghi chú để khỏi lẫn.
- **Nhóm** — A9 vẫn thiếu `Subscriptions` + `NotificationTemplates` (lần thứ năm "A9 lệch thực tế",
  docs/37 §8).
