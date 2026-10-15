# Hàng đợi ngoại tuyến soát vé — Sprint 4: giao thức máy quét ↔ server

> Viết cho các task quanh **US 15** *"Là phụ xe/tài xế, tôi muốn quét mã QR trên vé của khách bằng
> ứng dụng mobile để xác thực vé hợp lệ"*, sheet *Sprint 4*:
> **Hoàng** (dòng 18 — mảnh **server** của hàng đợi ngoại tuyến; tài liệu này) · **Băng** (dòng 20 —
> màn quét QR bằng camera; **hàng đợi phía máy quét đặc tả ở §3**) · **Hiếu** (dòng 15 — API soát vé
> `POST /tickets/validate`; là **bên gọi đầu tiên** của service này, xem §4) · **Thịnh** (dòng 21 —
> hiển thị kết quả xanh/đỏ + âm thanh; đọc §3.4 trước khi làm trạng thái "chờ đồng bộ") · **Kiên**
> (dòng 16 — chống soát trùng) · **Hạnh** (dòng 22, 24 — danh sách đã lên xe, thống kê lượt soát theo
> chuyến) · **Giàng A Vàng** (dòng 25–26 — test API + kiểm thử chéo).
>
> 🟢 **Trạng thái:** mảnh **SERVER** đã xong trên nhánh `feature/18-hang-doi-ngoai-tuyen-soat-ve`
> (chưa merge): `ITicketValidationRecorder` + `TicketValidationRecorder` + DTO nội bộ, đã đăng ký DI,
> **18 test mới xanh** (§6). Bảng `TicketValidations` **đã có thật trên CSDL chung** — mọi migration
> đều đã áp, không còn dòng `(Pending)` (khác ghi chú "chưa áp lên CSDL chung" ở đầu `docs/34`, viết
> từ lúc còn chờ merge). **Chưa ai gọi service**: endpoint dòng 15 của Hiếu sẽ là bên gọi đầu tiên;
> trước mốc đó service là mã chết nhưng đã có test.

## 1. Ai đọc tài liệu này, để làm gì

| Người | Dòng | Đọc mục | Để làm gì |
|---|---|---|---|
| **Băng** | 20 | **§3** (toàn bộ) | Viết hàng đợi phía máy quét: sinh `clientScanId`, lưu bền, đồng bộ lại |
| **Hiếu** | 15 (+19) | §2, §4 | Nối `RecordAsync` vào `POST /tickets/validate`; bổ sung `api-contract.md` |
| **Thịnh** | 21, 23 | §3.4 | Trạng thái màn quét khi ngoại tuyến: "chờ đồng bộ", KHÔNG xanh/đỏ |
| **Kiên** | 16 | §5 | Ranh giới: recorder KHÔNG lật `Ticket.Status`; chống soát trùng vẫn là việc của dòng 16 |
| **Hạnh** | 22, 24 | §3.3, §3.4 | Vì sao một dòng có thể tới muộn; `ScannedAt` là giờ máy quét, không phải giờ server nhận |
| **Giàng A Vàng** | 25–26 | §6 | Danh sách test đã có, để khỏi viết trùng |

Nguồn gốc nhiệm vụ: `docs/34-csdl-soat-ve-qr.md` §6 — phân công nguyên văn của dòng 18:

> *"hàng đợi ngoại tuyến: sinh `ClientScanId` ở **máy quét**, gửi kèm `ScannedAt`. Bắt lỗi unique của
> §4.1 và coi lượt gửi lại là **thành công** (idempotent), **đừng** để nó thành 500. Kiểm khoảng hợp
> lý của `ScannedAt` (§4.2)."*

Hai vế đầu ("sinh ở máy quét", "gửi kèm") là **hợp đồng phía client** — không nằm trong quyền của
server — nên chúng được đặc tả ở §3 và phần server được viết khớp theo đúng chúng.

## 2. Mảnh SERVER — cái gì đã có trong repo

### 2.1 Năm file + một dòng DI

