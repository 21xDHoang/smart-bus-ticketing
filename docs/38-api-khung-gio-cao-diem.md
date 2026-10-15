# API phân tích khung giờ cao điểm theo mật độ hành khách (dòng 67, US 20, Sprint 4)

> Task dòng 67 — backlog giao **Trần Trung Hiếu**; bản dựng này do Hiếu thực hiện. Endpoint
> **GET /api/reports/peak-hours** — gộp khách theo giờ khởi hành, đánh dấu giờ cao điểm theo mật
> độ trung bình cả kỳ.
>
> Đọc kèm: `docs/api-contract.md` mục "Khung giờ cao điểm — /reports/peak-hours" (hợp đồng),
> mục "Báo cáo doanh thu — /reports/revenue" (mục chị em US 19 — nhánh
> `feature/19-api-bao-cao-doanh-thu`, chưa merge lúc viết file này).
>
> ⚠️ **Vì sao số 38 mà không phải 35:** các nhánh chưa merge đang giữ `docs/35` (service xuất
> PDF — feature/21) và `docs/37` (báo cáo doanh thu — feature/19); đánh 38 để không đụng số.

## 1. Đã có gì trên nhánh này

| File | Việc |
|---|---|
| `backend/SmartBus.Api/Dtos/Reports/PeakHoursReportRequest.cs` | Tham số query — from/to/routeId, cùng quy tắc khoảng của /reports/revenue |
| `backend/SmartBus.Api/Dtos/Reports/PeakHoursReportResponse.cs` | Hình dạng phản hồi — 24 bucket giờ + peakHours |
| `backend/SmartBus.Api/Services/IPeakHoursReportService.cs` | Hợp đồng service |
| `backend/SmartBus.Api/Services/PeakHoursReportService.cs` | Cài đặt — `GroupIntoHours`/`ToVietnamHour` thuần, test gọi thẳng được |
| `backend/SmartBus.Api/Controllers/PeakHoursReportController.cs` | GET /api/reports/peak-hours, `ManagerOrAbove` |
| `backend/SmartBus.Api/Program.cs` | Một dòng DI (file của Hoàng — nhờ xem qua trong PR) |
| `docs/api-contract.md` | Mục "Khung giờ cao điểm — /reports/peak-hours" |
| `backend/SmartBus.Tests/PeakHoursReportApiTests.cs` | 19 ca tích hợp |

## 2. Năm chốt nghiệp vụ

### 2.1 Nền là `Tickets`, đếm vé Paid/Used của các chuyến khởi hành trong kỳ

Mật độ hành khách = khách **đã trả tiền và lên xe**, không phải số ghế hay số chuyến. Vé
`Cancelled` bỏ (khách không lên xe); vé `Used` vẫn tính — người đó đã đi thật, mật độ vận tải phải
đếm họ. Vé mới sinh ra sau khi tiền về (docs/api-contract.md mục "Phát hành vé sau khi thanh
toán") nên bảng này chính là "hành khách thực tế" của từng chuyến.

### 2.2 Khung giờ = giờ KHỞI HÀNH của chuyến, theo giờ Việt Nam (UTC+7 cố định)

Báo cáo trả lời "giờ nào đông khách" để nhà xe điều tần suất — trục phải là giờ chạy, không phải
giờ bán vé. Chuyến 00:30 giờ VN thuộc giờ 0 của NGÀY ĐÓ, không phải 17 giờ ngày hôm trước theo
UTC. Cố ý không dùng `TimeZoneInfo` (tên vùng khác nhau giữa Windows và Linux — cùng chốt của
`RevenueReportService`).

### 2.3 Mật độ = khách trung bình MỖI CHUYẾN

Hai giờ cùng 100 khách nhưng một giờ có 20 chuyến, giờ kia có 4 — chỗ đông là giờ 4 chuyến. Chia
cho số chuyến khiến "cao điểm" phản ánh **độ chật của xe** chứ không chỉ độ lớn của thời khoá
biểu. Cũng vì thế `hours[].trips` đếm chuyến KHÁC NHAU, không phải tổng lượt chạy.

### 2.4 Cao điểm = mật độ giờ ≥ mật độ trung bình cả kỳ — so trên giá trị ĐÃ LÀM TRÒN 2 chữ số

Ngưỡng là `averagePassengersPerTrip` trả về trong phản hồi — con số hiển thị chính là con số được
so sánh, người đọc (và FE) tự kiểm lại được mà không cần tái hiện phép tính. Hai chặn bảo vệ:

- giờ **không có chuyến** không bao giờ là cao điểm (mật độ 0 chưa đủ để gọi là "giờ cao điểm");
- cả kỳ **không có khách nào** (có chuyến nhưng chưa bán được vé) thì `peakHours` rỗng — ngưỡng
  trung bình bằng 0 mà vẫn so `≥` sẽ đánh dấu nhầm toàn bộ.

Dùng `≥` chứ không phải `>`: kỳ chỉ có một giờ có chuyến thì giờ đó **bằng** trung bình và vẫn là
cao điểm — dữ liệu ít thì báo cáo vẫn có câu trả lời.

### 2.5 Trục 24 giờ CỐ ĐỊNH + tổng kiểm chéo bằng phép kiểm THẬT

Giờ rỗng vẫn hiện diện với số 0 (biểu đồ cột cần đủ 24 cột — cùng lối "trục cố định" của
`RevenueReportResponse`). `totalPassengers`/`totalTrips` là Count gộp trong CSDL; bucket gộp
trong C# từ dòng thô (phép chia giờ VN không dịch được bằng LINQ chung cho cả Npgsql lẫn
InMemory). Đẳng thức "tổng == cộng các bucket" là phép kiểm thật — phép gộp lệch múi giờ làm rơi
dòng là test `Tong_khop_voi_tong_cac_bucket` đỏ ngay.

