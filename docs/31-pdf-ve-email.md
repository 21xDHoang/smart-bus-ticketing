# Service sinh PDF vé + gửi kèm qua email (dòng 42, US 4, Sprint 3)

> Task dòng 42 — backlog giao **Phùng Duy Hoàng**. Service **không có endpoint, không chạm CSDL**;
> chỉ chạm mạng ở đúng một chỗ: phiên SMTP lúc gửi thư.
>
> Đọc kèm: `docs/30-service-ma-qr-ky-so.md` §2 (mã QR in lên vé), `docs/28-csdl-ve-dien-tu.md` §6
> (vì sao in mã **đã lưu**), `docs/29-ban-giao-api-kiem-tra-hieu-luc-qr.md` (endpoint soát vé —
> nơi mã này được kiểm).

## 1. Đã có gì trên nhánh này

| File | Việc |
|---|---|
| `backend/SmartBus.Api/Assets/Roboto-Regular.ttf`, `Roboto-Bold.ttf`, `Roboto-LICENSE.txt` | Font nhúng (Apache-2.0) — font Lato mặc định của QuestPDF thiếu dấu tiếng Việt |
| `backend/SmartBus.Api/Services/TicketPdfModel.cs` | Record phẳng dữ liệu MỘT tấm vé + `DepartureText` (UTC+7) + `PriceText` (vi-VN) |
| `backend/SmartBus.Api/Services/ITicketPdfService.cs` | Hợp đồng vẽ: `GeneratePdf(TicketPdfModel)` → `byte[]` |
| `backend/SmartBus.Api/Services/TicketPdfService.cs` | QuestPDF khổ A5; `BuildQrSvg` public static (QR vẽ bằng SVG tự dệt từ ma trận ZXing) |
| `backend/SmartBus.Api/Services/EmailOptions.cs` | Section `Email` + `MissingPiece()` |
| `backend/SmartBus.Api/Services/EmailMessage.cs`, `IEmailSender.cs` | Thư (kèm `EmailAttachment`) + hợp đồng gửi |
| `backend/SmartBus.Api/Services/SmtpEmailSender.cs` | MailKit — fail-fast lúc dựng service; `BuildMessage` public cho test |
| `backend/SmartBus.Api/Services/ITicketEmailService.cs`, `TicketEmailService.cs` | Ghép hai mảnh: vẽ PDF → soạn thư → gửi (một lời gọi cho bên phát hành) |
| `backend/SmartBus.Api/Program.cs` | Bốn dòng đăng ký DI (sau khối `TicketQr`) |
| `backend/SmartBus.Api/appsettings.Development.json.example` | Mục `Email` — khuôn điền cấu hình |
| `backend/SmartBus.Tests/TicketPdfServiceTests.cs` | 16 ca — QR khứ hồi qua ZXing, vùng lặng, cấu trúc PDF, ghim giờ/giá |
| `backend/SmartBus.Tests/SmtpEmailSenderTests.cs` | 6 ca — soạn MIME, tệp đính kèm, luật 2 |
| `backend/SmartBus.Tests/TicketEmailServiceTests.cs` | 7 ca — ghép PDF + thư, thoát HTML, thứ tự lỗi |
| `backend/SmartBus.Tests/TicketPdfEmailWiringTests.cs` | 3 ca chạy trên app thật — xoá dòng DI là đỏ |

## 2. Tấm vé PDF

Khổ **A5** (in A4 gấp đôi vẫn vừa): tiêu đề "VÉ ĐIỆN TỬ" + tên tuyến; các dòng *Hành khách ·
Khởi hành · Ghế · [Biển số xe] · [Điểm lên] · [Điểm xuống] · Giá vé*; QR 48 mm giữa vé; chân vé in
`Mã vé: {Guid}` cỡ nhỏ cho người hỗ trợ tra cứu. Dòng trong ngoặc vuông **có thì in, không có thì
bỏ** — vé thiếu biển số không được vỡ.

Ba chốt kỹ thuật (lý do đầy đủ ở doc lớp `TicketPdfService`):

