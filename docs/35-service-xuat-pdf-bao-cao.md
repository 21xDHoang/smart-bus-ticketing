# Service xuất PDF báo cáo có tiêu đề, logo, ngày xuất (dòng 77, US 21, Sprint 4)

> Task dòng 77 Sprint 4 — backlog giao **Trần Trung Hiếu**. Service **không có endpoint, không
> chạm CSDL, không chạm mạng** — viên gạch chung cho mọi báo cáo xuất PDF của Sprint 4 (doanh thu,
> tỷ lệ lấp đầy, nhật ký). API xuất file (task của Hoàng) gom dữ liệu vào model rồi gọi đúng một
> phương thức này.
>
> Đọc kèm: `docs/31-pdf-ve-email.md` (service PDF vé — cùng QuestPDF, cùng font nhúng, cùng các
> chốt kỹ thuật), `docs/api-contract.md` mục `GET /audit-logs/export` (lối trả file nhị phân về).

## 1. Đã có gì trên nhánh này

| File | Việc |
|---|---|
| `backend/SmartBus.Api/Services/ReportPdfModel.cs` | Record phẳng dữ liệu MỘT báo cáo + `ReportTable` (đầu cột + các dòng) + `ExportedAtText` (UTC+7) |
| `backend/SmartBus.Api/Services/IReportPdfService.cs` | Hợp đồng vẽ: `GeneratePdf(ReportPdfModel)` → `byte[]` |
| `backend/SmartBus.Api/Services/ReportPdfService.cs` | QuestPDF khổ A4: đầu trang (logo + tiêu đề + dòng phụ), dòng "Ngày xuất / Người xuất", bảng số liệu, chân trang "Trang X / Y" |
| `backend/SmartBus.Api/Program.cs` | Một dòng đăng ký DI (cạnh khối xuất nhật ký ra Excel — cùng chủ đề "xuất file") |
| `backend/SmartBus.Tests/ReportPdfServiceTests.cs` | 13 ca — cấu trúc PDF, tính tất định, lỗi dữ liệu ném ngay, ghim giờ UTC+7 |
| `backend/SmartBus.Tests/ReportPdfWiringTests.cs` | 2 ca chạy trên app thật — xoá dòng DI là đỏ |

Không sửa file nào khác: font Roboto nhúng và gói QuestPDF đã có sẵn từ service vé (file của
Hoàng — `SmartBus.Api.csproj` không phải đụng).

## 2. Báo cáo PDF

Khổ **A4** (giấy trình ban giám đốc, in thẳng không phải gấp), khổ dọc. Bố cục từ trên xuống:

1. **Đầu trang** — logo (góc trái, khung 26 × 14 mm, giữ tỉ lệ) + **tiêu đề** in đậm + dòng phụ
   (thường là khoảng thời gian báo cáo).
2. **Dòng tự mô tả** — `Ngày xuất: 09:30 14/10/2026 (UTC+7)` và `Người xuất: …` (có thì in). File
   xuất ra là chứng từ RỜI KHỎI hệ thống nên phải tự nói được xuất lúc nào, ai xuất — cùng tinh
   thần khối tự mô tả của `AuditLogExportService`.
3. **Bảng số liệu** (có thì in) — hàng tiêu đề in đậm trên nền xám nhạt, **lặp lại ở đầu mỗi
   trang**; các cột rộng bằng nhau; dòng lệch số cột thì ném ngay khi gọi.
4. **Chân trang mỗi trang** — ghi chú (có thì in) bên trái, `Trang X / Y` bên phải.

Ba chốt kỹ thuật (lý do đầy đủ ở doc lớp `ReportPdfService`):

1. **Logo nhận dạng chuỗi SVG, không phải ảnh bitmap.** Cùng lý do mã QR trên vé (docs/31 §2
   chốt 1): vector in cỡ nào cũng sắc, không kéo đường giải mã ảnh native (máy dev Windows /
   CI ubuntu chạy giống nhau). Kiểm kiểu nông trước khi render: thiếu thẻ `<svg>` là ném
   `ArgumentException` — chặn sớm kiểu nhầm đường dẫn file hoặc chuỗi Base64 vào ô logo.
