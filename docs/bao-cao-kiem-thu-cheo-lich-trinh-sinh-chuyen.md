# Báo cáo kiểm thử chéo — luồng lịch trình → sinh chuyến

| | |
|---|---|
| **Người thực hiện** | Giàng A Vàng (Kiểm thử) |
| **Task** | Sprint 2, story 13 — *Kiểm thử chéo luồng lịch trình → sinh chuyến trên môi trường online* |
| **Ngày** | 03/10/2026 |
| **Mốc so sánh** | `main` @ `51b2795` |
| **Trạng thái** | ⚠️ Chỉ hoàn thành phần kiểm thử **không cần môi trường online** — xem mục 1 |

> Mã lỗi trong báo cáo này (**C1**…) là cục bộ cho báo cáo này, **khác** bộ `L1`–`L20` của
> `docs/bao-cao-kiem-thu-sprint-1.md`. Hai bộ không liên tục và không thay thế nhau.

---

## 1. Phạm vi — phần KHÔNG làm được và vì sao

Task yêu cầu kiểm thử **trên môi trường online**. So với báo cáo Sprint 1, khoảng trống đã **hẹp
lại nhưng chưa đóng**: người ta đã *chuẩn bị* đường deploy, chưa *tạo* môi trường nào.

| Kiểm tra | Sprint 1 | Nay |
|---|---|---|
| Dockerfile backend | Không có | ✅ **có** — `backend/Dockerfile`, thêm ở PR #41 |
| CORS mở cho domain thật | ❌ chỉ `localhost:5173`, chỉ khi `IsDevelopment()` | ✅ **đã sửa** — `UseCors` đăng ký vô điều kiện, origin đọc từ `Cors:Origins` |
| `render.yaml` / workflow deploy | Không có | ❌ **vẫn không có** — `.github/workflows/` chỉ có `ci.yml` |
| URL online ở đâu đó trong repo | Không | ❌ **không** — `git log -S "onrender"` trên toàn bộ lịch sử: **0 kết quả** |
| `frontend/.env.example` | `localhost:5080` | ❌ **vẫn vậy** — `VITE_API_URL=http://localhost:5080/api` |
| `appsettings.Production.json` | Không có | ❌ **không có** |
| Task deploy trong backlog Sprint 2 | Không có | ❌ **không có dòng nào** |
| PostgreSQL chạy được ở máy này | Không | ❌ **vẫn không** — không có `psql`, không có Docker, không có `appsettings.Development.json`, không có `dotnet user-secrets` |
| Cổng 5080 có gì đang chạy | — | ❌ không |

**Hệ quả:** vẫn **không chạy được một request HTTP thật nào** — không online, và cũng không chạy
nổi API ở máy vì thiếu cả PostgreSQL lẫn Docker. Vì vậy luồng đầu-cuối **chưa được kiểm trên môi
trường chạy thật**, và báo cáo này không tuyên bố điều ngược lại.

