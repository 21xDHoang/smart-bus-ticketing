# API thống kê số lượng hồ sơ ưu đãi theo trạng thái

> Task **"API thống kê số lượng hồ sơ ưu đãi theo trạng thái"** — US 17 *"Duyệt đối tượng ưu đãi"*,
> **Sprint 4 dòng 55**, mức **Trung bình**, **3 điểm**, người làm: **Vàng Thị Dăm**.
>
> 🟢 **Trạng thái:** code API + service + test + hợp đồng đã xong trên nhánh
> `feature/55-api-thong-ke-ho-so-uu-dai`. **Không có migration mới** — bảng `DiscountRequests` đã
> dựng từ Sprint 2 và service chỉ đọc.

## 1. Vì sao cần task này

HR/Quản lý duyệt thủ công từng hồ sơ ưu đãi (dòng 52), và hồ sơ cứ đến mãi. Không có bảng đếm thì
không trả lời được ba câu thường gặp:

- Hàng đợi duyệt còn bao nhiêu hồ sơ? (`pending`)
- Đã có bao nhiêu người được duyệt ưu đãi? (`approved`)
- Trong số đã duyệt, bao nhiêu người **đang thật sự** được hưởng giá ưu đãi?
  (`approvedActive` — phần còn hiệu lực sau khi trừ hồ sơ hết hạn)

Bảng `DiscountRequests` có sẵn từ Sprint 2 (task dòng 50, cũng của Dăm). Task này chỉ thêm **đường
đọc tổng hợp**: một endpoint trả sáu con số, không thêm bảng, không thêm cột.

## 2. Endpoint

| Method | Endpoint | Quyền | Trả về |
|---|---|---|---|
| GET | `/api/discount-requests/statistics` | `ManagerOrAbove` | `200 DiscountRequestStatistics` |

Không có token → **401**; không đủ quyền → **403**; đúng quyền → **200**, kể cả khi chưa có hồ sơ
nào (mọi con đếm bằng 0). Endpoint không nhận tham số nên không có 400/404.

Hình dạng response:

```json
{
  "total": 42,
  "pending": 7,
  "approved": 30,
  "rejected": 5,
  "approvedActive": 26,
  "approvedExpired": 4
}
```

Hợp đồng đầy đủ, gồm ý nghĩa từng trường: mục **"Hồ sơ ưu đãi — /discount-requests"** của
`docs/api-contract.md`.

## 3. Ba quyết định thiết kế

### 3.1 Hết hạn là giá trị SUY RA, không phải trạng thái thứ tư

`DiscountRequestStatus` chỉ có `Pending`, `Approved`, `Rejected`. **Cố ý không có `Expired`** —
XML doc của enum ghi rõ lý do: thêm `Expired` là đẻ hai nguồn sự thật, phải có job lật trạng thái
đúng hạn, không lật thì dữ liệu sai, và service áp giá phải xử lý cả hai đường.

Hệ quả cho endpoint này: câu *"bao nhiêu ưu đãi còn hiệu lực"* **không thể** trả lời bằng cách đếm
trạng thái. Phải đếm thêm:

```
approvedExpired = count(status == Approved && expiresAt != null && expiresAt <= now)
approvedActive  = approved - approvedExpired
```

Hai trường này là **tập con của `approved`**, không phải hai trạng thái mới — hợp đồng ghi rõ để
màn hình không cộng nhầm `approved + approvedActive`.

### 3.2 `expiresAt` null = còn hiệu lực

Entity ghi: *"người cao tuổi không có thẻ hết hạn — job nhắc hết hạn thẻ ưu đãi chỉ áp dụng cho học
sinh/sinh viên"*. Vì vậy `ExpiresAt` null nghĩa là **không có hạn**, không phải "đã hết hạn".

Nếu service đọc null thành hết hạn, mọi hồ sơ `Senior` đã duyệt sẽ biến mất khỏi `approvedActive`
mà không ai thấy lỗi: bảng thống kê chỉ đơn giản là nhỏ hơn thực tế. Test
`Ho_so_da_duyet_khong_co_ngay_het_han_van_con_hieu_luc` khoá lại.

### 3.3 Bốn con số trạng thái đếm ĐỘC LẬP

`total`, `pending`, `approved`, `rejected` là **bốn truy vấn `CountAsync` riêng**, không con nào
cộng dồn từ con khác. Nhờ vậy hai đẳng thức hợp đồng là **phép kiểm thật**:

```
total    == pending + approved + rejected
approved == approvedActive + approvedExpired
```

Nếu `total` được suy bằng `pending + approved + rejected` thì đẳng thức thứ nhất đúng theo định
nghĩa — không test nào có thể đỏ, và cả mục đẳng thức chỉ còn là tài liệu. Đếm thẳng thì một truy
vấn quên trạng thái (ví dụ lọc thiếu `Rejected`) làm test đỏ ngay. Đúng lối
`VoucherStatisticsService` và `FeedbackStatisticsService` đã làm.

## 4. Code

