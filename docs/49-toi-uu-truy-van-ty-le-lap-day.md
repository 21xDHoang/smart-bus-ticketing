# Tối ưu truy vấn thống kê tỷ lệ lấp đầy theo chuyến

> Task **"Tối ưu truy vấn thống kê tỷ lệ lấp đầy theo chuyến"** — US 20, **Sprint 5 dòng 50**
> của bảng phân công (người làm: **Vàng Thị Dăm**, 4 điểm, ưu tiên Cao), nhánh
> `feature/20-toi-uu-truy-van-ty-le-lap-day`.
>
> ⚠️ **Bảng phân công ghi Sprint 5, không phải Sprint 4** như yêu cầu phiên làm việc. Các tài liệu
> story lại ghi US 20 là "Sprint 4". Người làm thì đúng: Vàng Thị Dăm. Task này là **phần CSDL**
> của story 20; **API thống kê tỷ lệ lấp đầy** là dòng 51 — task của Nguyễn Duy Kiên, chưa làm.
>
> 🟢 **Trạng thái:** ba thay đổi chỉ mục đã thiết kế và migration
> `20261017035449_Sprint5_TripFillRateIndexes` đã sinh. **Chưa có EXPLAIN trên PostgreSQL thật**
> vì máy không có CSDL — cần đo trước/sau khi áp lên Supabase.

## 1. Truy vấn cần tối ưu

Story 20: quản lý muốn xem **tỷ lệ lấp đầy chỗ trên từng chuyến** để điều chỉnh quy mô xe. Báo cáo
cần, cho mỗi chuyến trong một kỳ:

- **Tử số:** số vé đang có hiệu lực — `Tickets.Status` là `Paid` hoặc `Used`.
  Vé `Cancelled` không chiếm chỗ. Vé tháng không có ghế (chốt A8.2) nên không nằm trong phép đếm.
- **Mẫu số:** `Buses.Capacity` của xe được phân cho chuyến.
- **Lọc:** khoảng `[from, to)` trên `Trips.DepartureTime` (UTC), tuỳ chọn theo `RouteId`.
- **Gộp:** theo chuyến; từ cùng tập dòng này gộp tiếp theo tuyến và theo khung giờ.

Hiện **chưa có service/API nào** làm báo cáo này (dòng 51 chưa làm), nên phần "tối ưu truy vấn"
được chốt bằng **hình dạng truy vấn + chỉ mục đỡ nó**, rồi bàn giao cho Kiên dùng đúng hình dạng
đó. Dăm không viết Service/Controller: ranh giới vai trò (quy ước mục 2.4) và dòng 51 là task của
Kiên.

### Hình dạng truy vấn được chốt

```sql
SELECT
    t."Id"            AS "TripId",
    t."RouteId",
    t."BusId",
    t."DepartureTime",
    b."Capacity",
    (SELECT count(*)
       FROM "Tickets" tk
      WHERE tk."TripId" = t."Id"
        AND tk."Status" IN ('Paid', 'Used')) AS "Sold"
FROM "Trips" t
INNER JOIN "Buses" b ON b."Id" = t."BusId"
WHERE t."DepartureTime" >= @from
  AND t."DepartureTime" <  @to
  [AND t."RouteId" = @routeId]
ORDER BY t."DepartureTime";
```

LINQ tương đương cho Kiên (dùng được với `AppDbContext`, không cần navigation `Trip.Tickets` —
entity `Trip` cố ý không có collection vé):

```csharp
var rows = await (
    from t in db.Trips.AsNoTracking()
    join b in db.Buses.AsNoTracking() on t.BusId equals b.Id
    where t.DepartureTime >= from
          && t.DepartureTime < to
          && (routeId == null || t.RouteId == routeId)
    select new
    {
        t.Id,
        t.RouteId,
        t.BusId,
        t.DepartureTime,
        b.Capacity,
        Sold = db.Tickets.Count(tk =>
            tk.TripId == t.Id &&
            (tk.Status == TicketStatus.Paid || tk.Status == TicketStatus.Used)),
    })
    .ToListAsync(cancellationToken);
```

