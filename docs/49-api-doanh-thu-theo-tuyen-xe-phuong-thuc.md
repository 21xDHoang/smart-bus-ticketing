# API doanh thu theo tuyến, theo xe, theo phương thức thanh toán

> Task **"API doanh thu theo tuyến, theo xe, theo phương thức thanh toán"** — US 19 *"Doanh thu"*,
> Sprint 4 dòng 55 của bảng phân công (**Nguyễn Duy Kiên**), nhánh
> `feature/55-api-doanh-thu-theo-tuyen-xe-phuong-thuc`.
>
> 🟢 **Trạng thái:** endpoint + test đã xong trên nhánh. Hợp đồng đã ghi vào
> `docs/api-contract.md` **trước** khi viết code (mục *"Báo cáo doanh thu — `/reports/revenue`"*,
> ba tiểu mục `by-route`, `by-bus`, `by-payment-method`). Không có migration — chỉ đọc các bảng
> `Payments`, `Trips`, `Routes`, `Buses` sẵn có.

## 1. Endpoint

| Method | Endpoint | Quyền |
|---|---|---|
| GET | `/api/reports/revenue/by-route` | `ManagerOrAbove` (Admin + Quản lý; vai trò khác → 403) |
| GET | `/api/reports/revenue/by-bus` | `ManagerOrAbove` |
| GET | `/api/reports/revenue/by-payment-method` | `ManagerOrAbove` |

Query — **cả hai tham số bỏ trống được**, cùng quy tắc với `GET /reports/revenue`:

| Tham số | Mặc định | Ý nghĩa |
|---|---|---|
| `from` | 29 ngày trước `to` | Ngày bắt đầu (lịch Việt Nam), tính cả |
| `to` | hôm nay (giờ VN) | Ngày kết thúc (lịch Việt Nam), tính cả |

Cố ý **không có** `granularity` (kết quả là một dòng mỗi nhóm, không có trục thời gian) và
**không có** `routeId` (báo cáo theo tuyến đã trả mọi tuyến). Muốn doanh thu của một tuyến thì
dùng `GET /reports/revenue?routeId=…`.

## 2. Năm quyết định thiết kế

### 2.1 Cùng nền số liệu với báo cáo thời gian, không dựng nền thứ hai

Ba báo cáo đọc đúng bảng `Payments` với đúng bộ lọc của `RevenueReportService`:

- chỉ `PaymentStatus.Success` — doanh thu là tiền **đã thu thực**;
- mốc thời gian `PaidAt`, thiếu thì `CreatedAt` (dữ liệu cũ/lỗi không được biến mất khỏi báo cáo);
- nửa khoảng `[00:00 VN from, 00:00 VN ngày sau to)`, giờ Việt Nam **UTC+7 cố định**;
- mặc định 30 ngày, trần 366 ngày, `to` trước `from` → 400 gắn ở cả `errors.from` lẫn `errors.to`.

Các hằng số và phép kiểm khoảng ngày **chép lại** từ `RevenueReportService` (file của Trần Trung
Hiếu — quy ước E1 không sửa file người khác). Để hai bên không trôi khỏi nhau, bộ test có một ca
đối chiếu trực tiếp: cùng dữ liệu, cùng khoảng ngày, `totalRevenue`/`totalPayments` của báo cáo
phân rã phải bằng con số của `GET /reports/revenue`.

### 2.2 Tổng bằng truy vấn gộp riêng, không cộng từ dòng trả về

`totalRevenue`/`totalPayments` lấy từ một truy vấn `SUM`/`COUNT` trên `Payments`, còn các dòng
lấy từ truy vấn `GROUP BY`. Nhờ vậy đẳng thức "tổng == cộng các dòng" là **phép kiểm thật**: một
dòng bị rơi (ví dụ chuyến mồ côi) làm test đỏ ngay thay vì tự che mất — cùng lý do chốt 3 của
`RevenueReportService`.

### 2.3 Nhóm trong CSDL, lấy tên ở bước thứ hai — không JOIN navigation

- Nhóm theo `Payments.TripId` (báo cáo tuyến/xe) hoặc `Payments.MethodCode` (báo cáo phương thức)
  ngay trong CSDL.
