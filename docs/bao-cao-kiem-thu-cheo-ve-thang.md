# Báo cáo kiểm thử chéo — luồng vé tháng (đăng ký · gia hạn · hết hạn)

| | |
|---|---|
| **Người thực hiện** | Giàng A Vàng (Kiểm thử) |
| **Task** | Sprint 2, story 16 — *Kiểm thử chéo luồng vé tháng trên môi trường online* (dòng r49) |
| **Ngày** | 04/10/2026 |
| **Mốc so sánh** | `main` @ `3ecc099` |
| **Trạng thái** | ⚠️ Chỉ hoàn thành phần kiểm thử **không cần môi trường online** — xem mục 1 |

> Mã lỗi trong báo cáo này (**V1**…) là cục bộ cho báo cáo này, **khác** bộ `C1`–`C6` của
> `docs/bao-cao-kiem-thu-cheo-lich-trinh-sinh-chuyen.md`, bộ `P1`–`P9` của
> `docs/bao-cao-kiem-thu-cheo-phan-cong.md` và bộ `L1`–`L20` của
> `docs/bao-cao-kiem-thu-sprint-1.md`. Bốn bộ không liên tục và không thay thế nhau.
>
> **V7 lặp lại ở báo cáo thứ tư** (`L20` → `P9` → (báo cáo tra cứu) `N9` → `V7`). Ghi lại là bằng
> chứng về việc nó vẫn chưa được xử lý, không phải lỗi mới.

---

## 1. Phạm vi — phần KHÔNG làm được và vì sao

Task yêu cầu kiểm thử **trên môi trường online**. Từ 03/10/2026 nhóm đã có **một nửa** thứ mà ba
báo cáo trước đòi: một CSDL PostgreSQL dùng chung trên Supabase. Nhưng nửa còn lại — API chạy ở
đâu đó ngoài máy cá nhân — **vẫn chưa có**. "Online" hiện tại nghĩa là *dữ liệu dùng chung*, không
phải *hệ thống chạy chung*.

| Kiểm tra | Báo cáo story 13 | Báo cáo story 14 | Nay |
|---|---|---|---|
| CSDL dùng chung cho cả nhóm | ❌ mỗi máy một bản | ❌ vẫn vậy | ✅ **MỚI** — Supabase, `docs/25-huong-dan-csdl-chung.md` |
| API chạy ngoài máy cá nhân | ❌ không | ❌ không | ❌ **vẫn không** — tài liệu vẫn ghi `http://localhost:5080` |
| `render.yaml` / `fly.toml` / `Procfile` / `vercel.json` / `docker-compose.yml` | ❌ không | ❌ không | ❌ **vẫn không** |
| Workflow deploy | ❌ chỉ `ci.yml` | ❌ chỉ `ci.yml` | ❌ **vẫn chỉ `ci.yml`** |
| URL online ở đâu đó trong repo | ❌ không | ❌ không | ❌ **không** — grep `onrender\|vercel.app\|netlify.app\|fly.io\|azurewebsites\|railway.app\|herokuapp` toàn repo (trừ `node_modules`, `dist`): **0 kết quả thật** |
| `appsettings.Production.json` | ❌ không | ❌ không | ❌ **vẫn không** |
| `frontend/.env.example` | `localhost:5080` | `localhost:5080` | ❌ **vẫn vậy** |
| Dockerfile backend | ✅ có | ✅ có | ✅ vẫn có — nhưng **chưa từng được deploy** |
| Task deploy trong backlog | ❌ không | ❌ không | ❌ **không có dòng nào** |

**Thêm một rào chắn mới ở chính máy này:** `backend/SmartBus.Api/appsettings.Development.json`
**không tồn tại**, và chuỗi kết nối Supabase nằm trong tin nhắn ghim của chat nhóm (cố ý — repo
public). Nên ngay cả việc trỏ API ở máy vào CSDL chung cũng không làm được từ đây.

**Hệ quả:** không chạy được một request HTTP thật nào *qua mạng*. Phần đã làm thay thế — như ba báo
cáo trước — là **kiểm thử chéo có bằng chứng chạy lại được**:

1. **Đo trên pipeline HTTP thật** bằng `TestAppFactory` (app thật + EF InMemory), cho cả bốn tình
   huống: có token hành khách, không token, token Admin. Số liệu ở mục 2.
