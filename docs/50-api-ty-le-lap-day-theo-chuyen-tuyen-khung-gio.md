# API thống kê tỷ lệ lấp đầy theo chuyến, tuyến, khung giờ

> Task **"API thống kê tỷ lệ lấp đầy theo chuyến, tuyến, khung giờ"** — US 20 *"Tỷ lệ lấp đầy"*,
> Sprint 4 dòng 66 của bảng phân công (**Nguyễn Duy Kiên**, 5 điểm), nhánh
> `feature/66-api-ty-le-lap-day-theo-chuyen-tuyen-khung-gio`.
>
> 🟢 **Trạng thái:** endpoint + test đã xong trên nhánh. Hợp đồng đã ghi vào
> `docs/api-contract.md` **trước** khi viết code (mục *"Tỷ lệ lấp đầy — `/reports/fill-rate`"*).
> Không có migration — chỉ đọc các bảng `Trips`, `Tickets`, `Routes`, `Buses` sẵn có.

## 1. Endpoint

| Method | Endpoint | Quyền |
|---|---|---|
| GET | `/api/reports/fill-rate` | `ManagerOrAbove` (Admin + Quản lý; vai trò khác → 403) |

Query — **mọi tham số bỏ trống được**:

| Tham số | Mặc định | Ý nghĩa |
|---|---|---|
| `from` | 29 ngày trước `to` | Ngày bắt đầu (lịch Việt Nam), tính cả |
| `to` | hôm nay (giờ VN) | Ngày kết thúc (lịch Việt Nam), tính cả |
| `routeId` | — | Chỉ tính chuyến của tuyến này; không tồn tại → 404 |
| `timeSlot` | — | `early` / `morning` / `afternoon` / `evening` / `night`; giá trị lạ → 400 `errors.timeSlot` |

Một lượt gọi trả **ba cách nhìn trên cùng tập chuyến** + dải tóm tắt:

- `trips[]` — từng chuyến, kèm sức chứa, số khách, tỷ lệ lấp đầy;
- `routes[]` — gộp theo tuyến;
- `timeSlots[]` — gộp theo khung giờ khởi hành, **luôn đủ năm dòng**;
- `summary` — tổng chuyến / khách / ghế, trung bình có trọng số, số chuyến đầy/trống.

Màn hình "Thống kê tỷ lệ lấp đầy theo chuyến" (`frontend/src/pages/FillRateReportPage.tsx`) đang
chạy dữ liệu giả trong `frontend/src/api/fillRateReportApi.ts`; endpoint này là nguồn thật để nối
vào. Tên trường giữ đúng các trường interface `TripFillRate` / `FillRateReport` mà FE đã viết, và
bổ sung ba trường tổng hợp (`routes[]`, `timeSlots[]`, `summary`) để màn hình không phải tự cộng.

## 2. Sáu quyết định thiết kế

### 2.1 Nền là bảng `Trips` — chuyến 0 khách vẫn phải hiện

Báo cáo trả lời "chuyến nào bán ế để điều chỉnh quy mô xe", nên tập nền là **mọi chuyến khởi hành
trong kỳ**, không phải các chuyến có vé. Bỏ chuyến 0 khách đi thì đúng nhóm cần điều chỉnh biến
mất khỏi báo cáo. Hệ quả: `trips[]` có thể dài đúng bằng số chuyến trong kỳ (tối đa 366 ngày), và
`summary.totalTrips` đếm cả chuyến rỗng.

Ngày theo **lịch Việt Nam (UTC+7 cố định)**, nửa khoảng `[00:00 VN from, 00:00 VN ngày sau to)`;
trần 366 ngày, mặc định 30 ngày. Các hằng số và phép kiểm khoảng ngày **chép lại** từ
`PeakHoursReportService` (file của Trần Trung Hiếu — quy ước E1 không sửa file người khác), cùng
lý do đã ghi ở `RevenueBreakdownService`.

### 2.2 Hành khách = vé `Paid` + `Used`, bỏ `Cancelled`

Đúng định nghĩa của báo cáo khung giờ cao điểm: người đã trả tiền và người đã soát vé đều đã trên
xe; người huỷ/hoàn tiền thì không. Để hai báo cáo không trôi khỏi nhau, bộ test có một ca đối
chiếu trực tiếp: cùng dữ liệu, cùng khoảng ngày,
`summary.totalPassengers` của báo cáo này phải bằng `totalPassengers` của
`GET /reports/peak-hours`.

### 2.3 Mẫu số là `Buses.Capacity` — không phải số ghế đã bán hay số chuyến

Tỷ lệ lấp đầy = `passengers / capacity × 100`. Sức chứa lấy từ xe được phân cho chuyến
(`Trips.BusId → Buses.Capacity`) — đó là con số điều xe quan tâm khi quyết định đổi xe to/nhỏ.
Tỷ lệ **không chặn ở 100**: bán vượt sức chứa là dữ liệu phải lộ ra, không bị báo cáo che mất.
Xe có `Capacity ≤ 0` (dữ liệu lỗi) trả 0 thay vì chia cho 0.

Làm tròn **1 chữ số thập phân, `MidpointRounding.AwayFromZero`** — cùng lối `Math.Round` của hai
báo cáo trước, tránh kết quả khác nhau giữa Npgsql và InMemory ở mốc .5.

### 2.4 Trung bình có trọng số, không phải trung bình cộng

