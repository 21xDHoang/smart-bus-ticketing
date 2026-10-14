# CSDL soát vé QR — Sprint 4: bảng `TicketValidations`

> Viết cho các task đang chờ bảng này — **US 15** *"Là phụ xe/tài xế, tôi muốn quét mã QR trên vé của
> khách bằng ứng dụng mobile để xác thực vé hợp lệ"*, sheet *Sprint 4*:
> **Hiếu** (API soát vé: nhận mã QR, xác thực chữ ký, trả kết quả hợp lệ/không, dòng 15; phân quyền
> chỉ tài xế/phụ xe được soát, dòng 19) · **Kiên** (API chống soát trùng, dòng 16; API ghi nhận lượt
> lên xe theo trạm, dòng 17) · **Hoàng** (xử lý ngoại tuyến: lưu tạm lượt soát khi mất mạng, đồng bộ
> lại sau, dòng 18) · **Băng** (màn quét QR bằng camera, dòng 20) · **Thịnh** (hiển thị kết quả
> xanh/đỏ + âm thanh, dòng 21; nhập mã thủ công, dòng 23) · **Hạnh** (danh sách hành khách đã lên xe,
> dòng 22; thống kê lượt soát theo chuyến, dòng 24) · **Giàng A Vàng** (test API + kiểm thử chéo,
> dòng 25–26).
>
> 🟢 **Trạng thái:** entity `TicketValidation` + `TicketValidationResult`, cấu hình
> `AppDbContext.TicketValidation.cs`, và migration `Sprint4_TicketValidations` (Vàng Thị Dăm, task
> Sprint 4 dòng 14) — **đã sinh và đã soát, xem §8**. **Chưa merge, chưa áp lên CSDL chung**: bảng chỉ
> có thật sau khi PR được merge rồi chạy `database update` (thứ tự ở
> `docs/25-huong-dan-csdl-chung.md` §4). Trước bước đó, endpoint nào chạm bảng này sẽ lỗi
> `relation "TicketValidations" does not exist`.

## 1. Bảng này KHÔNG nằm trong A9 — phải báo nhóm

A9 (danh sách **20 bảng đóng băng**, nguồn `quy uoc.docx`) **không có** `TicketValidations`. Bảng vẫn
được dựng trong nhánh này, vì có uỷ quyền trực tiếp:

| Nguồn | Nói gì |
|---|---|
| **Bảng phân công Sprint 4, dòng 14** | *"Migrate bảng TicketValidations"* — Vàng Thị Dăm, ưu tiên **Cao**, 3 điểm |
| **A9** | **Không có** bảng này |
| **`docs/02-sprint-roadmap.md`** §2 | Cũng **không** nhắc bảng này (và còn xếp US 15 ở Sprint 5 — xem §7.3) |
| Code | Trước nhánh này, repo **không** có entity/cấu hình/endpoint nào nhắc tới bảng này |

Đây là bảng thứ **BA** của dự án nằm ngoài A9 — sau `CancellationRequests` và `TicketChanges`
(`docs/32-csdl-huy-doi-ve.md` §1) — và là lần thứ **BA** việc *"A9 lệch thực tế"* được nhắc (hai lần
trước: `docs/28` §7.4 và `docs/32` §1). Cách xử lý giữ nguyên: **dựng theo phân công, rồi báo nhóm
bổ sung A9** (§7.1). Chủ CSDL không tự thêm bảng ngoài A9 mà im lặng.

## 2. Bảng `TicketValidations` — NHẬT KÝ SOÁT VÉ

Một dòng cho **mỗi lượt quét mã QR**, kể cả lượt bị từ chối. Đây là bảng **chỉ ghi thêm**.

| Cột | Kiểu | Ghi chú |
|---|---|---|
| `Id` | uuid | khoá chính |
| `TicketId` | uuid **NULL** | FK → `Tickets`, `RESTRICT` — vé bị soát. NULL ⟺ `Result = 'NotFound'` |
| `TripId` | uuid NULL | FK → `Trips`, `RESTRICT` — chuyến phụ xe **đang chạy** lúc quét |
| `ValidatedByUserId` | uuid | FK → `Users`, `RESTRICT` — tài xế/phụ xe đã quét |
| `BoardingStopId` | uuid NULL | FK → `Stops`, `RESTRICT` — trạm khách **thực sự** lên xe |
| `ScannedCode` | varchar(200) | **nguyên văn** chuỗi mã QR máy quét đọc được |
| `Result` | varchar(20) | `NotFound` / `Cancelled` / `AlreadyUsed` / `WrongTrip` / `Expired` / `Valid` |
| `ScannedAt` | timestamptz | giờ **máy quét** ghi nhận lượt soát |
| `ClientScanId` | uuid NULL | **UNIQUE** — khoá chống trùng khi hàng đợi ngoại tuyến đồng bộ lại |
| `CreatedAt` | timestamptz | giờ dòng nhật ký tới được **server** (A4) |