2. **Chạy lại được hai phép tính ngày** trên chính hai thư viện mà hai phía đang dùng
   (JS `Date.setMonth` của frontend, `dayjs` của frontend) để so với .NET `AddMonths` của backend.
3. Đối chiếu ba phía: mã nguồn frontend ↔ mã nguồn backend ↔ `docs/api-contract.md`.

---

## 2. Mốc chuẩn trên `main` @ `3ecc099`

| Hạng mục | Lệnh | Kết quả |
|---|---|---|
| Test backend | `dotnet test backend/SmartBus.Tests/SmartBus.Tests.csproj` | ✅ **599/599 pass**, 0 fail, 0 skip, 1 m 24 s (story 14: 560) |
| Build frontend | `npm --prefix frontend run build` | ✅ exit 0 — chỉ còn cảnh báo chunk > 500 kB |
| Lint frontend | `npm --prefix frontend run lint` | ⚠️ 1 cảnh báo — đúng cảnh báo cũ `StopManagePage.tsx:34`, **ngoài** luồng vé tháng |
| Rác debug | grep `console.log` / `debugger` / `TODO` / `FIXME` trong `frontend/src` + `backend/SmartBus.Api` | ⚠️ **1 dòng `TODO`** — `tripAssignmentApi.ts:90`, **ngoài** luồng vé tháng |
| Bí mật bị commit | `git ls-files` lọc `.env`, `appsettings.Development.json`, `*.pfx`, `*.pem`, `id_rsa` | ✅ sạch |

### 2.1 Đo trên pipeline HTTP thật — bảng số liệu

Dựng một hành khách, một tuyến, một loại vé và một vé tháng còn hạn trong CSDL test, rồi gọi đúng
những endpoint mà **module vé tháng của frontend sẽ gọi khi bỏ cờ dữ liệu giả**:

| Endpoint | Hành khách | Không token | Admin |
|---|---|---|---|
| `GET /api/routes` (danh sách tuyến cho ô chọn) | **403** | — | **200** |
| `GET /api/routes?status=Active&page=1&pageSize=100` (đúng tham số FE dùng) | **403** | — | **200** |
| `GET /api/monthly-passes/me` | **200** ✅ | 401 | — |
| `POST /api/monthly-passes` (**đăng ký** — FE gọi khi bấm nút) | **404** | **404** | — |
| `POST /api/monthly-passes/{id}/renew` (**gia hạn**) | **200** ✅ | — | — |
| `GET /api/pass-types` (FE dự kiến gọi) | **404** | — | — |

Hai chi tiết đáng chú ý trong bảng:

- `POST /api/monthly-passes` trả **404 kể cả khi không gửi token**. Endpoint có `[Authorize]` sẽ trả
  **401** trước. Ra 404 nghĩa là **routing không khớp được route nào** — đây là bằng chứng cơ học
  rằng endpoint đăng ký *không tồn tại*, không phải "tồn tại nhưng chặn quyền".
- `GET /api/routes` với **đúng tham số** mà `monthlyPassApi.ts` gửi vẫn 403 với hành khách. Không
  phải vấn đề tham số — vấn đề là quyền.

Nội dung 403: `{"message":"Bạn không có quyền truy cập tính năng này."}`.

---

## 3. Danh sách lỗi

Mức độ: **Cao** = sai chức năng hoặc gây hiểu nhầm nghiêm trọng · **TB** = ảnh hưởng trải nghiệm
hoặc rủi ro · **Thấp** = tài liệu, vệ sinh mã.

| # | Lỗi | Mức | Người xử lý |
|---|---|---|---|
| V1 | Màn vé tháng là màn **của hành khách**, nhưng danh sách tuyến gọi `GET /routes` — hành khách **403** | 🔴 Cao | Dương Thị Hạnh + Trần Trung Hiếu |
| V2 | `addMonths` cộng tháng **sai** — màn hình **tự mâu thuẫn**, và cả hai đều lệch backend | 🔴 Cao | Dương Thị Hạnh |
| V3 | Bảng `PassTypes` **không được seed** (0 `InsertData`), nhưng màn chào 4 loại vé kèm giá bịa | 🟠 TB | Vàng Thị Dăm + Trần Trung Hiếu |
| V4 | Hai component của story 16 (r46, r47) là **code chết** — không màn nào render, không hàm nào gọi `GET /monthly-passes/me` | 🟠 TB | Hoàng Văn Thịnh + Nguyễn Đình Băng |
| V5 | **Năm comment nói sai** tình trạng backend; một trong số đó đang là công tắc chặn cả tính năng | 🟠 TB | Dương Thị Hạnh · Băng · Thịnh |
| V6 | `USE_MOCK_DATA` là hằng cờ ở tầng module — **nhánh API thật chưa từng chạy lần nào**, đó là lý do V1 và V3 nằm im | 🟡 Thấp | Dương Thị Hạnh |
| V7 | `package-lock.json` lạc ở gốc repo (**lần thứ tư**) | 🟡 Thấp | Hoàng Văn Thịnh |

