# Báo cáo kiểm thử chéo — luồng tra cứu tuyến & tìm chuyến (User Story 1)

| | |
|---|---|
| **Người thực hiện** | Giàng A Vàng (Kiểm thử) |
| **Task** | Sprint 2, story 1 — *Kiểm thử chéo luồng tra cứu trên môi trường online* |
| **Ngày** | 04/10/2026 |
| **Mốc so sánh** | `main` @ `3ecc099` |
| **Trạng thái** | ⚠️ Chỉ hoàn thành phần kiểm thử **không cần môi trường online** — xem mục 1 |

> Mã lỗi trong báo cáo này (**N1**…) là cục bộ cho báo cáo này, **khác** bộ `C1`–`C6` của
> `docs/bao-cao-kiem-thu-cheo-lich-trinh-sinh-chuyen.md` và bộ `P1`–`P9` của
> `docs/bao-cao-kiem-thu-cheo-phan-cong.md`. Ba bộ không liên tục và không thay thế nhau.
>
> **Không có lỗi nào ở đây là "phát hiện mới chưa ai biết".** Giới hạn quyền của màn "Tra cứu tuyến"
> đã được ghi rõ trong `docs/25-huong-dan-csdl-chung.md` (dòng 108 và 133). Việc của báo cáo này
> không phải là phát hiện nó, mà là **đo xem nó thực sự tốn gì cho story 1** — và ở đó có ba điều
> tài liệu chưa nói ra, gồm một chỗ UI tự mâu thuẫn với chính nó (N1).

---

## 1. Phạm vi — phần KHÔNG làm được và vì sao

Task yêu cầu kiểm thử **trên môi trường online**. So với hai báo cáo trước, khoảng trống **vẫn
không hẹp thêm một chút nào**.

| Kiểm tra | Story 13 | Story 14 | Nay |
|---|---|---|---|
| Dockerfile backend | ✅ có | ✅ **vẫn có** | ✅ **vẫn chỉ có `backend/Dockerfile`** |
| `render.yaml` / `fly.toml` / `Procfile` / `vercel.json` / `netlify.toml` / `docker-compose.yml` | ❌ không | ❌ **vẫn không** | ❌ **vẫn không một file nào** |
| Workflow deploy | ❌ chỉ `ci.yml` | ❌ **vẫn chỉ `ci.yml`** | ❌ **vẫn chỉ `ci.yml`** |
| URL online ở đâu đó trong repo | ❌ không | ❌ **không** | ❌ **không** |
| `frontend/.env.example` | `localhost:5080` | ❌ **vẫn vậy** | ❌ **vẫn vậy** — `frontend/src/api/axiosClient.ts:21` mặc định `http://localhost:5080/api` |
| `appsettings.Production.json` | ❌ không có | ❌ **vẫn không** | ❌ **vẫn không** |
| Task deploy trong backlog Sprint 2 | ❌ không có | ❌ **không có dòng nào** | ❌ **vẫn không có dòng nào** |

**Hệ quả:** không gửi được một request HTTP thật nào tới môi trường chung. Toàn bộ kết luận ở mục 3
là **đọc mã + đối chiếu hợp đồng + chạy test tích hợp InMemory trong bộ test**, không phải thao tác
tay trên trình duyệt.

**Phần đã làm thay thế:** kiểm thử chéo tĩnh có bằng chứng chạy lại được (mục 2), **cộng một phép đo
RBAC chạy thật** trên host test dựng từ chính `main` — dựng app thật, seed một tuyến thật, gọi bốn
endpoint của luồng bằng hai danh tính (không token / Passenger). Đây là loại bằng chứng mạnh nhất
kiếm được khi chưa có môi trường online, và nó **quan sát được đúng cái mà tài liệu chỉ mô tả bằng
lời** (mục 3, N1/N2). File probe là file tạm, đã xoá sau khi lấy kết quả — repo không thêm gì.

---

## 2. Mốc chuẩn trên `main` — đều ĐẠT

