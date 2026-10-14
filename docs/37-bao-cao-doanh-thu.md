# API báo cáo doanh thu theo ngày / tuần / tháng (US 19, Sprint 4)

> Task Sprint 4 — backlog giao **Trần Trung Hiếu**: *"API báo cáo doanh thu theo ngày / tuần /
> tháng"*. Nửa "theo thời gian" của story 19: tổng doanh thu + dãy bucket liên tục cho thẻ số liệu
> và biểu đồ của dashboard. **Theo tuyến / xe / phương thức** (Kiên), **so sánh giữa các kỳ**
> (Hoàng) và **xuất Excel/PDF** là các task khác cùng story, mở rộng trên cùng bề mặt.
>
> Đọc kèm: `docs/api-contract.md` mục "Báo cáo doanh thu — /reports/revenue" (hợp đồng).

## 1. Đã có gì trên nhánh này

| File | Việc |
|---|---|
| `backend/SmartBus.Api/Dtos/Reports/RevenueReportRequest.cs` | Tham số query — `granularity`, `from`, `to`, `routeId`, mọi thứ bỏ trống được |
| `backend/SmartBus.Api/Dtos/Reports/RevenueGranularity.cs` | Enum `Day/Week/Month` + `TryParse` (khớp tên, bỏ qua hoa/thường, trống = day) |
| `backend/SmartBus.Api/Dtos/Reports/RevenueReportResponse.cs` | Hai con số tổng + dãy bucket liên tục |
| `backend/SmartBus.Api/Services/IRevenueReportService.cs`, `RevenueReportService.cs` | Tổng hợp — năm chốt nghiệp vụ ghi ở doc lớp service |
| `backend/SmartBus.Api/Controllers/RevenueReportController.cs` | `GET api/reports/revenue`, `ManagerOrAbove` |
| `backend/SmartBus.Api/Program.cs` | Một dòng đăng ký DI (sau khối voucher — cùng nhóm "đọc tổng hợp") |
| `backend/SmartBus.Tests/RevenueReportApiTests.cs` | 15 ca — quyền, biên múi giờ VN, tuần/tháng, nền Success, đẳng thức tổng |
| `docs/api-contract.md` | Mục "Báo cáo doanh thu — /reports/revenue" — hợp đồng mới |

Không sửa file nào khác: bảng `Payments` đã có từ migration Sprint 3 — không cần Dăm làm gì thêm.

## 2. Năm chốt nghiệp vụ

1. **Nền là bảng `Payments`, chỉ đếm `Success`.** Doanh thu là tiền ĐÃ THU THỰC — kiểm thử chéo
   của story đối chiếu "số liệu doanh thu khớp với giao dịch thực tế", mà giao dịch thực tế nằm ở
   `Payments`. Vé `Paid`/`Used` không phải nền: tiền mới là thứ được đối soát. Mốc thời gian của
   giao dịch là `PaidAt` (cổng ghi nhận thu tiền); `Success` thiếu `PaidAt` thì tính theo
   `CreatedAt` — tiền đã thu không được biến mất khỏi báo cáo.
2. **Trục thời gian là giờ Việt Nam (UTC+7 cố định).** Giao dịch 00:30 giờ VN thuộc ngày HÔM ĐÓ,
   không phải ngày hôm trước theo UTC. `from`/`to` là ngày lịch VN; tuần bắt đầu **thứ Hai**,
   tháng theo lịch dương. Không dùng `TimeZoneInfo` (tên vùng khác nhau giữa Windows/Linux).
3. **Tổng và dãy bucket lấy từ HAI nguồn truy vấn khác nhau.** Tổng là Sum/Count gộp trong CSDL;
   bucket gộp trong C# từ dòng thô — đẳng thức "tổng == cộng các bucket" là phép kiểm THẬT (phép
   gộp lệch múi giờ làm rơi dòng là test đỏ ngay).
4. **Dãy bucket LIÊN TỤC, không bỏ ngày rỗng** — biểu đồ cần trục đầy đủ. Bucket đầu có thể bắt
   đầu TRƯỚC `from` (tuần từ thứ Hai, tháng từ ngày 1).
5. **Lọc tuyến bằng hai truy vấn (danh sách tripId), không JOIN navigation** — phép JOIN qua
   `p.Trip.RouteId` hành xử khác nhau giữa Npgsql và InMemory của test (navigation null ở dòng
   seed mồ côi).

## 3. Cách dùng

```http
GET /api/reports/revenue?granularity=day&from=2026-10-01&to=2026-10-14&routeId=…
Authorization: Bearer <token Admin/Quản lý>
```

| Tham số | Mặc định | Ghi chú |
|---|---|---|
| `granularity` | `day` | `day` / `week` / `month` — không phân biệt hoa/thường |
| `from` / `to` | 30 ngày gần nhất tính cả hôm nay (giờ VN) | Ngày lịch Việt Nam, TÍNH CẢ hai đầu |
| `routeId` | toàn công ty | Lọc theo tuyến — không tồn tại → 404 |

Response: `totalRevenue`/`totalPayments` (thẻ số liệu) + `buckets[]` liên tục (`periodStart`
dạng `yyyy-MM-dd`, `revenue`, `payments`) cho biểu đồ. Mã lỗi: 401 không token · 403 không phải
Admin/Quản lý · 400 `granularity` lạ / `to` trước `from` / khoảng quá 366 ngày · 404 tuyến không
tồn tại.

## 4. Test

`RevenueReportApiTests.cs` — 15 ca chạy trên app thật (TestAppFactory): 401/403/200 quyền; **biên
00:00 giờ VN** (23:59:59 VN vẫn thuộc ngày cũ); tuần bắt đầu thứ Hai; tháng gộp theo lịch + bucket
đầu trước `from`; chỉ tính `Success`; `Success` thiếu `PaidAt` dùng `CreatedAt`; bucket liên tục
điền 0; **đẳng thức tổng == cộng bucket**; mặc định 30 ngày; lọc tuyến + 404 tuyến lạ; 3 nhánh 400
tham số.
