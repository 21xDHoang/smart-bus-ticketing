# Thông báo kết quả hoàn tiền cho khách (trên web) — Sprint 4: notifier + lớp bọc

> **US 8** — *"Là hệ thống, tôi muốn tự động xử lý hoàn tiền theo quy định khi giao dịch thanh toán thất bại hoặc vé bị hủy hợp lệ."*
>
> Việc này là **dòng 34** sheet Sprint 4 — *"Thông báo kết quả hoàn tiền qua email và trên web"* (Trung bình · 3 điểm · Hoàng). Các dòng chung quanh: **27** Dăm (Migrate `Refunds`/`RefundLogs`) · **28** Kiên (service hoàn tiền tự động) · **29** Hiếu (gọi cổng thật) · **30** Hoàng (thử lại theo lịch — `docs/40`) · **31** Dăm (API tra cứu) · **32** Kiên (đối soát) · **33** Hạnh (màn theo dõi) · **35–36** Băng (màn Admin) · **37–38** Vàng (test + kiểm thử chéo).
>
> ⚠️ **Tên task có vế "qua email" — vế đó KHÔNG được cài, theo chủ ý.** Quyết định A8.5 đã chốt thông báo in-app + bảng `Notifications`, không email/SMS/Web Push; người đặt việc chốt lại ngày **15/10**: *"không cần gửi về gmail chỉ cần thông báo trên web"*. Không có mã gửi thư, không có SMTP. Tài liệu này giữ nguyên tên dòng trong sheet để đối chiếu, nhưng phạm vi thật là **web**.
>
> 🟢 **Trạng thái**: nhánh `feature/34-thong-bao-ket-qua-hoan-tien` đứng trên main `875be9c`. **3 file mới + `Program.cs` + 2 file entity (thêm hộ Dăm) + `api-contract.md` + 1 file test mới (12 bài) + 1 file test sửa (ghim lớp bọc)**; toàn bộ backend **1450/1450 xanh**. Không mở endpoint nào — thông báo đi qua hub `notifications` đã có. PR này **không đụng `Migrations/`**.

---

## 1. Ai đọc tài liệu này, để làm gì

| Người | Dòng | Đọc mục | Để làm gì |
| --- | --- | --- | --- |
| **Băng / Thịnh** | 35–36 | **§5.2** | Hai loại `Refund…` + target `Refund` là **hợp đồng frontend mới**: màn thông báo phải hiện đúng hai loại này và điều hướng về "Hoàn tiền của tôi" |
| **Kiên** | **28** | **§3**, §4.3 | Mở endpoint tạo khoản hoàn chỉ cần gọi `ExecuteAsync(id, Initial, now)` như `docs/40` §4.4 — **thông báo tự chạy**, không phải nhớ gì |
| **Hiếu** | **29** | **§3.1** | Cổng thật trả `Succeeded` là mở đường báo tin thật cho khách; trả `Failed` dứt khoát (đừng ném) để thang thử lại chạy tới nấc cuối rồi mới báo "thất bại" |
| **Băng** | **35** | **§3.2** | Nút "Thử lại" gọi vào đúng `ExecuteAsync` — lượt thử tay thành công **cũng** sinh thông báo "Đã hoàn tiền" (khác loại với thông báo thất bại trước đó, nên khách nhận đủ cả hai) |
| **Hạnh** | 33 | **§5** | Màn "Hoàn tiền của tôi" là đích của `RelatedEntityType = Refund`; nội dung thông báo **cố ý không có số tiền** — số tiền khách đọc ở màn đó |
| **Dăm** | 27, 31 | §6.1 | Đối chiếu: hai giá trị enum mới + một target mới trong **file của Dăm** (thêm hộ, ghi rõ trong PR) — cột `varchar` không CHECK nên **không cần migration** |
| **Giàng A Vàng** | 37–38 | §6.2 | Danh sách 12 bài test đã có — khỏi viết trùng |

