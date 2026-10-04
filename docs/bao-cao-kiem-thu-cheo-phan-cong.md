# Báo cáo kiểm thử chéo — luồng phân công điều xe (gán xe + gán tài xế)

| | |
|---|---|
| **Người thực hiện** | Giàng A Vàng (Kiểm thử) |
| **Task** | Sprint 2, story 14 — *Kiểm thử chéo luồng phân công trên môi trường online* |
| **Ngày** | 04/10/2026 |
| **Mốc so sánh** | `main` @ `7d798b5` |
| **Trạng thái** | ⚠️ Chỉ hoàn thành phần kiểm thử **không cần môi trường online** — xem mục 1 |

> Mã lỗi trong báo cáo này (**P1**…) là cục bộ cho báo cáo này, **khác** bộ `C1`–`C6` của
> `docs/bao-cao-kiem-thu-cheo-lich-trinh-sinh-chuyen.md` và bộ `L1`–`L20` của
> `docs/bao-cao-kiem-thu-sprint-1.md`. Ba bộ không liên tục và không thay thế nhau.
>
> Báo cáo này **kế thừa có chủ đích**: phần "phân công điều xe" cũng là một nửa của luồng story 13,
> nên `C2` và `C3` của báo cáo trước xuất hiện lại ở đây với mã mới `P1`/`P3` — chúng **vẫn chưa
> được sửa**, đây là bằng chứng chứ không phải lỗi mới phát hiện.

---

## 1. Phạm vi — phần KHÔNG làm được và vì sao

Task yêu cầu kiểm thử **trên môi trường online**. So với báo cáo story 13 (03/10), khoảng trống
**không hẹp thêm một chút nào** — không có gì mới được tạo trong khoảng giữa hai báo cáo.

| Kiểm tra | Story 13 | Nay |
|---|---|---|
| Dockerfile backend | ✅ có — `backend/Dockerfile` | ✅ **vẫn có** |
| CORS mở cho domain thật | ✅ đã sửa — `UseCors` vô điều kiện, origin đọc từ `Cors:Origins` | ✅ **vẫn vậy** (`Program.cs:217-239`) |
| `render.yaml` / `fly.toml` / `Procfile` / `vercel.json` | ❌ không | ❌ **vẫn không** |
| Workflow deploy | ❌ `.github/workflows/` chỉ có `ci.yml` | ❌ **vẫn chỉ có `ci.yml`** |
| URL online ở đâu đó trong repo | ❌ không | ❌ **không** — grep `onrender\|vercel.app\|netlify.app\|fly.io\|azurewebsites\|railway.app` trên toàn repo (trừ `node_modules`): **0 kết quả**, ngoại lệ duy nhất là dòng ghi chú *"0 kết quả"* trong chính báo cáo story 13 |
| `frontend/.env.example` | `localhost:5080` | ❌ **vẫn vậy** — `VITE_API_URL=http://localhost:5080/api` |
| `appsettings.Production.json` | ❌ không có | ❌ **vẫn không** — chỉ có `appsettings.json` |
| `Cors:Origins` có giá trị thật trong `appsettings.json` | — | ❌ **không khai** — chỉ có mặc định `http://localhost:5173` trong code |
| Task deploy trong backlog Sprint 2 | ❌ không có | ❌ **không có dòng nào** |

**Hệ quả:** vẫn **không chạy được một request HTTP thật nào** — không online, và cũng không chạy nổi
API ở máy vì thiếu PostgreSQL lẫn Docker (`appsettings.Development.json` không tồn tại).

Phần đã làm thay thế: **kiểm thử chéo tĩnh có bằng chứng chạy lại được** — đối chiếu hợp đồng
`docs/api-contract.md` với cả hai phía mã nguồn, chạy bộ test backend, build + lint frontend.

---

## 2. Mốc chuẩn trên `main` — đều ĐẠT

| Hạng mục | Lệnh | Kết quả |
|---|---|---|
| Test backend | `dotnet test backend/SmartBus.Tests/SmartBus.Tests.csproj` | ✅ **560/560 pass**, 0 fail, 0 skip, 1 m 29 s (story 13: 456) |
| Build frontend | `npm --prefix frontend run build` | ✅ exit 0 (chỉ còn cảnh báo chunk > 500 kB) |
| Lint frontend | `npm --prefix frontend run lint` | ⚠️ 1 cảnh báo — đúng cảnh báo cũ `StopManagePage.tsx:34`, ngoài luồng phân công |
| Rác debug (`console.log`, `TODO`, `FIXME`, `debugger`) | grep `frontend/src` + `backend/SmartBus.Api` | ⚠️ **1 dòng `TODO`** — `tripAssignmentApi.ts:90`, xem P2 |
| Bí mật bị commit | `git ls-files` lọc `.env`, `appsettings.Development`, `*.pfx`, `*.pem`, `id_rsa` | ✅ sạch — chỉ có `appsettings.Development.json.example` |