**Cố ý KHÔNG có `UpdatedAt`** — A4 xếp bảng chỉ-ghi-thêm vào nhóm `AuditLogs` / `Payments` /
`Notifications`, và gọi tên đúng những bảng đó. Một lượt soát đã xảy ra thì không có gì để sửa; sửa
được nghĩa là nhật ký kiểm toán vô giá trị.

**Cố ý KHÔNG có `IsDeleted`** (A4).

### 2.1 Vì sao là bảng riêng, không phải vài cột trên `Tickets`

`Tickets` đã có `Status` + `UsedAt`, và hai cột đó **đủ** để trả lời *"vé này đã được soát chưa"*.
Nhưng bốn task của US 15 hỏi những câu mà hai cột đó **không** trả lời được:

| Task | Câu hỏi | Vì sao `Tickets` không trả lời được |
|---|---|---|
| Dòng 16 — chống soát trùng | *Ai đã soát trùng, lúc nào, trên chuyến nào?* | Lượt soát trùng bị **từ chối**, nên nó không được ghi vào `Tickets` — vé vẫn `Used` với `UsedAt` của lượt **đầu tiên**. Bằng chứng của lượt thứ hai chỉ có ở đây |
| Dòng 17 — lượt lên xe theo trạm | *Khách lên ở trạm nào?* | `Tickets.BoardingStopId` là trạm khách **chọn lúc mua** (A8.1: *"là thông tin, không phải khoá nghiệp vụ"*), không phải trạm phụ xe ghi nhận lúc khách bước lên |
| Dòng 18 — ngoại tuyến | *Lượt soát này quét lúc nào, đồng bộ lúc nào?* | `Tickets` chỉ giữ **một** `UsedAt` cho **một** lượt; cả hàng đợi ngoại tuyến đổ về cùng lúc thì không có chỗ ghi giờ quét thật của từng lượt |
| Dòng 24 — thống kê theo chuyến | *Chuyến này có bao nhiêu lượt soát?* | Đây là phép **đếm nhiều dòng**. Một vé chỉ có một dòng, và lượt bị từ chối không có dòng nào |

Thêm nữa, đây là dữ liệu **kiểm toán**: ghi đè lên `Tickets` là mất dấu vết. Cùng lối lập luận đã
dùng cho `CancellationRequests` (`docs/32` §2) và `Notification`.

### 2.2 Vì sao GHI CẢ lượt bị từ chối (và vì sao `TicketId` nullable)

Đây là điểm khác biệt lớn nhất so với mọi bảng còn lại của dự án: `TicketId` **nullable**, vì lượt
`NotFound` **không có vé nào để trỏ vào**.

Ghi cả lượt bị từ chối là chủ ý, không phải tiện tay:

1. **Dòng 16 chỉ có nghĩa nếu lượt soát trùng được ghi lại.** *"Chống soát trùng"* mà không lưu lượt
   trùng thì hàng `AlreadyUsed` không bao giờ tồn tại, và không ai chứng minh được một lượt soát
   trùng đã bị chặn.
2. **docs/30 §3.2 đã chốt:** *"log nguyên văn mã để điều tra sau"* — câu đó chỉ thực hiện được nếu
   lượt có mã sai chữ ký được ghi xuống.
3. **Dòng 24 là phép đếm *lượt soát*, không phải đếm vé.** Bỏ lượt bị từ chối thì con số trên màn
   thống kê không còn là số lượt phụ xe đã quét.

Bắt `TicketId` NOT NULL sẽ buộc service phải **bỏ luôn dòng nhật ký** cho lượt không tra ra vé — tức
là bỏ đúng loại lượt mà nhật ký sinh ra để ghi lại.

⚠️ **Bất biến kèm theo, phải đọc trước khi viết truy vấn:** `TicketId IS NULL` ⟺
`Result = 'NotFound'`, và chiều ngược lại. Đây là bất biến ở **tầng nghiệp vụ**, không diễn đạt được
bằng CHECK constraint vì `Result` lưu dạng chuỗi (§4.3).

### 2.3 Vì sao `ScannedCode` là cột riêng chứ không chỉ trỏ `TicketId`