`summary.averageFillRatePct` = `totalPassengers / totalSeats × 100`. Một chuyến 4/40 (10%) và một
chuyến 36/60 (60%) không cùng trọng lượng khi nhìn cả đội xe — trung bình cộng cho 35%, trong khi
con số đúng là 40/100 = 40%. Cách tính này khớp dòng ghi chú trên thẻ của màn hình: *"Trung bình
khách trên tổng sức chứa ghế"*. Từng dòng `routes[]` / `timeSlots[]` dùng đúng công thức đó trên
tổng của nhóm.

### 2.5 Hai ngưỡng đầy/trống, so trên giá trị đã làm tròn

- **đầy** (`fullTrips`): `fillRatePct ≥ 80`;
- **trống** (`emptyTrips`): `fillRatePct < 50`;
- khoảng 50–79 là bình thường, không rơi vào ô nào.

Hai ngưỡng cố ý không đối xứng: "đầy" là mốc cần tăng xe, "trống" là mốc cần giảm. So trên giá trị
**đã làm tròn** để con số hiển thị chính là con số được phân loại — người đọc tự kiểm lại được
(cùng chốt 4 của báo cáo khung giờ cao điểm).

### 2.6 Năm khung giờ phủ trọn 24 giờ và cộng khớp

Bốn khung đầu (`early` 5–9, `morning` 9–13, `afternoon` 13–17, `evening` 17–22) trùng đúng lựa
chọn mà FE đang dùng. Khung thứ năm `night` (22–5, **vắt qua nửa đêm**) được bổ sung để mọi chuyến
thuộc đúng một khung — nhờ vậy cộng `timeSlots[]` luôn bằng `summary`, không có chuyến nào rơi
khỏi báo cáo. `timeSlots[]` luôn đủ năm dòng theo thứ tự cố định, khung rỗng vẫn hiện với số 0
(biểu đồ cột cần đủ cột, cùng lối trục 24 giờ của báo cáo khung giờ cao điểm).

Cận trên KHÔNG tính: chuyến 08:59 là `early`, 09:00 đã sang `morning`; 17:00 là `evening`, 22:00 là
`night`, 04:59 vẫn `night`, 05:00 là `early`. Khung giờ là **giờ khởi hành**, không phải giờ bán
vé.

## 3. Thành phần

| Lớp | File |
|---|---|
| Controller | `Controllers/FillRateReportController.cs` |
| Service | `Services/IFillRateReportService.cs`, `Services/FillRateReportService.cs` |
| DTO | `Dtos/Reports/FillRateReportRequest.cs`, `FillRateReportResponse.cs`, `FillRateTimeSlot.cs` |
| Đăng ký DI | `Program.cs` — một dòng `AddScoped<IFillRateReportService, FillRateReportService>()` (file của Hoàng, nhờ xem qua trong PR) |
| Test | `SmartBus.Tests/FillRateReportApiTests.cs` — 21 ca |

Không sửa file của người khác: controller mới thay vì thêm action vào `PeakHoursReportController`
(của Hiếu), service mới thay vì sửa `PeakHoursReportService`, DTO mới thay vì đổi hình dạng
`PeakHoursReportResponse`. Không có migration và không đụng `frontend/` (màn hình của Hạnh vẫn
chạy mock — nối API là task riêng của frontend).

## 4. Kiểm thử

`FillRateReportApiTests` ghim:

- 401 khi không token, 403 với Passenger, 200 với Manager;
- tỷ lệ theo sức chứa; vé `Cancelled` bị loại; `Used` vẫn tính;
- chuyến 0 khách vẫn hiện với 0%; sắp theo giờ khởi hành;
- không chặn ở 100 (44 khách / 40 ghế = 110%); `capacity` 0 trả 0;
- trung bình có trọng số (40/40 + 0/60 = 40%, không phải 50%);
- ngưỡng đầy/trống đúng tại 80% / 50% / 49%;
- gộp tuyến sắp theo mã (Ordinal), cộng khớp tóm tắt;
- năm khung giờ đủ dòng, đúng biên 08:59/09:00/17:00/22:00/04:59, cộng khớp tóm tắt;
- lọc `timeSlot` (không phân biệt hoa/thường) vẫn trả đủ năm dòng;
- lọc `routeId`; tuyến không tồn tại → 404;
- biên ngày giờ Việt Nam (16:59:59Z ngoài, 17:00:00Z trong);
- mặc định 30 ngày; `to` trước `from` → 400 ở cả hai ô; quá 366 ngày → 400; kỳ rỗng → tóm tắt 0,
  `trips`/`routes` rỗng, `timeSlots` vẫn đủ năm dòng 0;
- tổng khách khớp `GET /reports/peak-hours` cùng khoảng ngày.

## 5. Giới hạn đã biết

- Nhóm theo **xe của chuyến** (`Trips.BusId`), không theo xe chở khách lúc soát vé — dự án không
  lưu liên kết vé ↔ xe thực chạy, và `Trips.BusId` mới là thứ điều xe gán.
- Sức chứa lấy tại **thời điểm hỏi**: đổi `Buses.Capacity` thì báo cáo cũ hiển thị theo sức chứa
  mới (báo cáo không lưu ảnh chụp danh mục). Cùng giới hạn với tên tuyến/biển số.
- **Chuyến đã huỷ (`Trips.Status = Cancelled`) vẫn tính** nếu có trong kỳ — cùng chốt đang dùng
  của báo cáo khung giờ cao điểm (docs/38 §5), chờ nhóm chốt khi luồng huỷ chuyến có dữ liệu thật.
  Vé của chuyến huỷ thường đã thành `Cancelled` nên không vào tử số.
- Không đệm: số liệu tươi ngay sau khi có vé mới; đây là báo cáo vận hành, không phải báo cáo
  tổng hợp nặng như doanh thu theo thời gian.
