# CSDL mẫu thông báo (`NotificationTemplates`) và theo dõi thông báo (`Subscriptions`)

> Viết cho task **"Migrate bảng Notifications, NotificationTemplates, Subscriptions"** — bảng phân công
> **Sprint 5, dòng 14**, ưu tiên **Cao**, **4 điểm**, Assign **Vàng Thị Dăm**, phục vụ **US 10**
> *"Là hành khách, tôi muốn nhận thông báo khi xe sắp đến trạm đón/trạm xuống để chuẩn bị di chuyển."*
>
> Người cần đọc: **Hiếu** (dòng 17 — API đăng ký/huỷ theo dõi, người ghi vào `Subscriptions`) ·
> **Kiên** (dòng 15/29 — hai service lọc theo bảng này) · **Hạnh** (dòng 24 — màn Admin quản lý mẫu) ·
> **Thịnh · Băng** (dòng 20/21 — công tắc theo trạm, chuông + badge) · **Giàng A Vàng** (dòng 25–26 —
> test) · **cả nhóm** (bốn câu hỏi ở §6 chưa ai chốt).

> ⚠️ **Đọc mục §0 trước.** Task này **không** nằm ở Sprint 4 — xem §0.

## 0. Hai điều phải nói ngay

### 0.1 Task này thuộc **Sprint 5**, không phải Sprint 4

Bảng phân công `Product_Backlog_Smart_Bus.xlsx` có **đúng một** dòng mang tên
*"Migrate bảng Notifications, NotificationTemplates, Subscriptions"*, và nó nằm ở **sheet Sprint 5,
dòng 14** — User Story của dòng đó là US 10 *"nhận thông báo khi xe sắp đến trạm"*. Sprint 4 **không
có dòng nào** mang tên này (đã tra cả 60 dòng của sheet Sprint 4).

Task được yêu cầu làm sớm hơn sprint của nó. Việc làm sớm **không đụng vào sprint đang chạy**: không
thêm task, không sửa story, không đổi thứ tự task nào của Sprint 4 — chỉ là hai bảng của Sprint 5
được dựng trước. Nhưng nhóm cần biết, vì hai lý do có thật:

1. **Nghiệm thu của Sprint 5 đã bớt việc**: dòng 14 coi như xong phần CSDL, không làm lại.
2. **Sprint 4 vẫn còn hai dòng migrate chưa ai làm** — dòng 27 (*"Migrate bảng Invoices"*) và dòng 50
   (*"Migrate bảng PriorityGroups, PriorityApprovals"*), cả hai đều giao Vàng Thị Dăm và đều đang
   `Chưa làm`. Nếu ai đó đọc tài liệu này để tìm việc Sprint 4 còn nợ thì hai dòng đó mới là câu trả lời.

### 0.2 Dòng 14 còn **hai** bảng, không phải ba

`Notifications` là bảng thứ ba trong tên task, nhưng **đã dựng xong ở Sprint 4** — kéo trước vì task
Sprint 4 dòng 7 (*"Ghi log và thông báo kết quả xử lý cho hành khách"*) không chạy được nếu thiếu bảng
đó: A8.5 chốt thông báo phải **lưu vào bảng** để khách đọc lại khi offline, không chỉ một sự kiện
SignalR. Lý do đầy đủ ở `docs/33-thong-bao-ket-qua-huy-doi-ve.md` §1, và chính `docs/33` §9.1 đã chốt
trước: *"bảng phân công Sprint 5 dòng 14 coi như bớt một bảng: còn lại `NotificationTemplates` +
`Subscriptions`"*.

Nhánh này vì vậy **không sinh lại** `Notifications`. Migration mới chỉ chứa hai bảng — xem §9.

## 1. Hai bảng này KHÔNG nằm trong A9 — phải báo nhóm

A9 (danh sách **20 bảng đóng băng**, nguồn `quy uoc.docx` mục A9) liệt kê `Notifications` ở vị trí
**#15**, và **không có** `NotificationTemplates` lẫn `Subscriptions`:

| Nguồn | Nói gì |
|---|---|
| **A9 #15** | `Notifications` — có trong danh sách đóng băng |
| **A9 #16** | `Incidents` — bảng kế tiếp, **không** phải `NotificationTemplates` |
| **A9** (cả 20 dòng) | **Không có** `NotificationTemplates`, **không có** `Subscriptions` |
| **A6** (chỉ mục nên có) | Chỉ có `Notifications (UserId, CreatedAt)` — không có dòng nào cho hai bảng này |
| **Bảng phân công Sprint 5, dòng 14** | *"Migrate bảng Notifications, NotificationTemplates, Subscriptions"* — tên bảng **có thật** |

