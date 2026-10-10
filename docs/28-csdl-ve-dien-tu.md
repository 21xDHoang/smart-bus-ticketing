# CSDL vé điện tử — Sprint 3: cấu hình xong, migration chưa sinh

> Viết cho các task đang chờ bảng vé: **Hiếu** (API tra cứu vé của tôi + service phát hành vé sau khi
> thanh toán), **Kiên** (service sinh mã QR duy nhất + ký số chống làm giả), **Băng/Hạnh/Thịnh**
> (màn hình "Vé của tôi" và màn QR đang chạy mock ở `frontend/src/api/ticketApi.ts`).
>
> 🟡 **Trạng thái 10/10/2026:** entity `Ticket` + cấu hình `AppDbContext.Ticket.cs` **đã xong**; bảng
> `Tickets` sẽ ra đời trong migration `Sprint3_Payments_Vouchers_Tickets` — Vàng Thị Dăm, task Sprint 3
> *"Migrate bảng Tickets, TicketQRCodes"* — **chạy bằng đúng một lệnh `dotnet ef migrations add` còn
> nợ**. Cùng lệnh đó trả luôn nợ migration của `Payments` (dòng 27) và `Vouchers` + `VoucherUsages`
> (dòng 50). Cho tới lúc đó, test tích hợp vẫn chạy được trên EF InMemory, nhưng InMemory **không**
> cưỡng chế chỉ mục/FK — xem cảnh báo ở §3.

## 1. Bảng `Tickets` — một vé đã phát hành

| Cột | Kiểu | Ghi chú |
|---|---|---|
| `Id` | uuid | khoá chính |
| `Code` | varchar(200) | **unique** — mã vé, đúng bằng nội dung mã QR in trên vé (A6) |
| `TripId` | uuid | FK → `Trips`, `RESTRICT` |
| `SeatId` | uuid | FK → `Seats`, `RESTRICT` |
| `UserId` | uuid | FK → `Users`, `RESTRICT` — chủ vé |
| `BoardingStopId` | uuid NULL | FK → `Stops`, `RESTRICT` — chỉ để hiển thị (A8.1) |
| `AlightingStopId` | uuid NULL | FK → `Stops`, `RESTRICT` — chỉ để hiển thị (A8.1) |
| `Price` | numeric(12,2) | giá đã thu, **chụp lại** lúc phát hành |
| `Status` | varchar(20) | `Paid` / `Used` / `Cancelled` |
| `CreatedAt` | timestamptz | thời điểm phát hành |
| `UsedAt` | timestamptz NULL | chỉ khác null khi `Status = 'Used'` |
| `UpdatedAt` | timestamptz NULL | vé CÓ bị sửa (`Paid` → `Used`/`Cancelled`) nên A4 buộc có |

**Vì sao `Price` chụp lại chứ không tra `Fares`:** bảng giá là thứ sửa được. Tra lại lúc đọc sẽ khiến
vé cũ hiển thị theo giá mới, tức hoá đơn đã xuất không còn khớp doanh thu. `Entities/MonthlyPass.cs`
đã ghi trước dự định này cho vé lượt; ở đây thực hiện đúng như vậy.

**Vì sao trạm lên/xuống nullable và không phải khoá nghiệp vụ:** vé đặt theo **cả chuyến** (A8.1).
Cặp trạm chỉ để in trên vé và hiển thị; hai vé cùng chuyến khác trạm hoàn toàn hợp lệ và không ràng
buộc gì nhau. Nullable vì luồng mua ở màn chọn ghế có thể không hỏi trạm.

**Cố ý KHÔNG có `IsDeleted`** (A4) và **KHÔNG có `xmin`** — khác `Payments`/`Vouchers`. Hai bảng kia
cần concurrency token vì tính năng của chúng có nhánh đua đã gặp thật (callback cổng gửi lại; hai
lượt áp voucher cùng lúc). Bảng vé chưa có nhánh đua nào thuộc Sprint 3: bước phát hành đã được chốt
bằng idempotency phía `Payments` cộng chỉ mục duy nhất ở §3, còn việc soát vé (`Paid` → `Used`) là
story khác. Thêm sau **không tốn migration cột** — `xmin` là cột hệ thống của PostgreSQL.

