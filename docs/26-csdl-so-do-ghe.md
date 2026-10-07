# CSDL sơ đồ ghế & giữ chỗ — Sprint 3 đã migrate

> Viết cho các task Sprint 3 đang chờ bảng: **Hiếu** (API lấy sơ đồ ghế theo chuyến, API cấu hình
> sơ đồ ghế), **Kiên** (API giữ ghế tạm + chống trùng ghế), **Thịnh/Băng/Hạnh** (màn hình chọn ghế,
> màn cấu hình sơ đồ ghế).
> Migration: `Sprint3_Seats_SeatLayouts_SeatHolds` + `Sprint3_SeatLayouts_CauHinhSoDoGhe` — Vàng Thị Dăm.

## 1. Ba bảng

### `SeatLayouts` — sơ đồ ghế của một LOẠI XE (mẫu)

| Cột | Kiểu | Ghi chú |
|---|---|---|
| `Id` | uuid | khoá chính |
| `BusType` | varchar(50) | **unique** — khớp thẳng `Buses.BusType` |
| `NumberOfFloors` | integer | 1 hoặc 2 |
| `RowsPerFloor` | integer | số hàng ghế trên **mỗi** tầng — mọi tầng dùng chung con số này |
| `ColumnsPerRow` | integer | số ghế tối đa trên một hàng |
| `TotalSeats` | integer | tổng số ghế của sơ đồ |
| `VipSeatPositions` | varchar(2000) | danh sách vị trí ghế VIP, `"tầng-hàng-cột"` ngăn bằng `';'`; rỗng = không có VIP |
| `CreatedAt` / `UpdatedAt` | timestamptz | `UpdatedAt` nullable |

Mỗi loại xe đúng **một** sơ đồ. Đây là mẫu, không phải ghế thật. Bốn cột đầu + `VipSeatPositions`
mô tả trọn hình dạng sơ đồ — dựng lại được toàn bộ lưới ghế mà không cần đọc bảng `Seats`.

**Bất biến:** `TotalSeats = NumberOfFloors × RowsPerFloor × ColumnsPerRow` (lưới không khuyết ô).
CSDL **không** đặt check constraint — kiểm tra ở tầng service, cùng lối các cột nghiệp vụ khác.
Hệ quả: loại xe có số ghế không phân tích được thành tích (29, 31, 47…) chưa biểu diễn được bằng
sơ đồ; cần thì phải bổ sung cột "ô khuyết" chứ đừng nới bất biến này.

### `Seats` — ghế thật của từng xe (Sprint 2 dựng, Sprint 3 mở rộng)

Cột Sprint 2 giữ nguyên: `Id`, `BusId`, `SeatNumber` (unique theo `(BusId, SeatNumber)`), `CreatedAt`.

Cột **thêm ở Sprint 3**:

| Cột | Kiểu | Ghi chú |
|---|---|---|
| `SeatLayoutId` | uuid **NOT NULL** | FK → `SeatLayouts`, `RESTRICT` |
| `Floor` | integer | mặc định 1 |
| `RowIndex` | integer | hàng trong tầng, 1 là hàng đầu |
| `ColumnIndex` | integer | cột trong hàng, 1 là cột trái cùng |
| `SeatType` | varchar(20) | `Standard` / `Vip`, mặc định `Standard` |

`(Floor, RowIndex, ColumnIndex)` là toạ độ để vẽ sơ đồ. `SeatType` là thuộc tính của **từng ghế** —
biết "có 6 ghế VIP" mà không biết sáu ghế nào thì không tô màu được.

⚠️ **Thứ tự đúng là: có `SeatLayout` trước, rồi mới sinh `Seat` cho xe.** `SeatLayoutId` NOT NULL
là ràng buộc cố ý — ghế không thuộc sơ đồ nào thì không có toạ độ, tức là không dùng được cho US 2.
Xe chưa có sơ đồ cho loại của nó thì chưa sinh được ghế; đó là trạng thái dữ liệu hợp lệ, không
phải lỗi.

### `SeatHolds` — một lượt giữ ghế tạm thời (US 3)