| File | Vai trò |
|---|---|
| `backend/SmartBus.Api/Dtos/TicketValidations/RecordTicketValidationRequest.cs` | DTO **nội bộ của service** (KHÔNG phải thân HTTP): `ScannedCode`, `TicketId?`, `TripId?`, `Result`, `ClientScanId?`, `ScannedAt?`, `BoardingStopId?` |
| `backend/SmartBus.Api/Services/ITicketValidationRecorder.cs` | Hợp đồng; doc-comment ở đây là nguồn chuẩn cho ai đọc code |
| `backend/SmartBus.Api/Services/TicketValidationRecorder.cs` | Cài đặt: đọc-trước → kiểm trường → ghi |
| `backend/SmartBus.Api/Program.cs` | Một dòng `AddScoped<ITicketValidationRecorder, TicketValidationRecorder>()` |
| `backend/SmartBus.Tests/TicketValidationRecorderTests.cs` + `TicketValidationRecorderRaceTests.cs` | 18 test mới (§6) |

### 2.2 Service làm gì — và KHÔNG làm gì

| Có làm | Không làm (việc của ai) |
|---|---|
| Chống trùng theo `ClientScanId` | Xác thực chữ ký QR — **dòng 15, Hiếu** |
| Kiểm khoảng hợp lý của `ScannedAt` (§3.3) | Tra vé, tính `Result` — **dòng 15, Hiếu** |
| Kiểm bất biến `TicketId IS NULL ⟺ Result = 'NotFound'` | Lật `Ticket.Status = Used` — **dòng 16, Kiên** |
| Kiểm `ScannedCode` rỗng / quá 200 ký tự | Phân quyền chỉ tài xế/phụ xe — **dòng 19, Hiếu** |
| Chuẩn hoá mọi `DateTime` về UTC trước khi ghi | Ghi lượt lên xe theo trạm (`BoardingStopId`) — **dòng 17, Kiên** |
| Ghi **một dòng** vào `TicketValidations` | |

**Vì sao tách đôi như vậy.** Cả lượt ONLINE lẫn lượt đồng bộ từ hàng đợi ngoại tuyến đều đi qua đúng
một cửa ghi này. Nếu để mỗi đường tự ghi nhật ký, hai đường sớm muộn ghi lệch nhau (khác cách chuẩn
hoá giờ, khác cách chống trùng) — và nhật ký kiểm toán ghi lệch nhau thì vô giá trị.

### 2.3 Hợp đồng idempotent — trái tim của dòng 18

> Gửi lại cùng `ClientScanId` — dù là lần gửi thứ hai do mất mạng, do app bị kill, hay do hai lượt
> thử chồng nhau — **luôn là THÀNH CÔNG** và trả về **ĐÚNG dòng đã ghi lần đầu**; không bao giờ đẻ
> dòng thứ hai, không bao giờ nổ 500 vì lỗi unique.

Ba đường dẫn tới cùng kết quả đó, cả ba đều có test ghìm:

| Tình huống | Cơ chế | Test ghìm |
|---|---|---|
| Gửi lại tuần tự (mạng chập, app kill) | Lượt **đọc-trước** theo `ClientScanId` thấy dòng cũ → trả luôn | `Gui_lai_cung_ClientScanId_tra_ve_dung_dong_da_ghi` |
| Gửi lại lượt đã ghi từ **hơn 72 giờ** trước | Đọc-trước nằm TRƯỚC cửa sổ `ScannedAt` — không bị trả 400 oan | `Gui_lai_luot_da_ghi_tu_truoc_qua_lau_van_thanh_cong` |
| Hai lượt thử **chồng nhau** (đua hiếm) | Lượt thua đâm chỉ mục UNIQUE → `DbUpdateException` → **đọc lại** thấy dòng thắng → trả về dòng đó | `Hai_luot_chong_nhau_cung_ClientScanId_tra_ve_dong_thang_khong_no_500` (SQLite) |

**Bên gọi vì vậy PHẢI lấy câu trả lời từ `ServiceResult.Data.Result`** — kết quả ĐÃ GHI đứng vững —
chứ không lấy kết quả vừa tự tính lại. Hai lần gọi cùng một lượt phải trả về cùng một câu trả lời;
chi tiết ở §4.2.

### 2.4 Ba quyết định cài đặt (đọc trước khi sửa service)

1. **Chống trùng bằng ĐỌC-TRƯỚC, không bằng phân tích mã lỗi.** Nhận diện lượt thua bằng cách đọc
   lại theo `ClientScanId`, không parse SqlState 23505 — mã đó không tồn tại trên SQLite của tầng
   test, và khoá cứng vào nhà cung cấp CSDL là thứ dự án này tránh.