### ⚠️ Cột KHÔNG có trong bảng, nhưng vẫn có trong hợp đồng API

Bảng trường của `docs/api-contract.md` liệt kê `routeCode`, `routeName`, `origin`, `destination`,
`departureTime`, `seatNumber`, `boardingStopName`, `alightingStopName` — **tám trường này không phải
cột của `Tickets`**. Chúng là kết quả join lúc đọc:

| Trường API | Lấy từ |
|---|---|
| `routeCode`, `routeName`, `origin`, `destination`, `departureTime` | `Trips` → `Routes` (+ `Stops` đầu/cuối) theo `TripId` |
| `seatNumber` | `Seats` theo `SeatId` |
| `boardingStopName`, `alightingStopName` | `Stops` theo `BoardingStopId` / `AlightingStopId` |

Đây **không phải** chỗ tiếc một phép join: chép tên tuyến / giờ chạy vào vé là nhân bản dữ liệu sẽ
lệch ngay lần đầu ai đó sửa tên tuyến. Chỉ **giá** mới cần chụp lại, vì lý do ở trên. Hình dạng
response vì thế không đổi — Hiếu viết `TicketResponse` theo đúng bảng trường đã chốt.

## 2. Vì sao KHÔNG có bảng `TicketQRCodes`

Tên task ghi *"Migrate bảng Tickets, TicketQRCodes"* — hai bảng. Bản dựng trong migration này có
**một** bảng, và đây là lý do, để lần sau không ai đi tìm bảng thứ hai:

| Nguồn | Nói gì |
|---|---|
| **A9** (danh sách bảng đóng băng) | **Không có** `TicketQRCodes`. Bảng vé là A9 #14 |
| **A6** (khoá duy nhất) | `Tickets \| QrCode` — khoá duy nhất nằm **trên bảng vé** |
| **`docs/api-contract.md`** § "Hình dạng `Ticket`" | Chỉ có `code` nằm trên vé; không endpoint nào trả về một "QR code" như thực thể riêng |
| Code | Không có tham chiếu nào tới `TicketQRCodes` ngoài đúng một dòng nhắc trong hợp đồng |

Tách một bảng chỉ để chứa **một cột** vừa thêm một phép join cho lượt quét nóng nhất của tính năng
(mỗi lượt soát vé đều tra theo mã QR), vừa là **thêm bảng ngoài danh sách A9** — việc mà ranh giới
vai trò của chủ CSDL không cho tự làm.

**Chốt 10/10/2026 (đã hỏi và được xác nhận trước khi code):** một bảng `Tickets`, mã QR là cột
`Code`. Nếu sau này Kiên cần lưu thêm **chữ ký số** tách khỏi nội dung QR thì đó là **một cột mới
trên `Tickets`** (một migration nhỏ, do Dăm chạy), không phải một bảng mới — trừ khi nhóm chủ trương
cho một vé có NHIỀU mã QR theo thời gian (xoay mã khi nghi lộ), lúc đó mới cần bảng riêng và phải
bổ sung A9 trước.

Độ dài `varchar(200)` là mức chừa cho payload ký số: dạng `SBT1:{ticketId}.{chuỗiBase64}` rơi vào
khoảng 90–130 ký tự. Hiện chưa có service đó nên bên phát hành tạm dùng `Guid` (36 ký tự) — đúng
cách tạm mà `docs/api-contract.md` bước 2 đã chốt. **Kiên: mã QR của anh phải nằm trong 200 ký tự**;
cần dài hơn thì nhắn Dăm nới cột, đừng để Postgres cắt cụt âm thầm.

## 3. Chống bán trùng ghế — ở tầng CSDL

