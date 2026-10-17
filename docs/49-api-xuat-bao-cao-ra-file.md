# API xuất báo cáo doanh thu / tỷ lệ lấp đầy / nhật ký ra file

> Task **"API xuất báo cáo doanh thu / lấp đầy / nhật ký ra file"** — US 24 *"Xuất dữ liệu"*,
> Sprint 4 dòng 78 của bảng phân công (người làm ghi: **Phùng Duy Hoàng**), nhánh
> `feature/78-api-xuat-bao-cao-ra-file`.
>
> 🟢 **Trạng thái:** ba endpoint + test đã xong trên nhánh. Hợp đồng đã ghi vào
> `docs/api-contract.md` **trước** khi viết code. Không có migration — cả ba đều chỉ đọc dữ liệu
> sẵn có và dựng file trong bộ nhớ.
>
> Task **không** gồm nút xuất trên giao diện (dòng 80 — task riêng) và **không** thay
> `GET /audit-logs` (bản JSON của Kiên): endpoint xuất nhật ký đã có từ trước, task này bổ sung
> định dạng PDF cho nó.

## 1. Endpoint

| Method | Endpoint | Quyền | Định dạng |
|---|---|---|---|
| GET | `/api/reports/revenue/export` | `ManagerOrAbove` | `.xlsx` mặc định, `.pdf` khi `format=pdf` |
| GET | `/api/reports/fill-rate/export` | `ManagerOrAbove` | `.xlsx` mặc định, `.pdf` khi `format=pdf` |
| GET | `/api/audit-logs/export` | `AdminOnly` | `.xlsx` mặc định, `.pdf` khi `format=pdf` |

Cả ba trả về **file nhị phân**, không phải JSON. Lỗi vẫn là JSON theo chuẩn chung
`{ "message": "...", "errors": { "field": ["..."] } }`.

### 1.1 Doanh thu

Query **giống hệt** `GET /api/reports/revenue`, thêm đúng một tham số:

| Tham số | Mặc định | Ý nghĩa |
|---|---|---|
| `granularity` | `day` | `day` / `week` / `month` (không phân biệt hoa/thường) |
| `from` | 29 ngày trước `to` | Ngày bắt đầu (lịch Việt Nam), tính cả |
| `to` | hôm nay (giờ VN) | Ngày kết thúc, tính cả |
| `routeId` | — | Lọc theo tuyến; không tồn tại → 404 |
| `format` | `xlsx` | `xlsx` / `pdf` |

Một dòng cho mỗi bucket — kể cả bucket 0 đồng, đúng những gì JSON trả về. Không có trần dòng
riêng: tối đa 366 ngày nên nhiều nhất 366 dòng (`day`), 54 (`week`) hoặc 13 (`month`).

### 1.2 Tỷ lệ lấp đầy

| Tham số | Mặc định | Ý nghĩa |
|---|---|---|
| `from` | 29 ngày trước `to` | Ngày bắt đầu (lịch Việt Nam), tính cả |
| `to` | hôm nay (giờ VN) | Ngày kết thúc, tính cả |
| `routeId` | — | Lọc theo tuyến; không tồn tại → 404 |
| `timeSlot` | — | `early` (05–09), `morning` (09–13), `afternoon` (13–17), `evening` (17–22), giờ VN; biên dưới tính, biên trên không |
| `format` | `xlsx` | `xlsx` / `pdf` |

Một dòng cho mỗi chuyến, sắp theo giờ khởi hành tăng dần (trùng giờ theo `Id`). Cột:
`Mã chuyến` · `Tuyến` · `Giờ khởi hành (VN)` · `Xe` · `Sức chứa` · `Khách` · `Tỷ lệ lấp đầy (%)`;
khối đầu file có tổng số chuyến, tỷ lệ trung bình, số chuyến đầy/trống.

### 1.3 Nhật ký kiểm toán

| Tham số | Mặc định | Ý nghĩa |
|---|---|---|
| `from` | 29 ngày trước hôm nay | Ngày bắt đầu, theo **UTC**, tính trọn ngày |
| `to` | hôm nay (UTC) | Ngày kết thúc, tính trọn ngày |
| `userId` | — | Lọc theo người thao tác (GUID) |
| `action` | — | Lọc theo một trong 6 mã hành động |
| `format` | `xlsx` | `xlsx` / `pdf` |