Ba lý do, và không lý do nào là "cho tiện":

1. Lượt `NotFound` không có vé để trỏ vào — không có cột này thì lượt đó không còn dấu vết nào ngoài
   một chữ `NotFound`.
2. docs/30 §2 chốt mã QR là `SBT1:{ticketId}.{blob}` với **nonce ngẫu nhiên**, nên hai lượt quét
   cùng một vé mang **hai chuỗi khác nhau**. Chuỗi của kẻ làm giả chỉ nằm ở đây.
3. docs/30 §3.2 chốt *"log nguyên văn mã để điều tra sau"*.

⚠️ **Không dùng cột này để TRA VÉ.** Đường tra là `Tickets.Code` (có chỉ mục UNIQUE `IX_Tickets_Code`,
A6) **sau khi** chữ ký đã được xác thực (`ITicketQrService.TryVerify` — docs/30 §3.2). Cột này là bản
ghi, không phải khoá tra cứu.

### 2.4 Vì sao `ValidatedByUserId` lệch khuôn tên A2

A2 đặt khoá ngoại là `<TênEntity>Id` → đáng lẽ `UserId`. Hai lý do để lệch:

1. Trên bảng `Tickets` — bảng mà lượt soát này trỏ tới — `UserId` là **hành khách chủ vé**; ở đây lại
   là **người soát**. Hai vai trò ngược nhau mang cùng một tên cột là đúng loại nhầm lẫn mà
   doc-comment của `Notification.UserId` đã cảnh báo, và ở đây hai bảng còn nằm sát nhau trong cùng
   một tính năng nên hậu quả là thật.
2. **Ngoại lệ có chủ ý, đã có tiền lệ:** `DiscountRequest.ReviewedByUserId` và
   `TicketChange.ReviewedByUserId` cũng đặt tên theo **vai trò** khi cột không còn là "chủ sở hữu" của
   bản ghi.

### 2.5 Vì sao `TripId` phải là cột riêng

`TripId` ở đây là **chuyến phụ xe đang chạy lúc quét**, KHÔNG phải `Ticket.TripId` (chuyến ghi trên
vé). Hai giá trị khác nhau chính là nội dung của `WrongTrip`.

🔴 Suy `TripId` từ vé thì lượt soát **nhầm chuyến** luôn tự thấy mình đúng, và lý do `WrongTrip`
không bao giờ xuất hiện — tức là mất luôn một trong năm kết quả từ chối mà hợp đồng đã chốt.

Nullable vì hợp đồng endpoint để `tripId` tuỳ chọn (docs/29 §3: *"Chuyến đang chạy của phụ xe"*), và
vì lượt `NotFound` không có chuyến nào để đối chiếu. **Không có giá trị này thì service không kết
luận được `WrongTrip`.**

## 3. Sáu giá trị `Result` — ánh xạ thẳng sang hợp đồng

Enum `TicketValidationResult` có **đúng sáu** giá trị: `Valid` cộng năm lý do từ chối đã chốt ở
docs/29 §3 và đã được frontend viết sẵn ở `frontend/src/api/ticketValidationApi.ts` (nhánh
`feature/15-quet-qr-camera`, Băng, dòng 20). **Thêm một giá trị là đổi hợp đồng với màn quét QR.**

Ánh xạ sang `POST /tickets/validate` là **1–1**:

| `Result` trong CSDL | Response |
|---|---|
| `Valid` | `{ "valid": true, "reason": null }` |
| `NotFound` · `Cancelled` · `AlreadyUsed` · `WrongTrip` · `Expired` | `{ "valid": false, "reason": "<tên giá trị>" }` |

**Vì sao GỘP "hợp lệ" và "lý do từ chối" vào MỘT cột** thay vì hai cột `IsValid` + `Reason`: hai cột
cho phép sinh ra dòng **tự mâu thuẫn** (`IsValid = true` đi kèm `Reason = 'Cancelled'`), và không có
ràng buộc CSDL nào chặn được tổ hợp đó. Một cột thì tổ hợp đó **không tồn tại**.

⚠️ **`Valid` cố ý KHÔNG phải giá trị 0.** `default(TicketValidationResult)` là `NotFound`. Một lượt
ghi quên gán `Result` vì vậy hiện ra như một lượt **bị từ chối** — ồn ào, và lộ ra ngay khi đối chiếu
thống kê theo chuyến (dòng 24). Nếu `Valid` đứng đầu thì cùng lỗi đó lại thành một lượt **hợp lệ**:
âm thầm, và làm sai đúng con số mà màn thống kê sinh ra để đếm. Đây là bảng nhật ký kiểm toán — chọn
kiểu hỏng ồn ào thay vì kiểu hỏng im lặng.