Tỷ lệ lấp đầy = `Sold / Capacity` (làm tròn ở tầng API, **không** làm tròn trong SQL). Nếu báo cáo
chỉ tính chuyến đã chạy, thêm `t.Status != TripStatus.Cancelled` — cột `Status` đã nằm trong
`INCLUDE` của cả hai chỉ mục nên điều kiện này không kéo thêm heap.

> ⚠️ **Đừng đặt phép đổi múi giờ vào `WHERE`.** `date_trunc('hour', "DepartureTime" AT TIME ZONE
> 'Asia/Ho_Chi_Minh')` là biểu thức, không dùng được chỉ mục trên `DepartureTime`. Hãy lọc bằng
> khoảng UTC như trên (đúng lối `RevenueReportService`), rồi gộp khung giờ Việt Nam trong C# —
> hoặc nếu buộc phải gộp trong SQL thì mới tính đến một chỉ mục biểu thức riêng.

## 2. Chỉ mục đang có trước task này

| Bảng | Chỉ mục | Dùng được cho báo cáo lấp đầy? |
|---|---|---|
| `Trips` | `IX_Trips_RouteId_DepartureTime` (`RouteId`, `DepartureTime`) | Dùng được khi **lọc tuyến**; nhưng thiếu `BusId`/`Status` nên phải quay lại heap đọc từng chuyến |
| `Trips` | `IX_Trips_DriverId_DepartureTime` | Không — dành cho lịch tài xế (US 14) |
| `Trips` | không có chỉ mục riêng trên `DepartureTime` | 🔴 Nhánh **toàn công ty** không có chỉ mục nào dùng được: `RouteId` đứng đầu chỉ mục nên Postgres không lấy được một khoảng `DepartureTime` xuyên qua cột tiền tố |
| `Tickets` | `IX_Tickets_TripId_SeatId_Active` (unique, điều kiện `Status IN ('Held','Paid')`) | Không — điều kiện không khớp `Paid`/`Used`, và `SeatId` chen giữa |
| `Tickets` | `IX_Tickets_TripId` | Đếm được vé của một chuyến, nhưng phải đọc heap từng dòng để kiểm `Status` |
| `Tickets` | `IX_Tickets_UserId`, `IX_Tickets_Code` | Không liên quan |
| `Buses` | khoá chính `Id` | Đủ — mỗi chuyến tra sức chứa bằng một lần seek theo PK |

## 3. Ba thay đổi chỉ mục

Migration sinh đúng đoạn DDL sau (xem §4):

```sql
-- 1) Nhánh toàn công ty: quét khoảng DepartureTime, phủ mọi cột báo cáo đọc từ Trips.
CREATE INDEX "IX_Trips_DepartureTime"
ON "Trips" ("DepartureTime")
INCLUDE ("Id", "RouteId", "BusId", "Status");

-- 2) Nhánh lọc tuyến: giữ nguyên hai cột khoá, chỉ thêm cột phủ.
--    Đổi chỉ mục cũ thay vì tạo chỉ mục trùng để không trả giá ghi hai lần.
CREATE INDEX "IX_Trips_RouteId_DepartureTime"
ON "Trips" ("RouteId", "DepartureTime")
INCLUDE ("Id", "BusId", "Status");

-- 3) Đếm vé Paid/Used của từng chuyến.
CREATE INDEX "IX_Tickets_TripId_Status"
ON "Tickets" ("TripId", "Status");
```

> ⚠️ **Không xoá `IX_Tickets_TripId`.** Chỉ mục hai cột đã phục vụ mọi truy vấn mà chỉ mục một
> cột phục vụ, nhưng `backend/SmartBus.Tests/TicketQrIssuanceChainTests.cs` của Giàng A Vàng ghim
> **nguyên văn** câu `CREATE INDEX "IX_Tickets_TripId" ...` trên DDL sinh từ mô hình. Bỏ chỉ mục là
> làm đỏ file test của người khác. Giữ cả hai trong lần này; việc dọn chỉ mục thừa phải bàn với
> Vàng trước (xem §7).

### Vì sao từng quyết định