Toàn bộ bản ghi khớp bộ lọc, **mới nhất trước**. Bảy cột: thời gian (UTC, ô ngày thật trong
Excel), người thao tác, SĐT, hành động, đối tượng, IP, mã người dùng.

### 1.4 Tên file và trần dòng

| Báo cáo | Tên file | Trần Excel | Trần PDF |
|---|---|---|---|
| Doanh thu | `bao-cao-doanh-thu_YYYYMMDD-HHmmssZ.xlsx` / `.pdf` | — (tối đa 366 bucket) | — |
| Lấp đầy | `bao-cao-ty-le-lap-day_YYYYMMDD-HHmmssZ.xlsx` / `.pdf` | 10.000 chuyến | 2.000 chuyến |
| Nhật ký | `nhat-ky-kiem-toan_YYYYMMDD-HHmmssZ.xlsx` / `.pdf` | 10.000 bản ghi | 2.000 bản ghi |

Tên file là **ASCII không dấu** (cố ý — `FileContentResult` sẽ mã hoá tên có dấu thành chuỗi lạ),
kèm mốc UTC để hai lần xuất trong cùng ngày không trùng tên. Tên thật nằm trong header
`Content-Disposition`, FE không tự ghép.

## 2. Bảy quyết định thiết kế

### 2.1 Doanh thu: một nguồn số duy nhất

`RevenueExportService` **không tự tính doanh thu**. Nó gọi `IRevenueReportService.GetAsync` —
đúng bản có đệm 60 giây mà endpoint JSON đang dùng — rồi chỉ dàn bucket ra file.

- Chỉ giao dịch `Success`, mốc `paidAt`/`createdAt`, trục Việt Nam, bucket liên tục, trần 366
  ngày, tuyến không tồn tại → 404: tất cả giữ nguyên ở báo cáo gốc.
- Lỗi đầu vào được chuyển nguyên thông báo và `errors` theo trường, không sinh bảng lỗi thứ hai.
- Đăng ký DI phải đặt `IRevenueExportService` **sau** cụm đăng ký báo cáo doanh thu trong
  `Program.cs`; chèn một đăng ký `IRevenueReportService` khác phía sau sẽ âm thầm bỏ lớp đệm
  60 giây (đã ghi chú tại chỗ).

### 2.2 Lấp đầy: công thức và mẫu số

```
tỷ lệ (%) = (vé Paid + Used của chuyến) / Buses.Capacity × 100, làm tròn 1 chữ số
```

- Vé `Cancelled` không tính khách. Chuyến `Cancelled` không xuất hiện; ba trạng thái còn lại đều có.
- Mẫu số là `Buses.Capacity` — sức chứa vận hành — không phải số ghế khai báo trong sơ đồ.
  Chuyến có `Capacity <= 0` bị loại khỏi cả bảng lẫn phần tóm tắt (chia cho 0 vô nghĩa).
- Tỷ lệ **vượt 100%** được giữ nguyên, không kẹp — bán quá sức chứa là bất thường cần lộ ra.
- Chuyến chưa tới giờ khởi hành vẫn có mặt (thường 0%): báo cáo phản ánh lượng vé đã bán tại thời
  điểm xuất. Muốn xem kỳ đã khép lại thì chọn `to` là hôm qua.

### 2.3 Ngưỡng đầy/trống và tỷ lệ trung bình

- Đầy: tỷ lệ **≥ 80%**; trống: **< 50%** — tính trên tỷ lệ **đã làm tròn 1 chữ số**, cùng con số
  hiện trên dòng. Nhờ vậy không có chuyến ghi `80.0` mà không được đếm là đầy.
- Trung bình là **trung bình không trọng số** của tỷ lệ từng chuyến (chuyến 10 ghế nặng bằng
  chuyến 100 ghế), cùng cách màn hình đang tính; không có chuyến → 0.

### 2.4 Lịch Việt Nam, khung giờ nửa khoảng, và trần không bao giờ cắt file