**Nguồn gốc nhiệm vụ** — `docs/35` §7.4 (tài liệu US 8 của Dăm), dòng giao cho Hoàng: thông báo kết quả cuối của khoản hoàn cho khách. Tài liệu đó còn mở cả vế email; **vế email bị người đặt việc chốt bỏ ngày 15/10** (xem đầu tài liệu).

---

## 2. Ba file mới + hai chỗ sửa

| File | Làm gì |
| --- | --- |
| `Services/IRefundResultNotifier.cs` | Hợp đồng `NotifyAsync(outcome)` — hai chốt: **không bao giờ ném lỗi**, **không nhận `CancellationToken`** |
| `Services/RefundResultNotifier.cs` (**245 dòng**) | Cài đặt: đọc dòng `Refunds` đã lưu → quyết định "có gì để báo" → ghi `Notifications` + đẩy SignalR |
| `Services/NotifyingRefundAttemptService.cs` (65 dòng) | **Lớp bọc** quanh `RefundAttemptService`: chạy lượt gọi cổng rồi đưa outcome cho notifier — chỗ nối để **mọi** đường gọi cổng đi qua |
| `Program.cs` | Thay 1 dòng `AddScoped<IRefundAttemptService, RefundAttemptService>()` bằng 3 dòng: lớp trần + notifier + lớp bọc (cùng lối `SeatHoldCreateService`) |
| `Entities/NotificationType.cs` (**file Dăm**) | Thêm `RefundSucceeded`, `RefundFailed` — **thêm hộ, ghi rõ trong PR**; cột `varchar`, không CHECK, không migration |
| `Entities/NotificationTarget.cs` (**file Dăm**) | Thêm `Refund` — cùng ghi chú trên |
| `docs/api-contract.md` | 5 chỗ: ghi chú hub, "hai nguồn" → **"ba nguồn"**, bảng chính sách + bảng kết quả, "bốn loại" → **"sáu loại"**, khối 🔴 "không số tiền trong nội dung" |

Toàn bộ nằm trong **một luồng không có endpoint**: khách nhận thông báo qua `/hubs/notifications` đã có từ US 5 và qua màn thông báo đọc bảng `Notifications`.

---

## 3. Chính sách — báo gì, im gì

### 3.1 Phán quyết đọc từ dòng `Refunds` ĐÃ LƯU

Notifier **không tin `outcome.Result`**. Outcome là ảnh chụp lúc service chạy; dòng có thể đã bị lượt khác lật kể từ đó. Thứ được báo là **trạng thái thật mới nhất của dòng**:

| Trạng thái dòng `Refunds` (đọc lại) | Báo? | Loại |
| --- | --- | --- |
| `Succeeded` | ✅ | `RefundSucceeded` — "Đã hoàn tiền" |
| `Failed` **và** `NextRetryAt = null` (hết thang) | ✅ | `RefundFailed` — "Hoàn tiền thất bại" |
| `Failed` còn `NextRetryAt` (đang chờ lịch) | ❌ | — thất bại tạm thời, một khoản hỏng rồi tự khỏi mà báo từng lượt là dội tới sáu thông báo |
| `Pending` (cổng chưa trả lời) | ❌ | — báo thành công hay thất bại đều là **đoán**; đoán thất bại còn mở đường cho job gọi lại khoản cổng đã trả |
| *(không có `outcome.Claimed`)* | ❌ | — lượt thua cuộc đua không gọi cổng, không có gì để báo; người gọi đã log "bỏ qua" |

Thang thử lại 5' → 30' → 2h → 12h → 24h (`docs/40` §2.4) nghĩa là một khoản hỏng hẳn đi qua **6 lượt gọi cổng**; khách chỉ nghe **một lần**, ở lượt cuối. Đọc dòng đã lưu còn tự chữa ca thua cuộc đua: nếu lượt khác cũng báo, chốt chống trùng (§5.3) nuốt lượt đến sau.