> **Về việc gán người xử lý:** các file frontend ở đây không ghi tên người sở hữu trong đầu file.
> Tên người được suy từ bảng phân công Sprint 2 (`Product_Backlog_Smart_Bus.xlsx`): r44 màn đăng ký
> vé tháng = Dương Thị Hạnh, r45 màn quản lý vé tháng của tôi = Hoàng Văn Thịnh, r46/r47 component
> trạng thái + nhắc nhở = Nguyễn Đình Băng; `App.tsx` thuộc Nguyễn Đình Băng (mục E1). Cần đối
> chiếu lại nếu phân công đã đổi.

---

### V1 — Màn vé tháng phục vụ hành khách, nhưng tải danh sách tuyến bằng `GET /routes` — hành khách bị 403 🔴 Cao

**Đường đi của lỗi:**

1. `MonthlyPassRegistrationPage.tsx:53` — `setRoutes(await monthlyPassApi.listRoutes())`
2. `monthlyPassApi.ts:175` — `listRoutes: async () => fetchActiveRoutes()`
3. `monthlyPassApi.ts:149-153` — `fetchActiveRoutes()` gọi `fetchRoutes({ page, pageSize, status: 'Active' })`
4. `routeApi.ts:91` — `fetchRoutes` → `axiosClient.get('/routes', { params })`
5. `RoutesController.cs:18` — `[Authorize(Policy = RbacPolicies.ManagerOrAbove)]` **ở mức class**
6. Đo được: hành khách → **403**, Admin → **200** (mục 2.1)

**Vì sao đây là lỗi chứ không phải "chờ API":** chính màn hình tự khai nó dành cho hành khách —
`MonthlyPassRegistrationPage.tsx:34-35`:

> *"Đây là màn hình cho HÀNH KHÁCH — không giới hạn vai trò như các màn hình quản trị."*

Và `docs/25-huong-dan-csdl-chung.md:143` có một dòng cảnh báo đúng cho màn tra cứu nhưng **quên
màn này**:

> *"Màn 'Tra cứu tuyến' báo không đủ quyền (403) | Màn này đọc `GET /routes` — chỉ Manager/Admin"*

**Các bước tái hiện (không cần môi trường online):**

1. Trong `frontend/src/api/monthlyPassApi.ts:116`, đổi `const USE_MOCK_DATA = true` → `false` — đúng
   việc mà comment ngay trên đó (`:115`) dặn người sau làm.
2. Đăng nhập vai trò Passenger.
3. Mở `/monthly-passes`.

**Kết quả hiện tại:** ô "Tuyến xe" rỗng và hiện `message.error` *"Không tải được danh sách tuyến."*
(đường `MonthlyPassRegistrationPage.tsx:55`). Nút "Đăng ký vé tháng" bị `disabled` vì
`!selectedRoute` (`:192`) — màn hình chết hoàn toàn với đúng vai trò nó phục vụ.

**Hướng sửa — cần một quyết định của nhóm, không phải sửa một dòng:**
`GET /routes` là endpoint quản trị. Một màn hành khách **không nên** đọc nó. Chọn một trong hai:

- **(a)** Trần Trung Hiếu thêm endpoint tra cứu tuyến công khai / cho hành khách (đúng việc mà
  `25-huong-dan-csdl-chung.md:138` đã ghi là đang chờ), rồi Hạnh trỏ `fetchActiveRoutes()` sang đó; hoặc
- **(b)** endpoint đăng ký vé tháng trả kèm danh sách tuyến hợp lệ.