---

## 3. Danh sách lỗi

Mức độ: **Cao** = sai chức năng hoặc gây hiểu nhầm nghiêm trọng · **TB** = ảnh hưởng trải nghiệm
hoặc rủi ro · **Thấp** = tài liệu, vệ sinh mã.

| # | Lỗi | Mức | Người xử lý |
|---|---|---|---|
| P1 | Màn hình "Phân công điều xe" **vẫn chạy dữ liệu GIẢ** — tài xế là người bịa, phân công mất khi tải lại | 🔴 Cao | Dương Thị Hạnh |
| P2 | `bulkAssignDriver` **ném lỗi "chưa có endpoint"**, nhưng endpoint đã có trên `main` | 🔴 Cao | Dương Thị Hạnh |
| P3 | Type `Trip` thiếu `driverId`/`driverName` + 2 comment nói sai tình trạng backend | 🟠 TB | Nguyễn Đình Băng |
| P4 | Comment nói API đổi xe/đổi tài xế khi sự cố "chưa có" — đã có — và chính nó đang **khoá nút** trên UI | 🟠 TB | Nguyễn Đình Băng |
| P5 | FE **bỏ qua** cảnh báo trùng lịch tài xế mà backend trả về | 🟠 TB | Dương Thị Hạnh |
| P6 | Cảnh báo trùng lịch của FE chỉ quét **1 tuyến × 1 ngày**, lại chỉ so **xe** — backend quét toàn bộ | 🟠 TB | Nguyễn Đình Băng |
| P7 | Comment nói `TripAssignmentController` nằm ở "branch chưa merge" — nay đã merge | 🟡 Thấp | Nguyễn Duy Kiên |
| P8 | Comment ở `UnassignedTripsFilter.tsx` nêu **lý do sai** cho một kết luận đúng | 🟡 Thấp | Dương Thị Hạnh |
| P9 | `package-lock.json` lạc ở gốc repo | 🟡 Thấp | Hoàng Văn Thịnh |

**Còn tồn từ báo cáo story 13** (không tính là lỗi mới, ghi để không mất dấu): `C2` = **P1**,
`C3` = **P3** — cả hai **vẫn nguyên**, xem bằng chứng bên dưới. `L20` (`package-lock.json`) = **P9**,
vẫn untracked sau hai báo cáo.

---

### P1 — Màn hình "Phân công điều xe" vẫn chạy dữ liệu GIẢ 🔴 Cao

**Ở đâu:** `frontend/src/api/tripAssignmentApi.ts:19` (cờ), `:42-46` (dữ liệu bịa), `:50` (bản đồ
trong phiên), `:84-88` và `:115-124` (nhánh giả); hiển thị ở
`frontend/src/pages/TripAssignmentPage.tsx:397` và `:488`.

**Chuyện gì:** `const USE_MOCK = true;` vẫn bật. Ở nhánh này:

- Danh sách tài xế là **ba người bịa**: `Nguyễn Văn An / Trần Thị Bích / Lê Văn Cường`
  (`MOCK_DRIVERS`, `:42-46`) — không tồn tại trong CSDL.
- `enrichTripsWithDriver` gán tài xế theo **so le chẵn/lẻ của vị trí dòng** (`:122-123`):
  `index % 2 === 0` thì có tài xế, lẻ thì `null`. Tức là **chuyến nào "đã phân công" là do tung
  đồng xu theo thứ tự trong trang**, không liên quan gì tới dữ liệu thật.
- Kết quả phân công ghi vào `mockAssignments` — một `Map` **sống trong phiên trình duyệt**
  (`:50`). Tải lại trang là mất sạch, nhưng thông báo vẫn báo thành công.

**Bằng chứng chạy lại được:**