⚠️ **`NotFound` bao gồm CẢ "mã rác" lẫn "mã sai chữ ký".** docs/30 §3.2 chốt hai ca đó trả về **cùng
một** câu trả lời: người soát chỉ cần biết cho lên xe hay không. Phân biệt "khách dán nhầm chuỗi" với
"kẻ làm vé giả" là việc **điều tra sau**, dựa vào `ScannedCode` đã lưu nguyên văn. Đừng thêm giá trị
`Forged`.

## 4. Chống trùng & chỉ mục — ở tầng CSDL

### 4.1 `ClientScanId` — chống trùng khi đồng bộ ngoại tuyến (dòng 18)

```sql
CREATE UNIQUE INDEX "IX_TicketValidations_ClientScanId"
    ON "TicketValidations" ("ClientScanId");
```

**Vì sao cần:** hàng đợi ngoại tuyến gửi lại là chuyện **bình thường** — mất mạng giữa chừng, app bị
kill, người dùng bấm "đồng bộ" hai lần. Không có khoá chống trùng thì:

| Hậu quả | Ai chịu |
|---|---|
| Mỗi lượt gửi lại đẻ thêm một dòng nhật ký → màn thống kê (dòng 24) **đếm khống** | Hạnh |
| Một lượt soát gửi lại **trông như** một lượt soát trùng → tự sinh bằng chứng gian lận cho một phụ xe vô can | Cả nhóm |

**Vì sao UNIQUE nhưng NULLABLE:** lượt soát **online** không cần khoá này — máy có mạng gửi thẳng một
lần. Bắt mọi client sinh mã là bắt cả những client không hề ngoại tuyến làm thêm một việc vô ích.
PostgreSQL cho phép **nhiều NULL** trên chỉ mục UNIQUE, nên các lượt online không chặn nhau.

⚠️ Đây là **UNIQUE thường**, KHÔNG phải partial index — partial index ở đây không chặn được gì thêm
mà lại bỏ sót đúng ca cần chặn.

### 4.2 `ScannedAt` khác `CreatedAt` — và cảnh báo cho dòng 18

| Cột | Nghĩa |
|---|---|
| `ScannedAt` | giờ **máy quét** ghi nhận lượt soát |
| `CreatedAt` | giờ dòng nhật ký **tới được server** (A4) |

🔴 Máy mất mạng thì hàng đợi có thể đồng bộ lại **sau hàng giờ**, thậm chí hôm sau. Lấy giờ đồng bộ
làm giờ soát sẽ **dồn cả một buổi chạy của phụ xe vào một mốc duy nhất**, và màn thống kê theo chuyến
(dòng 24) đọc ra một biểu đồ sai hẳn. Giờ nghiệp vụ là giờ **quét**, nên phải là cột riêng.

⚠️ **`ScannedAt` do THIẾT BỊ khai, không phải server** — máy phụ xe sai giờ thì cột này sai. Khuyến
nghị cho Hoàng (dòng 18): service nên kiểm `ScannedAt` nằm trong khoảng hợp lý (không ở tương lai,
không quá xa `CreatedAt`) trước khi nhận, **chứ đừng** so sánh `ScannedAt` giữa các thiết bị khác
nhau.

### 4.3 ⚠️ Ba ràng buộc KHÔNG diễn đạt được bằng chỉ mục — việc của service

1. **`TicketId IS NULL` ⟺ `Result = 'NotFound'`** (§2.2). `Result` lưu dạng **chuỗi** (A3) nên không
   viết được CHECK constraint so sánh nó với hằng số enum một cách bền vững — đổi tên giá trị trong
   enum là CHECK constraint ở CSDL lặng lẽ sai. Đây là bất biến của service.
2. **Một vé không được có hai lượt soát `Valid`.** Chỉ mục UNIQUE **không** chặn được: hai lượt soát
   song song cùng đọc thấy vé `Paid`, cả hai cùng ghi. Việc chặn nằm ở **dòng 16** (Kiên), trên
   `Tickets.Status`: lượt thắng lật `Paid → Used`, lượt thua phải đọc lại và ghi `AlreadyUsed` —
   **không** phải ghi `Valid`. Xem §6.
3. **Chỉ tài xế/phụ xe được soát** (dòng 19) là luật **phân quyền**, không phải ràng buộc CSDL.

