# API tra cứu trạng thái hoàn tiền — Sprint 4, US 8 (dòng 42)

> Viết cho các task nối vào bề mặt hoàn tiền — **US 8** *"Là hệ thống, tôi muốn tự động xử lý hoàn
> tiền theo quy định khi giao dịch thanh toán thất bại hoặc vé bị hủy hợp lệ"*, sheet *Sprint 4*:
> **Kiên** (service hoàn tiền tự động, dòng 39) · **Hiếu** (gọi cổng hoàn tiền, dòng 40) ·
> **Hoàng** (thử lại theo lịch, dòng 41) · **Dăm** (API tra cứu trạng thái hoàn tiền của giao dịch,
> **dòng 42 — tài liệu này**) · **Kiên** (đối soát, dòng 43) · **Hạnh** (màn theo dõi của hành
> khách, dòng 44) · **Thịnh** (thông báo, dòng 45) · **Băng** (màn Admin, dòng 46–47) ·
> **Giàng A Vàng** (test, dòng 48–49).
>
> 🟢 **Trạng thái:** hai endpoint **đã code xong** trên nhánh `feature/8-api-tra-cuu-hoan-tien`,
> `dotnet build` 0/0 và `dotnet test` **1309/1309** (§8). **Chưa merge.** Nhánh này **xếp trên**
> `feature/8-migrate-refunds-refundlogs` (task dòng 38, cũng chưa merge) vì API không biên dịch được
> nếu thiếu entity `Refund`/`RefundLog` — PR nhắm vào nhánh migration, sau khi nhánh đó vào `main`
> thì GitHub tự trỏ lại `main`.
>
> 🔴 **Chưa áp lên CSDL chung.** Bảng `Refunds` / `RefundLogs` chỉ có thật sau khi nhánh dòng 38
> merge rồi chạy `database update` (thứ tự ở `docs/25-huong-dan-csdl-chung.md` §4). Trước bước đó,
> gọi hai endpoint này sẽ lỗi `relation "Refunds" does not exist` — **không** phải lỗi của service.

## 1. Bề mặt này KHÔNG nằm trong A9 — và hợp đồng API vừa được bổ sung

Hai bảng đằng sau đã được báo ngoài A9 ở `docs/35-csdl-hoan-tien.md` §1 (nhóm bảng thứ **tư**). Phần
**API** cũng có một việc tương tự, nhưng khác bản chất:

