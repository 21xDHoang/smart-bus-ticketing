# CSDL hoá đơn điện tử (`Invoices`)

> Viết cho task **"Migrate bảng Invoices"** — bảng phân công **Sprint 4, dòng 27**, ưu tiên **Cao**,
> **3 điểm**, Assign **Vàng Thị Dăm**, phục vụ **US 7** *"Xuất hóa đơn"* — *"Là hành khách, tôi muốn
> nhận hóa đơn điện tử qua email để lưu trữ chứng từ thanh toán."*
>
> Người cần đọc: **Kiên** (dòng 28 — service sinh PDF hoá đơn, task **6 điểm** đang chờ bảng này) ·
> **Hiếu** (dòng 29, 30 — gửi email tự động + API tra cứu/tải lại) · **Hoàng** (dòng 31, và cấu hình
> `Email__*` khi deploy) · **Hạnh** (dòng 32 — màn danh sách hoá đơn của tôi) · **Thịnh · Băng**
> (dòng 33, 34, 35 — xem trước PDF, nút tải/gửi lại, màn Admin tra cứu) · **Giàng A Vàng**
> (dòng 36, 37 — test) · **cả nhóm** (sáu câu hỏi ở §6).

> ⚠️ Bảng này **không có trong A9** — đọc §1 trước.

## 0. Bối cảnh: dòng 27 là dòng migrate **duy nhất** của Sprint 4 còn nợ

Bảng phân công Sprint 4 có **năm** dòng migrate, đều giao Vàng Thị Dăm:

| Dòng | Bảng | Trạng thái thật |
|---|---|---|
| 2 | `TicketChanges`, `CancellationRequests` | ✅ xong (`docs/32`) |
| 14 | `TicketValidations` | ✅ xong (`docs/34`) |
| **27** | **`Invoices`** | 🔴 **chưa ai làm — nhánh này** |
| 38 | `Refunds`, `RefundLogs` | ⚠️ code có, nhưng chỉ ở nhánh `feature/8-api-tra-cuu-hoan-tien` **chưa merge** |
| 50 | `PriorityGroups`, `PriorityApprovals` | ✅ **đã xong từ Sprint 2** dưới tên `DiscountRequests` |

Dòng 50 là một cái bẫy đã sập một lần: `git grep "class PriorityGroup"` **không ra kết quả**, nhưng kết
luận *"chưa ai làm"* từ đó là **SAI**. Repo này cố ý **không** đặt tên bảng theo bảng phân công khi tên
đó trùng nghiệp vụ với một bảng A9 đã có. Chuỗi bằng chứng, để lần sau không ai tra lại từ đầu:

- Dòng 50 mang story *"Là HR/Quản lý, tôi muốn duyệt tài khoản học sinh/sinh viên/người cao tuổi để họ
  được hưởng giá vé ưu đãi."* — **trùng nguyên văn** với **US 17**.
- `docs/02-sprint-roadmap.md` §111: *"Chấp nhận lộ trình bổ sung (**US 17→S2**, US 10+11→S3, US 24→S5)"*.
- `AppDbContext.DiscountRequest.cs` ghi thẳng rằng task migrate của story 17 ghi *"PriorityGroups,
  PriorityApprovals"* — HAI bảng — nhưng A9 #13 chỉ có MỘT bảng `DiscountRequests`.
- Migration có thật trên `main`: `20260930095511_Sprint2_TripDriver_DiscountRequests.cs` (commit `6003f02`).

⚠️ **Cách tra "dòng này còn nợ không", ba bước không tắt được bước nào:** lấy **story text** của dòng
→ dò sang sheet User Story để ra **số US** → tra số US đó ở `docs/02-sprint-roadmap.md` và trong code.
**Đừng bao giờ kết luận từ tên bảng.** Sáu dòng US 17 (50–55) vẫn nằm nguyên ở sheet Sprint 4 trong
khi sheet Sprint 2 không có dòng nào của US 17 — đó là **dòng ma**, không phải việc còn nợ.

> Bản dài của phát hiện này ở `docs/37-csdl-mau-thong-bao-va-theo-doi.md` §8.1 — nhưng `docs/37` nằm ở
> nhánh `feature/10-migrate-notificationtemplates-subscriptions`, **chưa merge**. Ghi lại đủ ở đây để
> tài liệu này **đứng một mình được**, không phụ thuộc một nhánh khác có merge hay không.

## 1. Bảng này KHÔNG nằm trong A9 — lần thứ sáu

A9 (danh sách **20 bảng đóng băng**) có `Payments` ở **#17** với ghi chú *"mã giao dịch cổng + khoá
chống trùng"*, và **không có dòng nào** cho hoá đơn:

