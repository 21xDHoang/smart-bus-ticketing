# CSDL hoàn tiền — Sprint 4: hai bảng `Refunds` và `RefundLogs`

> Viết cho các task đang chờ hai bảng này — **US 8** *"Là hệ thống, tôi muốn tự động xử lý hoàn tiền
> theo quy định khi giao dịch thanh toán thất bại hoặc vé bị hủy hợp lệ"*, sheet *Sprint 4*:
> **Kiên** (service hoàn tiền tự động, dòng 39) · **Hiếu** (API gọi cổng thanh toán để hoàn tiền —
> MoMo/VNPay/ZaloPay, dòng 40) · **Hoàng** (xử lý hoàn tiền thất bại + retry theo lịch, dòng 41) ·
> **Dăm** (API tra cứu trạng thái hoàn tiền của giao dịch, dòng 42) · **Kiên** (đối soát hoàn tiền
> hằng ngày + cảnh báo khi lệch số liệu, dòng 43) · **Hạnh** (màn theo dõi trạng thái hoàn tiền của
> hành khách, dòng 44) · **Thịnh** (thông báo kết quả hoàn tiền, dòng 45) · **Băng** (màn Admin quản
> lý yêu cầu hoàn tiền, dòng 46; component lọc và xuất danh sách, dòng 47) · **Giàng A Vàng** (test
> API hoàn tiền + retry, dòng 48–49).
>
> 🟢 **Trạng thái:** entity `Refund` + `RefundLog`, bốn enum (`RefundSource`, `RefundStatus`,
> `RefundLogResult`, `RefundLogTrigger`), cấu hình `AppDbContext.Refund.cs`, và migration
> `Sprint4_Refunds_RefundLogs` (Vàng Thị Dăm, task Sprint 4 dòng 38) — **đã sinh và đã soát, xem §8**.
> **Chưa merge, chưa áp lên CSDL chung**: bảng chỉ có thật sau khi PR được merge rồi chạy
> `database update` (thứ tự ở `docs/25-huong-dan-csdl-chung.md` §4). Trước bước đó, endpoint nào chạm
> chúng sẽ lỗi `relation "Refunds" does not exist`.

## 1. Hai bảng này KHÔNG nằm trong A9 — phải báo nhóm

A9 (danh sách **20 bảng đóng băng**, nguồn `quy uoc.docx`) **không có** `Refunds` lẫn `RefundLogs`.
Hai bảng vẫn được dựng trong nhánh này, vì có uỷ quyền trực tiếp:

| Nguồn | Nói gì |
|---|---|
| **Bảng phân công Sprint 4, dòng 38** | *"Migrate bảng Refunds, RefundLogs"* — Vàng Thị Dăm, ưu tiên **Cao**, 4 điểm |
| **A9** | **Không có** hai bảng này |
| **`docs/02-sprint-roadmap.md`** §2 | US 8 **có** ở **Sprint 4** (*"Thanh toán & Vé điện tử: 4, 6, 7, 8"*) — khớp bảng phân công, **không** có xung đột lịch như US 5 (`docs/32` §7.3) hay US 15 (`docs/34` §7.3) |
| Code | Trước nhánh này, repo **không** có entity / cấu hình / endpoint nào nhắc tới hai bảng này |

Đây là nhóm bảng thứ **TƯ** của dự án nằm ngoài A9 — sau `CancellationRequests` + `TicketChanges`
(`docs/32` §1), `Notifications` (`docs/33` §1) và `TicketValidations` (`docs/34` §1) — và là lần thứ
**TƯ** việc *"A9 lệch thực tế"* được nhắc. Cách xử lý giữ nguyên: **dựng theo phân công, rồi báo nhóm
bổ sung A9** (§7.1). Chủ CSDL không tự thêm bảng ngoài A9 mà im lặng.

## 2. Bảng `Refunds` — một KHOẢN HOÀN TIỀN

Một dòng cho **mỗi khoản hoàn**: ý định trả lại tiền cho khách, cộng trạng thái hiện tại của ý định
đó. Bảng **có bị sửa** (trạng thái lật, thử lại, đổi lịch).

| Cột | Kiểu | Ghi chú |
|---|---|---|
| `Id` | uuid | khoá chính, `Guid.NewGuid()` (A1) |
| `RefundCode` | varchar(64) | **UNIQUE** — mã khoản hoàn, khoá chống trùng lúc tạo + mã tra cứu |
| `Source` | varchar(30) | `PaymentFailure` / `TicketCancellation` |
| `PaymentId` | uuid | FK → `Payments`, `RESTRICT` — giao dịch gốc đã thu tiền. **Bắt buộc** ở cả hai nguồn |
| `CancellationRequestId` | uuid NULL | FK → `CancellationRequests`, `RESTRICT` — chỉ nhánh `TicketCancellation` |
| `TicketId` | uuid NULL | FK → `Tickets`, `RESTRICT` — **cột hiển thị**, null ở nhánh `PaymentFailure` |
| `UserId` | uuid | FK → `Users`, `RESTRICT` — người **nhận lại** tiền |
| `Amount` | numeric(12,2) | số tiền hoàn (A3: không `float`/`double` cho tiền) |
| `Status` | varchar(20) | `Pending` / `Succeeded` / `Failed` |
| `GatewayTransactionId` | varchar(50) NULL | mã giao dịch phía cổng của lượt hoàn **thành công** |
| `FailReason` | varchar(500) NULL | lý do lượt gọi **gần nhất** thất bại |
| `NextRetryAt` | timestamptz NULL | mốc job thử lại (dòng 41) sẽ gọi cổng; null ⟺ không nằm trong lịch |
| `ProcessedAt` | timestamptz NULL | mốc cổng chốt kết quả **cuối** |
| `CreatedAt` | timestamptz | A4 — có ở mọi bảng |
| `UpdatedAt` | timestamptz NULL | A4 — bảng **có** sửa nên bắt buộc |

**Cố ý KHÔNG có `IsDeleted`** (A4).

### 2.1 Vì sao là bảng RIÊNG, không phải vài cột trên `CancellationRequests` / `Payments`

`CancellationRequests` **đã** có `Status` (`Pending` / `Approved` / `Rejected`) và đã có
`CancellationFee`. Câu hỏi tự nhiên: sao không thêm một cột `RefundedAt` vào đó rồi thôi?

Vì **hai bảng trả lời hai câu hỏi khác nhau, và chúng trả lời lệch nhau được trong nhiều ngày**:

| | `CancellationRequests` | `Refunds` |
|---|---|---|
| Trả lời | *"Có cho huỷ không"* | *"Đã trả được tiền chưa"* |
| Ai ghi | quản lý bấm duyệt | cổng thanh toán trả lời |
| Ai là "người ra quyết định" | con người | một hệ thống bên ngoài |

Quản lý duyệt huỷ xong mà cổng hoàn tiền lỗi, rồi job thử lại ba lượt đều lỗi, rồi hết lịch: yêu cầu
huỷ đã `Approved` **từ lâu** trong khi khoản hoàn vẫn `Failed`. Gộp hai thứ vào **một** cột trạng thái
là bịt mất đúng khoảng lệch đó — và khoảng lệch đó chính là thứ màn Admin (dòng 46) sinh ra để nhìn.