| Cột | Kiểu | Ghi chú |
|---|---|---|
| `Id` | uuid | khoá chính |
| `TripId` | uuid | FK → `Trips`, `RESTRICT` |
| `SeatId` | uuid | FK → `Seats`, `RESTRICT` |
| `UserId` | uuid | FK → `Users`, `RESTRICT` — bắt buộc đăng nhập |
| `SessionCode` | varchar(64) | mã phiên, **không unique** |
| `Status` | varchar(20) | `Holding` / `Confirmed` / `Expired` / `Released` |
| `ExpiresAt` | timestamptz | hạn giữ chỗ (10 phút) |
| `CreatedAt` / `UpdatedAt` | timestamptz | `UpdatedAt` nullable |

**Một phiên giữ nhiều ghế** → khách chọn 3 ghế là **3 dòng** cùng `SessionCode`. Đó cũng là lý do
`ExpiresAt` nằm trên từng dòng chứ không tách bảng "phiên" riêng: gia hạn là cập nhật cả nhóm theo
mã phiên.

Ý nghĩa `Status`:

- `Holding` — đang giữ, ghế bị chặn tới `ExpiresAt`. **Chỉ trạng thái này mới chặn ghế.**
- `Confirmed` — đã chốt thành vé sau khi thanh toán xong.
- `Expired` — job quét của BackgroundService chuyển sang khi quá `ExpiresAt`.
- `Released` — khách chủ động nhả trước hạn (huỷ thao tác).

## 2. Quy cách sơ đồ ghế — chốt ở dữ liệu seed (dòng 9 Sprint 3)

Ba quy ước này là **hợp đồng giữa CSDL, API và màn hình chọn ghế**. Sửa số trong
`backend/SmartBus.Api/Seed/SampleDataSeeder.cs` được, nhưng sửa quy cách thì phải báo cả nhóm.

**a. Lưới ghế.** Số ghế của một loại xe = `NumberOfFloors × RowsPerFloor × ColumnsPerRow`; lưới
không khuyết ô và mọi tầng dùng chung số hàng/số cột. Hàng và cột đếm **từ 1** (1 = hàng đầu, 1 =
cột trái cùng) — đúng cột `Seats.RowIndex` / `Seats.ColumnIndex`.

**b. Khoá vị trí một ghế.** `"<tầng>-<hàng>-<cột>"` — chính là `VipSeatPositions` và cũng là khoá ô
ghế của màn chọn ghế. Ví dụ ghế hàng đầu cột trái tầng 1: `"1-1-1"`.

**c. Mã ghế hiển thị.** Hàng đánh chữ A, B, C… cột đánh số 1, 2, 3… → `"A1"`, `"I5"`. Xe **hai
tầng** thêm tiền tố tầng: `"T1-A1"`, `"T2-A1"` — không thì ghế tầng 1 và tầng 2 trùng mã, mà
`(BusId, SeatNumber)` là unique theo A6. Quy cách này đủ tới 26 hàng (trần màn cấu hình: 13 hàng/tầng).

Dữ liệu mẫu đã seed (chạy `SmartBus.Seed` là có):

| Loại xe | Sơ đồ | Ghế VIP |
|---|---|---|
| `Xe buýt 45 chỗ` | 1 tầng · 9 hàng × 5 cột = 45 | 4 ghế: hai hàng đầu, hai cột trái (`1-1-1`, `1-1-2`, `1-2-1`, `1-2-2`) |
| `Xe buýt 2 tầng 60 chỗ` | 2 tầng · 6 hàng × 5 cột = 60 | 5 ghế: trọn hàng đầu **tầng 1** (`1-1-1`…`1-1-5`) |

Mỗi xe trong `XeMau` được sinh đủ dàn ghế theo sơ đồ của loại xe đó, **số ghế = `Buses.Capacity`**.
Xe có loại chưa có sơ đồ thì bỏ qua — trạng thái hợp lệ, không phải lỗi.

⚠️ **Xe bảo dưỡng `51B-345.67` đổi từ "Xe buýt 29 chỗ" (29) sang "Xe buýt 2 tầng 60 chỗ" (60).**
Lý do: 29 là số nguyên tố, không có lưới chữ nhật nào ra 29 ghế, mà sơ đồ là lưới. Đổi để vừa seed
được vừa có sẵn ca "số tầng = 2" cho màn cấu hình. Ai cần loại 29 chỗ thì đọc mục bất biến ở §1.

## 3. Chống hai khách giữ cùng một ghế — ở tầng CSDL