| Nguồn | Nói gì |
|---|---|
| **A9 #17** | `Payments` — có trong danh sách đóng băng |
| **A9 #18** | `Vouchers` — bảng kế tiếp, **không** phải `Invoices` |
| **A9** (cả 20 dòng) | **Không có** `Invoices` |
| **Toàn bộ repo, mọi nhánh** | `git grep -l "class Invoice"` trên `--all` → **không có kết quả nào** |
| **Bảng phân công Sprint 4, dòng 27** | *"Migrate bảng Invoices"* — tên bảng **có thật** |

Đây là **lần thứ sáu** việc "A9 lệch thực tế" được nhắc: `docs/28-csdl-ve-dien-tu.md` §7 mục 1
(`Tickets` — task ghi hai bảng, thực tế một bảng), `docs/32` §1
(*"lần thứ hai"*), `docs/34` §1 (*"lần thứ ba"*), `Refunds`/`RefundLogs` (*"lần thứ tư"* — tài liệu ở
nhánh chưa merge), `NotificationTemplates`/`Subscriptions` (*"lần thứ năm"* — cùng nhánh chưa merge).

**Cách xử lý không đổi** (`docs/32` §7.1): **dựng theo phân công, rồi báo nhóm bổ sung A9.** A7 cấm
*"tự thêm bảng ngoài danh sách A9 **mà không báo nhóm**"* — vế bị cấm là **im lặng**. Nhánh này không
im lặng: mục này và §8.

⚠️ Lần này chiều lệch **ngược** với bốn lần đầu: không phải một bảng trong A9 bị dựng lệch, mà là một
bảng **bảng phân công yêu cầu** mà **A9 thiếu**. Cùng loại với hai bảng ở lần thứ năm. Nhóm nên sửa A9
một lượt cho cả ba.

## 2. Cột của bảng `Invoices`

| Cột | Kiểu | Ghi chú |
|---|---|---|
| `Id` | uuid | khoá chính, `Guid.NewGuid()` (A1) |
| `PaymentId` | uuid | FK → `Payments`, **RESTRICT**, **UNIQUE** |
| `UserId` | uuid | FK → `Users`, **RESTRICT** — chủ hoá đơn |
| `Amount` | numeric(12,2) | **ảnh chụp** số tiền khách đã trả |
| `PdfUrl` | text NULL | đường dẫn file PDF; NULL = chưa sinh xong |
| `SentAt` | timestamptz NULL | mốc gửi email; NULL = chưa gửi được |
| `CreatedAt` | timestamptz | thời điểm phát hành |
| `UpdatedAt` | timestamptz NULL | A4 — `PdfUrl`/`SentAt` được ghi sau |

| Chỉ mục | Loại | Trả lời câu gì |
|---|---|---|
| `IX_Invoices_PaymentId` | **Duy nhất** | "một giao dịch, một hoá đơn" (§3.2); và đường tra của màn Admin dòng 35 |
| `IX_Invoices_UserId_CreatedAt` | Thường | "danh sách hoá đơn của tôi", mới nhất trước (dòng 32) — cùng khuôn A6 đã chốt cho `Notifications` |

**Cố ý không có cột `TicketId`** — xem §3.1. **Cố ý không có `IssuedAt`** và **cố ý không có cột trạng
thái gửi mail** — xem §4.

## 3. Hai quyết định lớn

### 3.1 🔴 Hoá đơn neo vào **LƯỢT THANH TOÁN**, không neo vào VÉ

Chốt này **không phải tự nghĩ ra** — nó đã được ghi trước trong repo.
`docs/32-csdl-huy-doi-ve.md` §3.1 lấy chính hoá đơn làm lý do để **không sửa vé cũ** khi duyệt đổi chuyến:

> *"Hoá đơn | `Payments.TicketId` đã trỏ vào vé đó và hoá đơn (US 7) đã xuất theo nó. Vé đổi chuyến
> nghĩa là hoá đơn cũ không còn khớp thứ nó mô tả."*

Và câu chữ của chính US 7 cũng nói rõ: hoá đơn là *"chứng từ **thanh toán**"* — chứng từ của tiền, không
phải của việc đi lại.

**Hệ quả — và đây là chỗ dễ hiểu sai nhất:** đổi chuyến làm **phát hành vé MỚI** (`docs/32` §3.1), nhưng
hoá đơn cũ **vẫn trỏ vào lượt thanh toán cũ và vẫn ĐÚNG**. Nó mô tả *"khách đã trả bao nhiêu cho giao
dịch nào"* — điều đó không đổi khi khách đổi chuyến. Neo vào vé thì mỗi lần đổi chuyến lại có một hoá
đơn *"không còn khớp thứ nó mô tả"*; neo vào thanh toán thì không bao giờ có ca đó.