**Đây cùng gốc với `N1`** của báo cáo kiểm thử chéo luồng tra cứu (`feature/1-kiem-thu-cheo-tra-cuu`,
chưa merge): cùng một endpoint quản trị bị dùng làm nguồn dữ liệu cho màn hành khách.

---

### V2 — `addMonths` cộng tháng sai: màn hình tự mâu thuẫn, và cả hai vế đều lệch backend 🔴 Cao

Cùng một màn hình, cùng một phép tính "cộng N tháng", nhưng dùng **hai thư viện khác nhau** cho hai
chỗ hiển thị:

| Chỗ | Thư viện | 31/01 + 1 tháng |
|---|---|---|
| Ô xem giá trước khi bấm (**preview**) — `MonthlyPassRegistrationPage.tsx:75` | `dayjs().add(1, 'month')` | **28/02** ✅ |
| Thẻ kết quả sau khi bấm (**mock**) — `monthlyPassApi.ts:124-128, 231` | JS `Date.setMonth` | **03/03** ❌ |
| Backend thật — `MonthlyPassRenewalService` | .NET `DateTime.AddMonths` | **28/02** ✅ |

Ba dòng trên đo được, không phải suy đoán:

```
JS  Date.setMonth : 2027-01-31 +1m = 2027-03-03     ← monthlyPassApi.ts đang làm thế này
dayjs add(1,month): 2027-01-31 +1m = 2027-02-28     ← màn hình preview làm thế này (dayjs 1.11.23)
.NET AddMonths    : 2027-01-31 +1m = 2027-02-28     ← backend làm thế này (đã kiểm bằng test xUnit)
```

**Vì sao 03/03:** `Date.setMonth` cộng theo chỉ số tháng rồi để JS tự tràn — tháng 1 ngày 31 cộng 1
thành "tháng 2 ngày 31", không tồn tại, nên tràn sang 03/03. `dayjs` và .NET thì **kẹp** về ngày cuối
tháng. Đây đúng là cái bẫy mà chính repo đã ghi ra — `backend/SmartBus.Api/Entities/PassType.cs`:

> *"cộng tháng mới đúng ý người mua (31/01 + 1 tháng = 28/02, **không phải 03/03**)"*

**Các bước tái hiện:**

1. Mở `/monthly-passes`, chọn một tuyến bất kỳ và loại vé "Vé tháng 1 tháng".
2. Ô "xem giá" ghi: *"Hiệu lực từ 31/01/2027 đến 28/02/2027"* — nhưng đó là ngày hệ thống; để thấy
   đúng ca biên, sửa tạm đồng hồ máy sang **31/01** rồi lặp lại.
3. Bấm "Đăng ký vé tháng" → thẻ kết quả ghi *"Hiệu lực 31/01/2027 → 03/03/2027"*.

Màn hình tự nói hai ngày kết thúc khác nhau cho cùng một vé. Khi `USE_MOCK_DATA` thành `false`, ô
preview (28/02) mới là bên khớp backend, còn thẻ kết quả lấy ngày từ backend nên cũng về 28/02 —
tức là **lỗi này tự khỏi khi bỏ mock**, nhưng trong lúc mock còn bật thì màn hình đang dạy người
xem một quy tắc ngày tháng khác với quy tắc thật.

**Hướng sửa:** thay `addMonths` bằng `dayjs(date).add(months, 'month').toDate()` — cùng thư viện
với phần preview, và cùng quy tắc kẹp với backend. (Dương Thị Hạnh.)

---

### V3 — Bảng `PassTypes` không được seed, nhưng màn hình chào 4 loại vé kèm giá 🟠 TB

**Bằng chứng:**

- Migration `backend/SmartBus.Api/Migrations/20261001133209_Sprint2_MonthlyPasses_PassTypes.cs`:
  `grep -c InsertData` → **0**. Bảng được `CreateTable` rồi để **rỗng**.
- `docs/api-contract.md` — không có mục `/pass-types`. Đo `GET /api/pass-types` → **404**.
- `monthlyPassApi.ts:12-13` dự kiến *"`GET /pass-types` → `PassType[]` (bảng tham chiếu loại vé, task
  của Dăm)"* — endpoint này **không có trong hợp đồng và không tồn tại**.
- `monthlyPassApi.ts:96-101` giữ bảng giá **ở client**: OneMonth 200 000 · ThreeMonths 550 000 ·
  SixMonths 1 000 000 · TwelveMonths 1 800 000.