*(Chữ nghĩa hai câu thông báo nằm ở **§6** — mục này chỉ nói lượt nào được phát.)*

### 3.2 Khi nào ghi hỏng / đẩy hỏng

Hai khối `try` **riêng** (cùng lối `TicketRequestResultNotifier`): ghi hỏng → `LogError` và bỏ (khách mất thông báo thật — không có gì bù được ngoài log); ghi xong, đẩy hỏng → `LogWarning` (dòng đã nằm trong CSDL, khách vẫn đọc lại được khi mở màn — mất đúng lời nhắc tức thời). Đường "chưa có gì để báo" (§3.1, dòng 3–4) **cố ý không ghi log** — đường đó đi qua ở mọi lượt thất bại tạm thời, ghi log ở đây là làm ngập log vận hành.

---

## 4. Chỗ nối — vì sao là lớp bọc, không phải sửa thẳng service

### 4.1 Ba đường gọi cổng, một cửa

`IRefundAttemptService` trong DI giờ trỏ vào `NotifyingRefundAttemptService`, nên **cả ba** đường gọi cổng đi qua chỗ báo tin mà không đường nào phải tự nhớ:

| Đường | Ai gọi | Trạng thái |
| --- | --- | --- |
| `Initial` | Kiên (dòng 28) | **chưa dựng** — dựng xong là thông báo tự chạy |
| `ScheduledRetry` | Job thử lại (dòng 30, đã vào main) | đang chạy — hôm nay mọi lượt ra `Unresolved` vì cổng còn placeholder, nên chưa sinh thông báo nào |
| `ManualRetry` | Nút "Thử lại" (dòng 35) | **endpoint chưa mở** — mở xong là thông báo tự chạy |

### 4.2 Vì sao bọc ở tầng interface

- **`RefundAttemptService` giữ giao thức giành dòng `xmin`** (`docs/40` §2.3) — trộn thêm một việc có thể chạm I/O kênh thông báo vào cùng lớp là mở đường cho lỗi phụ trợ làm hỏng lượt gọi cổng đã trả tiền. Bọc ở ngoài giữ service đó nguyên vẹn.
- **Lớp bọc cố ý mỏng** — khác `NotifyingSeatHoldCreateService` (lọc bằng `result.Success` ngay trong lớp bọc), phán quyết "kết quả cuối hay chưa" phải đọc dòng `Refunds` đã lưu, thứ chỉ notifier có `AppDbContext` để làm. Lớp bọc chuyển tiếp nguyên tử, không quyết định gì.

### 4.3 `CancellationToken` đi đâu

Token vẫn chuyển tiếp nguyên vẹn cho **lượt gọi cổng** (hợp đồng của `IRefundAttemptService`), nhưng `IRefundResultNotifier.NotifyAsync` **cố ý không nhận token**: khoản đã `Succeeded` thì khách phải biết, bất kể job có bị dừng ngay sau đó. Token được phép cắt lượt gọi cổng, không được phép làm dòng thông báo biến mất.

---

## 5. Khách thấy gì

### 5.1 Hai đường đến

1. **Đang mở app** — sự kiện `NotificationReceived(notificationId)` đẩy vào nhóm `user:{userId}` qua `/hubs/notifications` (hub đã có từ US 5; tên nhóm suy từ ID người nhận, không từ tham số client).
2. **Mở lại sau** — dòng `Notifications` nằm trong CSDL; màn thông báo đọc ra.

### 5.2 Hợp đồng frontend (Băng / Thịnh — dòng 35–36)

Đây là **thay đổi hợp đồng**: `NotificationType` từ bốn giá trị thành **sáu**; `NotificationTarget` thêm `Refund`. Màn thông báo hiện chưa có mã nào cho hai loại mới — cần:
- hiện đúng tiêu đề/nội dung (bảng **§6** — backend ghi sẵn cả `Title` lẫn `Body`, frontend chỉ hiển thị);
- điều hướng `RelatedEntityType = Refund` về màn **"Hoàn tiền của tôi"** (dòng 33 của Hạnh), dùng `RelatedEntityId` để mở chi tiết khoản.

