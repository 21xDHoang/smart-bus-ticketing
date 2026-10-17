# Tối ưu truy vấn tổng hợp doanh thu + đánh index

> Task **"Tối ưu truy vấn tổng hợp doanh thu + đánh index"** — US 19 *"Doanh thu"*,
> **Sprint 5 dòng 38** của bảng phân công (người làm: **Vàng Thị Dăm**), nhánh
> `feature/19-toi-uu-truy-van-doanh-thu`.
>
> ⚠️ **Bảng phân công ghi Sprint 5, không phải Sprint 4** như yêu cầu phiên làm việc. Người làm
> thì đúng: Vàng Thị Dăm. Task này là **phần CSDL** của story 19; các task API/dashboard cùng
> story nằm ở dòng 39–44 và thuộc người khác.
>
> 🟢 **Trạng thái:** hai chỉ mục đã thiết kế, migration `Sprint5_Payments_RevenueIndexes` và
> phần bàn giao cho Hiếu đã ghi ở dưới. **Chưa có EXPLAIN trên PostgreSQL thật** vì máy không có
> CSDL — cần đo trước/sau khi áp lên Supabase.

## 1. Truy vấn cần tối ưu

`RevenueReportService` (task dòng 39 — Trần Trung Hiếu) dựng nền báo cáo bằng:

```sql
SELECT ...
FROM "Payments" p
WHERE p."Status" = 'Success'
  AND COALESCE(p."PaidAt", p."CreatedAt") >= @fromUtc
  AND COALESCE(p."PaidAt", p."CreatedAt") <  @toExclusiveUtc;
```

Nếu lọc tuyến, service lấy danh sách `Trips.Id` của tuyến rồi thêm
`p."TripId" = ANY(@tripIds)` (hai truy vấn, không JOIN — chốt 5 của service).

Cùng một điều kiện được chạy **ba lần**: một lần `SUM(Amount)`, một lần `COUNT(*)`, một lần kéo
dòng thô `(PaidAt, CreatedAt, Amount)` để gộp bucket trong C#.

### Chỉ mục đang có trên `Payments`

| Chỉ mục | Nguồn | Dùng được cho báo cáo? |
|---|---|---|
| `IX_Payments_PaymentCode` (unique) | khai báo | Không |
| `IX_Payments_Status_CreatedAt` | khai báo (job đối soát) | Chỉ lọc được `Status`; **không** dùng được cho khoảng thời gian vì biểu thức là `COALESCE(PaidAt, CreatedAt)` |
| `IX_Payments_TicketId` | khai báo | Không |
| `IX_Payments_TripId` | EF sinh theo FK | Chỉ lọc được `TripId`; phần thời gian vẫn phải quét |
| `IX_Payments_UserId` | EF sinh theo FK | Không |

🔴 **Vấn đề:** `COALESCE("PaidAt", "CreatedAt")` không phải một cột, nên không chỉ mục thường
nào dùng được cho **khoảng thời gian**. PostgreSQL phải lọc `Status = 'Success'` (nếu chọn được
chỉ mục) rồi đọc từng dòng để tính `COALESCE` và so sánh — càng nhiều giao dịch càng chậm, và
bị lặp ba lần cho mỗi lần mở dashboard.

## 2. Hai chỉ mục được thêm

Vì `HasIndex` của EF Core chỉ nhận **danh sách property**, không nhận biểu thức, hai chỉ mục
dưới đây phải viết bằng SQL trong migration (xem §3):

```sql
-- Toàn công ty: lọc Status + khoảng COALESCE, phủ luôn ba cột mà service đọc.
CREATE INDEX "IX_Payments_Success_RevenueAt"
ON "Payments" ((COALESCE("PaidAt", "CreatedAt")))
INCLUDE ("PaidAt", "CreatedAt", "Amount")
WHERE "Status" = 'Success';

-- Có lọc tuyến: TripId đứng đầu để dùng được p."TripId" = ANY(@tripIds).
CREATE INDEX "IX_Payments_Success_TripId_RevenueAt"
ON "Payments" ("TripId", (COALESCE("PaidAt", "CreatedAt")))
INCLUDE ("PaidAt", "CreatedAt", "Amount")
WHERE "Status" = 'Success';
```