| Quyết định | Lý do |
|---|---|
| **Thêm chỉ mục `(DepartureTime)`** | Nhánh "toàn công ty" lọc đúng cột này. Không có nó, Postgres phải quét cả bảng `Trips` rồi mới lọc thời gian |
| **`INCLUDE (Id, RouteId, BusId, Status)` trên chỉ mục thời gian** | Bốn cột báo cáo đọc từ `Trips`: `Id` để trả về và tương quan với vé, `RouteId` để gộp tuyến, `BusId` để join `Buses` lấy sức chứa, `Status` để loại chuyến huỷ. Đủ cả bốn thì phép quét là **index-only** |
| **`INCLUDE (Id, BusId, Status)` trên chỉ mục tuyến** | Cùng bộ cột, trừ `RouteId` vốn đã là cột khoá. Giữ nguyên thứ tự `(RouteId, DepartureTime)` để `TripSearchService` và các truy vấn tìm chuyến hiện có vẫn dùng đúng chỉ mục |
| **Thêm `(TripId, Status)` bên cạnh `(TripId)`** | `TripId` đứng đầu nên chỉ mục mới phục vụ luôn "danh sách khách của chuyến" (A6), kiểm tra khoá ngoại `TripId` và mọi truy vấn `WHERE TripId = @t`. Thêm `Status` biến phép đếm `Paid`/`Used` thành index-only scan — đúng chi phí lớn nhất của báo cáo. Chỉ mục một cột **vẫn giữ** vì A6 chốt nó và test DDL của Giàng A Vàng ghim nguyên văn; gộp lại được là việc của một migration sau, sau khi bàn với Vàng |
| **Không dùng partial index `WHERE Status IN ('Paid','Used')`** | EF truyền `Status` dưới dạng **tham số**, không phải hằng trong câu SQL; Postgres không chứng minh được điều kiện của partial index với tham số ở kế hoạch chung, nên có nguy cơ bị bỏ qua. Chỉ mục hai cột đầy đủ nhỏ (mỗi vé một dòng) và chắc chắn dùng được |
| **Không thêm chỉ mục trên `Buses.Capacity`** | Mỗi chuyến join `Buses` bằng khoá chính; thêm chỉ mục chỉ tốn chỗ ghi |
| **Không thêm chỉ mục `(Status, TripId)`** | Truy vấn đi từ chuyến sang vé, không quét toàn bộ vé theo trạng thái |
| **Không unique** | Nhiều vé cùng chuyến, nhiều chuyến cùng giờ là dữ liệu thật |
| **Không gộp `IX_Tickets_TripId` vào chỉ mục hai cột (lần này)** | Có thể gộp về mặt kỹ thuật, nhưng test `TicketQrIssuanceChainTests.Ddl_co_chi_muc_TripId_va_UserId_khong_dieu_kien` của Giàng A Vàng sẽ đỏ. Đây là quyết định **phối hợp**, không phải quyết định kỹ thuật của Dăm |

Chỉ mục **không đổi kết quả** báo cáo. Chúng chỉ đổi đường đọc của PostgreSQL.

### Kế hoạch thực thi kỳ vọng

- Nhánh toàn công ty: `Index Only Scan using "IX_Trips_DepartureTime"` cho khoảng thời gian →
  `Nested Loop` sang `Buses` theo PK → với mỗi chuyến, `Index Only Scan using
  "IX_Tickets_TripId_Status"` cho phép đếm.
- Nhánh lọc tuyến: `Index Only Scan using "IX_Trips_RouteId_DepartureTime"` với
  `RouteId = @routeId` + khoảng `DepartureTime`, phần còn lại như trên.
- Kỳ **rất rộng** (ví dụ cả năm): bộ lập kế hoạch có thể đổi sang hash aggregate trên `Tickets`
  thay vì lặp từng chuyến. Cả hai đường đều đúng; chỉ mục hai cột vẫn là đường đọc tốt nhất cho
  phép gộp đó.
- Với bảng nhỏ, PostgreSQL có thể vẫn chọn `Seq Scan` — đó là lựa chọn đúng, không phải migration
  hỏng.

## 4. Migration