`api-contract.md` (mục thông báo, bảng "Sáu loại") là bản hợp đồng đã cập nhật — đọc ở đó khi nối UI.

### 5.3 Chống ghi trùng

Đọc-trước-khi-ghi theo khoá `(người nhận, loại, dòng Refunds)` — cùng giới hạn một-bản-app đã ghi ở `docs/33` §4, cố ý **không** đặt unique index ở tầng CSDL (dư một dòng trùng là thiệt hại nhỏ hơn hẳn một thông báo hợp lệ bị chặn). Hệ quả đúng đắn:
- gọi lại cùng loại → nuốt (một kết quả cuối = một thông báo);
- **hết thang thất bại rồi quản trị thử tay thành công → HAI thông báo** (khác loại) — đúng vòng đời thật của khoản đó;
- hai khoản hoàn khác nhau của cùng một khách → hai thông báo (khoá lọc theo dòng, không theo người nhận).

---

## 6. Nội dung thông báo — ba chốt

| Loại | Tiêu đề | Nội dung |
| --- | --- | --- |
| `RefundSucceeded` | Đã hoàn tiền | Khoản hoàn tiền của bạn đã được xử lý thành công. |
| `RefundFailed` | Hoàn tiền thất bại | Khoản hoàn tiền của bạn chưa thực hiện được sau nhiều lần thử lại. Bạn vui lòng liên hệ nhà xe để được hỗ trợ. |

Backend ghi sẵn cả `Title` lẫn `Body` vào dòng `Notifications` — frontend chỉ hiển thị (xem §5.2).

### 6.1 🔴 Không có SỐ TIỀN nào trong nội dung

Có test ghim: `Assert.DoesNotMatch("[0-9]", …)` — không một chữ số nào trong `Body`. Số tiền phải là thứ khách tra ở màn "Hoàn tiền của tôi" (nơi dòng `Refunds` sống, cột `Amount` — con số **đã thực sự gửi lên cổng**); chép số vào thông báo là dựng bản sao thứ hai có thể lệch, mà định dạng tiền cũng là việc của frontend. Cùng lối đã chốt ở `docs/33` §6.1; khối 🔴 trong `api-contract.md` (mục thông báo) là bản hợp đồng.

### 6.2 Không lộ lý do kỹ thuật

`FailReason` của cổng ("Sai mã định danh tài khoản nhận hoàn") không vào nội dung; khách đọc câu đó không làm được gì ngoài hoang mang. Có test ghim (`Assert.DoesNotContain`).

### 6.3 Câu "liên hệ nhà xe" là hướng dẫn hành động

Hết thang tự động nghĩa là khoản nằm chờ quản trị (dòng 35), khách cần biết mình phải làm gì chứ không chỉ biết "đã hỏng". Câu chữ cố định, không template — cùng lối `docs/33` §6.3.

## 7. File, test, số liệu

### 7.1 File của Dăm bị đụng (thêm hộ)

`NotificationType.cs` (+2 giá trị), `NotificationTarget.cs` (+1 giá trị) — **không cần migration**: cả hai cột lưu `varchar`, không `CHECK`, không index theo tập giá trị. Việc này ghi rõ trong PR theo lệ "thêm hộ".

### 7.2 Test

**12 bài mới** — `RefundResultNotifierTests.cs`:

