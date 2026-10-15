# Thông báo kết quả xử lý yêu cầu huỷ / đổi vé — Sprint 4

> Viết cho task **"Ghi log và thông báo kết quả xử lý cho hành khách"** (bảng phân công Sprint 4,
> dòng 7 — **Vàng Thị Dăm**, ưu tiên Trung bình, 3 điểm), phục vụ **US 5 "Hủy & Đổi vé"**.
>
> Người cần đọc: **Hoàng** (dòng 5 — người duyệt xong là người gọi thông báo) · **Hạnh** và **Băng**
> (dòng 8/10/11 — màn theo dõi trạng thái, nơi khách đọc thông báo) · **Hiếu · Kiên** (dòng 3/4 — biết
> rằng kết quả xử lý của mình sẽ được báo cho khách) · **Giàng A Vàng** (dòng 12–13 — test).
>
> 🟢 **Trạng thái 12/10/2026:** entity `Notification`, cấu hình `AppDbContext.Notification.cs`,
> `NotificationHub` + `INotificationClient`, `ITicketRequestResultNotifier` +
> `TicketRequestResultNotifier`, test (`TicketRequestResultNotifierTests`, 10 ca) và mục hợp đồng
> `/hubs/notifications` trong `docs/api-contract.md` **đã xong**. **Migration CHƯA sinh** (§10) và
> **`Program.cs` chưa nối** (§3.4) — cả hai đều là việc còn lại có chủ, không phải chỗ sót.

## 1. Bảng `Notifications` — bảng của Sprint 5, kéo trước ĐÚNG MỘT bảng

Khác hẳn hai bảng anh em `CancellationRequests` / `TicketChanges` (`docs/32` §1 — **không** có trong
A9), bảng này **có** trong A9:

| Nguồn | Nói gì |
|---|---|
| **A9 #15** | `Notifications` — có trong danh sách 20 bảng đóng băng |
| **A6** | Chỉ mục `Notifications (UserId, CreatedAt)` — đã đánh đúng cặp đó |
| **A4** | *"Bảng chỉ ghi thêm thì không cần `UpdatedAt`: AuditLogs, Payments, Notifications."* — gọi tên đúng bảng này |
| **A8.5** | *"Thông báo — **in-app qua SignalR**, lưu bảng `Notifications`"* + *"Không làm email/SMS/Web Push trong phạm vi đồ án… Lưu vào bảng để khách xem lại thông báo khi offline."* |
| **Bảng phân công Sprint 5, dòng 14** | *"Migrate bảng Notifications, NotificationTemplates, Subscriptions"* — Vàng Thị Dăm |
| **Bảng phân công Sprint 4, dòng 7** | *"Ghi log và thông báo kết quả xử lý cho hành khách"* — Vàng Thị Dăm, **việc đang làm ở đây** |

**Vì sao phải kéo trước:** A8.5 chốt thông báo **đi kèm một dòng trong bảng `Notifications`**, không
phải chỉ một sự kiện SignalR. Task dòng 7 mà không có bảng thì chỉ làm được nửa đầu — khách đang mở
app thì thấy, tắt app là mất, đúng thứ A8.5 vừa nói ngược lại. Nên bảng là **điều kiện chạy được**
của task dòng 7, không phải việc mở rộng phạm vi.

**Vì sao chỉ MỘT bảng:** `NotificationTemplates` (dòng 24 — màn Admin quản lý mẫu) và `Subscriptions`
(dòng 17 — đăng ký theo dõi theo tuyến/trạm) **không được kéo theo**. Không task nào của Sprint 4
đụng tới chúng: template là để soạn nội dung hàng loạt, còn nội dung ở đây chỉ có bốn câu do notifier
soạn tại chỗ (§6); subscription là để biết *ai muốn nhận thông báo về tuyến nào*, còn thông báo ở đây
nhắm thẳng vào người gửi yêu cầu. Kéo hai bảng đó theo là dựng schema cho nghiệp vụ chưa ai viết.

## 2. Bảng `Notifications` — cột

| Cột | Kiểu | Ghi chú |
|---|---|---|
| `Id` | uuid | khoá chính |
| `UserId` | uuid | FK → `Users`, `RESTRICT` — người **NHẬN** |
| `Type` | varchar(30) | `CancellationApproved` / `CancellationRejected` / `TicketChangeApproved` / `TicketChangeRejected` |
| `Title` | varchar(200) | dòng tiêu đề ngắn cho màn danh sách |
| `Body` | text | nội dung đầy đủ, phải đọc được **một mình** |
| `RelatedEntityType` | varchar(30) NULL | `CancellationRequest` / `TicketChange` — bảng mà cột dưới trỏ tới |
| `RelatedEntityId` | uuid NULL | id dòng yêu cầu mà thông báo nói về |
| `CreatedAt` | timestamptz | thời điểm sinh thông báo |

