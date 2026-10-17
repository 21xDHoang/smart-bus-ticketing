# API soát vé QR — `POST /api/tickets/validate` (dòng 15 + dòng 19, US 15, Sprint 4)

> Task *"API soát vé: nhận mã QR, xác thực chữ ký, trả kết quả hợp lệ/không"* (dòng 15) +
> *"Phân quyền: chỉ tài xế/phụ xe được soát vé"* (dòng 19) — **Trần Trung Hiếu** (Chủ API).
> Hợp đồng đầy đủ: mục "Soát vé QR" của `docs/api-contract.md`.
>
> Đọc kèm: `docs/29-ban-giao-api-kiem-tra-hieu-luc-qr.md` (đề xuất hợp đồng gốc),
> `docs/30-service-ma-qr-ky-so.md` (định dạng mã ký), `docs/34-csdl-soat-ve-qr.md` (bảng
> TicketValidations + bất biến), `docs/39-hang-doi-ngoai-tuyen-soat-ve.md` (giao thức hàng đợi —
> endpoint này là bên gọi đầu tiên của `ITicketValidationRecorder`).

## 1. Đã có gì trên nhánh này

| File | Việc |
|---|---|
| `backend/SmartBus.Api/Dtos/TicketValidations/ValidateTicketRequest.cs` | Body request — `code` bắt buộc, `tripId`/`clientScanId`/`scannedAt` tuỳ chọn |
| `backend/SmartBus.Api/Dtos/TicketValidations/TicketValidationResponse.cs` | Response `{ valid, reason, ticket }` |
| `backend/SmartBus.Api/Dtos/TicketValidations/ValidatedTicketResponse.cs` | Hình dạng vé kèm kết quả — khớp interface FE `ticketValidationApi.ts` + `usedAt` |
| `backend/SmartBus.Api/Services/ITicketValidationService.cs` | Hợp đồng service — luồng ba bước ghi ở doc-comment |
| `backend/SmartBus.Api/Services/TicketValidationService.cs` | Cài đặt — xác thực chữ ký → tra vé → **trạm lên xe (dòng 17)** → ghi nhật ký |
| `backend/SmartBus.Api/Services/ITicketBoardingStopService.cs` | Hợp đồng quyết định trạm lên xe thực tế — ba luật ở doc-comment (dòng 17, Kiên) |
| `backend/SmartBus.Api/Services/TicketBoardingStopService.cs` | Cài đặt — chỉ lượt Valid, trạm phải thuộc `RouteStops` của tuyến chuyến trên vé |
| `backend/SmartBus.Api/Controllers/TicketValidationController.cs` | `POST /api/tickets/validate`, `[Authorize(Roles = "Driver,Manager")]` |
| `backend/SmartBus.Api/Program.cs` | 2 dòng DI `ITicketValidationService` + `ITicketBoardingStopService` (kèm comment nhờ Hoàng xem — file của Hoàng) |
| `backend/SmartBus.Tests/TicketValidationApiTests.cs` | 22 test API — bảng trường, năm lý do, idempotent, nhật ký US 23 |
| `backend/SmartBus.Tests/TicketBoardingStopServiceTests.cs` | 12 test service trạm lên xe (dòng 17) |
| `backend/SmartBus.Tests/TicketBoardingStopApiTests.cs` | 8 test tích hợp — trạm thực tế, sai tuyến, lượt bị từ chối, gửi lại (dòng 17) |
| `backend/SmartBus.Tests/TicketValidationWiringTests.cs` | 2 test wiring — xoá dòng DI là đỏ |
| `backend/SmartBus.Tests/TicketValidationTestFactory.cs` | Host test riêng: TestAppFactory bị sealed, xem §4 |
| `docs/api-contract.md` | Mục "Soát vé QR" — bản chốt của đề xuất docs/29 §3 |

**Không sửa file của ai khác** ngoài hai dòng đã có tiền lệ của vai trò Chủ API: dòng DI trong
Program.cs (Hoàng) và dòng khai `POST /api/tickets/validate` trong bảng phủ sóng của
`AuditLogCoverageTests.cs` (Vàng) — cả hai đều kèm comment giải thích.

⚠️ Nhánh dòng 17 (Kiên) sửa thêm hai file của Chủ API, sau khi đã cập nhật `docs/api-contract.md`
trước (luật 5): `ValidateTicketRequest.cs` (thêm trường `boardingStopId`) và
`TicketValidationService.cs` (thêm một dependency + bước 2b). **Cần Hiếu review trong PR** — chi
tiết ở §3.5.

## 2. Luồng ba bước — thứ tự cố định

