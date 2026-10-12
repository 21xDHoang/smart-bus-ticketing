# CSDL huỷ & đổi vé — Sprint 4: migration đã sinh và đã soát

> Viết cho các task đang chờ hai bảng này — **US 5 "Hủy & Đổi vé"**, sheet *Sprint 4*:
> **Hiếu** (API gửi yêu cầu huỷ + kiểm tra điều kiện thời gian, dòng 3) · **Kiên** (API gửi yêu cầu
> đổi chuyến + tính chênh lệch giá, dòng 4; service tính phí huỷ trước 24h/12h/2h, dòng 6) ·
> **Hoàng** (API duyệt / từ chối, dòng 5) · **Hạnh** (màn gửi yêu cầu, dòng 8; màn Admin duyệt,
> dòng 11) · **Thịnh** (component chọn chuyến mới, dòng 9) · **Băng** (màn theo dõi trạng thái,
> dòng 10) · **Giàng A Vàng** (test API + kiểm thử chéo, dòng 12–13).
>
> 🟢 **Trạng thái 12/10/2026:** entity `CancellationRequest` + `TicketChange`, cấu hình
> `AppDbContext.CancellationRequest.cs` / `AppDbContext.TicketChange.cs`, và migration
> `20261012023745_Sprint4_CancellationRequests_TicketChanges` (Vàng Thị Dăm, task Sprint 4 dòng 2)
> **đã xong và đã soát** — xem §8. **Chưa merge, chưa áp lên CSDL chung**: hai bảng chỉ có thật sau
> khi PR được merge rồi chạy `database update`. Trong lúc chờ, test tích hợp vẫn chạy được trên EF
> InMemory — nhưng InMemory **không** cưỡng chế chỉ mục/FK, xem §4.

## 1. Hai bảng này KHÔNG nằm trong A9 — phải báo nhóm

A9 (danh sách **20 bảng đóng băng**, nguồn `quy uoc.docx`) **không có** `CancellationRequests`
lẫn `TicketChanges`. Cả hai vẫn được dựng trong nhánh này, vì có uỷ quyền trực tiếp:

| Nguồn | Nói gì |
|---|---|
| **Bảng phân công Sprint 4, dòng 2** | *"Migrate bảng TicketChanges, CancellationRequests"* — Vàng Thị Dăm, ưu tiên **Cao** |
| **A9** | **Không có** hai bảng này |
| **`docs/02-sprint-roadmap.md`** §2 | Cũng **không** nhắc hai bảng này (và còn xếp US 5 ở Sprint 5 — xem §7.3) |
| Code | Trước nhánh này, repo **không** có entity/cấu hình/endpoint nào nhắc tới hai bảng |

Đây đúng tình huống đã gặp ở Sprint 3 với `Payments`/`Transactions`/`PaymentLogs`
(`docs/28` §7.4): tên task có thật, A9 không có. Cách xử lý giữ nguyên — **dựng theo phân công,
rồi báo nhóm bổ sung A9** (§7.1). Chủ CSDL không tự thêm bảng ngoài A9 mà im lặng.

## 2. Bảng `CancellationRequests` — một yêu cầu HUỶ VÉ

| Cột | Kiểu | Ghi chú |
|---|---|---|
| `Id` | uuid | khoá chính |
| `TicketId` | uuid | FK → `Tickets`, `RESTRICT` — vé khách xin huỷ |
| `UserId` | uuid | FK → `Users`, `RESTRICT` — người gửi yêu cầu |
| `Reason` | text NULL | lý do khách nêu |
| `CancellationFee` | numeric(12,2) | phí huỷ, **chụp lại** lúc gửi; `0` = huỷ miễn phí |
| `DepartureAt` | timestamptz | giờ chạy của chuyến, **chụp lại** lúc gửi |
| `Status` | varchar(20) | `Pending` / `Approved` / `Rejected` |
| `ReviewedByUserId` | uuid NULL | FK → `Users`, `RESTRICT` — quản lý xử lý |
| `ReviewedAt` | timestamptz NULL | mốc quản lý bấm duyệt/từ chối |
| `RejectReason` | text NULL | lý do từ chối |
| `CreatedAt` | timestamptz | thời điểm khách gửi |
| `UpdatedAt` | timestamptz NULL | yêu cầu CÓ bị sửa (lật trạng thái) nên A4 buộc có |