Cùng lối lập luận đã ghi ở doc-comment `CancellationRequestStatus` (mục *"vì sao KHÔNG có trạng thái
đã hoàn tiền"*), và cùng lối `docs/32` §2 đã dùng để tách `CancellationRequests` khỏi `Tickets`.

Thêm ba lý do **không** gộp được vào `Payments`:

1. Nhánh `PaymentFailure` (dòng 39) sinh khoản hoàn từ một giao dịch **hỏng** — tức là `Payments.Status
   = 'Failed'` — nhưng tiền **vẫn** bị cổng giữ và **vẫn** phải hoàn. Ghi vào `Payments` là bắt một
   dòng mang hai trạng thái ngược nhau.
2. **Cổng trả lời chậm hơn giao dịch thu rất nhiều.** Khoản hoàn sống qua nhiều lượt gọi trong nhiều
   ngày; `Payments` là bản ghi của một lượt thu.
3. `Payments` **không** bị sửa sau khi chốt (đó là lý do nó có `xmin` chỉ để chặn callback trùng).
   Nhét trạng thái sống vào đó là mở lại một bảng vốn đã đóng.

### 2.2 Vì sao `Amount` LÀ cột, dù `CancellationFee` đã ghi *"số tiền hoàn suy ra được"*

Đây là chỗ dễ bị đọc thành **mâu thuẫn** với doc-comment `CancellationRequest.CancellationFee`, nên
ghi rõ: **không mâu thuẫn — hai con số khác nhau về bản chất.**

| | Là gì | Suy ra được không |
|---|---|---|
| `Ticket.Price - CancellationRequest.CancellationFee` | số tiền **phải** hoàn theo quy định hiện hành | **được** — vẫn là công thức suy diễn, vẫn không có cột nào giữ nó |
| `Refund.Amount` | số tiền hệ thống **đã thực sự gửi lên cổng** | **không** — xem dưới |

Ba lý do `Amount` buộc phải là cột:

1. 🔴 **Nhánh `PaymentFailure` không có `Ticket.Price` nào để trừ.** Khoản hoàn loại đó sinh ra từ số
   tiền **cổng đã giữ**, không từ một công thức trên vé. Bắt suy ra là bỏ luôn nhánh mà US 8 nêu ra
   **đầu tiên** trong chính câu story.
2. 🔴 **Đối soát (dòng 43) phải so ĐÚNG con số đã gửi đi.** Suy lại rồi đem so nghĩa là so một con số
   tính bằng quy định **hôm nay** với một giao dịch đã gửi bằng quy định **hôm đó**. Nhà xe sửa mức
   phí huỷ ở giữa là mọi phép đối soát cũ báo lệch **giả** — và một hệ thống cảnh báo lệch giả thì
   sớm muộn cũng bị tắt tiếng.
3. **Số tiền hoàn là thứ đã ra khỏi tài khoản nhà xe.** Đó là dòng tiền, không phải dòng suy diễn.
   Cùng lý do `Payments.Amount` là cột chứ không phải `Ticket.Price` nhìn sang.

### 2.3 Bất biến `Source` ⟺ các cột nullable — việc của service

Đây là **bất biến quan trọng nhất** của bảng, phải đọc trước khi viết truy vấn:

| `Source` | `CancellationRequestId` | `TicketId` |
|---|---|---|
| `PaymentFailure` | **null** | **null** (thường) |
| `TicketCancellation` | **khác null** (bắt buộc) | **khác null** (bắt buộc) |

- **Vì sao `TicketId` nullable:** vé chỉ được phát hành **sau khi** tiền về thành công
  (`docs/api-contract.md`, mục *"Phát hành vé sau khi thanh toán"*), nên giao dịch hỏng thì **chưa có
  vé** để trỏ vào. Bắt NOT NULL sẽ buộc service bỏ luôn khoản hoàn của nhánh `PaymentFailure`.
- ⚠️ **`TicketId IS NULL` KHÔNG suy ra được `Source`** — cột này là **cột hiển thị** (mã vé, tuyến,
  giờ chạy cạnh số tiền ở màn Admin dòng 46 và màn hành khách dòng 44), không phải khoá tra cứu. Khoá
  tra cứu là `RefundCode` và `PaymentId`.
- ⚠️ **Không diễn đạt được bằng CHECK constraint** vì `Source` lưu dạng **chuỗi** (A3) — đổi tên giá
  trị trong enum là CHECK constraint ở CSDL lặng lẽ sai. Cùng đúng lối `docs/34` §4.3 mục 1.

### 2.4 Vì sao KHÔNG có cột `RetryCount`

Frontend đã có trường `retryCount` (`refundAdminApi.ts`, màn Admin dòng 46 — **đã merge vào `main`**),
nên cám dỗ là thêm hẳn một cột đếm vào bảng. **Cố ý không**, vì:

1. **Đếm được từ `RefundLogs`:** số lượt thử lại = số dòng log có `Trigger != 'Initial'`. Đó là phép
   đếm trên một chỉ mục đã có (`IX_RefundLogs_RefundId_CreatedAt`), không phải phép quét bảng.
2. **Một cột đếm là nguồn sự thật thứ hai cho cùng một câu hỏi.** Mọi lần service ghi log mà quên
   tăng cột, hai con số lệch nhau — và lúc đó **không có cách nào biết bên nào đúng**. Bảng nhật ký là
   bằng chứng; cột đếm chỉ là bản chép lại của bằng chứng, kèm rủi ro chép sai.
3. **API tự tính rồi trả ra `retryCount`** cho frontend — hình dạng response không đổi, chỉ khác là
   con số được **đếm** thay vì được **lưu**.

Cùng lối lập luận đã dùng để **không** có cột "số lượt giữ chỗ" ở `SeatHolds` (`docs/28`), và **không**
có cột số tiền hoàn ở `CancellationRequests`.

### 2.5 Vì sao KHÔNG có cột `MethodCode`

`refundAdminApi.ts` có trường `methodName` (*"MoMo"* / *"VNPay"* / *"ZaloPay"* / *"Thẻ ngân hàng"*).
Cột đó **không** thuộc bảng này: phương thức hoàn tiền **luôn** là phương thức của giao dịch gốc —
hoàn qua đúng kênh đã thu, không có chuyện khách trả MoMo mà nhận lại VNPay. Giá trị đã nằm ở
`Payments.MethodCode` (enum `PaymentProviderCodes`: `MoMo` / `VNPay` / `ZaloPay` / `BankCard`) và
`Payments` là bảng **ghi một lần, không sửa**, nên chép sang đây chỉ tạo thêm một bản có thể lệch.

API lấy `methodName` bằng join sang `Payments` theo `PaymentId` — cột `PaymentId` đã có chỉ mục
(`IX_Refunds_PaymentId`), nên đây là phép join một dòng.

### 2.6 Vì sao `NextRetryAt` là cột riêng, không suy ra từ `Status` + `CreatedAt`

Lịch thử lại là **chính sách sửa được** (giãn cách tăng dần, số lần tối đa), không phải hằng số của hệ
thống. Suy ra lúc truy vấn nghĩa là **chép chính sách vào câu truy vấn của job** — đổi chính sách là
phải sửa và triển khai lại job, thay vì đổi một dòng dữ liệu. Cùng lối lập luận đã khiến
`CancellationRequest.CancellationFee` và `TicketValidation.ScannedAt` phải là cột.

Ba trạng thái của cột, phân biệt được **bằng mắt**:

| `Status` | `NextRetryAt` | Nghĩa |
|---|---|---|
| `Failed` | có mốc | **đang chờ tới hạn** — job (dòng 41) sẽ gọi lại |
| `Failed` | null | **đã hết lịch** — vẫn thử được **bằng tay** ở màn Admin (dòng 46), sinh lượt `ManualRetry` |
| `Pending` / `Succeeded` | (bỏ qua) | không nằm trong lịch |

⚠️ Đây là lý do **không** cần thêm giá trị enum kiểu "đã bỏ thử lại": dòng hết lịch vẫn là `Failed` với
`NextRetryAt = null`, và **vẫn** phải giữ đường thử tay. Thêm một giá trị enum là đổi hợp đồng với hai
màn frontend đã merge.

### 2.7 Vì sao `FailReason` chép lại thông điệp đã có ở `RefundLogs.Message`

Vì đây là **dòng tiêu đề của trạng thái hiện tại**, không phải lịch sử: mọi danh sách (màn Admin dòng
46, màn hành khách dòng 44) đều hiện nó, và bắt mỗi danh sách join sang bảng nhật ký rồi lấy dòng mới
nhất là trả giá cho một cột chép được. Cùng lối `Payments.Message`.

⚠️ **Bất biến kèm theo (service giữ):** `FailReason` null khi `Status` là `Pending` hoặc `Succeeded`;
**chỉ** khác null khi `Status` là `Failed`. Cột này là *"lý do của lượt cuối"*, không phải lịch sử —
lịch sử đầy đủ nằm ở `RefundLogs`.

## 3. Bảng `RefundLogs` — nhật ký TỪNG LƯỢT GỌI CỔNG

Một dòng cho **mỗi lượt** hệ thống gọi cổng để hoàn tiền — **kể cả lượt cổng không trả lời**. Đây là
bảng **chỉ ghi thêm**.

| Cột | Kiểu | Ghi chú |
|---|---|---|
| `Id` | uuid | khoá chính, `Guid.NewGuid()` (A1) |
| `RefundId` | uuid | FK → `Refunds`, `RESTRICT` — khoản hoàn mà lượt gọi này thuộc về |
| `Result` | varchar(20) | `Unresolved` / `Failed` / `Succeeded` |
| `Trigger` | varchar(20) | `Initial` / `ScheduledRetry` / `ManualRetry` |
| `GatewayTransactionId` | varchar(50) NULL | mã giao dịch phía cổng của **riêng lượt này** |
| `Message` | varchar(500) NULL | thông điệp cổng trả về, **nguyên văn** |
| `CreatedAt` | timestamptz | thời điểm gọi cổng (A4) |

**Cố ý KHÔNG có `UpdatedAt`** — A4 xếp bảng chỉ-ghi-thêm vào nhóm `AuditLogs` / `Notifications` /
`TicketValidations`. Một lượt gọi cổng đã xảy ra thì không có gì để sửa; sửa được nghĩa là nhật ký mất
giá trị.

**Cố ý KHÔNG có `IsDeleted`** (A4).

**Cố ý KHÔNG có `xmin`** — xem §4.3.

### 3.1 Vì sao cần bảng log khi `Refunds.Status` đã nói lên trạng thái

Cột `Status` chỉ giữ trạng thái **CUỐI**, còn câu hỏi của nghiệp vụ là **QUÁ TRÌNH**: *"khoản này đã
gọi cổng mấy lần"*, *"lần thứ ba cổng nói gì mà vẫn hỏng"*, *"lượt nào là do người bấm tay"*. Ghi đè
lên một cột thì trả lời được trạng thái hiện tại, **không** trả lời được lịch sử. Cùng lối
`SeatHolds` / `SeatHoldLogs`.

Bảng này phục vụ **bốn** task:

| Task | Câu hỏi | `Refunds.Status` có trả lời được không |
|---|---|---|
| Dòng 41 — retry theo lịch | *Khoản này đã thử mấy lần rồi?* | Không — cột chỉ nói `Failed`, không nói lần thứ mấy |
| Dòng 42 — tra cứu trạng thái | *Vì sao khoản này chưa xong?* | Không — `Pending` không phân biệt "chưa gọi" với "cổng không trả lời" |
| Dòng 43 — đối soát hằng ngày | *Lượt gọi nào không có câu trả lời?* | **Không — và đây là ca nguy hiểm nhất, xem §3.2** |
| Dòng 46 — màn Admin | *Ai/cái gì đã làm khoản này hỏng?* | Không — chỉ có lý do của lượt cuối |

### 3.2 🔴 Giá trị `Unresolved` — và vì sao nó là giá trị **0** của enum

`RefundLogResult` có **ba** giá trị, đúng ba thứ cổng có thể trả lời: đã hoàn xong, từ chối/lỗi, hoặc
**không trả lời** (timeout, đứt kết nối giữa chừng). Giá trị thứ ba không phải cho đủ bộ — nó là ca có
thật của **mọi** cổng thanh toán, và là ca duy nhất mà hệ thống **không được phép đoán**.

Với tiền, việc chọn giá trị 0 là một quyết định về **cách hỏng**, không phải về tính đọc được:

| Nếu giá trị 0 là… | Một dòng log ghi thiếu kết quả sẽ… | Hậu quả |
|---|---|---|
| `Succeeded` | tự nhận là đã hoàn tiền | **im lặng** — sổ sách ghi đã trả trong khi tiền chưa đi |
| `Failed` | tự nhận là thất bại | job thử lại (dòng 41) gọi cổng lần nữa cho một khoản **có thể đã hoàn** → **hoàn hai lần** |
| **`Unresolved`** | tự nhận là **chưa rõ** | khoản nằm nguyên ở `Pending`, không lượt gọi tự động nào chạm tới, và nó **lộ ra** ở bước đối soát (dòng 43) |

Đã soát **cả 25 enum** của dự án: đây là enum **duy nhất** đặt một giá trị **"chưa có câu trả lời"** ở
vị trí 0. `TicketValidationResult` cũng chọn kiểu hỏng ồn ào (0 = `NotFound`), nhưng ở đó `NotFound`
là một **lý do từ chối đã biết** — *"vé này không tồn tại"* — chứ không phải một trạng thái **chưa
biết**. Ghi lại đây để người sau không "sửa cho đều" rồi vô tình tạo ra đường hoàn hai lần.

⚠️ **`Unresolved` KHÔNG lật `Refunds.Status`.** Khoản hoàn **giữ nguyên `Pending`** và **không** được
xếp vào lịch thử lại — gọi lại một lượt chưa rõ kết quả là đúng đường dẫn tới hoàn hai lần. Việc tra
cho ra nhẽ thuộc bước **đối soát** (dòng 43): đối chiếu với sao kê cổng rồi mới chốt trạng thái.

### 3.3 Vì sao `RefundLogs` KHÔNG có `xmin` (còn `Refunds` thì có)

Khoá phiên bản dòng chỉ có nghĩa với dòng **bị sửa**. `RefundLogs` là bảng **chỉ ghi thêm** (A4),
không có nhánh đua nào giữa hai lượt **SỬA cùng một dòng** để mà chặn.

Nhánh đua **thật** của nghiệp vụ — hai nơi cùng gọi cổng cho **một** khoản hoàn (job dòng 41 chạy
trùng, hoặc job + nút "Thử lại" của Admin dòng 46, hoặc hai bản app cùng chạy job) — được chặn ở
**chính dòng `Refunds`**, vì dòng đó mới là thứ bị tranh nhau ghi. Cùng lối lập luận đã ghi ở
`AppDbContext.Notification.cs` và `AppDbContext.TicketValidation.cs`.

⚠️ **Hệ quả phải biết:** nhật ký **có thể** có nhiều dòng cho một lượt gọi nếu tầng service ghi log
hai lần. Đó là lý do phép đếm `retryCount` (§2.4) phải đếm theo `Trigger` — một cột **có nghĩa nghiệp
vụ** — chứ không đếm thô số dòng.

### 3.4 Vì sao `Trigger` KHÔNG ghi **ai** bấm "Thử lại"

`POST /admin/refunds/{id}/retry` là một endpoint **quản trị**, nên nó **đã** được
`AuditLogMiddleware` ghi vào bảng `AuditLogs` kèm **người thực hiện** (US 23). Chép thêm một cột
người-vừa-bấm vào đây là tạo **nguồn sự thật thứ hai** cho cùng một câu hỏi — hai chỗ trả lời *"ai
bấm"* thì sớm muộn cũng lệch nhau. Cùng đúng lối `Notification` đã dùng để không nhân bản dữ liệu đã
có ở bảng khác.

Hai giá trị còn lại (`Initial`, `ScheduledRetry`) **không có** người thực hiện nào để ghi — chúng do
hệ thống tự chạy.

## 4. Chống trùng & chống đua — ở tầng CSDL

### 4.1 🔴 `PaymentId` + `Status` — chặn hoàn hai lần cho một giao dịch

```sql
CREATE UNIQUE INDEX "IX_Refunds_PaymentId_Active"
    ON "Refunds" ("PaymentId")
    WHERE "Status" IN ('Pending', 'Succeeded');
```

**Đây là chốt quan trọng nhất của cả hai bảng.** Đây là lỗi **tiền**: hoàn hai lần cho một giao dịch
nghĩa là nhà xe mất tiền thật, và **không sửa được bằng cách sửa dữ liệu**.

**Vì sao chặn ở CSDL chứ không giao hết cho service:** hai luồng cùng chạy là chuyện xảy ra được —
job thử lại (dòng 41) và nút "Thử lại" của Admin (dòng 46) cùng nhặt một khoản; hoặc service hoàn tiền
(dòng 39) chạy hai lần cho cùng một callback. Cả hai đều **đọc thấy "chưa có khoản hoàn nào"** rồi
cùng `INSERT`. Kiểm tra ở tầng service là một phép đọc-rồi-ghi, và phép đọc-rồi-ghi **luôn** thua
trong một cuộc đua.

**Vì sao điều kiện là `IN ('Pending', 'Succeeded')` chứ không phải `= 'Pending'`:** một lượt thử lại
làm khoản hoàn **rời khỏi** `Pending` rồi có thể **quay lại** (`Failed → Pending`). Nếu điều kiện chỉ
bắt `'Pending'`, khe hở giữa hai lượt là đúng lúc dòng mang `'Failed'` — và một service gọi hai lần sẽ
tạo được khoản hoàn thứ hai cho cùng giao dịch. Gộp `'Succeeded'` vào điều kiện thì **sau khi tiền đã
đi**, không khoản hoàn nào khác được tạo cho giao dịch đó nữa, kể cả khi dòng cũ bị ai đó sửa hỏng.

**Vì sao là chỉ mục ĐIỀU KIỆN chứ không phải UNIQUE thường trên `PaymentId`:** một giao dịch **hoàn
toàn có thể** có nhiều khoản hoàn theo thời gian — khoản đầu thất bại, hết lịch thử lại, bị bỏ, rồi
phát sinh khoản mới. UNIQUE thường sẽ chặn **vĩnh viễn** khoản thứ hai, tức là chặn luôn **con đường
duy nhất** để trả tiền cho khách trong ca đó.

⚠️ **Khoản `Failed` bị bỏ rồi tạo lại được** nghĩa là **luôn** tồn tại khả năng hai dòng cùng trỏ một
giao dịch (một `Failed` cũ + một Active mới). Hệ quả cho người viết truy vấn: câu *"giao dịch này đã
hoàn chưa"* phải **lọc theo `Status`**, **đừng** đếm số dòng.

### 4.2 🔴 Hai nơi cùng gọi cổng cho một khoản — `xmin` + giành dòng trước khi gọi

`Refunds` có khoá phiên bản dòng:

```csharp
e.Property<uint>("xmin").HasColumnName("xmin").IsRowVersion();
```

`xmin` là cột **hệ thống** của PostgreSQL — **KHÔNG** sinh cột mới trong migration. Cùng lối đã dùng ở
`Payments` (callback trùng), `Vouchers` (hai lượt áp cùng lúc), `CancellationRequests` và
`TicketChanges` (hai admin cùng duyệt).

⚠️ **Cơ chế này chỉ chặn được lượt ghi THỨ HAI; nó KHÔNG tự làm cho lượt gọi cổng trở nên an toàn.**
Đây là chỗ dễ hiểu sai nhất của cả tài liệu này, nên nói thẳng:

> **Job thử lại (dòng 41) PHẢI giành dòng bằng một lượt UPDATE khoá phiên bản — đọc, sửa trạng thái,
> `SaveChanges()` — TRƯỚC khi gọi cổng.** Lượt thua nhận `DbUpdateConcurrencyException` và **thoát
> ra mà chưa gọi gì**. Nếu service gọi cổng **trước** rồi mới ghi trạng thái thì `xmin` chỉ phát hiện
> ra cuộc đua **sau khi** cả hai lời gọi đã đi — tức là đã hoàn hai lần rồi mới biết.

Khác `SeatHoldLogs`: ở đó khoá chống trùng là cặp `(SeatHoldId, Action)` vì hai lượt cùng một hành
động là **cùng một sự kiện**. Ở đây mỗi lượt thử lại là một sự kiện **khác nhau có thật** — thử lần 1
hỏng, thử lần 2 hỏng, thử lần 3 thành công — nên **không** được gộp chúng lại bằng một khoá unique.
Chốt phải là *"một lượt gọi tại một thời điểm"*, và đó là việc của `xmin` cộng với kỷ luật giành dòng.

### 4.3 ⚠️ Bốn ràng buộc KHÔNG diễn đạt được bằng chỉ mục — việc của service

1. **`CancellationRequestId` khác null ⟺ `Source = 'TicketCancellation'`** (§2.3). `Source` lưu dạng
   **chuỗi** (A3) nên không viết được CHECK constraint so sánh nó với hằng số enum một cách bền vững.
2. **`FailReason` null ⟺ `Status != 'Failed'`** (§2.7). Cùng lý do trên.
3. **`NextRetryAt` chỉ khác null khi `Status = 'Failed'`** (§2.6). Cùng lý do trên.
4. **"Một yêu cầu huỷ đã duyệt chỉ sinh MỘT khoản hoàn"** (§4.4) — và **"một lượt gọi cổng tại một thời
   điểm cho một khoản hoàn"** (§4.2), vốn là việc của service chứ không phải của chỉ mục.

Cả bốn đều là **bất biến ở tầng nghiệp vụ**, không phải ràng buộc CSDL. Ai viết service (dòng 39, 41)
phải tự giữ, và **Giàng A Vàng** (dòng 48) nên có test cho từng cái.

### 4.4 Vì sao KHÔNG có chỉ mục UNIQUE riêng trên `CancellationRequestId`

*"Một yêu cầu huỷ chỉ được sinh một khoản hoàn"* **cũng** là luật thật — nhưng nó **không** cần chốt
riêng, vì khoản hoàn của một yêu cầu huỷ **luôn** trỏ đúng giao dịch của chiếc vé đó, nên chỉ mục
`IX_Refunds_PaymentId_Active` ở §4.1 đã chặn trùng cho **cả hai** nguồn (`PaymentFailure` **lẫn**
`TicketCancellation`). Thêm một chỉ mục UNIQUE nữa là dựng **chốt thứ hai cho cùng một lỗi**, mà lại
chỉ chặn được một nửa số ca.

Ngược lại, chốt ở §4.1 còn mạnh hơn: nó chặn cả ca *"hai yêu cầu huỷ khác nhau trên cùng một vé"* —
ca mà một chỉ mục trên `CancellationRequestId` **không** bắt được, vì hai yêu cầu là hai giá trị khác
nhau.

⚠️ **Bộ test chạy trên provider InMemory, mà InMemory KHÔNG cưỡng chế unique index lẫn FK.** Nên bộ
test xanh **không** nói gì về việc chỉ mục §4.1 có tồn tại hay không, và **cũng** không nói gì về
`RESTRICT` của bốn khoá ngoại. Các chốt đó chỉ tồn tại thật ở PostgreSQL — đây là lý do §8 đáng đọc.

## 5. Chỉ mục đã đánh — dùng đúng cái có sẵn

### 5.1 Trên `Refunds`

| Truy vấn | Chỉ mục |
|---|---|
| Tra khoản hoàn theo mã (`RefundCode`) | `IX_Refunds_RefundCode` (**UNIQUE**) |
| Chặn hoàn hai lần cho một giao dịch (§4.1) | `IX_Refunds_PaymentId_Active` (**UNIQUE**, điều kiện) |
| *"Giao dịch này từng bị hoàn chưa"* (dòng 43) — **và** khoá ngoại `PaymentId` | `IX_Refunds_PaymentId` |
| *"Hoàn tiền của tôi"* của màn hành khách (dòng 44) — **và** khoá ngoại `UserId` | `IX_Refunds_UserId` |
| Hàng đợi màn Admin: *"đang chờ xử lý, mới nhất trước"* (dòng 46–47) | `IX_Refunds_Status_CreatedAt` |
| Job thử lại: `Status = 'Failed' AND NextRetryAt <= now()` (dòng 41) | `IX_Refunds_Status_NextRetryAt` |
| Khoá ngoại `CancellationRequestId` | EF tự sinh |
| Khoá ngoại `TicketId` | EF tự sinh |

**Vì sao `IX_Refunds_PaymentId` KHÔNG thừa** dù chỉ mục điều kiện ở trên cũng mở đầu bằng `PaymentId`
— đúng bài học đã ghi ở `docs/28` §4 cho `IX_Tickets_TripId` và lặp lại ở
`AppDbContext.CancellationRequest.cs`. PostgreSQL chỉ dùng được một **partial index** khi truy vấn
**chứng minh** được điều kiện của nó; câu hỏi *"giao dịch này từng bị hoàn chưa"* **không** nói gì về
`Status` nên không thoả, và bộ lập kế hoạch sẽ bỏ qua chỉ mục kia.

⚠️ **Vì sao phải gọi `HasIndex` kèm TÊN:** EF nhận diện chỉ mục theo **danh sách cột**, nên lần gọi
thứ hai cho cùng một cột sẽ trả về **đúng cái builder của lần gọi đầu** (đã unique + có điều kiện)
chứ không tạo chỉ mục thứ hai — **im lặng, không báo lỗi**. Có tên riêng thì EF coi là hai chỉ mục
khác nhau và sinh đủ cả hai. Đã sập bẫy này một lần ở `CancellationRequests`; ghi lại đây cho người
sau.

**Vì sao `IX_Refunds_Status_CreatedAt` và `IX_Refunds_Status_NextRetryAt` KHÔNG gộp thành một chỉ mục
ba cột:** hai câu truy vấn khác nhau về bản chất — một câu hỏi *"khoản nào mới nhất"* cho màn quản trị,
một câu hỏi *"khoản nào tới hạn gọi lại"* cho job. `CreatedAt` và `NextRetryAt` **không bao giờ** được
sắp cùng nhau, nên gộp lại chỉ làm chỉ mục phình thêm một cột mà không phục vụ trọn câu nào; và job —
câu chạy dày nhất trong hai — phải quét qua cột không liên quan.

### 5.2 Trên `RefundLogs`

| Truy vấn | Chỉ mục |
|---|---|
| Dựng lại diễn biến các lượt gọi cổng của **một** khoản hoàn, cũ nhất trước (dòng 42, 43, 46) | `IX_RefundLogs_RefundId_CreatedAt` |
| Khoá ngoại `RefundId` | Cùng chỉ mục trên — EF thấy cột đã có chỉ mục nên không sinh thêm chỉ mục FK trùng |

⚠️ **Cố ý KHÔNG khai chỉ mục cho `GatewayTransactionId`** dù bước đối soát (dòng 43) tra ngược từ mã
giao dịch phía cổng: bước đó đối chiếu theo **LÔ** — đọc sao kê cổng rồi đối với các khoản hoàn trong
ngày qua chỉ mục `IX_Refunds_Status_CreatedAt`, chứ không tra từng mã một. Thêm chỉ mục ở đây là trả
giá ghi cho **mọi** lượt gọi cổng để phục vụ một truy vấn **chưa tồn tại**. Ai viết bước đối soát mà
cần tra ngược từng mã thì nhắn Dăm.

⚠️ **Cố ý KHÔNG khai chỉ mục cho `CancellationRequestId` và `TicketId` trên `Refunds`:** chưa có truy
vấn nào hỏi *"yêu cầu huỷ này sinh khoản hoàn nào"* hay *"vé này hoàn bao nhiêu lần"* — hai cột đó là
**cột hiển thị** (màn Admin join lấy mã vé / tuyến / giờ chạy), nên chỉ mục khoá ngoại do EF tự sinh
là đủ. Đây là bảng gắn với **dòng tiền**, thêm chỉ mục là trả giá ghi cho **mọi** lượt thử lại — chỉ
thêm khi có câu truy vấn thật. Ai cần thì nhắn Dăm.

## 6. Việc còn lại của người khác

- **Kiên** (dòng 39) — service hoàn tiền tự động. Sinh `RefundCode` (**duy nhất**), chọn `Source`,
  `Amount` và ghi rõ `'Pending'`. ⚠️ Giữ **đủ bốn bất biến** ở §4.3 — đặc biệt
  `CancellationRequestId` **chỉ** khác null ở nhánh `TicketCancellation` (§2.3). ⚠️ `Amount` là số
  tiền **sẽ gửi lên cổng**, không phải một con số tính lại lúc đọc (§2.2). Bắt lỗi unique của §4.1 và
  coi *"đã có khoản hoàn rồi"* là **thành công** (idempotent), **đừng** để nó thành 500.
- **Hiếu** (dòng 40) — API gọi cổng hoàn tiền (MoMo/VNPay/ZaloPay). ⚠️ Phương thức hoàn **luôn** là
  phương thức của giao dịch gốc — đọc `Payments.MethodCode`, **đừng** nhận tham số phương thức từ
  client (§2.5). Ghi **một dòng `RefundLogs` cho MỖI lượt gọi**, kể cả lượt hỏng và lượt **không có
  câu trả lời** (`Unresolved` — §3.2). Chốt hình dạng endpoint trong `docs/api-contract.md` **trước
  khi code** (⛔ luật 5) — hợp đồng hiện **chưa có** mục hoàn tiền (§7.3).
- **Hoàng** (dòng 41) — job thử lại theo lịch. 🔴 **Giành dòng bằng `xmin` TRƯỚC khi gọi cổng** (§4.2)
  — đây là chỗ dễ sai nhất và sai thì mất tiền thật. Ghi `NextRetryAt` theo chính sách (§2.6); hết
  lịch thì để `null`, **đừng** thêm giá trị enum mới. ⚠️ **Đừng** xếp lịch cho khoản có lượt cuối là
  `Unresolved` (§3.2).
- **Dăm** (dòng 42) — API tra cứu trạng thái hoàn tiền của giao dịch. Đường tra là `RefundCode` hoặc
  `PaymentId` (§2.3), **không** phải `TicketId`. ✅ **Đã làm** — nhánh `feature/8-api-tra-cuu-hoan-tien`,
  viết đầy đủ ở **`docs/36-api-tra-cuu-hoan-tien.md`**; mục hoàn tiền của `docs/api-contract.md` cũng
  do nhánh đó bổ sung, nên câu *"hợp đồng chưa có mục hoàn tiền"* ở §7.3 dưới đây **đã hết đúng**.
- **Kiên** (dòng 43) — đối soát hoàn tiền hằng ngày. 🔴 Đây là **nơi duy nhất** xử lý được các khoản
  kẹt ở `Pending` vì lượt gọi `Unresolved` (§3.2), và là nơi phát hiện *"mã giao dịch của lượt cuối"
  khác *"mã của lượt đã trả tiền"* — xem doc-comment `Refund.GatewayTransactionId`. So **đúng con số
  trong `Refund.Amount`** với sao kê (§2.2); **đừng** tính lại từ `Ticket.Price`.
- **Hạnh** (dòng 44) — màn theo dõi trạng thái hoàn tiền của hành khách. `myRefundApi.ts` đã có sẵn
  (nhánh `feature/8-man-hinh-theo-doi-hoan-tien`) với `USE_MOCK_DATA = true`; lọc theo `UserId` với
  chỉ mục `IX_Refunds_UserId`. Tập trạng thái để vẽ là **đúng ba** giá trị ở §2/§3.
- **Thịnh** (dòng 45) — thông báo kết quả hoàn tiền. ⚠️ **A8.5 chốt: in-app qua SignalR + bảng
  `Notifications`, KHÔNG email** — xem §7.4. Nhãn tiếng Việt đã có sẵn ở `REFUND_STATUS_META`.
  ⚠️ **Đừng** đưa số tiền vào thân thông báo nếu chưa đối chiếu — `docs/api-contract.md` đã cố ý bỏ số
  tiền khỏi thân thông báo.
- **Băng** (dòng 46–47) — màn Admin quản lý + component lọc/xuất. `refundAdminApi.ts` **đã merge vào
  `main`** (PR #187) và là **hợp đồng de-facto** của bảng này — mọi tên trường ở đó đã được đối chiếu
  khi dựng bảng. ⚠️ Nút "Thử lại" gọi `POST /admin/refunds/{id}/retry`; endpoint đó **đã** được
  `AuditLogMiddleware` ghi vào `AuditLogs` kèm người bấm, nên **đừng** xin thêm cột người-bấm ở bảng
  này (§3.4).
- **Giàng A Vàng** (dòng 48–49) — test API hoàn tiền tự động + retry. ⚠️ InMemory **không** cưỡng chế
  unique index lẫn FK (§4.4), nên **đừng** viết test *"tạo hai khoản hoàn cho cùng một `PaymentId` thì
  khoản thứ hai phải lỗi"* rồi tin nó kiểm được §4.1 — trên InMemory nó sẽ **xanh dù ràng buộc không
  tồn tại**. Muốn kiểm thật thì phải chạy trên PostgreSQL. Bốn bất biến ở §4.3 thì test được bằng
  InMemory — nên có đủ.
- **Ai cần thêm cột** — nhắn Dăm, không tự sửa `Migrations/*`, `ModelSnapshot.cs`, `Entities/`.

## 7. Việc cần báo nhóm (không phải việc code)

1. **A9 cần bổ sung HAI dòng `Refunds` và `RefundLogs`.** A9 đang là 20 bảng đóng băng và không có
   bảng nào trong hai bảng này (§1). Bảng phân công Sprint 4 (dòng 38) đã giao migrate, nên **nhóm
   chốt bổ sung A9** cho khớp — nếu không thì mọi tài liệu còn nói *"20 bảng"* trong khi CSDL có **25**.
   Đây là lần thứ **tư** việc này được nhắc (`docs/28` §7.4, `docs/32` §1, `docs/34` §7.1). **Nhóm nên
   bổ sung MỘT LƯỢT cả năm bảng còn thiếu** (`CancellationRequests`, `TicketChanges`, `Notifications`,
   `TicketValidations`, `Refunds` + `RefundLogs`) thay vì nhắc lại từng lần.
2. **`docs/03-quy-uoc.md` vẫn không tồn tại trong repo.** Mọi tham chiếu A1–A9 ở đây chỉ còn là trích
   dẫn trong code và tài liệu. Đây là lần thứ **bảy** việc này được nhắc — nhóm nên đưa file vào repo.
3. **`docs/api-contract.md` chưa có mục cho US 8.** Hợp đồng **không** có bất kỳ mục nào về hoàn tiền.
   Hình dạng endpoint là **việc của chủ API**, nên tài liệu này **không tự chốt**; đề nghị **Hiếu chủ
   trì** một mục *"Hoàn tiền"* trong `docs/api-contract.md`, chốt trước rồi mới code (⛔ luật 5). Hình
   dạng đã có sẵn ở hai file frontend **đã merge**: `GET /admin/refunds`,
   `POST /admin/refunds/{id}/retry`, `GET /admin/refunds/export` (màn Băng, dòng 46–47) và
   `GET /refunds/mine` (màn Hạnh, dòng 44). Ba tập giá trị `Source`, `Status` và tên trường
   `retryCount` / `failReason` / `gatewayTransactionId` **đã bị frontend chốt cứng** — đổi tên ở CSDL
   là làm hỏng hai màn đã merge, nên hợp đồng nên chép **nguyên văn** chúng.

   > ✅ **Cập nhật 14/10/2026:** mục `## Hoàn tiền — /refunds` **đã được bổ sung** bởi nhánh
   > `feature/8-api-tra-cuu-hoan-tien` (task dòng 42) — xem `docs/36-api-tra-cuu-hoan-tien.md` §1.
   > Người viết là **Dăm** chứ không phải Hiếu, vì ⛔ luật 5 buộc sửa hợp đồng trước khi code mà task
   > dòng 42 không chờ được. **Hiếu và Băng nên đọc lại mục đó** — nội dung chép nguyên văn hình dạng
   > frontend đã chốt, không phải quyết định mới.
4. **Xung đột: dòng 45 ghi *"thông báo kết quả hoàn tiền qua **email** và trên web"*, nhưng A8.5 đã
   chốt KHÔNG email.** A8.5 ghi rõ: *"Thông báo — **in-app qua SignalR**, lưu bảng `Notifications`"*
   và *"Không làm email/SMS/Web Push trong phạm vi đồ án"*. Đây là xung đột **y hệt** đã gặp ở dòng 7
   của US 5 (`docs/33` §1). Bản này làm theo **A8.5**: thông báo hoàn tiền đi **in-app**. **Nhóm cần
   chốt lại dòng 45** — và chốt một lần cho cả dòng 7 lẫn dòng 45.
5. **Câu hỏi về cổng hoàn tiền: có cổng nào trong ba cổng thật sự hỗ trợ hoàn tiền ở môi trường
   sandbox không?** MoMo / VNPay / ZaloPay có API hoàn tiền, nhưng **không** phải tài khoản sandbox nào
   cũng bật được, và một số cổng chỉ cho hoàn **toàn phần**. Nếu cổng sandbox không hoàn được thì
   luồng thử lại (dòng 41) và đối soát (dòng 43) **không có gì để chạy thật** khi kiểm thử chéo (dòng
   49), và cả nhóm sẽ phải dựa vào mock. **Hiếu + Hoàng + nhóm chốt trước khi làm dòng 40–41.**
6. **Ghi chú kỹ thuật, không phải xung đột:** bảng `Refunds` giữ **cả hai** nguồn hoàn tiền
   (`PaymentFailure` và `TicketCancellation`) trong **một** bảng, chứ không tách hai bảng. Lý do ở
   §2.1 — hai nguồn dùng chung **toàn bộ** vòng đời sau khi khoản hoàn được tạo (gọi cổng, thử lại, đối
   soát), chỉ khác nhau ở ba cột lúc sinh ra. Tách hai bảng là nhân đôi job dòng 41, nhân đôi màn dòng
   46, và nhân đôi bước đối soát dòng 43 — để đổi lấy việc hai cột nullable thành NOT NULL. **Nhóm
   biết để khỏi thắc mắc**, không cần chốt lại.

## 8. Migration đã sinh và đã soát — 14/10/2026

MỘT file, chứa **đúng hai** bảng của nhánh này:

| File | Nội dung |
|---|---|
| `Migrations/20261014153025_Sprint4_Refunds_RefundLogs.cs` (+ `.Designer.cs`) | `CREATE TABLE "Refunds"` + `CREATE TABLE "RefundLogs"` + 9 chỉ mục |

`AppDbContextModelSnapshot.cs` bị sửa — **thuần thêm 168 dòng, 0 dòng xoá** (`git diff --numstat` ra
`168 0`), nên không bảng nào khác bị đụng. Tên theo A7 (`<Sprint><Nội dung>`).

### 8.1 Vì sao chỉ MỘT file — khác `docs/34` §8.1

`main` @ `274b6f7` **không** có nợ migration: `has-pending-model-changes --no-build` chạy **trước** khi
nhánh này thêm entity đã trả về *"No changes have been made to the model since the last migration"*.
Nên `migrations add` sinh **đúng một** file chứa **đúng hai** bảng của nhánh này — **không** cần thủ
thuật ẩn/hiện khai báo như `docs/34` §8.1 (ở đó `main` còn nợ bảng `Notifications` nên phải sinh hai
lần).

Hai bảng **cố ý** đi chung một migration: chúng thuộc **cùng một** task (dòng 38) và **cùng một** user
story, khác `Notifications` / `TicketValidations` (hai story khác nhau nên phải tách). Lối
một-migration-một-story của `docs/28` §8 và `docs/32` §8 vẫn được giữ.

### 8.2 Lệnh đã chạy — cần biến môi trường GIẢ

Cùng lối `docs/32` §8 và `docs/34` §8.2: máy chưa có `appsettings.Development.json` (file bị
`.gitignore`, repo public) thì host design-time không dựng được model. `migrations add`,
`has-pending-model-changes` và `migrations script` đều **không mở kết nối** nào — **đừng tạo file
`appsettings.Development.json` thật**, `scripts/setup-csdl.sh` bỏ qua file đã tồn tại nên nó sẽ chặn
luôn phần cài đặt thật về sau.

```bash
ConnectionStrings__Default="Host=localhost;Port=5432;Database=smartbus;Username=postgres;Password=placeholder" \
Jwt__Key="local-design-time-only-key-at-least-32-chars-long" \
Jwt__Issuer="SmartBus" Jwt__Audience="SmartBusClient" \
dotnet ef migrations add Sprint4_Refunds_RefundLogs --project backend/SmartBus.Api
```

⚠️ **Lệnh này do Dăm chạy, không phải do công cụ AI chạy:** `.claude/settings.json` của repo **CHẶN**
`dotnet ef migrations add` bằng deny rule (cùng `migrations remove`, `database drop`) — chốt cố ý,
khớp luật nhóm *"chỉ Dăm chạy migrations"*. Nhánh này dựng entity + cấu hình, **Dăm chạy lệnh trên**,
rồi cả sáu phép kiểm ở §8.3 được chạy trên file vừa sinh.

⚠️ **`dotnet ef migrations add` build project TRƯỚC khi ghi `ModelSnapshot.cs`**, nên DLL còn lại trên
đĩa giữ snapshot đời cũ. Chạy ngay `has-pending-model-changes --no-build` sau đó sẽ báo **sai** là
*"Changes have been made to the model since the last migration"*. Chạy `dotnet build` một lần rồi đo
lại là hết. Đã sập đúng bẫy này ở nhánh `docs/34` — lần này `dotnet build` được chạy **trước** khi đo.

⚠️ **Đừng để migration rỗng lọt qua:** nếu `Up()` không có gì thì bảng **không bao giờ** được tạo, mà
lệnh **không** báo lỗi — nó chỉ lặng lẽ ghi một dòng vào `__EFMigrationsHistory`. `Up()` của file này
đã được mở đọc: có **2** `CreateTable` và **9** `CreateIndex`.

### 8.3 Sáu phép kiểm, không cần CSDL thật

| Phép kiểm | Kết quả |
|---|---|
| `dotnet ef migrations has-pending-model-changes --no-build` (sau khi `dotnet build`) | ✅ *"No changes have been made to the model since the last migration"* |
| `dotnet ef migrations script 20261013144302_Sprint4_TicketValidations 20261014153025_Sprint4_Refunds_RefundLogs --no-build` rồi `grep -ic xmin` | ✅ **0** dòng — không cột `xmin` nào lọt vào SQL |
| SQL đó có `ALTER TABLE` / `^DROP` / `^UPDATE` / `^DELETE` nào không | ✅ **0 / 0 / 0 / 0** — **chỉ** `CREATE TABLE` ×2 + `CREATE INDEX` ×9 + 1 dòng `INSERT` vào `__EFMigrationsHistory` |
| `git diff --numstat AppDbContextModelSnapshot.cs` | ✅ `168 0` — **thuần thêm**, 0 dòng xoá |
| `dotnet build backend/SmartBus.Api/SmartBus.Api.csproj` | ✅ 0 warning, 0 error |
| `dotnet test backend/SmartBus.Tests/SmartBus.Tests.csproj` | ✅ **1287/1287** passed, 0 failed, 0 skipped |

SQL sinh ra khớp §2, §3, §4 và §5:

| Hạng mục | Kết quả |
|---|---|
| `CREATE TABLE "Refunds"` | ✅ **15 cột** đúng bảng ở §2 |
| `CREATE TABLE "RefundLogs"` | ✅ **7 cột** đúng bảng ở §3 |
| `Id` · `PaymentId` · `UserId` · `RefundLogs.Id` · `RefundLogs.RefundId` | ✅ `uuid` NOT NULL |
| `CancellationRequestId` · `TicketId` | ✅ `uuid` **nullable** — đúng cột "NULL" ở §2 (nhánh `PaymentFailure` không có hai giá trị này) |
| `RefundCode` | ✅ `character varying(64)` NOT NULL |
| `Source` | ✅ `character varying(30)` — lưu **chuỗi** (A3), không phải `integer` |
| `Status` · `Result` · `Trigger` | ✅ đều `character varying(20)` — lưu **chuỗi** (A3) |
| `Amount` | ✅ `numeric(12,2)` — **không** `double precision` / `real` (A3) |
| `GatewayTransactionId` (cả hai bảng) · `FailReason` · `Message` | ✅ `character varying(50)` / `(500)` |
| Năm cột thời gian | ✅ `NextRetryAt`, `ProcessedAt`, `CreatedAt`, `UpdatedAt`, `RefundLogs.CreatedAt` — **tất cả** `timestamp with time zone` (không `timestamp` thiếu múi giờ) |
| Khoá ngoại | ✅ **5 FK** — `Refunds`→`Payments`/`CancellationRequests`/`Tickets`/`Users` (4) và `RefundLogs`→`Refunds` (1) — **tất cả `ON DELETE RESTRICT`** (A5) |
| Chống hoàn hai lần (§4.1) | ✅ `CREATE UNIQUE INDEX "IX_Refunds_PaymentId_Active" ON "Refunds" ("PaymentId") WHERE "Status" IN ('Pending', 'Succeeded')` — **nguyên văn** câu ở §4.1 |
| 🔴 Chỉ mục thường cho `PaymentId` (§5.1) | ✅ `CREATE INDEX "IX_Refunds_PaymentId"` — **có đủ CẢ HAI** chỉ mục này và chỉ mục điều kiện ở trên. Đây là chốt của cái bẫy "EF nhận diện chỉ mục theo danh sách cột": hai dòng `HasIndex` cho cùng cột `PaymentId` đã **không** nuốt nhau, đúng nhờ tên đặt tay |
| `RefundCode` | ✅ `CREATE UNIQUE INDEX "IX_Refunds_RefundCode"` |
| Chỉ mục còn lại | ✅ `IX_Refunds_UserId`, `IX_Refunds_Status_CreatedAt`, `IX_Refunds_Status_NextRetryAt`, `IX_RefundLogs_RefundId_CreatedAt` — tổng cộng **7 khai tay + 2 do EF tự sinh = 9**, đúng danh sách §5 |
| Chỉ mục khoá ngoài do EF tự sinh | ✅ `IX_Refunds_CancellationRequestId`, `IX_Refunds_TicketId` — đúng như §5.1 đã ghi trước |
| `xmin` | ✅ **0 dòng** trong SQL — `xmin` là cột **hệ thống** của PostgreSQL, có mặt trong `Designer.cs` (dòng `b.Property<uint>("xmin")`) nhưng **không** sinh DDL |
| `xmin` trên `RefundLogs` | ✅ **KHÔNG có** — đúng §3.3, bảng chỉ ghi thêm |
| `IsDeleted` | ✅ **không** có ở cả hai bảng (A4) |
| `UpdatedAt` trên `RefundLogs` | ✅ **không** có — bảng chỉ ghi thêm (A4) |

⚠️ **Bộ test 1287/1287 xanh KHÔNG chứng minh chỉ mục UNIQUE ở §4.1 tồn tại** — InMemory không cưỡng
chế unique index lẫn FK (§4.4). Con số đó là **hồi quy** (không làm vỡ thứ đang chạy), không phải
bằng chứng cho chốt chống hoàn hai lần, và **cũng** không phải bằng chứng cho `RESTRICT` của 5 khoá
ngoại. Các chốt đó chỉ có thật ở PostgreSQL.

ℹ️ **Ghi chú về một lần chạy đỏ, để người sau không hoang mang:** lượt `dotnet test` **đầu tiên** của
nhánh ra **1286/1287** — đỏ ở
`SeatHoldRaceFlowApiTests.Nam_muoi_khach_cung_giu_mot_ghe_thi_dung_mot_nguoi_thang`. Chạy lại **một
mình** test đó thì ✅ pass, và **lượt chạy lại toàn bộ** ra ✅ 1287/1287. Đây là test **đua** thật
(nhánh `NhaMaySqlite`, 50 khách cùng giữ một ghế) nên phụ thuộc thời điểm; nhánh này chỉ **thêm**
bảng, **không** sửa `SeatHolds` / `SeatHoldLogs` / `SeatHoldRaceFlowApiTests`, nên không liên quan.

### 8.4 Việc còn lại, theo đúng thứ tự `docs/25-huong-dan-csdl-chung.md` §4

1. PR nhánh này → **có người khác approve** (⛔ luật 7) → **Squash and merge** vào `main`.
2. **Sau khi merge** mới áp lên CSDL chung:
   `dotnet ef database update --project backend/SmartBus.Api`.
   Trước bước đó, endpoint nào chạm hai bảng này sẽ lỗi `relation "Refunds" does not exist`.

### 8.5 ⚠️ Phân tích đồ thị (GitNexus) KHÔNG chạy được trên máy này

`CLAUDE.md` bắt buộc chạy `impact` trước khi sửa và `detect_changes` trước khi commit. Trên máy dựng
nhánh này, **cả hai không chạy được**: repo không có `.gitnexus/`, và `analyze` tự crash khi băm các
file trong npm-cache của chính nó. Nên **không có** kết quả `impact`/`detect_changes` cho nhánh này —
thay bằng: đọc trực tiếp `Entities/Payment.cs`, `CancellationRequest.cs`, `TicketValidation.cs`,
`SeatHoldLog.cs` và các file `AppDbContext.*.cs` để đối chiếu lối đặt tên, lối comment và `Restrict`;
cộng với `dotnet build` (0/0) và `dotnet test` (1287/1287) ở §8.3.

**Ai có máy chạy được GitNexus nên chạy `impact` cho `Refund`, `RefundLog` và `AppDbContext` trước khi
merge** — `AppDbContext.cs` là **file dùng chung**, và nhánh này **có** sửa nó (thêm một dòng
`ConfigureRefund` + một khai báo `partial void`), nên đây là chỗ va chạm với mọi nhánh khác đang mở.