```
request ──► (0) tripId khai thì phải tồn tại, không thì 404
        ──► (1) ITicketQrService.TryVerify(code) TRƯỚC — chữ ký sai/mã rác = NotFound, không phân biệt
                │ hợp lệ: tra Tickets theo Id ĐÃ KÝ (đừng tin id trước khi chữ ký khớp — docs/30 §3.2)
                ▼
           (2) xét kết quả theo thứ tự docs/29 §3:
                Cancelled → AlreadyUsed → WrongTrip → Expired → Valid
        ──► (2b) ITicketBoardingStopService.ResolveAsync — trạm lên xe THỰC TẾ (dòng 17):
                 chỉ lượt Valid, trạm phải thuộc tuyến của chuyến TRÊN VÉ; gửi lại thì bỏ qua
        ──► (3) ITicketValidationRecorder.RecordAsync — MỌI lượt quét đều ghi, kể cả lượt từ chối
        ──► response lấy từ DÒNG ĐÃ GHI (recorded.Result), không phải kết quả vừa tính
```

### 2.1 Thứ tự năm phép kiểm khớp docs/29 §3

`NotFound` → `Cancelled` → `AlreadyUsed` → `WrongTrip` → `Expired` → `Valid`. Đổi thứ tự là đổi hợp
đồng với màn quét QR — frontend đã viết sẵn năm lý do theo đúng tập này.

### 2.2 `WrongTrip` chỉ kết luận được khi có `tripId`

`tripId` là chuyến phụ xe ĐANG CHẠY lúc quét, khác `Ticket.TripId` (chuyến ghi trên vé) — hai giá
trị lệch nhau chính là nội dung `WrongTrip` (docs/34 §2.5). Không có `tripId` thì bỏ phép đối chiếu.

### 2.3 `Expired` = quá giờ khởi hành, hoặc chuyến đã chốt xong/huỷ

Ba điều kiện, đủ một là hết hạn:

1. `Trip.DepartureTime <= giờ server` (UTC) — "chuyến đã khởi hành xong" (docs/29 §3). Không có
   khoảng ân hạn: docs/29 không nhắc, không tự bịa ra.
2. `Trip.Status = Completed` — kể cả khi `DepartureTime` chưa tới (chốt trạng thái là tín hiệu mạnh
   hơn giờ dự kiến).
3. `Trip.Status = Cancelled` mà vé còn `Paid` (chưa hoàn tiền xong) — vé không còn hiệu lực; trong
   tập NĂM lý do đã chốt, `Expired` là lý do gần nhất (đừng thêm `TripCancelled` — docs/34 §3: thêm
   giá trị là đổi hợp đồng với màn quét).

### 2.4 Câu trả lời lấy từ dòng ĐÃ GHI — idempotent

`RecordAsync` trả về dòng VỪA ghi HOẶC dòng đã ghi từ lần gửi trước cùng `clientScanId`. Response
phải lấy `recorded.Result` — không lấy kết quả vừa tính: lượt gốc ghi `Valid`, vài phút sau hàng đợi
gửi lại, lúc này dòng 16 (Kiên) đã lật `Ticket.Status = Used` — tính lại sẽ ra `AlreadyUsed` trong
khi nhật ký vẫn ghi `Valid` (docs/39 §4.2). Hai lần gửi cùng một lượt phải trả về cùng một câu.

## 3. Các quyết định đáng đọc trước khi sửa

### 3.1 Endpoint KHÔNG lật `Ticket.Status = Used`

Việc đó thuộc dòng 16 *"API chống soát trùng: đánh dấu vé đã sử dụng theo chuyến"* (Kiên) — recorder
và endpoint đều không đụng trạng thái vé (docs/39 §5). **Ranh giới đã biết cho tới khi dòng 16 có:**
hai lượt soát cùng một vé còn `Paid` đều trả `Valid` và mỗi lượt ghi một dòng nhật ký. Khi Kiên làm
dòng 16, `DecideResult` của service này tự nhiên trả `AlreadyUsed` cho lượt thứ hai vì nó đọc
`Status = Used` — không cần sửa gì ở đây; Kiên chỉ cần đảm bảo lượt thua của cuộc đua ghi
`AlreadyUsed` chứ không ghi `Valid` (docs/34 §4.3 mục 2).

### 3.2 Tra thông tin hiển thị bằng truy vấn THEO KHOÁ NGOẠI, không Include/projection chạm navigation