Đây là **lần thứ năm** việc "A9 lệch thực tế" được nhắc: `docs/28` §7.4 (`Tickets`), `docs/32` §1
(`TicketChanges`/`CancellationRequests` — *"lần thứ hai"*), `docs/34` §1 (`TicketValidations` — *"lần
thứ ba"*), rồi `Refunds`/`RefundLogs` (*"lần thứ tư"*).

⚠️ **Lần thứ tư không có tài liệu mở được từ `main`**: nó nằm ở `docs/35` §1, mà `docs/35` và `docs/36`
hiện **chỉ tồn tại trên nhánh `feature/8-api-tra-cuu-hoan-tien`** (chưa merge). Bốn lần còn lại đều
đọc được từ `main`. Ghi rõ ở đây để người đọc `main` không tưởng tài liệu bị thiếu — và để lần merge
nhánh hoàn tiền đó không sinh ra hai số thứ tự đụng nhau.

**Cách xử lý vẫn giữ nguyên như bốn lần trước** (`docs/32` §7.1): **dựng theo phân công, rồi báo nhóm
bổ sung A9.** Chủ CSDL không tự thêm bảng ngoài A9 mà im lặng — nhưng tên bảng đã nằm sẵn trong bảng
phân công, nên đây **không phải** chỗ phải xin phép trước; đây là chỗ phải **báo lại** để tài liệu khớp
CSDL. Việc báo nằm ở §8.

⚠️ A7 cấm *"Tự thêm bảng ngoài danh sách A9 **mà không báo nhóm**"* — vế bị cấm là **im lặng**, không
phải việc dựng bảng. Nhánh này không im lặng: mục này, §8 và cả `AppDbContext.cs` đều ghi rõ.

## 2. Bảng `NotificationTemplates` — cột

| Cột | Kiểu | Ghi chú |
|---|---|---|
| `Id` | uuid | khoá chính, `Guid.NewGuid()` (A1) |
| `Type` | varchar(30) | **UNIQUE** — `NotificationType`, mỗi loại tối đa một mẫu |
| `Title` | varchar(200) | **cùng độ rộng** `Notifications.Title` |
| `Body` | text | **cùng kiểu** `Notifications.Body` |
| `IsActive` | boolean | mẫu đang dùng; tắt → quay về câu mặc định trong code |
| `CreatedAt` | timestamptz | |
| `UpdatedAt` | timestamptz NULL | A4 — bảng **có sửa** thì phải có |

Chỉ mục: `IX_NotificationTemplates_Type` — **unique**.

**Cố ý không có `IsDeleted`** (A4 cấm). **Cố ý không có cột `Code`** như `Routes.Code` /
`Vouchers.Code` / `MonthlyPasses.Code`: khoá nghiệp vụ của bảng này **đã là** `Type` — một giá trị
enum ổn định, do code sinh ra. Thêm `Code` là có **hai** định danh cho cùng một thứ, và chúng sẽ lệch
nhau ngay lần đầu ai đó đổi tên enum mà quên đổi `Code`.

### 2.1 Mẫu là phần **GHI ĐÈ**, không phải điều kiện để gửi

Đây là quyết định quan trọng nhất của bảng này:

> **Không có mẫu (hoặc mẫu đang tắt) → tầng service dùng câu mặc định trong code.**

🔴 **Vì sao không làm ngược lại:** nếu bảng mẫu là **nguồn duy nhất** thì một `NotificationType` chưa
có mẫu sẽ **im lặng không gửi gì cả** — khách không nhận được thông báo và **không có lỗi nào để lần**.
Đó đúng là kiểu hỏng mà `docs/33` §3.5 phải bắt, và nó sẽ xảy ra ngay ở Sprint 5: `NotificationType`
hiện chỉ có **bốn** giá trị (kết quả xử lý huỷ/đổi vé), còn US 10 cần thêm giá trị mới (xe sắp đến
trạm) — nếu mẫu là bắt buộc thì giữa lúc thêm enum và lúc Admin nhập mẫu, tính năng US 10 **không chạy
mà cũng không báo lỗi**.

Cách này cũng khớp đúng thứ `docs/33` §6.3 đã chốt trước: bốn câu của `TicketRequestResultNotifier`
đang là **hằng chuỗi trong code**, và *"chuyển sang template sau là thay chỗ đọc chuỗi, không phải sửa
schema"*. Nhánh này dựng đúng cái schema đó — và **không sửa** `TicketRequestResultNotifier`: bốn câu
đó nghiễm nhiên trở thành câu mặc định của bốn loại thông báo, không phải việc phải làm lại.

### 2.2 Vì sao `IsActive`, không phải "xoá dòng là xong"

Với quy ước ở §2.1, xoá một mẫu cũng cho ra kết quả đúng (quay về câu mặc định). `IsActive` vẫn cần, vì
hai lý do:

1. **Tắt không được mất chữ đã viết.** Người ta tắt một mẫu đang soạn dở để hệ thống dùng lại câu cũ
   trong lúc chờ — xoá dòng là mất phần chữ đó.
2. **Tắt phải là một hành động không phá gì.** Nếu cách duy nhất để tắt là xoá, thì một lượt bấm nhầm
   ở màn quản lý mẫu (dòng 24) làm mất nội dung vĩnh viễn.

`bool` theo đúng lối A4 đã chỉ: *"Dùng cột trạng thái sẵn có: `Users.IsActive`, `Routes.Status`,
`Buses.Status`"* — A4 gọi tên `IsActive` như cột bật/tắt hợp lệ của dự án. **Không** dùng enum
`Status` như `Voucher`: hai giá trị `Active`/`Inactive` trong một enum là dựng thêm một bảng mã cho
đúng một bit.

## 3. Bảng `Subscriptions` — cột

| Cột | Kiểu | Ghi chú |
|---|---|---|
| `Id` | uuid | khoá chính (A1) |
| `UserId` | uuid | FK → `Users`, **RESTRICT** — người theo dõi |
| `RouteId` | uuid | FK → `Routes`, **RESTRICT** — tuyến theo dõi |
| `StopId` | uuid **NULL** | FK → `Stops`, **RESTRICT** — `NULL` = theo dõi **cả tuyến** |
| `CreatedAt` | timestamptz | thời điểm bấm theo dõi |

**Cố ý không có `UpdatedAt`** — xem §3.2.

| Chỉ mục | Loại | Trả lời câu gì |
|---|---|---|
| `IX_Subscriptions_UserId` | thường | "danh sách theo dõi của tôi" (dòng 17, 20) — **kiêm luôn** chỉ mục FK `UserId` |
| `IX_Subscriptions_UserId_RouteId` | **duy nhất**, `WHERE "StopId" IS NULL` | chặn trùng "theo dõi cả tuyến" (§3.3) |
| `IX_Subscriptions_UserId_RouteId_StopId` | **duy nhất**, `WHERE "StopId" IS NOT NULL` | chặn trùng "theo dõi một trạm" (§3.3) |
| `IX_Subscriptions_RouteId` | thường (EF tự sinh) | dòng 29 — "ai đang theo dõi tuyến này" |
| `IX_Subscriptions_StopId` | thường (EF tự sinh) | dòng 15 — "ai đang theo dõi trạm này" |

### 3.1 `StopId` NULL = theo dõi **cả tuyến**

Hai nghĩa, phân biệt bằng NULL:

| Giá trị | Nghĩa | Ai dùng |
|---|---|---|
| có trạm | chỉ báo khi xe tới gần **trạm này** | dòng 15, 20 — công tắc bật/tắt theo trạm |
| `NULL` | theo dõi **cả tuyến**: mọi trạm, và mọi sự cố của tuyến | dòng 29 — *"hành khách đang theo dõi tuyến"* |

Không tách thành hai bảng: đây là **cùng một hành động của người dùng** (một công tắc), chỉ khác độ
chi tiết — tách bảng thì màn dòng 20 phải đọc hai nguồn để vẽ một công tắc.

⚠️ **Một lượt theo dõi KHÔNG gắn với chuyến hay ngày.** Khách theo dõi *"tuyến 01"*, không theo dõi
*"chuyến 07:30 ngày mai"*. Gắn với chuyến thì mỗi ngày khách phải đăng ký lại, và lượt đăng ký hết hạn
sau khi chuyến chạy xong. Đây là chủ ý.

⚠️ **Ràng buộc "trạm này phải thuộc tuyến này" KHÔNG chặn được ở tầng CSDL.** Khoá ngoại chỉ nói
`StopId` **có tồn tại**, không nói nó nằm trên `RouteId`. Việc kiểm thuộc `RouteStops` là của endpoint
đăng ký (**Hiếu**, dòng 17). Ghi ra đây vì đây là chỗ dễ tưởng khoá ngoại đã lo hộ.

### 3.2 "Huỷ theo dõi" là **XOÁ DÒNG**, không phải lật cờ

Cả dòng 17 (*"API đăng ký / **huỷ** theo dõi"*) lẫn dòng 20 (*"bật/**tắt** nhận thông báo theo trạm"*)
đều là **xoá dòng**. Hệ quả: bảng **không có cột nào sửa được**, nên **không có `UpdatedAt`** — A4 chỉ
bắt buộc cột đó ở bảng *có sửa*.

🔴 **Cố ý không dùng cột `IsActive` ở đây** (dù §2.2 vừa dùng nó cho bảng mẫu — hai bảng, hai kết luận
khác nhau vì hai vòng đời khác nhau). Ở bảng mẫu, dòng sống lâu và nội dung chữ là thứ đáng giữ; ở
đây dòng **không có nội dung nào**, nên giữ lại một dòng đã tắt chỉ tạo ra **hai cách nói cùng một
điều** ("còn dòng + `IsActive = false`" và "không còn dòng") — mọi câu truy vấn sau đó phải nhớ lọc cả
hai, và câu nào quên là bắn thông báo cho người đã tắt. A4 cảnh báo đúng loại này: *"Hai cơ chế xoá
mềm song song sẽ có ngày mâu thuẫn nhau."*

Bật lại = ghi dòng mới. `CreatedAt` khi đó mang nghĩa *"theo dõi từ khi nào"* của lượt đang có hiệu lực.

### 3.3 🔴 Hai chỉ mục duy nhất **bộ phận** — vì NULL không bằng NULL

Ý định: *một khách chỉ theo dõi một tuyến/trạm đúng **một** lần*. Hai dòng trùng nhau nghĩa là khách
nhận **hai thông báo y hệt** cho cùng một lượt xe — lỗi nhìn thấy được, nên chặn ở tầng CSDL.

**Nhưng viết một chỉ mục duy nhất trên `(UserId, RouteId, StopId)` là SAI.** Trong PostgreSQL hai giá
trị `NULL` được coi là **khác nhau**, nên `(khách A, tuyến 01, NULL)` chèn được **bao nhiêu lần cũng
được** — đúng cái ca "theo dõi cả tuyến" mà dòng 29 cần. Ràng buộc sẽ **không chặn được gì ở đúng chỗ
dễ trùng nhất**, và im lặng: không có lỗi nào để lần.

Nên tách làm hai, mỗi bên một điều kiện — cùng lối **partial index** mà A6 đã dùng cho phép chống bán
trùng ghế (`WHERE "Status" IN ('Held', 'Paid')`), và cùng lối viết `HasFilter(...)` trong
`OnModelCreating` vì EF Core không sinh được partial index bằng attribute:

```csharp
e.HasIndex(s => new { s.UserId, s.RouteId })
 .IsUnique()
 .HasFilter("\"StopId\" IS NULL");

e.HasIndex(s => new { s.UserId, s.RouteId, s.StopId })
 .IsUnique()
 .HasFilter("\"StopId\" IS NOT NULL");
```

⚠️ **Bài học dùng lại được:** bất kỳ ràng buộc duy nhất nào của dự án có cột **nullable** đều phải hỏi
câu này. Hiện chưa có ràng buộc nào khác rơi vào ca đó.

### 3.4 Vì sao ba khoá ngoại đều `Restrict`

A5: mặc định của EF là `Cascade`, phải ghi tường minh. Ở bảng này hậu quả nặng hơn bình thường: xoá
một **tuyến** mà cascade thì **âm thầm xoá hết lượt theo dõi của mọi khách trên tuyến đó** — khách
thôi nhận thông báo mà không ai biết vì sao, và không có gì trong CSDL nói rằng chúng từng tồn tại.
Cùng lý do với `Users` và `Stops`.

### 3.5 Vì sao cố ý **KHÔNG** có cột "loại thông báo muốn nhận"

Bảng phân công dòng 17 chốt độ chi tiết là **theo tuyến và trạm**, không phải theo loại. Cả hai
nghiệp vụ đã biết đều dùng **chung một** lượt đăng ký: thông báo trạm (dòng 15) và thông báo sự cố
(dòng 29 — *"hành khách đang theo dõi tuyến"*). Thêm cột loại bây giờ là dựng schema cho một màn hình
chưa ai viết — đúng lập luận `docs/33` §1 đã dùng để **hoãn chính bảng này lại**, và thêm sau chỉ là
một cột nullable.

## 4. Vì sao hai bảng nằm **hai file** `AppDbContext` khác nhau

`Refunds` + `RefundLogs` nằm chung một file (`AppDbContext.Refund.cs`) vì `RefundLogs` **không có đời
sống riêng**. `NotificationTemplates` và `Subscriptions` thì ngược lại: không quan hệ cha–con, không
bảng nào là nhật ký của bảng kia, và **người tiêu thụ khác nhau** (mẫu → màn quản lý mẫu dòng 24; lượt
theo dõi → API dòng 17 và hai service dòng 15/29). Nên tách: `AppDbContext.NotificationTemplate.cs` và
`AppDbContext.Subscription.cs`.

`NotificationTemplates` cũng **không** nhập vào `AppDbContext.Notification.cs` dù cùng nhóm nghiệp vụ:
file đó là **thông báo đã gửi** (chỉ ghi thêm, không sửa được), còn bảng này là **cấu hình do quản trị
viên sửa** — hai vòng đời khác hẳn nhau, và file kia đang gắn với một nhánh đã xong (`docs/33`).

## 5. Việc **cố ý KHÔNG** làm trong nhánh này

| Không làm | Vì sao |
|---|---|
| Không sửa `TicketRequestResultNotifier` để đọc bảng mẫu | Bốn câu của nó đã đúng và đang chạy; §2.1 chốt chúng là **câu mặc định**. Chuyển sang đọc bảng là việc riêng, và phải chờ §6.1 (quy ước chỗ trống) được chốt |
| Không seed sẵn mẫu nào | Mẫu là **ghi đè**: seed một mẫu nghĩa là chọn hộ nội dung cho một tính năng chưa viết. Bảng trống là trạng thái đúng của nó hôm nay |
| Không thêm cột "đã đọc" cho `Notifications` | Vẫn là **câu hỏi mở** của `docs/33` §9.3 — hình dạng đúng phụ thuộc badge của **Băng** (dòng 21). Nhánh này không mở lại |
| Không thêm cột chiều đi cho `Subscriptions` | Chưa ai chốt một tuyến có hai chiều hay không — §6.2 |
| Không viết API nào | Endpoint dòng 17 là việc của **Hiếu**; màn dòng 24 của **Hạnh**. Luật 5: muốn có endpoint thì `api-contract.md` phải có trước — mà hợp đồng cho hai màn đó chưa ai viết, nên **không** tự bịa ra ở đây |
| Không sửa `A9` | A9 nằm trong `quy uoc.docx` **ngoài repo**, và `docs/03-quy-uoc.md` vẫn không tồn tại (§7.2). Việc của nhóm — §8 |

## 6. Bốn câu hỏi mở — nhóm phải chốt trước Sprint 5

### 6.1 🔴 Quy ước CHỖ TRỐNG trong mẫu (đề xuất, chưa ai chốt)

Mẫu cần điền dữ liệu lúc gửi, ví dụ `"Xe buýt tuyến {RouteCode} sắp đến trạm {StopName}."`. Nhánh này
đề xuất: **cặp ngoặc nhọn, tên PascalCase**.

Hai hệ quả **bắt buộc** phải chốt cùng lúc, nếu không thì mẫu sinh ra thông báo sai:

1. **Chỗ trống không có dữ liệu thì KHÔNG được để nguyên `{StopName}`** trong thông báo khách đọc.
   Tầng service phải thay bằng câu trung tính hoặc bỏ cả câu — A8.5 chốt thông báo phải **đọc được một
   mình**, và `docs/33` §6.1 đã có tiền lệ: test ghim rằng `Body` không được chứa chữ số.
2. **Danh sách chỗ trống hợp lệ theo từng `Type` là việc của endpoint lưu mẫu** (dòng 24 — Hạnh),
   không phải của người đưa thư. Một chỗ trống gõ sai tên sẽ **không bao giờ khớp** và đi thẳng ra
   thông báo của khách.

Cố ý không làm sẵn ở đây: danh sách chỗ trống hợp lệ phụ thuộc dữ liệu mà từng service (dòng 15/18/29)
có trong tay, và **cả ba service đó chưa ai viết**.

### 6.2 Một tuyến có **hai chiều** hay không?

`Subscriptions` giả định **một `Route` là một chiều** — repo không có cột `Direction` nào (đã kiểm bằng
`git grep` toàn bộ `backend/`). Nhưng A8.6 cảnh báo: *"Xe đi và xe về trên cùng tuyến **trùng toạ độ**.
Xác định chiều bằng thứ tự trạm (`StopOrder`), không bằng khoảng cách toạ độ."*

Nếu nhóm chốt rằng một tuyến chạy **cả hai chiều**, thì khách theo dõi một trạm sẽ nhận thông báo cho
**cả hai lượt**, và bảng phải thêm cột chiều. Chưa thêm bây giờ vì chưa ai chốt — mà thêm một cột
nullable sau này thì rẻ, còn đoán sai bây giờ thì phải migrate kèm sửa dữ liệu.

### 6.3 Tên bảng `Subscriptions` có quá rộng không?

Tên này do **bảng phân công** ghi, và nhánh này giữ nguyên (cùng lối `TicketValidations` ở `docs/34`).
Nhưng nghĩa thật của nó hẹp hơn: **đăng ký nhận THÔNG BÁO**, không phải thuê bao trả tiền — repo không
có nghiệp vụ nào như vậy. Nếu nhóm muốn đổi (ví dụ `NotificationSubscriptions`), **đổi ngay bây giờ**:
đổi tên bảng lúc đã có dữ liệu đắt hơn hẳn.

### 6.4 Tên migration: `Sprint4_` hay `Sprint5_`?

Migration được sinh trong **Sprint 4** nhưng chứa bảng của **Sprint 5**. Nhánh này đặt tên theo **mốc
sinh migration** (`Sprint4_…`), đúng tiền lệ `docs/33` §9.1 đã chốt cho `Sprint4_Notifications` — bảng
cũng của Sprint 5 nhưng *"tên migration đặt theo mốc Sprint 4 (nơi công việc thật sự xảy ra)"*. A7
cũng ghi *"Gộp theo sprint — mỗi sprint một migration"*, tức là gom theo lúc áp lên CSDL chung.

Nhóm thấy sai thì nói; đổi tên một migration **chưa merge** thì rẻ, sau khi merge thì không.

## 7. Việc còn lại của người khác

- **Hiếu** (dòng 17) — API đăng ký/huỷ theo dõi. Ba việc: ghi/xoá dòng `Subscriptions`; **kiểm trạm có
  thuộc tuyến** (`RouteStops`) vì khoá ngoại không lo hộ (§3.1); và trả **409** khi khách bấm theo dõi
  hai lần — chỉ mục duy nhất ở §3.3 sẽ ném lỗi, nhưng đừng để nó nổi lên thành 500.
- **Kiên** (dòng 15, 29) — hai service đọc `Subscriptions`. ⚠️ **Đừng quên lọc `StopId IS NULL`** khi
  lấy danh sách "theo dõi tuyến", và đừng quên rằng một khách có thể vừa theo dõi cả tuyến **vừa**
  theo dõi một trạm — nếu không khử trùng ở tầng service thì khách nhận hai thông báo cho một lượt xe.
  Service dòng 15 cũng đọc bảng mẫu: **không có mẫu thì dùng câu mặc định**, tuyệt đối không bỏ qua
  lượt gửi (§2.1).
- **Hạnh** (dòng 24) — màn Admin quản lý mẫu. Cần: danh sách theo `Type`, sửa `Title`/`Body`/`IsActive`,
  và **kiểm chỗ trống hợp lệ** trước khi lưu (§6.1). `Body` phải hiển thị được **xuống dòng và độ dài
  bất kỳ**.
- **Thịnh** (dòng 20) và **Băng** (dòng 21) — công tắc theo trạm và chuông + badge. Công tắc đọc/ghi
  `Subscriptions`; "tắt" là **xoá dòng**, không phải gửi cờ `IsActive` (§3.2).
- **Giàng A Vàng** (dòng 25–26) — test API gửi thông báo và đăng ký theo dõi. ⚠️ **InMemory KHÔNG cưỡng
  chế chỉ mục lẫn khoá ngoại** (cùng cảnh báo đã ghi ở `docs/32` §4.4 và `docs/34` §8.3): test xanh **không**
  nói gì về hai chỉ mục duy nhất bộ phận ở §3.3 hay FK `Restrict` ở §3.4. Muốn ghim hai thứ đó phải
  chạy trên PostgreSQL — xem §9.
- **Ai cần thêm cột/bảng** — nhắn Dăm, không tự sửa `Migrations/*`, `ModelSnapshot.cs`, `Entities/`.

## 8. Việc cần báo nhóm (không phải việc code)

1. **A9 cần bổ sung hai dòng**: `NotificationTemplates` và `Subscriptions` — lần thứ năm A9 lệch thực
   tế (§1). A9 nằm trong `quy uoc.docx` ngoài repo.
2. **Task Sprint 5 dòng 14 coi như xong phần CSDL** — dòng 14 trong bảng phân công nên được đánh dấu,
   để không ai làm lại `Notifications`. Việc này kéo theo §0.1: xác nhận việc làm sớm là chủ ý.
3. **Sprint 4 còn nợ hai dòng migrate của Dăm, và một dòng đang nằm ở nhánh chưa merge.** Cả năm
   dòng migrate của sheet Sprint 4 đều giao Vàng Thị Dăm (dòng 2, 14, 27, 38, 50 — sheet có **đúng
   60 dòng**, đã tra cả 60 và **không** có dòng nào tên *"Notifications, NotificationTemplates,
   Subscriptions"*). Bốn dòng còn lại tra theo code:
   - **dòng 27 — `Invoices`**: 🔴 **chưa ai làm.** `class Invoice` **không tồn tại ở bất kỳ nhánh nào**
     (đã `git grep` trên `--all`).
   - **dòng 50 — `PriorityGroups`, `PriorityApprovals`**: 🔴 **chưa ai làm.** Cũng không có entity
     trên nhánh nào.
   - **dòng 38 — `Refunds`, `RefundLogs`**: code **đã có** nhưng chỉ trên nhánh
     `feature/8-api-tra-cuu-hoan-tien`, **chưa merge**. Không phải việc còn nợ, mà là **thứ tự merge**
     — xem mục 6 dưới.
   - dòng 2 (`TicketChanges`, `CancellationRequests`) và dòng 14 (`TicketValidations`): đã xong trên
     `main` (`docs/32`, `docs/34`).

   ⚠️ Cột `Status` trong file `.xlsx` ghi **"Chưa làm" cho cả năm dòng**, kể cả hai dòng đã xong từ
   Sprint 4 — nghĩa là cột đó **không được cập nhật** và không dùng được để biết việc nào còn nợ. Tra
   theo code là cách duy nhất đáng tin.
4. **Bốn câu hỏi ở §6** — quy ước chỗ trống, chiều đi của tuyến, tên bảng, tên migration. Câu §6.1 là
   câu **chặn đường** cho cả ba service Sprint 5.
5. **`docs/03-quy-uoc.md` vẫn không tồn tại trong repo** — mọi tham chiếu A1–A9 chỉ còn là trích dẫn
   trong code và tài liệu. Đây là lần thứ **bảy** việc này được nhắc (lần thứ sáu ở `docs/34` §9.3).
   ⚠️ Con số này **không đáng tin**: `docs/32` §7.2 và `docs/33` §9.2 cùng ghi *"lần thứ năm"*, `docs/34`
   §9.3 ghi *"lần thứ sáu"* — các tài liệu đếm lệch nhau. Việc đáng làm không phải sửa số, mà là **đưa
   `quy uoc.docx` vào repo**, lúc đó cả chuỗi đếm này thành vô nghĩa.
6. **`docs/35` và `docs/36` không có trên `main`** — chúng chỉ nằm ở nhánh
   `feature/8-api-tra-cuu-hoan-tien` (§1), cùng nhánh giữ code `Refunds`/`RefundLogs` (mục 3). Số thứ
   tự **không đụng nhau** (35, 36, 37 khác nhau), nên merge nhánh nào trước cũng được — nhưng cho tới
   lúc merge, mọi tham chiếu tới `docs/35`/`docs/36` **từ `main` là tham chiếu chết**. Tài liệu này
   vì vậy chỉ trỏ tới những gì đọc được từ `main`.

## 9. Migration — lệnh cần chạy (việc của Dăm)

🔴 A7 — **chỉ Vàng Thị Dăm được chạy `dotnet ef migrations add`**. Trong `.claude/settings.json` của
repo, lệnh này còn bị **deny rule** chặn thẳng (`dotnet ef migrations add`, `migrations remove`,
`database drop`) — chốt cố ý, khớp luật nhóm. Nên nhánh này dừng ở entity + cấu hình, và **model đang
lệch snapshot** — đó là trạng thái đúng của một nhánh chờ sinh migration, không phải lỗi.

Máy chưa có `appsettings.Development.json` (file bị gitignore, repo public) thì mọi lệnh `dotnet ef`
cần các biến môi trường **giá trị giả** để host design-time dựng được model. ⚠️ **Đừng tạo file
`appsettings.Development.json` thật** — `scripts/setup-csdl.sh` bỏ qua file đã tồn tại nên nó sẽ chặn
luôn phần cài đặt thật về sau (cùng cảnh báo đã ghi ở `docs/32` §8 và `docs/33` §10):

```bash
ConnectionStrings__Default="<chuoi-ket-noi-gia — chi can dung dinh dang>" \
Jwt__Key="<chuoi-bat-ky-dai-it-nhat-32-byte>" \
Jwt__Issuer="SmartBus" \
Jwt__Audience="SmartBusClient" \
dotnet ef migrations add Sprint4_NotificationTemplates_Subscriptions --project backend/SmartBus.Api
```

Nhánh đang đứng trên migration `20261013144302_Sprint4_TicketValidations` (bản mới nhất của `main`),
nên migration mới **chỉ chứa hai bảng** — không sinh lại `Notifications`, không chạm bảng nào khác.

### Hai phép kiểm sau khi sinh, không cần CSDL thật

| Phép kiểm | Kỳ vọng |
|---|---|
| `dotnet ef migrations has-pending-model-changes --project backend/SmartBus.Api --no-build` | ✅ *"No changes have been made to the model since the last migration"* |
| `dotnet ef migrations script <Sprint4_TicketValidations> <Sprint4_NotificationTemplates_Subscriptions> --project backend/SmartBus.Api --no-build` | **CHỈ** có 2 `CREATE TABLE` + 5 `CREATE INDEX` + 2 dòng `INSERT` vào `__EFMigrationsHistory`. **Không** `ALTER TABLE`, **không** `DROP`, **không** `UPDATE` |

Đối chiếu SQL với §2 và §3:

- `NotificationTemplates`: `Type` = `character varying(30)`, `Title` = `character varying(200)`,
  **`Body` = `text`** (không phải varchar), `IsActive` = `boolean NOT NULL`,
  `UpdatedAt` = `timestamp with time zone` **nullable**;
- `Subscriptions`: `StopId` = `uuid` **nullable**, `CreatedAt` = `timestamp with time zone`;
- **hai chỉ mục duy nhất phải có mệnh đề `WHERE`**: một cái `WHERE "StopId" IS NULL`, một cái
  `WHERE "StopId" IS NOT NULL`. ⚠️ Thiếu mệnh đề `WHERE` nghĩa là EF bỏ qua `HasFilter` — và ràng
  buộc trùng sẽ **không** chặn được ca "theo dõi cả tuyến" (§3.3). Đây là thứ phải nhìn bằng mắt
  trong file SQL, không suy ra được từ việc build xanh;
- **ba khoá ngoại đều `ON DELETE RESTRICT`** — không có `CASCADE` nào trong file SQL này;
- **không** được có cột `xmin` nào (nhánh này không khai khoá phiên bản dòng cho hai bảng — §4 của
  `AppDbContext.Subscription.cs`).

## 10. Đã kiểm trong nhánh này

| Phép kiểm | Kết quả |
|---|---|
| `dotnet build backend/SmartBus.Api/SmartBus.Api.csproj` | ✅ 0 warning, 0 error |
| `dotnet test backend/SmartBus.Tests` (toàn bộ) | ✅ **1287/1287 xanh**, 0 fail, 10 phút 58 giây |
| `git grep` tên hai symbol mới | ✅ `NotificationTemplate`, `Subscription` **chưa từng** tồn tại trong `backend/` — không có caller nào để vỡ |
| `git grep "Direction"` | ✅ không có cột chiều nào trong `backend/` — cơ sở cho §6.2 |

### 10.1 ⚠️ `CLAUDE.md` bắt buộc chạy `impact` / `detect_changes` của GitNexus — máy này KHÔNG chạy được

Repo không có `.gitnexus/`, `gitnexus analyze` tự crash trên máy Dăm — đã ghi ở `docs/34` §8.5, cùng
máy, cùng lỗi. Bù bằng đúng những gì làm được:

- **`git grep` hai symbol mới** — chưa từng tồn tại, nên **không có caller nào** để mà vỡ. Đây là ca
  duy nhất mà `risk: UNKNOWN` không cần điều tra thêm: không phải "không tìm thấy caller" mà là
  "symbol chưa hề tồn tại".
- **`AppDbContext.cs` là file dùng chung duy nhất bị sửa** — 4 dòng thêm vào `OnModelCreating` và 2
  khai báo `partial void`, **thuần thêm**, không sửa dòng nào có sẵn. Rủi ro cho người khác: một
  `ConfigureXxx` khai mà quên gọi (hoặc ngược lại) sẽ làm model lệch âm thầm — bắt được bằng
  `has-pending-model-changes` ở §9, và đó là lý do phép kiểm đó nằm trong Definition of Done của task.
- **Hai entity mới chưa được bảng nào tham chiếu** — không có khoá ngoại nào trỏ **tới** chúng, nên
  chúng không nằm trên đường đi của bất kỳ luồng nào đang chạy.

**Ai có máy chạy được GitNexus nên chạy `impact` cho `AppDbContext`** — file dùng chung duy nhất bị sửa.