1. **QR vẽ bằng SVG tự dệt, không qua ảnh bitmap.** Mỗi ô đen một lệnh `M x y h1v1h-1z` trong một
   thẻ `<path>`; QuestPDF nhúng bằng `.Svg()`. Không kéo đường encode ảnh native (máy dev Windows /
   CI ubuntu chạy giống nhau), in cỡ nào cũng sắc cạnh, và **test đọc ngược toạ độ rồi giải mã lại**
   — chứng minh "máy quét đọc vé của mình ra đúng mã", không chỉ "file mở được".
2. **Font Roboto nhúng trong assembly** (`EmbeddedResource` ở csproj) — "Vé điện tử" không được ra
   ô vuông (tofu) hay rơi xuống font hệ thống (máy dev có, CI ubuntu không).
3. **QR in từ `model.Code` đã lưu — KHÔNG sinh lại** (docs/30 §3.1): mỗi lượt `GenerateCode` ra mã
   khác (nonce ngẫu nhiên), in mã mới là vé trong hộp thư không khớp mã đã phát hành.

Thông số QR: vùng lặng **4 module** (chuẩn — cắt bớt là tự làm hỏng tỉ lệ quét), sửa lỗi mức **Q**
(~25% — vé bị gấp/hở góc nhỏ vẫn đọc được), mã hoá UTF-8. Giờ in theo **UTC+7 cố định** (không
`TimeZoneInfo` — tên vùng khác nhau giữa Windows và Linux, CI sẽ nổ); giá theo **vi-VN** —
`"350.000 ₫"`.

Giấy phép: QuestPDF **Community** (miễn phí cho tổ chức dưới 1 triệu USD doanh thu/năm — dự án học
tập thuộc diện này; đổi phải là quyết định có ý thức, không phải sửa cho hết cảnh báo). Roboto
**Apache-2.0** — bản license nằm cạnh font trong `Assets/`.

## 3. Cách dùng

### 3.1 Phía phát hành vé (Hiếu — dòng 40, sau khi tiền về, vé đã có `Code`)

```csharp
var model = new TicketPdfModel
{
    TicketId = ticket.Id,
    Code = ticket.Code,                        // mã ĐÃ lưu — docs/30 §3.1
    PassengerName = user.FullName,
    RouteName = route.Name,
    DepartureTime = trip.DepartureTime,        // PHẢI là UTC (timestamptz)
    SeatNumber = seat.SeatNumber,
    Price = ticket.Price,                      // snapshot trên vé, không đọc lại từ Trip
    LicensePlate = bus.LicensePlate,           // có gì truyền nấy — null thì vé tự bỏ dòng
    BoardingStopName = boardingStop?.Name,
    AlightingStopName = alightingStop?.Name,
};

try
{
    await ticketEmailService.SendTicketAsync(model, user.Email!, user.FullName);
}
catch (Exception ex)
{
    // Vé đã phát hành xong (đã ghi CSDL, đã có mã QR) — hộp thư chết/mạng đứt KHÔNG được làm
    // hỏng việc phát hành. Nuốt lỗi ở đây là quyết định của bên gọi, kèm log; service không nuốt.
    logger.LogError(ex, "Gửi vé điện tử thất bại, vé {TicketId}", ticket.Id);
}
```

Hai ghim hành vi đã có test:

- **PDF sinh TRƯỚC khi mở phiên SMTP** — PDF hỏng thì ném ngay, không bao giờ gửi thư thiếu đính kèm.
- Địa chỉ người nhận rỗng (`"   "`) ném **trước** khi vẽ PDF — không tốn công vô ích.

Thư gửi đi: tiêu đề `Vé điện tử — {RouteName}`; tệp đính kèm `ve-dien-tu-{8 ký tự Guid}.pdf` (tên
**cố ý không dấu** — tên có dấu bị một số ứng dụng thư/hệ điều hành cũ hiển thị sai); thân **cả
HTML lẫn chữ thuần** (thư giao dịch — máy lọc thư rác đọc bản chữ thuần). Mọi giá trị người dùng
nhập (tên hành khách, tên tuyến…) đi qua `WebUtility.HtmlEncode` trước khi vào HTML — tên hành
khách là chuỗi tự do, dán thẳng là mở đường chèn thẻ vào email gửi cho người khác.

