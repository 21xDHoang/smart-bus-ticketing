# API so sánh doanh thu giữa các kỳ (tăng/giảm %)

> Task **"API so sánh doanh thu giữa các kỳ (tăng/giảm %)"** — US 19 *"Doanh thu"*, Sprint 4
> dòng 56 của bảng phân công (người làm ghi: **Phùng Duy Hoàng**), nhánh
> `feature/56-api-so-sanh-doanh-thu-giua-cac-ky`.
>
> 🟢 **Trạng thái:** endpoint + test đã xong trên nhánh. Hợp đồng đã ghi vào
> `docs/api-contract.md` **trước** khi viết code (mục *"Báo cáo doanh thu — `/reports/revenue`"*,
> tiểu mục `GET /reports/revenue/comparison`). Không có migration — chỉ đọc bảng `Payments` sẵn có.

## 1. Endpoint

| Method | Endpoint | Quyền |
|---|---|---|
| GET | `/api/reports/revenue/comparison` | `ManagerOrAbove` (Admin + Quản lý; vai trò khác → 403) |

Query — **mọi tham số bỏ trống được**:

| Tham số | Mặc định | Ý nghĩa |
|---|---|---|
| `from` | 29 ngày trước `to` | Ngày bắt đầu **kỳ hiện tại** (lịch Việt Nam), tính cả |
| `to` | hôm nay (giờ VN) | Ngày kết thúc kỳ hiện tại, tính cả |
| `routeId` | — | Lọc theo tuyến, áp dụng cho **cả hai kỳ**; bỏ trống = toàn công ty |

Phản hồi:

```json
{
  "from": "2026-10-11",
  "to": "2026-10-17",
  "previousFrom": "2026-10-04",
  "previousTo": "2026-10-10",
  "currentRevenue": 150000000,
  "previousRevenue": 120000000,
  "revenueChange": 30000000,
  "revenueChangePercent": 25.00,
  "currentPayments": 600,
  "previousPayments": 500,
  "paymentsChange": 100,
  "paymentsChangePercent": 20.00
}
```

## 2. Bốn quyết định thiết kế

### 2.1 Không có tham số `granularity`

"Kỳ" do người gọi chọn bằng `from`/`to`; kỳ trước **luôn cùng số ngày**, kết thúc ngay trước
`from`. Ví dụ 11/10 → 17/10 (7 ngày) thì kỳ trước là 04/10 → 10/10 (7 ngày). Muốn so tuần/tháng
thì truyền khoảng ngày tương ứng — không định nghĩa lại "tuần bắt đầu thứ Hai / tháng theo lịch"
lần thứ hai, tránh hai báo cáo lệch nhau.

Cố ý **không** lấy "tháng trước theo lịch": tháng 28/30/31 ngày sẽ làm hai kỳ khác độ dài, trái
với yêu cầu "so sánh giữa các kỳ" cùng độ dài đã chốt trong hợp đồng.

### 2.2 Một nguồn số duy nhất

`RevenueComparisonService` **không tự truy vấn** bảng `Payments`. Nó gọi
`IRevenueReportService.GetAsync` hai lần — kỳ hiện tại rồi kỳ trước:

- Mặc định 30 ngày, trần 366 ngày, kiểm tra `from`/`to`, kiểm tra tuyến tồn tại nằm ở đúng một chỗ.
- Chỉ giao dịch `PaymentStatus.Success`, mốc thời gian `PaidAt ?? CreatedAt`, trục ngày giờ VN
  (UTC+7) — áp dụng y nguyên cho cả hai kỳ.
- Kỳ hiện tại trả lại `from`/`to` **đã áp mặc định**; kỳ trước tính từ chúng, không đoán lại.

Nhờ vậy con số trên thẻ "so sánh" luôn khớp đúng báo cáo chi tiết cùng khoảng ngày. Muốn đổi nền
số liệu (ví dụ tính cả giao dịch hoàn tiền) thì sửa **một chỗ** là `RevenueReportService`.

### 2.3 % là `null` khi kỳ trước bằng 0