- `from`/`to` của doanh thu và lấp đầy là **ngày lịch Việt Nam**: `fromUtc = from 00:00 − 7h`,
  `toExclusiveUtc = (to + 1) 00:00 − 7h`. Nhờ nửa khoảng, cả hai đầu mút được tính trọn ngày mà
  không sinh mốc `23:59:59.9999999` (mốc đó bỏ sót đuôi một giây).
- `timeSlot` lọc **trong bộ nhớ** sau khi lọc ngày ở CSDL: một ngày VN trải qua hai ngày UTC, lọc
  giờ bằng SQL sẽ phải dịch múi giờ trong câu truy vấn — chậm và khác nhau giữa Npgsql với
  provider InMemory của test.
- Đếm số dòng **trước** khi nạp vé/dựng file. Vượt trần → 400 gắn vào cả `errors.from` lẫn
  `errors.to`, **không bao giờ cắt bớt**: file thiếu dòng mà trông như thành công là báo cáo sai.
  Sau khi nạp còn một chốt đua thứ hai (bản ghi mới chen giữa `COUNT` và `SELECT`) — vẫn vượt thì
  từ chối tiếp, không cắt.

### 2.5 Nhật ký: giữ UTC và trọn ngày

Bộ lọc `from`/`to` và cột thời gian trong file dùng **cùng khung UTC**, để file và bộ lọc không bao
giờ nói hai chuyện khác nhau — tính chất quan trọng nhất của một chứng từ kiểm toán. Đánh đổi đã
biết: chọn ngày theo giờ Việt Nam sẽ lệch tối đa 7 giờ ở hai đầu mút; nhãn cột ghi rõ `(UTC)`.

- `action` gõ sai → **file rỗng** (mã lạ có thể hợp lệ trong tương lai, cùng lối `status` của
  `/routes`); `userId` sai định dạng GUID → **400** (định danh gõ sai là request hỏng, trả file
  rỗng sẽ che mất lỗi).
- Ô thời gian trong Excel gán `DateTime` thật, không gán chuỗi — giữ khả năng sort/lọc theo thời
  gian. Các cột còn lại để text để Excel không diễn giải SĐT thành số.
- Họ tên/SĐT người thao tác nạp bằng một truy vấn riêng, **không** `Include(a => a.User)` — không
  kéo `PasswordHash` về chỉ để lấy hai trường hiển thị.
- Chính thao tác xuất không để lại dòng `AuditLogs` nào (middleware chỉ ghi
  POST/PUT/PATCH/DELETE), nên khối tự mô tả ở đầu file là nơi duy nhất ghi nhận "ai xuất lúc nào".

### 2.6 PDF dùng chung service của Hiếu

Cả ba bản PDF gọi `IReportPdfService.GeneratePdf` (dòng 77 — Trần Trung Hiếu), không tự đụng
QuestPDF. Khối đầu trang thay cho hàng 1–2 của Excel; bảng rỗng vẫn hợp lệ (có đầu cột), nên kỳ
không có dữ liệu vẫn ra **200 + file**, không phải 404. File PDF là bản để đọc/trình ký, không có
auto-filter hay dòng tiêu đề cố định.

### 2.7 Excel tự mô tả, số là số thật

Mọi file Excel theo cùng một khuôn: hàng 1 tiêu đề tài liệu (bold 14), hàng 2 dòng ngữ cảnh
(khoảng thời gian, bộ lọc, tổng hợp, thời điểm xuất, người xuất), hàng 3 trống, **hàng 4 tiêu đề
cột**, dữ liệu từ hàng 5; auto-filter + cố định hàng tiêu đề; độ rộng cột đặt cứng — không gọi
`AdjustToContent()` vì nó đo font và sinh lỗi "missing font" trên Linux.

Ô tiền/số là **số thật** với định dạng `#,##0` (tỷ lệ `0.0`) để cộng/lọc/sort được. Chuỗi hiển thị
ở dòng mô tả và PDF dùng culture `vi-VN` (dấu chấm nhóm hàng nghìn).

## 3. Lỗi

