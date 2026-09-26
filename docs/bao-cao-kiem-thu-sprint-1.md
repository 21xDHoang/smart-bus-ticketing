# Báo cáo kiểm thử chéo — Sprint 1

| | |
|---|---|
| **Người thực hiện** | Giàng A Vàng (Kiểm thử) |
| **Task** | Sprint 1, dòng 25 — *Kiểm thử chéo toàn bộ luồng Sprint 1 trên môi trường online + ghi nhận lỗi* |
| **Ngày** | 26/09/2026 |
| **Mốc so sánh** | `main` @ `05eac22` |
| **Trạng thái** | ⚠️ Chỉ hoàn thành phần kiểm thử **không cần môi trường online** — xem mục 1 |

---

## 1. Phạm vi — phần KHÔNG làm được và vì sao

Task yêu cầu kiểm thử **trên môi trường online**. Nhóm **chưa có môi trường online nào**, và đây là
việc chặn chứ không phải việc bỏ qua:

| Kiểm tra | Kết quả |
|---|---|
| `vercel.json` / `render.yaml` / `Dockerfile` / workflow deploy | **Không có file nào** |
| `Program.cs` mở CORS cho domain thật | Không — chỉ `http://localhost:5173`, và chỉ khi `IsDevelopment()` |
| `frontend/.env.example` | Chỉ có `VITE_API_URL=http://localhost:5080/api` |
| Task deploy trong backlog Sprint 1 | **Không có dòng nào** |
| PostgreSQL chạy được ở máy này | Không — không có `psql`, không có Docker, không có `appsettings.Development.json` |

**Hệ quả:** không thể chạy một request HTTP thật nào, nên **chưa kiểm thử được luồng đầu-cuối**
(đăng ký → đăng nhập → CRUD → nhật ký) trên môi trường chạy thật. Phần đó vẫn đang mở.

**Đề nghị:** tách phần "trên môi trường online" thành task riêng **có người nhận cụ thể** và **có
estimate** — theo mục G (Definition of Ready), task chỉ được nhận khi có đủ 5 thứ, và điều kiện tiên
quyết "môi trường online" hiện chưa có ai sở hữu. Người phù hợp: Phùng Duy Hoàng (hạ tầng, `.github/`).

Phần đã làm thay thế: kiểm thử tĩnh có bằng chứng chạy được — đối chiếu mã nguồn hai phía, chạy bộ
test backend, build frontend, và **thực thi lại các hàm thuần của frontend bằng Node** để kiểm chứng
hành vi thật thay vì đọc code rồi đoán.

---

## 2. Mốc chuẩn trên `main` — đều ĐẠT

| Hạng mục | Lệnh | Kết quả |
|---|---|---|
| Test backend | `dotnet test backend/SmartBus.sln -c Release` | ✅ **151/151 pass**, 0 skip, 58 s |
| Build frontend | `npm --prefix frontend run build` | ✅ exit 0 (chỉ còn cảnh báo chunk > 500 kB) |
| Lint frontend | `npm --prefix frontend run lint` | ⚠️ 1 cảnh báo — xem **L14** |
| Rác debug (`console.log`, `TODO`, `FIXME`, `debugger`) | grep toàn bộ `frontend/src` + `backend` | ✅ **không có dòng nào** |
| Bí mật bị commit | kiểm file cấu hình | ✅ chỉ có `.example`, không có `appsettings.Development.json` — đúng mục F |

---

## 3. Danh sách lỗi

Mức độ: **Cao** = sai chức năng hoặc gây hiểu nhầm nghiêm trọng · **TB** = ảnh hưởng trải nghiệm
hoặc rủi ro · **Thấp** = tài liệu, vệ sinh mã.

| # | Lỗi | Mức | Người xử lý |
|---|---|---|---|
| L1 | Tên tiếng Việt hiển thị mojibake | 🔴 Cao | Băng |
| L2 | Màn hình Danh sách người dùng không vào được | 🔴 Cao | Băng + Hạnh |
| L3 | Màn hình Cấu hình giá vé không vào được | 🔴 Cao | Băng + Hạnh |
| L4 | Màn hình Nhật ký sẽ chết ngay khi merge | 🟠 TB | Băng + Hạnh |
| L5 | Trang cá nhân hiển thị nhật ký đăng nhập **giả** | 🔴 Cao | Băng |
| L6 | Đăng ký xong không tự đăng nhập dù BE đã trả token | 🟠 TB | Hiếu + Hạnh |
| L7 | 6 test đỏ: `[Range]` trên `double` ở `StopRequest.cs` | 🔴 Cao | Hiếu |
| L8 | Lỗi 429 bị báo sai thành "Không kết nối được máy chủ" | 🟠 TB | Băng |
| L9 | Form Đăng ký cho mật khẩu 6 ký tự, BE đòi 8 + chữ&số | 🟠 TB | Hạnh |
| L10 | Trùng SĐT: `/auth/register` trả 409, `/admin/users` trả 400 | 🟡 Thấp | Hiếu |
| L11 | `PUT fares` gửi thừa `passengerType` | 🟡 Thấp | Hạnh |
| L12 | Tầng đọc nhật ký chưa nối + type thiếu 2 trường | 🟡 Thấp | Băng |
| L13 | `authApi.refreshToken` là hàm chết | 🟡 Thấp | Băng |
| L14 | CI không chạy lint, dù script có sẵn | 🟡 Thấp | Hoàng |
| L15 | Frontend không có bài test nào | 🟠 TB | Hoàng + Băng |
| L16 | Tài liệu quy ước không nằm trong repo | 🟡 Thấp | Hoàng |
| L17 | Cột `Status` trong backlog sai toàn bộ | 🟡 Thấp | Hoàng |
| L18 | Comment mô tả sai tình trạng hiện tại của mã | 🟡 Thấp | Băng |
| L19 | README mâu thuẫn quy ước về vai trò người kiểm thử | 🟡 Thấp | Hoàng |
| L20 | `package-lock.json` lạc ở gốc repo | 🟡 Thấp | Hoàng |