⚠️ **Bộ test chạy trên provider InMemory, mà InMemory KHÔNG cưỡng chế unique index lẫn FK.** Nên bộ
test xanh **không** nói gì về việc chỉ mục §4.1 có tồn tại hay không. Chốt chống trùng chỉ tồn tại
thật ở PostgreSQL — đây là lý do §8 đáng đọc.

## 5. Chỉ mục đã đánh — dùng đúng cái có sẵn

| Truy vấn | Chỉ mục |
|---|---|
| *"Vé này đã bị soát những lần nào"* (đối chiếu khi khách khiếu nại; bước kiểm `AlreadyUsed`) | `IX_TicketValidations_TicketId` |
| Thống kê lượt soát theo chuyến (dòng 24) | `IX_TicketValidations_TripId_ScannedAt` |
| Chống trùng khi đồng bộ ngoại tuyến (dòng 18) | `IX_TicketValidations_ClientScanId` (**UNIQUE**) |
| Tài khoản đã quét (khoá ngoại `ValidatedByUserId`) | EF tự sinh |
| Trạm khách lên (khoá ngoại `BoardingStopId`) | EF tự sinh |

`ScannedAt` đứng sau `TripId` (không đứng trước) vì câu truy vấn của dòng 24 luôn lọc theo **một**
chuyến rồi mới sắp theo thời gian — đặt `ScannedAt` lên trước sẽ khiến chỉ mục không dùng được cho
phép lọc.

⚠️ **Cố ý KHÔNG khai chỉ mục cho `ValidatedByUserId` và `BoardingStopId`:** chưa có truy vấn nào của
Sprint 4 hỏi *"tài khoản này soát bao nhiêu lượt"* hay *"trạm này có bao nhiêu khách lên"*, nên chỉ
mục khoá ngoại do EF tự sinh là đủ. Đây là bảng **ghi dày nhất dự án** (mỗi lượt quét một dòng) —
thêm chỉ mục là trả giá ghi cho **mọi** lượt soát. Chỉ thêm khi có câu truy vấn thật; ai cần thì nhắn
Dăm.

## 6. Việc còn lại của người khác

- **Hiếu** (dòng 15 + dòng 19) — `POST /tickets/validate`: gọi `ITicketQrService.TryVerify` **trước**,
  rồi mới tra `Tickets` theo `Id` đã ký (docs/30 §3.2: ⚠️ đừng tin `ticketId` trước khi `TryVerify`
  trả `true`). Chữ ký sai → `NotFound`, **không** phân biệt với mã rác (§3). Chốt hình dạng endpoint
  trong `docs/api-contract.md` **trước khi code** (⛔ luật 5) — hợp đồng hiện **chưa có** mục soát vé
  (§7.4). Phân quyền (dòng 19): chỉ `Driver`/`Manager` được gọi.
- **Kiên** (dòng 16) — chống soát trùng: lượt thắng lật `Tickets.Status = 'Used'` + `Tickets.UsedAt`;
  lượt thua **ghi một dòng `Result = 'AlreadyUsed'`** vào bảng này rồi trả `valid: false`. Đây là chỗ
  §4.3 mục 2 được thực hiện — **đừng** để lượt thua ghi `Valid`.
- **Kiên** (dòng 17) — ghi `BoardingStopId` của lượt lên xe. ⚠️ Đây là trạm **thực tế**, khác
  `Tickets.BoardingStopId` (trạm khách chọn lúc mua) — xem §2.1.
- **Hoàng** (dòng 18) — hàng đợi ngoại tuyến: sinh `ClientScanId` ở **máy quét**, gửi kèm
  `ScannedAt`. Bắt lỗi unique của §4.1 và coi lượt gửi lại là **thành công** (idempotent), **đừng**
  để nó thành 500. Kiểm khoảng hợp lý của `ScannedAt` (§4.2).
- **Băng** (dòng 20) — màn quét QR. `ticketValidationApi.ts` đã có sẵn trên nhánh
  `feature/15-quet-qr-camera` với `USE_MOCK_DATA = true`; khi `api-contract.md` chốt thì viết nhánh
  `api` rồi lật cờ. Tập `reason` để vẽ là **đúng năm** giá trị ở §3.
- **Thịnh** (dòng 21, 23) — hiển thị kết quả xanh/đỏ + âm thanh; nhập mã thủ công. Nhãn + màu cho
  từng `reason` đã có sẵn ở `TICKET_VALIDATION_REASON_META`.
- **Hạnh** (dòng 22, 24) — danh sách hành khách đã lên xe theo chuyến; thống kê lượt soát theo chuyến.
  ⚠️ Dòng 24 đếm **lượt soát**, không đếm vé — lọc `Result = 'Valid'` nếu muốn số khách thật lên xe.