| Tình huống | Mã | Ghi chú |
|---|---|---|
| Không gửi token | 401 | middleware xác thực |
| Doanh thu/lấp đầy: không phải Admin/Quản lý | 403 | policy `ManagerOrAbove` |
| Nhật ký: không phải Admin | 403 | policy `AdminOnly` |
| `to` trước `from` (doanh thu, lấp đầy) | 400 | gắn vào **cả** `errors.from` và `errors.to` |
| `to` trước `from` (nhật ký) | 400 | chỉ `errors.to` — chỉ cần đổi ngày kết thúc |
| Khoảng quá 366 ngày (doanh thu, lấp đầy) | 400 | `errors.from`, `errors.to` |
| `timeSlot` lạ | 400 | `errors.timeSlot`; tên enum bằng số cũng bị từ chối |
| `granularity` lạ | 400 | `errors.granularity`, do báo cáo gốc trả về |
| `routeId` không tồn tại | 404 | `Không tìm thấy tuyến đường.` |
| `format` lạ | 400 | `errors.format` |
| `userId` sai định dạng GUID | 400 | `errors.userId` |
| Vượt trần dòng (lấp đầy, nhật ký) | 400 | `errors.from` + `errors.to`, kèm gợi ý thu hẹp khoảng/bộ lọc |
| Kỳ không có dữ liệu | 200 | file chỉ có tiêu đề (Excel) / bảng rỗng (PDF) — không phải 404 |

Định dạng lỗi theo chuẩn chung: `{ "message": "...", "errors": { "field": ["..."] } }`.

## 4. File

| File | Thay đổi |
|---|---|
| `backend/SmartBus.Api/Services/ReportExportFormat.cs` | **mới** — enum + `ReportExportFormats` (parse, content type, đuôi file, thông báo lỗi dùng chung) |
| `backend/SmartBus.Api/Services/ReportExportFile.cs` | **mới** — `Content` / `ContentType` / `FileName` |
| `backend/SmartBus.Api/Dtos/Reports/RevenueExportRequest.cs` | **mới** |
| `backend/SmartBus.Api/Services/IRevenueExportService.cs` · `RevenueExportService.cs` | **mới** |
| `backend/SmartBus.Api/Controllers/RevenueExportController.cs` | **mới** |
| `backend/SmartBus.Api/Dtos/Reports/FillRateExportRequest.cs` | **mới** |
| `backend/SmartBus.Api/Services/IFillRateExportService.cs` · `FillRateExportService.cs` | **mới** |
| `backend/SmartBus.Api/Controllers/FillRateExportController.cs` | **mới** |
| `backend/SmartBus.Api/Dtos/AuditLogs/ExportAuditLogsRequest.cs` | thêm `Format` |
| `backend/SmartBus.Api/Services/AuditLogExportFile.cs` | thêm `ContentType` |
| `backend/SmartBus.Api/Services/IAuditLogExportService.cs` · `AuditLogExportService.cs` | thêm nhánh PDF + trần 2.000 |
| `backend/SmartBus.Api/Controllers/AuditLogExportController.cs` | dùng `ContentType` của service |
| `backend/SmartBus.Api/Program.cs` | đăng ký DI (file của Hoàng) |
| `backend/SmartBus.Tests/RevenueExportApiTests.cs` | **mới** — 12 test |
| `backend/SmartBus.Tests/FillRateExportApiTests.cs` | **mới** — 24 test |
| `backend/SmartBus.Tests/AuditLogExportTests.cs` | +4 test PDF/định dạng (nâng lên 20) |
| `docs/api-contract.md` | mục ba endpoint xuất + ghi chú `responseType: 'blob'` |
| `docs/49-api-xuat-bao-cao-ra-file.md` | tài liệu này |

Không sửa `RevenueReportService`/`RevenueReportController`, `PeakHoursReport*`,
`ReportPdfService` hay DTO báo cáo gốc. Không có bảng/cột mới → không cần migration. Service xuất
dùng chung của dòng 76 (Kiên) chưa có; ba service ở đây chỉ chia sẻ hai helper nhỏ
`ReportExportFormat`/`ReportExportFile` — khi service dùng chung xuất hiện, hai helper này là điểm
nối tự nhiên.

## 5. Kiểm thử

**56 test** trong ba lớp (12 doanh thu + 24 lấp đầy + 20 nhật ký, trong đó 4 test nhật ký là mới):

