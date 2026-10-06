# CSDL sơ đồ ghế & giữ chỗ — Sprint 3 đã migrate

> Viết cho các task Sprint 3 đang chờ bảng: **Hiếu** (API lấy sơ đồ ghế theo chuyến),
> **Kiên** (API giữ ghế tạm + chống trùng ghế), **Thịnh/Băng/Hạnh** (màn hình chọn ghế).
> Migration: `Sprint3_Seats_SeatLayouts_SeatHolds` — Vàng Thị Dăm.

## 1. Ba bảng

### `SeatLayouts` — sơ đồ ghế của một LOẠI XE (mẫu)

| Cột | Kiểu | Ghi chú |
|---|---|---|
| `Id` | uuid | khoá chính |
| `BusType` | varchar(50) | **unique** — khớp thẳng `Buses.BusType` |
| `NumberOfFloors` | integer | 1 hoặc 2 |
| `TotalSeats` | integer | tổng số ghế của sơ đồ |
| `CreatedAt` / `UpdatedAt` | timestamptz | `UpdatedAt` nullable |

Mỗi loại xe đúng **một** sơ đồ. Đây là mẫu, không phải ghế thật.

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

## 2. Chống hai khách giữ cùng một ghế — ở tầng CSDL

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

## 3. Chỉ mục đã đánh — dùng đúng cái có sẵn

| Truy vấn | Chỉ mục |
|---|---|
| Job quét hold hết hạn (`Holding` + `ExpiresAt < now`) | `IX_SeatHolds_ExpiresAt` |
| Tra trạng thái / gia hạn / đếm ngược theo mã phiên | `IX_SeatHolds_SessionCode` |
| Đếm số lượt giữ của một tài khoản (task log & cảnh báo) | `IX_SeatHolds_UserId_CreatedAt` |
| Ghế của một chuyến | `IX_SeatHolds_TripId_SeatId` (cột đầu) |
| Sơ đồ ghế của một xe | `IX_Seats_BusId_SeatNumber`, `IX_Seats_SeatLayoutId` |

## 4. Việc còn lại của người khác

- **Hiếu** — `GET` sơ đồ ghế theo chuyến chưa có trong `docs/api-contract.md`. Muốn làm thì sửa
  hợp đồng trước rồi báo FE (⛔5), đừng code trước.
- **Kiên** — API giữ ghế: nhớ `SessionCode` sinh ở tầng service, và bắt lỗi unique để trả 409.
- **Ai cần thêm cột** — nhắn Dăm, không tự sửa `Migrations/*`, `ModelSnapshot.cs`, `Entities/`.

## 5. Việc cần báo nhóm (không phải việc code)

A9 của `docs/03-quy-uoc.md` đang liệt kê **20 bảng**, chưa có `SeatLayouts` và `SeatHolds`. Hai
bảng này là chủ trương có sẵn của bảng phân công Sprint 3 (dòng 5 — "Migrate bảng Seats, SeatHolds,
SeatLayouts"), nên chỉ cần nhóm chốt bổ sung 2 dòng vào A9 cho tài liệu khớp CSDL.