⚠️ Điều này **không** mâu thuẫn với `docs/32` §3.1: chỗ đó nói hoá đơn **đã xuất theo `Payments.TicketId`**,
tức là *đi qua* lượt thanh toán để tới vé — đúng như bảng này. Vé vẫn tới được bằng **một bước nữa**
(`Payments.TicketId`), nên chép `TicketId` vào đây chỉ tạo ra **cột thứ hai có thể lệch** với cột thứ nhất.

### 3.2 🔴 Một giao dịch chỉ có **MỘT** hoá đơn — ràng buộc ở tầng CSDL

Không phải để cho gọn: repo này **đã biết** cổng thanh toán gọi callback trùng, và cả bảng `Payments`
được dựng quanh đúng giả định đó — `PaymentCode` có **chỉ mục duy nhất** để *"callback trùng tìm lại
đúng dòng đó để bỏ qua, không tạo dòng mới"*, cộng thêm `xmin` để chặn hai luồng cùng lật một dòng.

Task dòng 29 (*"Gửi hóa đơn qua email **tự động sau khi thanh toán**"*) chạy đúng trên nhánh thành công
đó. Nếu việc phát hành hoá đơn không có khoá thì **một callback gửi lại sẽ phát hành hoá đơn thứ hai**,
và khách nhận **hai chứng từ cho một lần trả tiền**.

Lý do thứ hai, độc lập: màn Admin dòng 35 tra hoá đơn bằng **mã giao dịch** (`Payments.PaymentCode`).
Một mã giao dịch trả về hai hoá đơn thì màn đó không có cách nào chọn đúng cái — cùng lập luận đã dùng
cho chỉ mục duy nhất `NotificationTemplates.Type`.

⚠️ **Chặn ở tầng CSDL, không để tầng service kiểm.** Hai callback chạy **song song** thì tầng service
kiểu *"đọc trước, ghi sau"* vẫn lọt khe giữa hai bước — cùng lý do chỉ mục duy nhất của `PaymentCode`
nằm ở CSDL. Tầng service vẫn phải **bắt lỗi vi phạm khoá duy nhất** và coi đó là *"hoá đơn đã tồn tại"*
(trả về hoá đơn cũ), **không** để nó nổi lên thành 500.

## 4. Bốn chỗ **cố ý KHÔNG** làm

| Không làm | Vì sao |
|---|---|
| **Không có số hoá đơn** (`InvoiceNumber`) | Mọi task trong khối dòng 27–37 tra cứu bằng **mã giao dịch** (`PaymentCode`, đã duy nhất) hoặc bằng `Id`. Thêm số hoá đơn là có **hai định danh cho cùng một thứ**, và chúng sẽ lệch nhau — cùng lập luận đã dùng để từ chối cột `Code` ở `NotificationTemplates`. ⚠️ **Nếu nhóm muốn số hoá đơn thật** (chứng từ tài chính) thì **phải thêm NGAY BÂY GIỜ** — xem §6.1, vì thêm sau phải lấp số cho dữ liệu đã có |
| **Không có `IssuedAt`** | Dòng này chỉ được tạo **lúc phát hành**, nên `CreatedAt` *chính là* mốc phát hành. Thêm cột nữa là hai cột nói cùng một thời điểm |
| **Không có cột trạng thái gửi mail** (`Issued/Sent/Failed`) | *"Gửi lại"* (dòng 31, 34) **chính là** cơ chế thử lại, nên chỉ cần một mốc `SentAt` là đủ trả lời *"đã gửi chưa"*. Cùng lối đã chốt cho thông báo — kênh đẩy hỏng thì **không ném lỗi và dòng vẫn còn** để còn đường gửi lại (`docs/33` §3, bảng hợp đồng dòng *"Không bao giờ ném lỗi"*). ⚠️ Đánh đổi đã biết: *"chưa thử gửi"* và *"gửi rồi nhưng hỏng"* **trông giống nhau** (đều `null`) — chấp nhận, vì cả hai đều xử lý bằng *bấm gửi lại*, và ai cần phân biệt thật thì đó là việc của `AuditLogs` |
| **Không có `EmailTo`** | Địa chỉ nhận là `Users.Email` — **một nguồn duy nhất**. Chép sang đây là tạo nguồn thứ hai sẽ cũ đi (khách đổi email thì cột này thành sai), đúng loại lỗi `DiscountRequest` đã tránh bằng cách không đưa trạng thái ưu đãi lên `Users` |

## 5. Cái gì **đã có sẵn** để dòng 28–37 dùng lại