- **Doanh thu:** 401/403; mặc định `.xlsx` (magic ZIP + tên file ASCII); `format=pdf` (magic
  `%PDF-`); `format` lạ → 400; kỳ rỗng vẫn ra PDF; nội dung đúng ba cột + tiêu đề + dòng ngữ cảnh;
  doanh thu/số giao dịch là ô số thật; bucket 0 đồng vẫn có mặt; mặc định đúng 30 bucket; lỗi
  `granularity`, `to < from`, tuyến không tồn tại giữ nguyên từ báo cáo gốc.
- **Lấp đầy:** 401/403; định dạng file; `timeSlot` lạ/kể cả số; đảo ngày và quá 366 ngày → 400 hai
  ô; tuyến 404; nội dung cột/thứ tự/kiểu ô; kỳ rỗng; biên ngày Việt Nam (23:59:59 vs 00:00);
  biên khung giờ (05:00 tính, 09:00 không); lọc tuyến; sắp xếp + tie-break theo `Id`; mặc định 30
  ngày; chỉ `Paid`+`Used`, loại chuyến huỷ và xe sức chứa ≤ 0; làm tròn 1 chữ số và giữ >100%;
  ngưỡng 80/50 trên tỷ lệ đã làm tròn (79,95 → 80,0; 49,95 → 50,0); trung bình không trọng số;
  vượt trần Excel 10.001 và PDF 2.001 → 400, không trả file.
- **Nhật ký:** 16 test Excel sẵn có (401/403, mặc định 30 ngày, trọn ngày, `to` chỉ gắn
  `errors.to`, lọc người/hành động, hành động lạ → file rỗng, file rỗng có đầu cột, trần 10.000
  đúng/vượt, nội dung cột + ô `DateTime`, người thao tác không xác định, mới nhất trước, xuất
  không ghi thêm nhật ký) + 4 test mới: PDF đúng định dạng, `format` lạ → 400, kỳ rỗng vẫn ra PDF,
  vượt trần PDF 2.001 → 400.

Toàn bộ suite backend: **1833/1833 xanh**. Chạy `dotnet test --no-build` sau khi build
`-p:Deterministic=false` (Smart App Control trên máy dev chặn một số DLL mới build). Test đua ghế
SQLite đã biết là flaky — đỏ ở bước mở kết nối thì chạy lại, không liên quan nhánh này.

## 6. Bàn giao

- **Frontend (nút xuất — dòng 80, task riêng):** gọi cả ba endpoint với `responseType: 'blob'`.
  Khi đó body lỗi cũng là `Blob`, nên `error.response.data.message` là `undefined` — màn hình phải
  `await blob.text()` rồi `JSON.parse` cho nhánh lỗi. Tên file lấy từ `Content-Disposition`, đừng
  tự ghép; `errors.from`/`errors.to` gắn vào hai ô ngày, `errors.format` vào ô chọn định dạng.
- **Hiếu:** không cần làm gì — dùng lại `IRevenueReportService` và `IReportPdfService`, không đổi
  chữ ký.
- **Kiên:** JSON `GET /reports/fill-rate` (dòng 66) đã vào `main` (`FillRateReportService`) trong
  lúc nhánh này mở, nên giờ có **hai chỗ tự tính** — `FillRateReportService` và `FillRateExportService`.
  Tỷ lệ từng chuyến và ngưỡng 80/50 khớp nhau, nhưng **trung bình khác định nghĩa**: bản xuất lấy
  trung bình cộng tỷ lệ từng chuyến (không trọng số), JSON lấy `totalPassengers / totalSeats` (có
  trọng số). Hai bên chỉ lệch khi sức chứa các chuyến khác nhau — đã ghi rõ trong `api-contract.md`,
  đừng coi là một. Ngoài ra bản xuất **chưa nhận khung `night`** (JSON có): gửi `night` → 400
  `errors.timeSlot`. Việc tiếp theo — chốt một định nghĩa trung bình (JSON có lập luận chặt hơn) rồi
  cho bản xuất gọi lại `IFillRateReportService`, kèm test — cố ý **không gộp vào PR sửa merge** để
  không trộn đổi hành vi với việc giải quyết xung đột.
- **Vàng (test dòng 82):** hợp đồng chi tiết ở mục tương ứng của `docs/api-contract.md`; các test
  hiện có trong ba lớp trên đã ghim phần lớn nội dung — viết thêm thì tránh lặp lại nguyên xi.
- **Dăm:** không có migration.
