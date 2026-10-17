# Cache kết quả báo cáo doanh thu theo kỳ

> **Task:** *"Cache kết quả báo cáo theo kỳ để tăng tốc"* — Vàng Thị Dăm, 3 điểm, ưu tiên Trung bình.
> **US 19 "Doanh thu"**. Trong file backlog, task nằm ở sheet **Sprint 5, dòng 43**; story 19 được
> các tài liệu API ghi là "Sprint 4" (task API báo cáo gốc — Hiếu). Đợt này chỉ làm phần đệm, không
> đổi hình dạng API.

## 1. Vì sao cần đệm

`GET /api/reports/revenue` là truy vấn tổng hợp nặng nhất của bề mặt doanh thu: mỗi lượt gọi chạy
**ba truy vấn** trên `Payments` (SUM, COUNT và lấy dòng thô để gộp bucket), cộng thêm một truy vấn
`Routes`/`Trips` khi lọc tuyến. Dashboard đổi bộ lọc rồi quay lại cùng kỳ, hoặc nhiều quản lý cùng
xem một báo cáo, sẽ lặp lại y hệt công việc đó.

Chỉ mục phủ ở [docs/47](47-toi-uu-truy-van-tong-hop-doanh-thu.md) đã giảm chi phí mỗi lượt; đệm cắt
hẳn lượt lặp lại trong một cửa sổ ngắn.

## 2. Kiến trúc — lớp bọc, không sửa service

```
RevenueReportController
        │  IRevenueReportService
        ▼
CachedRevenueReportService   ← file mới của task này
        │  trúng đệm → trả luôn
        │  trượt    → gọi service thật rồi Store nếu thành công
        ▼
RevenueReportService          ← file của Hiếu, KHÔNG sửa
        │
        ▼
RevenueReportCache (Singleton, RAM) ← file mới của task này
```

`RevenueReportService`, controller và DTO là file của Trần Trung Hiếu (quy ước E1 — không sửa file
của người khác). Đệm móc vào bằng **lớp bọc** đứng trước, cùng lối `CachedTripSearchService` bọc
`TripSearchService` của Kiên (task cache tìm chuyến — Sprint 2).

`RevenueComparisonService` cũng gọi `IRevenueReportService` hai lần (kỳ hiện tại + kỳ trước), nên
**tự động được hưởng đệm** mà không phải sửa một dòng.

## 3. Chính sách đệm

| Quyết định | Giá trị | Lý do |
|---|---|---|
| Nơi lưu | RAM, `Singleton` | Sống qua nhiều request; chưa cần Redis khi chạy một instance |
| TTL | **60 giây**, hết hạn tuyệt đối | Cùng con số với đệm tìm chuyến; đủ gộp các lượt xem lặp lại, không để số liệu đứng yên quá lâu |
| Trần khoá | **200** (`IRevenueReportCache.MaxEntries`) | Chạm trần thì dọn khoá hết hạn; vẫn đầy thì không nhận thêm, không đuổi khoá đang nóng |
| Ngưỡng "phổ biến" | **Không có** | Endpoint đã phân quyền Admin/Manager; mỗi tổ hợp tham số là lượt xem có chủ đích, lưu ngay từ lượt đầu để đổi bộ lọc rồi quay lại vẫn trúng |
| Kết quả được đệm | Chỉ `Success` | `400`/`404` phải trả lời tươi: tuyến vừa tạo phải tra được ngay |
| Báo cáo rỗng | **Có đệm** | Kỳ chưa phát sinh giao dịch vẫn là truy vấn thật, hay được hỏi lặp lại |

### Khoá đệm

Khoá gồm đúng bộ tham số ảnh hưởng tới kết quả, đã chuẩn hoá theo cách service thật hiểu:

```
<granularity>|<from>|<to>|<routeId>|<today?>
```

- `granularity`: trim + lowercase; trống/whitespace → `day` (đúng `RevenueGranularityCodes.TryParse`).
  Giá trị lạ **không vào đệm** — chuyển thẳng service thật để trả `400` đúng cấu trúc `errors`.