```sql
CREATE UNIQUE INDEX "IX_SeatHolds_TripId_SeatId" ON "SeatHolds" ("TripId", "SeatId")
    WHERE "Status" = 'Holding';
```

Đây là bản partial unique index mà quy ước **A6** đã chốt cho `Tickets`, áp đúng tinh thần đó cho
`SeatHolds`. Hệ quả cho tầng service:

- Hai request `INSERT` cùng `(TripId, SeatId)` mà đều `Holding` → **request thứ hai nhận lỗi unique
  của PostgreSQL**. Bắt `DbUpdateException` và trả **409** (quy ước D2: xung đột trùng ghế), không
  trả 500.
- Kiểm tra "ghế còn trống" ở tầng service **không đủ** — hai request cùng đọc rồi cùng ghi là
  chuyện xảy ra được. Index này mới là thứ bảo đảm.
- Hold đã `Expired` / `Released` / `Confirmed` **không** chặn ghế: khách sau giữ lại được bình
  thường, không cần xoá dòng cũ.

## 4. Chỉ mục đã đánh — dùng đúng cái có sẵn

| Truy vấn | Chỉ mục |
|---|---|
| Job quét hold hết hạn (`Holding` + `ExpiresAt < now`) | `IX_SeatHolds_ExpiresAt` |
| Tra trạng thái / gia hạn / đếm ngược theo mã phiên | `IX_SeatHolds_SessionCode` |
| Đếm số lượt giữ của một tài khoản (task log & cảnh báo) | `IX_SeatHolds_UserId_CreatedAt` |
| Ghế của một chuyến | `IX_SeatHolds_TripId_SeatId` (cột đầu) |
| Sơ đồ ghế của một xe | `IX_Seats_BusId_SeatNumber`, `IX_Seats_SeatLayoutId` |

## 5. Việc còn lại của người khác

- **Hiếu** — hai endpoint chưa có trong `docs/api-contract.md`: `GET` sơ đồ ghế theo chuyến (US 2) và
  bộ CRUD cấu hình sơ đồ ghế. Sửa hợp đồng trước rồi báo FE (⛔5), đừng code trước. Khi viết API tạo
  sơ đồ: kiểm tra `TotalSeats = số tầng × số hàng × số cột` và `RowsPerFloor ≤ 26` (trần mã ghế),
  dùng lại đúng ba quy cách ở §2.
- **Kiên** — API giữ ghế: nhớ `SessionCode` sinh ở tầng service, và bắt lỗi unique để trả 409.
- **Hạnh / FE** — màn cấu hình sơ đồ ghế đang đánh số hàng/cột **từ 0** (`rowLetter(rowIndex)` với
  `rowIndex = 0` ra `"A"`), CSDL lưu **từ 1**. Hai bên phải chốt một chiều trong `api-contract.md`
  trước khi nối API thật, không thì ghế VIP tô lệch một hàng.
- **Hoàng** — nếu muốn console của `SmartBus.Seed` in thêm hai dòng "Sơ đồ ghế / Ghế" thì thêm vào
  `SmartBus.Seed/Program.cs` (file của Hoàng): `SeedResult` đã có sẵn `SeatLayoutsCreated`,
  `SeatsCreated`.
- **Ai cần thêm cột** — nhắn Dăm, không tự sửa `Migrations/*`, `ModelSnapshot.cs`, `Entities/`.

## 6. Việc cần báo nhóm (không phải việc code)

A9 của `docs/03-quy-uoc.md` đang liệt kê **20 bảng**, chưa có `SeatLayouts` và `SeatHolds`. Hai
bảng này là chủ trương có sẵn của bảng phân công Sprint 3 (dòng 5 — "Migrate bảng Seats, SeatHolds,
SeatLayouts"), nên chỉ cần nhóm chốt bổ sung 2 dòng vào A9 cho tài liệu khớp CSDL.

Ngoài ra Sprint 3 hiện có **hai** migration (`Sprint3_Seats_SeatLayouts_SeatHolds` và
`Sprint3_SeatLayouts_CauHinhSoDoGhe`) trong khi quy ước **A7** ghi "mỗi sprint một migration". Thực
tế Sprint 2 đã có 5 migration, mỗi task một cái — nên đây là chỗ quy ước A7 cần được nhóm sửa lại
cho khớp lệ thực tế ("mỗi task một migration"), chứ không phải chuyện phải gộp migration.