- `MonthlyPassRegistrationPage.tsx:173` hiển thị `formatVnd(selectedPassType.price)` — con số này
  chưa từng đi qua backend.

**Vì sao nghiêm trọng:** backend tra loại vé **theo `Code`** trong bảng `PassTypes`. Bảng rỗng ⇒ mọi
`passTypeCode` hợp lệ theo mắt frontend đều tra ra `null` ⇒ **404 `"Không tìm thấy loại vé"`**. Ca này
đã được khoá lại bằng test ở nhánh `feature/16-test-api-ve-thang`
(`MonthlyPassRenewalApiTests.PassTypeCode_khong_khop_loai_ve_nao_tra_404`) — nếu bảng rỗng, **mọi**
lần đăng ký/gia hạn đều rơi vào nhánh đó.

Nói cách khác: khi Trần Trung Hiếu làm xong `POST /monthly-passes`, tính năng **vẫn chưa chạy được**
cho tới khi có người seed `PassTypes` — và giá thì không phải quyết định kỹ thuật. Chính
`PassType.cs` đã ghi rõ việc **cố ý** không seed:

> *"Bảng này cũng KHÔNG được seed sẵn: giá gói là quyết định kinh doanh, không phải quyết định kỹ thuật."*

**Việc cần làm — là quyết định của nhóm, không phải của một người:**

1. Chốt 4 loại vé và **giá thật** (số ở client hiện là số bịa để dựng giao diện — `monthlyPassApi.ts:94`).
2. Vàng Thị Dăm thêm `InsertData` vào migration hoặc một seed riêng.
3. Trần Trung Hiếu quyết: có `GET /pass-types` không, hay frontend đọc từ chỗ khác. Nếu có thì phải
   vào `api-contract.md` **trước** khi Hạnh gọi.
4. Sau đó Hạnh bỏ hằng số `PASS_TYPES` ở client và lấy giá từ server, để màn hình không hiển thị
   một con số mà hệ thống không biết.

---

### V4 — Hai component của story 16 là code chết, và không có hàm nào gọi `GET /monthly-passes/me` 🟠 TB

**Bằng chứng (đều bằng grep, chạy lại được):**

| Kiểm tra | Kết quả |
|---|---|
| File nào import `MonthlyPassExpiryReminder` | **không file nào** |
| File nào import `MonthlyPassStatusTag` | chỉ `MonthlyPassRegistrationPage.tsx:18` |
| `monthlyPassApi.ts` có hàm gọi `GET /monthly-passes/me` | **không** — interface `MonthlyPassApi` (`:83-87`) chỉ có `listRoutes` và `register` |
| File frontend nào nhắc `/monthly-passes/me` ngoài comment | **không file nào** |
| `src/pages/MyMonthlyPassPage.tsx` (màn quản lý vé tháng của tôi) | **không tồn tại** |

Tức là:

- **r46** *"Component hiển thị trạng thái vé tháng"* (Băng) — component có, nhưng chỉ xuất hiện trong
  thẻ kết quả của màn đăng ký.
- **r47** *"Nhắc nhở vé tháng sắp hết hạn trên giao diện"* (Băng) — component có, **không ai render**.
- **r45** *"Màn hình quản lý vé tháng của tôi + nút gia hạn"* (Thịnh) — **chưa làm**, và nó chính là
  màn được thiết kế để nhúng hai component trên.

Chính `MonthlyPassExpiryReminder.tsx:28-29` nói ra điều này:

> *"Màn hình 'quản lý vé tháng của tôi' (Thịnh) nhúng component này ở đầu trang; nút 'gia hạn' thuộc
> màn hình đó, không phải ở đây."*

Hệ quả: **r46 và r47 có thể bị đánh dấu "Done" mà không người dùng nào nhìn thấy chúng.** Và vì
không có hàm nào gọi `GET /monthly-passes/me` (endpoint **đang chạy tốt**, đo được 200 — mục 2.1),
một nửa backend của story 16 hiện **không có đường nào tới giao diện**.

Đây không phải lỗi của người làm component — họ làm đúng theo màn hình chưa tồn tại. Đây là **lỗ
hổng phối hợp**: ba task r45/r46/r47 phụ thuộc nhau theo một chiều, và chỉ task ở giữa (r46) là xong.

