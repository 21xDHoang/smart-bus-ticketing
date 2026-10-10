# Báo cáo kiểm thử chéo — luồng giữ chỗ (giữ · tra · gia hạn · nhả)

| | |
|---|---|
| **Người thực hiện** | Giàng A Vàng (Kiểm thử) |
| **Task** | Sprint 3, story 3 — *Kiểm thử chéo luồng giữ chỗ trên môi trường online* (dòng r26) |
| **Ngày** | 11/10/2026 |
| **Mốc so sánh** | `main` @ `5f03357` |
| **Trạng thái** | ⚠️ Chỉ hoàn thành phần kiểm thử **không cần môi trường online** — xem mục 1 |

> Mã lỗi trong báo cáo này (**G1**…) là cục bộ cho báo cáo này, **khác** bộ `C1`–`C6` của
> `docs/bao-cao-kiem-thu-cheo-lich-trinh-sinh-chuyen.md`, bộ `P1`–`P9` của
> `docs/bao-cao-kiem-thu-cheo-phan-cong.md`, bộ `N1`–`N9` của `docs/bao-cao-kiem-thu-cheo-tra-cuu.md`,
> bộ `V1`–`V7` của `docs/bao-cao-kiem-thu-cheo-ve-thang.md`, bộ `F1`–`F9` của
> `docs/bao-cao-kiem-thu-cheo-phan-anh.md` và bộ `L1`–`L20` của `docs/bao-cao-kiem-thu-sprint-1.md`.
> Bảy bộ không liên tục và không thay thế nhau.
>
> **G7 lặp lại ở báo cáo thứ sáu** (`L20` → `P9` → (báo cáo tra cứu) `N9` → `V7` → `F9` → `G7`). Ghi
> lại là bằng chứng về việc nó vẫn chưa được xử lý, không phải lỗi mới.
>
> **G4 và một phần G1 là hệ quả của cùng một dòng backlog còn "Chưa làm"** (r40, Trần Trung Hiếu).
> Chúng được tách thành hai mục vì cách chúnga lộ ra khác nhau: G1 là *hành vi sai đo được trên API
> đang chạy*, G4 là *nguồn dữ liệu còn thiếu*.

---

## 1. Phạm vi — phần KHÔNG làm được và vì sao

Task yêu cầu kiểm thử **trên môi trường online**. Đây là **báo cáo thứ sáu liên tiếp** gặp đúng một
rào chắn, và tình hình không đổi: nhóm đã có **một nửa** — CSDL PostgreSQL dùng chung trên Supabase
từ 03/10/2026 — nhưng nửa còn lại, **API chạy ngoài máy cá nhân**, vẫn chưa có. "Online" hiện tại
nghĩa là *dữ liệu dùng chung*, không phải *hệ thống chạy chung*.

| Kiểm tra | Story 13 | Story 14 | Story 16 (vé tháng) | Story 24 (phản ánh) | Nay (story 3) |
|---|---|---|---|---|---|
| CSDL dùng chung cho cả nhóm | ❌ mỗi máy một bản | ❌ vẫn vậy | ✅ Supabase | ✅ vẫn có | ✅ vẫn có |
| API chạy ngoài máy cá nhân | ❌ không | ❌ không | ❌ không | ❌ không | ❌ **vẫn không** — tài liệu vẫn ghi `http://localhost:5080` |
| `render.yaml` / `fly.toml` / `Procfile` / `vercel.json` / `docker-compose.yml` | ❌ không | ❌ không | ❌ không | ❌ không | ❌ **vẫn không** |
| Workflow deploy | ❌ chỉ `ci.yml` | ❌ chỉ `ci.yml` | ❌ chỉ `ci.yml` | ❌ chỉ `ci.yml` | ❌ **vẫn chỉ `ci.yml`** — `grep -i deploy .github/workflows/`: 0 kết quả |
| URL online ở đâu đó trong repo | ❌ không | ❌ không | ❌ không | ❌ không | ❌ **không** — grep `onrender\|vercel.app\|netlify.app\|fly.dev\|azurewebsites\|railway.app\|herokuapp` toàn repo: **0 kết quả thật** |
| `appsettings.Production.json` | ❌ không | ❌ không | ❌ không | ❌ không | ❌ **vẫn không** — chỉ có `appsettings.json` |
| Dockerfile backend | ✅ có | ✅ có | ✅ có | ✅ có | ✅ vẫn có — **chưa từng được deploy** |
| Task deploy trong backlog | ❌ không | ❌ không | ❌ không | ❌ không | ❌ **không có dòng nào** |

**Rào chắn trên chính máy này vẫn nguyên:** `backend/SmartBus.Api/appsettings.Development.json`
**không tồn tại**, và chuỗi kết nối Supabase nằm trong tin nhắn ghim của chat nhóm (cố ý — repo
public). Nên ngay cả việc trỏ API ở máy vào CSDL chung cũng không làm được từ đây.

**Hệ quả:** không chạy được một request HTTP thật nào *qua mạng*. Phần đã làm thay thế — như năm báo
cáo trước — là **kiểm thử chéo có bằng chứng chạy lại được**:

1. **Đo trên pipeline HTTP thật** bằng `TestAppFactory` (app thật + EF InMemory), gọi đủ **năm
   endpoint** của hai bề mặt `GET /api/trips/{id}/seats` và `/api/seat-holds`, với **bốn vai trò** và
   trường hợp không token. Số liệu ở mục 2.1.