**Cố ý KHÔNG có `UpdatedAt`** — A4 gọi tên đúng bảng này ở nhóm chỉ-ghi-thêm. **Cũng không có
`IsDeleted`** (A4).

⚠️ **`UserId` ở đây là người NHẬN, không phải người THỰC HIỆN.** Trùng tên cột với `AuditLogs.UserId`
nhưng **khác nghĩa hẳn**: ở bảng nhật ký đó là quản lý vừa bấm duyệt, ở đây là hành khách được báo.
Đây là lý do §5 không được phép ghi id hành khách vào `AuditLogs.UserId` cho "cho có log".

⚠️ **Vì sao CHƯA có cột "đã đọc".** A4 xếp bảng này vào nhóm chỉ-ghi-thêm, còn đánh dấu đã đọc là một
phép **sửa**. Và chưa có ai tiêu thụ trạng thái đó: màn *"đánh dấu đã đọc"* là dòng 22 của **Sprint 5**
(Dương Thị Hạnh), badge chuông là dòng 21 (Nguyễn Đình Băng). Hình dạng đúng của nó — một cột
`ReadAt` trên dòng thông báo, hay bảng nối để mỗi **thiết bị** có trạng thái riêng — phụ thuộc vào
badge của Băng. Thêm một cột nullable sau này không tốn migrate dữ liệu; đoán sai bây giờ thì phải
sửa lại kèm migrate. **Đây là câu hỏi mở, không phải chỗ sót** (§9.3).

⚠️ **`RelatedEntityId` KHÔNG có khoá ngoại thật** — một cột không trỏ được vào hai bảng. Đây là lý do
`RelatedEntityType` tồn tại thành cột riêng thay vì dùng lối chuỗi `"<Tên bảng>:<Id>"` của
`AuditLog.Target`: bảng nhật ký là chuỗi để **đọc bằng mắt** trên màn Admin, còn đây là dữ liệu cho
**máy định tuyến** (bấm vào thông báo thì mở màn nào) — tách thành hai cột có kiểu thì frontend không
phải cắt chuỗi và không ghi ra được một chuỗi sai định dạng mà vẫn lưu.

Con trỏ đa hình này không mục theo thời gian vì mọi FK nghiệp vụ của dự án đều `Restrict` (A5): yêu
cầu huỷ/đổi **không bị xoá**. Nhưng vì nó không phải FK thật, thông báo **không bao giờ được dùng làm
nguồn dữ liệu nghiệp vụ** — nó là bản sao nội dung tại một thời điểm.

## 3. Kiến trúc: một cổng, hai đầu

```
Hoàng (dòng 5)  PATCH /cancellation-requests/{id}  hoặc  PATCH /ticket-changes/{id}
        │  lật trạng thái + SaveChanges  (xong, đã commit)
        ▼
ITicketRequestResultNotifier ── TicketRequestResultNotifier
        │                                   │
        │                                   ├─ 1. ghi 1 dòng Notifications   (bản ghi bền, A8.5)
        │                                   └─ 2. đẩy NotificationReceived  (nhắc tức thời)
        ▼
NotificationHub  /hubs/notifications   nhóm "user:{userId}"   [Authorize]
```

### 3.1 Vì sao là một cổng `ITicketRequestResultNotifier`, không phải hai lời gọi rời

Người gọi chỉ cần biết **một** việc: *"yêu cầu này vừa được xử lý, báo cho khách"*. Nếu để Hoàng tự
gọi `Notifications.Add(...)` rồi tự đẩy SignalR thì mỗi endpoint duyệt (hai cái, và Sprint 5 còn thêm)
phải chép lại: kiểm trạng thái, soạn nội dung, chống trùng, bắt lỗi ghi, bắt lỗi đẩy. Cổng này gom
đúng những thứ dễ chép sai đó vào một chỗ, và giữ nguyên tắc đã có của repo —
**service nghiệp vụ không cầm Hub trực tiếp** (cùng lối `ISeatMapNotifier` của Sprint 3).

### 3.2 Vì sao KHÔNG phải lớp bọc (decorator) như `Notifying*` của Sprint 3