---

### L1 — Tên người dùng tiếng Việt hiển thị sai (mojibake) 🔴 Cao

**Ở đâu:** `frontend/src/api/authApi.ts:42-44` — Nguyễn Đình Băng (mục 2.4: `src/api/` là file dùng chung của FE).

**Chuyện gì:** `decodeAccessToken` giải mã payload JWT bằng `atob()`. `atob()` trả về chuỗi
**Latin-1**, không phải UTF-8, nên mọi ký tự có dấu trong `fullName` bị vỡ.

**Bằng chứng — chạy lại được:** mô phỏng nguyên văn logic đó bằng Node với payload đúng như
`Services/TokenService.cs:31-34` phát ra:

```
Payload gốc   : "Giàng A Vàng"
Giải mã được  : "GiÃ ng A VÃ ng"
Khớp không?   : >>> LỆCH <<<
```

**Hậu quả:** hiển thị ở **ba chỗ** — tiêu đề "Xin chào, …" (`App.tsx:29`), góc phải header
(`App.tsx:132`), và trang cá nhân (`ProfilePage.tsx:93,96`). Tên có dấu là tên của **toàn bộ** thành
viên nhóm và gần như mọi người dùng Việt Nam, nên lỗi này hiện ra với tất cả mọi người ngay màn hình
đầu sau khi đăng nhập.

**Cách sửa đề xuất** (1 dòng, giữ nguyên phần còn lại):

```ts
const bytes = Uint8Array.from(
  atob(payload.replace(/-/g, '+').replace(/_/g, '/')),
  (c) => c.charCodeAt(0),
);
const decoded = JSON.parse(new TextDecoder().decode(bytes)) as Record<string, unknown>;
```

**Bước tái hiện:** đăng nhập bằng tài khoản có họ tên chứa dấu → nhìn góc phải header.

---

### L2 — Màn hình "Danh sách người dùng" không vào được 🔴 Cao

**Ở đâu:** `frontend/src/App.tsx` — Nguyễn Đình Băng (E1: *"frontend/src/main.tsx, App.tsx — Nhắn Băng thêm route"*). Trang do Dương Thị Hạnh làm (backlog dòng 11).