| Nguồn | Nói gì |
|---|---|
| **Bảng phân công Sprint 4, dòng 42** | *"API tra cứu trạng thái hoàn tiền của giao dịch"* — Vàng Thị Dăm, ưu tiên **Trung bình**, 4 điểm |
| **`docs/api-contract.md`** (trước nhánh này) | **Không có** mục nào về hoàn tiền — chỉ một câu nhắc ở mục "Phát hành vé sau khi thanh toán" |
| **`docs/35` §7.3** | Đề nghị **Hiếu chủ trì** mục *"Hoàn tiền"* trong hợp đồng, vì *"hình dạng endpoint là việc của chủ API"* |
| Code đã merge | **Có** hình dạng rồi — `frontend/src/api/refundAdminApi.ts` (PR #187, màn Băng dòng 46) đã chốt cứng tên trường |

⚠️ **Đã bổ sung `docs/api-contract.md`** (mục `## Hoàn tiền — /refunds`), và đây là chỗ **lệch nhẹ**
so với đề nghị ở `docs/35` §7.3: đề nghị đó muốn Hiếu chủ trì, còn nhánh này **tự viết** — vì ⛔ luật
5 buộc *sửa hợp đồng TRƯỚC rồi mới code*, mà chờ Hiếu thì task dòng 42 đứng lại. Nội dung **không**
phải quyết định mới: nó **chép nguyên văn** hình dạng đã bị frontend chốt cứng và bổ sung phần còn
thiếu (đường dẫn, mã trạng thái, ngữ nghĩa mảng rỗng). **Hiếu và Băng nên đọc lại mục đó** — Hiếu là
chủ API, Băng là người đã viết bản nháp mà mục này chép theo.

## 2. Ba quyết định về hình dạng — đọc trước khi sửa

### 2.1 Vì sao `/refunds` trần, KHÔNG có đoạn `/mine`

Bản nháp `frontend/src/api/myRefundApi.ts` (nhánh `feature/8-man-hinh-theo-doi-hoan-tien`, dòng 44 của
Hạnh) ghi endpoint dự kiến là `GET /refunds/mine?status=`. Bản này **không** có `/mine`.

Danh sách này **vốn đã** là "của tôi": phạm vi là `WHERE UserId = @userId` **trong truy vấn**, không
phải một đoạn đường dẫn. Thêm `/mine` là dựng hai đường dẫn cho cùng một câu hỏi, rồi phải trả lời
*"vậy `/refunds` trần để làm gì?"* — và câu trả lời duy nhất hợp lý là *"trả khoản hoàn của mọi
người"*, tức là mở một cửa cho hành khách nhìn thấy dữ liệu của người khác. Danh sách toàn hệ thống
là việc của `GET /admin/refunds` (bề mặt khác, chưa làm — §6).

> ⚠️ **Việc của Hạnh:** đổi `/refunds/mine` → `/refunds` khi nối API thật. File đó đã tự ghi hình
> dạng endpoint của nó là *"CHƯA cam kết"*, nên đây là chốt lại chứ không phải phá hợp đồng đã hứa.

### 2.2 Vì sao `[Authorize]` trần, và vì sao ca "của người khác" là 404 chứ không 403

RBAC của dự án chỉ có **hai** policy — `RbacPolicies.AdminOnly` và `RbacPolicies.ManagerOrAbove` —
**không có** policy `Passenger`. Bốn endpoint cùng dạng "dữ liệu của tôi" đã đi trước
(`/tickets/me`, `/feedbacks/me`, `/monthly-passes/me`, `/seat-holds/{sessionCode}`) đều dùng
`[Authorize]` trần với phạm vi trong truy vấn, và **không** service nào trong repo cài *"chủ sở hữu
HOẶC admin"*. Bản này theo đúng lối đó.

Chi tiết quan trọng: **cả hai endpoint gộp hai điều kiện vào MỘT `WHERE`**, không đọc rồi kiểm quyền
ở tầng trên —

```csharp
// RefundLookupService.GetByRefundCodeAsync
.Where(r => r.RefundCode == refundCode && r.UserId == userId)
```

Nhờ vậy không có nhánh nào chạm được khoản hoàn của người khác, kể cả để **đếm** hay để **kiểm sự tồn
tại** — và ca đó tự rơi vào `NotFound` giống hệt ca "mã không tồn tại", không cần nhánh `Forbidden`
riêng. Trả **403** ở đây là xác nhận *"mã này có thật, chỉ là không phải của bạn"* — tức là để lộ
thông tin cho người đang dò. Cùng lối `MyTicketsService` và `SeatHoldLookupService`.

### 2.3 Vì sao khoá tra của dòng 42 là `paymentCode`

Câu chữ của task là *"trạng thái hoàn tiền **của giao dịch**"*, và "giao dịch" trong dự án này là
bảng `Payments` (`PaymentCode` = *"mã giao dịch nội bộ"*). Nên `?paymentCode=` là thứ biến endpoint
thành câu trả lời cho **một** giao dịch — `Refund.PaymentId` là khoá ngoại BẮT BUỘC, và
`docs/35` §2.3 đã chỉ đúng đường tra này.

`GetByRefundCodeAsync` phục vụ câu hỏi còn lại (*"khoản RF-xxxx của tôi giờ ra sao"*), cũng đúng như
doc-comment `Refund.RefundCode` đã ghi trước. Hai đường tra, hai câu hỏi khác nhau — không có đường
tra theo `TicketId`, lý do ở `docs/35` §2.3.

`paymentCode` **không** chép thành cột riêng trên `Refunds` (đó là lý do `docs/35` §2.5 không có cột
`MethodCode`): lọc qua khoá ngoại sang `Payments`, dùng unique index của `Payments.PaymentCode` một
đầu và `IX_Refunds_PaymentId` đầu kia. Chép mã giao dịch sang bảng hoàn tiền là dựng nguồn sự thật
thứ hai cho cùng một chuỗi.

## 3. Bảng trường trả về — 21 trường, trong đó **bảy trường nullable**

Tên trường **giữ nguyên xi** interface `Refund` trong `frontend/src/api/refundAdminApi.ts` — bản nháp
đó **đã merge** (PR #187), và TypeScript của frontend **không** đọc được file C#, nên đổi một chữ là
vỡ màn hình mà không ai thấy lỗi biên dịch. Test `Response_chi_tra_dung_cac_truong_trong_hop_dong`
ghim đúng 21 tên đó.

> ⚠️ **FE phải nới kiểu bảy trường** `ticketCode`, `routeCode`, `routeName`, `origin`, `destination`,
> `departureTime`, `seatNumber` từ `string` sang `string | null` (`departureTime` là `string | null`
> dạng ISO). Bản nháp FE khai chúng không nullable, nhưng khoản hoàn nguồn `PaymentFailure` — **nhánh
> US 8 nêu ra đầu tiên** — thường **không có vé nào**, vì vé chỉ phát hành sau khi tiền về thành công.
> Bảy trường này **null cùng lúc**; `methodName`, `paymentCode`, `passengerName` và `amount` thì luôn
> có (khoản hoàn không gắn giao dịch nào là khoản hoàn không đối soát được, nên `PaymentId` NOT NULL).
>
> 🔴 **Không "sửa" bằng chuỗi rỗng.** `departureTime: ""` đi qua `new Date("")` ra `Invalid Date` —
> màn hình hiện `NaN` chứ không hiện ô trống. Null là giá trị duy nhất nói đúng *"không có chuyến nào
> gắn với khoản hoàn này"*.

`source` và `status` là **chuỗi** chứ không phải enum: dự án không bật `JsonStringEnumConverter` nên
enum tuôn ra JSON thành **con số** (bẫy đã ghi ở `TicketResponse` và `VoucherStatisticsService`).
Service `ToString()` **trong bộ nhớ** qua một lớp trung gian `RefundRow` — gọi `ToString()` ngay trong
projection là thứ EF không dịch được sang SQL.

`createdAt` / `processedAt` / `updatedAt` là ba mốc khác nhau, đừng gộp: `processedAt` là mốc **cổng**
chốt kết quả cuối (null khi còn `Pending`), còn `updatedAt` chỉ là *"dòng có bị sửa"* và **cũng nhảy
khi Admin bấm thử lại** — cùng lối tách mốc nghiệp vụ khỏi mốc kỹ thuật ở
`CancellationRequest.ReviewedAt`.

## 4. 🔴 Bốn ca trả **200 kèm mảng rỗng** — không phải 404, không phải 403

| Ca | Trả về |
|---|---|
| Người gọi chưa có khoản hoàn nào | `200 []` |
| `paymentCode` không tồn tại | `200 []` |
| `paymentCode` là giao dịch **của người khác** | `200 []` |
| `status` gõ sai (`?status=DangXuLy`) | `200 []` |

Ba ca đầu trả lời **giống hệt nhau**. Hệ quả có chủ ý: endpoint này **không dùng được để dò** xem một
mã giao dịch có tồn tại hay không — và đó chính là điều kiện để không ai đọc được giao dịch của người
khác qua đây. Ca thứ tư theo **mục D3** của `docs/03-quy-uoc.md`: mã trạng thái lạ có thể là trạng
thái hợp lệ trong tương lai, 400 ở đây làm màn hình cũ vỡ khi backend thêm giá trị mới.

**Hai ca 400 duy nhất** của bề mặt này là ràng buộc **hình dạng**, khác hẳn "gõ sai mã":
`paymentCode` > 64 ký tự, `status` > 20 ký tự — mã dài hơn cột là mã **không thể tồn tại**. Cả hai
theo cấu trúc lỗi chung `{ message, errors }`.

`GET /refunds/{refundCode}` **không có nhánh 400**: mã hoàn tiền tra thẳng vào cột unique, gõ sai chỉ
có nghĩa là không tìm thấy. Thứ tự mã trạng thái của endpoint này là **200 / 401 / 404** — hết.

### 4.1 Đọc `status`, ĐỪNG đếm dòng

🔴 Một giao dịch **có thể có nhiều dòng** trong `Refunds` theo thời gian: khoản đầu `Failed` hết lịch
thử lại rồi bị bỏ, sau đó phát sinh khoản mới. Chỉ mục duy nhất `IX_Refunds_PaymentId_Active` **cố ý**
chỉ chặn hai khoản **chưa thất bại** cùng lúc (`docs/35` §4.1). Nên *"giao dịch này đã được hoàn
chưa"* **không** trả lời được bằng phép đếm — phải đọc `status` của từng dòng. Đây là lời hứa đã ghi
sẵn trong `AppDbContext.Refund.cs`, và `GET /refunds?paymentCode=` trả **cả** các dòng lịch sử chứ
không lọc bớt.

## 5. `retryCount` — chỗ cài đặt lời hứa "không có cột đếm"

`Refunds` **cố ý không có cột `RetryCount`** (`docs/35` §2.4): số lượt gọi cổng là phép **đếm trên
nhật ký**, không phải trạng thái của khoản hoàn. Bản này là chỗ cài đặt lời hứa đó:

```
RetryCount = COUNT(RefundLogs WHERE RefundId = <khoản> AND Trigger <> 'Initial')
```

Lượt gọi **đầu tiên** (`Initial`, do service dòng 39 tự gọi) **không** tính — dòng vừa tạo có
`retryCount = 0`, khớp đúng ngữ nghĩa bản nháp FE. Đếm cả `ScheduledRetry` (job dòng 41) lẫn
`ManualRetry` (nút "Thử lại" ở màn Admin dòng 46), và **chỉ** đếm log của chính khoản đó —
`RetryCount_chi_dem_log_cua_chinh_khoan_do` ghim đúng chỗ dễ quên này.

Vì đây là tương quan `COUNT` chứ không phải `Include`, `Refund` **không** cần navigation `Logs`
(`AppDbContext.Refund.cs` để `WithMany()` trống) — không phải sửa file cấu hình của Dăm.

## 6. Việc còn lại của người khác

- **Hạnh** (dòng 44) — ⚠️ đổi `/refunds/mine` → `/refunds` (§2.1) và **nới kiểu bảy trường nullable**
  (§3). Màn này lọc theo `UserId` với chỉ mục `IX_Refunds_UserId`; hai endpoint mới **đã sẵn sàng**,
  `USE_MOCK_DATA` tắt được ngay khi CSDL có bảng.
- **Băng** (dòng 46–47) — màn Admin gọi ba endpoint **chưa có**: `GET /admin/refunds`,
  `POST /admin/refunds/{id}/retry`, `GET /admin/refunds/export`. Hình dạng 21 trường ở §3 là **chung**
  cho hai màn, nên khi dựng `GET /admin/refunds` thì tái dùng đúng `RefundResponse` — **đừng** đặt
  hình dạng thứ hai. ⚠️ Danh sách toàn hệ thống là màn quản trị nên phải **phân trang** (khác hẳn mảng
  trần ở đây) và gắn policy `ManagerOrAbove`/`AdminOnly`.
- **Hiếu** (dòng 40) — ghi **một dòng `RefundLogs` cho MỖI lượt gọi cổng**, kể cả lượt hỏng và lượt
  không có câu trả lời; `retryCount` ở đây đếm thẳng trên bảng đó nên thiếu log là **số hiển thị sai**
  cho khách (§5).
- **Hoàng** (dòng 41) — `NextRetryAt` là cột riêng, **không** suy ra từ `Status` + `CreatedAt`
  (`docs/35` §2.6). Endpoint này **không** trả `nextRetryAt`: màn hành khách không cần, còn màn Admin
  lấy được từ `GET /admin/refunds` khi endpoint đó có. Muốn thêm vào hình dạng chung thì sửa
  `docs/api-contract.md` trước (⛔ luật 5).
- **Ai cần thêm trường** — nhắn Dăm: hình dạng 21 trường đã bị **frontend chốt cứng** và có test ghim,
  thêm một trường là đổi hợp đồng API chứ không phải chi tiết nội bộ.

## 7. Việc cần báo nhóm (không phải việc code)

1. **Hợp đồng API đã có mục hoàn tiền — Hiếu và Băng đọc lại.** Trước nhánh này `docs/api-contract.md`
   **không** có một chữ nào về hoàn tiền (§1), trong khi hai màn frontend đã merge đều đọc API hoàn
   tiền. Đây là lần thứ **tư** việc này được nhắc — US 5 (`docs/32` §7.4, rồi nhắc lại ở `docs/33`
   §7.5), US 15 (`docs/34` §7.4) và US 8 (`docs/35` §7.3). **Nhóm nên chốt: mỗi story có endpoint thì
   mục hợp đồng là một phần của Definition of Done**, chứ không phải việc nhớ ra sau.
2. **`docs/03-quy-uoc.md` vẫn không tồn tại trong repo** — lần thứ **tám**. Mọi tham chiếu A1–A9 và
   mục D3 ở tài liệu này chỉ còn là trích dẫn trong code.
3. **Bốn câu hỏi nhóm phải chốt** đã liệt kê ở cuối mục hoàn tiền của `docs/api-contract.md`, trong
   đó hai câu thuộc nhánh này: *(a)* thông báo kết quả hoàn tiền đi **kênh nào** (bảng phân công dòng
   45 ghi *"qua email và trên web"*, A8.5 chốt **in-app qua SignalR**, không email — xung đột y hệt
   dòng 7 của US 5); *(b)* **cổng sandbox nào hoàn tiền được thật** — nếu không cổng nào thì dòng
   40–41 và bước kiểm thử chéo dòng 49 đều phải dựa vào mock.
4. **A9 cần bổ sung `Refunds` + `RefundLogs`** — nhắc lại lần thứ **tư** (đã ghi ở `docs/35` §7.1).
   Nhóm nên bổ sung **một lượt** cả năm bảng còn thiếu thay vì nhắc từng lần.
5. **Chưa ai nhận `GET /admin/refunds` và `GET /admin/refunds/export`.** Bảng phân công có dòng 46–47
   cho **Băng** (giao diện), nhưng **API** của hai màn đó không nằm ở dòng nào — dòng 42 là API **tra
   cứu của hành khách**, không phải API quản trị. Nhóm nên chỉ định người làm (Hiếu hoặc Kiên) trước
   khi Băng nối API thật.

## 8. Kiểm chứng trên nhánh này — 14/10/2026

### 8.1 Nội dung đã làm

| File | Việc |
|---|---|
| `backend/SmartBus.Api/Dtos/Refunds/RefundResponse.cs` | Hình dạng 21 trường (§3) |
| `backend/SmartBus.Api/Dtos/Refunds/ListMyRefundsRequest.cs` | Query `paymentCode` + `status` kèm chặn độ dài (§4) |
| `backend/SmartBus.Api/Services/IRefundLookupService.cs` | Hợp đồng service — **cố ý** không có tham số nào hỏi khoản của người khác |
| `backend/SmartBus.Api/Services/RefundLookupService.cs` | Cài đặt: lọc trong truy vấn, phép đếm `retryCount`, lớp trung gian `RefundRow` |
| `backend/SmartBus.Api/Controllers/RefundLookupController.cs` | `[Route("api/refunds")]`, `[Authorize]` trần |
| `backend/SmartBus.Api/Program.cs` | **MỘT** dòng `AddScoped` — ⚠️ **file của Hoàng**, nhờ Hoàng xem qua trong PR |
| `backend/SmartBus.Tests/RefundLookupApiTests.cs` | **22 test** mới |
| `docs/api-contract.md` | Mục `## Hoàn tiền — /refunds` (§1) |

Bốn quyết định **cố ý không làm**, để không lấn sang việc chưa ai nhận: `GET /admin/refunds`,
`POST /admin/refunds/{id}/retry`, `GET /admin/refunds/export` (§7.5) và đường dẫn `/refunds/mine`
(§2.1).

### 8.2 Lệnh đã chạy

```
dotnet build SmartBus.Api/SmartBus.Api.csproj     → 0 Warning, 0 Error
dotnet build SmartBus.Tests/SmartBus.Tests.csproj → 0 Warning, 0 Error
dotnet test  SmartBus.Tests/SmartBus.Tests.csproj → 1309/1309 ✅ (1287 cũ + 22 mới)
```

22 test mới phủ: 401 cả hai endpoint · mảng trần không phân trang · chỉ trả khoản của chính người gọi
· lọc theo `paymentCode` (§2.3) · **bốn ca `200 []`** (§4) · so khớp chính xác mã · lọc `status` kể cả
hoa/thường · lọc kết hợp · hai ca 400 · 404 cho mã không tồn tại và mã của người khác (§2.2) · 21 tên
trường · nhánh `PaymentFailure` không vé với **bảy trường null** (§3) · `retryCount` = 0 khi mới có
lượt `Initial` · chỉ đếm lượt gọi lại · chỉ đếm log của chính khoản đó (§5).

> ⚠️ **Một lượt chạy đã đỏ 6 test, không phải do nhánh này:** cả 6 là nhánh SQLite của
> `SeatHoldRaceFlowApiTests`, lỗi `SQLite Error 13: database or disk is full` — ổ `C:` của máy này
> **đầy 100%** (còn 204 KB). Chạy lại với `TEMP` trỏ sang ổ `D:` thì **1309/1309 xanh**. Ghi lại đây
> để lần sau ai gặp đúng 6 test đó thì kiểm ổ đĩa trước, đừng đi tìm lỗi trong `SeatHolds`.

### 8.3 ⚠️ Phân tích đồ thị (GitNexus) KHÔNG chạy được trên máy này

`CLAUDE.md` bắt buộc chạy `impact` trước khi sửa và `detect_changes` trước khi commit. Trên máy dựng
nhánh này **cả hai đều không chạy được** (repo không có `.gitnexus/`, `analyze` tự crash — đã ghi ở
`docs/35` §8.5). Nên **không có** kết quả `impact`/`detect_changes` cho nhánh này. Bù lại bằng:

- đọc trực tiếp `MyTicketsService`, `FeedbackLookupService`, `SeatHoldLookupService`,
  `MyTicketsController`, `FeedbackLookupController` và `SeatHoldLookupController` để đối chiếu lối
  viết, lối chú thích và cách trả 404;
- `dotnet build` 0/0 và `dotnet test` 1309/1309 ở §8.2;
- rà `git grep` cho `IRefundLookupService` / `RefundLookupController`: hai cái tên này **chưa từng**
  tồn tại trước nhánh này, nên không có ai gọi vào để mà vỡ.

**Ai có máy chạy được GitNexus nên chạy `impact` cho `Program.cs` trước khi merge** — đây là **file
dùng chung** duy nhất nhánh này sửa (đúng một dòng `AddScoped`), nên là chỗ va chạm với mọi nhánh
Sprint 4 khác đang mở.

### 8.4 Việc còn lại, theo đúng thứ tự `docs/25-huong-dan-csdl-chung.md` §4

1. PR nhánh này → **có người khác approve** (⛔ luật 7) → **Squash and merge**.
   Base của PR là `feature/8-migrate-refunds-refundlogs` (nhánh dòng 38), **không phải** `main`.
2. ⚠️ **Sau khi nhánh dòng 38 được squash-merge vào `main`, nhánh này phải được dựng lại** — đừng
   bấm merge tiếp trên nhánh cũ. Lý do: `main` nhận nội dung của dòng 38 dưới **commit hash khác**,
   nên commit đó **không** phải tổ tiên của nhánh này; PR sẽ hiện lại **toàn bộ** diff của dòng 38, và
   lần squash thứ hai dễ đụng nhau ở `AppDbContext.cs`, `ModelSnapshot.cs`. Cách làm đúng: dựng nhánh
   **mới** từ `main` đã cập nhật rồi `git cherry-pick` (⛔ luật 8 **cấm** `push --force` và
   `--force-with-lease`, nên **không** rebase-rồi-đẩy-đè lên nhánh cũ).
3. Merge nhánh dòng 38 vào `main` trước, rồi mới:
   `dotnet ef database update --project backend/SmartBus.Api` — sau bước này hai endpoint mới chạy thật
   được. Trước đó chúng trả 500 kèm `relation "Refunds" does not exist`.
4. Kiểm thử chéo dòng 48–49 (**Giàng A Vàng**) chỉ có nghĩa khi bước 3 xong **và** dòng 39 (service
   tạo khoản hoàn) đã có — hiện **chưa** có service nào ghi vào `Refunds`, nên test tích hợp ở §8.2
   seed thẳng dữ liệu thay vì đi qua luồng thật.