2. **Chạy được luồng trọn vẹn trong một ca test** — `SeatHoldCrossFlowApiTests`, 12 ca, đã vào
   `backend/SmartBus.Tests/`. Đây là phần **"chéo"** theo đúng nghĩa: năm file test hiện có mỗi file
   khoá MỘT endpoint, không file nào khẳng định được điều mà luồng thật hứa — *giữ ghế xong thì sơ
   đồ phải đổi màu, nhả xong thì sơ đồ phải trả ghế về trống*. Lệnh chạy ở mục 7.
3. Đối chiếu ba phía: mã nguồn frontend ↔ mã nguồn backend ↔ `docs/api-contract.md` +
   `docs/26-csdl-so-do-ghe.md`.

> **Một quan sát về chính môi trường máy này:** lần đầu chạy `npm --prefix frontend run build`,
> build **đỏ** (`src/components/TicketQr.tsx(1,27): error TS2307: Cannot find module 'qrcode.react'`).
> `qrcode.react@^4.2.0` **có** trong `frontend/package.json:18` nhưng chưa được cài ở
> `frontend/node_modules` của máy này. Sau `npm --prefix frontend install` (`added 1 package`) build
> xanh trở lại. Đây **không phải lỗi của repo** — CI chạy `npm ci` nên không gặp — chỉ là
> `node_modules` ở đây cũ hơn `package.json`. Ghi lại để lần sau ai thấy build đỏ y hệt thì biết
> ngay nguyên nhân.

---

## 2. Mốc chuẩn trên `main` @ `5f03357`

| Hạng mục | Lệnh | Kết quả |
|---|---|---|
| Test backend (trước khi thêm ca mới) | `dotnet test backend/SmartBus.Tests/SmartBus.Tests.csproj` | ✅ **1121/1121 pass**, 0 fail, 0 skip, 1 m 54 s |
| Build backend | `dotnet build backend/SmartBus.sln -c Release` | ✅ **0 warning, 0 error** |
| Build frontend | `npm --prefix frontend run build` | ✅ exit 0 — chỉ còn cảnh báo chunk > 500 kB (sau khi `npm install` bù `qrcode.react`, xem mục 1) |
| Lint frontend | `npm --prefix frontend run lint` | ⚠️ **1 cảnh báo** — `StopManagePage.tsx:33` `react(set-state-in-effect)`, đúng cảnh báo cũ, **ngoài** luồng giữ chỗ |
| Rác debug | grep `console.log` / `debugger` / `TODO` / `FIXME` trong `frontend/src` + `backend/SmartBus.Api` | ✅ **0 dòng** |
| Bí mật bị commit | `git ls-files` lọc `.env`, `appsettings.Development.json`, `*.pfx`, `*.pem`, `id_rsa` | ✅ sạch — chỉ có hai file `.example` (`appsettings.Development.json.example`, `frontend/.env.example`) |

### 2.1 Đo trên pipeline HTTP thật — bảng số liệu

Dựng một hành khách, một tuyến **có cấu hình giá phổ thông**, một xe, một sơ đồ ghế và một chuyến
trong CSDL test, rồi gọi đúng những endpoint mà hai bề mặt giữ chỗ phục vụ. **53 lượt gọi HTTP**,
tổng hợp lại:

| Endpoint | Không token | Hành khách | Ghi chú |
|---|---|---|---|
| `GET /api/trips/{id}/seats` | **200** | **200** | Công khai **đúng chủ ý** — khách chọn chỗ trước khi đăng nhập (US 2) |
| `POST /api/seat-holds` | **401** | **201** | Trả `sessionCode`, `status=Holding`, `expiresAt`, `canExtend=true`, `seatNumbers` |
| `GET /api/seat-holds/{code}` | **401** | **200** | Phiên của người khác → **404** (không lộ là phiên có thật) |
| `POST /api/seat-holds/{code}/extend` | **401** | **200** → lần hai **409** | `canExtend` lật `true` → `false` đúng sau lượt đầu |
| `POST /api/seat-holds/{code}/release` | **401** | **200** → gọi lại **200** | Nhả là thao tác idempotent, khác gia hạn |

Bốn vai trò của dự án gọi `POST /api/seat-holds`, **mỗi vai trò một ghế riêng** (dùng chung một ghế
thì lượt đầu 201 rồi ba lượt sau 409 "ghế đang được giữ" — ma trận đo ra bị nhiễm bẩn, đã sửa một
lần trong lúc đo):

| Vai trò | Kết quả |
|---|---|
| `Passenger` | **201 Created** |
| `Driver` | **201 Created** |
| `Manager` | **201 Created** |
| `Admin` | **201 Created** |

Đúng hợp đồng: bốn endpoint của `/seat-holds` là `[Authorize]` **trần, không policy vai trò**
(`api-contract.md`, mục "Giữ chỗ") — RBAC của dự án chỉ có `AdminOnly` / `ManagerOrAbove`, không có
policy `Passenger`, nên "hành khách tự giữ ghế cho mình" được thực thi bằng **`UserId` lấy từ claim
`NameIdentifier`**, không bằng vai trò. Ca `Moi_vai_tro_dang_nhap_deu_giu_duoc_ghe` trong file test
mới khoá đúng lời hứa này.

**Vòng đời đo được, đọc thẳng trên sơ đồ ghế (đây là phần chéo):**

| Mốc | `status` của ghế A1 trên `GET /trips/{id}/seats` |
|---|---|
| Trước khi giữ | `Available` |
| Sau `POST /seat-holds` (201) | **`Held`** |
| Sau `extend` (200) | `Held` |
| Sau `release` (200) | **`Available`** |

Hai lần đổi màu đó là thứ không file test nào hiện có khẳng định được.