Tên: `20261017035449_Sprint5_TripFillRateIndexes` (sinh ngày 17/10/2026). Khác task doanh thu ở
docs/47, ba chỉ mục này **khai báo được bằng mô hình EF** (`HasIndex(...).IncludeProperties(...)`),
nên migration do `dotnet ef migrations add` sinh tự động — không có `migrationBuilder.Sql` tay.

Hai file mô hình đã sửa (đều thuộc quyền Dăm):

- `backend/SmartBus.Api/Data/AppDbContext.Trip.cs` — thêm `IX_Trips_DepartureTime`, thêm
  `IncludeProperties` cho chỉ mục tuyến.
- `backend/SmartBus.Api/Data/AppDbContext.Ticket.cs` — thêm `(TripId, Status)`, **giữ nguyên**
  chỉ mục một cột `(TripId)`.

Hệ quả của việc khai báo bằng mô hình: `AppDbContextModelSnapshot.cs` **có đổi** (khác docs/47),
và `Down` tự sinh đúng chiều ngược lại: xoá ba chỉ mục mới và dựng lại
`IX_Trips_RouteId_DepartureTime` (không INCLUDE). `IX_Tickets_TripId` không bị đụng tới ở cả hai
chiều.

> 🔴 Lệnh `dotnet ef migrations add` bị `.claude/settings.json` chặn trong phiên Claude (chỉ Dăm
> được chạy). Dăm đã chạy tay:
>
> ```powershell
> dotnet ef migrations add Sprint5_TripFillRateIndexes --project backend/SmartBus.Api
> ```

Kiểm chứng không cần CSDL thật:

```powershell
dotnet ef migrations script 20261017025306_Sprint5_Payments_RevenueIndexes 20261017035449_Sprint5_TripFillRateIndexes --project backend/SmartBus.Api
dotnet ef migrations has-pending-model-changes --project backend/SmartBus.Api
```

## 5. Đã kiểm

| Phép kiểm | Kết quả |
|---|---|
| `dotnet build backend/SmartBus.Api` | ✅ 0 warning, 0 error |
| `dotnet build backend/SmartBus.Tests` | ✅ 0 error; 4 warning `xUnit2031` **có sẵn** trong `SubscriptionApiTests.cs`, không phải file của task này |
| Test mới `TripFillRateIndexTests` | ✅ 4/4 — cấu trúc chỉ mục trên mô hình + cột `INCLUDE` trên thao tác migration |
| `dotnet test` (toàn bộ) | ✅ 1797/1797 pass, 0 fail, 0 skip (6 phút 50 giây) |
| `dotnet ef migrations script ...` | ✅ thấy đúng ba `CREATE INDEX` với `INCLUDE` như §3, cùng **một** `DROP INDEX` (`IX_Trips_RouteId_DepartureTime` cũ) — không đụng `IX_Tickets_TripId` |
| `dotnet ef migrations has-pending-model-changes` | ✅ "No changes have been made to the model since the last migration" |
| `dotnet ef database update` | ⛔ chưa — chỉ chạy sau khi PR merge, trên CSDL Supabase |
| EXPLAIN trên PostgreSQL thật | ⛔ chưa — máy không có PostgreSQL; cần đo trên Supabase trước khi nghiệm thu |
| GitNexus `impact` / `detect-changes` | ⛔ không chạy được — repo không có `.gitnexus/run.cjs`; bù bằng đọc code, build, test và kiểm tra migration script |

### Test mới ghim điều gì

`backend/SmartBus.Tests/TripFillRateIndexTests.cs`:

1. `IX_Trips_DepartureTime` tồn tại, không unique, không lọc.
2. `IX_Trips_RouteId_DepartureTime` vẫn đúng thứ tự hai cột khoá.
3. `IX_Tickets_TripId_Status` tồn tại **và** chỉ mục A6 một cột `IX_Tickets_TripId` vẫn còn; thao
   tác `Up` không xoá chỉ mục A6 và `Down` không dựng lại nó.
4. Thao tác `Up` của migration sinh đúng cột `INCLUDE` cho cả hai chỉ mục `Trips`.

