# Việc của Dăm — migrate hai bảng Vouchers + VoucherUsages (US 18, Sprint 3)

> Ghi chú này do **Nguyễn Duy Kiên** viết khi làm task **dòng 52 — "API kiểm tra và áp dụng voucher
> vào đơn hàng"** (đã gộp luôn dòng 53 — "Validate điều kiện voucher" của Hoàng).
> Task **dòng 50 — "Migrate bảng `Vouchers`, `VoucherUsages`"** (Vàng Thị Dăm) là việc còn lại duy
> nhất để nhánh này chạy được trên CSDL thật.
>
> ✅ **XONG 10/10/2026 — MIGRATION ĐÃ SINH VÀ ĐÃ SOÁT.** Vàng Thị Dăm đã soát lại toàn bộ entity +
> cấu hình EF của Kiên theo checklist dưới: **không thiếu mục nào** (bảng, cột, kiểu, 3 FK `Restrict`,
> 6 index, `xmin`, `UpdatedAt`). Migration
> `Sprint3_Payments_Vouchers_Tickets` (`Migrations/20261010105248_…`) đã sinh và soát đạt cả checklist
> này lẫn checklist
> của `docs/28-csdl-ve-dien-tu.md` — xem mục "Đã soát file migration sinh ra" ở cuối tài liệu.
>
> Migration đó gộp cả ba thứ còn nợ của Sprint 3 (`Payments`, `Vouchers`, `VoucherUsages`) cùng bảng
> `Tickets` mới, vì EF diff TOÀN BỘ model trong một lệnh và không tách được. Tên migration vì thế khác
> tên dự kiến ghi dưới đây; phần còn lại của ghi chú này vẫn dùng được nguyên vẹn làm checklist soát.
>
> **Còn lại:** PR → merge `main` → rồi mới áp lên CSDL chung (`dotnet ef database update`), đúng thứ tự
> ở `docs/25-huong-dan-csdl-chung.md` §4. Trước khi merge thì CSDL chung chưa có bốn bảng này.
>
> Vàng Thị Dăm đã cho phép Kiên dựng entity + cấu hình EF cho hai bảng này (trao đổi trực tiếp trong
> nhóm), nên phần dưới đã xong. **Migration vẫn là việc của Dăm** — luật 3.

## Trạng thái hiện tại

Kiên đã viết sẵn (chỉ để API biên dịch + test được trên InMemory — test tích hợp của dự án chạy trên
EF InMemory nên **không chờ migration**):

| File | Nội dung |
|---|---|
| `backend/SmartBus.Api/Entities/Voucher.cs` | Entity bảng `Vouchers` — có `UpdatedAt` (bảng có sửa, A4) |
| `backend/SmartBus.Api/Entities/VoucherUsage.cs` | Entity bảng `VoucherUsages` — bảng chỉ ghi thêm, **không** `UpdatedAt` |
| `backend/SmartBus.Api/Entities/VoucherStatus.cs` | `Active` / `Inactive` |
| `backend/SmartBus.Api/Entities/VoucherDiscountType.cs` | `Percent` / `FixedAmount` |
| `backend/SmartBus.Api/Data/AppDbContext.Voucher.cs` | DbSet + `ConfigureVoucher` (index, FK Restrict, varchar(20), numeric(12,2), `xmin`) |
| `backend/SmartBus.Api/Data/AppDbContext.cs` | đã thêm 1 dòng hook `ConfigureVoucher` + 1 dòng khai báo `partial void` — **file của Dăm, nhờ Dăm review trong PR** |

Hình dạng bảng khớp hợp đồng **"Voucher — /vouchers"** trong `docs/api-contract.md` (mục này ở cuối
file). **Đừng sửa entity/DbContext cho khớp ý mình** — muốn đổi hình dạng thì sửa `api-contract.md`
trước rồi báo Kiên (luật 5). Việc ở đây chỉ là **sinh migration**.

## Dăm đã soát phần Kiên dựng — 10/10/2026

Task dòng 50 có kèm yêu cầu *"kiểm tra lại Kiên đã tạo bảng, nếu được hãy bổ sung cho đầy đủ"*. Kết
quả soát 5 file trên, đối chiếu **checklist của chính ghi chú này** và **bảng trường của hợp đồng**:

