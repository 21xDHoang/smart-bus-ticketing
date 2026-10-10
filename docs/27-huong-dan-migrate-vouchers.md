# Việc của Dăm — migrate hai bảng Vouchers + VoucherUsages (US 18, Sprint 3)

> Ghi chú này do **Nguyễn Duy Kiên** viết khi làm task **dòng 52 — "API kiểm tra và áp dụng voucher
> vào đơn hàng"** (đã gộp luôn dòng 53 — "Validate điều kiện voucher" của Hoàng).
> Task **dòng 50 — "Migrate bảng `Vouchers`, `VoucherUsages`"** (Vàng Thị Dăm) là việc còn lại duy
> nhất để nhánh này chạy được trên CSDL thật.
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

## Việc cần làm — đúng MỘT lệnh

```bash
cd backend/SmartBus.Api
dotnet ef migrations add Sprint3_Vouchers_VoucherUsages
```

(`dotnet ef` chưa cài thì chạy `dotnet tool restore` trước.)

Rồi soát file migration vừa sinh theo checklist dưới, commit trên nhánh riêng + PR như các migration
trước. **Không cần** sửa entity, không cần viết API, không cần đụng `api-contract.md`.

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
- [ ] ⚠️ **Không có cột `xmin` nào được sinh ra.** `Vouchers` có khai
      `e.Property<uint>("xmin").HasColumnName("xmin").IsRowVersion();` — đây là **cột hệ thống** của
      PostgreSQL dùng làm concurrency token, đã có sẵn trong mọi bảng, **không phải cột mới**.
      Npgsql nhận ra tên `xmin` và bỏ qua khi sinh `CreateTable`; nếu file migration **có** dòng
      khai cột `xmin` (hoặc `AlterColumn`/`AddColumn` tên đó) thì **đừng commit** — báo Kiên ngay,
      vì như vậy là cấu hình token sai và cả chốt chống tiêu thụ quá `Quantity` sẽ không chạy.

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
| 2 | Unique index trên `upper("Code")` | ⬜ cân nhắc khi sinh migration |
| 3 | Kiểu `citext` | ❌ cần extension PostgreSQL mà dự án chưa dùng ở đâu |

Đường (1) được chọn vì bản nháp FE (`frontend/src/api/voucherApi.ts`) **cũng** đã chuẩn hoá chữ HOA ở
cả create lẫn update, nên toàn hệ thống giữ được một hình dạng chuẩn duy nhất. Cái mất: chốt này
nằm ở **tầng ứng dụng**, nên một script SQL ghi thẳng vào bảng có thể tạo ra `summer10` song song với
`SUMMER10` mà index không chặn.

**Nếu Dăm muốn đóng hẳn khe đó**: viết thêm trong migration một dòng `CREATE UNIQUE INDEX` trên
`upper("Code")`, và thêm một mục vào checklist trên. Đây là **đối tượng schema không có test nào
phủ** (InMemory không dựng index) nên nếu chọn thì nhớ ghi lại trong PR để nhóm biết. Chọn đường nào
cũng được — nhưng phải là quyết định có ý thức, không phải mặc định.

## Nhắc nhóm (không phải việc code)

1. `VoucherUsages` là bảng **thứ 22** (và `Vouchers` là bảng 21, sau `FeedbackReplies`) — mục A9 của
   `docs/03-quy-uoc.md` đang liệt kê 20 bảng. Bảng tính Sprint 3 dòng 50 đã ghi rõ "Migrate bảng
   `Vouchers`, `VoucherUsages`" nên hai bảng là chủ trương có sẵn; nhóm chỉ cần chốt bổ sung vào A9.
2. **`Payments` vẫn chưa migrate** (Dăm còn nợ `Sprint3_Payments`). Vì vậy `VoucherUsages.PaymentCode`
   cố ý là **cột trần, không phải FK** sang `Payments` — đặt FK bây giờ là buộc migration `Vouchers`
   phải chạy SAU migration `Payments`, một ràng buộc thứ tự giữa hai việc của hai người, đổi lấy đúng
   một cột. Repo đã có sẵn lối này: `Payment.TicketId` cũng là cột trần chờ bảng `Tickets`. Nối FK
   bằng một migration sau, khi `Payments` đã có bảng.