---

### V5 — Năm comment nói sai tình trạng backend 🟠 TB

Luồng vé tháng có hai endpoint chạy được từ trước báo cáo này (`GET /monthly-passes/me`,
`POST /monthly-passes/{id}/renew` — hợp đồng tại `docs/api-contract.md:1732-1733`), nhưng năm chỗ
trong frontend vẫn nói backend trống:

| # | Vị trí | Câu sai | Đúng |
|---|---|---|---|
| 1 | `monthlyPassApi.ts:7-8` | *"Backend CHƯA có endpoint vé tháng … là task của Vàng Thị Dăm + Trần Trung Hiếu (**Sprint 5**)"* | Đã có 2 endpoint; và story 16 nằm ở **Sprint 2**, không phải Sprint 5 |
| 2 | `monthlyPassApi.ts:114-115` | *"Đang để `true` vì endpoint vé tháng chưa có"* | Endpoint gia hạn + tra cứu đã có từ trước Sprint 2 |
| 3 | `MonthlyPassRegistrationPage.tsx:35-36` | *"Backend vé tháng chưa có nên đang chạy trên dữ liệu giả"* | Đúng một nửa: thiếu là `POST /monthly-passes`, không phải "vé tháng" nói chung |
| 4 | `MonthlyPassExpiryReminder.tsx:29` | *"(API gia hạn chưa có)"* | `POST /monthly-passes/{id}/renew` **đã có** — `api-contract.md:1763` |
| 5 | `App.tsx:276` | *"/monthly-passes là màn hình đăng ký vé tháng (**US 10**)"* | US 10 là *"Thông báo trạm"* (Định vị & Theo dõi thời gian thực). Vé tháng là **US 16** |

**Vì sao đây không phải chuyện vặt:** câu #2 chính là **lý do** cờ `USE_MOCK_DATA` còn bật, và cờ đó
là thứ đang che V1 và V3. Một comment sai ở đây không chỉ gây nhầm — nó **giữ nguyên** một công tắc
khiến hai lỗi khác không lộ ra. (Số 5 là lỗi truy vết: đối chiếu `Product_Backlog_Smart_Bus.xlsx`
sheet `Backlogs`, `A=10` và `A=16`.)

Người xử lý: #1–#3 Dương Thị Hạnh · #4 Nguyễn Đình Băng · #5 Nguyễn Đình Băng (`App.tsx` thuộc E1).

---

### V6 — Nhánh API thật chưa từng chạy lần nào 🟡 Thấp

`monthlyPassApi.ts:116` và `:247`:

```ts
const USE_MOCK_DATA = true;
const monthlyPassApi: MonthlyPassApi = USE_MOCK_DATA ? mock : api;
```

Cờ là **hằng số ở tầng module**, không đọc từ biến môi trường và không có test. Nên đối tượng `api`
(`:173-179`) — gồm cả `register` thật — **chưa từng thực thi một lần nào**, không ở máy ai, không ở
CI. Đây là lời giải thích trực tiếp cho việc V1 và V3 nằm im: hai lỗi đó chỉ xuất hiện trên nhánh
chưa ai chạy.

Kèm một chi tiết nhỏ nhưng gây nhầm khi demo: dữ liệu giả ở `monthlyPassApi.ts:183-212` là **tuyến
Hà Nội** (`route-01` Bến xe Mỹ Đình, `route-08` Cầu Giấy…), còn CSDL chung đã seed là **tuyến TP.HCM**
(`01` Bến Thành — Chợ Lớn, `02` Bến Thành — Suối Tiên — `25-huong-dan-csdl-chung.md:57`). Màn hình
đang mời chọn những tuyến **không tồn tại trong hệ thống**.

---

### V7 — `package-lock.json` lạc ở gốc repo 🟡 Thấp

File 98 byte, chưa ai theo dõi, nằm ở `C:\smart-bus-ticketing\package-lock.json` (gốc repo, **không**
phải trong `frontend/`). Ghi lại lần thứ tư: `L20` (báo cáo Sprint 1) → `P9` (báo cáo phân công) →
`N9` (báo cáo tra cứu) → **V7**. Ngoài luồng vé tháng. Hoặc xoá, hoặc cho vào `.gitignore`
(`.gitignore` thuộc Phùng Duy Hoàng — mục E1).