**Đề nghị (nhắc lại từ báo cáo Sprint 1, nay gấp hơn vì Dockerfile đã sẵn):** phần "tạo môi trường
online" cần một task **có người nhận cụ thể** — người phù hợp là **Hoàng Văn Thịnh** (`Deploy, tài
liệu sprint`) hoặc **Phùng Duy Hoàng** (`.github/`, hạ tầng). Dockerfile đã viết và CORS đã sửa,
nên phần còn lại là tạo service Render + đặt `ConnectionStrings__Default` và `Cors__Origins` qua
Environment Variables, rồi dán URL vào `frontend/.env.example`.

Phần đã làm thay thế: **kiểm thử chéo tĩnh có bằng chứng chạy lại được** — đối chiếu mã nguồn hai
phía, chạy bộ test backend, build + lint frontend, và **thực thi lại các hàm thuần của frontend
bằng Node ở hai múi giờ** để kiểm chứng hành vi thật thay vì đọc code rồi đoán.

---

## 2. Mốc chuẩn trên `main` — đều ĐẠT

| Hạng mục | Lệnh | Kết quả |
|---|---|---|
| Test backend | `dotnet test backend/SmartBus.sln -c Release` | ✅ **456/456 pass**, 0 skip, 81 s (Sprint 1: 151) |
| Build frontend | `npm --prefix frontend run build` | ✅ exit 0 (chỉ còn cảnh báo chunk > 500 kB) |
| Lint frontend | `npm --prefix frontend run lint` | ⚠️ 1 cảnh báo — đúng cảnh báo cũ `StopManagePage.tsx:34` |
| Rác debug (`console.log`, `TODO`, `FIXME`, `debugger`) | grep `frontend/src` + `backend` | ✅ không có dòng nào |
| Bí mật bị commit | kiểm file cấu hình | ✅ `appsettings.json` không có connection string; chỉ có `.example` |

---

## 3. Danh sách lỗi

Mức độ: **Cao** = sai chức năng hoặc gây hiểu nhầm nghiêm trọng · **TB** = ảnh hưởng trải nghiệm
hoặc rủi ro · **Thấp** = tài liệu, vệ sinh mã.

| # | Lỗi | Mức | Người xử lý |
|---|---|---|---|
| C1 | Không có UI sinh chuyến hàng loạt — nửa "sinh chuyến" của luồng không chạy được ở đâu cả | 🔴 Cao | Hoàng Văn Thịnh |
| C2 | Màn hình "Phân công điều xe" vẫn chạy dữ liệu GIẢ, dù backend đã xong từ lâu | 🔴 Cao | Dương Thị Hạnh |
| C3 | Type `Trip` thiếu `driverId`/`driverName` + 2 comment nói sai tình trạng backend | 🟠 TB | Nguyễn Đình Băng |
| C4 | Mở "Sửa" rồi bấm Lưu mà không đổi gì → chuyến lệch 7 giờ (khi trình duyệt không ở UTC+7) | 🟠 TB | Dương Thị Hạnh |
| C5 | Ngày của bộ lọc (Việt Nam) và ngày của trần backend (UTC) là hai ngày khác nhau | 🟡 Thấp | cần chốt — xem C5 |
| C6 | `dayBounds` chép nguyên ở 2 màn hình; `UpdateTripPayload` khai 2 lần | 🟡 Thấp | Băng + Hạnh |

**Còn tồn từ báo cáo Sprint 1** (không tính là lỗi mới, ghi để không mất dấu): `L14` (CI không chạy
lint — vẫn đúng, lint vẫn chỉ ra đúng 1 cảnh báo cũ) và `L20` (`package-lock.json` lạc ở gốc repo —
**vẫn còn**, đang untracked).

---

### C1 — Không có UI sinh chuyến hàng loạt: nửa "sinh chuyến" của luồng không chạy được ở đâu cả 🔴 Cao

**Ở đâu:** thiếu hẳn ở frontend. Task tương ứng trong backlog: sheet `Sprint 2`, story 13 —
*"UI cấu hình tần suất chạy xe theo khung giờ trong ngày"*, giao **Hoàng Văn Thịnh**, trạng thái
**"Chưa làm"**.

**Chuyện gì:** luồng "lịch trình → sinh chuyến" gồm hai bước. Bước một — thêm/sửa/huỷ từng chuyến —
đã có và chạy tốt (`TripSchedulePage`). Bước hai — chọn tần suất rồi sinh hàng loạt — **không có
lối vào nào từ giao diện**.

**Bằng chứng — chạy lại được:** grep `generate|frequencyMinutes|sinh chuyến` trên toàn bộ
`frontend/src` chỉ ra **3 dòng, không dòng nào là lời gọi API**:

| File | Dòng | Nội dung |
|---|---|---|
| `api/tripApi.ts` | 173 | một câu **comment** nhắc tên `POST/generate` |
| `api/monthlyPassApi.ts` | 131 | `generatePassCode` — hàm sinh mã vé tháng, không liên quan |
| `components/TripAssignmentModal.tsx` | 73 | comment nhắc "sinh chuyến" |

Không có hàm `generateTrips` nào. Đầu file `api/tripScheduleApi.ts:8-10` liệt kê hợp đồng mà nó phủ
— đúng ba dòng `POST`, `PUT`, `DELETE`, **không có `POST .../trips/generate`**.

**Phía backend thì đã xong và đã có test:** `RouteTripsController.Generate` +
`RouteTripsService.GenerateAsync`, phủ bởi **20 ca** trong `backend/SmartBus.Tests/RouteTripsApiTests.cs`
(69 chuyến cho 05:00→22:00 @15 phút, trần 500 chuyến/lần, trần 200 chuyến/ngày, nguyên tử khi trùng
khung giờ, quy đổi +07:00 → UTC).

**Hậu quả:** `POST /routes/{routeId}/trips/generate` — endpoint mà **quy ước A8.3 chốt thay cho bảng
`Schedules`** — hiện **không thể gọi được từ sản phẩm**. Quản lý muốn lập lịch trình cả ngày phải
bấm "Thêm chuyến" 69 lần. Nghiệp vụ chính của story 13 ("thiết lập **thời gian biểu và tần suất**
chạy xe") không demo được. Cùng loại với `L2`/`L3` của báo cáo Sprint 1: backend xong, không có
đường tới từ UI.

**Lưu ý kỹ thuật cho người làm:** phần UI là của Thịnh (backlog), nhưng hàm gọi API nên nằm ở
`api/tripScheduleApi.ts` — mà `src/api/` là **vùng dùng chung của FE** (mục 2.4), hiện do Băng giữ.
Nên **chốt với Băng** xem ai thêm `generateTrips(routeId, { busId, startTime, endTime, frequencyMinutes })`
trước khi viết, tránh hai người cùng sửa một file.

**Bước tái hiện:** đăng nhập bằng tài khoản Quản lý → menu "Quản lý lịch trình" → tìm nút sinh
chuyến theo tần suất. **Không có nút nào.**

---

### C2 — Màn hình "Phân công điều xe" vẫn chạy dữ liệu GIẢ, dù backend đã xong 🔴 Cao

**Ở đâu:** `frontend/src/api/tripAssignmentApi.ts:19` — `const USE_MOCK = true;`
Story 14, task *"Component lọc chuyến chưa phân công + phân công hàng loạt"* — **Dương Thị Hạnh**.

**Chuyện gì:** đầu file (dòng 8-14) đưa **hai lý do** để giữ nhánh dữ liệu giả. **Cả hai đều không
còn đúng**, và không còn đúng đã lâu:

| Lý do trong comment | Sự thật hôm nay |
|---|---|
| *"TripResponse chưa trả `driverId`"* | `TripResponse.cs:31` trả `DriverId`, `:40` trả `DriverName`; `RouteTripsService.cs:505,509` map cả hai, truy vấn `.Include(t => t.Driver)` ở `:118, :146, :235, :295` |
| *"chưa có endpoint gán tài xế vào chuyến"* | `TripDriverAssignmentController.cs:46` — `[HttpPatch("driver-assignment")]`, `[Authorize(Policy = RbacPolicies.ManagerOrAbove)]`, **đã có test** (`TripDriverAssignmentApiTests.cs`) |

Comment còn tự nhận ra mâu thuẫn mà không xử lý: dòng 14 ghi *"`GET /drivers?isActive=true` → danh
sách tài xế đang hoạt động (**ĐÃ có thật**)"* — tức người viết biết backend đã tiến lên, nhưng vẫn
để cờ mock bật.

**Hậu quả:** `TripAssignmentPage` **vào được** (`App.tsx:232-237`, `RouteGuard` cho Admin/Manager),
và nó đang: hiển thị danh sách tài xế **bịa** (`MOCK_DRIVERS`), lưu phân công vào một `Map` **trong
bộ nhớ phiên** (`tripAssignmentApi.ts:48`), và **không gửi gì lên server**. Quản lý phân công xong,
bấm F5 là mất sạch — mà giao diện vẫn báo thành công. Nguy hiểm hơn cả để trống: người điều hành
tin rằng chuyến đã có tài xế, trong khi chuyến đó **không có tài xế nào** trong CSDL.

Đây **đúng cùng loại với `L5`** của báo cáo Sprint 1 (nhật ký đăng nhập giả trên trang cá nhân) —
một mock còn chạy thật trong khi endpoint thật đã sẵn sàng, và lý do hoãn trong comment đã hết hiệu lực.

**Cách xử lý:** chuyển `USE_MOCK` sang `false` và nối `PATCH /routes/{routeId}/trips/driver-assignment`.
Cần **C3 xong trước** (type `Trip` phải có `driverId`/`driverName`). Nếu chưa kịp nối API trong
sprint này thì tối thiểu phải **ghi rõ "dữ liệu minh hoạ" trên giao diện** — không để một màn hình
điều hành hiển thị phân công không có thật.

**Bước tái hiện:** vào "Phân công điều xe" → gán một tài xế cho một chuyến → F5 → phân công biến mất.

---

### C3 — Type `Trip` thiếu `driverId`/`driverName` + 2 comment nói sai tình trạng backend 🟠 TB

**Ở đâu:** `frontend/src/api/tripApi.ts` — Nguyễn Đình Băng (`src/api/`).

**Chuyện gì:** `TripResponse` của backend có **15 trường**; type `Trip` (`tripApi.ts:25-42`) khai
**13** — thiếu đúng `driverId` và `driverName`.

Kéo theo **hai comment mô tả sai sự thật**, cả hai đều dẫn người đọc sau đi sai:

| File | Dòng | Comment nói | Sự thật |
|---|---|---|---|
| `api/tripApi.ts` | 208-210 | *"khi backend trả thêm `driverId` trên `TripResponse` thì chỉ cần đổi thành `t => t.driverId`"* | `TripResponse` **đã** trả `driverId` — đổi được ngay |
| `components/UnassignedTripsFilter.tsx` | 12-13 | *"TripResponse hiện chưa trả `driverId` (chờ task 113 của Kiên) nên chưa thể đẩy xuống query string `unassigned=true`"* | **đã** trả `driverId` |

**Hậu quả:** cảnh báo trùng lịch **tài xế** trên UI (story 14) đang bị khoá lại một cách không cần
thiết. `findConflictingTrips` (`tripApi.ts:214-228`) đã được viết **tổng quát**, nhận tham số
`resourceIdOf`; comment nói rõ chỉ cần đổi `t => t.busId` thành `t => t.driverId`. Nghĩa là: hiện
chỉ cảnh báo trùng **xe**, chưa cảnh báo trùng **tài xế** — dù backend đã trả đủ dữ liệu để làm cả hai.

Đây là **cùng loại với `L18`** của báo cáo Sprint 1 (comment mô tả sai tình trạng hiện tại của mã) —
loại lỗi nhóm đã công nhận là thật. Khác một điểm: lần này hậu quả không chỉ là đọc nhầm, mà là
**một tính năng bị chặn**.

**Nói cho chính xác:** `AssignableTrip extends Trip` (`tripAssignmentApi.ts:32-37`) đã lách bằng
cách tự khai thêm 2 trường, nên **không có lỗi biên dịch** nào. Vấn đề là hai nguồn sự thật cho cùng
một hình dạng dữ liệu, và nguồn ở `tripApi.ts` đang thiếu.

**Cách xử lý:** thêm `driverId: string | null` và `driverName: string | null` vào `Trip`; sửa lại
hai comment; rồi `AssignableTrip` chỉ còn là bí danh.

---

### C4 — Mở "Sửa" rồi bấm Lưu mà không đổi gì → chuyến lệch 7 giờ 🟠 TB

**Ở đâu:** `frontend/src/components/TripScheduleFormModal.tsx:21-23` (hàm `toVietnamIso`) cùng dòng
68 (`dayjs(editing.departureTime)`) — Dương Thị Hạnh.

**Chuyện gì:** giá trị giờ được **hiển thị theo múi giờ của trình duyệt** (dayjs mặc định), nhưng
khi gửi lên lại **gắn cứng offset `+07:00`**. Hai phía lệch nhau đúng bằng chênh lệch giữa múi giờ
trình duyệt và UTC+7.

**Bằng chứng — chạy lại được:** chép nguyên văn `toVietnamIso` + `dayBounds` + `tripsOverlap` của
frontend sang Node và chạy hai lần với `TZ` khác nhau, dùng một chuyến thật trong CSDL
`2026-10-01T22:00:00Z` (= 05:00 ngày 02/10 giờ Việt Nam):

```
─────────────────── TZ = UTC ───────────────────
[2] Bảng hiển thị : 22:00 ngày 01/10/2026
    Hiển thị đúng giờ VN (05:00 ngày 02/10)? SAI