- `from`/`to`: `yyyy-MM-dd` (lịch Việt Nam); bỏ trống ghi `-`.
- `routeId`: Guid dạng `N`; bỏ trống ghi `-` (toàn công ty).
- `today`: **chỉ có mặt khi `from` hoặc `to` bỏ trống**, lấy ngày hiện tại theo giờ Việt Nam
  (UTC+7). Service thật lấy mốc "hôm nay" cho đầu bị trống; đưa ngày vào khoá để qua nửa đêm
  không trả lại báo cáo của cửa sổ cũ trong lúc entry còn hạn.

## 4. Nối dây ở `Program.cs`

`Program.cs` là file của Phùng Duy Hoàng — **nhờ Hoàng xem qua trong PR**. Ba dòng đăng ký nằm ngay
sau đăng ký `IRevenueReportService` của Hiếu; DI lấy bản đăng ký **sau cùng**, nên thứ tự là bắt
buộc:

```csharp
builder.Services.AddScoped<IRevenueReportService, RevenueReportService>(); // của Hiếu — giữ nguyên

builder.Services.AddScoped<RevenueReportService>();
builder.Services.AddSingleton<IRevenueReportCache, RevenueReportCache>();
builder.Services.AddScoped<IRevenueReportService>(provider => new CachedRevenueReportService(
    provider.GetRequiredService<RevenueReportService>(),
    provider.GetRequiredService<IRevenueReportCache>()));
```

Ai đăng ký thêm `IRevenueReportService` ở phía dưới sẽ **thay thế im lặng** bản có đệm — cùng cảnh
báo đã ghi ở khối cache tìm chuyến.

## 5. Giới hạn đã biết

1. **Không huỷ theo sự kiện.** Giao dịch `Success` vừa ghi có thể chậm tối đa 60 giây mới xuất
   hiện. Muốn tươi ngay thì phải nối hook từ luồng thanh toán (file của Hiếu/Kiên) — ngoài phạm vi
   task này; TTL là ranh giới chấp nhận được cho báo cáo quản lý.
2. **Mỗi instance một đệm.** Chạy nhiều instance sau này thì mỗi máy giữ bản riêng; lúc đó chuyển
   `IRevenueReportCache` sang Redis (hoặc dùng `IMemoryCache` phân tán) — hình dạng interface giữ
   nguyên.
3. **Khoá không phân biệt người gọi.** Báo cáo là số liệu chung của công ty; hai quản lý cùng bộ
   tham số dùng chung kết quả là đúng. Phân quyền vẫn nằm ở controller (Admin/Manager).

## 6. Đã kiểm

| Phép kiểm | Kết quả |
|---|---|
| `dotnet build` | ✅ 0 warning, 0 error |
| `dotnet test` (bộ hiện có + test mới) | ✅ 1715/1715 pass, 0 fail, 0 skip (9 phút 36 giây) |
| Test đơn vị `RevenueReportCacheTests` | ✅ 6/6 — trúng/trượt, hết hạn, ghi đè cùng khoá, trần 200, dọn khoá hết hạn |
| Test tích hợp `RevenueReportCacheApiTests` | ✅ 2/2 — đệm thật sự đứng trước endpoint; `404` không vào đệm |
| GitNexus `impact` / `detect-changes` | ⛔ không chạy được — repo không có `.gitnexus/run.cjs`; bù bằng đọc code, build và test |

## 7. Việc bàn giao

- **Hoàng:** review ba dòng nối dây trong `Program.cs` (file của Hoàng).
- **Hiếu:** biết báo cáo có thể cũ tối đa 60 giây; không cần sửa `RevenueReportService`.
- **Vàng (kiểm thử):** review hai file test mới; nếu muốn đo hiệu năng thật, chạy cùng một URL hai
  lần trên Supabase và so `EXPLAIN`/thời gian — lượt thứ hai không còn truy vấn CSDL.
- **FE (Băng/Thạnh/Hạnh):** khi hiển thị số liệu doanh thu, không giả định vừa thanh toán xong là
  báo cáo đổi ngay; chờ tối đa 60 giây hoặc gọi lại sau khi hết TTL.