Provider InMemory của tầng test bỏ rơi CẢ BẢN GHI GỐC khi Include (và cả projection có điều kiện
null) trỏ tới navigation không có dòng nào trong store — vé seed mồ côi thiếu Route (đúng lệ "seed
dòng mồ côi" của bộ test) khiến truy vấn trả null thay vì vé kèm cột hiển thị rỗng. PostgreSQL thì
LEFT JOIN bình thường, nhưng hai nơi phải cư xử như nhau. `LoadRowAsync` góp thông tin bằng các
truy vấn nhỏ theo Id từng bảng (vé → chuyến → tuyến, ghế, hai trạm, người) — trên CSDL thật các
khoá ngoại này không thể mồ côi (Restrict) nên mọi truy vấn trúng một dòng; chi phí vài câu SELECT
theo khoá chính không đáng kể cho một lượt soát.

### 3.3 `NotFound` gộp ba ca — không phân biệt ra ngoài

Mã rác, mã sai chữ ký, mã ký cho vé không tồn tại: cùng một `NotFound`, `ticket: null` (docs/34 §3).
Người soát chỉ cần một câu trả lời "không cho lên xe"; chuỗi làm giả nằm nguyên văn ở
`TicketValidations.ScannedCode` cho điều tra sau.

### 3.4 Bất biến `TicketId IS NULL ⟺ NotFound` do endpoint giữ

Endpoint dựng `RecordTicketValidationRequest` với `TicketId = null` đúng khi `Result = NotFound`
(chữ ký sai hoặc vé không tồn tại) — recorder kiểm cả hai chiều và 400 nếu lệch (docs/39 §2.2).

### 3.5 Trạm lên xe THỰC TẾ — dòng 17 (Kiên)

Request nhận thêm `boardingStopId` (tuỳ chọn), đi qua `ITicketBoardingStopService.ResolveAsync`
trước khi vào nhật ký. Ba luật:

1. **Chỉ lượt `Valid` mới ghi trạm.** Lượt bị từ chối là lượt không có ai lên xe — gửi trạm kèm
   cũng bị bỏ qua, không phải lỗi (response vẫn là kết quả soát bình thường).
2. **Trạm phải thuộc tuyến của chuyến TRÊN VÉ** (`RouteStops`). Không đối chiếu
   `Tickets.BoardingStopId`: đó là trạm khách **chọn lúc mua**, còn cột nhật ký là trạm khách **thực
   sự bước lên** (A8.1; docs/34 §2.1). Khách mua ở trạm A nhưng lên ở trạm B là ca bình thường —
   nhật ký ghi B, vé vẫn giữ A. Sai tuyến → 400 `errors.boardingStopId`, không ghi dòng nhật ký nào.
3. **Gửi lại cùng `clientScanId` không xét lại trạm.** Recorder đọc trước và trả dòng gốc
   (docs/39 §2.4 mục 2); kiểm lại ở đây sẽ biến một lượt đã thành công thành 400 nếu lần gửi sau
   mang giá trị khác. Dòng đã ghi giữ nguyên trạm của lần đầu.

## 4. `TicketValidationTestFactory` — vì sao không bọc TestAppFactory

TestAppFactory là `internal sealed` nên không kế thừa được. Cách tưởng chừng đúng là
`new TestAppFactory().WithWebHostBuilder(b => b.UseSetting("TicketQr:SigningKey", …))` — chính
TicketQrWiringTests đã dùng — nhưng bản bọc đó **KHÔNG áp dụng `ConfigureTestServices`** của
TestAppFactory: DbContext vẫn trỏ **PostgreSQL thật** và mọi lần seed đi thẳng vào CSDL chung (bộ
test soát vé này đã dính đúng bẫy đó trong lúc dựng — bài học ghi lại để người sau khỏi mất nửa
buổi như docs/34 §8.1 đã làm). Vì vậy nhánh này có factory riêng chép ba mảnh bắt buộc của
TestAppFactory: JWT bằng `UseSetting`, gỡ DbContext Npgsql thay bằng InMemory, gỡ năm job nền (gỡ
hẳn như `NhaMaySqlite` — không có test wiring nào cần giữ hình dạng đăng ký job).

⚠️ **Nhóm nên gỡ bẫy này một lần cho cả repo**: bất cứ ai cần thêm setting vào TestAppFactory mà
dùng `WithWebHostBuilder` sẽ lại ghi thẳng vào CSDL thật. Cách gỡ: bỏ `sealed` khỏi TestAppFactory
(file của người khác — cần người sở hữu làm), hoặc tách phần cấu hình InMemory ra một lớp chung.

## 5. Test

```
dotnet test backend/SmartBus.Tests/SmartBus.Tests.csproj --filter "FullyQualifiedName~TicketValidation"
```

**25 ca mới** (23 API + 2 wiring), bộ backend đầy đủ **1463/1463 xanh**:

| Nhóm | Số bài | Ghìm điều gì |
|---|---|---|
| Quyền | 3 | 401 không token; 403 hành khách; **Manager được soát** (dòng 19 chốt Driver/Manager) |
| Request sai khuôn | 5 | Thiếu code → 400 `errors.code`; code rỗng → 400 `errors.code`; code 201 ký tự → 400 `errors.scannedCode` (recorder); `scannedAt` quá tương lai → 400 `errors.scannedAt`; `tripId` không tồn tại → 404 |
| Chữ ký | 3 | Mã rác → NotFound + ghi nguyên văn + TicketId null; mã bị sửa 1 ký tự → NotFound (không phân biệt với rác); mã ký cho vé không tồn tại → NotFound |
| Năm lý do | 9 | Valid kèm đầy đủ bảng trường ValidatedTicket + dòng nhật ký đúng; Cancelled; AlreadyUsed kèm usedAt; WrongTrip (có tripId); không tripId thì không kết luận WrongTrip; Expired ×3 (quá giờ / Completed / chuyến huỷ vé còn Paid); chưa khởi hành → Valid |
| Hàng đợi ngoại tuyến | 2 | Gửi lại cùng clientScanId → kết quả GỐC không tính lại + không đẻ dòng hai; lượt online không clientScanId → mỗi lần một dòng |
| Nhật ký US 23 | 1 | POST ghi Create với Target "Validate" (giới hạn của AuditLogMiddleware — §7) |
| Wiring | 2 | Resolve được service từ app thật; soát được một mã thật qua service |

Nhánh dòng 17 thêm **20 test** (`TicketBoardingStopServiceTests` 12 + `TicketBoardingStopApiTests`
8): trạm khác trạm trên vé vẫn được ghi đúng; sai tuyến → 400 `errors.boardingStopId` và không ghi
dòng; lượt `Expired`/`NotFound` không ghi trạm; gửi lại cùng `clientScanId` giữ trạm lần đầu và
không đẻ dòng thứ hai, kể cả khi lần gửi sau mang trạm sai tuyến.

## 6. Câu hỏi mở — vé tháng (chưa ai chốt, từ docs/29 §4 câu 4)

`MonthlyPasses.Code` cũng là mã QR soát vé (A6). Endpoint này chỉ tra `Tickets` — mã vé tháng quét
vào trả `NotFound`. Nhóm cần quyết khi làm soát vé tháng:

- Dùng chung endpoint này → cần thêm cột trỏ `MonthlyPasses` vào `TicketValidations` (việc của
  Dăm) + thêm lý do mới cho "vé tháng hết hạn/sai tuyến" (đổi hợp đồng 5 lý do với màn quét của
  Băng) — docs/34 §7.5 đã phân tích.
- Hay tách endpoint riêng trên bề mặt `/monthly-passes`.

## 7. Giới hạn đã biết — nhật ký US 23 ghi Target "Validate"

`POST /api/tickets/validate` được AuditLogMiddleware ghi tự động (POST → Create) nhưng Target chỉ
là **"Validate"** chứ không phải `"TicketValidations:<id>"`: route không có tham số `{id}` nên
`ResolveTableName` rơi về đoạn tĩnh cuối (giới hạn đã được nhắc trước trong doc-comment của
middleware), và response hợp đồng không có trường `id` ở nóc để ghép. Nhật ký thật của lượt soát
nằm ở bảng `TicketValidations`; dòng AuditLogs chỉ chứng minh "có người gọi endpoint soát vé lúc
nào". Cùng nhóm giới hạn với `POST /seat-holds/{sessionCode}/extend` và `/release` — **nhóm cần
chốt MỘT cách xử lý chung** cho các endpoint hành động con; bảng phủ sóng
`AuditLogCoverageTests` đã khai kèm comment, test hành vi nằm ở `TicketValidationApiTests`.

## 8. Việc còn lại của người khác

| Việc | Ai |
|---|---|
| Màn quét QR: lật `USE_MOCK_DATA` trong `frontend/src/api/ticketValidationApi.ts` sang nhánh `api` thật (contract đã chốt) | Băng (dòng 20) |
| Chống soát trùng + lật `Ticket.Status = Used` — lượt thua phải ghi `AlreadyUsed` | Kiên (dòng 16) |
| ~~Ghi `BoardingStopId` của lượt lên xe thật (trạm khác trạm trên vé)~~ | ✅ Kiên (dòng 17) — xem §3.5 |
| Hàng đợi ngoại tuyến phía máy quét: sinh `clientScanId` lúc quét, IndexedDB, đồng bộ FIFO | Băng (dòng 18, đặc tả docs/39 §3) |
| Test API soát vé bổ sung + kiểm thử chéo trên môi trường online | Giàng A Vàng (dòng 25–26) |
| Chạy `database update` để áp bảng `TicketValidations` lên CSDL chung nếu chưa áp | Dăm |