2. **Đọc-trước nằm TRƯỚC mọi phép kiểm trường.** Một lượt đã ghi rồi thì gửi lại luôn thành công,
   không xét lại gì — kể cả `ScannedAt` đã quá cũ (xem hàng 2 của bảng §2.3).
3. **Nuốt lỗi unique, KHÔNG nuốt lỗi khác.** `DbUpdateException` mà đọc lại không thấy
   `ClientScanId` nào (khoá ngoại hỏng, cột quá dài…) thì **ném lại** — lỗi lập trình của bên gọi,
   phải ồn ào. Entity `Added` mồ côi được gỡ khỏi ChangeTracker trước khi ném, để context dùng chung
   của request không mang theo một entity sẽ nổ lại ở lần `SaveChanges` sau.

## 3. Giao thức hàng đợi phía máy quét (Băng — dòng 20)

Phần này là **hợp đồng** giữa máy quét và service ở §2. Service đã được viết khớp đúng những luật
dưới đây — đổi luật là phải đổi cả hai đầu.

### 3.1 Vòng đời một lượt soát

```
quét mã ──► tạo mục hàng đợi { clientScanId, code, tripId, scannedAt }   ← sinh id + chụp giờ TẠI ĐÂY
                 │
                 ├─ có mạng ──► gửi ngay ──► 200/400 ──► gỡ mục, hiện kết quả từ response
                 │                            └─ lỗi tạm ──► mục ở lại hàng đợi
                 └─ mất mạng ──► mục nằm lại ──► (khi có mạng) đổ tuần tự FIFO, cùng id cũ
```

Một lượt chỉ được coi là "xong" khi server đã trả lời dứt khoát (§3.5). Mọi trục trặc khác — mất
mạng, app bị kill, pin hết — chỉ có nghĩa là mục ở lại hàng đợi và sẽ được gửi lại y nguyên.

### 3.2 Luật 1 — `clientScanId` sinh ở LÚC QUÉT, không bao giờ sinh lại

- Sinh **một lần, ngay lúc quét**, bằng UUID v4 (`crypto.randomUUID()`), **trước lần gửi đầu tiên**
  — kể cả lượt đang online gửi được ngay.
- Mọi lần thử lại dùng **đúng id đó**, payload y nguyên.

**Vì sao không sinh lúc đồng bộ.** Giao vận là *at-least-once*: lượt gửi đầu có thể đã tới server
nhưng phản hồi lạc mất trên đường về. Nếu lúc đồng bộ mới sinh id mới, server không có cách nào
nhận ra đây là cùng một lượt — kết quả là **hai dòng nhật ký cho một lần quét**. `ClientScanId` là
thứ duy nhất phân biệt "gửi lại lượt cũ" với "lượt quét mới trùng mã" — và nó chỉ làm được việc đó
nếu sinh ở máy quét, tại lúc quét.

**Hệ quả bắt buộc:** mục hàng đợi phải được ghi xuống kho bền (IndexedDB khuyến nghị) **trước khi
gửi lần đầu**, không phải chỉ giữ trong RAM — app bị kill giữa chừng mà id chưa kịp lưu là mất luôn
khả năng chống trùng của lượt đó.

### 3.3 Luật 2 — `scannedAt` chụp tại lúc quét, ISO-8601 UTC

- Lấy `new Date().toISOString()` **tại thời điểm quét**, lưu vào mục hàng đợi — **không tính lại lúc
  đồng bộ**. Đồng bộ lúc 8h sáng cho lượt quét 6h sáng hôm qua thì `scannedAt` phải là 6h hôm qua.
- Chuỗi ISO **có hậu tố `Z`** (hoặc offset) — server nhận cả hai kiểu, nhưng `toISOString()` là lối
  đúng sẵn có trong JS.
- **Cửa sổ server chấp nhận** (chỉ áp cho lượt **CHƯA TỪNG** ghi):
  - Nhanh hơn server quá **5 phút** → 400 (`scannedAt`) — đồng hồ máy quét sai.
  - Cũ hơn **72 giờ** → 400 (`scannedAt`) — hàng đợi để chờ quá lâu.
  - Nằm trong khoảng → nhận, và ghi **đúng giờ máy quét khai**, không ghi đè bằng giờ đồng bộ
    (`ScannedAt` = giờ quét, `CreatedAt` = giờ server nhận — hai cột, hai ý nghĩa, xem docs/34 §2).