### Vì sao từng quyết định

| Quyết định | Lý do |
|---|---|
| **Biểu thức `COALESCE(PaidAt, CreatedAt)`** | Khớp **nguyên văn** điều kiện service đang sinh. Chỉ mục trên `PaidAt` hay `CreatedAt` riêng lẻ không dùng được cho khoảng, vì mỗi dòng có thể lấy mốc từ cột khác nhau |
| **`WHERE Status = 'Success'`** | Báo cáo chỉ đếm giao dịch thành công (chốt 1 của service). Chỉ mục điều kiện nhỏ hơn nhiều, ít tốn chỗ và ít phải cập nhật khi ghi `Pending`/`Failed` |
| **`INCLUDE (PaidAt, CreatedAt, Amount)`** | Ba cột duy nhất service đọc sau khi lọc. Có chúng trong chỉ mục thì `SUM`, `COUNT` và truy vấn lấy dòng thô đều có thể chạy **index-only scan**, không nhảy về heap |
| **Chỉ mục thứ hai bắt đầu bằng `TripId`** | Nhánh lọc tuyến dùng `= ANY(@tripIds)`; đặt `TripId` đầu thì mỗi trip là một lần seek, rồi khoảng thời gian là cột thứ hai |
| **Không unique** | Nhiều giao dịch cùng thời điểm là bình thường; unique sẽ chặn dữ liệu thật |
| **Giữ `IX_Payments_TripId`** | Chỉ mục mới là **điều kiện** (chỉ `Success`) nên không phục vụ được kiểm tra khoá ngoại khi xoá `Trips` (A5 — `Restrict`) và các truy vấn đọc giao dịch `Pending`/`Failed`. Giữ chỉ mục FK đầy đủ là cố ý |

Chỉ mục không thay đổi kết quả báo cáo — `COALESCE`, múi giờ Việt Nam và cách gộp bucket giữ
nguyên. Nó chỉ đổi **đường đọc** của PostgreSQL.

## 3. Migration

Tên: `Sprint5_Payments_RevenueIndexes`. Vì hai chỉ mục là biểu thức, `Up`/`Down` dùng
`migrationBuilder.Sql(...)`; model snapshot **không đổi** (không thêm property/index nào vào
model EF). Đây là lý do `dotnet ef migrations add` sinh một migration rỗng rồi mới chèn SQL tay.

> 🔴 Lệnh `dotnet ef migrations add` bị `.claude/settings.json` chặn trong phiên Claude (chỉ Dăm
> được chạy). Dăm chạy tay trên máy mình:
>
> ```powershell
> $env:Jwt__Key = "local-design-time-only-key-at-least-32-chars-long"
> $env:Jwt__Issuer = "SmartBus"; $env:Jwt__Audience = "SmartBusClient"
> $env:ConnectionStrings__Default = "Host=localhost;Port=5432;Database=smartbus;Username=postgres;Password=placeholder"
> dotnet ef migrations add Sprint5_Payments_RevenueIndexes --project backend/SmartBus.Api
> ```
>
> Sau đó chèn hai câu `CREATE INDEX` vào `Up`, hai câu `DROP INDEX IF EXISTS` vào `Down`.

Kiểm chứng không cần CSDL thật:

```powershell
dotnet ef migrations script --project backend/SmartBus.Api          # phải thấy hai CREATE INDEX
dotnet ef migrations has-pending-model-changes --project backend/SmartBus.Api  # phải báo không đổi
```

## 4. Phần "tối ưu truy vấn" còn lại — bàn giao Hiếu