| Hạng mục | Kết quả |
|---|---|
| Tên 2 bảng (`Vouchers`, `VoucherUsages`) | ✅ lấy từ tên DbSet, PascalCase số nhiều |
| `Code` varchar(20) NOT NULL + unique · `Name` varchar(200) NOT NULL | ✅ |
| `DiscountType` / `Status` varchar(20) chuỗi; `Status` default `Active`; `DiscountType` không default | ✅ đúng cả hai vế |
| 5 cột tiền `numeric(12,2)` | ✅ `DiscountValue`, `MinOrderValue`, `MaxDiscount`, `OrderAmount`, `DiscountAmount` |
| `UsedCount` default `0` · `RouteId` nullable | ✅ |
| `PaymentCode` varchar(64) NOT NULL | ✅ |
| 3 FK đều `Restrict`; `VoucherUsages→Vouchers` **không** Cascade (`VoucherUsages.cs` chép nguyên văn nỗi lo này) | ✅ |
| Đủ 6 index | ✅ `Code`(unique), `(Status,ValidFrom,ValidUntil)`, `RouteId`, `PaymentCode`(unique), `(VoucherId,CreatedAt)`, `UserId` |
| `UpdatedAt` có ở `Vouchers`, không ở `VoucherUsages`; không `IsDeleted` | ✅ |
| `xmin` khai bằng `IsRowVersion()` trên cột hệ thống, không sinh cột mới | ✅ có dòng trong file `.cs` là **đúng**, SQL sinh ra **0** lần `xmin` — đã kiểm |
| Nối dây `ConfigureVoucher` + `partial void` | ✅ `AppDbContext.cs:29` và `:68` |

→ **Không thiếu mục nào**, nên không có gì phải "bổ sung". Việc thật của dòng 50 đúng là **sinh
migration**.

**Một thứ trông như thiếu nhưng không phải:** `VoucherUsages.PaymentCode` là **cột trần**, chưa nối FK
sang `Payments`. Về kỹ thuật nay nối được — `Payments.PaymentCode` đã có unique index
(`AppDbContext.Payment.cs:22`), và hai bảng cùng nằm trong một migration nên ràng buộc thứ tự ngày
trước không còn. Nhưng mục "Nhắc nhóm" số 2 dưới đây ghi rõ việc đó **bàn riêng với Kiên**, và hợp
đồng có cả khối 📌 giải thích thiết kế cột trần → đổi là phải sửa `api-contract.md` trước (luật 5).
**Không đổi trong lúc soát migration.**

## Việc cần làm — đúng MỘT lệnh ✅ đã chạy 10/10/2026

```bash
cd backend/SmartBus.Api
dotnet ef migrations add Sprint3_Payments_Vouchers_Tickets
```

(`dotnet ef` chưa cài thì chạy `dotnet tool restore` trước. Repo **không** có
`.config/dotnet-tools.json`, nên máy nào đã cài `dotnet ef` global thì bỏ qua bước này.)

⚠️ Máy chưa có `appsettings.Development.json` (file bị .gitignore, repo public) thì lệnh trên **vẫn
cần** hai biến môi trường, nếu không host design-time chết trước khi EF kịp đọc model — `Program.cs`
kiểm chuỗi kết nối, còn `JwtMiddleware` kiểm khoá ký. Giá trị chỉ dùng lúc dựng model, `migrations
add` không mở kết nối nào:

```bash
ConnectionStrings__Default="<chuoi-ket-noi-gia — chi can dung dinh dang>" \
Jwt__Key="<chuoi-bat-ky-dai-it-nhat-32-byte>" \
dotnet ef migrations add Sprint3_Payments_Vouchers_Tickets
```

Rồi soát file migration vừa sinh theo checklist dưới, commit trên nhánh riêng + PR như các migration
trước. **Không cần** sửa entity, không cần viết API. `api-contract.md` chỉ đổi dòng trạng thái
*"CHƯA migrate"*, không đụng hình dạng endpoint.

## Checklist soát migration

- [ ] Đủ **2 bảng** tên đúng: `Vouchers`, `VoucherUsages` (PascalCase số nhiều — tên bảng lấy từ tên
      DbSet, không phải tên entity).
- [ ] `Vouchers.Code` là **`varchar(20)` NOT NULL**; `Vouchers.Name` là **`varchar(200)` NOT NULL**.
- [ ] `DiscountType` / `Status` là **`varchar(20)`** — không phải integer (`HasConversion<string>()`,
      A3). `Status` có `defaultValue: "Active"`; `DiscountType` **không** có default (đúng như cấu
      hình — xem chú thích trong `AppDbContext.Voucher.cs`).