```
$ grep -n "USE_MOCK" frontend/src/api/tripAssignmentApi.ts
19:const USE_MOCK = true;
101:  if (!USE_MOCK) {

$ sed -n '42,46p;50p;122,123p' frontend/src/api/tripAssignmentApi.ts
const MOCK_DRIVERS: DriverOption[] = [
  { id: 'driver-1', fullName: 'Nguyễn Văn An', phoneNumber: '0912345678' },
  ...
const mockAssignments = new Map<string, string | null>();
  return index % 2 === 0 ? MOCK_DRIVERS[index % MOCK_DRIVERS.length] : null;
```

```
$ grep -n "USE_MOCK\|driver-assignment" frontend/src/api/*.ts frontend/src/pages/*.tsx
frontend/src/api/tripAssignmentApi.ts:19:const USE_MOCK = true;
```

**Hậu quả:** hai hệ quả riêng biệt, cái thứ hai nặng hơn cái thứ nhất:

1. Màn hình hiện **tên người không có thật** như thể là tài xế của công ty. Nhãn phụ ở
   `TripAssignmentPage.tsx:405-407` *có* ghi "phần tài xế đang dùng dữ liệu giả" — trung thực,
   nhưng người dùng chỉ đọc bảng và cột "Tài xế" thì không thấy nhãn đó.
2. **Bộ lọc và nút chọn dòng bị điều khiển bởi dữ liệu bịa.** Dòng 397
   (`data.filter((trip) => trip.driverId === null)`) và dòng 488
   (`disabled: ... || record.driverId !== null`) đọc `driverId` lấy từ phép so le chẵn/lẻ. Nghĩa là
   **màn hình quyết định chuyến nào được phép gán theo một quy tắc không tồn tại trong nghiệp vụ**:
   chuyến thật đã có tài xế có thể hiện "Chưa phân công" và cho gán đè, chuyến thật chưa có tài xế
   có thể bị khoá. Đây không còn là "dựng giao diện trước" nữa — đây là **hành vi sai** đang chạy.

**Cách xử lý:** đây là quyết định của người viết (Dương Thị Hạnh), không phải việc kiểm thử. Điều
kiện tiên quyết để bật `USE_MOCK = false` **đã hội đủ từ lâu**: `TripResponse` trả `driverId` +
`driverName` (xem P3) và cả hai endpoint gán tài xế đã nằm trên `main`. Nên việc cần làm là bật
nhánh thật, **không phải** chờ ai.

**Bước tái hiện:** mở `/trip-assignment` → chọn tuyến + ngày → cột "Tài xế" hiện ba cái tên trên →
bật công tắc "Chỉ chuyến chưa phân công" → số dòng còn lại đổi theo **vị trí trong trang**, đổi
sang trang 2 thì tập dòng "chưa phân công" đổi khác → gán hàng loạt → F5 → kết quả về như cũ.

---

### P2 — `bulkAssignDriver` ném lỗi "chưa có endpoint", nhưng endpoint đã có 🔴 Cao

**Ở đâu:** `frontend/src/api/tripAssignmentApi.ts:90-92`.

**Chuyện gì:**

```ts
// TODO(task 113 - Kiên): chưa có endpoint gán tài xế hàng loạt. Khi có thì gọi endpoint
// thật ở đây (thay `_routeId` bằng tham số dùng trong URL) rồi trả số chuyến đã gán.
throw new Error('Chưa có endpoint gán tài xế — chờ backend task 113 của Kiên.');
```

Nhưng endpoint **đã có trên `main`**: `PATCH /api/routes/{routeId}/trips/driver-assignment`,
`backend/SmartBus.Api/Controllers/TripDriverAssignmentController.cs:46`, commit `25884d9`, hợp đồng ở
`docs/api-contract.md:1451`.

**Bằng chứng chạy lại được:**

```
$ git log --oneline -1 origin/main -- backend/SmartBus.Api/Controllers/TripDriverAssignmentController.cs
25884d9 feat(trips): API gán tài xế vào chuyến theo lô cho màn hình phân công điều xe

$ grep -n "HttpPatch" backend/SmartBus.Api/Controllers/TripDriverAssignmentController.cs
46:    [HttpPatch("driver-assignment")]
```

**Hậu quả:** đây là lỗi **nặng hơn P1** và là lý do nó đứng riêng. File `tripAssignmentApi.ts:18`
tự khai rằng việc nối API thật chỉ là **"đổi cờ thành false"**. Nhưng nếu ai đó làm đúng một việc
đó, `bulkAssignDriver` **ném lỗi 100% số lần bấm**, kèm thông báo *"Chưa có endpoint gán tài xế —
chờ backend task 113 của Kiên."* — tức là **cờ báo lỗi chỉ sai người và sai việc**: Kiên đã xong,
người bấm nút sẽ đi hỏi nhầm người. Đây là dạng hỏng tệ nhất: đường "đi đúng" lại là đường duy nhất
hỏng, còn đường hỏng (mock) thì trông như đang chạy tốt.