- **Giàng A Vàng** (dòng 25–26) — test API soát vé: vé hợp lệ, vé giả, vé đã dùng. ⚠️ InMemory
  **không** cưỡng chế unique index/FK (§4.3), nên **đừng** viết test *"gửi hai lượt cùng `ClientScanId`
  thì lượt thứ hai phải lỗi"* rồi tin nó kiểm được §4.1 — trên InMemory nó sẽ **xanh dù ràng buộc
  không tồn tại**. Muốn kiểm thật thì phải chạy trên PostgreSQL.
- **Ai cần thêm cột** — nhắn Dăm, không tự sửa `Migrations/*`, `ModelSnapshot.cs`, `Entities/`.

## 7. Việc cần báo nhóm (không phải việc code)

1. **A9 cần bổ sung dòng `TicketValidations`.** A9 đang là 20 bảng đóng băng và không có bảng này
   (§1). Bảng phân công Sprint 4 (dòng 14) đã giao migrate, nên **nhóm chốt bổ sung A9** cho khớp —
   nếu không thì mọi tài liệu còn nói "20 bảng" trong khi CSDL có 23. Đây là lần thứ **ba** việc này
   được nhắc (`docs/28` §7.4, `docs/32` §1). **Nhóm nên bổ sung một lượt cả ba bảng còn thiếu**
   (`CancellationRequests`, `TicketChanges`, `TicketValidations`) thay vì nhắc lại từng lần.
2. **`docs/03-quy-uoc.md` vẫn không tồn tại trong repo.** Mọi tham chiếu A1–A9 ở đây chỉ còn là trích
   dẫn trong code và tài liệu. Đây là lần thứ **sáu** việc này được nhắc — nhóm nên đưa file vào
   repo.
3. **Xung đột lịch sprint: US 15 nằm ở hai sprint khác nhau.** `docs/02-sprint-roadmap.md` §2 xếp
   US 15 (*"Soát vé QR"*) ở **Sprint 5**, còn bảng phân công Sprint 4 giao **toàn bộ** story này — kể
   cả migration — ở **Sprint 4** (dòng 14–26). Bản này làm theo **bảng phân công Sprint 4**, đúng
   luật "xung đột #7" của `quy uoc.docx` mục J: khi hai tài liệu phân công trái nhau thì theo phân
   công nhận trực tiếp từ Scrum Master. Thêm một điểm nghiêng về bảng phân công: chính
   `docs/02-sprint-roadmap.md` ghi trạng thái **"đề xuất, chờ nhóm chốt"** ở đầu file. Đây là xung
   đột **y hệt** đã gặp ở US 5 (`docs/32` §7.3) — nhóm nên chốt một lần cho cả hai.
4. **`docs/api-contract.md` chưa có mục cho US 15.** Hợp đồng đã có sẵn mục "Vé điện tử —
   `/tickets`" nhưng **chưa có** `POST /tickets/validate`. Hình dạng endpoint là **việc của chủ API**,
   nên tài liệu này **không tự chốt**; đề nghị Hiếu chủ trì một mục "Soát vé QR" trong
   `docs/api-contract.md`, chốt trước rồi mới code (⛔ luật 5). Hình dạng đề xuất đã có sẵn ở
   `docs/29` §3 và `ticketValidationApi.ts`.
5. **Vé tháng có dùng chung endpoint soát vé không?** `MonthlyPasses.Code` **cũng** là mã QR soát vé
   (A6 ghi rõ: *"mã QR soát vé tháng"*). Nếu phụ xe quét một mã mà không biết trước là vé lượt hay vé
   tháng thì endpoint phải tra **hai** bảng — và lúc đó bảng này cần thêm một cột trỏ
   `MonthlyPasses`. Đây là câu hỏi **đã nêu ở `docs/29` §4 câu (4)** và **vẫn chưa ai chốt**.
   Quyết định của nhánh này: **giữ bảng chỉ phục vụ `Tickets`** (đúng phạm vi US 15), **không** thêm
   cột đoán trước. Thêm một cột nullable sau này không tốn migration dữ liệu, còn đoán sai bây giờ thì
   phải sửa lại kèm migrate. **Hiếu + nhóm chốt trước khi làm endpoint.**

## 8. Migration đã sinh và đã soát — 13/10/2026

Hai file, **cố ý tách làm hai** (vì sao ở §8.1):