- [ ] Tiền là **`numeric(12, 2)`** — đủ 5 cột: `Vouchers.DiscountValue`, `Vouchers.MinOrderValue`,
      `Vouchers.MaxDiscount`, `VoucherUsages.OrderAmount`, `VoucherUsages.DiscountAmount`.
      Không có cột nào là `float`/`double`/`real` (A3).
- [ ] `Vouchers.UsedCount` có `defaultValue: 0`.
- [ ] `Vouchers.RouteId` **nullable** (null = áp dụng mọi tuyến — điều kiện "tuyến" của dòng 53).
- [ ] `VoucherUsages.PaymentCode` là **`varchar(64)` NOT NULL** — cố ý dài bằng `Payments.PaymentCode`
      dù hai bảng chưa nối FK, để ngày nối FK không phải sửa kiểu cột.
- [ ] FK nào cũng **`Restrict`**, không có `Cascade` nào: `Vouchers→Routes` (nullable),
      `VoucherUsages→Vouchers`, `VoucherUsages→Users`. 🔴 Riêng `VoucherUsages→Vouchers` mà để
      `Cascade` thì xoá một voucher là xoá sạch dấu vết tiền đã giảm — mất dữ liệu doanh thu.
- [ ] Đủ **6 index**:
      - `Vouchers (Code)` — **UNIQUE** ← chốt "mã duy nhất toàn hệ thống"
      - `Vouchers (Status, ValidFrom, ValidUntil)` — đường quét "mã còn dùng được không"
      - `Vouchers (RouteId)` — tra "tuyến này có mã nào không"
      - `VoucherUsages (PaymentCode)` — **UNIQUE** ← chốt idempotency, quan trọng nhất
      - `VoucherUsages (VoucherId, CreatedAt)` — nền cho API thống kê hiệu quả voucher (dòng 54)
      - `VoucherUsages (UserId)`
- [ ] `CreatedAt` / `UpdatedAt` / `ValidFrom` / `ValidUntil` là **`timestamptz`** (A3), không phải
      `timestamp` thiếu múi giờ.
- [ ] `Vouchers` **có** `UpdatedAt` (nullable), `VoucherUsages` **không có** `UpdatedAt`; không bảng
      nào có cột `IsDeleted`.