**Cách xử lý:** gọi thẳng endpoint thật theo hợp đồng đã chốt (`docs/api-contract.md:1603-1638`) —
body `{ tripIds, driverId }`, đọc `assignedCount` để báo số chuyến, và đọc `items[].conflicts` (xem
P5). Việc này gộp chung với P1 vì cùng một hàm, cùng một người.

**Bước tái hiện:** đổi `USE_MOCK` thành `false` → mở `/trip-assignment` → chọn ≥1 chuyến → chọn tài
xế → "Gán tài xế" → luôn nhận thông báo "Chưa có endpoint gán tài xế — chờ backend task 113 của
Kiên."

---

### P3 — Type `Trip` thiếu `driverId`/`driverName` + 2 comment nói sai backend 🟠 TB

**Ở đâu:** `frontend/src/api/tripApi.ts:31-50` (khai báo `Trip`), `:207-210` (comment);
`frontend/src/components/UnassignedTripsFilter.tsx:12-13` (comment).

**Chuyện gì:** `Trip` khai `busId`, `busLicensePlate`, `departureTime`… nhưng **không có**
`driverId`/`driverName`. Backend **đã trả cả hai** (hợp đồng `docs/api-contract.md:1393-1394` và
`:1410-1418`, có hẳn mục *"Vì sao trả kèm driverId/driverName?"*). Comment ở `tripApi.ts:207-210`
vẫn viết ở thì tương lai: *"khi backend trả thêm `driverId` trên `TripResponse` thì chỉ cần đổi
thành `t => t.driverId`"* — backend đã trả rồi.

Hệ quả kèm theo: `TripAssignmentPage.tsx` và `tripAssignmentApi.ts` đều phải định nghĩa thêm một
interface song song (`AssignableTrip extends Trip`) chỉ để **thêm hai trường lẽ ra đã có**. Đây là
lớp bù trừ tồn tại chỉ vì khai báo sai, và chính nó là chỗ để P1 lách vào.

**Bằng chứng chạy lại được:**

```
$ sed -n '31,50p' frontend/src/api/tripApi.ts   # không có driverId/driverName
$ sed -n '1393,1394p' docs/api-contract.md
| `driverId` | `string | null` (GUID) | Tài xế được phân công. `null` = chưa phân công ...
| `driverName` | `string | null` | Họ tên tài xế — kèm sẵn cùng lý do `busLicensePlate` ...
```

**Cách xử lý:** thêm hai trường vào `Trip` (Nguyễn Đình Băng — chủ `tripApi.ts`), rồi xoá
`AssignableTrip` và lớp `enrichTripsWithDriver`. Sửa xong P3 thì P1 tự nhiên mất chỗ đứng.

**Bước tái hiện:** `grep -n "driverId" frontend/src/api/tripApi.ts` → không kết quả.

---

### P4 — Comment nói API đổi xe/đổi tài xế khi sự cố "chưa có" — và nó đang khoá nút 🟠 TB

**Ở đâu:** `frontend/src/pages/TripAssignmentPage.tsx:371-372`.

**Chuyện gì:**

```ts
// Chỉ phân công chuyến chưa chạy (Scheduled). Đổi xe giữa chừng (sự cố) là task
// "API đổi xe/đổi tài xế khi có sự cố" của Hoàng, endpoint riêng chưa có.
if (trip.status !== 'Scheduled') { /* ... nút Phân công bị disabled ... */ }
```

Endpoint **đã có trên `main`**: `PATCH /api/trips/{id}/assignment`,
`backend/SmartBus.Api/Controllers/TripAssignmentController.cs:41`, commit `14fbbab`, đã merge **sau**
`25884d9` (thứ tự trong `git log origin/main`: `25884d9` ở dòng 44, `14fbbab` ở dòng 48).

Khác P3 ở chỗ: đây **không chỉ là comment cũ**. Nhánh `if` này đang **thực sự chặn người dùng** —
mọi chuyến `Running` (đang chạy) đều bị khoá nút "Phân công", với tooltip nói *"Chỉ phân công chuyến
đã lên lịch (Scheduled)"*. Nghĩa là **đúng cái tình huống cần đổi tài xế nhất — xe đang chạy thì tài
xế ốm — lại là tình huống màn hình không cho làm**, và lý do đưa ra là một endpoint đã tồn tại.