---

## 4. Đã kiểm và ĐẠT

### Endpoint vé tháng của backend — đúng hợp đồng

| Endpoint | Kiểm | Kết quả |
|---|---|---|
| `GET /api/monthly-passes/me` | Hành khách có 1 vé còn hạn | ✅ 200, trả **mảng trần** đúng `api-contract.md:1733`, phần tử có đủ `id/code/routeId/passTypeCode/price/validFrom/validTo/status/createdAt` |
| `GET /api/monthly-passes/me` | Không token | ✅ **401** — chặn đúng |
| `POST /api/monthly-passes/{id}/renew` | Hành khách tự gia hạn vé của mình | ✅ 200, `code` sinh đúng khuôn `MP-01-BYBQLM` (`MP-{mã tuyến}-{6 ký tự}`) |
| Cả hai | Vé của người khác | ✅ 404 *"Không tìm thấy vé tháng"* — **không** lộ là vé có tồn tại |

### Thiết kế trạng thái của frontend — **khớp** với backend, đây là chỗ làm đúng

`monthlyPassStatus.ts:50-68` suy trạng thái hiển thị từ **cả** `status` **và** `validTo`:

```ts
if (pass.status === 'Expired' || daysLeft < 0) status = 'Expired';
```

Comment ở `:42-44` giải thích đúng lý do — job quét của Kiên lật cột `Status` theo lịch nên có thể
chưa kịp chạy. Điều này **khớp chính xác** với cảnh báo 🔴 trong `Entities/MonthlyPassStatus.cs`
("hiệu lực thật phải suy từ ValidFrom/ValidTo, không phải từ cột này") và với hành vi thật của
`MonthlyPassRenewalService` (mà bộ test ở `feature/16-test-api-ve-thang` đã khoá bằng hai ca đối
chứng). Đây là ví dụ hiếm trong báo cáo này về **hai tầng hiểu đúng nhau**.

Ba điểm cộng khác ở tầng component:

- `getExpiringSoonPasses` (`monthlyPassReminder.ts:22-31`) và `MonthlyPassStatusTag` **dùng chung**
  `getMonthlyPassStatus`, nên tag và nhắc nhở không thể nói hai chuyện khác nhau; ngưỡng
  `EXPIRING_SOON_DAYS = 7` khai ở đúng một chỗ.
- `MonthlyPassStatusTag` nhận `Pick<MonthlyPass, 'status' | 'validTo'>` — đúng lượng dữ liệu cần,
  không kéo cả object.
- Menu `roles: []` (`App.tsx:72`) khiến mục "Vé tháng" hiện với **mọi vai trò** — đúng ý đồ màn hình
  hành khách. Và `/monthly-passes` **cố ý không bọc `RouteGuard`** (`App.tsx:279`), khác 9 route
  quản trị — đây là **đúng** ở đây (màn không giới hạn vai trò, còn hai API nó gọi là `[Authorize]`
  trần), **không phải** cùng lỗi với màn tra cứu.

### Một quan sát nhỏ, chưa đủ để thành lỗi

`monthlyPassStatus.ts:54-56` so ngày theo **ngày địa phương** (`dayjs(...).startOf('day')`), backend
so theo **UTC**. Với vé hết hạn lúc `ValidTo` (UTC) thì có một khoảng vài giờ frontend còn gọi là
"hết hạn hôm nay" trong khi backend đã coi là hết hạn. **Nhưng** `GET /monthly-passes/me` lọc
`ValidTo >= now`, nên vé đã quá hạn **không quay về** frontend nữa — khoảng lệch này hiện **không
quan sát được** qua API. Ghi lại để lần sau ai đổi bộ lọc đó thì biết chỗ này mà xem.

---

## 5. Việc chuyển cho ai

