# CSDL sơ đồ ghế & giữ chỗ — Sprint 3 đã migrate

> Viết cho các task Sprint 3 đang chờ bảng: **Hiếu** (API lấy sơ đồ ghế theo chuyến, API cấu hình
> sơ đồ ghế), **Kiên** (API giữ ghế tạm + chống trùng ghế), **Thịnh/Băng/Hạnh** (màn hình chọn ghế,
> màn cấu hình sơ đồ ghế).
> Migration: `Sprint3_Seats_SeatLayouts_SeatHolds` + `Sprint3_SeatLayouts_CauHinhSoDoGhe` +
> `Sprint3_SeatHoldLogs` — Vàng Thị Dăm.

## 1. Bốn bảng

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

### `SeatHoldLogs` — nhật ký vòng đời của một lượt giữ ghế

Dựng ở migration thứ ba — `Sprint3_SeatHoldLogs` — cho task *"Migrate bảng SeatHoldLogs + job quét
hold hết hạn"* (dòng 14 Sprint 3, Vàng Thị Dăm).

| Cột | Kiểu | Ghi chú |
|---|---|---|
| `Id` | uuid | khoá chính |
| `SeatHoldId` | uuid | FK → `SeatHolds`, `RESTRICT` |
| `UserId` | uuid | FK → `Users`, `RESTRICT` — **chép lại** từ lượt giữ, không nullable |
| `SessionCode` | varchar(64) | **chép lại** từ lượt giữ |
| `Action` | varchar(20) | `Held` / `Extended` / `Expired` / `Released` / `Confirmed` |
| `CreatedAt` | timestamptz | thời điểm sự kiện — bảng chỉ ghi thêm nên **không** có `UpdatedAt` (A4) |

**Vì sao cần bảng này khi `SeatHolds.Status` đã có:** cột `Status` chỉ giữ trạng thái CUỐI. Câu hỏi
của nghiệp vụ là quá trình — "tài khoản này đã để hết hạn giữ chỗ bao nhiêu lần", "lượt giữ này được
gia hạn lúc nào". Ghi đè lên một cột thì trả lời được trạng thái hiện tại, không trả lời được lịch sử.

**Vì sao `UserId` và `SessionCode` chép lại thay vì suy ra qua khoá ngoại:** để câu hỏi *"Ghi log và
cảnh báo khi một tài khoản giữ chỗ quá nhiều lần"* (dòng 19) đếm thẳng trên bảng này theo chỉ mục
`(UserId, CreatedAt)`, và tra vết trọn một phiên đọc thẳng bảng này — không phải join sang `SeatHolds`
mỗi lần. Cùng lối `AuditLogs` ghi lại người thực hiện tại thời điểm sự kiện.

🔴 **Unique index `(SeatHoldId, Action)`.** Mỗi hành động của một lượt giữ xảy ra **đúng một lần**,
nên cặp đó là unique. Đây không phải ràng buộc cho đẹp:

```sql
CREATE UNIQUE INDEX "IX_SeatHoldLogs_SeatHoldId_Action" ON "SeatHoldLogs" ("SeatHoldId", "Action");
```

- **Chống ghi trùng khi hai bản app cùng chạy job quét.** Cả hai cùng đọc một lượt giữ quá hạn rồi
  cùng ghi nhật ký. Bên `SeatHolds` chuyện đó vô hại (hai bản ghi cùng một giá trị `Status`), nhưng ở
  bảng chỉ-ghi-thêm thì nó thành hai dòng cho một sự kiện, và câu "để hết hạn bao nhiêu lần" trả lời
  sai gấp đôi. Bản thua đâm vào unique là `SaveChanges` ném `DbUpdateException`, cả lượt quét của nó
  rollback; lượt sau không còn gì để làm vì bản thắng đã lật xong.
- **Là chốt CSDL cho luật "gia hạn tối đa 1 lần"** của US 3 (API gia hạn là task của Trần Trung Hiếu):
  dòng `Extended` thứ hai không lọt được xuống bảng.

⚠️ Chốt này **chỉ tồn tại ở tầng PostgreSQL**. Bộ test chạy trên provider InMemory, mà InMemory không
cưỡng chế unique index — nên `SeatHoldExpiryServiceTests` xanh KHÔNG có nghĩa chốt đó đã được kiểm.

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

## 4. Job quét hold hết hạn — nhả ghế và ghi nhật ký

`SeatHoldExpiryBackgroundService` (hosted service) + `SeatHoldExpiryService` (phần ruột, scoped) —
task *"Migrate bảng SeatHoldLogs + job quét hold hết hạn"* (Vàng Thị Dăm).

**Việc job làm mỗi lượt:** tìm mọi lượt giữ còn `Status = 'Holding'` mà `ExpiresAt < now`, lật sang
`Expired`, đặt `UpdatedAt = now`, và ghi **một dòng `SeatHoldLogs`** (`Action = 'Expired'`) cho mỗi
lượt vừa lật. Không xoá dòng nào — ghế trống trở lại là nhờ partial unique index ở §3 chỉ chặn
`Holding`.

| Chốt | Giá trị | Vì sao |
|---|---|---|
| Nhịp quét | **1 phút** | Ghế bị chặn là ghế người khác không đặt được. Nhịp 15 phút của job vé tháng biến lượt giữ 10 phút thành chặn ghế 25 phút. |
| Điều kiện hạn | `ExpiresAt < now` (nghiêm ngặt) | Đúng mốc `ExpiresAt` lượt giữ **vẫn còn** hiệu lực — US 3 hứa "giữ 10 phút", nhả sớm là rút chỗ trong lúc khách đang trả tiền. |
| Lượt chạy đầu | Ngay khi khởi động | App restart sau khi đã qua `ExpiresAt` của một loạt lượt giữ: chờ hết một nhịp mới quét thì ghế bị chặn thêm đúng bằng nhịp đó. |