- **Lượt ĐÃ ghi thì không giới hạn tuổi**: gửi lại cùng `clientScanId` sau 4 ngày vẫn 200 (đọc-trước
  ở §2.3). Nghĩa là: mất phản hồi không bao giờ làm hỏng lượt; chỉ hàng đợi **chưa từng tới server**
  mới chết ở mốc 72 giờ — và cái chết đó là 400 dứt khoát, xử theo §3.5.

### 3.4 Luật 3 — ngoại tuyến KHÔNG có kết quả xanh/đỏ

Máy quét ngoại tuyến **không tự phán được** hợp lệ hay không: chữ ký QR do server xác thực, còn
"vé đã dùng chưa, hết hạn chưa, đúng chuyến không" là dữ liệu nằm ở server. Ngoại tuyến chỉ xác nhận
được đúng một điều: **đã lưu, chờ đồng bộ**.

- Màn quét lúc này hiện **trạng thái thứ ba** — không xanh, không đỏ (Thịnh dòng 21: màu/âm thanh
  riêng cho "chờ đồng bộ", hoặc im lặng chờ; đừng mượn màu của kết quả soát).
- Kết quả thật của lượt đó hiện ra **sau khi đồng bộ** — trong danh sách lượt đã soát (Hạnh, dòng
  22), không phải ngay lúc quét.
- Hệ quả cho thống kê (dòng 24): một dòng nhật ký có thể mang `ScannedAt` của hôm qua nhưng
  `CreatedAt` của hôm nay — đó là dấu vết của lượt ngoại tuyến, không phải lỗi dữ liệu.

### 3.5 Luật 4 — bảng ứng xử theo mã trả về

| Kết quả | Nghĩa | Hàng đợi | Màn quét |
|---|---|---|---|
| **200**, `valid: true` | đã ghi, hợp lệ | Gỡ mục | Xanh + vé (kết quả **từ response**) |
| **200**, `valid: false` | đã ghi, bị từ chối | Gỡ mục | Đỏ + `reason` (kết quả **từ response**) |
| **400** (`errors` có trường) | payload **vĩnh viễn** hỏng: quá 72 giờ, `scannedCode` rỗng/quá dài… | Gỡ mục + đánh dấu "không đồng bộ được" | Đưa vào danh sách lượt hỏng để phụ xe biết mà **quét lại** |
| **401 / 403** | hết phiên đăng nhập | **GIỮ NGUYÊN**, dừng đồng bộ | Nhắc đăng nhập lại rồi thử tiếp |
| **5xx** / mạng đứt / timeout | lỗi tạm | **GIỮ NGUYÊN**, thử lại **cùng `clientScanId`** (lùi dần) | Vẫn "chờ đồng bộ" |
| **409** | *không nên có* — lỗi unique đã bị chặn trong service (§2.4) | Coi như 400 + báo dev | — |

Hai điều cấm:

- **Không gỡ mục khi chưa có phản hồi dứt khoát.** Gỡ trước khi nhận 200/400 là mất lượt soát thật.
- **Không đổi `clientScanId` khi thử lại.** Đổi id là biến lượt gửi lại thành lượt mới — đúng cái
  mà luật 3.2 sinh ra để chặn.

**400 là lỗi KHÔNG THỂ thử lại** — đây là lý do service trả lỗi kèm tên trường trong `errors`
(`scannedCode` / `ticketId` / `scannedAt`), để màn quét phân biệt được "payload này chết" (bỏ, báo
quét lại) với "mạng đang chập" (giữ, thử lại).

### 3.6 Một lượt = một request; FIFO; lưu bền

- **Gửi từng mục, tuần tự theo thứ tự FIFO.** Server không cần thứ tự (mỗi mục có `scannedAt` riêng
  và `clientScanId` riêng), nhưng gửi tuần tự là đủ và tránh dồn dập ngay khi mạng vừa trở lại.
- **Thời điểm flush**: sự kiện `online` của trình duyệt, và sau mỗi lượt quét mới thì thử gửi luôn
  phần đang tồn.
- **Kho bền**: IndexedDB (khuyến nghị — đủ dung lượng, bất đồng bộ) hoặc bất cứ kho nào sống sót qua
  lần tắt app. RAM không tính là kho.