Ba lớp `NotifyingSeatHold*` bọc **service thật đã tồn tại**. Ở đây chưa có gì để bọc: endpoint duyệt
là **task dòng 5 của Hoàng**, chưa viết, và hình dạng của nó (service riêng hay code thẳng trong
controller) là quyết định của Hoàng. Dựng một interface `ICancellationRequestService` giả để bọc là
tự đặt hình dạng cho file của người khác. Nên cổng ở đây là một **service gọi được**, Hoàng gọi ở
cuối handler của mình — và khi nào anh ấy có service riêng thì bọc lại là việc của một dòng.

### 3.3 Hợp đồng của cổng — hai chốt

| Chốt | Nghĩa |
|---|---|
| **Không bao giờ ném lỗi** | Người gọi đang ở cuối một thao tác **đã thành công**. Để lỗi thông báo nổi lên là biến lượt duyệt thành 500 — quản lý tưởng chưa duyệt được và bấm lại. Cùng hợp đồng với `IAuditLogService.RecordAsync` |
| **Không nhận `CancellationToken`** | Không được để dòng thông báo biến mất chỉ vì khách đóng tab ngay sau khi quản lý bấm duyệt — lúc đó `RequestAborted` của lượt PATCH **đã bị huỷ**. Truyền token vào đây là tự tay tạo ra lỗi "duyệt xong mà khách không bao giờ biết". Cùng lý do đã ghi ở `IAuditLogService` |

Thứ tự gọi: **sau khi trạng thái đã lưu**. Hàm đọc `Status` để biết phải báo kết quả gì, nên gọi
trước khi commit là báo sai (và nếu còn `Pending` thì hàm chỉ ghi log cảnh báo, không ghi dòng nào —
cố ý, xem §8). Hàm tự mở một lượt lưu riêng, **không** tham gia giao dịch của người gọi, và **không**
được dùng để bảo đảm tính nguyên tử cho nghiệp vụ.

### 3.4 ⏳ Hai dòng còn thiếu trong `Program.cs` — việc của Hoàng

`Program.cs` là **file của Phùng Duy Hoàng** (quy ước 4), nên nhánh này **không sửa**. Hai dòng cần
thêm, đặt cạnh khối SignalR sẵn có:

```csharp
// Trong khối đăng ký service (cạnh AddScoped<ISeatMapNotifier, SeatMapNotifier>()):
builder.Services.AddScoped<ITicketRequestResultNotifier, TicketRequestResultNotifier>();

// Cạnh app.MapHub<SeatMapHub>("/hubs/seat-map") — SAU app.UseCors(AppCors):
app.MapHub<NotificationHub>("/hubs/notifications");
```

Không cần dòng nào nữa: `AddSignalR()` đã có, và `IHubContext<NotificationHub, INotificationClient>`
do chính `AddSignalR()` cấp.

⚠️ **Cho tới khi hai dòng đó có mặt, thứ duy nhất chạy được là phần GHI.** `Notifications` vẫn được
ghi đầy đủ (đó là nửa A8.5 quan trọng hơn), nhưng sự kiện SignalR đẩy vào một hub **không tồn tại**
nên `IHubContext` ném — và notifier nuốt lỗi (§3.3) nên triệu chứng duy nhất là **một dòng log
warning mỗi lượt duyệt**. Đúng kiểu lỗi im lặng mà §3.5 phải bắt.

### 3.5 ⏳ Test ghim chỗ nối — chưa viết được, và vì sao

`SeatMapRealtimeTests` ghim *"ba interface phải trỏ vào ba lớp bọc"* và `SeatHoldAbuseWiringTests`
ghim *"job nền có thật sự được đăng ký"*. Cùng loại test đó cho nhánh này **chưa viết được**: nó phải
`GetRequiredService<ITicketRequestResultNotifier>()` và `POST /hubs/notifications/negotiate`, mà cả
hai đều đỏ cho tới khi hai dòng ở §3.4 có mặt. Viết trước là để lại một test đỏ treo trên `main`.

**Việc của người thêm hai dòng đó (Hoàng):** sau khi thêm, chép `TestAppFactory` từ
`SeatHoldAbuseWiringTests` và ghim đúng hai điều —

1. `factory.Services.GetRequiredService<ITicketRequestResultNotifier>()` là `TicketRequestResultNotifier`
   (thiếu `AddScoped` thì endpoint của anh ấy ném `InvalidOperationException` ngay lượt duyệt đầu);
2. `POST /hubs/notifications/negotiate` trả **401** khi không có token (hub này có `[Authorize]`, khác
   `/hubs/seat-map` — xem §3.6) và trả **200** khi có token hợp lệ.

### 3.6 🔴 Token của hub đi bằng query string — chỗ dễ hỏng nhất của cả nhánh này