### 3.2 Cấu hình (luật 2)

| Môi trường | Đặt ở đâu |
|---|---|
| Máy dev | `dotnet user-secrets set "Email:Password" "<mật khẩu ứng dụng>"` (và Host/UserName/FromAddress) |
| Deploy | Biến môi trường `Email__Host`, `Email__Port`, `Email__UserName`, `Email__Password`, `Email__FromAddress` |
| Khuôn | Mục `Email` trong `appsettings.Development.json.example` |

- **Gmail:** bật xác thực 2 bước → tạo "mật khẩu ứng dụng" 16 ký tự; **KHÔNG dùng mật khẩu đăng
  nhập thường** (Google chặn). Port 587 + `UseStartTls=true` (mặc định), hoặc 465 +
  `UseStartTls=false`.
- **Không có đường gửi trần**: cả hai chế độ đều mã hoá — email chứa vé là chứng từ, không được đi
  qua kênh không mã hoá.
- Thiếu mảnh nào (kể cả `Port` ngoài 1–65535) → service ném **ngay lúc dựng**, nêu đúng tên mảnh
  (`Email:Host`, `Email:Password`…). Lỗi cấu hình nổ lúc khởi động, không đợi tới lúc hành khách bấm
  mua vé.

### 3.3 Component tải vé PDF (dòng 46 — Thịnh)

Máy chủ chưa có endpoint trả file PDF; khi làm, dùng lại `ITicketPdfService.GeneratePdf` với đúng
`TicketPdfModel` — **đừng dựng đường vẽ thứ hai** (hai đường vẽ là hai tấm vé lệch nhau). Hình dạng
endpoint (route/response) phải vào `api-contract.md` trước khi code (luật 5).

## 4. Test

```
dotnet test backend/SmartBus.Tests/SmartBus.Tests.csproj --filter "FullyQualifiedName~TicketPdf|FullyQualifiedName~SmtpEmail|FullyQualifiedName~TicketEmail"
```

32 ca, ghim những thứ dễ hỏng thầm lặng: **giải mã lại QR bằng ZXing** và so với mã đầu vào (vé in
ra phải quét được), vùng lặng 4 module không có ô đen lọt vào, kích thước ma trận hợp lệ, đầu–đuôi
file `%PDF-`…`%%EOF`, cùng đầu vào ra cùng chuỗi SVG, giờ Việt Nam (kể cả Kind `Unspecified` coi như
UTC và ca qua nửa đêm sang ngày hôm sau), giá kiểu vi-VN, tệp đính kèm khứ hồi **từng byte** qua
vòng ghi–đọc MIME, tiêu đề tiếng Việt sống sót vòng ghi–đọc, mã QR không bị sinh lại ở tầng ghép
(`Assert.Same`), PDF hỏng thì chưa gửi gì, thân HTML không còn thẻ sống, thiếu cấu hình ném đúng
tên mảnh, và 3 ca chạy trên app thật (xoá dòng DI trong `Program.cs` là đỏ ngay).

> Máy Windows bật **Smart App Control** có thể chặn DLL trong `bin` (`FileLoadException
> 0x800711C7`) — không phải test đỏ; build lại là qua, và **CI ubuntu không dính**.

## 5. Việc còn lại — không thuộc service này

| Việc | Ai |
|---|---|
| Nối `ITicketEmailService.SendTicketAsync` vào API phát hành vé (dòng 40) — kèm `try/catch` như §3.1 | Hiếu |
| Endpoint trả file PDF cho component tải vé (dòng 46) — dùng lại `ITicketPdfService`, chốt hình dạng vào `api-contract.md` trước | Hiếu + Thịnh |
| Đặt cấu hình `Email__*` trên môi trường deploy (Render) khi có hộp thư thật | Hoàng |
| `docs/29` §5 ghi "Chưa có file nào trong `Services/`" — đã lỗi thời từ docs/30, nay càng lỗi thời; tài liệu của Dăm nên để Dăm cập nhật | Dăm (nếu muốn) |