2. **Font Roboto nhúng trong assembly** (đã có sẵn từ service vé) — "Báo cáo doanh thu" không được
   ra ô vuông (tofu). Service này **không đăng ký font lần hai**: kiểm tra danh sách font đã đăng
   ký của QuestPDF rồi mới đăng ký, vì service vé có thể đã chạy trước trong cùng tiến trình.
3. **Ngày xuất do bên gọi truyền vào, không tự lấy `DateTime.Now`.** Cùng một model phải ra cùng
   một file — test ghim tính tất định (`GeneratePdf_cung_model_ra_cung_file`). Giờ in theo
   **UTC+7 cố định** (không `TimeZoneInfo` — tên vùng khác nhau giữa Windows và Linux, CI sẽ nổ),
   cùng khuôn `HH:mm dd/MM/yyyy` với giờ khởi hành trên vé.

Giấy phép: QuestPDF **Community** (miễn phí cho tổ chức dưới 1 triệu USD doanh thu/năm — dự án học
tập thuộc diện này) — giống service vé, không có gì mới phải duyệt.

## 3. Cách dùng

### 3.1 Phía API xuất báo cáo (Hoàng — task "API xuất báo cáo doanh thu / lấp đầy / nhật ký ra file")

```csharp
// Dữ liệu đã gom xong từ truy vấn báo cáo — dịch sang chuỗi in sẵn (số tiền theo vi-VN,
// giờ theo UTC+7) rồi nhét vào model. Service không chạm CSDL, không đoán kiểu dữ liệu.
var model = new ReportPdfModel
{
    Title = "Báo cáo doanh thu theo tuyến",
    LogoSvg = logoMarkup,                       // chuỗi SVG; null/trắng = không in logo
    ExportedAt = DateTime.UtcNow,               // ngày xuất — UTC, in ra thành giờ Việt Nam
    Subtitle = $"Từ {from:dd/MM/yyyy} đến {to:dd/MM/yyyy}",
    GeneratedBy = currentUserFullName,
    FooterNote = "Số liệu tổng hợp từ vé đã thanh toán",
    Table = new ReportTable
    {
        Headers = ["Tuyến", "Số vé", "Doanh thu"],
        Rows = rows,                            // mỗi dòng ĐÚNG 3 ô — lệch là ném ngay
    },
};

byte[] pdf = reportPdfService.GeneratePdf(model);   // IReportPdfService, đã có trong DI
```

Trả về trình duyệt thì kèm header `Content-Type: application/pdf` và `Content-Disposition`
(có thể tham khảo lối `GET /audit-logs/export` trong api-contract.md — phần tên file ASCII
không dấu).

### 3.2 Lỗi ném ngay khi gọi

| Lỗi | Khi nào |
|---|---|
| `ArgumentException` — thiếu tiêu đề | `Title` null / rỗng / whitespace |
| `ArgumentException` — logo không phải SVG | `LogoSvg` không chứa thẻ `<svg>` |
| `ArgumentException` — bảng lệch cột | dòng nào của `Table.Rows` không đủ đúng số ô của `Headers` |
| `ArgumentException` — có dòng không đầu cột | `Headers` rỗng mà `Rows` có dòng |

`LogoSvg` trắng/whitespace và các trường phụ (`Subtitle`, `GeneratedBy`, `FooterNote`, `Table`)
đều **có thì in, không có thì bỏ** — báo cáo tối giản không được vỡ.

## 4. Test

`ReportPdfServiceTests.cs` — 13 ca: đầu–đuôi file PDF, cùng model cùng file (tất định), báo cáo
tối giản, logo trắng, bảng 300 dòng phân trang thật, 4 lỗi dữ liệu ném đúng kiểu, 3 ghim giờ
UTC+7 (đổi múi, Kind Unspecified coi như UTC, qua nửa đêm sang ngày hôm sau).

`ReportPdfWiringTests.cs` — 2 ca trên app thật (TestAppFactory): DI resolve được
`IReportPdfService`, và sinh được PDF thật qua cấu hình app (font nhúng phải sống trong tiến
trình test).