`NotificationHub` có `[Authorize]` vì thông báo là **dữ liệu riêng của một người**. Nhưng trình duyệt
**không đặt được header `Authorization` cho WebSocket** — API WebSocket của trình duyệt không có chỗ
truyền header. Client SignalR JS vì vậy gửi token qua query string `?access_token=…`.

Phía server, việc đọc query string đó nằm ở **`JwtMiddleware.OnMessageReceived`** (file của Dăm, cùng
story 22) — **chỉ áp cho đường dẫn `/hubs`**. Đã thêm trong nhánh này.

| Nếu thiếu | Triệu chứng |
|---|---|
| `OnMessageReceived` | `[Authorize]` trả **401** cho mọi lượt negotiate → không client nào nối được |
| `[Authorize]` trên hub | Nối được nhưng `Context.UserIdentifier` rỗng → connection **không vào nhóm nào**, thông báo rơi vào khoảng không, **không có lỗi nào** |
| Token ở client (`accessTokenFactory`) | y hệt dòng trên |

Hai dòng đầu đã có; dòng thứ ba là việc của frontend — xem mục `/hubs/notifications` của
`docs/api-contract.md`.

⚠️ **Đánh đổi còn lại, đã biết:** token nằm trong URL thì log truy cập của hạ tầng (IIS/Nginx/Render)
có thể ghi lại nó. Giảm nhẹ đã có: `AuditLogMiddleware.IgnoredPaths` đã loại cả tiền tố `/hubs` nên
URL mang token **không** đi vào bảng `AuditLogs`. Cách xử lý triệt để (token riêng ngắn hạn cho hub,
hoặc xác thực bằng cookie) vượt phạm vi đồ án — đây là giới hạn chung của mọi app SignalR dùng JWT với
WebSocket. Người vận hành phải bảo vệ log truy cập.

### 3.7 Nhóm suy từ token, KHÔNG từ tham số client gửi lên

Client **không** gọi method nào để vào nhóm: `NotificationHub.OnConnectedAsync` tự thêm connection vào
nhóm `user:{userId}` lấy từ `Context.UserIdentifier`. Nhờ vậy **không có cách nào một client xin vào
nhóm của người khác** — khác `SeatMapHub.JoinTrip(tripId)` (ở đó client gửi tham số, chấp nhận được vì
sơ đồ ghế là thông tin công khai).

Tên nhóm chỉ sinh ở **một chỗ** (`NotificationHub.GroupOf`) cho cả hai đầu: hub dùng bản nhận `string`
(chuỗi thô của claim), notifier dùng bản nhận `Guid`. Hai đầu phải ra **đúng cùng một chuỗi** —
`TokenService` phát claim `NameIdentifier` bằng `user.Id.ToString()`, và `Guid.ToString()` cho ra đúng
định dạng đó. Lệch định dạng (chữ hoa/thường) là hai nhóm khác nhau: khách không nhận được gì mà
không có lỗi nào để lần.

## 4. Chống ghi trùng: đọc trước khi ghi

Một quyết định = **một** thông báo. Trước khi ghi, notifier hỏi: *"đã có dòng nào cho
`(UserId, Type, RelatedEntityId)` chưa?"* — có thì bỏ qua.

**Vì sao cần:** endpoint duyệt là PATCH, và PATCH thì client retry được (mạng chập, hoặc quản lý bấm
hai lần). Không có chốt này thì khách thấy **hai dòng y hệt nhau** cho cùng một kết quả.

**Vì sao lọc theo `RelatedEntityId` chứ không theo người nhận:** khách có thể có nhiều yêu cầu
(một vé bị từ chối, rồi xin đổi một vé khác) — mỗi yêu cầu phải được báo riêng. Lọc theo người nhận
sẽ nuốt mất thông báo thứ hai.

⚠️ **Đây là phép ĐỌC TRƯỚC KHI GHI nên chỉ đúng khi chạy MỘT bản app** — cùng giới hạn đã ghi ở
`docs/26` cho phép chống ghi trùng của job cảnh báo giữ chỗ. Dự án đang chạy hai bản (Render + máy
thành viên) chung một CSDL, nên hai lượt duyệt **đồng thời** trên hai máy có thể cùng đọc lúc bảng
còn trống rồi cùng ghi.

⚠️ **Cố ý KHÔNG đặt unique index ở tầng CSDL.** Khoá đó sẽ là `(UserId, Type, RelatedEntityId)`. Nhưng
Sprint 5 sẽ có loại thông báo **được phép lặp trên cùng một bản ghi** — "xe trễ chuyến" cho cùng một
chuyến có thể bắn nhiều lần khi sự cố kéo dài (dòng 18/29 Sprint 5). Một unique index dựng bây giờ sẽ
**chặn nhầm nghiệp vụ chưa viết**, và lỗi đó chỉ lộ ra ở Sprint 5 dưới dạng "thông báo thứ hai biến
mất". Dư một dòng trùng là thiệt hại nhỏ hơn hẳn. Ai định "siết lại cho chắc" thì đọc mục này trước:
đây là **quyết định**, không phải chỗ sót.