Điều tra cả repo trước khi dựng bảng, và kết quả quan trọng cho sáu người: **phần khó nhất của US 7
đã được viết xong từ Sprint 3**, chỉ chưa nối vào luồng nào.

| Có sẵn | Ở đâu | Trạng thái |
|---|---|---|
| Vẽ PDF | `QuestPDF` 2026.9.1 + `ITicketPdfService`/`TicketPdfService` + `TicketPdfModel` | ✅ chạy, **16 test** |
| Gửi email | `MailKit` 4.18.1 + `IEmailSender`/`SmtpEmailSender` + `EmailMessage` + `EmailOptions` | ✅ chạy, **13 test**; đăng ký DI ở `Program.cs:445-448` |
| Ghép PDF vào email | `ITicketEmailService`/`TicketEmailService` | ✅ chạy, **7 test** |
| Font nhúng | `Assets/Roboto-Regular.ttf`, `Roboto-Bold.ttf` (giấy phép kèm theo) | ✅ |

🔴 **Nhưng cả ba đều có 0 lời gọi trong mã sản phẩm** — chỉ test gọi
(`docs/bao-cao-kiem-thu-cheo-mua-ve-nhan-qr.md` §76-78 và mục I10). Việc nối vào luồng là của **Hiếu**,
đã ghi ở `docs/31-pdf-ve-email.md` §5. **Không cần viết lại từ đầu.**

Ba cảnh báo rút ra từ đó, đều ảnh hưởng trực tiếp tới US 7:

1. ⚠️ **Đừng dựng đường vẽ PDF thứ hai.** `docs/31` §3.3 đã cảnh báo về một đường vẽ phân kỳ cho
   **vé**; hoá đơn cũng là PDF nên **dòng 28 phải dùng lại `QuestPDF` + font nhúng sẵn có**, không kéo
   thêm thư viện. Nếu hoá đơn cần vẽ kèm thông tin vé thì **dùng chung** phần vẽ vé.
2. 🔴 **Cấu hình `Email__*` chưa từng chạy thật.** `SmtpEmailSender` **fail-fast khi thiếu cấu hình**,
   và vì email hiện có 0 caller nên thiếu cấu hình hôm nay **không làm hỏng gì cả**. Ngay khi dòng 29
   lên, nó thành **đường gửi hoá đơn** — thiếu `Email__*` ở bản deploy là hoá đơn không tới khách.
   **Hoàng** cần biết trước khi deploy, không phải sau.
3. ⚠️ **Chưa có endpoint nào trả `application/pdf`.** Dòng 30 (*"API tra cứu và tải lại hóa đơn"*) là
   endpoint PDF **đầu tiên** của dự án — và theo luật 5, `docs/api-contract.md` phải có trước khi code.
   Hiện `docs/api-contract.md` **không có** mục hoá đơn nào (chỉ có hai câu nhắc chữ *"hoá đơn"* nằm
   trong ghi chú của `/payments` và `/vouchers`). Cùng cảnh báo đã ghi ở `docs/31` §3.3 cho vé PDF.

## 6. Sáu câu hỏi mở — nhóm chốt trước khi dòng 28–37 chạy

### 6.1 🔴 Có cần **số hoá đơn** thật không?

Bảng này cố ý không có (§4). Nếu nhóm muốn (chứng từ tài chính thường có), phải quyết **bây giờ**: thêm
sau thì phải **lấp số cho mọi dòng đã có**. Và cần biết trước một khó khăn thật: **số hoá đơn không
được nhảy cóc** (khác `PaymentCode` — mã đó sinh ngẫu nhiên, trùng thì sinh lại). Cấp số tuần tự an
toàn cần hoặc một `SEQUENCE` của PostgreSQL (đơn giản, nhưng số sẽ nhảy khi rollback) hoặc một bảng
đếm có khoá riêng (phức tạp hơn, không nhảy). Chọn cách nào cũng được, nhưng **phải chốt trước** — đây
là quyết định một chiều.

### 6.2 `Users.Email` **nullable** — dòng 29 gửi cho ai?

`User.Email` là `string?`. Khách không có email thì **gửi hoá đơn tự động cho ai?** Schema này không
giúp được: bảng chỉ ghi *"đã gửi chưa"*, không quyết định *"gửi cho ai"*. Cần chốt: bỏ qua im lặng, hay
buộc có email trước khi thanh toán, hay đẩy thông báo in-app *"hoá đơn đã sẵn sàng, hãy thêm email"*.
Đây là việc của dòng 29 (Hiếu) nhưng **nhóm nên biết** vì nó đụng luồng đăng ký.

### 6.3 ⚠️ Vé tháng không đi qua cổng thanh toán — **đúng thiết kế, KHÔNG phải lỗi cần sửa**