Không có nền để chia. Trả `0%` sẽ khiến màn hình nói "không đổi" trong khi thực tế là từ không có
doanh thu → có doanh thu. Số tuyệt đối (`revenueChange`, `paymentsChange`) **vẫn luôn có**; FE hiển
thị `—` cho phần trăm. Hai trường `revenueChangePercent` / `paymentsChangePercent` độc lập nhau
(kỳ trước có thể có doanh thu nhưng 0 giao dịch? không xảy ra trên dữ liệu thật, nhưng quy tắc vẫn
áp riêng từng trường).

Phần trăm làm tròn **2 chữ số thập phân**, `MidpointRounding.AwayFromZero`.

### 2.4 Ranh giới kỳ theo giờ Việt Nam

Kỳ trước kết thúc `from - 1` (lịch VN). Cửa sổ UTC giao cho báo cáo gốc tự tính, nên giao dịch
23:59:59 ngày cuối kỳ trước vẫn thuộc kỳ trước; 00:00 ngày `from` đã sang kỳ hiện tại. Có test
ghim đúng biên `16:59:59Z` / `17:00:00Z`.

## 3. Lỗi

| Tình huống | Mã | Ghi chú |
|---|---|---|
| Không gửi token | 401 | middleware xác thực |
| Không phải Admin/Quản lý | 403 | policy `ManagerOrAbove` |
| `from` sau `to` | 400 | lỗi gắn vào **cả** `errors.from` và `errors.to`, do báo cáo gốc trả về |
| Khoảng hiện tại quá 366 ngày | 400 | cùng quy tắc báo cáo gốc |
| `routeId` không tồn tại | 404 | `Không tìm thấy tuyến đường.` |
| Tràn lịch khi tính kỳ trước | 400 | chỉ xảy ra với ngày đầu năm 1; chặn để không 500 |

Định dạng lỗi theo chuẩn chung: `{ "message": "...", "errors": { "field": ["..."] } }`.

## 4. File

| File | Thay đổi |
|---|---|
| `backend/SmartBus.Api/Dtos/Reports/RevenueComparisonRequest.cs` | **mới** |
| `backend/SmartBus.Api/Dtos/Reports/RevenueComparisonResponse.cs` | **mới** |
| `backend/SmartBus.Api/Services/IRevenueComparisonService.cs` | **mới** |
| `backend/SmartBus.Api/Services/RevenueComparisonService.cs` | **mới** |
| `backend/SmartBus.Api/Controllers/RevenueComparisonController.cs` | **mới** — controller riêng, đúng lệ "mỗi task một controller" |
| `backend/SmartBus.Api/Program.cs` | thêm 1 dòng đăng ký DI (file của Hoàng) |
| `backend/SmartBus.Tests/RevenueComparisonApiTests.cs` | **mới** — 18 test |
| `docs/api-contract.md` | thêm tiểu mục endpoint + cập nhật câu giới thiệu story 19 |

Không sửa `RevenueReportService.cs`, `RevenueReportController.cs`, DTO báo cáo gốc hay bất kỳ file
nào của người khác. Không có bảng/cột mới → không cần migration.

## 5. Kiểm thử

18 test tích hợp trong `RevenueComparisonApiTests`, phủ: 401/403/200; ranh giới hai kỳ (kể cả biên
giờ VN); tăng/giảm/không đổi; kỳ trước bằng 0 → % `null`; làm tròn 2 chữ số; chỉ đếm `Success`;
thiếu `PaidAt` dùng `CreatedAt`; lọc tuyến cho cả hai kỳ; tuyến không tồn tại → 404; `from > to` và
quá 366 ngày → 400; mặc định 30 ngày.

Toàn bộ suite backend: **1707/1707 xanh** (1689 cũ + 18 mới).

## 6. Bàn giao

- **Frontend (Hạnh — dashboard dòng 59–60):** gọi endpoint này để vẽ thẻ "so với kỳ trước" cạnh
  biểu đồ doanh thu. Phần trăm `null` → hiển thị `—`, đừng hiển thị `0%`.
- **Hiếu:** không cần làm gì; endpoint dùng lại service báo cáo của Hiếu, không sửa chữ ký. Nếu
  sau này đổi nền số liệu trong `RevenueReportService`, endpoint so sánh tự động theo.
- **Dăm:** không có migration.