```sql
CREATE UNIQUE INDEX "IX_Tickets_TripId_SeatId_Active"
    ON "Tickets" ("TripId", "SeatId")
    WHERE "Status" IN ('Held', 'Paid');
```

Đây là bản gốc mà `SeatHolds` đang mô phỏng (`WHERE "Status" = 'Holding'` — `docs/26` §3).

**Vì sao phải là chỉ mục ĐIỀU KIỆN chứ không phải unique thường:** một ghế của một chuyến hợp lệ khi
có nhiều vé theo thời gian — vé cũ bị huỷ, rồi khách khác mua lại chính ghế đó. Unique thường trên
`(TripId, SeatId)` sẽ chặn vĩnh viễn việc bán lại; điều kiện `Status` chỉ chặn khi vé **đang có hiệu
lực**, tức đúng lúc cần chặn. `Cancelled` không chiếm ghế.

⚠️ **Nhánh `'Held'` là chữ của A6, giữ nguyên dù enum `TicketStatus` không có giá trị này.**
Không dòng nào mang `'Held'` (vé sinh thẳng ở `Paid`), nên nhánh đó là **dự phòng vô hại** — và nếu
sau này nhóm cho vé sinh ngay từ lúc giữ chỗ thì chỉ mục đã sẵn sàng. Đừng "dọn" nó đi: đó là chữ
của một luật cứng. Lý do đầy đủ ở doc-comment `Entities/TicketStatus.cs`.

**Vì sao điều kiện này sống ở tầng CSDL chứ không ở service:** hai request cùng đọc "ghế còn trống"
rồi cùng ghi là chuyện xảy ra được; service không tự lo được. Cùng lập luận đã ghi ở `docs/26` §3.

⚠️ **Bộ test chạy trên provider InMemory**, mà InMemory **KHÔNG cưỡng chế unique index** — nên
`Tickets` xanh trong bộ test **không** nói gì về việc chỉ mục này có tồn tại hay không. Chốt chống
bán trùng ghế chỉ tồn tại thật ở PostgreSQL. Đây là lý do §5 dưới đây đáng đọc.

## 4. Chỉ mục đã đánh — dùng đúng cái có sẵn

| Truy vấn | Chỉ mục |
|---|---|
| Cổng soát vé tra theo mã QR (`WHERE Code = @c`) | `IX_Tickets_Code` (**unique**) |
| Chống bán trùng ghế | `IX_Tickets_TripId_SeatId_Active` (unique, **có điều kiện**) |
| Danh sách khách của chuyến (`WHERE TripId = @t`) | `IX_Tickets_TripId` |
| "Vé của tôi" (`WHERE UserId = @u`) | `IX_Tickets_UserId` |
| Giao dịch này đã sinh vé nào | `IX_Payments_TicketId` |

🔴 **`IX_Tickets_TripId` KHÔNG thừa** dù chỉ mục điều kiện ở trên cũng mở đầu bằng `TripId`.
PostgreSQL chỉ dùng được một partial index khi truy vấn **chứng minh được** điều kiện của nó — câu
hỏi "vé của chuyến này" không nói gì về `Status` nên không thoả, và bộ lập kế hoạch sẽ bỏ qua chỉ
mục đó. Đây cũng là chỗ EF dễ đánh lừa: quy ước sinh chỉ mục cho khoá ngoại của EF thấy `TripId` đã
là cột tiền tố của một chỉ mục đã khai nên nó **không** tự sinh thêm — khai tường minh là cách duy
nhất để có chỉ mục dùng được cho truy vấn này.

Chỉ mục cho `SeatId`, `BoardingStopId`, `AlightingStopId` do EF tự sinh (không phải cột tiền tố của
chỉ mục nào), không khai tay.

## 5. FK `Payments.TicketId` đã nối

`Entities/Payment.cs` và `AppDbContext.Payment.cs` để lại ghi chú *"FK/nav sang Tickets để trống tới
khi bảng vé migrate — lúc đó nối thêm HasOne + Restrict đúng A5"*. Bảng vé đã có, nên FK **đã nối
trong chính migration này** (`RESTRICT` tường minh — xoá một vé không được kéo theo dòng tiền đã
thu). Cột nullable nên quan hệ là tuỳ chọn: giao dịch `Pending` chưa có vé.