**Các nhánh lỗi đã đo (16 ca, tất cả đúng khuôn `{ message, errors }` của quy ước mục 5):**

| Nhánh | Kết quả |
|---|---|
| Body `{}` / `tripId` thiếu | **400** `errors.tripId` = `"Chuyến xe không được để trống"` |
| `seatIds` rỗng / `null` | **400** `errors.seatIds` = `"Danh sách ghế không được để trống"` |
| `seatIds` chứa `Guid.Empty` | **400** `"Danh sách ghế có ghế không hợp lệ"` |
| `seatIds` trùng nhau | **400** `"Danh sách ghế có ghế bị lặp lại"` |
| `seatId` không tồn tại | **400** `"Danh sách ghế có ghế không tồn tại"` |
| Ghế của **xe khác** | **400** `"Danh sách ghế có ghế không thuộc xe của chuyến"` |
| Chuyến không tồn tại / chuyến mồ côi (xe đã xoá) | **404** `"Không tìm thấy chuyến xe"` |
| Chuyến đã huỷ | **409** `"Chuyến đã hủy, không thể giữ ghế"` |
| Chuyến đã chạy xong | **409** `"Chuyến đã hoàn thành, không thể giữ ghế"` |
| Ghế đang có người giữ | **409** `"Ghế A1 đang được giữ cho chuyến này."` |
| Mã phiên không tồn tại (GET / extend / release) | **404** `"Không tìm thấy phiên giữ chỗ"` ×3 |
| Gia hạn lần thứ hai | **409** `"Phiên giữ chỗ đã được gia hạn rồi — mỗi phiên chỉ được gia hạn tối đa 1 lần."` |
| Nhả phiên đã `Confirmed` | **409** `"Phiên giữ chỗ đã chốt thành vé, không thể nhả."` |
| Gia hạn phiên đã `Confirmed` | **409** `"Phiên giữ chỗ đã kết thúc, không thể gia hạn."` |
| Nhả phiên đã `Expired` | **200** — trả đúng `status: "Expired"` |

---

## 3. Danh sách lỗi

Mức độ: **Cao** = sai chức năng hoặc gây hiểu nhầm nghiêm trọng · **TB** = ảnh hưởng trải nghiệm
hoặc rủi ro · **Thấp** = tài liệu, vệ sinh mã.

| # | Lỗi | Mức | Người xử lý |
|---|---|---|---|
| G1 | Ghế **đã bán** (bảng `Tickets`) vẫn trả `Available` và **vẫn giữ được** — `TripSeatMapService` không hỏi `Tickets` | 🔴 Cao | Trần Trung Hiếu |
| G2 | Nửa frontend của story 3 **chưa nối**: không có hàm tạo phiên, màn chọn ghế nhảy thẳng sang thanh toán demo, ba module đếm ngược/modal/nhắc nhở là **code chết** | 🔴 Cao | Nguyễn Đình Băng · Dương Thị Hạnh · Hoàng Văn Thịnh |
| G3 | **Ba comment nói sai** tình trạng backend, và một trong số đó đang là công tắc chặn cả tính năng | 🟠 TB | Nguyễn Đình Băng · Hoàng Văn Thịnh |
| G4 | **Không dòng code nào ghi `Tickets`** và **không gì đặt `SeatHoldStatus.Confirmed`** — trạng thái `Paid` và nhánh 409 "đã chốt thành vé" không có nguồn sinh | 🟠 TB | Trần Trung Hiếu |
| G5 | **Kiểu nullable lệch hai tầng**: backend trả `null`, `seatMapApi.ts` khai `number` | 🟠 TB | Nguyễn Đình Băng |
| G6 | `api-contract.md` **trỏ sai chỗ** về "khe hở luồng đặt vé", và tiền đề của cảnh báo nay đã sai | 🟡 Thấp | Trần Trung Hiếu |
| G7 | `package-lock.json` lạc ở gốc repo (**lần thứ sáu**) | 🟡 Thấp | Phùng Duy Hoàng |

> **Về việc gán người xử lý:** tên người được suy từ bảng phân công Sprint 3
> (`Product_Backlog_Smart_Bus.xlsx`, sheet *Sprint 3*): r3 API sơ đồ ghế = Trần Trung Hiếu, r8 màn
> sơ đồ ghế = Nguyễn Đình Băng, r20 component đếm ngược = Hoàng Văn Thịnh, r21 modal hết hạn + nút
> gia hạn = Nguyễn Đình Băng, r22 tự động nhả ghế = Dương Thị Hạnh, r40 API phát hành vé điện tử =
> Trần Trung Hiếu. `.gitignore` và `.github/` thuộc Phùng Duy Hoàng (mục E1). Cần đối chiếu lại nếu
> phân công đã đổi.

---

### G1 — Ghế đã bán vẫn trả `Available`, và vẫn giữ được 🔴 Cao

**Đo được (không cần suy đoán):** seed **3 dòng `Tickets` `Status = Paid`** cho ba ghế của một
chuyến, **0 dòng `SeatHolds`**, rồi gọi `GET /api/trips/{id}/seats`:

```
A1: status=Available
A2: status=Available
A3: status=Available
```

Và `POST /api/seat-holds` cho chính ghế đã bán đó:

```
HTTP 201 Created
{"sessionCode":"PHIEN-504cc14a","tripId":"d618cbfe-…","seatNumbers":["A1"],
 "status":"Holding","expiresAt":"…","canExtend":true}
```

Tức là **một ghế đã bán cho khách A vẫn được chào cho khách B, và khách B giữ được nó**. Cho tới
khi hết hạn giữ chỗ, cả hai cùng tin mình có ghế.