- **Thứ tự thao tác một mục**: gửi → nhận phản hồi → xử theo §3.5 → gỡ mục. Chết giữa chừng ở bước
  nào cũng chỉ dẫn tới gửi lại cùng id — idempotent lo phần còn lại.

## 4. Việc của Hiếu — endpoint dòng 15 và `api-contract.md`

### 4.1 Thân request thêm hai trường TUỲ CHỌN

`docs/29-ban-giao-api-kiem-tra-hieu-luc-qr.md` §3 đề xuất `{ code, tripId? }`. Hàng đợi cần thêm
hai trường:

```json
{
  "code": "SBT1:...",
  "tripId": "…uuid chuyến đang chạy…",
  "clientScanId": "…uuid máy quét sinh lúc quét…",
  "scannedAt": "2026-10-15T03:20:11.000Z"
}
```

- **Vắng cả hai** = lượt online kiểu cũ (server tự lấy giờ, không chống trùng) — client nào chưa
  nâng cấp không phải sửa gì, và mỗi lượt quét thật vẫn để lại một dòng (kể cả quét lặp).
- **Có `clientScanId`** = lượt có thể được gửi lại — server chống trùng theo nó.
- Endpoint nhận request → xác thực chữ ký + tra vé (việc của dòng 15) → dựng
  `RecordTicketValidationRequest` → gọi `RecordAsync`. Service lo phần còn lại.

`RecordAsync` chỉ trả **hai** khả năng: `Ok` (200, kèm dòng nhật ký) hoặc `Invalid` (400, kèm
`errors` theo trường: `scannedCode` / `ticketId` / `scannedAt`). Không có nhánh `NotFound`/`Conflict`
— endpoint không phải map ba nhánh, chỉ hai.

### 4.2 Gửi lại phải trả kết quả GỐC, không phải kết quả tính lại

Đây là câu dễ làm sai nhất khi nối endpoint, và là lý do nó được viết thành mục riêng:

> **Khi `RecordAsync` trả `Ok`, câu trả lời HTTP phải lấy từ `Data.Result` — kết quả ĐÃ GHI — không
> phải kết quả vừa tự tính ở bước xác thực.**

Lượt gốc ghi `Valid`; vài phút sau hàng đợi gửi lại, lúc này dòng 16 của Kiên đã kịp lật
`Ticket.Status = Used` — tính lại sẽ ra `AlreadyUsed`, trong khi nhật ký vẫn ghi `Valid`. Trả về kết
quả tính lại là để nhật ký tự mâu thuẫn với câu trả lời. Hai lần gọi cùng một lượt phải trả về cùng
một câu trả lời, và câu trả lời đúng là câu đã ghi.

### 4.3 `api-contract.md` chưa có mục US 15

Kiểm ngày 15/10: `api-contract.md` **chưa có** mục nào cho `POST /tickets/validate` (các mặt
`/tickets` khác thì có, và `/vouchers/validate` có). Cần Hiếu bổ sung **trước khi code client gọi
thật** (luật 5: sửa contract trước, báo người còn lại). Mục mới cần nói rõ:

- Hai trường tuỳ chọn `clientScanId` / `scannedAt` và ngữ nghĩa vắng mặt (§4.1).
- **Gửi lại cùng `clientScanId` → 200 với kết quả gốc** (§4.2) — kể cả khi kết quả gốc khác kết quả
  tính lại.
- 400 theo house-format `{ "message": "...", "errors": { "scannedAt": ["..."] } }`.
- Bốn câu hỏi treo ở `docs/29` §4 (đường dẫn, có ghi hay chỉ đọc, ràng buộc chuyến, vé tháng) vẫn
  **chưa chốt** — hai trong số đó (có ghi không; chuyến lấy từ đâu) ảnh hưởng trực tiếp thân request
  ở §4.1.

## 5. Ranh giới đã biết

- **Kết quả của lượt ngoại tuyến được tính Ở THỜI ĐIỂM ĐỒNG BỘ, không phải thời điểm quét.** Máy quét
  ngoại tuyến không tra được vé, nên `Result` do endpoint tính lúc nhận request — một lượt quét lúc
  vé còn hạn, đồng bộ lúc vé đã dùng xong, sẽ được ghi `AlreadyUsed` kèm `ScannedAt` cũ. Dòng nhật
  ký vì vậy mang hai mốc thời gian khác nhau về ý nghĩa; đừng đọc nó như kết luận tại thời điểm quét.
  Đây là ranh giới đã biết và chấp nhận — server không có cách nào biết trạng thái vé tại thời điểm
  quét trên một máy ngoại tuyến.