**Chuyện gì:** `AdminUserListPage.tsx` **đã merge** (PR #10) nhưng `App.tsx` không import nó và không
có `<Route>` nào trỏ tới. Vì `App.tsx:182` đặt `path="*"` → `<Navigate to="/" replace />`, gõ thẳng
URL cũng bị đá về trang chủ. Không có đường nào tới màn hình này.

**Bằng chứng:** grep `AdminUserListPage` trên toàn bộ `frontend/src` chỉ ra 4 dòng — tất cả đều **nằm
trong chính file đó** (dòng 5, 6, 71, 193). Không file nào khác nhắc tới. Danh sách `<Route>` trong
`App.tsx:147-183` chỉ có `/`, `/profile`, `/stops`, `/routes`, `/route-stops`, `*`.

**Hậu quả:** chức năng quản lý người dùng của Sprint 1 (story "Phân quyền tài khoản") **không demo
được**. Cũng vì thế mà thao tác tạo tài khoản qua màn hình quản trị — vốn được `AuditLogMiddleware`
ghi nhật ký — không có cách nào thực hiện từ giao diện.

**Cách xử lý:** nhắn Băng thêm route (kèm `RouteGuard allowedRoles={['Admin']}` — `AdminUserController`
yêu cầu `RbacPolicies.AdminOnly`), **không tự sửa** theo mục E1.

---

### L3 — Màn hình "Cấu hình giá vé" không vào được 🔴 Cao

**Ở đâu:** `frontend/src/App.tsx` — cùng nguyên nhân và cùng người xử lý như L2. Trang do Dương Thị Hạnh làm (backlog dòng 23).

**Chuyện gì:** `FareConfigPage.tsx` **đã merge** (PR #18) nhưng cũng không được import ở đâu.
Grep `FareConfigPage` chỉ ra 1 dòng ngoài chính nó — `RouteStopsPage.tsx:34`, và đó chỉ là **một câu
comment nhắc tên file**, không phải import.

**Hậu quả:** nghiệp vụ "cấu hình bảng giá vé theo tuyến và đối tượng ưu đãi" của story 12 **không
demo được**, dù API `/api/routes/{routeId}/fares` đã xong và đã có test.

---

### L4 — Màn hình "Nhật ký hoạt động" sẽ chết ngay khi merge 🟠 TB (phòng ngừa)

**Ở đâu:** nhánh `feature/23-man-hinh-xem-nhat-ky-hoat-dong` (chưa merge) — Dương Thị Hạnh (dòng 32), route do Băng.

**Chuyện gì:** nhánh này thêm `AuditLogPage.tsx` + `auditLogListApi.ts`, nhưng **không đụng vào
`App.tsx`**. Kiểm bằng `git diff main...origin/feature/23-man-hinh-xem-nhat-ky-hoat-dong --stat`:
đúng 2 file, không có `App.tsx`.

**Hậu quả:** merge xong thì trang lại rơi vào đúng cái bẫy của L2/L3 — có trang, không có route, không
vào được. Nên chặn trước khi merge thay vì phát hiện lại sau.

**Ghi chú:** 3 component của story 23 (`AuditLogFilter`, `AuditLogDetailModal`, `AuditLogUserSelect`)
cũng đã merge mà chưa trang nào dùng — hiện chưa phải lỗi, vì trang dùng chúng đang ở nhánh trên.
Nhưng nếu nhánh đó bị bỏ thì 3 file này thành mã chết.

---

### L5 — Trang cá nhân hiển thị nhật ký đăng nhập **GIẢ** 🔴 Cao

**Ở đâu:** `frontend/src/api/auditLogApi.ts:67-80` — Nguyễn Đình Băng (`src/api/` + backlog dòng 35).

**Chuyện gì:** `fetchMyLoginActivity` trả về **4 bản ghi bịa cứng** — IP `203.113.188.5`,
`113.161.72.12`, mốc thời gian `Date.now() - 2 giờ`, kèm `setTimeout` 350 ms để giả độ trễ mạng.
`ProfilePage.tsx:111` hiển thị chúng dưới tiêu đề **"Nhật ký đăng nhập gần nhất"**, không có nhãn nào
cho biết đây là dữ liệu mẫu. Đây là **mock duy nhất còn chạy thật** trong toàn bộ frontend — mọi cờ
`USE_MOCK` khác đều đã tắt.

**Vì sao đây là lỗi chứ không phải "đang chờ":** comment ở `auditLogApi.ts:2-5` và `58-61` giải thích
rằng hàm này tạm trả dữ liệu giả vì *"GET /audit-logs (truy vấn danh sách) là task của Kiên, chưa
có"*. **Nhưng endpoint đó đã có rồi** — `Controllers/AuditLogsController.cs:40` (`[HttpGet]`,
`[Authorize(Policy = RbacPolicies.AdminOnly)]`), merge qua PR #29. Lý do hoãn không còn đúng nữa.

**Hậu quả:** đây là màn hình **kiểm toán an ninh**. Hiển thị lịch sử đăng nhập bịa cho người dùng
nguy hiểm hơn là để trống: người dùng tin rằng tài khoản mình chỉ có 4 lần truy cập, trong khi một
lần truy cập lạ thật sẽ không bao giờ hiện ra. Nếu không kịp nối API thật trong Sprint 1 thì tối
thiểu phải ghi rõ "dữ liệu minh hoạ" trên giao diện.

**Lưu ý kỹ thuật cho người sửa:** `GET /api/audit-logs` hiện là **AdminOnly**, nên Hành khách gọi sẽ
nhận 403. Trang cá nhân ai cũng vào được, nên nối thẳng vào endpoint đó là **chưa đủ** — cần một
endpoint "nhật ký của chính tôi" hoặc nới quyền có kiểm soát (chỉ trả bản ghi của người gọi). Đây là
quyết định về API nên phải chốt với Trần Trung Hiếu **trước** khi sửa frontend (mục 2.2 điều 5).

---

### L6 — Đăng ký xong không tự đăng nhập, dù backend đã trả token 🟠 TB

**Ở đâu:** `AuthController.cs:38-43` (Trần Trung Hiếu, dòng 3) ↔ `RegisterForm.tsx:26-34` (Dương Thị Hạnh, dòng 8).

**Chuyện gì:** Backend ghi rõ trong doc comment: *"Dữ liệu hợp lệ thì trả luôn cặp access/refresh
token — người dùng được vào hệ thống ngay sau khi đăng ký, không cần đăng nhập lại"*, và khai
`[ProducesResponseType(typeof(AuthResponse), StatusCodes.Status201Created)]`.

Frontend gọi `await authApi.register(...)` rồi **vứt bỏ kết quả**, báo *"Đăng ký tài khoản thành
công! Hãy đăng nhập ngay."* và chuyển về tab đăng nhập. Cặp token backend cất công sinh ra không
được dùng.

**Hậu quả:** tính năng "vào hệ thống ngay sau khi đăng ký" không hoạt động. Không phải lỗi chặn,
nhưng là **hai bên hiểu khác nhau về cùng một hợp đồng** — đúng loại lỗi mà kiểm thử chéo sinh ra để
tìm.

**Cần chốt:** giữ auto-login (sửa FE) hay bỏ (sửa BE cho trả 201 rỗng)? Không tự quyết.

---

### L7 — 6 test đỏ: `[Range]` trên `double` ở `StopRequest.cs` 🔴 Cao

**Ở đâu:** `backend/SmartBus.Api/Dtos/Stops/StopRequest.cs:25,29` — Trần Trung Hiếu (backlog dòng 16).

Đây là lỗi tôi đã phát hiện và báo cáo ở task 24 (nhánh `feature/12-test-api-crud-tuyen-tram-gia-ve`),
ghi lại đây để báo cáo này là một mối liên tục:

```csharp
[Range(-90, 90, ErrorMessage = "Vĩ độ phải từ -90 đến 90")]     // dòng 25
public double Latitude { get; set; }

[Range(-180, 180, ErrorMessage = "Kinh độ phải từ -180 đến 180")] // dòng 29
public double Longitude { get; set; }
```

`RangeAttribute(int, int)` đặt `OperandType = typeof(int)`, nên giá trị `double` bị **ép về `int`
trước khi so sánh** (làm tròn banker's). Kết quả: `[Range(-90, 90)]` thực chất cho qua tới
±90.4999, và `[Range(-180, 180)]` cho qua tới ±180.4999.

**Hậu quả:** API nhận toạ độ ngoài Trái Đất. Vĩ độ 90.4 là điểm không tồn tại; dữ liệu bẩn này chảy
vào bảng Stops rồi vào bản đồ Leaflet.

**Đã kiểm hệ thống — không phải lỗi rải rác:** tôi rà **toàn bộ** `[Range(...)]` trong `Dtos/`. Tất
cả chỗ còn lại đều đặt trên `int?` (`Page`, `PageSize`) nên dùng overload `int` là **đúng**.
**Chỉ duy nhất `StopRequest.cs` dính bẫy này.** Sửa đúng 2 dòng:

```csharp
[Range(-90.0, 90.0, ErrorMessage = "Vĩ độ phải từ -90 đến 90")]
[Range(-180.0, 180.0, ErrorMessage = "Kinh độ phải từ -180 đến 180")]
```

**Bước tái hiện:** `POST /api/stops` với `latitude = 90.4` → hiện trả **201 Created**, phải trả **400**
kèm `errors.latitude`. Test đã viết sẵn ở nhánh của tôi; sửa xong thì 6 test đỏ chuyển xanh.

---

### L8 — Lỗi 429 bị báo sai thành "Không thể kết nối đến máy chủ" 🟠 TB

**Ở đâu:** `frontend/src/api/axiosClient.ts:114-127` — Nguyễn Đình Băng.

**Chuyện gì:** hàm `toAppError` rẽ nhánh theo `status` cho **400, 401, 403, 404, 409, ≥500** — **không
có 429**. Status 429 rơi xuống nhánh `else if (error.request)`, mà nhánh này dành cho **lỗi mạng**.
Axios luôn gắn `request` vào `AxiosError` khi có response, nên 429 rơi đúng vào đó.

Trong khi đó `AuthController.cs:49-56` trả **429** kèm thông báo thật:

```
"Quá nhiều lần đăng ký từ thiết bị này. Vui lòng thử lại sau 1 phút."
```

**Hậu quả:** người dùng đăng ký quá 5 lần/phút được bảo *"Không thể kết nối đến máy chủ. Kiểm tra
mạng hoặc backend."* — **sai hẳn nguyên nhân**, trong khi thông báo đúng của backend bị nuốt mất.
Người dùng sẽ đi kiểm tra mạng thay vì chờ 1 phút.

**Cách sửa:** thêm một nhánh `status === 429` giữ nguyên `backendMessage`, cùng lối với 401.

**Bước tái hiện:** gọi `POST /api/auth/register` 6 lần trong 1 phút → đọc thông báo lỗi hiển thị.

---

### L9 — Form Đăng ký cho qua mật khẩu 6 ký tự, backend đòi 8 + chữ và số 🟠 TB

**Ở đâu:** `frontend/src/components/Auth/RegisterForm.tsx:93` (Dương Thị Hạnh, dòng 8) ↔ `backend/SmartBus.Api/Dtos/Auth/RegisterRequest.cs:27-28`.

| | Ràng buộc mật khẩu |
|---|---|
| Frontend | `{ min: 6 }` — *"Mật khẩu phải tối thiểu 6 ký tự!"* |
| Backend | `MinimumLength = 8` **+** `RegularExpression(@"^(?=.*[A-Za-z])(?=.*\d).+$")` — phải có **cả chữ và số** |

**Hợp đồng đứng về phía backend:** `docs/api-contract.md:324` ghi *"Ít nhất 8 ký tự, gồm **cả chữ và
số** — giống hệt form đăng ký"*.

**Hậu quả:** người dùng gõ mật khẩu `123456` hoặc `abcdefgh` → form báo hợp lệ, bấm đăng ký → mới
nhận 400 từ server. Trải nghiệm ngược: form nói "được" rồi server nói "không".

**Cách sửa:** nâng rule của `RegisterForm` lên khớp backend (min 8 + mẫu chữ-và-số).

---

### L10 — Trùng SĐT: `/auth/register` trả 409 còn `/admin/users` trả 400 🟡 Thấp

**Ở đâu:** `AuthController.cs:66-69` ↔ `AdminUserService.cs:133-138` — Trần Trung Hiếu.

**Chuyện gì:** `AuthController.Register` map **mọi** lỗi nghiệp vụ sang `Conflict` (409):

```csharp
return result.Success
    ? StatusCode(StatusCodes.Status201Created, result.Data)
    : Conflict(new { message = result.Error, errors = result.Errors });
```

Cùng điều kiện trùng SĐT, `AdminUserService` trả `ServiceResult.Invalid` → **400**.

**Điểm đáng chú ý:** hợp đồng (`docs/api-contract.md:330-331`) giải thích nó chọn **400** cho
`/admin/users` là *để "dùng chung cấu trúc lỗi theo-từng-ô với form đăng ký"* và giữ cho màn hình
quản trị không phải xử lý lỗi khác màn hình đăng ký. Nhưng form đăng ký thật lại trả **409** — nên
**chính cái nhất quán mà hợp đồng định bảo vệ lại không tồn tại**: cùng một lỗi, hai endpoint, hai mã.

**Chưa gây hỏng gì:** `RegisterForm.tsx:39-45` gắn lỗi theo `appError.errors` bất kể status, nên cả
400 lẫn 409 đều hiện đúng ở ô SĐT. Đây là lỗi **nhất quán API**, không phải lỗi giao diện.

**Cần chốt:** đổi register sang 400 (theo lý luận của hợp đồng) hay đổi admin/users sang 409? Phải
sửa `api-contract.md` trước rồi mới sửa code (mục 2.2 điều 5).

---

### L11 — `PUT fares` gửi thừa `passengerType` mà DTO không nhận 🟡 Thấp

**Ở đâu:** `frontend/src/components/FareFormModal.tsx:61` → `frontend/src/api/fareApi.ts:161-162` — Dương Thị Hạnh.

`handleOk` **luôn** gửi cả hai trường, kể cả khi đang sửa:

```ts
onSubmit({ passengerType: values.passengerType, price: values.price }, editing?.id);
```

`fareApi.update` PUT nguyên payload đó. Nhưng `backend/.../Dtos/Fares/UpdateFareRequest.cs` **chỉ có
`Price`**, và doc comment của nó giải thích rõ vì sao cố ý không nhận `PassengerType`. Hợp đồng cũng
ghi *"Body chỉ gồm `price`"*.

**Hậu quả:** **hiện chưa gây lỗi** — `System.Text.Json` mặc định bỏ qua member lạ. Nhưng request đang
đi ngược hợp đồng, và sẽ vỡ ngay nếu có ai bật chặn member lạ. Đáng sửa vì nó miễn phí.

*Ghi nhận công bằng:* chính comment ở `FareFormModal.tsx:49-50` cho thấy người viết **đã biết** PUT
chỉ đổi giá — họ chỉ gửi thừa trường chứ không hiểu sai nghiệp vụ.

---

### L12 — Tầng đọc nhật ký chưa nối, và type thiếu 2 trường 🟡 Thấp (phòng ngừa)

**Ở đâu:** `frontend/src/api/auditLogApi.ts` + `frontend/src/components/AuditLogDetailModal.tsx` — Nguyễn Đình Băng.

Hai điểm, cả hai sẽ thành lỗi ngay khi màn hình nhật ký được nối (tức là khi L4 được xử lý):

1. **`auditLogApi.ts` không import `axiosClient` lần nào** — chưa có lời gọi thật nào tới
   `GET /api/audit-logs` hay `GET /api/audit-logs/export`, dù cả hai endpoint đã sẵn sàng ở backend.
2. **Type `AuditLog` thiếu 2 trường mà API có.** Backend `AuditLogResponse.cs:25,30` trả
   `userFullName` và `userPhoneNumber`; type ở `auditLogApi.ts:19-30` chỉ khai `id, userId, action,
   target, ipAddress, createdAt`. Hệ quả: `AuditLogDetailModal.tsx:68-70` chỉ hiển thị được GUID.

**Nói cho chính xác:** modal ghi nhãn là *"Người thao tác (ID)"* và hiện GUID — **đúng như thiết kế**,
không phải lỗi hiển thị. Vấn đề chỉ là **chưa tận dụng** dữ liệu tên mà API đã trả sẵn: nhãn "Nguyễn
Văn An (0912345678)" đọc tốt hơn nhiễu cho người kiểm toán. Đây là việc nên làm khi nối API, không
phải lỗi cần sửa gấp.

*Ghi chú thêm cho người nối:* `axiosClient.ts:26` đặt `timeout: 10000`; endpoint export trả file nên
cần `responseType: 'blob'` và đọc `blob.text()` khi lỗi — chưa có code nào làm việc đó.

---

### L13 — `authApi.refreshToken` là hàm chết 🟡 Thấp

**Ở đâu:** `frontend/src/api/authApi.ts:69-70` — Nguyễn Đình Băng.

Hàm được export nhưng **không nơi nào gọi**. Việc làm mới token thật do một axios instance riêng đảm
nhiệm (`axiosClient.ts:75-77`, qua `refreshClient`) — và đó là chủ ý, vì dùng chung `axiosClient` sẽ
gây vòng lặp refresh vô hạn (đã ghi chú ở `axiosClient.ts:29-31`). Vậy hàm này thừa; nên xoá hoặc ghi
chú rõ là không dùng để người sau không tưởng còn phải gọi nó.

---

### L14 — CI không chạy lint, dù script có sẵn 🟡 Thấp

**Ở đâu:** `.github/workflows/ci.yml` — Phùng Duy Hoàng (`.github/`).

Job `frontend` chỉ chạy `npm ci` rồi `npm run build`. `package.json` **có** script `lint` (`oxlint`)
nhưng CI không gọi. Hôm nay lint chỉ ra **1 cảnh báo**:

```
src/pages/StopManagePage.tsx:34:10 warning react(set-state-in-effect)
```

Tôi đọc kỹ thì đây là **dương tính giả** — `loadStops()` là hàm `async`, `setStops` nằm sau `await`
nên không hề chạy đồng bộ trong effect. Các file khác trong repo gặp đúng tình huống này và đã tắt
cảnh báo có chủ đích bằng `// oxlint-disable-next-line react/set-state-in-effect` (xem
`RouteStopsPage.tsx:88`, `ProfilePage.tsx:57`). Chỗ này thì chưa.

**Vấn đề thật không phải cảnh báo này**, mà là **cơ chế**: lint không chạy ở CI thì cảnh báo tích
tụ âm thầm cho tới lúc không ai còn đọc. Thêm 1 bước `npm run lint` vào job frontend, và tắt cảnh
báo kia kèm lý do cho nhất quán với các file còn lại.

---

### L15 — Frontend không có bài test nào 🟠 TB (rủi ro)

**Ở đâu:** toàn bộ `frontend/` — Phùng Duy Hoàng.

`package.json` chỉ có `dev`, `build`, `lint`, `preview` — **không có `test`**. Không có `vitest`,
không có `jest`, không có file `*.test.*` / `*.spec.*` / config nào. Toàn bộ 151 test của dự án nằm ở
backend.

Điều này đáng nói vì **chính mã nguồn đã mong có test FE mà chưa bao giờ có**:

- `components/routeStopOrder.ts:10-11` — *"Hàm thuần, không đụng React … Nhờ vậy kiểm chứng được bằng jsdom"*
- `RouteStopsPage.tsx:37-39`, `A8.6` — thứ tự trạm quyết định chiều đi của tuyến, sai là **sai nghiệp vụ**

Trong khi đó Sprint 1 giao hẳn 5 task kiểm thử (dòng 13, 24, 25, 36, 37) mà **tất cả đều là xUnit
backend**. Phần giao diện — nơi có `moveStop`, `isDirty`, bộ lọc nhật ký, `RouteGuard` — không có
lưới an toàn nào, trong khi đó đúng là mảng tôi phụ trách.

**Đề xuất:** thêm `vitest` + một script `test` cho frontend thành một task riêng (cần Hoàng vì
`package.json` là file của Băng — E1 ghi *"Nhắn Băng cài thêm thư viện"*), rồi chuyển các hàm thuần
(`routeStopOrder.ts`, `auditLogFilterValue.ts`) thành test. Đây là đề xuất, không phải việc tôi tự làm.

---

### L16 — Tài liệu quy ước không nằm trong repo 🟡 Thấp

`docs/03-quy-uoc.md` và `docs/02-sprint-roadmap.md` **không tồn tại**, nhưng được tham chiếu **6 lần**
trong mã nguồn:

| File | Dòng |
|---|---|
| `backend/.../Controllers/AdminUserController.cs` | 219 |
| `backend/.../Dtos/Admin/AdminUserResponse.cs` | 6 |
| `backend/.../Dtos/Fares/FareResponse.cs` | 6 |
| `backend/.../Dtos/Routes/UpdateRouteRequest.cs` | 10 |
| `backend/.../Services/ServiceResult.cs` | 4 |
| `frontend/src/components/RouteGuard.tsx` | 31 |

Chính bản quy ước ghi nguồn của nó là *"docs/03-quy-uoc.md (bản Markdown trong repo)"* — nhưng repo
chỉ có `docs/01-kien-truc.md` và `docs/api-contract.md`. Người mới đọc code sẽ đi tìm một file không
có thật. Cần Hoàng đưa bản Markdown vào `docs/` (nguồn hiện chỉ nằm ở file `.docx` ngoài repo).

---

### L17 — Cột `Status` trong backlog sai toàn bộ 🟡 Thấp

`Product_Backlog_Smart_Bus.xlsx`, sheet **Sprint 1**: **cả 36/36 dòng đều ghi "Chưa làm"**, trong khi
rất nhiều task đã merge vào `main` (PR #10, #18, #29, #30, #31…). Task của tôi ở dòng 24 cũng vậy.

Đây không chỉ là chuyện hình thức: **mục H (Definition of Done)** yêu cầu thẳng *"Cột `Status` trong
file `Product_Backlog_Smart_Bus.xlsx` đã chuyển thành `Done`"*, và mục H cũng yêu cầu *"Thẻ task trên
GitHub Projects đã chuyển sang Done"*. Với cột này sai 100%, không ai nhìn backlog mà biết Sprint 1
đang ở đâu. Cần Hoàng cập nhật.

**Liên quan:** repo có bật GitHub Issues nhưng **chưa từng có issue nào** — toàn bộ #10–#31 đều là pull
request. Nghĩa là **chưa có kênh nào để ghi nhận lỗi** trong suốt Sprint 1. Báo cáo này đề xuất
chính nó làm kênh tạm, và nhóm nên chốt có mở issue thật hay không (quy ước B1 ghi *"Số issue là số
trên GitHub"* nhưng không có issue nào để đánh số — trên thực tế nhóm đang đặt tên nhánh theo **số
User Story**).

---

### L18 — Comment mô tả sai tình trạng hiện tại của mã 🟡 Thấp

Ba chỗ nói một điều đã không còn đúng, khiến người đọc sau bị dẫn sai:

| File | Dòng | Comment nói | Sự thật |
|---|---|---|---|
| `frontend/src/api/authApi.ts` | 64 | *"backend chưa có endpoint này (task của Hiếu, sẽ bổ sung sau)"* | `POST /api/auth/register` **đã có** — `AuthController.cs:38` |
| `frontend/src/api/auditLogApi.ts` | 2-5, 58-61 | *"GET /audit-logs … là task của Kiên, chưa có"* | **Đã có** — `AuditLogsController.cs:40` (PR #29) |
| `frontend/src/api/routeStopApi.ts` | 57 | *"file đó còn `USE_MOCK_DATA = true`"* | `stopApi.ts:28` nay là **`= false`** |

Đều thuộc `src/api/` — Nguyễn Đình Băng.

---

### L19 — README mâu thuẫn quy ước về vai trò của người kiểm thử 🟡 Thấp

`README.md:16` ghi Giàng A Vàng là **"Hạ tầng + Kiểm thử"**; quy ước mục 2.1 ghi **"Frontend, Kiểm
thử"**. Hai tài liệu trong cùng repo nói khác nhau về cùng một người. Cần chốt một bản — nếu là
"Frontend + Kiểm thử" thì README sai.

---

### L20 — `package-lock.json` lạc ở gốc repo 🟡 Thấp

Có file `package-lock.json` (98 byte, nội dung rỗng `"packages": {}`) ở **gốc repo**, đang ở trạng
thái untracked. Gốc repo **không có** `package.json`. File này không phải của tôi và **không do tôi
tạo ra** trong phiên làm việc này.

Hệ quả: ai chạy `npm ci` hoặc `npm install` ở gốc repo sẽ gặp lỗi khó hiểu (tôi đã gặp khi chạy
`npm run lint` mà quên `--prefix`). Đề nghị Hoàng quyết: xoá đi, hay thêm vào `.gitignore`. Tôi
**không tự xoá** vì không rõ nguồn gốc.

---

## 4. Đã kiểm và ĐẠT

Ghi lại để biết vùng nào đã có người soi, không phải để lấp chỗ trống.

| Hạng mục | Cách kiểm | Kết quả |
|---|---|---|
| **Đường dẫn / method / tham số query của FE ↔ hợp đồng** | đối chiếu cả 9 file `src/api/*.ts` với `api-contract.md` và toàn bộ controller | ✅ **Khớp hết** — không có endpoint, verb hay tên tham số phân trang nào sai (kể cả `pageSize=100` so với `[Range(1,100)]`) |
| **Sắp thứ tự trạm** (`moveStop`) | chạy lại hàm thật bằng Node, 6 ca | ✅ Đúng cả 6: ca trong doc `[A,B,C,D]` kéo 1→3 ra `[B,C,A,D]`; kéo lên/xuống liền kề; đầu↔cuối; đứng yên. Mảng gốc **không bị sửa**, ra ngoài khoảng trả **nguyên tham chiếu** (không render thừa) |
| **Mã vai trò FE ↔ BE** | `RoleCodes.cs` ↔ `adminUserApi.ts:12` | ✅ Khớp **chính xác** cả 4: `Admin`, `Manager`, `Driver`, `Passenger` |
| **Dữ liệu giả còn sót** | grep `USE_MOCK` toàn bộ `src/api/` | ✅ Mọi cờ đều `false` — `stopApi.ts:28`, `adminUserApi.ts:80`, `fareApi.ts:86`. Nghiệp vụ chạy API thật hết (ngoại lệ duy nhất là L5) |
| **Rác debug** | grep `console.log`/`debugger`/`TODO`/`FIXME` | ✅ Sạch ở cả FE lẫn BE |
| **Vệ sinh bí mật (mục F)** | kiểm file cấu hình | ✅ Chỉ commit `.example`; không có `appsettings.Development.json`; không có connection string trong mã |
| **Bẫy `RangeAttribute`** | rà **toàn bộ** `Dtos/` | ✅ Chỉ `StopRequest.cs` dính (L7); các chỗ khác đặt trên `int?` nên đúng |
| **Ghi nhật ký của middleware** | đọc `AuditLogMiddleware.cs` | ✅ Logic verb→action đúng; nhánh bỏ qua `/api/auth` có **ghi chú giải thích đánh đổi** (dòng 36-37) — là chủ ý, không phải sót |
| **Xử lý lỗi 401/403 của FE** | đọc `axiosClient.ts` | ✅ Gộp refresh token dùng chung 1 promise để tránh refresh-token-one-time bị thu hồi 4 lần — xử lý đúng và có ghi chú lý do |

---

## 5. Việc chuyển cho ai

| Lỗi | Người xử lý | Vì sao là người đó |
|---|---|---|
| L1, L5, L12, L13, L18 | **Nguyễn Đình Băng** | `src/api/` là file dùng chung của FE (mục 2.4) |
| L2, L3, L4 | **Nguyễn Đình Băng** (thêm route) + Dương Thị Hạnh (trang) | E1: *"App.tsx — Nhắn Băng thêm route"* |
| L6, L10 | **Trần Trung Hiếu** + **Dương Thị Hạnh** | Cần chốt hợp đồng trước khi sửa FE |
| L7 | **Trần Trung Hiếu** | Chủ `Dtos/` (backlog dòng 16) |
| L8 | **Nguyễn Đình Băng** | `axiosClient.ts` |
| L9, L11 | **Dương Thị Hạnh** | `RegisterForm`, `FareFormModal` (backlog dòng 8, 23) |
| L14, L16, L17, L19, L20 | **Phùng Duy Hoàng** | `.github/`, tài liệu, backlog |
| L15 | **Phùng Duy Hoàng** + **Nguyễn Đình Băng** | Cần thêm thư viện vào `package.json` |

**Lưu ý về xung đột #7:** quy ước ghi nhận "Route guard + xử lý 401/403" bị **hai tài liệu giao cho
hai người khác nhau** — `Huong-dan-thanh-vien` ghi Giàng A Vàng, `Product_Backlog` ghi Hoàng Văn
Thịnh — và **xung đột này chưa được giải quyết** (mục J). Tôi không tự nhận phần đó; nếu cần tôi làm
thì chốt với Scrum Master trước.

---

## 6. Còn lại — chưa kiểm được

1. **Toàn bộ luồng đầu-cuối trên môi trường chạy thật** — chặn bởi mục 1 (không có môi trường online,
   không có PostgreSQL ở máy).
2. **Hành vi thật của CORS, JWT, rate-limit, và `AuditLogMiddleware` đệm response cho POST** — chỉ
   kiểm được khi API chạy.
3. **Ghi nhật ký cho từng thao tác** (task dòng 37 — *"xác nhận mọi thao tác đều được ghi log"*) —
   chưa chạy được thao tác thật nào nên chưa xác nhận được.
4. **Kiểm thử giao diện bằng mắt** (kéo-thả, bản đồ Leaflet, modal) — cần trình duyệt và API sống.

Khi có môi trường chạy thật, 4 mục này làm được trong một buổi.