**Đường đi của lỗi:**

1. `Services/TripSeatMapService.cs:83-91` — trạng thái ghế chỉ suy từ **một** nguồn:
   ```csharp
   var heldSeatIds = await _db.SeatHolds.AsNoTracking()
       .Where(h => h.TripId == tripId && h.Status == SeatHoldStatus.Holding)
       …
   Status = heldSet.Contains(seat.Id) ? StatusHeld : StatusAvailable,
   ```
   **Không có truy vấn `Tickets` nào** trong cả file.
2. `Dtos/Trips/TripSeatMapResponse.cs` — lời giải thích cho việc thiếu nguồn đó:
   > *"Trạng thái "Paid" chưa xuất hiện vì bảng Tickets chưa migrate (US 4, Vàng Thị Dăm)."*
3. **Nhưng bảng `Tickets` ĐÃ được migrate** — `backend/SmartBus.Api/Migrations/20261010105248_Sprint3_Payments_Vouchers_Tickets.cs`,
   commit `6da838c` ngày **10/10/2026** của Vàng Thị Dăm (backlog r38 *"Migrate bảng Tickets,
   TicketQRCodes"*). Tiền đề của cả `TripSeatMapResponse.cs` và `docs/api-contract.md` (mục
   `GET /trips/{id}/seats`: *"⚠️ Bảng `Tickets` chưa được migrate … nên hôm nay chưa có ghế nào trả
   về `Paid`"*) **đã sai từ hôm qua**.
4. `docs/api-contract.md:1447-1450` tự nhận đây là khe hở có thật: *"⚠️ Ghế đã `Confirmed` **không**
   chặn lượt giữ mới … và bảng `Tickets` chưa migrate — khe hở có thật của luồng đặt vé"*.

**Vì sao là lỗi chứ không phải "chờ API":** sơ đồ ghế là **thứ duy nhất** khách nhìn để biết ghế nào
còn. Sai ở đây không phải lỗi hiển thị — nó là lỗi bán: cùng một ghế được hứa cho hai người, và
không có tầng nào chặn lại (partial unique index chỉ lọc `Status = 'Holding'`, nên hai phiên `Holding`
trên hai ghế **khác nhau** — thực ra là chuyện bán trùng, không phải chuyện chống trùng ghế).

**Hướng sửa — thuộc Trần Trung Hiếu (chủ `TripSeatMapService` + chủ `api-contract.md`):** thêm đúng
một truy vấn nữa vào `GetSeatMapAsync` — `Tickets` theo `(TripId, SeatId, Status ∈ {Paid, Used})` —
rồi trả `"Paid"`. Hình dạng response **không đổi** (`api-contract.md` đã chốt sẵn ba giá trị
`Available` / `Held` / `Paid`), nên ⛔5 **không** bị chạm. Việc này **không thể làm trước r40** theo
nghĩa dữ liệu: hôm nay chưa có gì ghi ra `Tickets` (xem G4), nên sửa xong cũng chưa quan sát được gì
trên môi trường thật — nhưng sửa được **ngay**, và phải sửa **trước** khi r40 chạy, vì lúc đó một
ghế bán trùng là tiền thật.

**Các bước tái hiện:** xem mục 7.

---

### G2 — Nửa frontend của story 3 chưa nối, và ba module là code chết 🔴 Cao

Backend của US 3 đã đủ bốn endpoint (đo ở mục 2.1). Phía frontend thì **không có đường nào chạm tới
chúng**:

| Việc | File | Tình trạng đo được |
|---|---|---|
| Tạo phiên giữ chỗ | `frontend/src/api/seatHoldApi.ts:66-72` | ❗ **`SeatHoldApi` KHÔNG có hàm `create`** — chỉ có `getSession` và `extend`. Không có cách nào tạo phiên từ giao diện |
| Màn chọn ghế → giữ chỗ | `frontend/src/pages/SeatMapPage.tsx:106-120` | `handleContinue` **không gọi** `seatHoldApi`. Nó dựng URL `/payment-waiting` bằng `paymentCode: \`demo-${tripId}\`` và chỉ gửi **nhãn** ghế (`seats: labels`), **mất hẳn seat id** mà `POST /seat-holds` cần |
| Component đếm ngược | `frontend/src/components/seatHoldReminder.ts` | **0 importer** |
| Modal hết hạn + nút gia hạn | `frontend/src/components/SeatHoldExpiryModal.tsx` | **0 importer** |
| Tự động nhả ghế | `frontend/src/components/useSeatHoldAutoRelease.ts` | **0 importer** (hook này gọi `seatHoldReleaseApi.release`) |

Lệnh kiểm chứng (`grep -rn` trên toàn `frontend/src`, trừ chính năm file đó) trả về **đúng một** dòng
— và đó là một *comment*, không phải `import`:

```
frontend/src/components/paymentWaiting.ts:6:// seatHoldReminder.ts / monthlyPassReminder.ts.
```

**Vì sao mục này quan trọng hơn vẻ ngoài của nó:** nó khiến chính task này **không thể hoàn thành
kể cả khi có môi trường online**. Một buổi kiểm thử chéo trên môi trường thật sẽ dừng ở bước thứ hai
— mở `/seat-map`, chọn ghế, bấm "Tiếp tục" — và **không có request `POST /seat-holds` nào được gửi
đi**. Ba story (r20, r21, r22) đã có code nhưng chưa màn nào render chúng. Đây đúng cùng hình dạng
với `V4` của báo cáo vé tháng ("hai component của story 16 là code chết").

**Hướng sửa:** là ba việc của ba người, không phải một dòng:

- **Nguyễn Đình Băng** (r8 màn chọn ghế, r21 modal): bổ sung `create` vào `seatHoldApi.ts`; cho
  `SeatMapPage.handleContinue` gọi nó **trước** khi `navigate`, và chuyển **`sessionCode`** sang
  `/payment-waiting` thay cho `paymentCode` demo.
- **Dương Thị Hạnh** (r22 tự động nhả ghế): nối `useSeatHoldAutoRelease` vào màn đang chờ thanh toán.
- **Hoàng Văn Thịnh** (r20 component đếm ngược): render `seatHoldReminder` / modal vào cùng màn đó.

---

### G3 — Ba comment nói sai tình trạng backend, một trong số đó đang là công tắc chặn tính năng 🟠 TB

| # | Chỗ | Comment nói | Thực tế đo được |
|---|---|---|---|
| 1 | `frontend/src/api/seatMapApi.ts:6-9` | *"Backend CHƯA có endpoint 'lấy sơ đồ ghế theo chuyến + trạng thái từng ghế'"* | Endpoint **có**, công khai, trả 200 (mục 2.1) |
| 2 | `frontend/src/api/seatHoldApi.ts:6-9` | *"Backend CHƯA có endpoint giữ chỗ … hợp đồng chưa vào docs/api-contract.md"* | **Bốn** endpoint đã có và **đã có hợp đồng** (`api-contract.md`, mục "Giữ chỗ — `/seat-holds`") |
| 3 | `frontend/src/pages/SeatMapPage.tsx:21-22` | *"endpoint đó chưa có nên đang chạy dữ liệu giả (xem seatMapApi.ts)"* | Như trên |

Không chỉ là chữ nghĩa: comment #1 và #2 là **lý do được viết ra** cho `const USE_MOCK_DATA = true`
(`seatMapApi.ts:79`, `seatHoldApi.ts:69`, `seatHoldReleaseApi.ts:32`). Người sau đọc comment đó thấy
hợp lý nên không đổi cờ — và tính năng đứng im. Cùng gốc với `V5`/`V6` của báo cáo vé tháng.

**Hướng sửa:** sửa ba comment rồi hạ ba cờ `USE_MOCK_DATA` xuống `false` — nhưng **theo thứ tự**:
hạ cờ chỉ nên làm **sau** G2 (chưa có hàm `create` thì hạ cờ cũng chưa chạy được gì) và **sau** G5
(hạ cờ với kiểu `number` không-nullable sẽ nhận `null` thật từ backend).

---

### G4 — Trạng thái `Paid` và nhánh "đã chốt thành vé" không có nguồn sinh 🟠 TB

Hai thứ mà hợp đồng đã đặc tả đầy đủ **không có dòng code nào tạo ra chúng**:

```bash
$ grep -rn "Tickets.Add\|Tickets.Remove" backend/SmartBus.Api --include=*.cs
(0 kết quả)

$ grep -rn "SeatHoldStatus.Confirmed" backend/SmartBus.Api --include=*.cs
SeatHoldExtendService.cs:152   if (statuses.Any(s => s == SeatHoldStatus.Confirmed))
SeatHoldExtendService.cs:154       return SeatHoldStatus.Confirmed;
SeatHoldLookupService.cs:87    if (statuses.Any(s => s == SeatHoldStatus.Confirmed))
SeatHoldLookupService.cs:89        return SeatHoldStatus.Confirmed;
SeatHoldReleaseService.cs:54   if (rows.Any(r => r.Status == SeatHoldStatus.Confirmed))
SeatHoldReleaseService.cs:140  if (statuses.Any(s => s == SeatHoldStatus.Confirmed))
SeatHoldReleaseService.cs:142      return SeatHoldStatus.Confirmed;
```

Sáu dòng — **tất cả đều là nhánh ĐỌC**. Không dòng nào **ghi** giá trị này. Nghĩa là:

- Ghế không bao giờ trả `"Paid"` (G1) — không phải vì sơ đồ ghế quên hỏi, mà trước hết vì **không có
  vé nào để hỏi**.
- Nhánh 409 `"Phiên giữ chỗ đã chốt thành vé, không thể nhả."` **không thể chạy trên môi trường
  thật** hôm nay. Tôi chỉ đo được nó bằng cách **seed tay** một dòng `SeatHold` `Status = Confirmed`
  (mục 2.1, dòng cuối bảng nhánh lỗi) — tức là đo được *hợp đồng*, chưa đo được *luồng*.

**Thiếu cái gì:** backlog Sprint 3 **r40** — *"API phát hành vé điện tử sau khi thanh toán thành
công"*, Trần Trung Hiếu, cột Status **"Chưa làm"**. Đây không phải việc của người kiểm thử (2.4), và
cũng không phải lỗi của ai — chỉ là một mắt xích chưa tới lượt. Ghi ra vì nó **chặn việc kiểm chứng
G1**: sửa `TripSeatMapService` xong vẫn chưa có gì để nhìn thấy cho tới khi r40 ghi được dòng
`Tickets` đầu tiên.

---

### G5 — Kiểu nullable lệch giữa hai tầng 🟠 TB

Backend trả **`null` thật** ở ba trường giá (đo được, không phải suy đoán):

| Tình huống | `pricePerSeat` | `vipSurcharge` | `seat[].price` |
|---|---|---|---|
| Tuyến CÓ giá phổ thông | `7000` | `null` | `7000` |
| Tuyến **KHÔNG** cấu hình giá | **`null`** | `null` | **`null`** |

Frontend khai cả ba là **số không-nullable** — `frontend/src/api/seatMapApi.ts`:

```ts
pricePerSeat: number;
vipSurcharge: number;
price: number;
```

Hạ cờ `USE_MOCK_DATA` xuống `false` là màn hình `SeatMapPage` gặp `null` ở chỗ nó tin là `number`;
`vipSurcharge` thì **luôn** `null` (backend chưa có nguồn phụ trội VIP — `TripSeatMapResponse.cs` ghi
rõ, và đó là phần việc của Dương Thị Hạnh).

**Đây không phải backend làm sai:** `TripSeatMapResponse.cs` ghi rõ ngay đầu file rằng hai trường
nullable là **khác có chủ đích** so với bản dự kiến của frontend, và *"Khi bật cờ USE_MOCK_DATA trong
seatMapApi.ts xuống false, frontend cập nhật hai điểm này theo hợp đồng."* Việc còn lại đúng là của
phía frontend — nhưng phải làm **trước** G3, không thì thứ tự ngược lại và bug sẽ lộ ra như một lỗi
backend.

---

### G6 — `api-contract.md` trỏ sai chỗ, và tiền đề của cảnh báo nay đã sai 🟡 Thấp

`docs/api-contract.md:1447-1450` viết:

> *"⚠️ Ghế đã `Confirmed` **không** chặn lượt giữ mới (partial index chỉ lọc `Status = 'Holding'`,
> đúng đặc tả docs/26 §1) và bảng `Tickets` chưa migrate — khe hở có thật của luồng đặt vé, **đã ghi
> ở docs/26 §7**, ngoài phạm vi task này."*

