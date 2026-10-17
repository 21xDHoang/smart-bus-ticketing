# API đề xuất tăng/giảm tần suất chạy xe theo dữ liệu

> Task **"API đề xuất tăng/giảm tần suất chạy xe theo dữ liệu"** — US 20 *"Tỷ lệ lấp đầy"*,
> Sprint 4 dòng 68 của bảng phân công (người làm ghi: **Phùng Duy Hoàng**), nhánh
> `feature/68-api-de-xuat-tang-giam-tan-suat`.
>
> 🟢 **Trạng thái:** endpoint + test đã xong trên nhánh. Hợp đồng đã ghi vào
> `docs/api-contract.md` **trước** khi viết code (mục *"Đề xuất tần suất chạy xe —
> `/reports/frequency-proposals`"*). Không có migration — chỉ đọc báo cáo có sẵn.

## 1. Endpoint

| Method | Endpoint | Quyền |
|---|---|---|
| GET | `/api/reports/frequency-proposals` | `ManagerOrAbove` (Admin + Quản lý; vai trò khác → 403) |

Query — **mọi tham số bỏ trống được**, cùng bộ tham số với báo cáo khung giờ cao điểm:

| Tham số | Mặc định | Ý nghĩa |
|---|---|---|
| `from` | 29 ngày trước `to` | Ngày bắt đầu kỳ (lịch Việt Nam), tính cả |
| `to` | hôm nay (giờ VN) | Ngày kết thúc kỳ, tính cả |
| `routeId` | — | Lọc theo tuyến; bỏ trống = toàn công ty |

Phản hồi (rút gọn — ví dụ đầy đủ nằm trong hợp đồng):

```json
{
  "from": "2026-10-14",
  "to": "2026-10-14",
  "periodDays": 1,
  "hasEnoughData": true,
  "totalPassengers": 100,
  "totalTrips": 20,
  "averagePassengersPerTrip": 5.00,
  "increaseHours": [17],
  "decreaseHours": [3, 11, 22],
  "hours": [
    {
      "hour": 11,
      "passengers": 10,
      "trips": 4,
      "averagePassengersPerTrip": 2.50,
      "isPeak": false,
      "currentTripsPerDay": 4.00,
      "currentFrequencyMinutes": 15.00,
      "suggestedTripsInPeriod": 2,
      "suggestedTripsPerDay": 2.00,
      "suggestedFrequencyMinutes": 30.00,
      "changePercent": -50.00,
      "action": "Decrease"
    }
  ]
}
```

`hours` **luôn đủ 24 phần tử**, kể cả giờ không có chuyến (`action: "NoService"`).

## 2. Bốn quyết định thiết kế

### 2.1 Một nguồn số duy nhất — đọc thẳng báo cáo khung giờ cao điểm

`FrequencyProposalService` **không tự truy vấn** `Tickets`/`Trips`. Nó gọi
`IPeakHoursReportService.GetAsync` rồi chỉ quy đổi kết quả:

- Vé `Paid`/`Used` (bỏ `Cancelled`), trục giờ Việt Nam (UTC+7), mặc định 30 ngày, trần 366 ngày,
  kiểm tra tuyến tồn tại — tất cả nằm ở đúng một chỗ là báo cáo gốc.
- Bốn số gốc của mỗi giờ (`passengers`, `trips`, `averagePassengersPerTrip`, `isPeak`) được lặp
  lại **đúng con số** báo cáo gốc trả về; có test đối chiếu cả 24 giờ giữa hai endpoint.
- Lỗi đầu vào (`from > to`, quá 366 ngày, tuyến không tồn tại) được chuyển nguyên từ báo cáo gốc.

Muốn đổi nền số liệu (ví dụ tính thêm vé `Held`) thì sửa `PeakHoursReportService` — endpoint đề
xuất tự động theo, không có định nghĩa thứ hai.

### 2.2 Mật độ mục tiêu là trung bình cả kỳ, không phải tỷ lệ ±% cố định

Số chuyến đề xuất của một giờ:

```
suggested = round(khách của giờ / mật độ trung bình cả kỳ)
```

Mật độ trung bình = `totalPassengers / totalTrips`. Giờ đông hơn trung bình cần thêm chuyến để hạ
tải mỗi xe; giờ thưa cần bớt. Làm tròn **2 chữ số**, `MidpointRounding.AwayFromZero` — cùng quy
tắc báo cáo gốc.

Cố ý **không** dùng "tăng 10% / giảm 20%": tỷ lệ đó không đến từ dữ liệu, và hai báo cáo (gốc +
đề xuất) sẽ không bao giờ khớp nhau nếu mỗi bên tự chọn một tỷ lệ.

### 2.3 Không mâu thuẫn với `isPeak`

Đề xuất bị kẹp về phía đúng của cờ cao điểm:

- Giờ **cao điểm**: chỉ tăng hoặc giữ (`suggested >= trips`).
- Giờ **không cao điểm**: chỉ giảm hoặc giữ (`suggested <= trips`).

Nếu làm tròn rơi đúng bằng hiện tại thì `action: "Keep"`. Nhờ vậy dữ liệu cùng một giờ không thể
vừa "là cao điểm" vừa "đề xuất giảm chuyến".

### 2.4 Không đủ dữ liệu thì không đề xuất

| Tình huống | `action` mỗi giờ | Trường đề xuất |
|---|---|---|
| Giờ không có chuyến | `NoService` | mọi trường đề xuất `null` |
| Kỳ có chuyến nhưng chưa có khách, hoặc mật độ trung bình làm tròn bằng 0 | `NoData` | mọi trường đề xuất `null` |
| Kỳ đủ dữ liệu, giờ có chuyến nhưng 0 khách | `Decrease` | `suggestedTripsInPeriod: 0` |

Dữ liệu mới, seeder rỗng hoặc kỳ chưa bán được vé **không phải bằng chứng để cắt chuyến**. Mật độ
trung bình làm tròn bằng 0 (ví dụ 1 khách / 1000 chuyến) thì phép chia vô nghĩa — chặn bằng
`hasEnoughData: false` thay vì chia cho 0.0033.

Chuyện "không chạy" đã là hiện trạng, nên giờ trống trả `NoService`, **không** phải đề xuất 0
chuyến.

### 2.5 Quy đổi theo số ngày và giãn cách

- `currentTripsPerDay` = `trips / periodDays`, 2 chữ số; `suggestedTripsPerDay` tương tự.
- `currentFrequencyMinutes` / `suggestedFrequencyMinutes` = `60 / số chuyến mỗi ngày`, chỉ khi
  số chuyến/ngày **≥ 1**; dưới 1 chuyến/ngày thì `null` — 60 / 0.5 = 120 phút nằm ngoài giờ, con
  số vô nghĩa.
- `changePercent` có dấu (`-50`, `+200`, `0`), 2 chữ số.

`action` là **chuỗi** (`Increase` / `Decrease` / `Keep` / `NoService` / `NoData`) trong hằng số
`FrequencyActionCodes`, vì dự án không bật `JsonStringEnumConverter` — enum sẽ ra số.

## 3. Lỗi

| Tình huống | Mã | Ghi chú |
|---|---|---|
| Không gửi token | 401 | middleware xác thực |
| Không phải Admin/Quản lý | 403 | policy `ManagerOrAbove` |
| `from` sau `to` | 400 | lỗi gắn vào **cả** `errors.from` và `errors.to`, do báo cáo gốc trả về |
| Khoảng quá 366 ngày | 400 | cùng quy tắc báo cáo gốc |
| `routeId` không tồn tại | 404 | `Không tìm thấy tuyến đường.` |

Định dạng lỗi theo chuẩn chung: `{ "message": "...", "errors": { "field": ["..."] } }`.

## 4. File

| File | Thay đổi |
|---|---|
| `backend/SmartBus.Api/Dtos/Reports/FrequencyProposalRequest.cs` | **mới** |
| `backend/SmartBus.Api/Dtos/Reports/FrequencyProposalResponse.cs` | **mới** — kèm `FrequencyActionCodes` |
| `backend/SmartBus.Api/Services/IFrequencyProposalService.cs` | **mới** |
| `backend/SmartBus.Api/Services/FrequencyProposalService.cs` | **mới** |
| `backend/SmartBus.Api/Controllers/FrequencyProposalController.cs` | **mới** — controller riêng, đúng lệ "mỗi task một controller" |
| `backend/SmartBus.Api/Program.cs` | thêm 1 dòng đăng ký DI (file của Hoàng) |
| `backend/SmartBus.Tests/FrequencyProposalApiTests.cs` | **mới** — 17 test |
| `docs/api-contract.md` | thêm mục endpoint, thay ghi chú bàn giao cũ cho task này |
| `docs/48-api-de-xuat-tang-giam-tan-suat.md` | tài liệu này |

Không sửa `PeakHoursReportService.cs`, `PeakHoursReportController.cs`, DTO báo cáo gốc hay bất kỳ
file nào của người khác. Không có bảng/cột mới → không cần migration.

## 5. Kiểm thử

17 test tích hợp trong `FrequencyProposalApiTests`, phủ: 401/403/200 (cả Admin); CSDL trống → 24
giờ `NoService`; biên giờ Việt Nam (`16:59:59Z` là giờ 23); ví dụ số trong hợp đồng (100 khách /
20 chuyến → mật độ 5) với đủ Increase/Decrease/Keep; kỳ không có khách → `NoData`; mật độ trung
bình làm tròn 0 → `NoData`; giờ 0 khách khi kỳ có dữ liệu → đề xuất 0 chuyến; làm tròn
`AwayFromZero` (3.5 → 4, 1.5 → 2); cửa sổ nhiều ngày quy đổi chuyến/ngày và giãn cách `null` dưới
1 chuyến/ngày; mặc định 30 ngày; đối chiếu cả 24 giờ với `/api/reports/peak-hours`; lọc tuyến;
tuyến không tồn tại → 404; `from > to` và quá 366 ngày → 400.

Toàn bộ suite backend: **1724/1724 xanh** (1707 cũ + 17 mới). Lần chạy đầu có 1 test đua ghế SQLite
đỏ do flaky đã biết — chạy lại xanh, không liên quan nhánh này.

## 6. Bàn giao

- **Frontend (màn thống kê tỷ lệ lấp đầy, dòng 20 Sprint 4):** dùng `increaseHours` /
  `decreaseHours` để tô nổi các khung giờ cần điều chỉnh; `hours` để vẽ bảng chi tiết. Khi
  `hasEnoughData: false`, hiển thị "chưa đủ dữ liệu" thay vì bảng đề xuất — đừng vẽ các con số
  `null` thành 0. `action` là chuỗi để hiển thị trực tiếp.
- **Hiếu:** không cần làm gì; endpoint dùng lại `IPeakHoursReportService`, không sửa chữ ký.
- **Dăm:** không có migration.