| File | Nội dung |
|---|---|
| `Migrations/20261013143603_Sprint4_Notifications.cs` | `CREATE TABLE "Notifications"` — bù migration còn thiếu của `main` (§8.1) |
| `Migrations/20261013144302_Sprint4_TicketValidations.cs` | `CREATE TABLE "TicketValidations"` — bảng của nhánh này |

`AppDbContextModelSnapshot.cs` bị sửa — **thuần thêm 136 dòng, 0 dòng xoá**, nên không bảng nào khác
bị đụng. Tên theo A7 (`<Sprint><Nội dung>`).

### 8.1 Vì sao có HAI file, và vì sao `Notifications` đi kèm nhánh này

`main` đang có **nợ migration**: commit `8246d08` thêm entity `Notification` + cấu hình
`AppDbContext.Notification.cs`, nhưng **chưa bao giờ sinh migration** cho nó — dù `docs/33` §10 đã ghi
sẵn lệnh kèm bảng kiểm. Hệ quả: `TicketRequestResultNotifier` (task Sprint 4 dòng 7, Phùng Duy Hoàng)
**không chạy được** trên CSDL chung vì bảng không tồn tại.

Lệnh `dotnet ef migrations add` diff **toàn bộ** model trong **một** file, nên lần sinh đầu tiên ra
một migration **gộp cả hai bảng** của **hai story khác nhau** — sai lối một-migration-một-story mà
`docs/28` §8 và `docs/32` §8 đã theo. Cách chữa: tạm ẩn phần khai báo `TicketValidation` (cả thuộc
tính `DbSet` lẫn lời gọi `ConfigureTicketValidation`), sinh `Sprint4_Notifications` **một mình**, rồi
khôi phục và sinh `Sprint4_TicketValidations`. Cả hai file migration đều **chưa từng** chạy
`database update`, nên không có dấu vết nào trong `__EFMigrationsHistory` trên CSDL chung.

⚠️ **Cái bẫy đã sập một lần, ghi lại để người sau khỏi mất nửa buổi:** khi phần khai báo bị ẩn mà
lệnh `migrations add` vẫn được chạy tiếp, EF sinh ra một migration **rỗng** (`Up()`/`Down()` không có
gì) vì model không đổi. Migration rỗng **không** báo lỗi — nó chỉ lặng lẽ ghi một dòng vào
`__EFMigrationsHistory` và bảng **không bao giờ** được tạo. Luôn mở file vừa sinh ra đọc `Up()`.

### 8.2 Lệnh chạy — cần biến môi trường GIẢ

Cùng lối `docs/32` §8: máy chưa có `appsettings.Development.json` (file bị .gitignore, repo public)
thì host design-time không dựng được model. `migrations add`, `has-pending-model-changes` và
`migrations script` đều **không mở kết nối** nào — **đừng tạo file `appsettings.Development.json`
thật**, `scripts/setup-csdl.sh` bỏ qua file đã tồn tại nên nó sẽ chặn luôn phần cài đặt thật về sau.
Máy này còn đòi thêm `Jwt__Key` (so với `docs/32` §8 chỉ cần hai biến):

```bash
ConnectionStrings__Default="<chuoi-ket-noi-gia — chi can dung dinh dang>" \
Jwt__Key="<chuoi-bat-ky-dai-it-nhat-32-byte>" \
dotnet ef migrations add Sprint4_TicketValidations --project backend/SmartBus.Api
```

⚠️ **`dotnet ef migrations add` build project TRƯỚC khi ghi `ModelSnapshot.cs`**, nên DLL còn lại trên
đĩa giữ snapshot đời cũ. Chạy ngay `has-pending-model-changes --no-build` sau đó sẽ báo **sai** là
*"Changes have been made to the model since the last migration"*. Chạy `dotnet build` một lần rồi đo
lại là hết. Đã sập đúng bẫy này trong nhánh.

### 8.3 Hai phép kiểm, không cần CSDL thật

Chạy trên đúng file migration vừa sinh:

| Phép kiểm | Kết quả |
|---|---|
| `dotnet ef migrations has-pending-model-changes --no-build` (sau khi `dotnet build`) | ✅ "No changes have been made to the model since the last migration" |
| `dotnet ef migrations script 20261012023745_Sprint4_CancellationRequests_TicketChanges 20261013144302_Sprint4_TicketValidations --no-build` rồi `grep -in xmin` | ✅ không dòng nào — không cột `xmin` lọt vào SQL |
| SQL đó có `ALTER TABLE` / `DROP` / `UPDATE` / `DELETE` nào không | ✅ không — **chỉ** `CREATE TABLE` ×2 + `CREATE INDEX` ×6 + 1 dòng `INSERT` vào `__EFMigrationsHistory` |
| `dotnet build` | ✅ 0 warning, 0 error |
| `dotnet test backend/SmartBus.Tests/SmartBus.Tests.csproj` | ✅ **1287/1287** passed, 0 failed, 0 skipped |