Hai chỗ sai:

1. **§7 không nói gì về chuyện đó.** `docs/26-csdl-so-do-ghe.md` §7 là *"Việc cần báo nhóm (không
   phải việc code)"* — nội dung là ba dòng bảng còn thiếu ở A9, quy ước A7 về migration, và việc
   trùng lặp giữa dòng 14 và dòng 16. Grep cả file: `Tickets` xuất hiện **đúng một lần**, ở dòng 156
   (§3, chống trùng ghế) — không liên quan.
2. **Tiền đề đã sai:** *"bảng `Tickets` chưa migrate"* — đã migrate 10/10/2026 (xem G1).

Cùng vấn đề ở `Dtos/Trips/TripSeatMapResponse.cs` và `TripSeatMapService.cs:83-86`. **Hướng sửa:**
Trần Trung Hiếu cập nhật lại hai câu trong `api-contract.md` + docstring (đúng việc, vì ⛔5 nói sửa
hợp đồng là việc của chủ hợp đồng), Vàng Thị Dăm bổ sung một mục vào `docs/26` mô tả khe hở thật.

---

### G7 — `package-lock.json` lạc ở gốc repo (lần thứ sáu) 🟡 Thấp

```
$ git status --porcelain
?? package-lock.json

$ ls -la package-lock.json
-rw-r--r-- 1 ASUS 197121 98 Sep 26 16:42 package-lock.json
```