Nối ngay bây giờ là **rẻ nhất**: `Payments` cũng chưa có bảng, cả hai bảng sinh ra trong cùng một
lệnh `migrations add`. Để sau sẽ tốn thêm một migration chỉ chứa một khoá ngoại.

⚠️ **`PaymentSettlementServiceTests` đang seed `TicketId = Guid.NewGuid()` mà không có vé tương
ứng.** InMemory bỏ qua FK nên test vẫn xanh, nhưng từ giờ nó **không còn mô tả đúng** ràng buộc
thật: trên PostgreSQL, một `TicketId` trỏ vào vé không tồn tại sẽ bị chặn. Ai sửa file test đó thì
seed thêm dòng `Tickets` thật, đừng nới FK.

## 6. Việc còn lại của người khác

- **Hiếu** — ba việc, theo đúng thứ tự trong `docs/api-contract.md` mục "Vé điện tử":
  1. **Service phát hành vé** (không phải endpoint): chống phát hành trùng theo `paymentCode` → tạo
     `Ticket` với `Status = 'Paid'` và `Code` = mã QR duy nhất (tạm `Guid` khi Kiên chưa có service)
     → lật `SeatHolds` của phiên sang `Confirmed` + ghi `SeatHoldLogs` `Action = 'Confirmed'`
     (`docs/26` §1) → ghi ngược `Payments.TicketId`. Nhớ **bọc lỗi unique** của `IX_Tickets_Code` và
     `IX_Tickets_TripId_SeatId_Active` để lượt gọi lại trả về vé cũ thay vì ném 500 — đó là nửa sau
     của tính idempotent mà Hoàng đã làm ở phía `Payments`.
  2. `GET /tickets/me` — nhớ chỉ trả vé của **chính** người gọi, và trả **mảng rỗng**, không 404.
  3. `GET /tickets/{id}` — vé của chính người gọi, người khác thì 404 (đừng 403: đừng tiết lộ vé
     có tồn tại).
- **Kiên** — service sinh mã QR duy nhất + ký số. Hai ràng buộc từ phía CSDL: mã **≤ 200 ký tự**
  (§2) và **duy nhất toàn hệ thống** (đã có unique index). Sinh mã rồi mới ghi thì phải xử lý được
  lượt đâm unique.
- **Hiếu** — nối `TripSeatMapService`: hiện trạng thái ghế `"Paid"` chưa có nguồn và comment trong
  file đã ghi sẵn cách nối (thêm một truy vấn `Tickets` theo `(TripId, SeatId, Status = 'Paid')`).
  Hình dạng response **không đổi** — đã chốt trong `api-contract.md`. Lưu ý thêm: nhánh `'Held'`
  trong chỉ mục §3 không sinh ra dòng nào, nên chỉ cần hỏi `'Paid'`.
- **Băng / Hạnh** — `frontend/src/api/ticketApi.ts` đang `USE_MOCK_DATA = true` và ném lỗi ở nhánh
  thật. Bảng **sắp có** (entity + cấu hình đã xong, chờ lệnh sinh migration ở §8) nhưng **endpoint
  chưa** — chờ Hiếu xong rồi mới lật cờ, đừng lật trước.
- **Ai cần thêm cột** — nhắn Dăm, không tự sửa `Migrations/*`, `ModelSnapshot.cs`, `Entities/`.

## 7. Việc cần báo nhóm (không phải việc code)

1. **Tên task ghi hai bảng, thực tế một bảng.** Bảng phân công Sprint 3 ghi *"Migrate bảng Tickets,
   TicketQRCodes"*. Bản dựng trong migration này có **một** bảng `Tickets` với mã QR là cột `Code` (lý
   do ở §2). Nhóm chốt sửa lại tên dòng đó cho khớp, và tài liệu nào nhắc `TicketQRCodes` thì bỏ đi.