`Payments.TripId` là **NOT NULL** (`Payment.cs` §51: *"Chuyến được trả tiền — có từ lúc tạo
(`POST /payments` bắt buộc `tripId`)"*). Nghĩa là **không có lượt thanh toán nào cho `MonthlyPasses`**
— và vì hoá đơn neo vào thanh toán (§3.1), **US 7 hiện chỉ xuất được hoá đơn cho vé lượt**.

⚠️ **Đã kiểm lại toàn bộ 12 task của US 16 *"Đăng ký vé tháng"* (dòng 38–49 của sheet Sprint 5):
KHÔNG task nào nhắc tới thanh toán.** Luồng vé tháng theo kế hoạch đóng băng chỉ có: API đăng ký
(chọn tuyến, loại vé, thời hạn) → API gia hạn → BackgroundService hết hạn → API tra cứu → validate
trùng → 4 màn hình → 2 task test. **Không có bước cổng thanh toán.**

Vậy `TripId` NOT NULL **không phải chỗ sót** — nó khớp với kế hoạch: chỉ **vé lượt** đi qua cổng.
Nên đây **không phải việc cần làm**, cũng **không phải việc của Dăm**: `Entities/Payment.cs` và
`AppDbContext.Payment.cs` do **Phùng Duy Hoàng** viết (commit `7e753ab` *"feat(payments): chốt thanh
toán idempotent"*); US 6 / dòng 33 là task của Hoàng, Dăm chỉ dựng bảng đó **theo uỷ quyền
10/10/2026**. Thêm nữa, `POST /payments` bắt buộc `tripId` là **hình dạng API** — sửa nó phải sửa
`docs/api-contract.md` trước và báo người còn lại (luật 5).

🔴 **Nếu nhóm muốn khách trả tiền vé tháng qua cổng thì đó là một USER STORY MỚI, không phải một
fix** — và 24 story đã đóng băng, "không thêm, không sửa story nào". Việc đúng phải làm là **báo
nhóm** (§8 mục 7), không phải tự sửa `Payments`. Ghi ở đây để lần sau không ai đọc ra thành
*"còn nợ một fix"*.

### 6.4 Hoá đơn cho giao dịch **thất bại** hoặc vé **đã huỷ**?

Schema **không** ràng buộc `Payments.Status` — một dòng `Invoices` cạnh một `Payment` ở trạng thái
`Failed`/`Pending` vẫn chèn được. CSDL **không chặn được** ràng buộc xuyên bảng như vậy. Chốt: chỉ phát
hành khi `Payment.Status = Success` (kiểm ở tầng service, dòng 29), hay cần một ràng buộc mạnh hơn?
Với **huỷ vé** (`docs/32`): tiền đã thu, hoá đơn **vẫn đúng** và **không** bị xoá — cùng lý do đã dùng
để không sửa vé cũ (§3.1).

### 6.5 Kiểu chữ của `Amount` khi in ra PDF

`Amount` là `numeric(12,2)` nhưng đơn vị là **VND** — in ra thành `150000.00` là sai khuôn người Việt.
`docs/31` §3 đã chốt cho vé PDF: *"vi-VN price"* và **UTC+7 cố định**. Dòng 28 **phải theo đúng hai
chốt đó**, không tự nghĩ ra cách định dạng mới.

### 6.6 `Invoices` có phải tên đúng không?

Tên do bảng phân công ghi và nhánh này giữ nguyên (cùng lối `TicketValidations` ở `docs/34`,
`Subscriptions` ở `docs/37`). Nhưng nếu nhóm định đổi (`Invoice` số ít? `HoaDon`?) thì **đổi ngay bây
giờ** — sau khi có dữ liệu thì đắt hơn hẳn. A2 chốt entity số ít / bảng số nhiều, nên `Invoices` là
đúng khuôn.

## 7. Việc còn lại của người khác (khối dòng 28–37 — cả 10 task đều **chưa viết**)

| Dòng | Task | Ai | Cần biết gì từ bảng này |
|---|---|---|---|
| 28 | Service sinh hoá đơn PDF **theo mẫu** | Kiên (6đ) | Dùng lại `QuestPDF` + font nhúng, **đừng** dựng đường vẽ thứ hai (§5); ghi `PdfUrl` sau khi sinh |
| 29 | Gửi hoá đơn qua email **tự động sau khi thanh toán** | Hiếu (5đ) | 🔴 Phát hành hoá đơn **phải idempotent** (§3.2) — bắt lỗi vi phạm khoá duy nhất, coi là *"đã có"*, trả hoá đơn cũ; `Users.Email` nullable (§6.2) |
| 30 | API tra cứu và **tải lại** hoá đơn | Hiếu (4đ) | Endpoint PDF **đầu tiên** của dự án — `docs/api-contract.md` phải có trước (luật 5) |
| 31 | API **gửi lại** hoá đơn theo yêu cầu | Hoàng (3đ) | Ghi `SentAt` mỗi lượt gửi; không cần cột đếm |
| 32 | Màn **danh sách hoá đơn của tôi** | Hạnh (4đ) | Chạy trên `IX_Invoices_UserId_CreatedAt`, mới nhất trước |
| 33 | Component **xem trước PDF** trên web | Thịnh (4đ) | Như dòng 30 |
| 34 | Nút **tải hoá đơn và gửi lại qua email** | Băng (3đ) | Như dòng 31 |
| 35 | Màn **Admin tra cứu hoá đơn theo mã giao dịch** | Băng (4đ) | Tra bằng `Payments.PaymentCode` → `Payments` → `Invoices`; mã giao dịch trả về **đúng một** hoá đơn (§3.2) |
| 36 | Test API sinh hoá đơn và gửi email (xUnit) | Giàng A Vàng (4đ) | ⚠️ InMemory **KHÔNG cưỡng chế chỉ mục duy nhất lẫn khoá ngoại** — xem §9 |
| 37 | Kiểm thử chéo luồng hoá đơn trên môi trường online | Giàng A Vàng (3đ) | Cần `Email__*` thật (§5 cảnh báo 2) |

## 8. Việc cần báo nhóm (không phải việc code)

1. **A9 cần bổ sung ba dòng**: `Invoices` (nhánh này), `NotificationTemplates` và `Subscriptions`
   (nhánh `feature/10-…`) — lần thứ sáu A9 lệch thực tế (§1). A9 nằm trong `quy uoc.docx` **ngoài repo**.
2. **Dòng 27 coi như xong phần CSDL** sau khi migration được sinh — để không ai làm lại.
3. 🔴 **Sáu dòng 50–55 của sheet Sprint 4 là dòng MA** — US 17 đã xong từ Sprint 2 dưới tên
   `DiscountRequests` (§0). Bảng phân công cần xoá hoặc đánh dấu, nếu không thì Sprint 5/6 sẽ có người
   nhận lại một việc đã xong.
4. **Sáu câu hỏi ở §6** — trong đó §6.1 (số hoá đơn) là **quyết định một chiều**, chốt muộn là phải
   migrate kèm sửa dữ liệu.
7. ⚠️ **Câu hỏi phạm vi, không phải lỗi: vé tháng KHÔNG đi qua cổng thanh toán** (§6.3). 12/12 task
   của US 16 không có task nào về thanh toán, nên `Payments.TripId` NOT NULL **khớp kế hoạch**, và
   hoá đơn (US 7) vì thế chỉ áp dụng cho vé lượt. **Nếu nhóm muốn khách trả tiền vé tháng qua cổng
   thì đó là một USER STORY MỚI** — 24 story đã đóng băng nên phải do nhóm quyết. ⚠️ Việc này cũng
   **không phải việc của Dăm**: `Payment.cs` là file của **Hoàng**, và `POST /payments` bắt buộc
   `tripId` là hình dạng API (luật 5 — sửa contract trước). **Đừng sửa `Payments` vì mục này.**
5. **Cấu hình `Email__*` phải có trước khi US 7 lên bản deploy** (§5 cảnh báo 2) — hiện chưa ai chạy
   thử đường gửi email thật lần nào.
6. **Số thứ tự tài liệu đang bị chia cắt giữa ba nhánh chưa merge**: `docs/35`, `docs/36` (nhánh hoàn
   tiền), `docs/37` (nhánh thông báo), `docs/38` (nhánh này). Số không đụng nhau, nhưng từ `main` thì
   `docs/35`–`docs/38` **đều chưa đọc được**.
7. ⚠️ **Ba tài liệu đang trích một mục không tồn tại.** `docs/32` §1 và §9, `docs/34` §1 và §8 đều viết
   `docs/28` **§7.4** (và `docs/32` §9 viết `docs/28` §7.3) — nhưng `docs/28-csdl-ve-dien-tu.md` **§7
   không có mục con nào**: đó là danh sách đánh số 1–5. Chỗ đúng là **§7 mục 1** (`Tickets`) và **§7
   mục 4** (`Transactions`/`PaymentLogs`). Ngoài ra tên file cũng bị viết tắt thành `docs/28` ở cả ba
   tài liệu, trong khi tên thật là `docs/28-csdl-ve-dien-tu.md`.
   **Tài liệu này (`docs/38`) đã ghi đúng** — hai tài liệu kia **chưa sửa**, cố ý: sửa chúng là việc
   của một nhánh khác, không phải nhánh `Invoices`. Ai rảnh tay thì sửa một lượt cả `docs/32` §1/§9 và
   `docs/34` §1/§8 (cùng một lỗi, bốn chỗ).

## 9. Đã kiểm trong nhánh này

| Phép kiểm | Kết quả |
|---|---|
| `dotnet build backend/SmartBus.Api/SmartBus.Api.csproj` | ✅ 0 warning, 0 error |
| `dotnet test backend/SmartBus.Tests/SmartBus.Tests.csproj` | ✅ **1287/1287**, 0 failed, 0 skipped — chạy **hai lần**: trước migration (7p07s) và **sau migration** (7p16s), cùng xanh |
| 🔴 `has-pending-model-changes` (KHÔNG `--no-build`) | ✅ *"No changes have been made to the model since the last migration."* |
| 🔴 `migrations script <TicketValidations> <Invoices> --no-build` | ✅ **đúng một** `CREATE TABLE` + 2 `CREATE INDEX` + 1 `INSERT` lịch sử. **Không** `ALTER`, **không** `DROP`, **không** `UPDATE` |
| SQL: `IX_Invoices_PaymentId` | ✅ **`CREATE UNIQUE INDEX`** — có chữ `UNIQUE` |
| SQL: hai khoá ngoại | ✅ cả hai `ON DELETE RESTRICT`, không `CASCADE` nào |
| SQL: `Amount` / `PdfUrl` / `xmin` | ✅ `numeric(12,2)` · `text` · **không** có cột `xmin` |
| `AppDbContextModelSnapshot.cs` | ✅ **57 dòng thêm, 0 dòng xoá** — thuần thêm, không đụng bảng nào khác |
| `git grep "class Invoice"` trên `--all` | ✅ **không có kết quả** ở bất kỳ nhánh nào — không có caller nào để vỡ, và không có thiết kế cũ nào để đụng |
| `docs/32` §3.1 đọc lại | ✅ giả định *"hoá đơn xuất theo `Payments.TicketId`"* khớp với §3.1 của tài liệu này |
| Điều tra PDF/email toàn repo | ✅ §5 — có sẵn, 0 caller sản phẩm |

⚠️ **Bộ test chạy trên InMemory, mà InMemory KHÔNG cưỡng chế chỉ mục duy nhất lẫn khoá ngoại** — nên
test xanh **không** nói gì về `IX_Invoices_PaymentId` (ràng buộc quan trọng nhất của bảng, §3.2) hay
hai FK `Restrict` có tồn tại thật hay không. Hai thứ đó chỉ có thật ở PostgreSQL, tức là sau khi sinh
migration. **Đây là lý do dòng 36 (test) phải có một ca chạy trên PostgreSQL thật**, không chỉ InMemory.

### 9.1 ⚠️ `CLAUDE.md` bắt buộc chạy `impact` / `detect_changes` của GitNexus — máy này KHÔNG chạy được

Repo không có `.gitnexus/` và `gitnexus analyze` tự crash trên máy dựng nhánh — đã ghi ở `docs/34` §8.5,
cùng máy, cùng lỗi. Bù bằng đúng những gì làm được:

- **`git grep "class Invoice"` trên `--all`** — symbol chưa từng tồn tại, nên **không có caller nào**
  để mà vỡ. Đây là ca duy nhất `risk: UNKNOWN` không cần điều tra thêm: không phải *"không tìm thấy
  caller"* mà là *"symbol chưa hề tồn tại"*.
- **`AppDbContext.cs` là file dùng chung duy nhất bị sửa** — 1 dòng gọi `ConfigureInvoice` và 1 khai
  báo `partial void`, **thuần thêm**, không sửa dòng nào có sẵn. Một `ConfigureXxx` khai mà quên gọi
  (hoặc ngược lại) sẽ làm model lệch snapshot âm thầm — bắt được bằng
  `has-pending-model-changes` ở §10, và đó là lý do phép kiểm đó nằm trong Definition of Done.
- **Bảng mới chưa được bảng nào tham chiếu** — không có khoá ngoại nào trỏ **tới** `Invoices`, nên nó
  không nằm trên đường đi của bất kỳ luồng nào đang chạy.

**Ai có máy chạy được GitNexus nên chạy `impact` cho `AppDbContext`** — file dùng chung duy nhất bị sửa.

## 10. Migration — ✅ ĐÃ SINH VÀ ĐÃ KIỂM

```bash
ConnectionStrings__Default="<chuoi-ket-noi-gia — chi can dung dinh dang>" \
Jwt__Key="<chuoi-bat-ky-dai-it-nhat-32-byte>" \
Jwt__Issuer="SmartBus" \
Jwt__Audience="SmartBusClient" \
dotnet ef migrations add Sprint4_Invoices --project backend/SmartBus.Api
```

✅ **Đã chạy ngày 14/10/2026** (Vàng Thị Dăm) → `20261014171948_Sprint4_Invoices.cs` +
`.Designer.cs`. Tên `Sprint4_Invoices` theo đúng khuôn ba migration gần nhất của cùng sprint
(`Sprint4_Notifications`, `Sprint4_TicketValidations`).

⚠️ **Đừng tạo file `appsettings.Development.json` thật** — `scripts/setup-csdl.sh` bỏ qua file đã tồn
tại nên nó sẽ chặn luôn phần cài đặt thật về sau (cùng cảnh báo ở `docs/32` §8, `docs/33` §10,
`docs/37` §9).

Nhánh đang đứng trên `20261013144302_Sprint4_TicketValidations` (bản mới nhất của `main`), nên migration
mới **chỉ chứa một bảng** — không chạm bảng nào khác.

### Hai phép kiểm, không cần CSDL thật — ✅ cả hai xanh

| Phép kiểm | Kết quả thật |
|---|---|
| `dotnet ef migrations has-pending-model-changes --project backend/SmartBus.Api` | ✅ *"No changes have been made to the model since the last migration."* |
| `dotnet ef migrations script 20261013144302_Sprint4_TicketValidations 20261014171948_Sprint4_Invoices --project backend/SmartBus.Api --no-build` | ✅ **CHỈ** 1 `CREATE TABLE` + 2 `CREATE INDEX` + 1 `INSERT` vào `__EFMigrationsHistory`. Không `ALTER TABLE`, không `DROP`, không `UPDATE` |

⚠️ ⚠️ **`has-pending-model-changes` phải chạy KHÔNG có `--no-build`.** `AppDbContextModelSnapshot` nằm
trong assembly, mà `migrations add` build **trước** khi sinh file — nên assembly cũ chưa chứa migration
vừa sinh, và `--no-build` sẽ báo *"còn thay đổi"* một cách **sai**. (Bài học này đã ghi ở `docs/37` §9;
lặp lại ở đây vì nó làm mất thời gian của người chạy.)

### SQL sinh ra — đã đọc bằng mắt, khớp từng dòng với §2

```sql
CREATE TABLE "Invoices" (
    "Id" uuid NOT NULL,
    "PaymentId" uuid NOT NULL,
    "UserId" uuid NOT NULL,
    "Amount" numeric(12,2) NOT NULL,
    "PdfUrl" text,
    "SentAt" timestamp with time zone,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone,
    CONSTRAINT "PK_Invoices" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_Invoices_Payments_PaymentId" FOREIGN KEY ("PaymentId") REFERENCES "Payments" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_Invoices_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
);

CREATE UNIQUE INDEX "IX_Invoices_PaymentId" ON "Invoices" ("PaymentId");

CREATE INDEX "IX_Invoices_UserId_CreatedAt" ON "Invoices" ("UserId", "CreatedAt");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261014171948_Sprint4_Invoices', '10.0.12');
```

Đối chiếu từng điểm đã dặn ở bản trước của mục này — **tất cả đều đạt**:

- ✅ `Amount` = `numeric(12,2)` — **không** `double precision`/`real`;
- ✅ `PdfUrl` = **`text`** — không phải `character varying(n)`;
- ✅ `SentAt`, `UpdatedAt` = `timestamp with time zone` **nullable**; `CreatedAt` **NOT NULL**;
- 🔴 ✅ **`IX_Invoices_PaymentId` là `CREATE UNIQUE INDEX`** — ràng buộc một-hoá-đơn-một-giao-dịch
  **có thật trong CSDL** (§3.2), không phải chỉ nằm trong cấu hình EF;
- ✅ **cả hai khoá ngoại đều `ON DELETE RESTRICT`** — không có `CASCADE` nào;
- ✅ **không** có cột `xmin`;
- ✅ `AppDbContextModelSnapshot.cs` **57 dòng thêm, 0 dòng xoá**.

⚠️ Vẫn còn một thứ **chưa** kiểm được ở đây: SQL đúng **trên giấy**, nhưng chưa chạy lên PostgreSQL
thật (việc của người triển khai migration). Và như §9 đã nói, test InMemory **không** cưỡng chế được
chỉ mục duy nhất — nên ca kiểm chứng thật sự cho `IX_Invoices_PaymentId` là **một test chạy trên
PostgreSQL**, tức là dòng 36 (Giàng A Vàng).