98 byte, không được theo dõi, ngày 26/09 — **không đổi** qua cả sáu báo cáo. Chuỗi lặp:
`L20` (báo cáo Sprint 1) → `P9` (phân công) → `N9` (tra cứu) → `V7` (vé tháng) → `F9` (phản ánh) →
nay `G7`.

Gốc rễ vẫn là `.gitignore` ở gốc chưa chặn `package-lock.json` (thuộc mục E1, Phùng Duy Hoàng). Đây
là mục duy nhất trong báo cáo này mà việc sửa chỉ là **một dòng** — và cũng là mục đã treo lâu nhất.

---

## 4. Đã kiểm và ĐẠT

### Bốn endpoint `/seat-holds` + sơ đồ ghế — đúng hợp đồng

| Endpoint | Kiểm | Kết quả |
|---|---|---|
| `GET /api/trips/{id}/seats` | Không token | ✅ **200** — công khai đúng chủ ý (US 2: chọn chỗ trước khi đăng nhập) |
| | Chuyến không tồn tại / chuyến mồ côi | ✅ **404** `"Không tìm thấy chuyến xe"` |
| | Thứ tự vẽ | ✅ `Floor → RowIndex → ColumnIndex` — seed lệch thứ tự (tầng 2 trước, cột 2 trước) vẫn trả `[T1-A1, T1-A2, T2-A1]` |
| | Xe 2 tầng | ✅ `floors = 2`, `busType = "Xe buýt 2 tầng 60 chỗ"`, số ghế dùng tiền tố `T1-` / `T2-` (docs/26 §2) |
| | Nguồn giá | ✅ có cả `Standard(7000)` lẫn `Student(5000)` → chọn đúng bản **`Standard`**; tuyến chưa cấu hình giá → `null` (không phải lỗi) |
| `POST /api/seat-holds` | Hành khách | ✅ **201**, `status = "Holding"`, `canExtend = true` |
| | Mã phiên | ✅ đúng khuôn hợp đồng `PHIEN-` + **đúng 8 ký tự hex** (14 ký tự), ví dụ `PHIEN-fabdfbe3` |
| | `seatNumbers` | ✅ gửi lên **đảo thứ tự** (`A3, A1, A2`) vẫn trả `["A1","A2","A3"]` — sắp theo toạ độ sơ đồ đúng lời hứa |
| | Bốn vai trò | ✅ Passenger / Driver / Manager / Admin đều **201** — đúng `[Authorize]` trần |
| | 16 nhánh lỗi | ✅ tất cả đúng mã + đúng khuôn `{ message, errors }` (bảng ở mục 2.1) |
| `GET /api/seat-holds/{code}` | Chính chủ | ✅ 200, đúng hình dạng 6 trường `SeatHoldSession` |
| | Người khác | ✅ **404** — không lộ là phiên có tồn tại |
| | Mã phiên không tồn tại | ✅ **404** |
| `POST /{code}/extend` | Lần đầu | ✅ 200, `canExtend` lật `true → false` |
| | Lần thứ hai | ✅ **409** `"…đã được gia hạn rồi — mỗi phiên chỉ được gia hạn tối đa 1 lần."` |
| | Công thức hạn mới | ✅ **tính từ lúc gọi**, không phải hạn cũ + 10 phút — phiên dựng sẵn còn hạn 1 phút đem gia hạn, hạn mới lệch `0,010 s` so với `lúc gọi + 10 phút` và lệch `−59,988 s` so với `hạn cũ + 10 phút`. Hai công thức cho hai kết quả khác nhau, nên phép đo này **phân biệt được** |
| `POST /{code}/release` | Chính chủ | ✅ 200, `status = "Released"` |
| | Gọi lại lần hai | ✅ **200** — nhả là idempotent, khác gia hạn (đây là chủ ý đã ghi trong hợp đồng) |
| | Phiên đã `Expired` | ✅ **200**, trả đúng `status: "Expired"` |
| | Phiên đã `Confirmed` | ✅ **409** `"Phiên giữ chỗ đã chốt thành vé, không thể nhả."` |
| | Người khác / mã không tồn tại | ✅ **404** cả hai |