Theo ranh giới vai trò (mục 2.4 quy ước nhóm), Dăm **không sửa** `Services/RevenueReportService.cs`
— file của Hiếu. Chỉ mục ở §2 đã làm truy vấn **hiện tại** dùng được chỉ mục mà không phải đổi
một dòng code nào. Hai cải tiến nữa nên đưa vào task của Hiếu nếu cần:

1. **Gộp `SUM` và `COUNT` thành một truy vấn.** Hiện service chạy hai truy vấn gộp riêng trên
   cùng điều kiện:

   ```csharp
   var totals = await query
       .GroupBy(_ => 1)
       .Select(g => new { Revenue = g.Sum(p => p.Amount), Payments = g.Count() })
       .FirstOrDefaultAsync(cancellationToken);
   ```

   Kết quả giữ nguyên; chỉ giảm một vòng khứ hồi CSDL. Lưu ý: phải giữ phép kiểm "tổng == cộng
   bucket" — đừng suy tổng từ bucket (chốt 3 của service).

2. **Gộp bucket trong CSDL** thay vì kéo dòng thô. Chỉ nên làm nếu chấp nhận câu SQL riêng cho
   PostgreSQL, vì phép đổi giờ Việt Nam hiện được cố ý gộp trong C# để test chạy được trên cả
   InMemory (chốt 3 và 6 của service). Với chỉ mục phủ ở §2, số dòng thô đã nằm trong chỉ mục
   nên chi phí hiện tại chấp nhận được.

### Cách đo trên Supabase

```sql
EXPLAIN (ANALYZE, BUFFERS)
SELECT COALESCE("PaidAt", "CreatedAt"), "Amount"
FROM "Payments"
WHERE "Status" = 'Success'
  AND COALESCE("PaidAt", "CreatedAt") >= '2026-09-01T00:00:00+07:00'
  AND COALESCE("PaidAt", "CreatedAt") <  '2026-10-01T00:00:00+07:00';
```

Chạy **trước** và **sau** khi áp migration; ghi lại `Seq Scan`/`Bitmap Heap Scan` và `Buffers`.
Kỳ vọng sau: `Index Only Scan using "IX_Payments_Success_RevenueAt"` (hoặc chỉ mục `TripId` khi
có lọc tuyến). Với bảng nhỏ, PostgreSQL có thể vẫn chọn `Seq Scan` — đó là lựa chọn đúng, không
phải migration hỏng.

## 5. Đã kiểm

| Phép kiểm | Kết quả |
|---|---|
| `dotnet build` | ✅ 0 warning, 0 error |
| `dotnet test` (bộ hiện có) | ✅ 1657/1657 pass, 0 fail, 0 skip (8 phút 8 giây) — migration không đổi model nên không ảnh hưởng InMemory |
| `dotnet ef migrations script --no-build` | ✅ thấy đúng hai `CREATE INDEX` (`IX_Payments_Success_RevenueAt`, `IX_Payments_Success_TripId_RevenueAt`) với `INCLUDE` + `WHERE "Status" = 'Success'`; hai `DROP INDEX IF EXISTS` nằm trong `Down` của migration |
| `dotnet ef migrations has-pending-model-changes --no-build` | ✅ "No changes have been made to the model since the last migration" — `AppDbContextModelSnapshot.cs` không đổi |
| `dotnet ef database update` | ⛔ chưa — chỉ chạy sau khi PR merge, trên CSDL Supabase (máy không có PostgreSQL) |
| EXPLAIN trên PostgreSQL thật | ⛔ chưa — máy không có PostgreSQL; cần đo trên Supabase trước khi nghiệm thu |
| GitNexus `impact` / `detect-changes` | ⛔ không chạy được — repo không có `.gitnexus/run.cjs` (giới hạn đã ghi ở docs/40, docs/45); bù bằng rà soát text: chỉ thêm migration + tài liệu, không sửa code nghiệp vụ |