- Báo cáo tuyến/xe nạp `Trips` theo danh sách `TripId` để biết `RouteId`/`BusId`, rồi nạp
  `Routes`/`Buses` theo danh sách khoá để lấy tên/biển số.

Không JOIN qua navigation (`p.Trip.RouteId`) vì navigation null ở dòng seed mồ côi hành xử khác
nhau giữa Npgsql và InMemory — cùng lối `BuildQuery` của báo cáo gốc và
`AuditLogExportService.LoadActorsAsync`.

### 2.4 Chỉ trả nhóm CÓ phát sinh giao dịch `Success`

Tuyến/xe/phương thức không bán được vé trong kỳ **không** hiện thành dòng 0. Phản hồi vì thế
không phình theo số tuyến/xe của toàn công ty, và biểu đồ "top doanh thu" không bị đẩy xuống bởi
hàng trăm dòng rỗng. Màn hình nào cần danh sách đầy đủ thì đối chiếu với API danh mục ở FE.

Thứ tự dòng cố định: `revenue` giảm dần; hoà thì `routeCode`/`licensePlate`/`methodCode` tăng dần
(Ordinal). Hai lượt gọi cùng dữ liệu trả cùng thứ tự.

### 2.5 Không đệm

`GET /reports/revenue` có đệm 60 giây (task của Dăm). Ba endpoint này **không** đệm: chúng là
lượt xem có chủ đích, và đệm riêng sẽ khiến tổng của chúng lệch bản thời gian mà không có lợi ích
rõ ràng. Hệ quả đã ghi trong hợp đồng: so tổng ngay sau khi có giao dịch mới thì bản thời gian có
thể chậm tới 60 giây, còn ba báo cáo này luôn tươi.

## 3. Thành phần

| Lớp | File |
|---|---|
| Controller | `Controllers/RevenueBreakdownController.cs` |
| Service | `Services/IRevenueBreakdownService.cs`, `Services/RevenueBreakdownService.cs` |
| DTO | `Dtos/Reports/RevenueBreakdownRequest.cs`, `RevenueBreakdownResponse.cs`, `RevenueByRouteResponse.cs`, `RevenueByBusResponse.cs`, `RevenueByPaymentMethodResponse.cs` |
| Đăng ký DI | `Program.cs` — một dòng `AddScoped<IRevenueBreakdownService, RevenueBreakdownService>()` (file của Hoàng, nhờ xem qua trong PR) |
| Test | `SmartBus.Tests/RevenueBreakdownApiTests.cs` — 15 ca |

Không sửa file của người khác: controller mới thay vì thêm action vào `RevenueReportController`
(của Hiếu), service mới thay vì sửa `RevenueReportService`, DTO mới thay vì đổi hình dạng
`RevenueReportResponse`.

## 4. Kiểm thử

`RevenueBreakdownApiTests` ghim:

- 401 khi không token, 403 với Passenger, 200 với Manager — cả ba endpoint;
- gộp đúng theo tuyến (nhiều chuyến cùng tuyến), theo xe (một xe chạy nhiều tuyến), theo
  `MethodCode`;
- chỉ đếm `Success`; `Success` thiếu `PaidAt` vẫn tính theo `CreatedAt`;
- nhóm không phát sinh không hiện; thứ tự doanh thu giảm dần + hoà theo mã;
- biên ngày giờ Việt Nam (16:59:59Z ngoài, 17:00:00Z trong);
- mặc định 30 ngày; `to` trước `from` → 400 ở cả hai ô; quá 366 ngày → 400; kỳ rỗng → mảng rỗng;
- "tổng == cộng các dòng" cho cả ba báo cáo;
- tổng khớp `GET /reports/revenue` cùng khoảng ngày.

## 5. Giới hạn đã biết

- Nhóm theo **xe của chuyến** (`Trips.BusId`), không theo xe chở khách lúc soát vé — dự án không
  lưu liên kết vé ↔ xe thực chạy, và `Trips.BusId` mới là thứ điều xe gán.
- Tên tuyến/xe lấy tại **thời điểm hỏi**: đổi tên tuyến thì báo cáo cũ hiển thị tên mới (báo cáo
  không lưu ảnh chụp danh mục).
- Tổng doanh thu không trừ hoàn tiền. Story 19 hỏi tiền bán vé; hoàn tiền là luồng riêng (US 8).