Nhịp và điều kiện hạn là **hằng số trong code**, cố ý không mở ra cấu hình — cùng lý do đã ghi ở
`MonthlyPassExpiryBackgroundService`: đây không phải tham số của story nào, và một khoá cấu hình gõ
sai sẽ làm app chết ngay lúc khởi động.

## 5. Chỉ mục đã đánh — dùng đúng cái có sẵn

| Truy vấn | Chỉ mục |
|---|---|
| Job quét hold hết hạn (`Holding` + `ExpiresAt < now`) | `IX_SeatHolds_ExpiresAt` |
| Tra trạng thái / gia hạn / đếm ngược theo mã phiên | `IX_SeatHolds_SessionCode` |
| Đếm số lượt giữ của một tài khoản (task log & cảnh báo) | `IX_SeatHolds_UserId_CreatedAt` |
| Ghế của một chuyến | `IX_SeatHolds_TripId_SeatId` (cột đầu) |
| Sơ đồ ghế của một xe | `IX_Seats_BusId_SeatNumber`, `IX_Seats_SeatLayoutId` |
| Chống ghi trùng nhật ký + chốt "gia hạn tối đa 1 lần" | `IX_SeatHoldLogs_SeatHoldId_Action` (unique) |
| Đếm số lượt hết hạn / nhả ghế của một tài khoản | `IX_SeatHoldLogs_UserId_CreatedAt` |

## 6. Việc còn lại của người khác

- **Hiếu** — hai endpoint chưa có trong `docs/api-contract.md`: `GET` sơ đồ ghế theo chuyến (US 2) và
  bộ CRUD cấu hình sơ đồ ghế. Sửa hợp đồng trước rồi báo FE (⛔5), đừng code trước. Khi viết API tạo
  sơ đồ: kiểm tra `TotalSeats = số tầng × số hàng × số cột` và `RowsPerFloor ≤ 26` (trần mã ghế),
  dùng lại đúng ba quy cách ở §2.
- **Kiên** — API giữ ghế: nhớ `SessionCode` sinh ở tầng service, và bắt lỗi unique để trả 409.
  Ghi thêm **một dòng `SeatHoldLogs`** cho mỗi lượt giữ vừa tạo (`Action = 'Held'`) và mỗi lượt khách
  chủ động nhả (`Action = 'Released'`) — bảng nhật ký chỉ có giá trị nếu mọi mốc của vòng đời đều
  được ghi, không chỉ mốc hết hạn do job ghi.
- **Hiếu** — API gia hạn giữ chỗ: ghi `SeatHoldLogs` với `Action = 'Extended'`. Nhớ luật "tối đa 1
  lần" đã có chốt ở tầng CSDL (unique index `(SeatHoldId, Action)`, §1) — nhưng cứ kiểm ở tầng
  service để trả **400/409 có thông báo**, đừng để khách nhận lỗi unique thô.
- **Hạnh / FE** — màn cấu hình sơ đồ ghế đang đánh số hàng/cột **từ 0** (`rowLetter(rowIndex)` với
  `rowIndex = 0` ra `"A"`), CSDL lưu **từ 1**. Hai bên phải chốt một chiều trong `api-contract.md`
  trước khi nối API thật, không thì ghế VIP tô lệch một hàng.
- **Hoàng** — nếu muốn console của `SmartBus.Seed` in thêm hai dòng "Sơ đồ ghế / Ghế" thì thêm vào
  `SmartBus.Seed/Program.cs` (file của Hoàng): `SeedResult` đã có sẵn `SeatLayoutsCreated`,
  `SeatsCreated`.
- **Ai cần thêm cột** — nhắn Dăm, không tự sửa `Migrations/*`, `ModelSnapshot.cs`, `Entities/`.

## 7. Việc cần báo nhóm (không phải việc code)

A9 của `docs/03-quy-uoc.md` đang liệt kê **20 bảng**, chưa có `SeatLayouts`, `SeatHolds` và
`SeatHoldLogs`. Ba bảng này là chủ trương có sẵn của bảng phân công Sprint 3 (dòng 5 — "Migrate bảng
Seats, SeatHolds, SeatLayouts"; dòng 14 — "Migrate bảng SeatHoldLogs"), nên chỉ cần nhóm chốt bổ
sung 3 dòng vào A9 cho tài liệu khớp CSDL. Không phải việc phải xin phép trước: bảng phân công đã
giao tên bảng, việc ở đây là cập nhật tài liệu cho khớp.

Ngoài ra Sprint 3 hiện có **ba** migration (`Sprint3_Seats_SeatLayouts_SeatHolds`,
`Sprint3_SeatLayouts_CauHinhSoDoGhe` và `Sprint3_SeatHoldLogs`) trong khi quy ước **A7** ghi "mỗi
sprint một migration". Thực tế Sprint 2 đã có 5 migration, mỗi task một cái — nên đây là chỗ quy ước
A7 cần được nhóm sửa lại cho khớp lệ thực tế ("mỗi task một migration"), chứ không phải chuyện phải
gộp migration.

**Trùng việc cần chốt với Kiên:** dòng 14 giao Dăm *"Migrate bảng SeatHoldLogs + job quét hold hết
hạn"*, còn dòng 16 giao Kiên *"BackgroundService tự động giải phóng ghế hết hạn giữ chỗ"* — hai dòng
nói về cùng một job. Bản đã làm ở §4 là bản của dòng 14 (Dăm). Nếu nhóm muốn job nằm ở phần của Kiên
thì chuyển nguyên cặp `SeatHoldExpiryBackgroundService` + `SeatHoldExpiryService`, đừng viết thêm một
job thứ hai quét cùng bảng.