**Bằng chứng chạy lại được:**

```
$ grep -n "HttpPatch" backend/SmartBus.Api/Controllers/TripAssignmentController.cs
41:    [HttpPatch("{id:guid}/assignment")]
$ git ls-tree -r --name-only origin/main | grep TripAssignmentController
backend/SmartBus.Api/Controllers/TripAssignmentController.cs
```

**Cách xử lý:** Nguyễn Đình Băng mở nút cho chuyến `Running` và gọi `PATCH /trips/{id}/assignment`.
Tôi đã có sẵn 39 test cho endpoint này ở nhánh `feature/14-test-api-phan-cong-dieu-xe` (chưa mở PR)
— dùng lại được.

**Bước tái hiện:** mở `/trip-assignment`, bỏ lọc trạng thái (hoặc chọn "Đang chạy") → mọi dòng đều
có nút "Phân công" bị mờ, không bấm được.

---

### P5 — FE bỏ qua cảnh báo trùng lịch tài xế mà backend trả về 🟠 TB

**Ở đâu:** `frontend/src/api/tripAssignmentApi.ts:74-93` (hàm `bulkAssignDriver` — trả về **một con
số**), `frontend/src/pages/TripAssignmentPage.tsx:260-265`.

**Chuyện gì:** hợp đồng nói rõ backend **không chặn** trùng lịch tài xế mà **trả về trong
`items[].conflicts`** để màn hình cảnh báo (comment `TripDriverAssignmentController.cs:42-44`:
*"Trùng lịch tài xế KHÔNG chặn — trả trong `conflicts` của từng chuyến để màn hình cảnh báo mà vẫn
cho lưu"*; hợp đồng `docs/api-contract.md:1635`). Nhưng `bulkAssignDriver` được thiết kế để trả về
`Promise<number>` — **chỉ một con số** — và trang chỉ dùng nó để báo
`Đã phân công tài xế cho ${count} chuyến.`

Nghĩa là ngay cả khi gỡ P2, **hợp đồng trả về vẫn bị vứt bỏ ở tầng api**: `conflicts` không có
đường nào lên tới màn hình. Thiết kế này trong nhánh mock đã như vậy (mock luôn trả `tripIds.length`,
không có khái niệm trùng lịch), nên nếu chỉ "đổi cờ thành false" thì một chuyến được báo *"Đã phân
công thành công"* trong khi tài xế đang trùng lịch với chuyến khác — và màn hình im lặng.

**Bằng chứng chạy lại được:**

```
$ sed -n '74,81p' frontend/src/api/tripAssignmentApi.ts
/**
 * Gán một tài xế cho NHIỀU chuyến cùng lúc. Trả về số chuyến đã gán.
 */
export async function bulkAssignDriver(...): Promise<number> {
```

**Cách xử lý:** cho `bulkAssignDriver` trả về cả `assignedCount` lẫn `items[].conflicts`, rồi hiện
cảnh báo (không chặn) sau khi gán — đối xứng với cách `TripAssignmentModal` đang cảnh báo trùng **xe**.

**Bước tái hiện:** (chờ môi trường chạy thật) gán cùng một tài xế cho hai chuyến trùng giờ →
backend trả `conflicts.driverConflicts` khác rỗng → màn hình vẫn chỉ báo thành công.

---

### P6 — Cảnh báo trùng lịch của FE chỉ quét 1 tuyến × 1 ngày, lại chỉ so xe 🟠 TB

**Ở đâu:** `frontend/src/pages/TripAssignmentPage.tsx:172`, `:278-286`;
`frontend/src/api/tripApi.ts:202-210`.

**Chuyện gì:** `conflictsByTrip` dò trên `dayTrips`, mà `dayTrips` lấy từ
`fetchAllTripsForDay(selectedRouteId, from, to)` — tức là **chỉ chuyến của tuyến đang xem, trong
đúng ngày đang xem**. Backend `TripConflictService` quét **mọi tuyến**. Hợp đồng đã ghi nhận việc
này (báo cáo story 13 và `docs/api-contract.md` đều nói kiểm tra của backend mạnh hơn).

Thêm một tầng nữa: `findConflictingTrips(dayTrips, trip.busId, trip, trip.id, (t) => t.busId)`
(`tripAssignmentPage.tsx:282`) — chốt cứng vào `busId`. Nên cảnh báo của FE là **chỉ trùng XE**;
trùng **TÀI XẾ** không có cảnh báo nào ở phía client, kể cả trong phạm vi một tuyến một ngày.
Comment ở `tripApi.ts:207-210` nói sẽ đổi thành `t => t.driverId` "khi backend trả thêm driverId" —
nhưng đổi được rồi thì hàm này cũng chỉ nhận **một** khoá tài nguyên, không phải cả hai; muốn cảnh
báo cả hai phải gọi hai lần.

**Hậu quả:** màn hình có thể **hoàn toàn sạch cảnh báo** trong khi một chuyến vừa gán trùng tài xế
với chuyến của tuyến khác. Người điều hành tin vào màn hình thì sai; backend vẫn cho lưu (đúng thiết
kế) nên lỗi trùng lịch **đi thẳng vào dữ liệu**.

**Bằng chứng chạy lại được:**

```
$ sed -n '278,286p' frontend/src/pages/TripAssignmentPage.tsx
const conflictsByTrip = useMemo(() => {
  ...
  const conflicts = findConflictingTrips(dayTrips, trip.busId, trip, trip.id, (t) => t.busId);
```

**Cách xử lý:** chốt với backend rằng cảnh báo là **việc của backend** (nó là bên duy nhất thấy toàn
bộ dữ liệu), và sửa FE theo hướng đó — đọc `conflicts` từ response thay vì tự dò lại. Nếu vẫn muốn
cảnh báo trước khi gửi thì phải gọi API kiểm tra, không tự tính trên một trang dữ liệu.

---

### P7 — Comment nói `TripAssignmentController` ở "branch chưa merge" 🟡 Thấp

**Ở đâu:** `backend/SmartBus.Api/Controllers/TripDriverAssignmentController.cs:18-19`.

**Chuyện gì:** *"Tên lớp có tiền tố `Trip` để không đụng `TripAssignmentController` — controller của
API đổi xe/đổi tài xế khi có sự cố (Phùng Duy Hoàng) đang nằm ở **branch chưa merge**."*

Câu này **đúng vào lúc viết** (`25884d9` merge ở dòng 44 của `git log`, `14fbbab` ở dòng 48 — tức
`14fbbab` merge **sau**), nhưng nay đã cũ: cả hai controller cùng nằm trên `main` và
`git ls-tree origin/main` liệt kê đủ cả hai. Không sai chức năng; ghi lại vì câu này là một trong
những chỗ khiến người đọc sau (gồm cả tôi, ở P4) tưởng endpoint chưa có.

**Cách xử lý:** Nguyễn Duy Kiên sửa còn *"đã merge ở `14fbbab`"*, hoặc bỏ hẳn vế sau.

**Bước tái hiện:** `git log --oneline origin/main | grep -n "14fbbab\|25884d9"`.

---

### P8 — Comment nêu lý do sai cho một kết luận đúng 🟡 Thấp

**Ở đâu:** `frontend/src/components/UnassignedTripsFilter.tsx:12-13`.

**Chuyện gì:** *"TripResponse hiện chưa trả driverId (chờ task 113 của Kiên) nên chưa thể đẩy xuống
query string `unassigned=true`"*. Vế **kết luận đúng** — tham số `unassigned` thật sự **không tồn
tại** trong `docs/api-contract.md` lẫn `RouteTripsController`; vế **lý do sai** — `driverId` đã được
trả về. Hai chuyện độc lập bị gộp làm một, nên khi `driverId` có thật thì comment này trông như đã
hết hiệu lực trong khi kết luận (lọc phía client) vẫn còn đúng và vẫn cần giữ.

**Cách xử lý:** sửa lý do thành *"hợp đồng `GET /routes/{routeId}/trips` không có tham số lọc theo
tài xế"* — đúng và bền hơn.

---

### P9 — `package-lock.json` lạc ở gốc repo 🟡 Thấp

**Ở đâu:** gốc repo. `git status --porcelain` → `?? package-lock.json`.

**Chuyện gì:** đã báo ở `L20` (Sprint 1) và còn nguyên. `package.json` thật nằm ở `frontend/`, nên
file này ở gốc là dấu vết của một lần chạy `npm` sai thư mục. **Tôi cố ý không commit nó** — nó
không thuộc phần việc của tôi và luật nhóm không cho tôi tự quyết.

**Cách xử lý:** Hoàng Văn Thịnh xác nhận rồi `rm`, hoặc thêm vào `.gitignore`. Người khác đừng
`git add .` kẻo cuốn nó vào PR.

---

## 4. Đã kiểm và ĐẠT

Phần dưới đây đối chiếu trực tiếp hợp đồng với mã, và **đã chạy test thật** cho backend.

### Hợp đồng `PATCH /api/routes/{routeId}/trips/driver-assignment`

| # | Điều khoản (nguồn) | Kết quả |
|---|---|---|
| 1 | Phân quyền `ManagerOrAbove`, vai trò khác 403 (`TripDriverAssignmentController.cs:31`) | ✅ có test `Vai_tro_khac_quan_ly_goi_thi_tra_403` |
| 2 | Không token → 401 | ✅ `Khong_gui_token_thi_tra_401` |
| 3 | Trả `driverId`, `driverName`, `assignedCount`, `items[]` | ✅ `Gan_mot_tai_xe_cho_nhieu_chuyen_tra_ve_so_luong_va_ten_tai_xe` |
| 4 | Chuyến chưa phân công trả `driverId=null` **và** `driverName=null` cùng lúc | ✅ `Chuyen_chua_phan_cong_tra_driverId_null_va_driverName_null` |
| 5 | Gọi lại y hệt → 200, `assignedCount=0`, `updatedAt` không đổi | ✅ `Goi_lai_y_het_thi_idempotent_va_khong_doi_updatedAt` |
| 6 | Tuyến không tồn tại → 404 | ✅ `Tuyen_khong_ton_tai_tra_404` |
| 7 | Chuyến không tồn tại → 404 | ✅ `Chuyen_khong_ton_tai_tra_404` |
| 8 | Chuyến của tuyến khác → 404 **và cả lô bị chặn** (nguyên tử) | ✅ `Chuyen_cua_tuyen_khac_tra_404_va_ca_lo_bi_chan` |
| 9 | Chuyến đã huỷ → 409, cả lô không bị gán | ✅ `Chuyen_da_huy_tra_409_va_ca_lo_khong_bi_gan` |
| 10 | Chuyến đã hoàn thành → 409 | ✅ `Chuyen_da_hoan_thanh_tra_409` |
| 11 | GUID không mang vai trò Driver → 404 | ✅ `Guid_khong_phai_tai_xe_tra_404` |
| 12 | Tài khoản tài xế bị khoá (`isActive=false`) → 409 | ✅ `Tai_khoan_tai_xe_bi_khoa_tra_409` |
| 13 | Trùng lịch tài xế **không chặn** — 200 kèm cảnh báo | ✅ `Trung_lich_voi_chuyen_khac_cua_tai_xe_thi_van_200_kem_canh_bao` |
| 14 | Trùng lịch giữa hai chuyến **trong cùng lô** vẫn 200 kèm cảnh báo | ✅ `Trung_lich_giua_hai_chuyen_trong_cung_lo_thi_van_200_kem_canh_bao` |
| 15 | Hai chuyến nối đuôi nhau (chạm mốc) **không** tính là trùng | ✅ `Hai_chuyen_noi_duoi_nhau_khong_tinh_la_trung` |
| 16 | Thiếu `driverId` → 400 theo đúng khuôn D3 `{message, errors}` | ✅ `Thieu_driverId_tra_400_kem_loi_theo_truong` |
| 17 | `tripIds` rỗng → 400 | ✅ `Danh_sach_chuyen_rong_tra_400` |
| 18 | `tripIds` trùng nhau → 400 | ✅ `Trung_id_trong_lo_tra_400` |
| 19 | Quá 200 chuyến → 400 | ✅ `Qua_200_chuyen_tra_400` |

19/19 test xanh — `backend/SmartBus.Tests/TripDriverAssignmentApiTests.cs`.

### Các điểm khác

| Hạng mục | Kết quả |
|---|---|
| `TripResponse` trả `driverId` + `driverName` (hợp đồng `:1393-1394`) | ✅ **backend đúng** — chính FE là bên thiếu (P3) |
| Trường mới thêm **vào cuối** `Trip`, client cũ không vỡ (hợp đồng `:1417-1418`) | ✅ đúng thiết kế |
| Sinh chuyến hàng loạt ra đời ở trạng thái chưa phân công (hợp đồng `:1393`) | ✅ khớp |
| Luồng "đổi xe" trên màn hình (`PUT /routes/{routeId}/trips/{id}`) | ✅ gọi đúng endpoint thật, không nằm trong nhánh mock; payload gửi kèm đủ `departureTime`/`arrivalTime`/`status` nên không mất dữ liệu |
| Audit log cho thao tác phân công | ✅ có `AuditLogCoverageTests.cs` phủ cả hai endpoint (3 + 7 dòng tham chiếu) |
| Phân quyền màn hình `/trip-assignment` | ✅ `App.tsx:83` giới hạn `['Admin', 'Manager']` |
| Nút chọn dòng chỉ mở cho chuyến `Scheduled` | ✅ đúng ý định — nhưng điều kiện thứ hai dựa vào dữ liệu bịa (P1) |
| Comment "phụ xe" | ✅ `TripAssignmentPage.tsx:65` ghi đúng quy ước A8.4 (không có vai trò phụ xe) |

---

## 5. Việc chuyển cho ai

| Người | Việc | Mã |
|---|---|---|
| **Dương Thị Hạnh** | Bật `USE_MOCK = false`; gọi thật `PATCH /routes/{routeId}/trips/driver-assignment` và `/drivers?isActive=true`; trả `conflicts` lên UI | P1, P2, P5 |
| **Nguyễn Đình Băng** | Thêm `driverId`/`driverName` vào type `Trip`, xoá `AssignableTrip` + `enrichTripsWithDriver`; sửa comment `tripApi.ts:207-210`; mở nút "Phân công" cho chuyến `Running` qua `PATCH /trips/{id}/assignment` | P3, P4, P6 |
| **Nguyễn Duy Kiên** | Sửa comment "branch chưa merge" ở `TripDriverAssignmentController.cs:18-19` | P7 |
| **Hoàng Văn Thịnh** | Xử lý `package-lock.json` lạc ở gốc repo (đã treo từ Sprint 1) | P9 |
| **Scrum Master** | Chốt việc tạo môi trường online — **chưa có ai nhận** sau hai sprint, xem mục 1 | — |

**Ranh giới của tôi trong việc này:** theo mục *Kiểm thử* của bộ quy ước, tôi báo lỗi kèm bước tái
hiện và **không tự sửa mã nghiệp vụ cho test xanh**. `P1`–`P9` đều là mã nghiệp vụ hoặc comment
trong file của người khác, nên tôi **không sửa dòng nào**. Riêng `P4` tôi đã có sẵn test cho endpoint
liên quan ở nhánh `feature/14-test-api-phan-cong-dieu-xe` (39 test, chưa mở PR) — bàn giao được ngay
khi Băng mở nút.

---

## 6. Còn lại — chưa kiểm được

Ghi rõ để không ai đọc báo cáo này rồi tưởng luồng đã được kiểm đầu-cuối:

1. **Không có request HTTP thật nào được gửi.** Không có môi trường online, không chạy nổi API ở
   máy (thiếu PostgreSQL + Docker + `appsettings.Development.json`). Mọi kết luận ở mục 3 là đọc mã
   và đối chiếu hợp đồng, **trừ** mục 4 (test backend chạy thật, InMemory).
2. **Chưa kiểm luồng đầu-cuối "chọn tài xế → bấm gán → dữ liệu vào CSDL".** Không thể, vì P2 chặn
   nhánh thật và P1 khiến nhánh mock không ghi vào đâu cả. Đây là **hệ quả trực tiếp** của P1/P2,
   không phải thiếu sót của bộ test.
3. **Chưa kiểm tải đồng thời** (hai quản lý cùng gán một chuyến). Cần môi trường thật.
4. **Chưa kiểm hành vi phân trang của "chỉ chuyến chưa phân công" khi backend có tham số lọc thật** —
   tham số đó chưa tồn tại trong hợp đồng.
5. **Bộ đếm test:** 560 trên `main` — chưa gồm 39 test tôi viết cho `PATCH /trips/{id}/assignment` ở
   nhánh `feature/14-test-api-phan-cong-dieu-xe` (chưa mở PR). Sau khi PR đó merge sẽ là 599.
6. **Chưa chạy được trên trình duyệt** để xác nhận P1 bằng mắt (ba cái tên bịa hiện ở cột "Tài xế").
   Phần này suy ra từ mã ở mục 3 — nếu ai có môi trường chạy, đây là bước xác nhận rẻ nhất.
