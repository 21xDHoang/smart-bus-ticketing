# API cập nhật vị trí chuyến — PUT /trips/{id}/location (US 10, Sprint 4)

> Task Sprint 4 — backlog giao **Trần Trung Hiếu**: *"API cập nhật vị trí chuyến
> (CurrentLat/CurrentLng/CurrentStopId)"*. Đây là nửa **GHI** của vị trí xe: thiết bị của tài xế
> báo toạ độ định kỳ. Nửa **ĐỌC** (hành khách xem xe đang ở đâu — US 9) là Sprint sau.
>
> Đọc kèm: `docs/api-contract.md` mục "PUT /trips/{id}/location" (hợp đồng), mục "Chuyến xe —
> `/trips`" (vì sao vị trí nằm trên `Trips` — quy ước A8.6).

## 1. Đã có gì trên nhánh này

| File | Việc |
|---|---|
| `backend/SmartBus.Api/Dtos/Trips/UpdateTripLocationRequest.cs` | Body: `currentLat`/`currentLng` bắt buộc (double? + Required + Range), `currentStopId` bỏ trống được |
| `backend/SmartBus.Api/Dtos/Trips/TripLocationResponse.cs` | Ảnh chụp vị trí vừa lưu: `tripId`, `currentStopId`, `currentLat`, `currentLng`, `positionUpdatedAt` |
| `backend/SmartBus.Api/Services/ITripLocationService.cs`, `TripLocationService.cs` | Ghi vị trí vào `Trips` — chốt nghiệp vụ ghi ở doc lớp service |
| `backend/SmartBus.Api/Controllers/TripLocationController.cs` | `PUT api/trips/{id:guid}/location`, `[Authorize(Roles = RoleCodes.Driver)]` |
| `backend/SmartBus.Api/Program.cs` | Một dòng đăng ký DI (cạnh khối chuyến xe — cùng bảng Trips) |
| `backend/SmartBus.Tests/TripLocationApiTests.cs` | 15 ca — quyền, hạnh phúc, trạm lạ, toạ độ sai, nhật ký kiểm toán |
| `backend/SmartBus.Tests/AuditLogCoverageTests.cs` | Thêm dòng `PUT /api/trips/{id:guid}/location` vào bảng phủ sóng |
| `docs/api-contract.md` | Mục "PUT /trips/{id}/location" — hợp đồng mới |

Không sửa file nào khác: cột `CurrentLat/CurrentLng/CurrentStopId/PositionUpdatedAt` đã có trên
bảng `Trips` từ migration Sprint 2 — không cần Dăm làm gì thêm.

## 2. Bốn chốt nghiệp vụ

1. **Chỉ tài xế được phân công của chuyến mới ghi được.** `[Authorize(Roles = Driver)]` chặn vai
   trò khác ở cổng; service lọc truy vấn theo `DriverId` — chuyến không tồn tại và chuyến của tài
   xế khác CÙNG trả 404 (không để lộ chuyến nào đang chạy — cùng lối "vé của người khác trả 404").
2. **Chỉ chuyến `Scheduled` hoặc `Running` nhận vị trí.** `Completed`/`Cancelled` → 409. Cố ý
   NHẬN `Scheduled`: chưa có endpoint "bắt đầu chuyến" nào đổi trạng thái sang `Running` — chặn
   thì thiết bị không báo được vị trí trước giờ chạy.
3. **Trạm phải thuộc tuyến của chuyến.** `currentStopId` là mỏ neo cho phép "cách trạm kế tiếp
   bao xa" của service thông báo (Kiên, cùng story): trạm lạ không có `StopOrder` trên tuyến sẽ
   làm hỏng phép tính khoảng cách. Không tồn tại → 404; tồn tại nhưng không nằm trong
   `RouteStops` của tuyến → 400 theo trường `currentStopId`.
4. **`positionUpdatedAt` luôn là giờ server nhận request** — không tin giờ thiết bị gửi lên;
   `updatedAt` của chuyến cũng được làm mới.

## 3. Cách dùng

### 3.1 Thiết bị của tài xế

```http
PUT /api/trips/{id}/location
Authorization: Bearer <token tài xế>

{ "currentLat": 21.0285, "currentLng": 105.8542, "currentStopId": "c5d2e1f0-…" }
```

Gọi định kỳ (vd 10–15 giây/chuyến đang chạy). Gọi lại y hệt chỉ làm mới `positionUpdatedAt`
(idempotent); bỏ trống `currentStopId` thì chỉ đổi toạ độ, giữ nguyên trạm hiện tại. Response
200 là ảnh chụp vị trí vừa lưu.

### 3.2 Service thông báo 500m (Kiên — task cùng story)

Đọc `Trips.CurrentLat/CurrentLng/CurrentStopId/PositionUpdatedAt` + `RouteStops` để tính "cách
trạm kế tiếp bao xa". Trạm đã được chặn từ nguồn (chốt 3) nên không phải tự lọc trạm lạ.

### 3.3 Mã lỗi

| Mã | Khi nào |
|---|---|
| 401 | Không token / token hỏng |
| 403 | Vai trò không phải `Driver` |
| 404 | Chuyến không tồn tại hoặc không do tài xế này điều khiển · trạm không tồn tại |
| 409 | Chuyến đã `Completed` / `Cancelled` |
| 400 | Toạ độ thiếu/sai khoảng (lỗi theo trường) · trạm không thuộc tuyến |

## 4. Test

`TripLocationApiTests.cs` — 15 ca chạy trên app thật (TestAppFactory): 401/403/404 quyền; hạnh
phúc (200 + CSDL khớp + `positionUpdatedAt` là giờ server); vị trí mới thay vị trí cũ; bỏ trống
trạm giữ trạm cũ; `Scheduled` vẫn báo được; 409 hai trạng thái kết thúc; trạm không tồn tại 404;
trạm lạ 400 theo trường; toạ độ sai khoảng 400; thiếu toạ độ 400; và nhật ký kiểm toán ghi
`Update` / `Trips:{id}` đúng người thực hiện.

`AuditLogCoverageTests.cs` — bảng phủ sóng thêm `PUT /api/trips/{id:guid}/location`
(`CachGhi.Middleware`): test `Bang_phu_song_khop_voi_routing_that_cua_app` đối chiếu với routing
thật nên không thể lặng lẽ lạc hậu.