[3] Mở "Sửa" rồi bấm Lưu, không đổi gì:
    Giờ gốc trong CSDL : 2026-10-01T22:00:00Z
    Giờ form gửi lại   : 2026-10-01T22:00:00+07:00 -> 2026-10-01T15:00:00Z
    Khớp không?        : >>> LỆCH -7 GIỜ <<<

─────────────── TZ = Asia/Ho_Chi_Minh ──────────────
[2] Bảng hiển thị : 05:00 ngày 02/10/2026
    Hiển thị đúng giờ VN (05:00 ngày 02/10)? ĐÚNG
[3] Mở "Sửa" rồi bấm Lưu, không đổi gì:
    Giờ gốc trong CSDL : 2026-10-01T22:00:00Z
    Giờ form gửi lại   : 2026-10-02T05:00:00+07:00 -> 2026-10-01T22:00:00Z
    Khớp không?        : >>> KHỚP <<<
```

**Hậu quả:** với máy ở UTC+7 (máy của cả nhóm) **không thấy gì** — đó là lý do lỗi này chưa ai phát
hiện. Nhưng ở máy đặt múi giờ khác (UTC, hoặc bất kỳ nước nào), thao tác **vô hại nhất** — mở form
Sửa rồi bấm Lưu mà không sửa gì — **làm chuyến chạy lệch 7 giờ**, và bấm lặp lại thì lệch tiếp.
Dữ liệu sai âm thầm, không có thông báo lỗi nào.

**Điểm đáng nói:** comment ngay trên hàm (`TripScheduleFormModal.tsx:12-15`) khẳng định *"người dùng
nhìn thấy đúng giờ mình chọn, **không phụ thuộc múi giờ của trình duyệt người xem**"* — **đúng
ngược lại**: cách làm này phụ thuộc hoàn toàn vào múi giờ trình duyệt. Bảng `TripListByDayPage.tsx:28-34`
cũng khẳng định y hệt cho phần hiển thị. Hai comment đang ghi nhận một bất biến mà mã không giữ.

**Cách sửa đề xuất:** hoặc gửi **đúng thời điểm** (`value.toISOString()` — `DatePicker` đã giữ sẵn
thời điểm, không cần tự nối offset), hoặc cố định cả hiển thị lẫn gửi về UTC+7 (dayjs
`.utcOffset(420)`). Cách thứ nhất ít mã hơn và không bao giờ lệch.

**Bước tái hiện:** đổi múi giờ hệ điều hành sang UTC → mở "Quản lý lịch trình" → Sửa một chuyến →
bấm Lưu, không đổi gì → giờ khởi hành lùi 7 tiếng.

---

### C5 — Ngày của bộ lọc (Việt Nam) và ngày của trần backend (UTC) là hai ngày khác nhau 🟡 Thấp

**Ở đâu:** `frontend/src/pages/TripListByDayPage.tsx:45-51` và `TripSchedulePage.tsx:41-47` (hàm
`dayBounds`, ngày Việt Nam) ↔ `backend/.../RouteTripsService.cs:183` và `:370-371` (trần 200
chuyến tính theo **ngày UTC**).

**Chuyện gì:** hai phía hiểu "một ngày" là hai khoảng khác nhau:

| | Khoảng của một ngày |
|---|---|
| Bộ lọc frontend (ngày VN) | `02/10 00:00+07:00` → `02/10 23:59:59+07:00` = **`01/10 17:00Z` → `02/10 16:59Z`** |
| Trần backend (ngày UTC) | `02/10 00:00Z` → `02/10 23:59Z` |

**Hậu quả:** ngày Việt Nam vắt qua **hai** ngày UTC, nên một ngày trên giao diện có thể chứa tới
**~400 chuyến** (200 của mỗi ngày UTC) trong khi quy tắc nói 200/ngày. Và khi trần chặn thật, thông
báo lỗi nêu **ngày UTC** — `"Tuyến đã đạt trần 200 chuyến trong ngày 2026-10-02 (UTC)"` — trong khi
người quản lý đang nhìn lịch **ngày Việt Nam**, có thể là 02/10 hoặc 03/10. Thông báo đúng nhưng
không khớp với thứ người dùng đang thấy.

**Nói cho công bằng:** backend **đúng theo hợp đồng** (hợp đồng ghi rõ trần tính theo ngày UTC) và
**có ghi rõ "(UTC)"** trong thông báo — đây không phải lỗi backend. Vấn đề là **hai phía không khớp
nhau**, và người dùng không có cách nào nhìn ra điều đó từ giao diện.

**Cần chốt (không tự quyết):** giữ trần theo UTC rồi **hiển thị số chuyến/ngày và ghi chú "(UTC)"**
trên màn hình lịch trình, hay đổi trần sang ngày Việt Nam? Nếu đổi thì phải **sửa `api-contract.md`
trước** rồi mới sửa code (mục 2.2 điều 5), và người sửa là Trần Trung Hiếu (`RouteTripsService`).

---

### C6 — `dayBounds` chép nguyên ở 2 màn hình; `UpdateTripPayload` khai 2 lần 🟡 Thấp

**Ở đâu:** frontend.

1. **`dayBounds` + `VIETNAM_OFFSET` chép y nguyên** ở `pages/TripListByDayPage.tsx:28-51` và
   `pages/TripSchedulePage.tsx:37-47` — cùng hằng số, cùng thân hàm, khác nhau chỉ ở comment.
   Sửa một chỗ quên chỗ kia là hai màn hình lọc ra hai kết quả khác nhau cho cùng một ngày.
2. **`UpdateTripPayload` khai hai lần** với **độ bắt buộc khác nhau**:
   `api/tripApi.ts:141-150` (`arrivalTime: string | null` — bắt buộc) và `api/tripScheduleApi.ts:31-34`
   (`arrivalTime?: string | null` — tuỳ chọn, thừa kế từ `CreateTripPayload`).

   Hiện hai bản **tương thích** nhau nên không gây lỗi biên dịch, nhưng đây là hai nguồn sự thật cho
   cùng một body. Lần đầu tiên ai đó sửa một bản mà quên bản kia, `TripScheduleFormModal` và
   `TripAssignmentModal` sẽ gửi hai hình dạng body khác nhau tới cùng một endpoint.

**Vì sao vẫn ghi:** không phải lỗi chức năng, nhưng đúng loại nợ kỹ thuật mà báo cáo Sprint 1 đã
tính vào (`L11`, `L13`). Cần **Băng + Hạnh chốt** bản nào là bản chính, rồi xoá bản còn lại — cần
chốt với nhau vì `src/api/` là vùng dùng chung.

---

## 4. Đã kiểm và ĐẠT

Ghi lại để biết vùng nào đã có người soi, không phải để lấp chỗ trống.

| Hạng mục | Cách kiểm | Kết quả |
|---|---|---|
| **Luật trùng khung giờ FE ↔ BE** | chép `tripsOverlap` (`tripApi.ts:196-203`) sang Node, so với luật SQL của `RouteTripsService.cs:200-201`, **6 ca** | ✅ **Khớp 6/6** — trùng đúng giờ khởi hành, chồng lấn một phần, nối đuôi (không tính trùng), cách xa, mốc nằm trong / ngoài khung giờ |
| **Route của 3 màn hình trong `App.tsx`** | đọc `App.tsx:212-237` | ✅ Cả `TripListByDayPage`, `TripSchedulePage`, `TripAssignmentPage` **đều có `<Route>` + `RouteGuard`** — không dính bẫy `L2`/`L3` |
| **`GET /buses` (ô chọn xe) ↔ `fetchActiveBuses`** | đối chiếu `BusesController.cs`, `ListBusesRequest`, `BusResponse` với `tripScheduleApi.ts:64-88` | ✅ Khớp hết: quyền `ManagerOrAbove` (khớp `RouteGuard` Admin/Manager), `status`/`page`/`pageSize` với `[Range(1,100)]` đúng trần FE dùng, response có đủ `licensePlate`/`busType`/`capacity` |
| **Lỗi theo từng ô có tới đúng ô input không** | đối chiếu tên khoá `errors` của BE với tên field của form | ✅ `departureTime` / `arrivalTime` / `endTime` / `busId` / `status` đều là tên ô trong `TripScheduleFormModal` — gắn lỗi vào đúng ô |
| **Quy đổi múi giờ phía gửi lên** | `DateTimeOffset?` trong `CreateTripRequest`/`UpdateTripRequest` + test backend | ✅ Chuỗi `+07:00` của FE được quy đúng về UTC (đã chứng minh ở `RouteTripsApiTests`, ca `Gio_gui_kem_mui_gio_duoc_quy_ve_utc`) |
| **Trần 500 chuyến/lần và 200 chuyến/ngày** | đọc `RouteTripsService.cs:39,46,183-189,379-388` | ✅ Sinh nguyên tử (một chuyến trùng thì không chuyến nào được tạo), và thông báo ghi rõ "(UTC)" |
| **Vòng lặp phân trang ở các ô chọn** | `fetchActiveBuses`, `fetchRouteOptions`, `fetchAllTripsForDay` | ✅ Đều lặp theo trang tới `total`, đều có chốt `items.length === 0` để không lặp vô hạn |

---

## 5. Việc chuyển cho ai

| Lỗi | Người xử lý | Vì sao là người đó |
|---|---|---|
| **C1** | **Hoàng Văn Thịnh** | Backlog sprint 2 giao thẳng task UI tần suất cho Thịnh. Cần chốt thêm với **Băng** nếu hàm gọi API đặt ở `src/api/` (vùng dùng chung) |
| **C2** | **Dương Thị Hạnh** | `tripAssignmentApi.ts` ghi rõ story 14, task 120 của Hạnh |
| **C3** | **Nguyễn Đình Băng** | `src/api/` là vùng dùng chung của FE (mục 2.4) |
| **C4** | **Dương Thị Hạnh** | `TripScheduleFormModal.tsx` là file của màn hình lịch trình (Dương Thị Hạnh, backlog sprint 2) |
| **C5** | **cần chốt trước** | Nếu đổi trần sang ngày VN → sửa `api-contract.md` rồi **Trần Trung Hiếu** (`RouteTripsService`) |
| **C6** | **Băng + Hạnh** | Hai bên cùng chốt bản `UpdateTripPayload` nào là chính |

---

## 6. Còn lại — chưa kiểm được

1. **Toàn bộ luồng đầu-cuối trên môi trường chạy thật** — chặn bởi mục 1 (không có môi trường
   online, và ở máy cũng không chạy nổi API vì thiếu PostgreSQL/Docker).
2. **`POST .../trips/generate` chạy thật** — endpoint đã có test nhưng **chưa từng được gọi từ
   giao diện** (C1), nên chưa biết nó chạy thế nào khi có người dùng thật bấm.
3. **Hành vi thật của CORS, JWT, phân quyền theo vai trò trên môi trường deploy** — chỉ kiểm được
   khi API chạy.
4. **Kiểm thử giao diện bằng mắt** (DatePicker, modal, Popconfirm huỷ chuyến) — cần trình duyệt và
   API sống.

Khi có môi trường chạy thật, 4 mục này làm được trong một buổi — và khi đó C1, C2 mới lộ hết hậu quả.