Test dùng Npgsql với chuỗi kết nối giả (không mở kết nối) để đọc metadata; InMemory bỏ qua toàn bộ
chỉ mục nên không thay thế được. Đây không phải bằng chứng hiệu năng — chỉ là chốt chống hồi quy.

## 6. Bàn giao

- **Nguyễn Duy Kiên (dòng 51 — API thống kê lấp đầy):** dùng hình dạng truy vấn ở §1. Mục
  "Khung giờ cao điểm — /reports/peak-hours" của `docs/api-contract.md` đã ghi đúng hướng: mở
  endpoint riêng (ví dụ `GET /reports/fill-rate`), **đừng đổi nghĩa `passengers`** của báo cáo
  peak-hours. Chưa có endpoint trong contract; phải **sửa contract trước** rồi mới code (quy ước
  mục 1.5). Chốt rõ trong contract: chỉ đếm `Paid`/`Used`, mẫu số là `Buses.Capacity`, vé tháng
  không có ghế nên không tính, và múi giờ gộp khung giờ là Việt Nam (UTC+7).
- **Trần Trung Hiếu:** `PeakHoursReportService` hiện có **không** bị ảnh hưởng — báo cáo khung giờ
  cao điểm đếm vé, không có mẫu số sức chứa; đừng trộn hai báo cáo.
- **Vàng Thị Dăm:** sau khi PR merge, chạy `dotnet ef database update` trên Supabase và ghi lại
  `EXPLAIN (ANALYZE, BUFFERS)` trước/sau.
- **Vàng (kiểm thử):** review `TripFillRateIndexTests`; khi API dòng 51 có, thêm test tích hợp so
  số vé/sức chứa với dữ liệu seed.
- **Hoàng:** không cần sửa `Program.cs` — task này không thêm service/DI.
- **FE (Băng/Thạnh/Hạnh):** chờ endpoint dòng 51; chưa có hình dạng response để gọi.

## 7. Giới hạn đã biết

1. **Chưa có API nên chưa đo được.** Chỉ mục được thiết kế theo hình dạng truy vấn đã chốt; nếu
   Kiên viết khác đi (ví dụ gộp bằng `GroupJoin` hoặc đổi điều kiện thời gian thành biểu thức) thì
   phải đo lại EXPLAIN và có thể cần chỉ mục khác.
2. **InMemory không cưỡng chế chỉ mục.** Test xanh chỉ nói mô hình/migration đúng, không nói
   PostgreSQL dùng chỉ mục.
3. **Chi phí ghi tăng.** Ba chỉ mục mới làm mỗi lần chèn/sửa `Trips`/`Tickets` ghi thêm index entry.
   Với khối lượng vé của một hệ thống xe buýt, chi phí này nhỏ so với lợi ích đọc báo cáo; nếu sau
   này đo thấy ghi chậm, xem lại `INCLUDE` (bỏ `Id`/`Status` nếu báo cáo không cần).
4. **`IX_Tickets_TripId` trở nên thừa.** `(TripId, Status)` đã phủ mọi truy vấn mà chỉ mục một cột
   phủ, nên mỗi lần ghi vé tốn thêm một index entry không đổi lại lợi ích đọc. Giữ lại **có chủ ý**
   vì test DDL `TicketQrIssuanceChainTests` của Giàng A Vàng ghim nguyên văn câu `CREATE INDEX` đó.
   Việc cần làm: bàn với Vàng, cập nhật test để chấp nhận chỉ mục hai cột, rồi một migration sau
   (thuộc Dăm) mới `DROP INDEX "IX_Tickets_TripId"`. **Không tự xoá trong nhánh này.**
5. **Chưa xét vé tháng.** Chốt A8.2 nói vé tháng không kèm ghế. Nếu nghiệp vụ muốn tính cả hành
   khách vé tháng vào tỷ lệ lấp đầy, đó là quyết định của task API dòng 51, không phải của chỉ mục;
   lúc đó cần thêm nguồn đếm chứ không sửa chỉ mục này.
6. **Một instance/CSDL.** Chỉ mục là tối ưu tầng CSDL, không phụ thuộc số instance API. Nếu sau này
   đọc từ replica, chỉ mục đi theo CSDL.