| File | Vai trò |
|---|---|
| `backend/SmartBus.Api/Dtos/DiscountRequests/DiscountRequestStatisticsResponse.cs` | DTO sáu trường + hai đẳng thức trong XML doc |
| `backend/SmartBus.Api/Services/IDiscountRequestStatisticsService.cs` | Hợp đồng service (chỉ đọc, không có nhánh lỗi nghiệp vụ) |
| `backend/SmartBus.Api/Services/DiscountRequestStatisticsService.cs` | Bốn truy vấn đếm độc lập + một truy vấn đếm hồ sơ đã duyệt hết hạn |
| `backend/SmartBus.Api/Controllers/DiscountRequestStatisticsController.cs` | `[Authorize(Policy = ManagerOrAbove)]`, `[HttpGet("statistics")]` |
| `backend/SmartBus.Api/Program.cs` | Đăng ký `IDiscountRequestStatisticsService` (file của Hoàng — Hoàng đã đồng ý cho sửa) |
| `backend/SmartBus.Tests/DiscountRequestStatisticsApiTests.cs` | 10 ca tích hợp |

**Vì sao controller riêng?** Luật nhóm không cho sửa file của người khác. Hai endpoint còn lại của
bề mặt `api/discount-requests` là của Trần Trung Hiếu (dòng 51) và Nguyễn Duy Kiên (dòng 52), nên
Dăm đứng ở controller riêng trên cùng bề mặt — cùng lối `VoucherStatisticsController` đứng chung
`api/vouchers` với `VouchersController`.

**Vì sao không đụng `AppDbContext.DiscountRequest.cs`?** Không cần thay đổi schema hay chỉ mục.
Chỉ mục `(Status, ExpiresAt)` dựng từ Sprint 2 đã phục vụ đúng truy vấn `approvedExpired`; comment
trong file đó đã ghi trước rằng nó phục vụ luôn "truy vấn thống kê số lượng hồ sơ theo trạng thái".

## 5. Test

| Ca | Khoá lại điều gì |
|---|---|
| `Khong_gui_token_thi_tra_401` | Thiếu token → 401 |
| `Hanh_khach_va_tai_xe_goi_thi_tra_403` (×2) | Passenger/Driver → 403, đúng câu `message` của RbacMiddleware |
| `Quan_ly_va_admin_goi_duoc` (×2) | Manager/Admin → 200 |
| `Chua_co_ho_so_nao_thi_tra_200_moi_con_dem_bang_0` | CSDL rỗng → 200, đúng **sáu** tên trường, mọi con 0 |
| `Dem_dung_theo_tung_trang_thai_va_hai_dang_thuc_luon_dung` | 6 hồ sơ đủ ca; sáu con đếm đúng; hai đẳng thức |
| `Ho_so_da_duyet_khong_co_ngay_het_han_van_con_hieu_luc` | `expiresAt` null của `Senior` → `approvedActive` |
| `Chi_ho_so_da_duyet_moi_tinh_vao_nhom_het_han` | Pending/Rejected có `expiresAt` quá khứ **không** vào `approvedExpired` |
| `Thong_ke_tren_toan_he_thong_khong_theo_nguoi_goi` | Đếm hồ sơ của mọi hành khách, không lọc theo người gọi |

Lệnh chạy:

```powershell
$env:TEMP='D:\tmp\sbt-test'; $env:TMP='D:\tmp\sbt-test'
dotnet test backend/SmartBus.Tests/SmartBus.Tests.csproj --filter "FullyQualifiedName~DiscountRequestStatistics"
```

⚠️ Bộ test chạy trên **InMemory**, không chạy migration. Test xanh chứng minh **hành vi service**,
không chứng minh bảng đã tồn tại trên CSDL chung — phần đó do migration Sprint 2 đảm bảo (đã merge).

## 6. Giới hạn đã biết

| Giới hạn | Lý do / hướng xử lý |
|---|---|
| **Chưa có màn hình frontend** | Task dòng 55 chỉ giao backend; màn hình duyệt hồ sơ là dòng 58 (Nguyễn Đình Băng). `api-contract.md` đã đủ để nối |
| **Không lọc theo thời gian / đối tượng ưu đãi** | Task chỉ hỏi "theo trạng thái". Thêm bộ lọc là đổi hình dạng API (⛔5) — sửa hợp đồng trước |
| **Không có chi tiết từng hồ sơ** | Đây là endpoint đếm; danh sách hồ sơ cho màn hình duyệt thuộc dòng 52/58, chưa có hợp đồng |
| **Mốc "bây giờ" lấy trong service** | `DateTime.UtcNow` đọc một lần cho cả request để hai truy vấn không rơi vào hai mốc khác nhau; test dùng mốc tương đối (± ngày) nên không phụ thuộc giờ chạy |

## 7. Kiểm chứng đã chạy

- `dotnet build backend/SmartBus.Api/SmartBus.Api.csproj` → **0 warning, 0 error**.
- `dotnet test … --filter "FullyQualifiedName~DiscountRequestStatistics"` → **10/10 xanh**.
- Chạy toàn bộ test suite (1.922 ca): **1.922 xanh, 0 đỏ**.
- ⚠️ **GitNexus không chạy được trên máy này** (không có `.gitnexus/run.cjs`, `analyze` crash — xem
  ghi chú trong bộ nhớ dự án). Bước `impact`/`detect_changes` bắt buộc của `CLAUDE.md` được bù bằng:
  đọc toàn bộ entity/enum/cấu hình `AppDbContext` liên quan, `dotnet build`, chạy test, và rà
  `git status` trước khi commit. Thay đổi chỉ **thêm** file mới + một dòng đăng ký DI trong
  `Program.cs` — không sửa symbol dùng chung nào.