| Việc | Người | Vì sao là của họ |
|---|---|---|
| Quyết định nguồn danh sách tuyến cho màn hành khách; thêm endpoint tra cứu công khai | **Trần Trung Hiếu** | Chủ API + chủ `api-contract.md` (`25-huong-dan-csdl-chung.md:138` đã ghi việc này đang chờ) |
| Sửa `addMonths` (V2), bỏ `PASS_TYPES` cứng (V3), cờ `USE_MOCK_DATA` (V6), 3 comment (V5 #1–#3) | **Dương Thị Hạnh** | r44 — màn đăng ký vé tháng + `monthlyPassApi.ts` |
| Làm màn "Vé tháng của tôi" + nút gia hạn (r45) | **Hoàng Văn Thịnh** | r45; và là màn duy nhất gọi `GET /monthly-passes/me` |
| Nối `MonthlyPassExpiryReminder` + `MonthlyPassStatusTag` vào màn hình | **Nguyễn Đình Băng** | r46/r47; `App.tsx` + component thuộc E1 |
| Chốt loại vé + giá thật rồi seed `PassTypes` | **Vàng Thị Dăm** (kỹ thuật) + **nhóm** (con số) | `Migrations/*` và `Entities/` thuộc Dăm; giá là quyết định kinh doanh |
| `package-lock.json` (V7) | **Phùng Duy Hoàng** | `.gitignore` thuộc E1 |

---

## 6. Còn lại — chưa kiểm được

| Hạng mục | Vì sao chưa kiểm được |
|---|---|
| Bất kỳ request HTTP thật nào qua mạng | Không có môi trường online (mục 1) |
| API trỏ vào CSDL Supabase chung | Máy này không có `appsettings.Development.json`; chuỗi kết nối nằm ngoài repo |
| `POST /monthly-passes` (đăng ký) | Endpoint **không tồn tại** — đo được 404 (mục 2.1). Task r39 của Trần Trung Hiếu còn "Chưa làm" |
| Dữ liệu `PassTypes` thật trong CSDL chung | Bảng không được seed (V3); không kết nối được để xác nhận bảng đang rỗng |
| Luồng gia hạn từ giao diện | Frontend chưa có nút/hàm gọi `renew` nào (V4) |
| Màn "Vé tháng của tôi" | Chưa tồn tại (r45) |
| Đa trình duyệt / đa thiết bị | Không có môi trường để mở |
| Nhật ký kiểm toán cho `POST /monthly-passes/{id}/renew` | Đã có `AuditLogCoverageTests` phủ ở tầng test — ngoài phạm vi báo cáo này |

---

## 7. Phụ lục — lệnh chạy lại mọi bằng chứng trong báo cáo

```bash
# Mốc chuẩn
dotnet test backend/SmartBus.Tests/SmartBus.Tests.csproj          # 599/599
npm --prefix frontend run build                                    # exit 0
npm --prefix frontend run lint                                     # 1 cảnh báo cũ

# V1 — quyền của GET /routes (đo bằng TestAppFactory, hành khách => 403, Admin => 200)
grep -n "ManagerOrAbove" backend/SmartBus.Api/Controllers/RoutesController.cs
grep -n "fetchRoutes\|/routes" frontend/src/api/monthlyPassApi.ts frontend/src/api/routeApi.ts

# V2 — hai phép cộng tháng (lệnh thứ hai phải chạy trong frontend/ vì dayjs nằm ở đó)
node -e "const d=new Date(Date.UTC(2027,0,31));const r=new Date(d);r.setMonth(r.getMonth()+1);console.log(r.toISOString().slice(0,10))"
cd frontend && node -e "console.log(require('dayjs')('2027-01-31').add(1,'month').format('YYYY-MM-DD'))" && cd ..
# => 2027-03-03  và  2027-02-28

# V3 — PassTypes có được seed không (0 = không)
grep -c "InsertData" backend/SmartBus.Api/Migrations/20261001133209_Sprint2_MonthlyPasses_PassTypes.cs
grep -n "pass-types" docs/api-contract.md

# V4 — component chết + không hàm nào gọi GET /monthly-passes/me
grep -rn "MonthlyPassExpiryReminder\|MonthlyPassStatusTag" frontend/src --include="*.tsx" | grep import
grep -rn "monthly-passes/me" frontend/src
ls frontend/src/pages/ | grep -i monthly

# V6 — cờ dữ liệu giả
grep -n "USE_MOCK_DATA" frontend/src/api/monthlyPassApi.ts
```

---

*Báo cáo này **không** sửa một dòng code nghiệp vụ nào — đúng ranh giới "Người kiểm thử — Giàng A
Vàng" ở `docs/03-quy-uoc.md` mục 2.4. Mọi mục trong mục 3 kèm các bước tái hiện để người sở hữu file
tự sửa.*