### Phân quyền — đúng trên cả năm endpoint

| | Không token |
|---|---|
| 3 endpoint theo phiên (`GET`, `extend`, `release`) | ✅ **401** đủ cả ba |
| `POST /api/seat-holds` | ✅ **401** |
| `GET /api/trips/{id}/seats` | ✅ **200** — và đây là **đúng**, không phải thiếu `[Authorize]`: `TripSeatMapController` cố ý không gắn, cùng lối `TripSearchController` |

### Một quan sát nhỏ, chưa đủ để thành lỗi

Chuyến `Status = Running` (đang chạy) **vẫn giữ ghế được** — đo được **201**. Hợp đồng chỉ chặn
`Cancelled` và `Completed`, và như vậy là **hợp lý về nghiệp vụ**: khách còn lên xe dọc đường thì vẫn
phải bán được vé. Ghi lại để nếu nhóm sau này muốn chặn thêm `Running` thì biết đó là **quyết định
nghiệp vụ**, không phải bug — và sửa nó là **đổi hợp đồng** (⛔5).

---

## 5. Việc chuyển cho ai

| Việc | Người | Vì sao là của họ |
|---|---|---|
| Thêm truy vấn `Tickets` vào `GetSeatMapAsync` để ghế đã bán trả `"Paid"` (G1); cập nhật `api-contract.md` + docstring DTO đã lỗi thời (G6) | **Trần Trung Hiếu** | r3 — chủ `TripSeatMapService` + chủ `api-contract.md` |
| Bổ sung `create` vào `seatHoldApi.ts`, cho `SeatMapPage.handleContinue` gọi `POST /seat-holds` (G2); sửa kiểu nullable (G5); sửa 2 comment (G3 #1, #3) | **Nguyễn Đình Băng** | r8 (màn sơ đồ ghế) + r21 (modal hết hạn) |
| Nối `useSeatHoldAutoRelease` vào màn chờ thanh toán (G2) | **Dương Thị Hạnh** | r22 — *"Tự động nhả ghế và quay về trang chọn ghế khi hết hạn"* |
| Nối `seatHoldReminder` / component đếm ngược vào màn (G2); sửa comment + hạ cờ `USE_MOCK_DATA` ở `seatHoldApi.ts` (G3 #2) | **Hoàng Văn Thịnh** | r20 — *"Component đếm ngược thời gian giữ chỗ trên UI"* |
| Ghi dòng `Tickets` đầu tiên + đặt `SeatHoldStatus.Confirmed` sau khi thanh toán (G4) | **Trần Trung Hiếu** | r40 — *"API phát hành vé điện tử sau khi thanh toán thành công"*, còn "Chưa làm" |
| Mô tả khe hở "ghế đã `Confirmed` / đã bán không chặn lượt giữ" vào `docs/26` | **Vàng Thị Dăm** | Chủ CSDL, chủ `docs/26-csdl-so-do-ghe.md` |
| `package-lock.json` ở gốc repo (G7) | **Phùng Duy Hoàng** | `.gitignore` thuộc E1 |

---

## 6. Còn lại — chưa kiểm được

| Hạng mục | Vì sao chưa kiểm được |
|---|---|
| Bất kỳ request HTTP thật nào qua mạng | Không có môi trường online (mục 1) |
| API trỏ vào CSDL Supabase chung | Máy này không có `appsettings.Development.json`; chuỗi kết nối nằm ngoài repo |
| **Chốt chống trùng ghế** — partial unique index `(TripId, SeatId) WHERE Status = 'Holding'` (docs/26 §3) | Provider InMemory **bỏ qua mọi unique index**, nên hai request cùng giữ một ghế không phủ được ở đây. Nhánh `feature/3-kiem-thu-dong-thoi-50-khach-cung-giu-1-ghe` phủ phần này bằng **SQLite** — engine duy nhất máy này cưỡng chế được index. Trên **PostgreSQL thật** thì vẫn chưa ai chạy |
| Job quét hold hết hạn chạy thật theo nhịp 1 phút | Job nền bị thay bằng bản noop trong test; đã có `SeatHoldExpiryServiceTests` phủ phần ruột, nhưng **nhịp** thì không test nào đo được |
| Trạng thái `"Paid"` của ghế | Không có nguồn sinh (G4) — đo bằng dữ liệu seed tay, chưa đo được luồng |
| Luồng đặt vé trọn vẹn (giữ → thanh toán → thành vé → soát QR) | Thiếu r40; thêm nữa luồng **mua vé → nhận QR** là task riêng (r49 của tôi) |
| Luồng giữ chỗ **từ giao diện** | Không màn nào gọi `POST /seat-holds` (G2) |
| Đua thật trên PostgreSQL (50 khách) | Không có `docker` / `psql` / `pg_ctl` trên máy này, CI không kèm service PostgreSQL |
| Đa trình duyệt / đa thiết bị, đếm ngược trên màn hình | Không có môi trường để mở, và màn hình chưa nối (G2) |

---

## 7. Phụ lục — lệnh chạy lại mọi bằng chứng trong báo cáo

```bash
# Mốc chuẩn
dotnet test backend/SmartBus.Tests/SmartBus.Tests.csproj          # 1121/1121 trên main @ 5f03357
dotnet build backend/SmartBus.sln -c Release                       # 0 warning, 0 error
npm --prefix frontend install                                      # bù qrcode.react còn thiếu ở máy này
npm --prefix frontend run build                                    # exit 0
npm --prefix frontend run lint                                     # 1 cảnh báo cũ StopManagePage.tsx

# 12 ca kiểm thử chéo của báo cáo này (luồng trọn vẹn + phân quyền + công thức gia hạn + sơ đồ 2 tầng)
dotnet test backend/SmartBus.Tests/SmartBus.Tests.csproj \
  --filter "FullyQualifiedName~SeatHoldCrossFlowApiTests"

# G1 — sơ đồ ghế có hỏi bảng Tickets không? (0 = không hỏi)
grep -n "Tickets" backend/SmartBus.Api/Services/TripSeatMapService.cs

# G1 — bảng Tickets đã migrate chưa, và từ bao giờ
ls backend/SmartBus.Api/Migrations/ | grep -i ticket
git log --format="%h %ad %an %s" --date=short \
  -- backend/SmartBus.Api/Migrations/*Sprint3_Payments_Vouchers_Tickets*

# G1 — tiền đề đã lỗi thời nằm ở đâu
grep -n "chưa migrate\|chưa được migrate" docs/api-contract.md \
  backend/SmartBus.Api/Dtos/Trips/TripSeatMapResponse.cs \
  backend/SmartBus.Api/Services/TripSeatMapService.cs

# G2 — ba module giữ chỗ có ai import không (chỉ ra 1 dòng COMMENT, không dòng nào là import)
grep -rn "seatHoldApi\|seatHoldReleaseApi\|SeatHoldExpiryModal\|useSeatHoldAutoRelease\|seatHoldReminder" \
  frontend/src --include=*.ts --include=*.tsx

# G2 — SeatHoldApi có hàm create không (0 = không có, nên không cách nào tạo phiên từ UI)
grep -n "create" frontend/src/api/seatHoldApi.ts

# G2 — màn chọn ghế có gọi API giữ chỗ không
sed -n '106,120p' frontend/src/pages/SeatMapPage.tsx

# G3 — ba comment nói sai + ba cờ chặn tính năng
grep -n "USE_MOCK_DATA" frontend/src/api/seatMapApi.ts frontend/src/api/seatHoldApi.ts \
  frontend/src/api/seatHoldReleaseApi.ts
sed -n '6,9p'  frontend/src/api/seatMapApi.ts
sed -n '6,9p'  frontend/src/api/seatHoldApi.ts
sed -n '21,22p' frontend/src/pages/SeatMapPage.tsx

# G4 — không gì ghi Tickets, không gì đặt Confirmed (6 dòng Confirmed đều là nhánh ĐỌC)
grep -rn "Tickets.Add\|Tickets.Remove" backend/SmartBus.Api --include=*.cs
grep -rn "SeatHoldStatus.Confirmed" backend/SmartBus.Api --include=*.cs

# G5 — kiểu nullable hai bên
grep -n "pricePerSeat\|vipSurcharge\|price:" frontend/src/api/seatMapApi.ts
grep -n "decimal? PricePerSeat\|decimal? VipSurcharge\|decimal? Price" \
  backend/SmartBus.Api/Dtos/Trips/TripSeatMapResponse.cs \
  backend/SmartBus.Api/Dtos/Trips/TripSeatMapSeatResponse.cs

# G6 — hợp đồng trỏ tới docs/26 §7, mà §7 nói chuyện khác
sed -n '1447,1450p' docs/api-contract.md
grep -n "Tickets" docs/26-csdl-so-do-ghe.md      # đúng 1 dòng, ở §3, không liên quan

# G7 — package-lock.json lạc ở gốc repo
git status --short | grep package-lock
grep -n "package-lock" .gitignore || echo "(không có dòng nào chặn)"
```

---

*Báo cáo này **không** sửa một dòng code nghiệp vụ nào — đúng ranh giới "Người kiểm thử — Giàng A
Vàng" ở `docs/03-quy-uoc.md` mục 2.4. File test mới `backend/SmartBus.Tests/SeatHoldCrossFlowApiTests.cs`
nằm trong phần được phép của vai trò đó. Mọi mục trong mục 3 kèm các bước tái hiện để người sở hữu
file tự sửa; riêng G1 cố ý **không** được viết thành ca test đỏ, vì biến một lỗi đang sống thành chốt
hồi quy cho hành vi sai là che lỗi, không phải sửa lỗi.*