- [ ] ⚠️ **`xmin` phải KHÔNG xuất hiện trong SQL sinh ra** — nhưng **có** xuất hiện trong file
      migration `.cs`, và đó là chuyện bình thường. `Vouchers` khai
      `e.Property<uint>("xmin").HasColumnName("xmin").IsRowVersion();` — đây là **cột hệ thống** của
      PostgreSQL dùng làm concurrency token, đã có sẵn trong mọi bảng, **không phải cột mới**.
      EF luôn ghi đủ mọi cột của model vào khối `CreateTable` của file `.cs` (kể cả cột hệ thống), nên
      sẽ thấy đúng dòng
      `xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)`. **Đừng xoá dòng
      đó, đừng vì nó mà bỏ commit** — Npgsql mới là bên quyết định SQL, và nó bỏ qua cột hệ thống.

      Phép kiểm đúng, không cần CSDL thật:

      ```bash
      dotnet ef migrations script --no-build -o /tmp/sprint3.sql && grep -c xmin /tmp/sprint3.sql
      ```

      Kết quả phải là **`0`**. Chỉ khi SQL **có** `xmin` (dạng cột trong `CREATE TABLE`, hay
      `AddColumn`/`AlterColumn` tên đó) thì mới là cấu hình token sai — lúc đó báo Kiên, đừng commit,
      vì cả chốt chống tiêu thụ quá `Quantity` sẽ không chạy.

      ✅ **Đã kiểm 10/10/2026 trên migration `20261010105248_Sprint3_Payments_Vouchers_Tickets`:
      `grep -c xmin` trả về `0` trên 556 dòng SQL.** (Mục này trước đây viết ngược — nói "file
      migration có dòng khai cột `xmin` thì đừng commit", tức sẽ chặn nhầm một migration đúng.)

## Hai chốt của tính năng này chỉ sống ở PostgreSQL

Bộ test của dự án chạy trên provider **InMemory**, mà InMemory **không dựng unique index** và **bỏ
qua concurrency token**. Nghĩa là hai chốt quan trọng nhất của tính năng **không có test nào phủ** —
test xanh không có nghĩa là chúng chạy:

1. **`VoucherUsages.PaymentCode` UNIQUE** — chống tiêu thụ voucher hai lần khi cổng gửi lại callback
   trùng (MoMo retry tới khi nhận 204) hoặc job đối soát chạy đè. Service có phép kiểm ở tầng ứng
   dụng cho ca tuần tự, còn ca **đua nhau** chỉ chốt được bằng index này.
2. **`xmin` trên `Vouchers`** — chống hai lượt tiêu thụ song song cùng vượt qua phép kiểm
   `UsedCount >= Quantity`, tức chống tiêu thụ quá số lượng phát hành.

→ Sau khi migration chạy, **nhờ Dăm kiểm hai chốt này trên PostgreSQL thật** (đúng lối đã ghi cho
partial unique index chống trùng ghế ở `docs/26-csdl-so-do-ghe.md`). Đây là việc Kiểm thử chéo, không
phải việc của bộ test InMemory.

## Quyết định đã cân nhắc: unique index trên `upper("Code")`

`Vouchers.Code` phải duy nhất **không phân biệt hoa thường**, nhưng unique index của PostgreSQL
**phân biệt** hoa thường — ghi thẳng xuống bảng bằng chữ thường vẫn lọt. Ba đường đã xét (ghi đầy đủ
trong chú thích của `AppDbContext.Voucher.cs`):

| | Cách | Chọn? |
|---|---|---|
| 1 | Chuẩn hoá `trim().ToUpperInvariant()` ở tầng service + unique index thường | ✅ **đang dùng** |
| 2 | Unique index trên `upper("Code")` | ❌ **đã quyết KHÔNG dùng** — xem dưới |
| 3 | Kiểu `citext` | ❌ cần extension PostgreSQL mà dự án chưa dùng ở đâu |

Đường (1) được chọn vì bản nháp FE (`frontend/src/api/voucherApi.ts`) **cũng** đã chuẩn hoá chữ HOA ở
cả create lẫn update, nên toàn hệ thống giữ được một hình dạng chuẩn duy nhất. Cái mất: chốt này
nằm ở **tầng ứng dụng**, nên một script SQL ghi thẳng vào bảng có thể tạo ra `summer10` song song với
`SUMMER10` mà index không chặn.

✅ **ĐÃ QUYẾT 10/10/2026 — Vàng Thị Dăm: giữ đường (1), KHÔNG thêm index trên `upper("Code")`.**

Lý do: cả ba tầng đều đã chuẩn hoá chữ HOA (DTO chặn khuôn ký tự → service
`Trim().ToUpperInvariant()` → bản nháp FE `voucherApi.ts` ở cả create lẫn update), nên unique index
thường là **chốt cuối đủ cho mọi đường đi qua API**. Cái mất đã cân nhắc và **chấp nhận**: một script
SQL ghi thẳng xuống bảng vẫn tạo được `summer10` song song `SUMMER10`. Đổi lại, migration không phải
mang một đối tượng schema **không test nào phủ** (InMemory không dựng index) và Dăm không phải sửa tay
file migration do EF sinh — giữ được lối "migration là bản EF sinh ra" của cả repo.

→ Nếu sau này nhóm đổi ý, đóng khe bằng **một migration riêng**: thêm index không phải sửa cột nào,
nên không tốn gì ngoài một lệnh. Đây là quyết định có ý thức, không phải mặc định — đúng như mục này
yêu cầu.

## Nhắc nhóm (không phải việc code)

1. `VoucherUsages` là bảng **thứ 22** (và `Vouchers` là bảng 21, sau `FeedbackReplies`) — mục A9 của
   `docs/03-quy-uoc.md` đang liệt kê 20 bảng. Bảng tính Sprint 3 dòng 50 đã ghi rõ "Migrate bảng
   `Vouchers`, `VoucherUsages`" nên hai bảng là chủ trương có sẵn; nhóm chỉ cần chốt bổ sung vào A9.
2. **`Payments` và `Vouchers` nay cùng MỘT migration** — `Sprint3_Payments_Vouchers_Tickets`, sinh
   bằng đúng một lệnh ở trên — nên ràng buộc thứ tự mà ghi chú cũ lo (`Vouchers` phải chạy sau
   `Payments`) không còn: hai bảng ra đời trong cùng một lệnh. Vì vậy quyết định cũ — để
   `VoucherUsages.PaymentCode` là **cột trần, không phải FK** — vẫn giữ nguyên nhưng giờ là **lựa
   chọn**, không còn là ràng buộc kỹ thuật. Nối FK hay không là việc của Dăm + Kiên, bàn riêng; đừng
   tự đổi trong lúc soát migration.

   Ghi chú cũ viện dẫn `Payment.TicketId` như một "cột trần chờ bảng" — **nay cấu hình FK đó đã nối
   sẵn** (`Restrict`, trong `AppDbContext.Payment.cs`), cùng migration này sinh luôn bảng `Tickets`.
   Xem `docs/28-csdl-ve-dien-tu.md` §5.

## Đã soát file migration sinh ra — 10/10/2026

File `Migrations/<mốc-thời-gian>_Sprint3_Payments_Vouchers_Tickets.cs` (+ `.Designer.cs`, và
`AppDbContextModelSnapshot.cs` bị sửa). Hai phép kiểm chạy được **không cần CSDL thật**:

| Phép kiểm | Lệnh | Kết quả |
|---|---|---|
| Migration bắt trọn model | `dotnet ef migrations has-pending-model-changes --no-build` | ✅ "No changes have been made to the model since the last migration" |
| Không sinh cột `xmin` | `dotnet ef migrations script --no-build -o /tmp/sprint3.sql` rồi `grep -c xmin` | ✅ `0` (trên 556 dòng SQL) |

Đối chiếu checklist ở trên bằng **SQL sinh ra**, không phải bằng file `.cs`:

| Hạng mục | Trong SQL |
|---|---|
| 2 bảng `Vouchers`, `VoucherUsages` (+ `Tickets`, `Payments` cùng lệnh) | ✅ 4 `CREATE TABLE` |
| `Code` varchar(20), `Name` varchar(200) NOT NULL | ✅ |
| `Status` varchar(20) `DEFAULT 'Active'`; `DiscountType` varchar(20) **không** default | ✅ đúng cả hai vế |
| 5 cột tiền `numeric(12,2)` | ✅ `DiscountValue`, `MinOrderValue`, `MaxDiscount`, `OrderAmount`, `DiscountAmount` |
| `UsedCount` `DEFAULT 0`; `RouteId` nullable | ✅ |
| `PaymentCode` varchar(64) NOT NULL + UNIQUE | ✅ |
| 3 FK `ON DELETE RESTRICT`, không `CASCADE` nào | ✅ `Vouchers→Routes`, `VoucherUsages→Vouchers`, `VoucherUsages→Users` |
| 6 index | ✅ `IX_Vouchers_Code`(unique), `IX_Vouchers_Status_ValidFrom_ValidUntil`, `IX_Vouchers_RouteId`, `IX_VoucherUsages_PaymentCode`(unique), `IX_VoucherUsages_VoucherId_CreatedAt`, `IX_VoucherUsages_UserId` |
| Mọi mốc thời gian là `timestamp with time zone` | ✅ không có `timestamp` trần |
| `UpdatedAt` có ở `Vouchers`, không ở `VoucherUsages`; không `IsDeleted` | ✅ |
| `Down()` xoá theo thứ tự ngược | ✅ `Payments` → `VoucherUsages` → `Tickets` → `Vouchers` |

Bảng `Tickets` và `Payments` soát theo checklist riêng ở `docs/28-csdl-ve-dien-tu.md` — cùng đạt.

⚠️ **Một chỗ đã phải sửa trước khi commit:** tên chỉ mục chống trùng ghế. A6 viết
`IX_Tickets_TripId_SeatId_Active` trong đoạn DDL mẫu, nhưng EF mặc định đặt tên theo cột nên lần sinh
đầu ra `IX_Tickets_TripId_SeatId`. Chốt 10/10/2026: **ghim tên theo A6** bằng
`.HasDatabaseName("IX_Tickets_TripId_SeatId_Active")` trong `AppDbContext.Ticket.cs`, để A6 = tài liệu =
đối tượng thật trong CSDL là một.

Đã **xoá migration cũ và sinh lại** (không sửa tay file EF sinh — giữ lối của cả repo). Bằng chứng
việc sinh lại không kéo theo thay đổi nào khác: `diff` hai bản SQL chỉ ra **đúng 2 dòng** khác nhau —
tên chỉ mục, và mốc thời gian trong bảng `__EFMigrationsHistory`:

```
536c536
< CREATE UNIQUE INDEX "IX_Tickets_TripId_SeatId" ON "Tickets" ("TripId", "SeatId") WHERE "Status" IN ('Held', 'Paid');
---
> CREATE UNIQUE INDEX "IX_Tickets_TripId_SeatId_Active" ON "Tickets" ("TripId", "SeatId") WHERE "Status" IN ('Held', 'Paid');
```