**Chốt cứng thật của nhánh đua nằm ở chỗ khác:** hai quản lý cùng duyệt một yêu cầu thì lượt thua
nhận `DbUpdateConcurrencyException` từ `xmin` của `CancellationRequests`/`TicketChanges`
(`docs/32` §4.3) — tức là **chỉ một lượt duyệt tới được** notifier. Chốt chống trùng ở đây chỉ để
chặn lượt gọi lại của **cùng một** lượt duyệt.

## 5. Vế "ghi log" của tên task — vì sao KHÔNG ghi thêm `AuditLogs`

Task tên là *"**Ghi log** và thông báo kết quả xử lý cho hành khách"*, nên chỗ này phải nói rõ: trong
nhánh này **không có dòng `AuditLogs` nào được ghi**, và đó là chủ ý.

**Vì sao:** `AuditLogMiddleware` **đã tự ghi** một dòng `Action = Update`, `Target =
"CancellationRequests:{id}"` (hoặc `"TicketChanges:{id}"`) cho chính lượt PATCH mà quản lý vừa gọi,
kèm người thực hiện và IP. Ghi thêm một dòng nữa ở đây là **mỗi lượt duyệt sinh hai dòng nhật ký cho
một sự việc**, làm loãng đúng màn hình mà Admin dùng để kiểm toán (US 23).

Và không thể "gộp cho gọn" bằng cách ghi dòng nhật ký đó với `UserId` = **hành khách**: `AuditLogs.UserId`
là người **THỰC HIỆN** hành động (`AuditLog.AuditAction` ghi rõ), nên điền id hành khách vào đó là
**ghi sai sự thật** — màn nhật ký sẽ nói khách tự duyệt yêu cầu của mình.

**Vậy "ghi log" nằm ở đâu:** ba chỗ, đều đã có trong nhánh này —

| Chỗ | Trả lời câu hỏi gì |
|---|---|
| Dòng `Notifications` (bản ghi bền) | *"Hệ thống đã báo gì cho khách này, lúc nào?"* — câu đầu tiên phải trả lời khi khách khiếu nại là chưa nhận được thông báo |
| Dòng `AuditLogs` do middleware ghi | *"Ai đã duyệt yêu cầu này, từ IP nào, lúc nào?"* (US 23) — **không** chép lại |
| `ILogger` có cấu trúc, ở **cả ba** nhánh | Vận hành: ghi được (Information) · ghi hỏng (Error) · kênh đẩy hỏng (Warning) · gọi sai trạng thái (Warning) |