2. **A9 cần bổ sung dòng cho `Tickets`.** A9 (nguồn: `quy uoc.docx` mục A9) liệt kê **20 bảng** và
   **có** `Tickets` ở #14 (`BoardingStopId, AlightingStopId`) — nhưng tài liệu trong repo
   (`docs/03-quy-uoc.md`) **không tồn tại**, nên A6/A9 ở đây chỉ còn là trích dẫn trong code và hợp
   đồng. Nhóm nên đưa `docs/03-quy-uoc.md` vào repo; đây là lần thứ ba việc này được nhắc.
3. **A7 "mỗi sprint một migration" đã lệch thực tế từ lâu.** Sprint 2 có 5 migration, Sprint 3 nay có
   4. Thực tế đang là "mỗi task một migration" — nhóm sửa A7 cho khớp, đừng gộp migration.
4. **`Payments` + `Vouchers` nằm chung migration với `Tickets`.** EF gộp toàn bộ thay đổi model còn nợ
   vào một lệnh `migrations add`; không tách được vì `VoucherValidationService` /
   `VoucherRedemptionService` tham chiếu `_db.Vouchers` nên không thể tạm gỡ khỏi model để build.
   Nhánh này trả nốt nợ migration của task *"Migrate bảng Payments, Transactions, PaymentLogs"* và
   `docs/27-huong-dan-migrate-vouchers.md`. **`Transactions` và `PaymentLogs` không được dựng**: cả
   hai đều **không nằm trong A9** và không có entity/cấu hình nào trong repo — muốn có thì phải bổ
   sung A9 trước, rồi báo Dăm.
5. **Xung đột lịch sprint chưa được giải quyết.** Bảng phân công Sprint 3 giao migration vé ở
   **Sprint 3**; `docs/02-sprint-roadmap.md` xếp story 4 ("Vé điện tử") ở **Sprint 4**. Bản này làm
   theo **bảng phân công Sprint 3** — đúng luật "xung đột #7" của `quy uoc.docx` mục J: khi hai tài
   liệu phân công trái nhau thì theo phân công nhận trực tiếp từ Scrum Master. Nhóm vẫn nên chốt lại
   để `docs/02-sprint-roadmap.md` không còn nói ngược.
6. **`docs/api-contract.md` đã cập nhật trạng thái** ở hai mục "Vé điện tử" và "Thanh toán": từ
   *"CHƯA migrate"* (sai — lượt trước viết nhầm thành "ĐÃ migrate" khi migration chưa tồn tại) thành
   🟡 *"cấu hình ĐÃ XONG, MIGRATION CHƯA SINH"* + tên migration sẽ sinh. **Không đụng bảng trường,
   không đổi hình dạng endpoint nào** (⛔ luật 5) — chỉ đổi dòng trạng thái.

## 8. Việc còn lại — đúng MỘT lệnh (Dăm tự chạy)

```bash
cd backend/SmartBus.Api
dotnet ef migrations add Sprint3_Payments_Vouchers_Tickets
```

Máy chưa có `appsettings.Development.json` (file bị .gitignore, repo public) thì cần thêm hai biến
môi trường **giá trị giả** để host design-time dựng được model — `migrations add` không mở kết nối
nào; đừng tạo file `appsettings.Development.json` thật:

```bash
ConnectionStrings__Default="<chuoi-ket-noi-gia — chi can dung dinh dang>" \
Jwt__Key="<chuoi-bat-ky-dai-it-nhat-32-byte>" \
dotnet ef migrations add Sprint3_Payments_Vouchers_Tickets
```

Sau đó soát file migration theo checklist ở `docs/27` (mục "Checklist soát migration") **cộng** các
mục riêng của bảng vé ở §1 (cột), §3–§4 (chỉ mục) và §5 (FK `Payments.TicketId`) của tài liệu này.
`dotnet ef migrations has-pending-model-changes` phải sạch — đó là phép kiểm cho biết migration đã
bắt trọn model.