**Vì sao là bảng riêng chứ không phải vài cột trên `Tickets`:** một vé có thể bị xin huỷ **nhiều
lần** theo thời gian (bị từ chối rồi xin lại), mà `Tickets` chỉ giữ được **một** trạng thái. Và câu
hỏi *"vì sao vé này bị huỷ, ai duyệt, phí bao nhiêu, lúc nào"* là câu hỏi **kiểm toán** — ghi đè lên
vé là mất dấu vết. Cùng lối lập luận đã dùng cho `DiscountRequest` (A9 #13).

**Vì sao `CancellationFee` chụp lại:** phí là kết quả của một **quy định sửa được** (trước 24h /
12h / 2h) và của **khoảng cách tới giờ chạy tại đúng lúc khách bấm gửi**. Tính lại lúc duyệt — có
thể vài giờ sau — sẽ ra con số khác con số khách đã nhìn thấy trên màn hình, tức thu tiền sai so với
thứ khách đã đồng ý. Cùng lý do đã khiến `Ticket.Price` chụp lại giá (`docs/28` §1).

**Vì sao `DepartureAt` phải là cột chứ không suy ra từ `Trips`:** giờ chạy **sửa được** — quản lý dời
chuyến bằng `PUT /routes/{routeId}/trips/{id}`. Không chụp lại thì sau một lần dời chuyến không còn
cách nào giải thích vì sao hoá đơn ghi phí 30% trong khi "trước giờ chạy 20 tiếng". NOT NULL vì yêu
cầu huỷ luôn gắn với một chuyến đã có `DepartureTime`.

**Cố ý KHÔNG có `IsDeleted`** (A4).

**Ba giá trị trạng thái, không hơn** — `CancellationRequestStatus`. Cố ý **không** có "đã hoàn tiền"
(tiền chỉ rời hệ thống ở bảng `Refunds`, US 8, có vòng đời riêng kèm retry) và **không** có "khách tự
rút yêu cầu" (chưa task nào của Sprint 4 làm nút đó). Lý do đầy đủ ở doc-comment enum.

## 3. Bảng `TicketChanges` — một yêu cầu ĐỔI CHUYẾN

| Cột | Kiểu | Ghi chú |
|---|---|---|
| `Id` | uuid | khoá chính |
| `TicketId` | uuid | FK → `Tickets`, `RESTRICT` — vé **GỐC** |
| `UserId` | uuid | FK → `Users`, `RESTRICT` — người gửi yêu cầu |
| `NewTicketId` | uuid NULL | FK → `Tickets`, `RESTRICT` — vé **MỚI**, chỉ có sau khi DUYỆT |
| `NewTripId` | uuid | FK → `Trips`, `RESTRICT` — chuyến muốn đổi sang |
| `NewSeatId` | uuid | FK → `Seats`, `RESTRICT` — ghế muốn ngồi ở chuyến mới |
| `NewPrice` | numeric(12,2) | giá vé chuyến MỚI, **chụp lại** lúc gửi |
| `Reason` | text NULL | lý do khách nêu |
| `Status` | varchar(20) | `Pending` / `Approved` / `Rejected` |
| `ReviewedByUserId` | uuid NULL | FK → `Users`, `RESTRICT` — quản lý xử lý |
| `ReviewedAt` | timestamptz NULL | mốc quản lý bấm duyệt/từ chối |
| `RejectReason` | text NULL | lý do từ chối |
| `CreatedAt` | timestamptz | thời điểm khách gửi |
| `UpdatedAt` | timestamptz NULL | yêu cầu CÓ bị sửa nên A4 buộc có |

### 3.1 🔴 Duyệt một yêu cầu đổi = HUỶ vé cũ + phát hành vé MỚI

**Không sửa vé cũ.** Chốt 12/10/2026 (đã hỏi và được xác nhận trước khi code). Duyệt xong thì:

1. vé gốc (`TicketId`) lật sang `TicketStatus.Cancelled` — ghế cũ trả về chợ, chỉ mục duy nhất điều
   kiện của A6 (`WHERE Status IN ('Held', 'Paid')`) không còn thấy nó nên người khác đặt lại được;
2. một vé **MỚI** được phát hành cho chuyến mới, khoá ngoại ghi vào `NewTicketId`.

Vì sao không sửa thẳng `Trips`/`Seats` trên dòng vé cũ — hai hậu quả có thật:

| Sửa vé cũ | Hậu quả |
|---|---|
| Doanh thu | Chuyến CŨ mất dòng đó khỏi báo cáo (US 19) — nhưng tiền của chuyến cũ **đã thực thu**. Báo cáo tháng đã chốt sẽ đổi số khi có người đổi vé. |
| Hoá đơn | `Payments.TicketId` đã trỏ vào vé đó và hoá đơn (US 7) đã xuất theo nó. Vé đổi chuyến nghĩa là hoá đơn cũ không còn khớp thứ nó mô tả. |

Thêm nữa, `Tickets` **không có cột nào trỏ ngược** về lượt đổi đã sinh ra nó — nên nếu không phát
hành vé mới thì màn "theo dõi trạng thái yêu cầu" (Băng, dòng 10) không trả lời được câu *"duyệt
xong thì khách xem vé mới ở đâu"*.

### 3.2 Vì sao `NewTicketId` lệch khuôn tên A2

A2 đặt FK là `<TênEntity>Id` → đáng lẽ `TicketId`. Nhưng bảng này có **hai** khoá ngoại cùng trỏ
`Tickets`: `TicketId` (vé gốc) và `NewTicketId` (vé mới). Buộc phải đặt tên phân biệt — **ngoại lệ có
chủ ý**, cùng tình huống và cùng cách đặt tên với `ReviewedByUserId` của `DiscountRequest`.

### 3.3 Vì sao `NewTripId`/`NewSeatId` bắt buộc, và ghế mới KHÔNG được giữ

Không nullable: một yêu cầu *đổi chuyến* mà không nói đổi sang đâu thì không ai xử lý được, và để
null sẽ đẻ ra nhánh "null nghĩa là giữ nguyên chuyến cũ" — vô nghĩa với một yêu cầu ĐỔI. Ghế cũng
bắt buộc vì **ghế thuộc về XE của chuyến**, nên đổi chuyến là đổi luôn sơ đồ ghế; không có chuyện
"giữ nguyên số ghế cũ" một cách ngầm hiểu.

⚠️ **Ở trạng thái `Pending`, ghế mới CHƯA bị giữ** — không có dòng `SeatHolds` nào cho nó, và vé gốc
vẫn `Paid` (khách còn nguyên chuyến cũ). Hệ quả có thật, **việc của Hoàng (dòng 5)**: giữa lúc khách
gửi và lúc quản lý duyệt, người khác hoàn toàn có thể mua mất ghế đó. `PATCH .../{id}` vì vậy phải
**kiểm tra lại ghế còn trống ngay lúc duyệt** rồi mới lật trạng thái, chứ không tin dữ liệu đã kiểm
lúc gửi.

### 3.4 Hai bảng cố ý dùng CHUNG tên cột cho phần chung

`TicketId`, `UserId`, `Reason`, `Status`, `ReviewedByUserId`, `ReviewedAt`, `RejectReason`,
`CreatedAt`, `UpdatedAt` — giống nhau ở cả hai bảng, và `Status` dùng **trùng tên từng giá trị**
(`Pending`/`Approved`/`Rejected`). Lý do: màn duyệt của Admin là **một** màn cho cả hai loại yêu cầu
(*"Màn hình Admin duyệt yêu cầu huỷ - đổi vé"* — Hạnh, dòng 11), nên FE phải vẽ được hai hàng đợi
bằng cùng một tập nhãn. Vẫn là **hai enum riêng** (đúng lối "một enum một entity" — `TicketStatus`,
`PaymentStatus`, `DiscountRequestStatus`…) để hai nghiệp vụ còn chỗ rẽ nhánh về sau.

## 4. Chống trùng & nhánh đua — ở tầng CSDL

### 4.1 Một vé chỉ được có MỘT yêu cầu ĐANG CHỜ

```sql
CREATE UNIQUE INDEX "IX_CancellationRequests_TicketId_Pending"
    ON "CancellationRequests" ("TicketId") WHERE "Status" = 'Pending';

CREATE UNIQUE INDEX "IX_TicketChanges_TicketId_Pending"
    ON "TicketChanges" ("TicketId") WHERE "Status" = 'Pending';
```

**Vì sao phải chặn ở CSDL chứ không giao hết cho service:** hai tab cùng bấm "Gửi yêu cầu" là chuyện
xảy ra được — cả hai đọc "vé chưa có yêu cầu nào", cả hai ghi. Hai dòng `Pending` cho một vé nghĩa
là **hai lượt duyệt, tức hai khoản hoàn tiền cho một chiếc vé**. Đây là lỗi tiền, đúng loại mà A5/A6
sinh ra để chặn ở tầng CSDL. Ở `TicketChanges` hậu quả còn nặng hơn một bậc: hai yêu cầu cùng được
duyệt nghĩa là **hai vé mới phát hành** và chuyến cũ bị huỷ hai lần.

**Vì sao là chỉ mục ĐIỀU KIỆN chứ không phải unique thường trên `TicketId`:** một vé hợp lệ khi có
nhiều yêu cầu **theo thời gian** — bị từ chối rồi xin lại. Unique thường sẽ chặn vĩnh viễn lần xin
thứ hai; điều kiện `Status` chỉ chặn khi yêu cầu **đang chờ**.

### 4.2 Chỉ mục thường trên `TicketId` KHÔNG thừa

Cả hai bảng đều có thêm một chỉ mục **không điều kiện** trên cùng cột `TicketId`
(`IX_CancellationRequests_TicketId`, `IX_TicketChanges_TicketId`). Nghe như trùng, nhưng đúng bài học
đã ghi ở `docs/28` §4: PostgreSQL chỉ dùng được một partial index khi truy vấn **chứng minh được**
điều kiện của nó. Câu hỏi *"vé này từng bị xin huỷ/đổi chưa"* không nói gì về `Status` nên không
thoả, và bộ lập kế hoạch sẽ bỏ qua chỉ mục điều kiện kia.

⚠️ **Cách viết quan trọng — phải gọi `HasIndex` kèm TÊN.** EF nhận diện chỉ mục theo **danh sách
cột**, nên lần gọi thứ hai cho cùng một cột sẽ trả về đúng cái builder của lần gọi đầu (đã unique +
có điều kiện) chứ **không** tạo chỉ mục thứ hai — im lặng, không báo lỗi. Có tên riêng thì EF coi là
hai chỉ mục khác nhau và sinh đủ cả hai. Ai đọc lại `AppDbContext.CancellationRequest.cs` mà định
"dọn" lần gọi thứ hai cho gọn thì sẽ mất chỉ mục này mà build vẫn xanh.

### 4.3 `xmin` — nhánh đua "hai quản lý cùng duyệt một yêu cầu"

Cả hai bảng có `e.Property<uint>("xmin").HasColumnName("xmin").IsRowVersion()`.

**Vì sao cần dù đã có chỉ mục `Pending`:** việc **duyệt** chính là nhánh đua. Hai quản lý cùng bấm
Duyệt trên một yêu cầu `Pending`, cả hai đều đọc thấy `Pending` rồi cùng ghi `Approved` — chỉ mục
`Pending` không cứu được vì **lúc ghi cả hai đều đã rời khỏi tập `Pending`**. Với `xmin`, EF thêm
`WHERE xmin = ...` vào câu UPDATE, lượt thua nhận `DbUpdateConcurrencyException` và service coi là
"đã có người xử lý" — chặn đúng khoản hoàn tiền thứ hai (và ở `TicketChanges`, chặn vé mới thứ hai).

**Vì sao `Tickets` cố ý không có mà hai bảng này có:** `docs/28` §1 đã ghi — thêm sau **không tốn
migration cột**, vì `xmin` là **cột hệ thống của PostgreSQL**. Đã kiểm: `grep -c xmin` trên SQL sinh
ra = **0** (§8).

### 4.4 ⚠️ Hai ràng buộc KHÔNG diễn đạt được bằng chỉ mục — việc của service

1. **Một vé không thể vừa có yêu cầu huỷ vừa có yêu cầu đổi đang chờ.** Hai bảng khác nhau, nên hai
   chỉ mục ở §4.1 không nói gì về nhau. Đây là **việc của service** (Hiếu dòng 3 và Kiên dòng 4 phải
   thống nhất một chỗ kiểm chung, đừng mỗi endpoint tự kiểm một nửa).
2. **Kiểm tra điều kiện thời gian** (*"API gửi yêu cầu huỷ vé + kiểm tra điều kiện thời gian"* — Hiếu,
   dòng 3): cột `DepartureAt` đã có sẵn để so, nhưng luật "còn đủ X giờ mới được huỷ" là luật nghiệp
   vụ sửa được, không phải ràng buộc CSDL.

⚠️ **Bộ test chạy trên provider InMemory, mà InMemory KHÔNG cưỡng chế unique index lẫn FK.** Nên bộ
test xanh **không** nói gì về việc các chỉ mục ở §4.1 có tồn tại hay không. Chốt chống trùng chỉ tồn
tại thật ở PostgreSQL — đây là lý do §8 đáng đọc.

## 5. Chỉ mục đã đánh — dùng đúng cái có sẵn

| Truy vấn | Chỉ mục |
|---|---|
| Chặn trùng yêu cầu đang chờ | `IX_CancellationRequests_TicketId_Pending` · `IX_TicketChanges_TicketId_Pending` (unique, **có điều kiện**) |
| "Vé này từng bị xin huỷ/đổi chưa" | `IX_CancellationRequests_TicketId` · `IX_TicketChanges_TicketId` |
| "Yêu cầu huỷ - đổi của tôi" | `IX_CancellationRequests_UserId` · `IX_TicketChanges_UserId` |
| Hàng đợi duyệt của Admin (cũ nhất trước) | `IX_CancellationRequests_Status_CreatedAt` · `IX_TicketChanges_Status_CreatedAt` |
| Người duyệt là ai (khoá ngoại `ReviewedByUserId`) | EF tự sinh |
| Chuyến/ghế muốn đổi sang, vé mới (khoá ngoại) | EF tự sinh: `IX_TicketChanges_NewTripId`, `IX_TicketChanges_NewSeatId`, `IX_TicketChanges_NewTicketId` |

`Status` đứng đầu chỉ mục hàng đợi vì `Pending` là **số ít** so với `Approved`/`Rejected`;
`CreatedAt` đứng sau để sắp được luôn. Cùng khuôn đã dùng ở `AppDbContext.DiscountRequest.cs` nên màn
duyệt gộp đọc được cả ba bảng bằng một câu truy vấn.

## 6. Việc còn lại của người khác

- **Hiếu** (dòng 3) — `POST /cancellation-requests`: kiểm điều kiện thời gian **trước**, rồi mới tính
  `CancellationFee` và **chụp `DepartureAt`**. Bọc lỗi unique của §4.1 để lượt gọi lại trả về yêu cầu
  cũ thay vì ném 500. Yêu cầu của **chính** người gọi; người khác thì 404 (đừng 403 — đừng tiết lộ vé
  có tồn tại, cùng lối `docs/28` §6).
- **Kiên** (dòng 4 + dòng 6) — service tính phí huỷ theo quy định 24h/12h/2h: ghi thẳng con số vào
  `CancellationFee` (không để null, `0` = miễn phí). API đổi chuyến: `NewPrice` chụp lại từ `Fares`.
  **Chênh lệch KHÔNG ghi vào CSDL** — nó là `NewPrice - Ticket.Price`, xem §3. Quy ước **dấu**:
  **dương = khách trả thêm, âm = nhà xe hoàn lại**, `0` = đổi ngang giá.
- **Hoàng** (dòng 5) — `PATCH /cancellation-requests/{id}` và `PATCH /ticket-changes/{id}`: **kiểm lại
  ghế còn trống ngay lúc duyệt** (§3.3), lật trạng thái, và ở `TicketChanges` thì huỷ vé cũ + phát
  hành vé mới + ghi `NewTicketId` (§3.1). Bắt `DbUpdateConcurrencyException` của §4.3 và trả về "yêu
  cầu đã được người khác xử lý" — **đừng** để nó thành 500.
- **Hạnh** (dòng 8, 11) — màn gửi yêu cầu huỷ/đổi và màn Admin duyệt. Bảng trường của hợp đồng API
  chưa có (§7.4) — chờ chốt với Hiếu/Kiên/Hoàng trước khi cứng hoá cột.
- **Thịnh** (dòng 9) — component chọn chuyến mới + so sánh giờ và giá. Con số chênh lệch để hiển thị
  chỉ cần `NewPrice - Ticket.Price`, không cần trường mới từ API.
- **Băng** (dòng 10) — màn theo dõi trạng thái. Tập giá trị để vẽ là **đúng ba** ở mỗi enum
  (`Pending`/`Approved`/`Rejected`, §2 và §3.4); thêm giá trị là đổi hợp đồng với frontend.
- **Giàng A Vàng** (dòng 12–13) — test API huỷ/đổi + tính phí. ⚠️ InMemory **không** cưỡng chế unique
  index/FK (§4.4), nên **đừng** viết test "gửi hai yêu cầu cho một vé thì cái thứ hai phải lỗi" rồi
  tin nó kiểm được §4.1 — trên InMemory nó sẽ **xanh dù ràng buộc không tồn tại**. Muốn kiểm thật thì
  phải chạy trên PostgreSQL.
- **Ai cần thêm cột** — nhắn Dăm, không tự sửa `Migrations/*`, `ModelSnapshot.cs`, `Entities/`.

## 7. Việc cần báo nhóm (không phải việc code)

1. **A9 cần bổ sung 2 dòng: `CancellationRequests`, `TicketChanges`.** A9 đang là 20 bảng đóng băng và
   không có bảng nào trong hai bảng này (§1). Bảng phân công Sprint 4 (dòng 2) đã giao migrate, nên
   **nhóm chốt bổ sung A9** cho khớp — nếu không thì mọi tài liệu còn nói "20 bảng" trong khi CSDL có
   22. Đây là lần thứ hai việc "A9 lệch thực tế" được nhắc (lần đầu: `docs/28` §7.4).
2. **`docs/03-quy-uoc.md` vẫn không tồn tại trong repo.** Mọi tham chiếu A1–A9 ở đây chỉ còn là trích
   dẫn trong code và tài liệu. Đây là lần thứ **năm** việc này được nhắc — nhóm nên đưa file vào repo.
3. **Xung đột lịch sprint: US 5 nằm ở hai sprint khác nhau.** `docs/02-sprint-roadmap.md` §2 xếp US 5
   (*Hủy/Đổi vé*) ở **Sprint 5**, còn bảng phân công Sprint 4 giao **toàn bộ** story này — kể cả
   migration — ở **Sprint 4** (dòng 2–13). Bản này làm theo **bảng phân công Sprint 4**, đúng luật
   "xung đột #7" của `quy uoc.docx` mục J: khi hai tài liệu phân công trái nhau thì theo phân công
   nhận trực tiếp từ Scrum Master. Thêm một điểm nữa nghiêng về bảng phân công: chính
   `docs/02-sprint-roadmap.md` ghi trạng thái **"đề xuất, chờ nhóm chốt"** ở đầu file. Nhóm vẫn nên
   chốt lại để `docs/02-sprint-roadmap.md` không còn nói ngược.
4. **`docs/api-contract.md` chưa có mục cho US 5.** Hợp đồng đã có sẵn chỗ cho mọi bảng khác mà
   migration này chạm, riêng luồng huỷ/đổi thì chưa — nên ba người viết API (Hiếu · Kiên · Hoàng) và
   ba người viết màn (Hạnh · Thịnh · Băng) đang không có hình dạng nào để bám. Hình dạng endpoint là
   **việc của chủ API**, nên tài liệu này **không tự chốt**; đề nghị Hiếu chủ trì một mục "Huỷ & Đổi
   vé" trong `docs/api-contract.md`, chốt trước rồi mới code (⛔ luật 5).
5. **A7 "mỗi sprint một migration" đã lệch thực tế từ lâu** (đã nhắc ở `docs/28` §7.3). Sprint 4 này
   sẽ có ít nhất 5 migration, mỗi task migrate một cái. Nhóm sửa A7 cho khớp, **đừng gộp migration** —
   gộp 5 bảng của 5 story khác nhau vào một file là chặn nhau giữa các nhánh.
6. **Bảng `Notifications` được kéo trước từ Sprint 5 sang Sprint 4.** Task dòng 7 Sprint 4 (*"Ghi log
   và thông báo kết quả xử lý cho hành khách"*) không chạy được nếu thiếu bảng đó — A8.5 chốt thông
   báo phải **lưu vào bảng** để khách đọc lại khi offline, không chỉ một sự kiện SignalR. Khác hai
   bảng của tài liệu này, `Notifications` **có** trong A9 (mục #15) nên không cần bổ sung A9. Hệ quả
   cho nhóm: bảng phân công **Sprint 5 dòng 14** (*"Migrate bảng Notifications, NotificationTemplates,
   Subscriptions"* — cùng người làm) coi như **bớt một bảng**, còn lại `NotificationTemplates` +
   `Subscriptions`. Nhóm chốt lại để dòng 14 không làm trùng. Chi tiết đầy đủ:
   **`docs/33-thong-bao-ket-qua-huy-doi-ve.md`** — tài liệu anh em của tài liệu này, cùng US 5.

## 8. Migration đã sinh và đã soát — 12/10/2026

File `Migrations/20261012023745_Sprint4_CancellationRequests_TicketChanges.cs` (kèm `.Designer.cs`, và
`AppDbContextModelSnapshot.cs` bị sửa — **thuần thêm 217 dòng, 0 dòng xoá**, nên không bảng nào khác
bị đụng). Tên theo A7 (`<Sprint><Nội dung>`).

Máy chưa có `appsettings.Development.json` (file bị .gitignore, repo public) thì mọi lệnh `dotnet ef`
cần các biến môi trường **giá trị giả** để host design-time dựng được model. `migrations add`,
`has-pending-model-changes` và `migrations script` đều **không mở kết nối** nào — **đừng tạo file
`appsettings.Development.json` thật**, `scripts/setup-csdl.sh` bỏ qua file đã tồn tại nên nó sẽ chặn
luôn phần cài đặt thật về sau:

```bash
ConnectionStrings__Default="<chuoi-ket-noi-gia — chi can dung dinh dang>" \
Jwt__Key="<chuoi-bat-ky-dai-it-nhat-32-byte>" \
Jwt__Issuer="SmartBus" \
Jwt__Audience="SmartBusClient" \
dotnet ef migrations add Sprint4_CancellationRequests_TicketChanges --project backend/SmartBus.Api
```

**Hai phép kiểm, không cần CSDL thật** — chạy trên đúng file migration vừa sinh:

| Phép kiểm | Kết quả |
|---|---|
| `dotnet ef migrations has-pending-model-changes --no-build` | ✅ "No changes have been made to the model since the last migration" |
| `dotnet ef migrations script <Sprint3> <Sprint4> --no-build` rồi `grep -ci xmin` | ✅ `0` — không cột `xmin` nào lọt vào SQL |
| SQL đó có `ALTER TABLE` / `DROP` / `UPDATE` nào không | ✅ không — **chỉ** `CREATE TABLE` ×2 + `CREATE INDEX` ×13 + 1 dòng `INSERT` vào `__EFMigrationsHistory` |

SQL sinh ra khớp §2, §3, §4 và §5:

| Hạng mục | Kết quả |
|---|---|
| Hai `CREATE TABLE` | ✅ có, cột đúng bảng ở §2 (12 cột) và §3 (14 cột) |
| Tiền | ✅ `"CancellationFee" numeric(12,2)`, `"NewPrice" numeric(12,2)` |
| Thời gian | ✅ mọi mốc `timestamp with time zone` |
| Trạng thái | ✅ `character varying(20)` |
| Lý do | ✅ `text` (không phải `varchar(n)`) |
| Khoá ngoại | ✅ **9 FK** — `CancellationRequests` 3, `TicketChanges` 6 — **tất cả `ON DELETE RESTRICT`** |
| Chỉ mục điều kiện | ✅ cả hai, đúng câu ở §4.1 |
| Chỉ mục thường cùng cột | ✅ cả hai `IX_..._TicketId` **vẫn còn** — tức cách gọi kèm tên ở §4.2 đã đúng |
| Build | ✅ `dotnet build backend/SmartBus.Api/SmartBus.Api.csproj` — 0 warning, 0 error |

⚠️ **Đừng "dọn" dòng `xmin` trong file migration.** Trong `Up()` có
`xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)` — trông như đang tạo một
cột, nhưng **Npgsql lọc cột hệ thống `xid` ra khỏi DDL**, nên SQL thật không hề có cột đó (bảng trên,
`grep -ci xmin` = 0). Đây là **đúng lối đã có ở migration Sprint 3 đã merge**
(`Sprint3_Payments_Vouchers_Tickets` khai y hệt cho `Payments` và `Vouchers`), không phải sai sót của
file này. Xoá dòng đó đi thì mất concurrency token của §4.3.

**Việc còn lại, theo đúng thứ tự `docs/25-huong-dan-csdl-chung.md` §4:**

1. PR nhánh này → có người khác approve → **Squash and merge** vào `main`.
2. **Sau khi merge** mới áp lên CSDL chung: `dotnet ef database update --project backend/SmartBus.Api`.
   Trước bước đó, CSDL chung **chưa có** hai bảng — endpoint nào chạm chúng sẽ lỗi
   `relation "…" does not exist`.