| Nhóm | Số bài | Ghìm điều gì |
| --- | --- | --- |
| Hai kết quả cuối | 2 | `Succeeded` → đúng một dòng, đúng loại/target/dòng, đúng câu chữ, **không chữ số**, đúng nhóm `user:{id}`; hết thang → `RefundFailed`, **không lộ lý do kỹ thuật** |
| Im lặng có chủ ý | 3 | Thất bại còn lịch · `Pending` · lượt thua cuộc đua (`Claimed = false`) — cả ba không ghi, không đẩy |
| Phán quyết từ dòng đã lưu | 1 | Outcome nói Succeeded nhưng dòng đã lật về `Pending` → im; outcome nói `Unresolved` nhưng dòng đã `Succeeded` → vẫn báo |
| Chống ghi trùng | 3 | Gọi lại lần hai → một dòng, một lần đẩy; hết thang thất bại rồi thành công → **hai** thông báo khác loại; hai khoản khác nhau cùng khách → hai thông báo |
| Hai nhánh hỏng | 3 | Kênh đẩy ném → không lỗi nổi ra + dòng vẫn ghi; đọc hỏng → không lỗi; ghi hỏng (interceptor) → không lỗi |

**1 bài sửa** — `RefundRetryWiringTests.Ruot_mot_luot_goi_cong_resolve_duoc_tu_scope_con_cua_tung_khoan`: thêm `Assert.IsType<NotifyingRefundAttemptService>(…)` (thay `NotNull` cũ). Ghim đúng lớp bọc: ai đăng ký lùi về lớp trần thì cổng vẫn được gọi, tiền vẫn về, mọi test khác vẫn xanh — chỉ mỗi khách là không bao giờ được báo.

**Bộ backend đầy đủ: 1450/1450 xanh** (trước nhánh: 1438).

**Không có test nào chạm CSDL thật** — InMemory; chốt chống trùng chỉ kiểm được trên một tiến trình (giới hạn đã biết, `docs/33` §4).

---

## 8. Ranh giới đã biết

- **Vế email không cài** — chủ ý (A8.5 + chốt 15/10). Ai muốn dựng lại: đó là việc mới, không phải chỗ thiếu của PR này.
- **Hôm nay chưa có thông báo nào chạy thật** — cổng còn placeholder (Hiếu dòng 29) nên mọi lượt ra `Unresolved → Pending`; chưa khoản nào tới `Succeeded` hay hết thang. Muốn thử tay: cắm một cổng giả trả `Succeeded`/`Failed`, hoặc đặt thẳng trạng thái dòng `Refunds`.
- **Chốt chống trùng là đọc-trước-khi-ghi** — hai bản app cùng báo một khoản trong cùng tích tắc có thể sinh hai dòng (InMemory không cưỡng chế được gì). Chấp nhận; không unique index.
- **Notifier không phải người lật trạng thái** — chỉ đọc (`AsNoTracking`) rồi ghi `Notifications`; dòng `Refunds` do lượt gọi cổng ghi trước đó.
- **Không mở endpoint, không đụng `Migrations/`, không đụng `frontend/`** — hai loại thông báo mới là việc nối UI của Băng/Thịnh (§5.2).
- **`RelatedEntityId` có thể trỏ vào khoản đã bị dọn?** Không — `Refunds` là dữ liệu doanh thu, không luồng nào xoá; nhánh "dòng biến mất" chỉ có `LogWarning` phòng xa.

---

## 9. Bàn giao

| Việc | Ai | Ghi chú |
| --- | --- | --- |
| Nối hai loại `Refund…` + target `Refund` vào màn thông báo | **Băng / Thịnh** (35–36) | Hợp đồng đã cập nhật ở `api-contract.md`; `Title`/`Body` backend gửi sẵn |
| Mở endpoint `POST /admin/refunds/{id}/retry` | **ai mở dòng 35** | Ruột chỉ cần gọi `ExecuteAsync(id, ManualRetry, now)` như `docs/40` §3.3 — thông báo tự chạy |
| Cổng thật trả `Succeeded`/`Failed` dứt khoát | **Hiếu** (29) | Là điều kiện để thấy thông báo thật đầu tiên (`docs/40` §3.1) |
| Lượt `Initial` khi tạo khoản hoàn | **Kiên** (28) | `docs/40` §4.4 — không phải nhớ gì thêm cho thông báo |
| Đối chiếu enum/target trong file mình | **Dăm** (27, 31) | Thêm hộ, không migration |