⚠️ Cố ý **không** dùng `AuditAction.Warning` như task anh em Sprint 3 (*"Ghi log và cảnh báo khi một
tài khoản giữ chỗ quá nhiều lần"* — `docs/26`): `Warning` là **hàng đợi giữa job nền và màn Admin**,
dành cho thứ Admin cần thấy. "Khách đã được báo kết quả" không phải một bất thường cần Admin soi.

## 6. Nội dung thông báo — ba chốt

### 6.1 🔴 Không có SỐ TIỀN nào trong nội dung

Hai lý do, cả hai đều có thật:

1. **Việc hoàn tiền chưa xong.** Số tiền hoàn do bảng `Refunds` (US 8, **sprint khác**) thực hiện, có
   vòng đời riêng kèm retry. Thông báo ghi *"đã hoàn 70.000đ"* khi lượt hoàn còn chưa chạy là **hứa
   trước một việc hệ thống chưa làm**.
2. **Định dạng tiền là việc của frontend.** Nhét chuỗi `"70.000 đ"` vào CSDL là đóng đinh định dạng
   và ngôn ngữ vào dữ liệu, trong khi màn theo dõi yêu cầu (Băng, dòng 10) **đã hiển thị** con số đó
   từ chính dòng yêu cầu, và phí huỷ là cột `CancellationFee` có sẵn.

Test `Duyet_yeu_cau_huy_thi_ghi_mot_dong_va_day_dung_nhom` ghim đúng điều này: body **không được chứa
chữ số nào**. Ai thêm `"Phí huỷ 30.000đ"` vào sẽ làm đỏ test — và đó là chủ ý.

### 6.2 Lý do từ chối chép NGUYÊN VĂN vào `Body`

Lý do từ chối là **nội dung duy nhất** trả lời được câu *"vì sao tôi bị từ chối"*. A8.5 chốt khách đọc
lại được khi **offline**, mà một câu trỏ sang màn khác ("bấm để xem chi tiết") thì không đọc được ở
đó. Nên `RejectReason` được chép thẳng vào `Body`, chỉ cắt khoảng trắng thừa.

⚠️ **Không cắt ngắn, không kiểm độ dài** — cột là `text`. Việc chặn lý do dài vô lý là việc của
validation ở endpoint duyệt (Hoàng, dòng 5), không phải của người đưa thư. Quản lý không nêu lý do
(cột nullable, hợp lệ) thì `Body` ghi rõ *"Người duyệt không nêu lý do."* chứ **không** để cụt thành
"…đã bị từ chối." — để cụt thì khách không phân biệt được với trường hợp lý do bị mất.

### 6.3 Bốn câu, không dùng template

| `Type` | `Title` | `Body` |
|---|---|---|
| `CancellationApproved` | Đã duyệt yêu cầu huỷ vé | Yêu cầu huỷ vé của bạn đã được duyệt. Nhà xe sẽ xử lý hoàn tiền theo quy định. |
| `CancellationRejected` | Từ chối yêu cầu huỷ vé | Yêu cầu huỷ vé của bạn đã bị từ chối. **Lý do: …** |
| `TicketChangeApproved` | Đã duyệt yêu cầu đổi chuyến | Yêu cầu đổi chuyến của bạn đã được duyệt. Vé mới của bạn đã được phát hành. |
| `TicketChangeRejected` | Từ chối yêu cầu đổi chuyến | Yêu cầu đổi chuyến của bạn đã bị từ chối. **Lý do: …** |

**Vì sao là bốn hằng chuỗi trong code chứ không phải bảng `NotificationTemplates`:** bảng template là
việc của **Sprint 5 dòng 24** (màn Admin quản lý mẫu) — dựng nó bây giờ là làm trước một màn hình của
người khác, và bốn câu này chưa ai yêu cầu sửa được. Chuyển sang template sau là thay chỗ đọc chuỗi,
không phải sửa schema.

⚠️ **Câu "Vé mới của bạn đã được phát hành" chỉ đúng nếu lượt duyệt thật sự phát hành vé mới**
(`docs/32` §3.1) — notifier **không** kiểm `NewTicketId` trước khi báo. Kiểm ở đây là đoán hộ thứ tự
công việc trong handler của Hoàng; nếu anh ấy gọi thông báo trước khi phát hành vé thì câu này nói
trước sự thật vài mili giây. Ràng buộc thật nằm ở §3.3: **gọi sau khi đã commit**.

## 7. Chỉ mục — dùng đúng cái có sẵn

| Truy vấn | Chỉ mục |
|---|---|
| "Danh sách thông báo của tôi, mới nhất trước" (Sprint 5 dòng 19) | `IX_Notifications_UserId_CreatedAt` — **đúng cặp cột A6** |
| Phép chống ghi trùng của notifier (§4) | Cùng chỉ mục trên: nó lọc `UserId` trước, mà một khách chỉ có vài chục dòng |
| Khoá ngoại `UserId` | Cùng chỉ mục trên — EF thấy cột đã có chỉ mục nên không sinh thêm chỉ mục FK trùng |

⚠️ **Cố ý KHÔNG thêm chỉ mục cho `(RelatedEntityType, RelatedEntityId)`**: câu hỏi "yêu cầu này đã
được báo chưa" luôn lọc kèm `UserId` (một yêu cầu thuộc về đúng một người), nên chỉ mục trên đã thu
hẹp về tập nhỏ trước khi tới hai cột kia. Thêm chỉ mục thứ hai là trả giá ghi cho **mọi** dòng thông
báo để đổi lấy một phép lọc trên tập đã nhỏ.

## 8. Việc còn lại của người khác

- **Hoàng** (dòng 5) — **hai dòng `Program.cs` ở §3.4** là việc chặn đường: không có chúng thì chỉ nửa
  ghi chạy được. Sau đó, ở cuối handler `PATCH /cancellation-requests/{id}` và
  `PATCH /ticket-changes/{id}`, **sau khi đã lưu trạng thái**, gọi đúng một hàm:
  `await _notifier.NotifyCancellationReviewedAsync(request)` (hoặc `NotifyTicketChangeReviewedAsync`).
  ⚠️ **Đừng truyền `HttpContext.RequestAborted`** vào đó — hàm cố ý không nhận token, xem §3.3. Và
  **đừng bọc lời gọi trong giao dịch** rồi kỳ vọng tính nguyên tử: hàm tự mở lượt lưu riêng.
  ⚠️ Nếu yêu cầu còn `Pending` thì hàm **không ghi gì** (chỉ log warning) — nên gọi trước khi commit
  là mất thông báo trong im lặng.
- **Hạnh** (dòng 8, 11) và **Băng** (dòng 10) — bốn `Type` và hai `RelatedEntityType` ở §2/§6.3 là
  **tập giá trị để hiển thị**; thêm một giá trị là đổi hợp đồng với frontend (luật 5).
  `Body` phải hiển thị được **xuống dòng và độ dài bất kỳ** (lý do từ chối là văn bản tự do).
- **Thịnh** (dòng 9) — component chọn chuyến mới không liên quan thông báo; con số chênh lệch để hiển
  thị vẫn là `NewPrice - Ticket.Price` (`docs/32` §3).
- **Hiếu** (dòng 3) và **Kiên** (dòng 4) — không phải gọi cổng thông báo: thông báo chỉ đi ở bước
  **duyệt/từ chối**, không đi ở bước khách **gửi** yêu cầu. Nhưng nếu endpoint gửi yêu cầu của ai đó
  phát hành vé mới thì nhớ §6.3.
- **Giàng A Vàng** (dòng 12–13) — test API huỷ/đổi phải phủ: duyệt xong thì **có đúng một** dòng
  `Notifications` cho khách **chủ vé** (không phải cho quản lý), và gọi lại lượt duyệt thứ hai
  **không** sinh dòng thứ hai. ⚠️ InMemory **không** cưỡng chế chỉ mục/FK (`docs/32` §4.4), nên test
  xanh không nói gì về `IX_Notifications_UserId_CreatedAt`. Phần token-của-hub (§3.6) thì **không**
  test được bằng InMemory — phải là test chạy `TestAppFactory`, xem §3.5.
- **Ai cần thêm cột/bảng** — nhắn Dăm, không tự sửa `Migrations/*`, `ModelSnapshot.cs`, `Entities/`.

## 9. Việc cần báo nhóm (không phải việc code)

1. **A7 "mỗi sprint một migration" lại lệch thêm một nhịp.** Bảng `Notifications` thuộc **Sprint 5**
   (dòng 14) nhưng migration của nó sinh trong nhánh Sprint 4 này (§10), vì task dòng 7 ở Sprint 4
   không chạy được nếu thiếu bảng. Tên migration vì vậy đặt theo mốc **Sprint 4** (nơi công việc thật
   sự xảy ra), và bảng phân công Sprint 5 dòng 14 coi như **bớt một bảng**: còn lại
   `NotificationTemplates` + `Subscriptions`. Nhóm chốt lại để dòng 14 không bị làm trùng.
   > ✅ **Cập nhật 14/10/2026:** hai bảng còn lại **đã dựng xong** trên nhánh
   > `feature/10-migrate-notificationtemplates-subscriptions` — xem
   > **`docs/37-csdl-mau-thong-bao-va-theo-doi.md`**. Dòng 14 coi như xong phần CSDL, không ai làm lại
   > `Notifications`. Mục 3 dưới đây (hình dạng "đã đọc") **vẫn còn treo** — nhánh đó không mở lại.
2. **`docs/03-quy-uoc.md` vẫn không tồn tại trong repo.** Mọi tham chiếu A1–A9 ở đây chỉ còn là trích
   dẫn trong code và tài liệu. Đây là lần thứ **năm** việc này được nhắc (bốn lần trước: `docs/32` §7.2).
3. **Cần chốt hình dạng "đã đọc" của thông báo trước Sprint 5.** §2 để trống có chủ ý, nhưng hai task
   Sprint 5 (dòng 21 badge chuông của Băng, dòng 22 đánh dấu đã đọc của Hạnh) **đều giả định có trạng
   thái đã đọc**. Nhóm chốt: một cột `ReadAt` trên `Notifications` (một trạng thái cho mọi thiết bị)
   hay bảng nối riêng (mỗi thiết bị một trạng thái)? Chốt muộn thì phải migrate kèm sửa dữ liệu.
4. **Xác thực cho hub phải được biết trước khi deploy.** §3.6 dùng query-string token — cách duy nhất
   với WebSocket của trình duyệt, nhưng nó làm token xuất hiện trong log truy cập của hạ tầng. Nhóm
   cần biết để bảo vệ log (hoặc chấp nhận đánh đổi này một cách có ý thức) **trước** khi bật hub ở
   bản deploy Render.
5. **`docs/api-contract.md` chưa có mục cho endpoint REST của US 5** (`docs/32` §7.4) — vẫn đang chờ
   Hiếu chủ trì. Mục `/hubs/notifications` của nhánh này **đã thêm** vì nó là hình dạng API do chính
   task dòng 7 sinh ra.

## 10. ⏳ Migration — lệnh cần chạy (việc của Dăm)

**Chưa sinh.** `dotnet ef migrations add` chỉ Dăm được chạy (A7 — luật cứng), nên nhánh này dừng ở
entity + cấu hình; model **đang lệch** snapshot và đó là trạng thái đúng của một nhánh chờ sinh
migration.

Máy chưa có `appsettings.Development.json` (file bị .gitignore, repo public) thì mọi lệnh `dotnet ef`
cần các biến môi trường **giá trị giả** để host design-time dựng được model. ⚠️ **Đừng tạo file
`appsettings.Development.json` thật** — `scripts/setup-csdl.sh` bỏ qua file đã tồn tại nên nó sẽ chặn
luôn phần cài đặt thật về sau (cùng cảnh báo đã ghi ở `docs/32` §8):

```bash
ConnectionStrings__Default="<chuoi-ket-noi-gia — chi can dung dinh dang>" \
Jwt__Key="<chuoi-bat-ky-dai-it-nhat-32-byte>" \
Jwt__Issuer="SmartBus" \
Jwt__Audience="SmartBusClient" \
dotnet ef migrations add Sprint4_Notifications --project backend/SmartBus.Api
```

**Tên `Sprint4_…` là chủ ý** — xem §9.1. Nhánh đang đứng trên migration
`20261012023745_Sprint4_CancellationRequests_TicketChanges`, nên migration mới **chỉ chứa bảng
`Notifications`**, không sinh lại hai bảng kia.

**Hai phép kiểm sau khi sinh, không cần CSDL thật** (cùng bộ kiểm đã dùng ở `docs/32` §8):

| Phép kiểm | Kỳ vọng |
|---|---|
| `dotnet ef migrations has-pending-model-changes --no-build` | ✅ *"No changes have been made to the model since the last migration"* |
| `dotnet ef migrations script <Sprint4_CancellationRequests_TicketChanges> <Sprint4_Notifications> --no-build` | **CHỈ** có `CREATE TABLE "Notifications"` + `CREATE INDEX "IX_Notifications_UserId_CreatedAt"` + 1 dòng `INSERT` vào `__EFMigrationsHistory`. **Không** `ALTER TABLE`, **không** `DROP`, **không** `UPDATE` — migration này không được chạm bảng nào khác |

Đối chiếu SQL với §2: cột `CreatedAt` và `RelatedEntityType`/`RelatedEntityId` phải là
`timestamp with time zone` và `character varying(30)`; `Body` phải là `text` (**không** `varchar`);
khoá ngoại `UserId` phải là **`ON DELETE RESTRICT`**; **không** được có cột `xmin` nào trong SQL (nếu
có dòng `xmin = table.Column<uint>(type: "xid", …)` trong file `.cs` thì đó vẫn là đúng — Npgsql lọc
cột hệ thống `xid` ra khỏi DDL, cùng hiện tượng đã ghi ở `docs/32` §8 — nhưng nhánh này **không** khai
`xmin` cho `Notifications`).

## 11. Đã kiểm trong nhánh này

| Phép kiểm | Kết quả |
|---|---|
| `dotnet build backend/SmartBus.Api/SmartBus.Api.csproj` | ✅ 0 warning, 0 error |
| `dotnet test backend/SmartBus.Tests` (toàn bộ) | ✅ **1269/1269 xanh** — gồm `TicketRequestResultNotifierTests` (10 ca mới) và toàn bộ `JwtAuthTests`/`RbacTests` (bài kiểm hồi quy cho thay đổi `OnMessageReceived` ở §3.6) |
| `TicketRequestResultNotifierTests` | ✅ 10 ca: hai nhánh duyệt/từ chối của cả hai loại yêu cầu · lý do từ chối chép nguyên văn · lý do trống · trạng thái `Pending` không ghi gì · gọi lại không sinh dòng thứ hai · yêu cầu khác vẫn báo bình thường · kênh đẩy hỏng không ném lỗi và dòng vẫn còn · ghi hỏng không ném lỗi · nhóm đúng `user:{id}` và body không có chữ số |

⚠️ **Bộ test chạy trên InMemory, mà InMemory KHÔNG cưỡng chế chỉ mục lẫn khoá ngoại** — nên 1269 xanh
**không** nói gì về việc `IX_Notifications_UserId_CreatedAt` hay FK `Restrict` có tồn tại thật hay
không. Hai thứ đó chỉ có thật ở PostgreSQL, tức là sau §10.