- **`ScannedAt` là giờ đồng hồ máy quét** — tin ở mức có kiểm soát (dung sai 5 phút nhanh / trần 72
  giờ, §3.3). **Đừng so sánh `ScannedAt` giữa các thiết bị khác nhau** để suy ra thứ tự (docs/34
  §4.2) — dùng để thống kê và đối chiếu, không dùng làm khoá thứ tự.
- **Bất biến `TicketId ⟺ NotFound` giữ ở tầng service, không phải ràng buộc CSDL** (docs/34 §4.3 —
  không diễn đạt được thành index). Dòng ghi thẳng bằng SQL tay có thể phá bất biến; đường nghiệp vụ
  thì không.
- **Lỗi CSDL không phải đua `ClientScanId` thì NÉM LẠI (500)** — khoá ngoại hỏng, cột quá dài… là lỗi
  lập trình của bên gọi, không có đường nuốt thành công giả (§2.4, quyết định 3).
- **Recorder KHÔNG lật `Ticket.Status = Used`** — việc của dòng 16 (Kiên). Service này chỉ ghi nhật
  ký; trạng thái vé do endpoint quyết (docs/34 §2.1).
- **Chưa ai gọi service** — endpoint dòng 15 của Hiếu là bên gọi đầu tiên. Giữa hai mốc này service
  là mã chết (đã có 18 test).
- **InMemory không cưỡng chế unique index** — các test "gửi lại" ở file InMemory chỉ ghìm được đường
  tuần tự (đọc-trước); nhánh đua nằm ở file SQLite. Chống trùng THẬT chỉ đảm bảo trên SQLite /
  PostgreSQL — tức là trên CSDL chung thì có.
- **Hàng đợi quá 72 giờ cho lượt chưa từng đồng bộ → 400 vĩnh viễn**; client phải báo phụ xe biết mà
  quét lại (§3.5). Lượt đã ghi thì không giới hạn tuổi.
- **Hai máy cùng quét một vé = hai lượt thật = hai dòng** (khác `clientScanId`) — đúng, vì đã có hai
  lần quét; việc chặn "soát vé đã dùng" là của dòng 16, không phải chống trùng hàng đợi.

## 6. File, test, số liệu

**18 test mới** — 16 trên InMemory (`TicketValidationRecorderTests.cs`) + 2 trên SQLite
(`TicketValidationRecorderRaceTests.cs`). Bộ backend đầy đủ: **1394/1394 xanh** (trước nhánh: 1376).

| Nhóm | Số bài | Ghìm điều gì |
|---|---|---|
| Đường online (không `ClientScanId`) | 2 | Hai lượt online tạo hai dòng; lượt không khai `ScannedAt` lấy giờ server, kiểu UTC |
| Gửi lại cùng `ClientScanId` | 3 | Trả đúng dòng gốc; trả **kết quả gốc** chứ không phải kết quả tính lại; lượt đã ghi quá 72 giờ vẫn 200 |
| Cửa sổ `ScannedAt` | 6 | +6 phút → 400; +4 phút → nhận; −73 giờ → 400; −25 giờ → nhận và giữ đúng giờ khai; Unspecified → dán nhãn UTC; Local → đổi giờ thật theo offset |
| Bất biến `TicketId ⟺ NotFound` | 2 | Cả hai chiều lệch đều 400 kèm trường `ticketId` |
| `ScannedCode` | 3 | Rỗng/khoảng trắng → 400; 201 ký tự → 400; đúng 200 ký tự → ghi được |
| Đua + ném lại (SQLite) | 2 | Chồng nhau cùng `ClientScanId` → trả dòng thắng, không 500, không đẻ dòng hai, ChangeTracker sạch; `DbUpdateException` không phải đua → **ném lại** |

**Bảng CSDL:** `TicketValidations` đã có trên CSDL chung (mọi migration đã áp, kiểm 15/10 —
`dotnet ef migrations list` không còn dòng `(Pending)`). `ClientScanId` có chỉ mục UNIQUE thường
(uuid nullable, nhiều NULL hợp lệ) — docs/34 §4.1.