## 3. Vì sao KHÔNG chạm Entities/Migrations

Báo cáo chỉ ĐỌC hai bảng đã migrate từ Sprint 3 (`Trips` — Sprint 2, `Tickets` — migration
`20261010105248_Sprint3_Payments_Vouchers_Tickets`, PR #144): không cột mới, không bảng mới, đúng
luật "migration là của Dăm".

## 4. Ranh giới với các task cùng story

| Task | Ai | Ranh giới |
|---|---|---|
| API thống kê tỷ lệ lấp đầy theo chuyến/tuyến/khung giờ (dòng 66) | Kiên | Tỷ lệ lấp đầy cần mẫu số **sức chứa** (ghế của xe) — đơn vị phần trăm khác hẳn, mở endpoint riêng (`GET /reports/fill-rate`), đừng đổi ngữ nghĩa `passengers` ở đây |
| API đề xuất tăng/giảm tần suất (dòng 68) | Hoàng | Đọc thẳng `peakHours` — giờ cao điểm là giờ nên tăng chuyến, không dựng lại nền tính |
| Component biểu đồ nhiệt mật độ theo giờ và trạm (dòng 71) | Vàng (FE) | Tiêu thụ `hours[]`; chiều "trạm" cần nguồn dữ liệu riêng (`BoardingStopId` của vé) — chưa có endpoint, hỏi Hiếu khi cần |

## 5. Chưa chốt — để nhóm quyết

- **Ngưỡng so sánh có nên là trung bình của CẢ CÔNG TY hay của từng tuyến khi lọc `routeId`?**
  Hiện tại: luôn so với trung bình của tập chuyến ĐANG LỌC — lọc tuyến nào thì ngưỡng của tuyến
  đó (một tuyến có 2 giờ chạy sẽ luôn có 1 giờ cao điểm). Cách kia (so với trung bình toàn công
  ty) lại trả lời "tuyến này có giờ nào đông hơn mặt bằng chung". Chưa có story nào đòi cách
  thứ hai — giữ cách hiện tại cho tới khi Hoàng làm task đề xuất tần suất (dòng 68) có ý kiến.
- **Vé của chuyến bị huỷ (`Trips.Status = Cancelled`) có tính không?** Hiện tại TÍNH — chuyến
  huỷ thường đi kèm hoàn tiền làm vé thành `Cancelled` (đã bị loại bởi chốt 2.1), vé còn `Paid`
  thì khách vẫn có chứng từ. Chưa gặp trường hợp thực tế nào cần chặn — ghi lại để nhóm chốt khi
  luồng huỷ chuyến (Sprint 4, Kiên/Hoàng) có dữ liệu thật.

## 6. Đối chiếu Definition of Done

- ✅ Build sạch, toàn bộ 1306 test xanh (thêm 19 ca mới) — chạy ngày 15/10/2026
- ✅ Đã thử cả trường hợp lỗi (401/403/400/404, biên múi giờ, vé huỷ, kỳ rỗng)
- ✅ Không hardcode bí mật — báo cáo thuần đọc, không config mới
- ✅ Đã cập nhật api-contract.md; endpoint GET thuần đọc → không khai BangPhuSong
- ✅ Nhánh riêng `feature/20-api-khung-gio-cao-diem`, chờ PR + approve (luật 1 — không tự merge)