| Hạng mục | Lệnh | Kết quả |
|---|---|---|
| Test backend | `dotnet test backend/SmartBus.Tests/SmartBus.Tests.csproj` | ✅ **599/599 pass**, 0 fail, 0 skip, 1 m 46 s (story 14: 560; 599 = 560 + 39 test của story 14 đã merge qua PR #92) |
| Build frontend | `npm --prefix frontend run build` | ✅ exit 0 — chỉ còn cảnh báo chunk > 500 kB (1 642 kB / 514 kB gzip), đã có từ Sprint 1 |
| Lint frontend | `npm --prefix frontend run lint` | ⚠️ 1 cảnh báo — đúng cảnh báo cũ `StopManagePage.tsx:34`, **ngoài luồng tra cứu** |
| Rác debug (`console.log`, `TODO`, `FIXME`, `debugger`) | grep `frontend/src` + `backend/SmartBus.Api` | ⚠️ **1 dòng `TODO`** — `tripAssignmentApi.ts:90`, xem `P2` của báo cáo story 14. **Luồng tra cứu sạch: 0 dòng** |
| Bí mật bị commit | `git ls-files` lọc `.env`, `appsettings.Development`, `*.pfx`, `*.pem`, `id_rsa` | ✅ sạch |
| `package-lock.json` lạc ở gốc repo | `git status` | ⚠️ **vẫn untracked** — báo cáo thứ ba liên tiếp, xem N9 |

**Phép đo RBAC chạy thật** (probe tạm, đã xoá; cách dựng lại ở mục 6.7):

```
GET /api/routes                | khong-token=401 | passenger=403 | {"message":"Bạn không có quyền truy cập tính năng này."}
GET /api/routes/{id}/stops     | khong-token=401 | passenger=403 | {"message":"Bạn không có quyền truy cập tính năng này."}
GET /api/routes/{id}/fares     | khong-token=401 | passenger=403 | {"message":"Bạn không có quyền truy cập tính năng này."}
GET /api/trips/search          | khong-token=200 | passenger=200 | []
```

Kèm một dòng do chính middleware ghi ra lúc chạy:

```
warn: RbacAuthorizationResultHandler[0]
      Từ chối truy cập: người dùng <guid> (vai trò: Passenger) gọi GET /api/routes
      nhưng thiếu vai trò Admin, Manager.
```

---

## 3. Danh sách lỗi

Mức độ: **Cao** = sai chức năng hoặc gây hiểu nhầm nghiêm trọng · **TB** = ảnh hưởng trải nghiệm
hoặc rủi ro · **Thấp** = tài liệu, vệ sinh mã.

| # | Lỗi | Mức | Người xử lý |
|---|---|---|---|
| N1 | Menu hiện "Tra cứu tuyến" cho **mọi vai trò** nhưng màn đó **luôn 403** với Passenger — UI tự mâu thuẫn | 🔴 Cao | Hoàng Văn Thịnh |
| N2 | Cả app nằm sau cổng đăng nhập ⇒ phần **công khai** của `GET /trips/search` **không có đường vào từ giao diện** | 🔴 Cao | Hoàng Văn Thịnh + chốt lại hợp đồng |
| N3 | `/trip-results` có **hai mức quyền khác nhau** cho cùng một màn hình, tuỳ URL có `routeId` hay không | 🟠 TB | Hoàng Văn Thịnh |
| N4 | Thiếu `routeId` ⇒ kéo **toàn bộ** tuyến Active về rồi bắn **3 request/tuyến**, không trần | 🟠 TB | Dương Thị Hạnh · Hoàng Văn Thịnh |
| N5 | Comment nói backend *"chưa có bảng Trips"* — sai từ story 13, và tả sai luôn việc đã xong | 🟡 Thấp | Dương Thị Hạnh |
| N6 | Comment nói múi giờ là `+07:00` — backend trả `Z`; kết luận đúng nhưng **lý do sai** | 🟡 Thấp | Nguyễn Đình Băng |
| N7 | `capacity` khai báo *"để hiển thị còn X/Y ghế"* nhưng **không màn nào dùng** | 🟡 Thấp | Nguyễn Đình Băng |
| N8 | `GET /trips/search` công khai chỉ nhờ **THIẾU** `[Authorize]` — không `[AllowAnonymous]`, không fallback policy | 🟡 Thấp | Phùng Duy Hoàng |
| N9 | `package-lock.json` lạc ở gốc repo (rỗng, 98 byte) | 🟡 Thấp | Hoàng Văn Thịnh |

**Điểm khác biệt so với hai báo cáo trước:** ở story 13 và 14 tôi phải đi tìm lỗi. Ở đây **không có
lỗi logic nào trong luồng**. Backend của story 1 đúng, và đúng theo cách khó — xem mục 4. Toàn bộ
vấn đề nằm ở chỗ **ai được phép đi vào luồng đó**, tức là tầng giao diện và tài liệu.

---

### N1 — Menu hiện "Tra cứu tuyến" cho mọi vai trò, nhưng màn đó luôn 403 với Passenger 🔴 Cao

**Ở đâu:**
- `frontend/src/App.tsx:71` — mục menu: `{ to: '/route-lookup', label: 'Tra cứu tuyến', roles: [] as string[] }`
- `frontend/src/App.tsx:86` — bộ lọc menu: `item.roles.length === 0 || item.roles.includes(user?.role ?? '')`
- `frontend/src/App.tsx:186` — khai báo route: `<Route path="/route-lookup" element={<RouteLookupPage />} />`, **không có `RouteGuard`**
- `frontend/src/api/routeLookupApi.ts:69` — `const USE_MOCK_DATA = false;` (nhánh API thật)
- `frontend/src/api/routeLookupApi.ts:96`, `:130`, `:131` — ba endpoint mà nhánh thật gọi
- `backend/SmartBus.Api/Controllers/RoutesController.cs:18`, `RouteStopsController.cs:23`, `FaresController.cs:22` — cả ba đều `[Authorize(Policy = RbacPolicies.ManagerOrAbove)]`

**Chuyện gì:** `roles: []` nghĩa là **hiện với tất cả mọi người**, không lọc vai trò. Route cũng
không bọc `RouteGuard` — trong khi **9 màn quản trị ngay dưới nó** đều bọc
(`App.tsx:200`, `:210`, `:220`, `:230`, `:240`, …).

Nghĩa là một tài khoản Passenger (đăng ký mới luôn là Passenger — `AuthService.cs:61-62`,
`RoleIds.Passenger`) **thấy mục "Tra cứu tuyến" trên thanh menu**, bấm vào được, gõ điểm đi/điểm đến,
bấm "Tìm tuyến", rồi nhận:

> **Bạn không có quyền truy cập tính năng này.**

Màn hình **tự quảng cáo một tính năng rồi tự từ chối phục vụ**. Không phải "tính năng chưa xong" —
là một mục menu chắc chắn thất bại với 2 trong 4 vai trò của hệ thống.

**Bằng chứng chạy lại được:**

```
$ grep -n "route-lookup" frontend/src/App.tsx
71:    { to: '/route-lookup', label: 'Tra cứu tuyến', roles: [] as string[] },
186:                <Route path="/route-lookup" element={<RouteLookupPage />} />

$ grep -n "Authorize" backend/SmartBus.Api/Controllers/RoutesController.cs \
      backend/SmartBus.Api/Controllers/RouteStopsController.cs \
      backend/SmartBus.Api/Controllers/FaresController.cs
backend/SmartBus.Api/Controllers/RoutesController.cs:18:[Authorize(Policy = RbacPolicies.ManagerOrAbove)]
backend/SmartBus.Api/Controllers/RouteStopsController.cs:23:[Authorize(Policy = RbacPolicies.ManagerOrAbove)]
backend/SmartBus.Api/Controllers/FaresController.cs:22:[Authorize(Policy = RbacPolicies.ManagerOrAbove)]
```

Và phép đo RBAC ở mục 2: `GET /api/routes` với token Passenger → **403**, kèm đúng câu
`Bạn không có quyền truy cập tính năng này.` mà `routeLookupApi` sẽ ném lên `message.error`
(`axiosClient.ts:119-120` lấy `message` của backend làm `customMessage`).

**Tài liệu đã biết — nhưng chưa nói hết:** `docs/25-huong-dan-csdl-chung.md:108` có hẳn một dòng
xử lý sự cố:

> | Màn "Tra cứu tuyến" báo không đủ quyền (403) | Màn này đọc `GET /routes` — chỉ Manager/Admin | Đăng nhập `0900000001`/`0900000002`; … |

Dòng đó **mô tả đúng cái triệu chứng**, nhưng nó nằm ở mục *"Xử lý sự cố"* — tức là được đóng khung
như một lỗi cấu hình của người dùng, không phải như một quyết định thiết kế. Hệ quả là **không ai
sửa mục menu**, vì mục menu trông vẫn "đúng": màn hình dành cho hành khách thì phải hiện cho hành
khách.

**Vì sao tôi xếp 🔴 dù tài liệu đã ghi:** vì đây là **chỗ duy nhất trong ba báo cáo mà giao diện tự
mâu thuẫn với chính nó**, và cách sửa rẻ nhất không nằm ở backend. Chờ API công khai của Hiếu
(task *"API tìm kiếm chuyến theo điểm đi, điểm đến, ngày giờ"*) là đúng về đích, nhưng trong lúc
chờ, `roles: []` vẫn đang nói dối hành khách mỗi ngày.

**Hướng sửa rẻ nhất (một dòng, của Thịnh):** đổi `App.tsx:71` thành
`roles: ['Admin', 'Manager']` và bọc `RouteGuard` cho `App.tsx:186` — đúng lối 9 màn quản trị ngay
dưới. Khi Hiếu xong API công khai thì trả lại `roles: []` và gỡ guard. Việc này **không đụng vào
`routeLookupApi.ts` của Hạnh**.

---

### N2 — Phần "công khai" của `GET /trips/search` không có đường vào từ giao diện 🔴 Cao

**Ở đâu:**
- `frontend/src/App.tsx:98` — `{!user ? (<AuthPage />) : (<Layout>…</Layout>)}`
- `docs/api-contract.md:754-756` — lời hứa ở phía ngược lại

**Chuyện gì:** toàn bộ `<Routes>` — mọi màn hình của app — nằm trong **nhánh đã đăng nhập**. Chưa
đăng nhập thì `App` trả về `AuthPage`, và `AuthPage` chỉ có hai tab: đăng nhập và đăng ký (không có
lối vào kiểu khách vãng lai). Gõ thẳng `/route-lookup` trên thanh địa chỉ cũng không giúp gì: router
chưa được dựng.

Trong khi đó hợp đồng viết:

> **Đây là endpoint CÔNG KHAI duy nhất của mục `/trips`** — gọi không cần đăng nhập. Story 1 mở đầu
> bằng *"Là hành khách, tôi muốn tìm kiếm tuyến xe…"*: tra cứu chuyến là việc trước khi đăng nhập,
> đăng nhập là bước của màn hình đặt vé.

Nghĩa là: Phùng Duy Hoàng đã làm `GET /trips/search` công khai **có chủ đích**, với lý do được viết
thành văn — và **lý do đó hiện không thể phát huy tác dụng qua giao diện**. Phép đo ở mục 2 cho thấy
endpoint trả **200 khi không có token**; nhưng không có hành khách nào chạm được tới nó trước khi
đăng nhập, vì app không cho họ vào.

Đây là **nửa còn lại của cùng một vấn đề với N1**, và nó nghiêm trọng hơn: N1 làm hỏng một màn hình,
N2 làm hỏng **lý do tồn tại của một quyết định thiết kế backend**. Ai đọc `api-contract.md:754` rồi
tin rằng hành khách tra cứu được trước khi đăng nhập sẽ mô tả sai hệ thống cho người ngoài.

**Bằng chứng chạy lại được:**

```
$ grep -n "!user ?" frontend/src/App.tsx
98:      {!user ? (

$ sed -n '98,101p' frontend/src/App.tsx
      {!user ? (
        // NẾU CHƯA ĐĂNG NHẬP: Hiển thị Màn hình Auth (Login / Register)
        <AuthPage />
      ) : (
```

Cổng này có từ `d1477d7` — *"feat(auth): màn hình đăng nhập / đăng ký"* (PR #4, 23/09/2026), tức
**trước** khi story 1 lên sóng. Nó không phải lỗi của story 1; nó là **một quyết định cũ chưa được
xem lại khi story 1 xuất hiện**.

**Đây là ngã ba cần người chốt, không phải lỗi để một người sửa:** hoặc (a) mở một nhánh công khai
cho `/route-lookup` + `/trip-results` — hành khách tra cứu trước, đăng nhập sau, đúng như hợp đồng
viết; hoặc (b) giữ nguyên cổng và **sửa lại `api-contract.md:754-756`** cho khớp sự thật, bỏ câu
"tra cứu chuyến là việc trước khi đăng nhập". Cả hai đều hợp lệ. Điều **không** hợp lệ là để nguyên
hiện trạng: hợp đồng hứa một đằng, app làm một nẻo, không ai ghi lại.

---

### N3 — `/trip-results` có hai mức quyền cho cùng một màn hình 🟠 TB

**Ở đâu:**
- `frontend/src/api/tripSearchApi.ts:90-92` — nhánh quyết định quyền
- `frontend/src/pages/TripSearchResultPage.tsx:74` — `routeId` là tham số **tuỳ chọn**
- `frontend/src/pages/TripSearchResultPage.tsx:80` — `hasCriteria` **chỉ đòi** `origin` + `destination`

**Chuyện gì:**

```ts
// tripSearchApi.ts:90-92
const routeIds = routeId
  ? [routeId]
  : (await routeLookupApi.search({ origin, destination })).map((route) => route.routeId);
```

Cùng một màn hình, cùng một URL gốc, hai số phận:

| URL | Đường đi | Passenger |
|---|---|---|
| `/trip-results?origin=…&destination=…&routeId=…` | gọi thẳng `GET /trips/search` | ✅ **200** |
| `/trip-results?origin=…&destination=…` | rơi vào `routeLookupApi` → `GET /routes` | ❌ **403** |

Màn hình **tự nhận mình chạy được khi chỉ có `origin` + `destination`** (`hasCriteria` chỉ kiểm hai
trường đó, `routeId` để tuỳ chọn) — và khi đúng như vậy thì nó hỏng với hành khách. Ai đó F5 trên
màn kết quả, hoặc gửi link cho đồng nghiệp, sẽ mất `routeId` và nhận 403 thay vì danh sách chuyến.

**Vì sao đáng ghi dù cùng gốc với N1:** vì đây là **một màn hình khác** (của Băng, không phải của
Hạnh) và `docs/25-huong-dan-csdl-chung.md:138` khẳng định với người đọc:

> Bước này gọi `GET /trips/search` công khai — **hành khách cũng xem được**.

Câu đó **đúng khi và chỉ khi** người dùng đi tới đây bằng nút "Xem chuyến". Nó không đúng với URL
trần. Tài liệu đang hứa rộng hơn thực tế.

---

### N4 — Nhánh thiếu `routeId` bắn 3 request mỗi tuyến, không có trần 🟠 TB

**Ở đâu:**
- `frontend/src/api/routeLookupApi.ts:92-109` — `fetchActiveRoutes` lặp trang tới hết
- `frontend/src/api/routeLookupApi.ts:127-145` — mỗi tuyến khớp: 2 request song song
- `frontend/src/api/fareApi.ts:154` — `GET /routes/{routeId}/fares`
- `frontend/src/api/routeStopApi.ts:90` — `GET /routes/{routeId}/stops`
- `frontend/src/api/tripSearchApi.ts:96-102` — thêm 1 request `GET /trips/search` mỗi tuyến

**Chuyện gì:** một lượt tra cứu ở nhánh thiếu `routeId` tốn **1 + 3M request**, với M là số tuyến
khớp — và M **không có trần**, vì `fetchActiveRoutes` kéo về **mọi tuyến đang khai thác của toàn hệ
thống** (lặp trang, `pageSize` 100, dừng khi `routes.length >= total`) rồi mới lọc ở client
(`:123-125`).

Comment ở `:89-90` giải thích vì sao lọc ở client:

> Không thể lọc origin/destination ngay tại `GET /routes` vì endpoint chỉ nhận MỘT tham số `search`
> chung, không tách riêng hai chiều — phải lấy về rồi lọc phía client.

Đúng — nhưng **bỏ sót nửa lợi ích còn lại**: `GET /routes` **có** nhận `search` và nó khớp *"mã,
tên, điểm đầu hoặc điểm cuối"* (`frontend/src/api/routeApi.ts:47-48`). Gửi `search=<điểm đi>` sẽ cắt
thô ở server rồi mới tinh chỉnh hai chiều ở client. Hiện tại mỗi lượt tìm tuyến kéo về **toàn bộ
danh mục tuyến**, kèm 2 request con cho **từng** tuyến khớp.

Hôm nay dữ liệu seed chỉ vài tuyến nên không ai thấy. Với vài trăm tuyến thì đây là request nặng
nhất trong toàn app, và nó chạy trên **màn hình đầu tiên của story 1**.

---

### N5 — Comment nói backend *"chưa có bảng Trips"* 🟡 Thấp

**Ở đâu:** `frontend/src/api/routeLookupApi.ts:32-34`

```ts
  /**
   * Ngày đi (yyyy-MM-dd). Backend chưa có bảng Trips nên tạm thời chưa dùng để lọc —
   * giữ trường này sẵn để khi API tìm chuyến xong chỉ cần truyền thẳng vào.
   */
```

**Chuyện gì:** cả hai vế đều đã sai:

1. *"Backend chưa có bảng Trips"* — bảng `Trips` có từ story 13, và `GET /trips/search` đã tồn tại,
   công khai, đã được story 1 dùng.
2. *"giữ trường này sẵn để khi API tìm chuyến xong chỉ cần truyền thẳng vào"* — việc "truyền thẳng
   vào" **đã xong rồi**, ở file khác: `tripSearchApi.ts:81` ghép `from`/`to` từ đúng trường `date`
   này.

Người đọc `routeLookupApi.ts` hôm nay sẽ tưởng `date` là trường chết đang chờ backend. Thực tế nó
đang chạy thật, chỉ là chạy ở file bên cạnh. Cùng loại với `P3`/`P7` của báo cáo story 14.

---

### N6 — Comment nói múi giờ `+07:00`, backend trả `Z` 🟡 Thấp

**Ở đâu:** `frontend/src/pages/TripSearchResultPage.tsx:53`

```ts
    // Cùng ngày, cùng múi giờ (+07:00) nên so chuỗi ISO là so đúng thứ tự giờ.
    copy.sort((a, b) => a.departureTime.localeCompare(b.departureTime));
```

**Chuyện gì:** backend serialize `DateTime` kiểu `Utc` nên chuỗi trả về kết thúc bằng **`Z`**, không
phải `+07:00`. Ví dụ trong chính hợp đồng (`docs/api-contract.md`, mục `GET /trips/search`):

```json
"departureTime": "2026-10-01T01:00:00Z",
```

**Kết luận của comment vẫn đúng** — so chuỗi ISO cho đúng thứ tự giờ — nhưng **lý do nêu ra thì
sai**: các chuỗi giống nhau ở chỗ *cùng một khuôn dạng*, chứ không phải cùng múi giờ `+07:00`.

Đây đúng là dạng `P8` của báo cáo story 14: *"comment nêu lý do sai cho một kết luận đúng"*. Đáng
sửa vì nó nguy hiểm về sau — người tin comment này sẽ tưởng việc trộn một chuỗi `+07:00` vào danh
sách là vô hại, trong khi `"2026-10-01T08:00:00+07:00"` xếp **sau** `"2026-10-01T02:00:00Z"` dù là
cùng một thời điểm.

---

### N7 — `capacity` khai báo để hiển thị "còn X/Y ghế" nhưng không được dùng 🟡 Thấp

**Ở đâu:**
- `frontend/src/api/tripSearchApi.ts:54` — `/** Sức chứa theo số ghế — để hiển thị "còn X/Y ghế". */`
- `frontend/src/pages/TripSearchResultPage.tsx:27-31` — `seatsMeta` chỉ nhận `seatsRemaining`

**Chuyện gì:**

```
$ grep -n "capacity" frontend/src/pages/TripSearchResultPage.tsx
KHÔNG dùng capacity trong trang
```

Comment nói trường này có mặt *để* hiển thị `X/Y`, nhưng nhãn thực tế chỉ là `Còn 5 ghế` / `Hết chỗ`
— không có mẫu số. `capacity` đi từ backend → type → state → `sortResults` rồi **dừng ở đó**.

Không phải lỗi chức năng (màn hình vẫn đọc được), nhưng là **dấu vết của một yêu cầu hiển thị chưa
được làm**, và hợp đồng có trả trường đó nên không ai phát hiện thiếu. Cùng nhóm với các trường
"khai báo rồi bỏ không" mà báo cáo story 14 đã gặp ở phía phân công (`P3`).

---

### N8 — `GET /trips/search` công khai chỉ nhờ THIẾU `[Authorize]` 🟡 Thấp

**Ở đâu:**
- `backend/SmartBus.Api/Controllers/TripSearchController.cs:21-23` — có `[ApiController]` + `[Route]`, **không** `[Authorize]`, **không** `[AllowAnonymous]`
- `backend/SmartBus.Api/Program.cs:215` — `AddRbacAuthorization()` chỉ `AddPolicy`, **không đặt `FallbackPolicy`**

**Chuyện gì:** trong toàn bộ backend:

```
$ grep -rn "AllowAnonymous" backend/SmartBus.Api/ --include=*.cs
(không có kết quả nào)

$ grep -rln "^\[Authorize" backend/SmartBus.Api/Controllers/*.cs | wc -l   →  19
$ ls backend/SmartBus.Api/Controllers/*.cs | wc -l                          →  21
```

21 controller, 19 có `[Authorize]` cấp class. Hai cái còn lại là `AuthController` (đúng — phải công
khai) và `TripSearchController` (cố ý công khai). Vì **không có `FallbackPolicy`**, quyền truy cập
mặc định là **công khai**, và bảo vệ là việc **tự nguyện gõ thêm attribute** — ngược với thế đứng
thường thấy, và ngược với điều một người đọc `api-contract.md` sẽ mặc định.

Hệ quả: không ai nhìn vào `TripSearchController` mà biết ngay "đây là endpoint cố ý công khai".
Người viết kế tiếp một controller mới mà quên `[Authorize]` sẽ **âm thầm mở một endpoint ra công
cộng**, không có gì chặn.

**Tôi hạ mức xuống 🟡 vì rủi ro này đã được test che:** `TripSearchCacheApiTests` gọi endpoint
**không kèm token** và khẳng định 200 (chính vì thế mà phép đo ở mục 2 mới chạy được). Nếu ai thêm
`FallbackPolicy` toàn cục, bộ test đó đỏ ngay. Đây là ghi chú về **thế đứng bảo mật**, không phải lỗ
đang hở — và nó thuộc về người sở hữu tầng xác thực, không phải người viết endpoint.

---

### N9 — `package-lock.json` lạc ở gốc repo 🟡 Thấp

**Ở đâu:** `C:\smart-bus-ticketing\package-lock.json` — untracked, 98 byte:

```json
{
  "name": "smart-bus-ticketing",
  "lockfileVersion": 3,
  "requires": true,
  "packages": {}
}
```

Không phải lockfile thật (không có package nào), nhưng `npm install` ở gốc repo sẽ **ghi đè** nó
bằng một cây phụ thuộc đầy đủ — lúc đó nó thành file rác hàng nghìn dòng, dễ bị `git add .` cuốn vào.

Đã treo từ `L20` (báo cáo Sprint 1) → `P9` (story 14) → **N9** (đây). Ba báo cáo, vẫn nguyên. Đề
xuất: xoá, hoặc thêm `package-lock.json` vào `.gitignore` gốc nếu không ai định dùng npm ở gốc repo.

---

## 4. Đã kiểm và ĐẠT

Phần này dài hơn hai báo cáo trước vì **backend của story 1 không có lỗi nào** — điều đáng nói, vì
đây là luồng có nhiều cạm bẫy thời gian nhất trong hệ thống.

### Hợp đồng `GET /api/trips/search`

Bộ `backend/SmartBus.Tests/TripSearchInputDataApiTests.cs` (67 ca, nhánh
`feature/1-test-api-tim-kiem-tuyen-nhieu-bo-du-lieu`) đã phủ toàn bộ không gian đầu vào. Những điểm
liên quan trực tiếp tới luồng tra cứu:

| # | Hợp đồng hứa | Kết quả |
|---|---|---|
| 1 | `routeId` thiếu / sai định dạng GUID → **400** kèm `errors.routeId` | ✅ 8 giá trị `routeId` hỏng, thiếu tham số, tham số rỗng — đều 400, **không** phải 404 |
| 2 | `routeId` hợp lệ nhưng không trỏ tới tuyến nào → **404** `Không tìm thấy tuyến đường` | ✅ 4 GUID hợp lệ, gồm `Guid.Empty` và dạng N 32 ký tự |
| 3 | `to` sớm hơn `from` → **400** kèm `errors.to` | ✅ 4 ca; `from == to` là hợp lệ |
| 4 | Không có chuyến → **`[]`**, không phải 404 | ✅ |
| 5 | Chỉ trả chuyến `Scheduled` | ✅ bảng 4 trạng thái; tham số `status` lạ bị bỏ qua |
| 6 | `price` = giá vé **phổ thông** (`PassengerType.Standard`) | ✅ 6 cấu hình giá, gồm tuyến chỉ có giá ưu đãi → `null` |
| 7 | `capacity` / `busType` / `seatsRemaining` lấy từ xe | ✅ 45 / 29 / 16 |
| 8 | Chuyến mồ côi (thiếu xe) bị loại | ✅ |
| 9 | `arrivalTime` `null` khi chưa chốt giờ đến | ✅ cả hai chiều |
| 10 | Cùng giờ khởi hành → sắp theo `Id` | ✅ |
| 11 | Không rò chuyến sang tuyến khác | ✅ |
| 12 | `from`/`to` **tính luôn mốc**, kể cả hai đầu | ✅ 9 tổ hợp điểm đi/điểm đến; khoảng vắt qua nửa đêm |
| 13 | Kết quả **không phân trang** | ✅ 25 chuyến trả đủ |
| 14 | Token không đổi kết quả của endpoint công khai | ✅ |

**Không tìm thấy lỗi backend nào.** 67/67 xanh, và bộ test xanh **ngay sau khi tôi sửa hai lỗi của
chính tôi** (chi tiết ở mục 6.5) — không có ca nào phải nới lỏng kỳ vọng để test xanh.

### Các điểm khác

| Hạng mục | Kết quả |
|---|---|
| `from`/`to` gửi kèm múi giờ **tường minh** `+07:00` | ✅ `tripSearchApi.ts:81` hard-code `+07:00` — gửi **mốc tuyệt đối**, không phụ thuộc máy chủ ở đâu. **`C5` của báo cáo story 13 KHÔNG tái diễn ở đây** |
| Sắp xếp theo giờ ở FE | ✅ đúng thứ tự (xem N6 về *lý do* được viết sai, kết luận vẫn đúng) |
| Sắp xếp theo giá ở FE | ✅ `comparePrice` (`:37-42`) đẩy chuyến `price === null` xuống cuối **ở cả hai chiều**, không coi là 0 đồng — xử lý đúng chỗ dễ sai |
| `price === null` trên UI | ✅ hiện "Chưa có giá" thay vì "0 đ" (`TripSearchResultPage.tsx:212-215`) |
| `arrivalTime === null` trên UI | ✅ hiện "—" (`:193`) |
| Nhãn số ghế | ✅ 0 → "Hết chỗ" (đỏ), ≤ 5 → cam, còn lại xanh |
| Chặn điểm đi trùng điểm đến ở client | ✅ `RouteLookupPage.tsx:57-60` — chặn trước khi gọi API |
| Ba trạng thái của màn tra cứu | ✅ chưa tìm / đã tìm có kết quả / đã tìm rỗng — ba nhánh riêng, không nhầm |
| `/trip-results` thiếu tiêu chí | ✅ mời quay lại màn tra cứu thay vì dựng form thứ hai |
| Phân quyền 2 màn mới của US 24/US 10 cùng PR | ✅ `roles: []` là **đúng** cho chúng — chúng thật sự dành cho mọi vai trò (N1 là ngoại lệ, không phải lỗi hệ thống của `roles: []`) |
| Rác debug trong luồng tra cứu | ✅ **0 dòng** `console.log` / `TODO` / `FIXME` / `debugger` |
| `capacity` đi từ backend tới type FE | ✅ backend trả đúng — vấn đề chỉ là FE không hiển thị (N7) |

---

## 5. Việc chuyển cho ai

| Người | Việc | Mã |
|---|---|---|
| **Hoàng Văn Thịnh** | `App.tsx:71` → `roles: ['Admin','Manager']` + bọc `RouteGuard` cho `App.tsx:186` (một dòng, gỡ lại được khi Hiếu xong API công khai) | N1 |
| **Hoàng Văn Thịnh** | Chốt ngã ba N2: mở nhánh công khai cho `/route-lookup` + `/trip-results`, **hoặc** sửa `api-contract.md:754-756`. Đây là quyết định thiết kế, cần người sở hữu `App.tsx` + người viết hợp đồng ngồi cùng | N2 |
| **Hoàng Văn Thịnh** | Bỏ nhánh `routeLookupApi` khi thiếu `routeId` ở `tripSearchApi.ts:90-92` — hoặc bắt buộc `routeId`, hoặc chấp nhận 403 và ghi vào tài liệu; sửa `25-huong-dan-csdl-chung.md:138` nếu giữ | N3 |
| **Dương Thị Hạnh** | Sửa comment `routeLookupApi.ts:32-34` (bảng `Trips` đã có từ story 13) | N5 |
| **Dương Thị Hạnh · Hoàng Văn Thịnh** | `routeLookupApi.ts:92-109`: gửi `search=<điểm đi>` để lọc thô ở server trước khi lọc hai chiều ở client | N4 |
| **Nguyễn Đình Băng** | Sửa comment `TripSearchResultPage.tsx:53` (backend trả `Z`, không phải `+07:00`) | N6 |
| **Nguyễn Đình Băng** | Hoặc hiển thị `còn X/Y ghế` bằng `capacity`, hoặc bỏ comment hứa điều đó ở `tripSearchApi.ts:54` | N7 |
| **Phùng Duy Hoàng** | Cân nhắc đặt `FallbackPolicy` toàn cục + `[AllowAnonymous]` tường minh cho `/trips/search` — **không gấp**, đã có test che | N8 |
| **Hoàng Văn Thịnh** | Xử lý `package-lock.json` gốc repo (ba báo cáo rồi) | N9 |
| **Scrum Master** | Chốt việc tạo môi trường online — **chưa có ai nhận** sau ba báo cáo, xem mục 1 | — |

**Ranh giới của tôi trong việc này:** theo mục *Kiểm thử* của bộ quy ước, tôi báo lỗi kèm bước tái
hiện và **không tự sửa mã nghiệp vụ cho test xanh**. `N1`–`N9` **đều** là mã nghiệp vụ, comment
hoặc tài liệu trong file của người khác — **tôi không sửa dòng nào**. File probe RBAC là file tạm
đã xoá, không nằm trong repo.

---

## 6. Còn lại — chưa kiểm được

Ghi rõ để không ai đọc báo cáo này rồi tưởng luồng tra cứu đã được kiểm đầu-cuối:

1. **Không có request HTTP thật nào được gửi tới môi trường chung.** Không có môi trường online,
   không chạy nổi API ở máy (thiếu PostgreSQL + `appsettings.Development.json`). Mọi kết luận ở
   mục 3 là đọc mã + đối chiếu hợp đồng + **phép đo RBAC trên host test InMemory**, không phải thao
   tác tay trên trình duyệt.
2. **Chưa mở được trình duyệt để xác nhận N1 bằng mắt** (menu hiện "Tra cứu tuyến" cho tài khoản
   `0900000004` — Passenger). Suy ra từ `App.tsx:71`, `:86`, `:186` và `RouteGuard` của 9 màn dưới.
   Nếu ai có CSDL chung đang chạy, **đây là bước xác nhận rẻ nhất**: đăng nhập `0900000004`, xem
   thanh menu.
3. **Chưa kiểm luồng đầu-cuối "hành khách tra cứu trước khi đăng nhập"** — vì theo N2 luồng đó
   **không tồn tại** ở tầng giao diện. Đây là hệ quả trực tiếp của N2, không phải thiếu sót của bộ
   test.
4. **Chưa đo N4 bằng số thật** (M tuyến khớp × 3 request). Cần CSDL chung có nhiều tuyến; hôm nay
   dữ liệu seed chỉ vài tuyến nên con số sẽ vô nghĩa.
5. **Hai lỗi trong bộ test của tôi ở lần chạy đầu** — ghi lại để người sau không mất thời gian:
   `RouteId_hop_le_nhung_khong_ton_tai_tra_404` nhận 400 vì chuỗi GUID N-format tôi viết **36 ký tự**
   (đúng phải 32); và một ca `ArrivalTime null` không diễn đạt được ý "cố tình null" bằng tham số mặc
   định `null`. **Cả hai là lỗi của tôi, không phải bằng chứng backend sai** — sửa xong thì 67/67
   xanh ngay lần thứ ba.
6. **Bộ đếm test:** 599 trên `main` @ `3ecc099`. Nhánh
   `feature/1-test-api-tim-kiem-tuyen-nhieu-bo-du-lieu` (đã đẩy, `331f24a`) có thêm 67 ca → **666**,
   nhưng **chưa mở PR** vì máy này không có `gh` CLI.
7. **Cách dựng lại phép đo RBAC** (probe đã xoá): tạo một file test trong `backend/SmartBus.Tests/`
   dùng `TestAppFactory`, seed một `Route` + một `User` mang vai trò `Passenger`, rồi gọi bốn URL ở
   mục 2 bằng hai `HttpClient` (một không token, một có `factory.CreateTokenFor(user)`), in ra cặp
   mã trạng thái. Chạy `dotnet test --filter`. Kết quả kỳ vọng đúng như bảng ở mục 2.