SQL sinh ra khớp §2, §4 và §5:

| Hạng mục | Kết quả |
|---|---|
| `CREATE TABLE "TicketValidations"` | ✅ **10 cột** đúng bảng ở §2 |
| `Id` · `TicketId` · `TripId` · `ValidatedByUserId` · `BoardingStopId` · `ClientScanId` | ✅ `uuid`; `TicketId`, `TripId`, `BoardingStopId`, `ClientScanId` **nullable**, `ValidatedByUserId` NOT NULL — đúng cột "NULL" ở §2 |
| `ScannedCode` | ✅ `character varying(200)`, NOT NULL |
| `Result` | ✅ `character varying(20)` — lưu **chuỗi** (A3), không phải `integer` |
| `ScannedAt` · `CreatedAt` | ✅ **cả hai** `timestamp with time zone` (không `timestamp` thiếu múi giờ) |
| Khoá ngoại | ✅ **4 FK** — `Tickets`, `Trips`, `Users`, `Stops` — **tất cả `ON DELETE RESTRICT`** (A5) |
| Chống trùng ngoại tuyến | ✅ `CREATE UNIQUE INDEX "IX_TicketValidations_ClientScanId"` — đúng câu ở §4.1, **UNIQUE thường** không phải partial index |
| Chỉ mục thống kê | ✅ `IX_TicketValidations_TripId_ScannedAt` — đúng thứ tự cột ở §5 |
| Chỉ mục tra vé | ✅ `IX_TicketValidations_TicketId` |
| Chỉ mục khoá ngoài do EF tự sinh | ✅ `IX_TicketValidations_BoardingStopId`, `IX_TicketValidations_ValidatedByUserId` — đúng như §5 đã ghi trước |
| `UpdatedAt` | ✅ **không có** — bảng chỉ ghi thêm (A4) |
| `IsDeleted` | ✅ **không có** (A4) |
| `xmin` | ✅ **không có** — khác `Payments`/`Vouchers`/`CancellationRequests`: bảng chỉ ghi thêm **không có** nhánh đua sửa-cùng-dòng nào để mà chặn, cùng lý do đã ghi ở `AppDbContext.Notification.cs` |

⚠️ **Bộ test 1287/1287 xanh KHÔNG chứng minh chỉ mục UNIQUE ở §4.1 tồn tại** — InMemory không cưỡng
chế unique index lẫn FK (§4.3). Con số đó là **hồi quy** (không làm vỡ thứ đang chạy), không phải
bằng chứng cho chốt chống trùng. Chốt đó chỉ có thật ở PostgreSQL.

### 8.4 Việc còn lại, theo đúng thứ tự `docs/25-huong-dan-csdl-chung.md` §4

1. PR nhánh này → **có người khác approve** (⛔ luật 7) → **Squash and merge** vào `main`.
2. **Sau khi merge** mới áp lên CSDL chung:
   `dotnet ef database update --project backend/SmartBus.Api`.
   Lệnh này áp **cả hai** bảng — `Notifications` (nợ của `main`) lẫn `TicketValidations`. Trước bước
   đó, endpoint nào chạm chúng sẽ lỗi `relation "…" does not exist`.

### 8.5 ⚠️ Phân tích đồ thị (GitNexus) KHÔNG chạy được trên máy này

`CLAUDE.md` bắt buộc chạy `impact` trước khi sửa và `detect_changes` trước khi commit. Trên máy dựng
nhánh này, **cả hai không chạy được**: repo không có `.gitnexus/`, và `analyze` tự crash khi băm các
file trong npm-cache của chính nó. Nên **không có** kết quả `impact`/`detect_changes` cho nhánh này —
thay bằng: đọc trực tiếp `Entities/Ticket.cs`, `TicketStatus.cs`, `Notification.cs`,
`AppDbContext.Ticket.cs`, `AppDbContext.Notification.cs` để đối chiếu lối đặt tên và `Restrict`; cộng
với `dotnet build` (0/0) và `dotnet test` (1287/1287) ở §8.3. **Ai có máy chạy được GitNexus nên
chạy `impact` cho `TicketValidation` và `AppDbContext` trước khi merge** — bảng này ghi dày nhất dự án
và `AppDbContext.cs` là file dùng chung.

