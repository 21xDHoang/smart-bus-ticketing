# Hợp đồng API — Smart Bus Ticketing

> Đây là NGUỒN CHÍNH THỨC cho mọi endpoint. Cả frontend và backend đều đọc file này
> trước khi code — không ai tự bịa tên API (xem Quy tắc vàng trong Hướng dẫn thành viên).

## Quy ước chung

- Base URL (dev): `http://localhost:5080/api` — frontend đọc qua `VITE_API_URL`.
- Định dạng: JSON, mọi tên thuộc tính dùng **camelCase** (do .NET `System.Text.Json`
  với `PropertyNamingPolicy = CamelCase`). **Một ngoại lệ duy nhất:** `GET /audit-logs/export`
  trả file `.xlsx` nhị phân — xem mục "Nhật ký kiểm toán". Riêng đường **lỗi** của endpoint
  đó vẫn theo đúng định dạng JSON bên dưới.
- Xác thực: gửi header `Authorization: Bearer <accessToken>` cho mọi endpoint, trừ
  `/auth/login`, `/auth/register` và ba endpoint tra cứu công khai của story 1:
  `GET /routes/search` (tìm tuyến), `GET /stops/search` (gợi ý trạm) và `GET /trips/search`
  (tìm chuyến).
- Lỗi trả về thống nhất:

  ```json
  { "message": "Số điện thoại đã được đăng ký", "errors": { "phoneNumber": ["…"] } }
  ```

  | Mã | Ý nghĩa |
  |---|---|
  | 400 | Dữ liệu đầu vào sai |
  | 401 | Chưa đăng nhập / token hết hạn |
  | 403 | Đã đăng nhập nhưng không đủ quyền |
  | 404 | Không tìm thấy |
  | 409 | Xung đột dữ liệu |

## Auth — đã làm (Sprint 1)

| Method | Endpoint | Mô tả | Body | Trả về |
|---|---|---|---|---|
| POST | `/auth/register` | Đăng ký tài khoản | `{ fullName, email, phoneNumber, password }` | `AuthResponse` |
| POST | `/auth/login` | Đăng nhập | `{ phoneNumber, password }` | `AuthResponse` |
| POST | `/auth/refresh-token` | Làm mới cặp token | `{ refreshToken }` | `AuthResponse` |
| POST | `/auth/logout` | Thu hồi refresh token | `{ refreshToken }` | 204 No Content |

```json
// AuthResponse
{ "accessToken": "…", "refreshToken": "…", "tokenType": "Bearer", "expiresIn": 3600 }
```

## Trạm dừng — `/stops`

> ✅ Backend đã có (`StopsController` — Trần Trung Hiếu, story 12). Frontend đổi
> `USE_MOCK_DATA = false` trong `frontend/src/api/stopApi.ts` là chạy được thật.
>
> **Toàn bộ endpoint dưới đây yêu cầu vai trò `Admin` hoặc `Manager`.** Người đã đăng nhập
> nhưng không đủ quyền nhận **403** kèm body `{ "message": "Bạn không có quyền truy cập tính năng này." }`.
>
> Ô gợi ý trạm cho hành khách là endpoint **công khai**, đứng ở mục riêng
> "Tra cứu trạm dừng — `/stops/search`" bên dưới.

### Entity `Stop`

| Trường | Kiểu | Mô tả |
|---|---|---|
| `id` | `string` (GUID) | Khoá chính |
| `name` | `string` | Tên trạm dừng |
| `address` | `string` | Địa chỉ |
| `latitude` | `number` | Vĩ độ (lat) |
| `longitude` | `number` | Kinh độ (lng) |

```json
// Ví dụ Stop
{
  "id": "3f2a1b0c-0000-0000-0000-000000000000",
  "name": "Trạm Cầu Giấy",
  "address": "Số 1 Cầu Giấy, Hà Nội",
  "latitude": 21.0307,
  "longitude": 105.8034
}
```

### Endpoints

| Method | Endpoint | Mô tả | Body | Trả về |
|---|---|---|---|---|
| GET | `/stops` | Danh sách trạm dừng | — | `Stop[]` |
| GET | `/stops/{id}` | Chi tiết một trạm | — | `Stop` |
| POST | `/stops` | Thêm trạm | `{ name, address, latitude, longitude }` | `Stop` (201) |
| PUT | `/stops/{id}` | Sửa trạm | `{ name, address, latitude, longitude }` | `Stop` |
| DELETE | `/stops/{id}` | Xoá trạm | — | 204 No Content |

#### Ràng buộc dữ liệu đầu vào — POST và PUT dùng cùng bộ trường

| Trường | Bắt buộc | Ràng buộc |
|---|---|---|
| `name` | ✅ | 2–200 ký tự |
| `address` | ✅ | Tối đa 300 ký tự |
| `latitude` | ✅ | Từ **-90** đến **90** |
| `longitude` | ✅ | Từ **-180** đến **180** |

Vi phạm ràng buộc trên → **400** với `errors.<tên trường>`.

#### `DELETE /stops/{id}` — khi nào bị chặn

Trạm đang nằm trên ít nhất một tuyến (`RouteStops` tham chiếu tới) → **409**:

```json
{ "message": "Trạm đang nằm trên tuyến đường nên không thể xóa. Gỡ trạm khỏi tuyến trước." }
```

Trạm không nằm trên tuyến nào → xoá hẳn khỏi CSDL, trả 204. `Stop` không có cột trạng thái
nên không thể xoá mềm như `Routes` — quy ước A4 chỉ cho dùng cột trạng thái sẵn có.

## Tra cứu trạm dừng — `/stops/search`

> 📌 **Hợp đồng chốt ở đây.** Backend: `StopsSearchController` + `StopSearchService` trong
> `backend/SmartBus.Api/`. Mục này do Hoàng Văn Thịnh bổ sung cho task *"Component autocomplete
> chọn trạm dừng"* (Sprint 2 dòng 33, US 1.0).
>
> Lịch sử chỗ này: task *"API gợi ý trạm dừng theo từ khoá (autocomplete)"* (Sprint 2 dòng 29)
> trong backlog ghi **Nguyễn Duy Kiên** và đánh dấu **"Đã xong"**, nhưng trong `main` không có
> endpoint nào như vậy — `StopsController` chỉ có 5 route CRUD ở mục trên, `RouteSearchService` chỉ
> giải từ khoá ra trạm ở nội bộ. Backend được Hoàng Văn Thịnh viết bù ngày 2026-10-04;
> **backlog cần sửa lại người thực hiện ở dòng 29.**
>
> **Endpoint CÔNG KHAI** — hành khách gõ từ khoá **trước khi đăng nhập**, cùng nhóm với
> `GET /routes/search` và `GET /trips/search`. Đây là mảnh còn thiếu của luồng tra cứu: hành khách
> cần được **gợi ý trạm** để chọn, rồi mới gọi `GET /routes/search` bằng tên trạm vừa chọn. Thiếu
> nó thì màn "Tra cứu tuyến" buộc phải dùng `GET /stops` của quản lý (Admin/Manager), và hành
> khách chưa đăng nhập không có gợi ý nào.
>
> **Phía frontend:** ô gợi ý đã viết xong ở `frontend/src/components/StopAutocomplete.tsx`
> (đã vào `main` qua PR #101). Bản hiện tại lấy **trọn**
> `GET /stops` một lượt rồi tự lọc trong bộ nhớ. Khi chuyển sang endpoint này, component phải đổi
> **cách lấy dữ liệu** chứ không phải chỉ đổi địa chỉ gọi: gọi theo từng từ khoá (có chờ gõ xong —
> debounce) và bỏ lớp lọc phía client. **Chưa làm ở đây**: ô nhập nằm trong
> `frontend/src/pages/RouteLookupPage.tsx`, không phải file của người viết mục này, nên phải do chủ
> file đổi.

### Endpoint

| Method | Endpoint | Mô tả | Body | Trả về |
|---|---|---|---|---|
| GET | `/stops/search` | Gợi ý trạm theo từ khoá cho hành khách (**công khai**) | — | `Stop[]` |

Tham số query:

| Tham số | Kiểu | Mặc định | Mô tả |
|---|---|---|---|
| `keyword` | `string` | — | Từ khoá — khớp **tên trạm hoặc địa chỉ**, **không phân biệt hoa thường và không phân biệt dấu**. Bỏ trống hoặc toàn khoảng trắng = lấy đầu danh sách |
| `limit` | `number` | 10 | Số gợi ý tối đa, **1..50** |

```json
// Stop[] — ví dụ GET /stops/search?keyword=cau+giay
[
  {
    "id": "3f2a1b0c-0000-0000-0000-000000000000",
    "name": "Trạm Cầu Giấy",
    "address": "Số 1 Cầu Giấy, Hà Nội",
    "latitude": 21.0307,
    "longitude": 105.8034
  }
]
```

Trả về đúng entity `Stop` của mục "Trạm dừng" — **không thêm trường nào**. Màn hình cần `name` để
hiển thị và để gửi thẳng vào `GET /routes/search`; `address` để phân biệt hai trạm trùng tên nằm ở
hai đường khác nhau.

- **Xếp hạng:** tên trạm **bắt đầu bằng** từ khoá trước, rồi tên **chứa** từ khoá, cuối cùng là
  trạm chỉ khớp **địa chỉ**; trong cùng một hạng xếp theo tên tăng dần (chuẩn `vi`). Hai lần gọi
  cùng tham số cho ra cùng một kết quả.
- Không trạm nào khớp → `[]`, **không** phải 404 — bộ lọc mềm, cùng lối `GET /routes/search`.
- `limit` ngoài khoảng 1..50 → **400** `errors.limit`.
- `limit` mặc định **10**: dropdown gợi ý chỉ đọc được chừng đó dòng, lấy thêm không mang lại
  thông tin gì mà buộc hành khách phải cuộn.

> **Vì sao `keyword` KHÔNG bắt buộc (khác `GET /routes/search`)?** Hai câu hỏi khác nhau.
> `/routes/search` là ô **tìm**, thiếu từ khoá là **400**. Đây là ô **chọn** trạm: hành khách vừa
> bấm vào ô phải thấy ngay vài trạm để chọn, chứ không phải nghĩ ra từ khoá trước mới thấy gì.
> Bỏ trống trả đầu danh sách theo tên — biến ô nhập thành danh sách rút gọn thay vì ô trống rỗng.

> **Vì sao khớp cả ĐỊA CHỈ?** Mục đích là giúp hành khách **chọn đúng trạm**, không phải lọc tuyến.
> Người chỉ nhớ tên đường ("Hai Bà Trưng") vẫn tìm ra được trạm mình cần. Giá trị gửi tiếp cho
> `GET /routes/search` vẫn là **tên trạm**, mà endpoint kia khớp theo tên trạm — nên vòng khớp này
> không đổi luật của nó.

> ⚠️ **Vì sao BẮT BUỘC bỏ dấu, không chỉ bỏ hoa thường?** Trạm lưu tên CÓ dấu ("Trạm Cầu Giấy"),
> còn hành khách gõ trên điện thoại gần như luôn **không gõ dấu** — "cau giay". Chỉ `ToLower()`
> như `RouteSearchService` đang làm thì "cau giay" không khớp gì và ô gợi ý trả về rỗng đúng lúc
> hành khách cần nhất. Ô gợi ý đã viết ở frontend hiện **đã bỏ dấu phía client**
> (`normalizeForSearch` trong `frontend/src/components/stopSuggest.ts`), nên nếu endpoint này chỉ
> bỏ hoa thường thì lúc chuyển sang nó, tính năng **thoái hoá** chứ không phải nâng cấp.
>
> Gợi ý hiện thực: PostgreSQL có extension `unaccent` (nhớ kiểm thử riêng chữ **`đ`**), hoặc một
> cột chuẩn hoá sẵn — **cột thêm vào bảng là việc của Vàng Thị Dăm** (quy ước A7), nên nếu đi
> đường cột thì phải phối hợp trước.
>
> Ghi nhận kèm: `GET /routes/search` **cũng đang** chỉ bỏ hoa thường (`ToLower().Contains(...)`),
> nên hành khách gõ "ben thanh" ở ô điểm đi cũng không ra tuyến. Đó là lỗ hổng có sẵn của endpoint
> Hiếu, **không sửa trong mục này** — nhưng nếu ô gợi ý chọn sẵn tên trạm có dấu thì đường tra cứu
> tuyến cũng được lợi theo.

> **Vì sao đứng ở controller riêng (`StopsSearchController`)?** Cùng lối `RouteSearchController` và
> `TripSearchController`: mỗi bề mặt một controller, hai task hai người. `GET /stops` là CRUD của
> quản lý (Admin/Manager) trả **toàn bộ** trạm; đường này **công khai** và trả **một lát cắt**. Route
> template đầy đủ khác nhau (`api/stops/search` so với `api/stops` và `api/stops/{id:guid}`) nên
> không tranh chấp — đoạn literal `search` không thể khớp `{id:guid}`.

> **Vì sao KHÔNG mở công khai luôn `GET /stops`?** Sửa quyền của một đường đã có là đổi hình dạng
> API (⛔5), và phải gỡ `[Authorize]` ở mức lớp của `StopsController` rồi gắn lại cho **từng** hành
> động ghi — đụng vào controller của người khác nhiều hơn hẳn việc thêm một endpoint chỉ-đọc. Ngoài
> ra `GET /stops` trả **hết** trạm kèm toạ độ trong một lượt, còn ô gợi ý chỉ cần một lát cắt.

## Tuyến đường — `/routes`

> ✅ Backend đã có (`RoutesController` — Trần Trung Hiếu, story 12).
> **Toàn bộ endpoint dưới đây yêu cầu vai trò `Admin` hoặc `Manager`.** Người đã đăng nhập
> nhưng không đủ quyền nhận **403** kèm body `{ "message": "Bạn không có quyền truy cập tính năng này." }`.

### Entity `Route`

| Trường | Kiểu | Mô tả |
|---|---|---|
| `id` | `string` (GUID) | Khoá chính |
| `code` | `string` | Mã tuyến hiển thị cho hành khách — "01", "B10"… Duy nhất toàn hệ thống |
| `name` | `string` | Tên tuyến, ví dụ "Bến Thành — Chợ Lớn" |
| `origin` | `string` | Điểm đầu của tuyến — tên địa danh, không phải khoá ngoại tới Stops |
| `destination` | `string` | Điểm cuối của tuyến |
| `distanceKm` | `number` | Tổng chiều dài tuyến (km), tối đa 2 chữ số thập phân |
| `status` | `string` | `Active` = đang khai thác · `Inactive` = ngừng khai thác (lưu dạng chuỗi — quy ước A3) |
| `createdAt` | `string` (ISO 8601, UTC) | Thời điểm tạo |
| `updatedAt` | `string \| null` | `null` khi chưa sửa lần nào |

```json
// Ví dụ Route
{
  "id": "3f2a1b0c-0000-0000-0000-000000000000",
  "code": "01",
  "name": "Bến Thành — Chợ Lớn",
  "origin": "Bến Thành",
  "destination": "Chợ Lớn",
  "distanceKm": 12.5,
  "status": "Active",
  "createdAt": "2026-09-25T03:15:00Z",
  "updatedAt": null
}
```

### Endpoints

| Method | Endpoint | Mô tả | Body | Trả về |
|---|---|---|---|---|
| GET | `/routes` | Danh sách + tìm kiếm + lọc trạng thái + phân trang | — | `RouteListResponse` |
| GET | `/routes/{id}` | Chi tiết một tuyến | — | `Route` |
| POST | `/routes` | Thêm tuyến | `CreateRoute` | `Route` (201) |
| PUT | `/routes/{id}` | Sửa tuyến | `UpdateRoute` | `Route` |
| DELETE | `/routes/{id}` | **Xoá mềm** — ngừng khai thác | — | `Route` |

#### `GET /routes`

Tham số query (đều không bắt buộc):

| Tham số | Kiểu | Mặc định | Mô tả |
|---|---|---|---|
| `search` | `string` | — | Tìm theo mã, tên, điểm đầu hoặc điểm cuối — **không phân biệt hoa thường** |
| `status` | `string` | — | Lọc theo trạng thái: `Active` hoặc `Inactive`. Bỏ trống = lấy cả hai |
| `page` | `number` | `1` | Trang, tính từ 1 |
| `pageSize` | `number` | `10` | Số dòng mỗi trang, tối đa **100** |

```json
// RouteListResponse — ví dụ GET /routes?page=1&pageSize=10&status=Active
{
  "items": [ /* Route[] của trang hiện tại */ ],
  "total": 42,
  "page": 1,
  "pageSize": 10
}
```

- `total` là tổng số dòng khớp bộ lọc (không phải số dòng trong `items`) — dùng để vẽ phân trang.
- Thứ tự sắp xếp: `createdAt` giảm dần, tuyến tạo cùng lúc xếp theo `id` để phân trang ổn định.
- `page` hoặc `pageSize` ngoài khoảng hợp lệ → **400**.
- `status` không khớp `Active`/`Inactive` → danh sách rỗng (không báo lỗi — cùng lối bộ lọc
  `role` của `/admin/users`).

#### `POST /routes`

```json
// CreateRoute — body
{
  "code": "01",
  "name": "Bến Thành — Chợ Lớn",
  "origin": "Bến Thành",
  "destination": "Chợ Lớn",
  "distanceKm": 12.5
}
```

| Trường | Bắt buộc | Ràng buộc |
|---|---|---|
| `code` | ✅ | 2–20 ký tự, duy nhất toàn hệ thống |
| `name` | ✅ | 2–200 ký tự |
| `origin` | ✅ | 2–200 ký tự |
| `destination` | ✅ | 2–200 ký tự |
| `distanceKm` | — | Từ 0 đến `9999.99` — trần của cột numeric(6,2). Bỏ trống = 0 |

Lỗi thường gặp: trùng mã tuyến → **400** `errors.code` · `distanceKm` âm hoặc vượt trần →
**400** `errors.distanceKm`.

> **Vì sao trùng mã tuyến trả 400 mà không phải 409?** Mã tuyến là khoá nghiệp vụ do quản lý
> nhập tay vào ô form — cùng lý do trùng SĐT trả 400 `errors.phoneNumber`: frontend gắn thẳng
> lỗi vào ô input. (Trùng giá vé trả 409 vì đối tượng được chọn trong danh sách có sẵn, không
> phải gõ tay.)

#### `PUT /routes/{id}`

Body gồm đủ 5 trường của `CreateRoute` **cộng thêm `status`**:

```json
{ "code": "01", "name": "…", "origin": "…", "destination": "…", "distanceKm": 12.5, "status": "Active" }
```

| Trường | Bắt buộc | Ràng buộc |
|---|---|---|
| `code`, `name`, `origin`, `destination` | ✅ | Như POST |
| `distanceKm` | — | Như POST |
| `status` | — | `Active` hoặc `Inactive`. Bỏ trống = giữ nguyên trạng thái hiện tại |

- `status` ngoài hai mã trên → **400** `errors.status`.
- Đây cũng là cách **mở lại tuyến đã ngừng khai thác**: PUT với `status: "Active"`.

#### `DELETE /routes/{id}` — xoá mềm

Chuyển tuyến về `Inactive`, **không xoá dữ liệu** khỏi CSDL — còn chuyến và vé cũ tham chiếu
tới, và quy ước A4 cấm thêm cột `IsDeleted`, nên dùng đúng cột trạng thái sẵn có.

Trả **200** kèm `Route` đã ngừng khai thác — giống `DELETE /admin/users/{id}` trả về tài khoản
đã khóa. Tuyến đã `Inactive` gọi lại → **200** không báo lỗi (idempotent).

## Quản trị người dùng — `/admin/users`

> ✅ Backend đã có (`AdminUserController` — Nguyễn Duy Kiên, story 22).
> Frontend đổi `USE_MOCK = false` trong `frontend/src/api/adminUserApi.ts` là chạy được thật.
>
> **Toàn bộ endpoint dưới đây yêu cầu vai trò `Admin`.** Người đã đăng nhập nhưng không phải
> Admin nhận **403** kèm body `{ "message": "Bạn không có quyền truy cập tính năng này." }`.

### Entity `AdminUser`

| Trường | Kiểu | Mô tả |
|---|---|---|
| `id` | `string` (GUID) | Khoá chính |
| `fullName` | `string` | Họ và tên |
| `phoneNumber` | `string` | SĐT đăng nhập, duy nhất toàn hệ thống |
| `email` | `string \| null` | Không bắt buộc — tài khoản vẫn đăng nhập được bằng SĐT |
| `isActive` | `boolean` | `true` = đang mở, `false` = đã bị khóa |
| `role` | `string` | Mã **vai trò chính** — cột "Vai trò" trên bảng |
| `roles` | `string[]` | **Toàn bộ** vai trò tài khoản đang giữ (vai trò chính + bảng nối `UserRoles`), xếp theo thứ tự chữ cái |
| `createdAt` | `string` (ISO 8601, UTC) | Thời điểm tạo |

Mã vai trò hợp lệ — đúng 4 giá trị, khớp `Role.Code` và claim role trong JWT:
`Admin` · `Manager` · `Driver` · `Passenger`

```json
// Ví dụ AdminUser
{
  "id": "3f2a1b0c-0000-0000-0000-000000000000",
  "fullName": "Nguyễn Văn An",
  "phoneNumber": "0912345678",
  "email": "an@gmail.com",
  "isActive": true,
  "role": "Manager",
  "roles": ["Manager", "Passenger"],
  "createdAt": "2026-09-24T03:15:00Z"
}
```

### Endpoints

| Method | Endpoint | Mô tả | Body | Trả về |
|---|---|---|---|---|
| GET | `/admin/users` | Danh sách + lọc + phân trang | — | `AdminUserListResponse` |
| GET | `/admin/users/{id}` | Chi tiết một tài khoản | — | `AdminUser` |
| POST | `/admin/users` | Tạo tài khoản | `CreateAdminUser` | `AdminUser` (201) |
| PUT | `/admin/users/{id}` | Sửa hồ sơ | `UpdateAdminUser` | `AdminUser` |
| DELETE | `/admin/users/{id}` | **Xoá mềm** — khóa tài khoản | — | `AdminUser` |
| PATCH | `/admin/users/{id}/status` | Khóa / mở khóa tài khoản | `{ isActive }` | `AdminUser` |
| PUT | `/admin/users/{id}/roles` | Gán **và** thu hồi vai trò | `{ roleCodes }` | `AdminUser` |

#### `GET /admin/users`

Tham số query (đều không bắt buộc):

| Tham số | Kiểu | Mặc định | Mô tả |
|---|---|---|---|
| `search` | `string` | — | Tìm theo họ tên, SĐT hoặc email — **không phân biệt hoa thường** |
| `role` | `RoleCode` | — | Lọc theo **vai trò chính**, khớp đúng cột `role` đang hiển thị |
| `isActive` | `boolean` | — | `true` = đang mở, `false` = đã khóa. Bỏ trống = lấy cả hai |
| `page` | `number` | `1` | Trang, tính từ 1 |
| `pageSize` | `number` | `10` | Số dòng mỗi trang, tối đa **100** |

```json
// AdminUserListResponse — ví dụ GET /admin/users?page=1&pageSize=10&role=Manager
{
  "items": [ /* AdminUser[] của trang hiện tại */ ],
  "total": 42,
  "page": 1,
  "pageSize": 10
}
```

- `total` là tổng số dòng khớp bộ lọc (không phải số dòng trong `items`) — dùng để vẽ phân trang.
- Thứ tự sắp xếp: `createdAt` giảm dần, tài khoản tạo cùng lúc xếp theo `id` để phân trang ổn định.
- `page` hoặc `pageSize` ngoài khoảng hợp lệ → **400**.

#### `POST /admin/users`

```json
// CreateAdminUser — body
{
  "fullName": "Nguyễn Văn An",
  "phoneNumber": "0912345678",
  "email": "an@gmail.com",
  "password": "matkhau123",
  "roleCode": "Manager"
}
```

| Trường | Bắt buộc | Ràng buộc |
|---|---|---|
| `fullName` | ✅ | 2–200 ký tự |
| `phoneNumber` | ✅ | SĐT di động Việt Nam: 10 số, bắt đầu `0[35789]` |
| `email` | — | Đúng định dạng, tối đa 256 ký tự |
| `password` | ✅ | Ít nhất 8 ký tự, gồm **cả chữ và số** — giống hệt form đăng ký |
| `roleCode` | ✅ | Một trong 4 mã vai trò |

Lỗi thường gặp: trùng SĐT → **400** `errors.phoneNumber` · trùng email → **400** `errors.email` ·
`roleCode` không tồn tại → **400** `errors.roleCode`.

> **Vì sao trùng SĐT trả 400 mà không phải 409?** Trường này dùng chung cấu trúc lỗi theo-từng-ô
> (`errors.phoneNumber`) với form đăng ký, và frontend gắn thẳng vào ô input. Đổi sang 409 sẽ buộc
> màn hình quản trị xử lý lỗi theo một cách khác với màn hình đăng ký.

#### `PUT /admin/users/{id}`

Body gồm `fullName`, `phoneNumber`, `email` — cùng ràng buộc như trên.
**Không** đổi mật khẩu (nghiệp vụ riêng), **không** đổi vai trò hay trạng thái (có endpoint riêng).

#### `DELETE /admin/users/{id}` — xoá mềm

Khóa tài khoản (`isActive = false`), **không xoá dữ liệu** khỏi CSDL. Quy ước A4 cấm thêm cột
`IsDeleted` và chỉ cho dùng cột trạng thái sẵn có, nên "xoá" ở đây chính là khóa.

Trả **200** kèm `AdminUser` đã khóa — giống `PATCH /status` với `{ "isActive": false }`.

#### `PATCH /admin/users/{id}/status`

```json
{ "isActive": false }
```

- Thiếu hẳn trường `isActive` → **400** (cố ý, xem ghi chú trong `UpdateUserStatusRequest`).
- Gọi lại với đúng trạng thái hiện tại → **200**, không báo lỗi (idempotent).

#### `PUT /admin/users/{id}/roles`

```json
// Gán thêm Manager và Driver, thu hồi mọi vai trò khác
{ "roleCodes": ["Manager", "Driver"] }
```

Gửi lên **danh sách đầy đủ** vai trò muốn tài khoản giữ:

- Mã **chưa có** → gán thêm.
- Mã **không còn trong danh sách** → thu hồi.
- Gửi lại cùng một body → không đổi gì, không báo lỗi (idempotent).
- Danh sách rỗng hoặc mã vai trò không tồn tại → **400** `errors.roleCodes`.

Vai trò chính (`role`) được cập nhật tự động: giữ nguyên nếu tài khoản vẫn còn vai trò đó,
ngược lại chuyển sang vai trò có mức quyền cao nhất trong danh sách mới
(Admin → Manager → Driver → Passenger).

### Hai chốt an toàn — cả hai trả **409**

| Tình huống | Thông báo |
|---|---|
| Admin tự khóa / tự xoá tài khoản của chính mình | `Không thể tự khóa tài khoản của chính mình` |
| Admin tự bỏ vai trò Admin khỏi chính mình | `Không thể tự thu hồi vai trò Admin của chính mình` |

Cả hai đều dẫn tới cùng một ngõ cụt: mọi endpoint quản trị đều đòi vai trò Admin, nên tự khoá
mình xong là **không còn đường nào mở lại** trong hệ thống. Chặn ở tầng API là cách duy nhất
không phụ thuộc vào việc frontend có ẩn nút hay không.

### Hiệu lực tức thì, không cần đăng nhập lại

`JwtMiddleware` đọc lại `IsActive` và **toàn bộ** vai trò từ CSDL ở mọi request, nên khóa tài khoản
hoặc thu hồi vai trò có hiệu lực ngay với access token đang dùng — không phải chờ token hết hạn
(mặc định 30 phút) và không cần thu hồi refresh token.

## Bảng giá vé — `/routes/{routeId}/fares`

> ✅ Backend đã có (`FaresController` — Phùng Duy Hoàng, story 12).
> **Toàn bộ endpoint dưới đây yêu cầu vai trò `Admin` hoặc `Manager`.** Người đã đăng nhập nhưng
> không đủ quyền nhận **403** kèm body `{ "message": "Bạn không có quyền truy cập tính năng này." }`.

Giá vé gắn với **tuyến** và **đối tượng hành khách**: mỗi cặp (tuyến, đối tượng) có đúng một giá —
ràng buộc unique `(RouteId, PassengerType)` ở CSDL, xem quy ước A6.

### Entity `Fare`

| Trường | Kiểu | Mô tả |
|---|---|---|
| `id` | `string` (GUID) | Khoá chính |
| `routeId` | `string` (GUID) | Tuyến mà dòng giá này thuộc về |
| `passengerType` | `string` | Đối tượng áp dụng — xem bảng mã bên dưới |
| `price` | `number` | Giá vé (VND), tối đa 2 chữ số thập phân |
| `createdAt` | `string` (ISO 8601, UTC) | Thời điểm tạo |
| `updatedAt` | `string \| null` | `null` khi chưa sửa lần nào |

Mã `passengerType` hợp lệ — đúng **5** giá trị, khớp enum `PassengerType` của backend:

| Mã | Nghĩa |
|---|---|
| `Standard` | Người lớn — giá phổ thông, giá gốc của tuyến |
| `Student` | Học sinh, sinh viên (đối tượng ưu đãi — US 17) |
| `Senior` | Người cao tuổi (đối tượng ưu đãi — US 17) |
| `Child` | Trẻ em |
| `Disabled` | Người khuyết tật |

> Trả về **chuỗi** chứ không phải số: `"Student"` đọc là hiểu, còn `1` thì phải tra code —
> cùng lý do quy ước A3 bắt cột trạng thái lưu dạng chuỗi.

```json
// Ví dụ Fare
{
  "id": "8c1d4e77-0000-0000-0000-000000000000",
  "routeId": "3f2a1b0c-0000-0000-0000-000000000000",
  "passengerType": "Student",
  "price": 5000.00,
  "createdAt": "2026-09-25T03:15:00Z",
  "updatedAt": null
}
```

### Endpoints

| Method | Endpoint | Mô tả | Body | Trả về |
|---|---|---|---|---|
| GET | `/routes/{routeId}/fares` | Bảng giá của tuyến | — | `Fare[]` |
| GET | `/routes/{routeId}/fares/{id}` | Một dòng giá | — | `Fare` |
| POST | `/routes/{routeId}/fares` | Thêm giá cho một đối tượng | `CreateFare` | `Fare` (201) |
| PUT | `/routes/{routeId}/fares/{id}` | Sửa **giá** | `UpdateFare` | `Fare` |
| DELETE | `/routes/{routeId}/fares/{id}` | Xoá một dòng giá | — | 204 No Content |

#### `GET /routes/{routeId}/fares`

Sắp xếp theo `passengerType` **đúng thứ tự khai báo trong enum** (Standard → Student → Senior →
Child → Disabled), không theo alphabet — đây là thứ tự hiển thị của bảng giá trên màn hình.

- Tuyến chưa cấu hình giá nào → mảng rỗng, **không** phải 404.
- Tuyến không tồn tại → **404**.

#### `POST /routes/{routeId}/fares`

```json
{ "passengerType": "Student", "price": 5000 }
```

| Trường | Bắt buộc | Ràng buộc |
|---|---|---|
| `passengerType` | ✅ | Một trong 5 mã ở bảng trên |
| `price` | ✅ | Lớn hơn 0, tối đa `9999999999.99` |

Lỗi thường gặp: tuyến không tồn tại → **404** · `passengerType` ngoài 5 mã → **400**
`errors.passengerType` · `price` ≤ 0 hoặc vượt giới hạn → **400** `errors.price` ·
tuyến đã có giá cho đối tượng đó → **409**.

> **Vì sao trùng trả 409 mà không phải 400?** Đây không phải dữ liệu sai định dạng mà là xung đột
> với dữ liệu đang có — cùng loại với trùng ghế ở mục D2. Muốn đổi giá thì sửa dòng đã có.

#### `PUT /routes/{routeId}/fares/{id}`

Body **chỉ gồm `price`**. Cố ý không cho đổi `passengerType`: đổi đối tượng tại chỗ có thể đâm vào
ràng buộc unique của đối tượng kia, mà cách xử lý (báo 409? gộp dòng?) lại tuỳ ngữ cảnh. Muốn đổi
đối tượng thì xoá dòng cũ rồi tạo dòng mới — hai thao tác đều đã có endpoint riêng.

#### `DELETE /routes/{routeId}/fares/{id}`

Xoá cứng. `Fare` không có cột trạng thái và không bảng nào tham chiếu tới nó, nên không cần xoá
mềm — khác `Routes` (A4 chỉ cho dùng cột trạng thái sẵn có, mà `Fare` không có cột nào như vậy).

#### `routeId` phải khớp

`GET`/`PUT`/`DELETE` trên `/routes/{routeId}/fares/{id}` đều kiểm tra dòng giá có **thuộc đúng**
tuyến đó không. Dòng giá của tuyến khác → **404**, không phải 200.

## Trạm trên tuyến — `/routes/{routeId}/stops`

> ✅ Backend đã có (`RouteStopsController` — Nguyễn Duy Kiên, story 12). Frontend chưa có module
> gọi API này: màn hình gán trạm bằng kéo-thả là task riêng của Hoàng Văn Thịnh, dựng theo khuôn
> `frontend/src/api/fareApi.ts`.
>
> **Toàn bộ endpoint dưới đây yêu cầu vai trò `Admin` hoặc `Manager`.** Người đã đăng nhập nhưng
> không đủ quyền nhận **403** kèm body `{ "message": "Bạn không có quyền truy cập tính năng này." }`.

Bảng `RouteStops` là bảng nối `Routes` ↔ `Stops`, kiêm **thứ tự trạm trên tuyến**. Một trạm không
xuất hiện hai lần trên cùng một tuyến — ràng buộc unique `(RouteId, StopId)` ở CSDL (quy ước A6).
Cùng một trạm **được** nằm trên nhiều tuyến khác nhau: ràng buộc là theo *cặp*, không phải theo trạm.

Thứ tự trạm là thứ tự xe chạy thật, và là thứ duy nhất xác định chiều của tuyến — xe đi và xe về
trên cùng tuyến trùng toạ độ, nên không phân biệt được bằng khoảng cách (quy ước A8.6).

### Entity `RouteStop`

| Trường | Kiểu | Mô tả |
|---|---|---|
| `id` | `string` (GUID) | Khoá chính của dòng bảng nối |
| `routeId` | `string` (GUID) | Tuyến |
| `stopId` | `string` (GUID) | Trạm |
| `stopName` | `string` | Tên trạm — kèm sẵn để màn hình kéo-thả hiển thị mà không phải gọi thêm `/stops` |
| `stopAddress` | `string` | Địa chỉ trạm |
| `latitude` | `number` | Vĩ độ trạm |
| `longitude` | `number` | Kinh độ trạm |
| `stopOrder` | `number` | Thứ tự trạm trên tuyến, tính từ **1** và liên tục |
| `distanceKm` | `number` | Khoảng cách từ trạm liền trước tới trạm này (km), tối đa 2 chữ số thập phân. Trạm đầu tiên = 0 |

```json
// Ví dụ RouteStop
{
  "id": "b7e4c9a1-0000-0000-0000-000000000000",
  "routeId": "3f2a1b0c-0000-0000-0000-000000000000",
  "stopId": "9d2f5c33-0000-0000-0000-000000000000",
  "stopName": "Trạm Cầu Giấy",
  "stopAddress": "Số 1 Cầu Giấy, Hà Nội",
  "latitude": 21.0307,
  "longitude": 105.8034,
  "stopOrder": 2,
  "distanceKm": 1.8
}
```

### Endpoints

| Method | Endpoint | Mô tả | Body | Trả về |
|---|---|---|---|---|
| GET | `/routes/{routeId}/stops` | Danh sách trạm của tuyến, xếp theo `stopOrder` | — | `RouteStop[]` |
| POST | `/routes/{routeId}/stops` | Gán một trạm vào tuyến, nối vào **cuối** | `{ stopId, distanceKm? }` | `RouteStop` (201) |
| PUT | `/routes/{routeId}/stops/order` | Sắp xếp lại thứ tự **toàn bộ** trạm của tuyến | `{ items: [{ stopId, distanceKm }] }` | `RouteStop[]` |
| DELETE | `/routes/{routeId}/stops/{id}` | Gỡ một trạm khỏi tuyến | — | 204 No Content |

#### `GET /routes/{routeId}/stops`

- Thứ tự: `stopOrder` tăng dần; hai dòng cùng `stopOrder` (xem ghi chú bên dưới) xếp tiếp theo
  `id` để thứ tự hiển thị luôn ổn định.
- Tuyến chưa gán trạm nào → mảng rỗng, **không** phải 404.
- Tuyến không tồn tại → **404**.

#### `POST /routes/{routeId}/stops`

```json
{ "stopId": "9d2f5c33-0000-0000-0000-000000000000", "distanceKm": 1.8 }
```

| Trường | Bắt buộc | Ràng buộc |
|---|---|---|
| `stopId` | ✅ | GUID của trạm có thật |
| `distanceKm` | — | Từ 0 đến `9999.99` — trần của cột numeric(6,2). Bỏ trống = 0 |

Trạm mới luôn nối vào **cuối** tuyến. Muốn chèn vào giữa thì gán xong rồi gọi
`PUT /routes/{routeId}/stops/order` — hai thao tác đều đã có endpoint riêng, không cần thêm tham
số vị trí cho POST.

Trả **201** kèm `RouteStop` vừa tạo. Header `Location` trỏ về chính danh sách trạm của tuyến
(`/api/routes/{routeId}/stops`) — không có endpoint xem riêng một dòng, và cũng không mở thêm chỉ
để có chỗ cho `Location`.

Lỗi thường gặp: tuyến không tồn tại → **404** · trạm không tồn tại → **404** · trạm đã nằm trên
tuyến này → **409** · `distanceKm` âm hoặc vượt trần → **400** `errors.distanceKm`.

> **Vì sao trùng trạm trả 409 mà không phải 400?** Trạm được chọn từ danh sách có sẵn chứ không gõ
> tay, và đây là xung đột với dữ liệu đang có — cùng loại với trùng giá vé ở mục trên.

#### `PUT /routes/{routeId}/stops/order`

Thay thế **toàn phần**: gửi lên đủ và đúng danh sách trạm hiện có của tuyến, theo thứ tự mới.
`stopOrder` **không** nhận từ client — server suy ra từ vị trí trong mảng (phần tử đầu = 1), nhờ
vậy không thể tồn tại hai trạm cùng thứ tự do client gửi lên.

```json
{
  "items": [
    { "stopId": "…c1", "distanceKm": 0 },
    { "stopId": "…a2", "distanceKm": 1.8 },
    { "stopId": "…f3", "distanceKm": 2.5 }
  ]
}
```

| Trường | Bắt buộc | Ràng buộc |
|---|---|---|
| `items` | ✅ | Ít nhất 1 phần tử |
| `items[].stopId` | ✅ | Tập `stopId` gửi lên phải **đúng bằng** tập trạm hiện có của tuyến — thiếu hay thừa đều bị chặn |
| `items[].distanceKm` | — | Như POST, nhưng bỏ trống = **giữ nguyên** giá trị đang có (không phải gán 0) |

Lỗi: tuyến không tồn tại → **404** · mảng rỗng · trùng `stopId` trong mảng · tập `stopId` không
khớp tập trạm hiện có → **400** `errors.items`.

> **Vì sao chặn khi thiếu/thừa trạm?** Đây là lưới an toàn cho thao tác kéo-thả: màn hình gửi thiếu
> một dòng mà server cứ ghi đè thì trạm đó bị gỡ khỏi tuyến mà không ai chủ ý. Muốn gỡ trạm thì
> gọi `DELETE` — một thao tác nói rõ ý định.

> **Vì sao bỏ trống `distanceKm` là giữ nguyên?** Đây là thao tác "sắp xếp lại thứ tự", không phải
> "viết lại khoảng cách". Nếu bỏ trống mà hiểu là 0 thì một lần kéo-thả sẽ xoá sạch khoảng cách
> quản lý đã nhập — cùng lối `status` bỏ trống ở `PUT /routes/{id}` là giữ nguyên trạng thái.
> Muốn đặt lại về 0 thì gửi thẳng `"distanceKm": 0`.

Trả về danh sách trạm **sau khi** sắp xếp, cùng hình dạng với `GET`.

#### `DELETE /routes/{routeId}/stops/{id}`

Gỡ trạm khỏi tuyến rồi **dồn số** các trạm còn lại thành 1..N liên tục, nên `stopOrder` không bao
giờ có lỗ hổng. Trả **204 No Content**.

⚠️ **`{id}` là `id` của DÒNG `RouteStop`, KHÔNG phải `stopId`.** Mỗi dòng có hai GUID: `id` (khoá
của dòng bảng nối) và `stopId` (khoá của trạm). Endpoint này nhận **`id`** — đúng giá trị `id` mà
`GET`/`POST`/`PUT` trả về cho từng dòng, nên màn hình kéo-thả gửi thẳng `item.id` là xong. Gửi
`stopId` vào đây sẽ ra **404**.

> **Vì sao không nhận thẳng `stopId` cho tiện?** Vì `{id}` phải là khoá của chính bản ghi bị tác
> động, và `AuditLogMiddleware` dựa vào đúng tên tham số `id` để ghi `Target` của nhật ký hoạt động
> (`Services/AuditLogMiddleware.cs`, hàm `ResolveTableName`) — đặt tên khác thì dòng nhật ký mất hẳn
> id đối tượng, còn lại mỗi chữ "Stops" không nói được đã gỡ trạm nào. Cùng lối
> `DELETE /routes/{routeId}/fares/{id}`, nơi `{id}` là khoá của dòng giá.

`stopOrder` và `distanceKm` là thuộc tính của dòng bảng nối, còn tên, địa chỉ, toạ độ trạm là của
`Stop`: muốn sửa trạm thì gọi `PUT /stops/{id}` của `StopsController`, không sửa qua đường dẫn này.

Tuyến không tồn tại → **404** · trạm không nằm trên tuyến này → **404** (không phải 204 — gỡ một
thứ không có sẵn không phải là thành công).

#### `routeId` phải khớp

Mọi endpoint đều kiểm tra trạm có **thuộc đúng** tuyến đó không. Trạm của tuyến khác → **404**.

> ⚠️ **Vì sao `stopOrder` không có ràng buộc unique ở CSDL?** Hai request `POST` chạy song song có
> thể cùng đọc "tuyến đang có N trạm" rồi cùng ghi `N + 1`. CSDL không chặn được vì `RouteStops`
> chỉ unique trên `(RouteId, StopId)`, không unique trên `(RouteId, StopOrder)`. Hệ quả duy nhất
> là hai trạm cùng thứ tự; `GET` xếp tiếp theo `id` nên thứ tự hiển thị vẫn ổn định, và gọi
> `PUT /stops/order` một lần là về đúng thứ tự. Thêm unique index cần migration — việc của chủ CSDL.

## Tra cứu tuyến — `/routes/search`

> ✅ Backend đã có (`RouteSearchController` — Trần Trung Hiếu, story 1, task *"API tìm kiếm chuyến
> theo điểm đi, điểm đến, ngày giờ"*). Frontend đang ghép tạm từ `GET /routes` + `/stops` +
> `/fares` (cần vai trò Manager/Admin) — đổi sang endpoint này thì màn "Tra cứu tuyến" thành công
> khai hoàn toàn (xem ghi chú đầu `frontend/src/api/routeLookupApi.ts`).
>
> **Endpoint CÔNG KHAI** — hành khách tra cứu trước khi đăng nhập, cùng nhóm với
> `GET /trips/search` ở mục "Chuyến xe" bên dưới. Khác hẳn `GET /routes` của mục "Tuyến đường":
> đường kia là CRUD của quản lý (Admin/Manager), đường này trả lời câu hỏi của hành khách
> *"điểm đi A, điểm đến B, ngày D → những tuyến nào?"*.

### Endpoint

| Method | Endpoint | Mô tả | Body | Trả về |
|---|---|---|---|---|
| GET | `/routes/search` | Tìm tuyến cho hành khách (**công khai**): điểm đi, điểm đến, ngày đi | — | `RouteSearchResult[]` |

Tham số query:

| Tham số | Kiểu | Mặc định | Mô tả |
|---|---|---|---|
| `origin` | `string` | — | **Bắt buộc.** Tên điểm đi — khớp tên trạm dừng (`Stop.name`), **không phân biệt hoa thường** |
| `destination` | `string` | — | **Bắt buộc.** Tên điểm đến — cùng lối khớp trên |
| `date` | `string` (`yyyy-MM-dd`) | — | Ngày đi. Có ngày thì chỉ trả tuyến có ít nhất một chuyến `Scheduled` khởi hành trong **trọn ngày đó giờ Việt Nam** |

```json
// RouteSearchResult[] — ví dụ GET /routes/search?origin=Bến+Thành&destination=Chợ+Lớn
[
  {
    "routeId": "3f2a1b0c-0000-0000-0000-000000000000",
    "routeCode": "01",
    "routeName": "Bến Thành — Chợ Lớn",
    "origin": "Bến Thành",
    "destination": "Chợ Lớn",
    "distanceKm": 7.0,
    "stops": [
      { "stopId": "9d2f5c33-0000-0000-0000-000000000000", "stopName": "Bến Thành", "stopOrder": 1 },
      { "stopId": "a1b2c3d4-0000-0000-0000-000000000000", "stopName": "Công viên 23/9", "stopOrder": 2 },
      { "stopId": "b7e4c9a1-0000-0000-0000-000000000000", "stopName": "Đại học Y Dược", "stopOrder": 3 },
      { "stopId": "c8f5d0b2-0000-0000-0000-000000000000", "stopName": "Chợ Lớn", "stopOrder": 4 }
    ],
    "minPrice": 3500
  }
]
```

| Trường | Kiểu | Mô tả |
|---|---|---|
| `routeId` | `string` (GUID) | Khoá tuyến — màn hình gửi thẳng cho `GET /trips/search` để xem chuyến |
| `routeCode` | `string` | Mã tuyến hiển thị — "01" |
| `routeName` | `string` | Tên tuyến |
| `origin` | `string` | Điểm đầu của tuyến (để hiển thị, không phải kết quả khớp — xem ghi chú dưới) |
| `destination` | `string` | Điểm cuối của tuyến |
| `distanceKm` | `number` | Tổng chiều dài tuyến (km) |
| `stops[]` | `RouteSearchStop[]` | Trạm của tuyến theo đúng thứ tự chạy (`stopOrder` tăng dần) |
| `minPrice` | `number` \| `null` | Giá vé **thấp nhất** trong bảng giá của tuyến (VND) — màn hình hiển thị "giá từ …". Tuyến chưa cấu hình giá → `null` |

- **Khớp theo TÊN TRẠM, không phải theo `origin`/`destination` của tuyến**: hành khách tìm từ trạm
  giữa tuyến ("Công viên 23/9" → "Chợ Lớn") vẫn ra tuyến 01. Tuyến khớp khi chứa **CẢ** trạm đi
  lẫn trạm đến, và trạm đi đứng **TRƯỚC** trạm đến theo `stopOrder` — chiều chạy xác định bằng
  thứ tự trạm, không bằng toạ độ (quy ước A8.6). Truy vấn này chạy trên chỉ mục
  `RouteStops(StopId, RouteId, StopOrder)` mà task *"Tối ưu truy vấn tìm tuyến"* (Dăm) đã chuẩn bị.
- Chỉ trả tuyến `Active` — tuyến ngừng khai thác không phải lựa chọn cho hành khách.
- `origin`/`destination` thiếu hoặc toàn khoảng trắng → **400**; hai điểm trùng nhau (sau khi bỏ
  khoảng trắng thừa, không phân biệt hoa thường) → **400** `errors.destination` "Điểm đi và điểm
  đến không được trùng nhau" — cùng lối chặn sớm của form tra cứu.
- `date` sai định dạng → **400** `errors.date`. Không có `date` thì không lọc theo chuyến — trả
  mọi tuyến khớp điểm đi/điểm đến.
- Không có tuyến nào khớp → `[]`, **không** phải 404 — bộ lọc mềm, cùng lối `GET /trips/search`.
- Thứ tự: mã tuyến tăng dần, trùng mã xếp theo `routeId` để hai lần gọi ra cùng một kết quả.
  Màn hình có thể sắp xếp lại theo ý hành khách.
- Trả về **mảng trần**, không phân trang: một truy vấn tra cứu chỉ ra số tuyến nhỏ — màn hình đọc
  hết để hiển thị lưới kết quả, cùng lối `GET /routes/{routeId}/stops`.

> **Ranh giới với `GET /trips/search` (Phùng Duy Hoàng):** endpoint này trả **TUYẾN** — kết quả
> của việc tìm tuyến từ điểm đi/điểm đến; endpoint kia trả **CHUYẾN** của một tuyến đã chọn kèm
> giá phổ thông và số ghế còn trống. Màn "Kết quả tìm kiếm" (Băng, đã xong) ghép hai bước lại:
> lấy `routeId` từ đây rồi gọi `GET /trips/search?routeId=…&from=…&to=…` của trọn ngày đã chọn.

> **Vì sao đứng ở controller riêng (`RouteSearchController`)?** Cùng lối `TripSearchController`
> ở mục dưới: mỗi bề mặt một controller, hai task thuộc hai người. Route template đầy đủ khác
> nhau (`api/routes/search` so với `api/routes` và `api/routes/{id:guid}`) nên không tranh chấp —
> đoạn literal `search` không thể khớp `{id:guid}`.

> **Vì sao không gộp vào `GET /routes` (có sẵn tham số `search`)?** Hai câu hỏi khác nhau về
> quyền và hình dạng. `GET /routes` là bộ lọc phân trang cho màn hình quản lý, khớp một từ khoá
> với mã/tên/điểm đầu/điểm cuối và yêu cầu Admin/Manager. `GET /routes/search` khớp **hai** điểm
> với các trạm trên tuyến, trả mảng trần kèm trạm và giá để hành khách lựa chọn, và phải công
> khai cho người chưa đăng nhập. Trộn hai thứ vào một endpoint thì hình dạng response phải biến
> đổi theo người gọi — trái lối "mỗi bề mặt một controller" của dự án.

## Chuyến xe — `/trips`

> ✅ Backend **đã có `GET /trips/{id}`** (`TripsController` — Vàng Thị Dăm, story 13) và
> **`GET /trips`** (`TripLookupController` — Phùng Duy Hoàng, story 13). Task còn lại của story 13
> là *BackgroundService sinh chuyến tự động* (Kiên) — **chưa làm**. Đường **tạo** chuyến thì đã có:
> CRUD lịch trình theo tuyến của Hiếu ở mục "Lịch trình chạy xe — `/routes/{routeId}/trips`" bên
> dưới. Xem cột "Assign" ở sheet `Sprint 2` của `Product_Backlog_Smart_Bus.xlsx`.
>
> **Mọi endpoint dưới đây yêu cầu vai trò `Admin` hoặc `Manager` — trừ `GET /trips/search`**,
> API công khai dành cho hành khách chưa đăng nhập (story 1, mục riêng bên dưới). Người đã đăng
> nhập nhưng không đủ quyền nhận **403** kèm body `{ "message": "Bạn không có quyền truy cập tính năng này." }`.

### Vì sao chỉ có hai bảng, không có `Schedules` và không có `TripStops`

Story 13 được phát biểu trong backlog là *"Migrate bảng Schedules, Trips, TripStops"*, nhưng quy ước
**A8.3** chốt **không tách bảng `Schedules`** và **A9** chốt **không có bảng `TripStops`**. Hệ quả
trực tiếp lên hợp đồng API:

- **Không có `Schedules`.** Lịch trình định kỳ không lưu thành bảng mẫu. Quản lý chọn tuyến + giờ bắt
  đầu + giờ kết thúc + tần suất (phút) → hệ thống sinh thẳng ra N dòng `Trips`. Nếu có bảng mẫu thì
  mọi endpoint phải trả lời thêm một câu mà không story nào hỏi: *"sửa mẫu thì các chuyến đã sinh có
  đổi theo không?"*.
- **Không có `TripStops`.** Thứ tự trạm của một chuyến **không** lưu riêng: cùng một tuyến, mọi
  chuyến đều dừng đúng dãy trạm của tuyến đó theo `RouteStop.stopOrder`. Vì vậy `stops` trong chi
  tiết chuyến là **suy ra** từ `/routes/{routeId}/stops`, không phải một bảng riêng — và cũng vì thế
  mà sửa thứ tự trạm của tuyến là mọi chuyến của tuyến đó đổi theo, không cần đồng bộ gì.

### Endpoints

| Method | Endpoint | Mô tả | Body | Trả về |
|---|---|---|---|---|
| GET | `/trips` | Tra cứu danh sách chuyến theo ngày + lọc theo tuyến/trạng thái + phân trang | — | `TripLookupListResponse` |
| GET | `/trips/search` | Tìm chuyến cho hành khách (**công khai**): giờ chạy, giá vé, số ghế còn trống — kết quả được đệm tối đa 60 giây | — | `TripSearchResult[]` |
| GET | `/trips/{id}` | Chi tiết một chuyến: giờ chạy, xe, sức chứa, danh sách trạm dừng | — | `TripDetail` |
| PATCH | `/trips/{id}/assignment` | Đổi xe/đổi tài xế một chuyến (luồng sự cố) — cảnh báo trùng lịch, không chặn | `ReassignTrip` | `TripAssignment` |

#### `GET /trips` — tra cứu danh sách chuyến theo ngày

Tham số query (đều không bắt buộc):

| Tham số | Kiểu | Mặc định | Mô tả |
|---|---|---|---|
| `routeId` | `string` (GUID) | — | Chỉ lấy chuyến của một tuyến. Bỏ trống = mọi tuyến. GUID không trỏ tới tuyến nào → **404** |
| `from` | `string` (ISO 8601 có múi giờ) | — | Chỉ lấy chuyến khởi hành **từ** thời điểm này, tính luôn mốc |
| `to` | `string` (ISO 8601 có múi giờ) | — | Chỉ lấy chuyến khởi hành **tới** thời điểm này, tính luôn mốc |
| `status` | `string` | — | `Scheduled` / `Running` / `Completed` / `Cancelled`. Bỏ trống = lấy cả bốn |
| `page` | `number` | `1` | Trang, tính từ 1 |
| `pageSize` | `number` | `10` | Số dòng mỗi trang, tối đa **100** |

```json
// TripLookupListResponse — ví dụ GET /trips?routeId=3f2a1b0c-0000-0000-0000-000000000000&from=2026-10-01T00:00:00+07:00&to=2026-10-01T23:59:59+07:00&page=1&pageSize=10
{
  "items": [
    {
      "id": "6b3e8d12-0000-0000-0000-000000000000",
      "routeId": "3f2a1b0c-0000-0000-0000-000000000000",
      "routeCode": "01",
      "routeName": "Bến xe Mỹ Đình — Bến xe Gia Lâm",
      "busId": "1c9a4f05-0000-0000-0000-000000000000",
      "busLicensePlate": "29B-123.45",
      "departureTime": "2026-10-01T01:00:00Z",
      "arrivalTime": "2026-10-01T01:45:00Z",
      "status": "Scheduled",
      "createdAt": "2026-09-30T03:15:00Z",
      "updatedAt": null
    }
  ],
  "total": 42,
  "page": 1,
  "pageSize": 10
}
```

- **Lọc "theo ngày" = gửi `from`/`to` của trọn ngày đó** (ví dụ `00:00:00` → `23:59:59` giờ Việt
  Nam) — không có tham số `date` riêng: cùng một cách lọc khoảng thời gian như
  `/routes/{routeId}/trips` và `/drivers/{id}/trips` nên frontend không phải nói hai thứ tiếng.
- `total` là tổng số dòng khớp bộ lọc (không phải số dòng trong `items`) — dùng để vẽ phân trang.
- `items[]` kèm sẵn `routeCode`/`routeName`: khác danh sách chuyến của một tuyến (`routeId` đã nằm
  trong đường dẫn), kết quả ở đây trải nhiều tuyến nên mỗi dòng phải tự nói mình thuộc tuyến nào —
  cùng lối danh sách ca làm việc của tài xế. `busLicensePlate` kèm sẵn cùng lý do.
- **Không** trả 4 trường vị trí (`currentStopId`, `currentLat`, `currentLng`, `positionUpdatedAt`):
  vị trí là chuyện của nhóm story theo dõi thời gian thực (Sprint 3) — cùng lối `TripDetail` và
  `DriverTrip`.
- Thứ tự sắp xếp: `departureTime` **tăng dần** — màn hình đọc như một cuốn thời gian biểu; trùng
  giờ khởi hành xếp tiếp theo `id` để phân trang ổn định.
- Không có chuyến nào khớp bộ lọc → `items: []`, `total: 0`, **không** phải 404.
- `routeId` không trỏ tới tuyến nào → **404** `Không tìm thấy tuyến đường`: đây là tham chiếu cứng
  tới một tuyến, không phải bộ lọc mềm — cùng câu hỏi *"chuyến của tuyến X"* hỏi ở
  `/routes/{routeId}/trips` cũng trả 404, hai đường phải trả lời giống nhau. (Khác `userId` của
  `/audit-logs`: người dùng có thể không còn trong hệ thống, còn tuyến thì khoá ngoại `Restrict`
  không cho xoá cứng.)
- `to` sớm hơn `from` → **400** `errors.to`.
- `status` không khớp mã nào → danh sách rỗng (không báo lỗi — cùng lối bộ lọc `status` của
  `/routes/{routeId}/trips`).
- `page`/`pageSize` ngoài khoảng hợp lệ, hoặc `routeId` sai định dạng GUID → **400**.

> **Vì sao vừa có `/trips` vừa có `/routes/{routeId}/trips`?** Hai màn hình đi từ hai đầu khác
> nhau. Màn hình lập lịch trình đi từ tuyến ra — chọn tuyến trước, rồi xem/sửa chuyến của đúng
> tuyến đó (CRUD của Hiếu, `routeId` là một phần định danh nằm trên đường dẫn). Màn hình điều hành
> theo ngày đi từ ngày vào — xem cả ngày của toàn mạng lưới rồi thu hẹp dần theo tuyến, nên
> `routeId` ở đây là bộ lọc bỏ trống được. Gộp hai cái vào một endpoint thì màn hình lập lịch trình
> phải gửi `routeId` như tham số lọc và mất tính "chuyến nào cũng thuộc đúng tuyến trên đường dẫn".

> **Vì sao đứng ở controller riêng (`TripLookupController`)?** Cùng lối cặp `StopsController` /
> `RouteStopsController` và cặp `TripsController` / `RouteTripsController` đã có: mỗi bề mặt một
> controller, hai task thuộc hai người. Tên lớp khác nhau, route template khác nhau (`api/trips`
> so với `api/trips/{id:guid}`) nên không tranh chấp.

#### `GET /trips/search` — tìm chuyến cho hành khách: giờ chạy · giá vé · số ghế còn trống

> ✅ Backend đã có (`TripSearchController` — Phùng Duy Hoàng, story 1, task *"API trả về kết quả gồm
> giá vé, giờ chạy, số ghế còn trống"*).
>
> **Đây là một trong hai endpoint CÔNG KHAI của mục `/trips`** — gọi không cần đăng nhập. Story 1 mở đầu
> bằng *"Là hành khách, tôi muốn tìm kiếm tuyến xe…"*: tra cứu chuyến là việc trước khi đăng nhập,
> đăng nhập là bước của màn hình đặt vé. Endpoint công khai còn lại là `GET /trips/{id}/seats` (sơ
> đồ ghế — story 2). Mọi endpoint khác của mục này (kể cả `GET /trips` ngay
> trên) đều yêu cầu `Admin`/`Manager`.
>
> Endpoint trả lời câu hỏi *"tuyến này, trong khoảng ngày này, có những chuyến nào — giá bao nhiêu,
> còn bao nhiêu ghế?"*. Việc **tìm ra tuyến** từ điểm đi/điểm đến là task *"API tìm kiếm chuyến theo
> điểm đi, điểm đến, ngày giờ"* của Trần Trung Hiếu (story 1) — xem ghi chú ranh giới cuối mục này.

Tham số query:

| Tham số | Kiểu | Mặc định | Mô tả |
|---|---|---|---|
| `routeId` | `string` (GUID) | — | **Bắt buộc.** Tuyến cần xem chuyến. Thiếu hoặc sai định dạng GUID → **400**; GUID không trỏ tới tuyến nào → **404** |
| `from` | `string` (ISO 8601 có múi giờ) | — | Chỉ lấy chuyến khởi hành **từ** thời điểm này, tính luôn mốc |
| `to` | `string` (ISO 8601 có múi giờ) | — | Chỉ lấy chuyến khởi hành **tới** thời điểm này, tính luôn mốc |

```json
// TripSearchResult[] — ví dụ GET /trips/search?routeId=3f2a1b0c-0000-0000-0000-000000000000&from=2026-10-01T00:00:00+07:00&to=2026-10-01T23:59:59+07:00
[
  {
    "id": "6b3e8d12-0000-0000-0000-000000000000",
    "routeId": "3f2a1b0c-0000-0000-0000-000000000000",
    "routeCode": "01",
    "routeName": "Bến xe Mỹ Đình — Bến xe Gia Lâm",
    "departureTime": "2026-10-01T01:00:00Z",
    "arrivalTime": "2026-10-01T01:45:00Z",
    "price": 7000,
    "seatsRemaining": 45,
    "capacity": 45,
    "busType": "Xe buýt 45 chỗ"
  }
]
```

| Trường | Kiểu | Mô tả |
|---|---|---|
| `id` | `string` (GUID) | Khoá chuyến — màn hình gửi lại khi đặt vé (Sprint 3) |
| `routeId` | `string` (GUID) | Khoá tuyến |
| `routeCode` | `string` | Mã tuyến hiển thị — "01" |
| `routeName` | `string` | Tên tuyến |
| `departureTime` | `string` | Giờ khởi hành (UTC) |
| `arrivalTime` | `string` \| `null` | Giờ đến dự kiến; chuyến chưa có giờ đến → `null` |
| `price` | `number` \| `null` | **Giá vé phổ thông** của tuyến (đồng). Tuyến chưa cấu hình giá → `null` |
| `seatsRemaining` | `number` | Số ghế còn trống — xem ghi chú bên dưới |
| `capacity` | `number` | Sức chứa của xe chạy chuyến |
| `busType` | `string` | Loại xe — "Xe buýt 45 chỗ" |

- **Chỉ trả chuyến `Scheduled`** — chuyến đã chạy, đang chạy hay đã hủy không còn là lựa chọn để đặt
  vé. Vì vậy endpoint không có tham số `status` (khác `GET /trips` của màn hình điều hành).
- Thứ tự: `departureTime` tăng dần, trùng giờ xếp tiếp theo `id` để hai lần gọi ra cùng một kết quả.
  Màn hình kết quả sắp xếp lại theo giờ/giá phía client — thứ tự này không thay thế việc đó.
- Lọc "theo ngày" = gửi `from`/`to` của trọn ngày — không có tham số `date` riêng, cùng quy ước của
  `GET /trips` và `/routes/{routeId}/trips`.
- Không có chuyến nào khớp → `[]`, **không** phải 404. `routeId` không trỏ tới tuyến nào → **404**
  `Không tìm thấy tuyến đường` (tham chiếu cứng — cùng câu trả lời của `GET /trips`).
- `to` sớm hơn `from` → **400** `errors.to`. `routeId` thiếu hoặc sai định dạng GUID → **400**.
- Trả về **mảng trần**, không phân trang: một tuyến trong một khoảng ngày là vài chục chuyến — màn
  hình đọc hết để tự sắp xếp theo giờ/giá. Cùng lối `GET /routes/{routeId}/stops`.
- Kèm sẵn `routeCode`/`routeName`/`busType`/`capacity` thay vì chỉ `routeId`: màn hình kết quả hiển
  thị được ngay mà không phải gọi thêm `/routes/{id}` rồi `/buses/{id}` — cùng lối `TripDetail`.
- Chuyến mồ côi (tuyến/xe đã biến mất) không bao giờ lọt ra kết quả: phép đọc ghép tường minh
  `Trips` ⋈ `Routes` ⋈ `Buses`, cùng lối `GET /trips`.
- Không trả 4 trường vị trí thời gian thực — chuyện của Sprint 3, cùng lối `TripDetail`/`TripLookup`.
  Không lọc theo trạng thái tuyến/xe: bộ lọc duy nhất là chuyến `Scheduled` + khoảng thời gian —
  chuyến bị hủy tự rời khỏi kết quả (hủy chuyến là `DELETE /routes/{routeId}/trips/{id}`).

> **Vì sao `price` là giá phổ thông chứ không phải cả bảng giá theo đối tượng ưu đãi?** Màn hình kết
> quả so sánh các chuyến theo một con số niêm yết; chọn đối tượng ưu đãi (sinh viên, người cao
> tuổi…) là bước của màn hình đặt vé (Sprint 3), khi đó mới cần `GET /routes/{routeId}/fares` đầy
> đủ. Trả kèm cả bảng giá ở đây thì mỗi dòng kết quả nặng thêm một mảng trong khi cột hiển thị vẫn
> chỉ có một chỗ. Tuyến chưa cấu hình giá → `price: null` (màn hình hiện "Chưa có giá"), không phải
> 404 — thiếu giá là trạng thái dữ liệu bình thường, không phải lỗi gọi.

> **Vì sao `seatsRemaining` hôm nay luôn bằng `capacity`?** Vì bảng vé / giữ chỗ chưa được migrate —
> Sprint 3 mới có `Tickets`/`SeatHolds` để trừ ghế đã bán hoặc đang giữ. Hợp đồng chốt hình dạng
> trường ngay từ bây giờ để màn hình kết quả (Băng, story 1) ghép được mà không phải sửa hợp đồng
> lần nữa (⛔5); khi bảng vé vào, chỉ phần tính toán trong `TripSearchService` đổi, hình dạng
> response giữ nguyên.

> **Ranh giới với task *"API tìm kiếm chuyến theo điểm đi, điểm đến, ngày giờ"* (Trần Trung Hiếu,
> story 1):** endpoint này **không** nhận điểm đi/điểm đến — nó nhận thẳng `routeId` đã chọn. Hai
> task chia nhau hai nửa của một luồng: task của Hiếu trả lời *"điểm đi A, điểm đến B, ngày D →
> những tuyến nào?"* — đã có: `GET /routes/search` ở mục "Tra cứu tuyến" phía trên — endpoint này
> trả lời *"tuyến X trong khoảng ngày → những chuyến nào, giá bao nhiêu, còn mấy ghế?"*. Màn hình
> kết quả (Băng, đã xong) ghép hai bước lại. Làm gộp cả hai vào một endpoint thì task của Hiếu
> không còn gì để làm — và ngược lại; mỗi bên giữ đúng phần mình.

> **Vì sao đứng ở controller riêng (`TripSearchController`)?** Cùng lối `TripLookupController` ở
> trên: mỗi bề mặt một controller, hai task thuộc hai người. Route template khác nhau
> (`api/trips/search` so với `api/trips` và `api/trips/{id:guid}`) nên không tranh chấp — đoạn
> literal `search` không thể khớp `{id:guid}`.

> **Kết quả được đệm trong bộ nhớ — cũ tối đa 60 giây** (task *"Cache kết quả tìm kiếm tuyến phổ
> biến để giảm tải DB"*, Nguyễn Duy Kiên). Mỗi lượt gọi endpoint này tốn ba truy vấn CSDL (tuyến,
> giá phổ thông, chuyến ⋈ xe) mà kết quả gần như không đổi trong vài chục giây, nên kết quả được
> giữ lại trong bộ nhớ của máy chủ: **chỉ những truy vấn được hỏi lặp lại mới vào đệm** (tuyến phổ
> biến), mỗi kết quả sống tối đa **60 giây** rồi tự hết hạn, tối đa 200 khoá. Hệ quả với người gọi:
> hai lượt gọi cùng `routeId` + cùng `from`/`to` trong vòng 60 giây **có thể nhận cùng một kết quả**,
> kể cả khi ở giữa hai lượt có chuyến vừa được thêm/xoá hoặc giá vừa đổi. **Tham số và hình dạng
> response không đổi** — chỉ độ tươi của dữ liệu là có giới hạn. Chỉ kết quả thành công mới vào
> đệm: `404` tuyến không tồn tại và `400` khoảng thời gian sai luôn được trả lời tươi từ CSDL.
>
> Ghi chú cho Sprint 3: khi bảng vé/giữ chỗ vào và `seatsRemaining` bắt đầu đổi theo từng lượt đặt,
> 60 giây là quá cũ cho một con số mà hành khách quyết định theo nó — lúc đó phải giảm TTL hoặc huỷ
> đệm cho tới khi có cơ chế huỷ theo sự kiện.

#### `GET /trips/{id}`

```json
// TripDetail — ví dụ
{
  "id": "a1c7f0e2-0000-0000-0000-000000000000",
  "routeId": "3f2a1b0c-0000-0000-0000-000000000000",
  "routeCode": "01",
  "routeName": "Bến xe Mỹ Đình — Bến xe Gia Lâm",
  "origin": "Bến xe Mỹ Đình",
  "destination": "Bến xe Gia Lâm",
  "departureTime": "2026-10-01T01:00:00Z",
  "arrivalTime": "2026-10-01T01:45:00Z",
  "status": "Scheduled",
  "busId": "7b3d9a44-0000-0000-0000-000000000000",
  "licensePlate": "29B-123.45",
  "busType": "Hyundai County 29 chỗ",
  "capacity": 29,
  "busStatus": "Active",
  "stops": [
    {
      "stopId": "9d2f5c33-0000-0000-0000-000000000000",
      "stopName": "Bến xe Mỹ Đình",
      "stopAddress": "Số 20 Phạm Hùng, Nam Từ Liêm, Hà Nội",
      "latitude": 21.0287,
      "longitude": 105.7788,
      "stopOrder": 1,
      "distanceKm": 0
    },
    {
      "stopId": "b7e4c9a1-0000-0000-0000-000000000000",
      "stopName": "Cầu Giấy",
      "stopAddress": "Số 1 Cầu Giấy, Hà Nội",
      "latitude": 21.0307,
      "longitude": 105.8034,
      "stopOrder": 2,
      "distanceKm": 4.2
    }
  ]
}
```

`stops[]` — mỗi phần tử là một trạm của **tuyến**, đúng hình dạng `RouteStop` của mục
"Trạm trên tuyến" **trừ `id` và `routeId`** (hai trường đó là của cả danh sách, đã có ở cấp ngoài
cùng — lặp lại ở từng dòng chỉ làm response phình ra mà không thêm thông tin):

| Trường | Kiểu | Mô tả |
|---|---|---|
| `stopId` | `string` (GUID) | Khoá của trạm |
| `stopName` | `string` | Tên trạm |
| `stopAddress` | `string` | Địa chỉ trạm |
| `latitude` | `number` | Vĩ độ trạm |
| `longitude` | `number` | Kinh độ trạm |
| `stopOrder` | `number` | Thứ tự trạm trên tuyến, tính từ **1** và liên tục |
| `distanceKm` | `number` | Khoảng cách từ trạm liền trước tới trạm này (km). Trạm đầu tiên = 0 |

- Thứ tự `stops`: `stopOrder` tăng dần, hai dòng cùng `stopOrder` (xem ghi chú ở mục "Trạm trên
  tuyến") xếp tiếp theo `stopId` để thứ tự hiển thị luôn ổn định.
- Tuyến của chuyến chưa gán trạm nào → `stops` là mảng rỗng, **không** phải 404.
- Chuyến không tồn tại → **404**.
- Chuyến có thật nhưng tuyến hoặc xe của nó đã bị xoá khỏi CSDL → **404** (dữ liệu mồ côi, không
  phải chuyến để hiển thị). Đường đi thường không tới đây: cả hai khoá ngoại đều `Restrict` nên
  không xoá cứng được tuyến/xe còn chuyến tham chiếu.

> **Vì sao `stops[]` không trả `id` của dòng `RouteStop`?** Vì chuyến **không sở hữu** dòng đó —
> sửa hay gỡ trạm là thao tác trên tuyến (`PUT`/`DELETE /routes/{routeId}/stops/{id}`), không phải
> trên chuyến. Trả kèm `id` ở đây là mời người gọi gửi nó vào một endpoint khác mà ở đó nó chỉ có
> nghĩa khi đi kèm đúng `routeId`; bỏ hẳn đi thì không ai ghép nhầm. Cần `id` của dòng thì gọi
> `GET /routes/{routeId}/stops` — `routeId` đã có sẵn trong response này.

> **Vì sao trả kèm `routeCode`/`routeName`/`origin`/`destination` và `licensePlate`/`busType`/
> `capacity` mà không chỉ `routeId`/`busId`?** Cùng lối `RouteStop` trả kèm `stopName`/`stopAddress`:
> màn hình chi tiết chuyến hiển thị được ngay mà không phải gọi thêm `/routes/{id}` rồi
> `/buses/{id}` để tự ghép. Đây là dữ liệu chỉ để đọc — muốn sửa tuyến thì gọi `PUT /routes/{id}`,
> muốn sửa xe thì gọi API CRUD xe ở mục "Xe buýt — `/buses`" bên dưới (Hiếu, story 14).

> **Vì sao có `busStatus` khi story chỉ hỏi "loại xe, sức chứa"?** Vì `Maintenance`/`Inactive` là
> cách duy nhất để một xe rời khỏi đội (quy ước A4 cấm cột `IsDeleted`): chuyến vẫn `Scheduled` với
> xe đã vào bảo dưỡng, và người điều hành nhìn chi tiết chuyến cần thấy đúng điều đó. Cột đã nằm
> sẵn trên dòng `Bus` được đọc cho `busType`/`capacity`, nên không tốn thêm truy vấn nào.

> **Vì sao KHÔNG trả vị trí hiện tại của xe (`currentLat`/`currentLng`/`currentStopId`)?** Vì story
> 13 là *lập lịch trình*, còn vị trí là chuyện của nhóm story theo dõi thời gian thực (Sprint 3).
> Hình dạng response cho vị trí — có kèm "cách trạm kế tiếp bao xa", có đẩy qua SignalR hay không —
> chưa story nào chốt; đoán trước ở đây thì lúc story đó làm sẽ phải sửa lại hợp đồng này lần nữa,
> mà sửa hợp đồng thì phải báo cả nhóm (⛔5).

> **Vì sao KHÔNG trả `seatsRemaining` (số ghế còn trống)?** Màn hình điều hành đọc thời gian biểu,
> không bán vé — giá và ghế trống là chuyện của màn hình tra cứu dành cho hành khách, đã có ở
> `GET /trips/search` (story 1, Phùng Duy Hoàng). Màn hình cần giá/ghế thì gọi endpoint đó.

#### `GET /trips/{id}/seats` — sơ đồ ghế theo chuyến + trạng thái từng ghế

> ✅ Backend đã có (`TripSeatMapController` — Trần Trung Hiếu, story 2, task *"API lấy sơ đồ ghế
> theo chuyến + trạng thái từng ghế"*).
>
> **Endpoint CÔNG KHAI thứ hai của mục `/trips`** — cùng lối `GET /trips/search`: hành khách xem sơ
> đồ để chọn ghế trống **trước khi đăng nhập**, đăng nhập là bước của API giữ ghế (US 3). Đây là
> nửa "đọc" của luồng chọn ghế; nửa "ghi" (giữ ghế / nhả ghế) là các task khác của Sprint 3.

```json
// TripSeatMapResponse — ví dụ GET /trips/a1c7f0e2-0000-0000-0000-000000000000/seats
{
  "tripId": "a1c7f0e2-0000-0000-0000-000000000000",
  "busType": "Xe buýt 45 chỗ",
  "floors": 1,
  "pricePerSeat": 7000,
  "vipSurcharge": null,
  "seats": [
    {
      "id": "5f9c2d31-0000-0000-0000-000000000000",
      "seatNumber": "A1",
      "floor": 1,
      "rowIndex": 1,
      "columnIndex": 1,
      "seatType": "Vip",
      "status": "Held",
      "price": 7000
    }
  ]
}
```

| Trường | Kiểu | Mô tả |
|---|---|---|
| `tripId` | `string` (GUID) | Khoá chuyến — lặp lại để màn hình không phải tự nhớ đang gọi cho chuyến nào |
| `busType` | `string` | Loại xe của chuyến — "Xe buýt 45 chỗ" |
| `floors` | `number` | Số tầng của sơ đồ (`SeatLayouts.NumberOfFloors`). Loại xe chưa có sơ đồ → `0` kèm `seats: []` |
| `pricePerSeat` | `number` \| `null` | Giá vé phổ thông của tuyến (đồng) — cùng nguồn và cùng nghĩa với `price` của `GET /trips/search`. Tuyến chưa cấu hình giá → `null` |
| `vipSurcharge` | `number` \| `null` | Phụ trội ghế VIP (đồng). **Hôm nay luôn `null`**: chưa có nguồn dữ liệu nào lưu phụ trội VIP — phần "giá theo ghế + tổng tiền tạm tính" là task của Dương Thị Hạnh, khi đó mới chốt con số này |
| `seats[]` | mảng | Toàn bộ ghế của xe chạy chuyến — kể cả ghế đã bán / đang giữ (xem ghi chú "Vì sao" bên dưới) |

Mỗi phần tử `seats[]`:

| Trường | Kiểu | Mô tả |
|---|---|---|
| `id` | `string` (GUID) | Khoá ghế (`Seats.Id`) — màn hình gửi lại cho API giữ ghế (US 3) |
| `seatNumber` | `string` | Số ghế hiển thị — "A1"; xe hai tầng thêm tiền tố tầng "T2-A1" (quy cách mã ghế: docs/26-csdl-so-do-ghe.md §2) |
| `floor` | `number` | Tầng của ghế, bắt đầu từ 1 |
| `rowIndex` | `number` | Hàng trong tầng, 1 là hàng đầu |
| `columnIndex` | `number` | Cột trong hàng, 1 là cột trái cùng |
| `seatType` | `string` | `"Standard"` hoặc `"Vip"` |
| `status` | `string` | `"Available"` / `"Held"` / `"Paid"` — suy ra theo CHUYẾN, xem quy tắc bên dưới |
| `price` | `number` \| `null` | Giá ghế này = `pricePerSeat` + `vipSurcharge` khi ghế là VIP và phụ trội đã có. `pricePerSeat` là `null` → `price` cũng `null` |

**Trạng thái ghế — suy ra ở tầng service, không phải cột của `Seats`:**

- `Held` — có dòng `SeatHolds` (`TripId` = chuyến này, `SeatId` = ghế này) với `Status = Holding`.
  Chỉ trạng thái này mới chặn ghế; hold đã `Expired` / `Released` / `Confirmed` không chặn.
- `Paid` — ghế đã bán cho chuyến (bảng `Tickets`). ⚠️ **Bảng `Tickets` chưa được migrate** (task của
  Vàng Thị Dăm, US 4) nên hôm nay chưa có ghế nào trả về `Paid` — hợp đồng chốt hình dạng trường
  ngay từ bây giờ để màn hình chọn ghế ghép được mà không phải sửa hợp đồng lần nữa (⛔5); khi bảng
  vé vào, chỉ phần truy vấn trong `TripSeatMapService` đổi, hình dạng response giữ nguyên.
- `Available` — mọi trường hợp còn lại.

- Thứ tự `seats`: `floor` tăng dần → `rowIndex` → `columnIndex`; hai dòng trùng cả ba (dữ liệu lỗi
  ngoài khuôn lưới) xếp tiếp theo `id` để thứ tự hiển thị luôn ổn định.
- Không lọc theo trạng thái chuyến: endpoint trả sơ đồ của chuyến có thật, việc chỉ dẫn khách tới
  chuyến `Scheduled` là của `GET /trips/search` — cùng lối `GET /trips/{id}`.
- Loại xe chưa có sơ đồ (`SeatLayouts.BusType` chưa khớp) → **200** với `floors: 0`, `seats: []` —
  trạng thái dữ liệu hợp lệ (xe chưa được sinh ghế), không phải lỗi. Xem docs/26-csdl-so-do-ghe.md §1.
- Chuyến không tồn tại, hoặc chuyến có thật nhưng xe của nó đã biến mất khỏi CSDL → **404**
  `Không tìm thấy chuyến xe` — cùng lối `GET /trips/{id}`.
- `id` sai định dạng GUID → **404** (ràng buộc `{id:guid}`), cùng lối các endpoint khác của mục này.

> **Vì sao trả cả ghế đã bán / đang giữ mà không chỉ ghế còn trống?** Màn hình chọn ghế vẽ nguyên
> sơ đồ xe — ghế đã bán phải hiện màu khác chứ không phải biến mất khỏi lưới. Cắt bớt ở server thì
> màn hình phải tự "lấp" lại các ô thiếu; trả đủ cả dàn thì việc vẽ chỉ là đọc thẳng mảng về.
>
> **Vì sao trạng thái suy ra theo chuyến mà không lưu một cột trên `Seats`?** Vì trạng thái là của
> CẶP (chuyến, ghế): cùng một ghế, chuyến này đã bán còn chuyến khác vẫn trống. Cột trên `Seats`
> không biểu diễn được điều đó — `SeatHolds` và `Tickets` mới là nơi lưu, endpoint này chỉ cộng lại.
>
> **Vì sao đứng ở controller riêng (`TripSeatMapController`) chứ không gộp vào `GET /trips/{id}`?**
> Cùng lối `TripSearchController`: mỗi bề mặt một controller, hai task thuộc hai người (chi tiết
> chuyến là Vàng Thị Dăm, sơ đồ ghế là Trần Trung Hiếu). Route template khác nhau
> (`api/trips/{id:guid}/seats` so với `api/trips/{id:guid}`) nên không tranh chấp.

#### `PATCH /trips/{id}/assignment` — đổi xe / đổi tài xế khi có sự cố

> ✅ Backend đã có (`TripAssignmentController` — Phùng Duy Hoàng, story 14, task *"API đổi xe/đổi
> tài xế khi có sự cố + ghi log thay đổi"*). Đây là luồng **điều hành khi có sự cố** (xe hỏng giữa
> đường, tài xế ốm đột xuất): thay xe và/hoặc tài xế của một chuyến đã nằm trong lịch, **không
> đụng tới giờ chạy**.
>
> **Endpoint yêu cầu vai trò `Admin` hoặc `Manager`** (cùng nhóm với lịch trình — story 14 là
> nghiệp vụ của quản lý). Người đã đăng nhập nhưng không đủ quyền nhận **403** kèm body
> `{ "message": "Bạn không có quyền truy cập tính năng này." }`.

Body — **ít nhất một trong hai trường**, cả hai đều không bắt buộc (bỏ trống = giữ nguyên):

```json
// ReassignTrip — ví dụ đổi cả xe lẫn tài xế
{
  "busId": "7b3d9a44-0000-0000-0000-000000000000",
  "driverId": "9a2b1c3d-0000-0000-0000-000000000000"
}
```

| Trường | Bắt buộc | Ràng buộc |
|---|---|---|
| `busId` | — | Xe **mới** của chuyến. Xe phải đang `Active`; bỏ trống = giữ nguyên xe hiện tại |
| `driverId` | — | Tài xế **mới** của chuyến — tài khoản mang vai trò `Driver`. Bỏ trống = giữ nguyên tài xế hiện tại |

- Cả hai trường bỏ trống → **400** kèm lỗi ở cả `errors.busId` lẫn `errors.driverId`. API này
  **không có** cách bỏ phân công (gán tài xế về null): tài xế hiện tại không dùng được nữa thì
  gán người thay thế.
- Truyền đúng giá trị hiện tại = **không thay đổi gì**: trả **200** nguyên trạng, `updatedAt` giữ
  nguyên (idempotent).

```json
// TripAssignment — ví dụ chuyến vừa được đổi xe, chưa phân công tài xế, không có xung đột
{
  "id": "a1c7f0e2-0000-0000-0000-000000000000",
  "routeId": "3f2a1b0c-0000-0000-0000-000000000000",
  "busId": "7b3d9a44-0000-0000-0000-000000000000",
  "busLicensePlate": "29B-123.45",
  "driverId": null,
  "driverName": null,
  "departureTime": "2026-10-01T01:00:00Z",
  "arrivalTime": "2026-10-01T01:45:00Z",
  "status": "Running",
  "updatedAt": "2026-10-01T02:10:00Z",
  "conflicts": {
    "busConflicts": [],
    "driverConflicts": []
  }
}
```

`conflicts` giữ nguyên hình dạng kết quả kiểm tra trùng lịch điều xe (`busConflicts` /
`driverConflicts`, mỗi phần tử là một chuyến đang hoạt động trùng khung giờ — xem service kiểm tra
trùng lịch của story 14). Chỉ soi tài nguyên **được đổi** trong request này.

- Chuyến không tồn tại → **404** `Không tìm thấy chuyến xe`.
- Chuyến đã `Cancelled` → **409** `Chuyến đã hủy, không thể đổi xe hoặc tài xế`; đã `Completed` →
  **409** `Chuyến đã hoàn thành, không thể đổi xe hoặc tài xế`. Chỉ đổi được chuyến `Scheduled`
  hoặc `Running` — hai trạng thái còn chiếm chỗ trên thời gian biểu.
- Xe không tồn tại → **404** · xe không `Active` → **409** (cùng câu với lúc tạo lịch trình) ·
  tài xế không tồn tại hoặc tài khoản không mang vai trò `Driver` → **404**
  `Không tìm thấy tài xế` · tài khoản tài xế đang bị khóa → **409**.
- **Trùng lịch điều xe KHÔNG chặn** — khác `POST /routes/{routeId}/trips` (409): thay đổi vẫn
  được áp dụng, xung đột trả về trong `conflicts` để màn hình cảnh báo (hai ghi chú "Vì sao" ngay
  dưới).
- Thay đổi thành công **tự động vào nhật ký kiểm toán** (US 23) — xem ghi chú "Ghi log thay đổi"
  ngay dưới.

> **Vì sao trùng lịch chỉ cảnh báo mà không chặn?** Đây là luồng sự cố: chuyến đang chạy cần thay
> xe/tài xế ngay, người điều hành có quyền cố ý chấp nhận trùng (ví dụ đổi xe giữa chừng khi chuyến
> cũ sắp về bến). Cùng triết lý "cảnh báo trực quan, không chặn lưu" của modal phân công điều xe —
> nhưng phép kiểm tra ở đây mạnh hơn cảnh báo phía client: nó soi **mọi tuyến**, không chỉ các
> chuyến trong ngày của một tuyến.

> **Vì sao chỉ soi tài nguyên được đổi?** Đổi mỗi tài xế thì vế xe hiện tại không đem ra soi: một
> chuyến vướng lịch xe từ trước (dữ liệu cũ — `PUT /routes/{routeId}/trips/{id}` cố ý không kiểm
> trùng) mà bị chặn luôn việc đổi tài xế thì vô lý. Phép kiểm tra trả lời đúng một câu: *"thay đổi
> này có tạo xung đột MỚI không?"*.

> **Ghi log thay đổi (US 23).** Mọi request thành công đi qua middleware nhật ký tự động: một dòng
> hành động `Update`, đối tượng `Trips:{id}`, kèm người thực hiện + IP + thời điểm. Endpoint không
> tự gọi ghi log — middleware phủ sẵn mọi thao tác thay đổi dữ liệu (đúng thiết kế: "không ai phải
> nhớ thêm một dòng gọi log vào controller mới"). Request lỗi (400/404/409) không sinh dòng nào vì
> dữ liệu chưa hề đổi.
> ⚠️ Bảng `AuditLogs` **không có cột chi tiết cũ/mới** (A9): nhật ký trả lời được "ai đổi chuyến
> nào lúc nào", không trả lời được "đổi từ xe nào sang xe nào". Muốn lưu chi tiết phải đổi bảng —
> việc của Dăm (quy ước 2.3), không tự thêm cột.

> **Cho task "API gán xe + tài xế vào từng chuyến" (Kiên):** bề mặt phân công điều xe nằm ở đây.
> Khi làm API gán hàng loạt / gán cho chuyến chưa phân công, mở rộng trên cùng khuôn
> `TripAssignment` + `conflicts` thay vì dựng endpoint thứ hai có hình dạng khác, và dùng chung
> service kiểm tra trùng lịch của story 14 ("cùng một hàm kiểm tra, chỉ khác điểm gọi").

#### `PUT /trips/{id}/location` — tài xế báo vị trí hiện tại của xe

> ✅ Backend đã có (`TripLocationController` — Trần Trung Hiếu, story 10, task *"API cập nhật vị
> trí chuyến (CurrentLat/CurrentLng/CurrentStopId)"*, Sprint 4). Một lần gọi là MỘT ảnh chụp vị
> trí của chuyến: toạ độ hiện tại + trạm vừa đi qua. Dữ liệu ghi ở đây là đầu vào của service
> *"bắn thông báo khi xe cách trạm dưới 500m"* (Kiên, cùng story).

**Endpoint yêu cầu vai trò `Driver`** — và phải đúng tài xế được phân công của chuyến (kiểm theo
`DriverId` ngay trong truy vấn). Người đã đăng nhập nhưng không đủ quyền nhận **403** kèm body
`{ "message": "Bạn không có quyền truy cập tính năng này." }`.

Body — `currentLat` và `currentLng` bắt buộc, `currentStopId` bỏ trống được:

```json
{
  "currentLat": 21.0285,
  "currentLng": 105.8542,
  "currentStopId": "c5d2e1f0-0000-0000-0000-000000000000"
}
```

| Trường | Bắt buộc | Ràng buộc |
|---|---|---|
| `currentLat` | ✅ | Vĩ độ — số thực, từ `-90` đến `90` |
| `currentLng` | ✅ | Kinh độ — số thực, từ `-180` đến `180` |
| `currentStopId` | — | Trạm xe vừa đi qua gần nhất — phải là trạm **thuộc tuyến của chuyến**. Bỏ trống = chỉ cập nhật toạ độ, giữ nguyên trạm hiện tại |

- `positionUpdatedAt` **không nhận từ client** — hệ thống tự ghi giờ server nhận request (UTC),
  không tin giờ thiết bị gửi lên; `updatedAt` của chuyến cũng được làm mới.
- Gọi lại y hệt (idempotent): chỉ `positionUpdatedAt` thay đổi, dữ liệu khác giữ nguyên.

```json
// TripLocation — ví dụ chuyến vừa báo vị trí kèm trạm
{
  "tripId": "a1c7f0e2-0000-0000-0000-000000000000",
  "currentStopId": "c5d2e1f0-0000-0000-0000-000000000000",
  "currentLat": 21.0285,
  "currentLng": 105.8542,
  "positionUpdatedAt": "2026-10-14T03:10:00Z"
}
```

- Chuyến không tồn tại **hoặc** không do tài xế này điều khiển → **404** `Không tìm thấy chuyến
  xe` — một câu cho cả hai để không lộ chuyến nào đang có tài xế khác chạy (cùng lối "vé của
  người khác trả 404" của `GET /tickets/{id}`).
- Chuyến đã `Completed` → **409** `Chuyến đã hoàn thành, không thể cập nhật vị trí`; đã
  `Cancelled` → **409** `Chuyến đã hủy, không thể cập nhật vị trí`. Chuyến `Scheduled` **vẫn
  nhận** vị trí: chưa có endpoint nào chuyển trạng thái sang `Running`, chặn `Scheduled` thì
  thiết bị không báo được vị trí trước giờ chạy.
- Trạm không tồn tại → **404** `Không tìm thấy trạm dừng` · trạm tồn tại nhưng không nằm trong
  `RouteStops` của tuyến → **400** kèm lỗi ở `errors.currentStopId` `Trạm không thuộc tuyến của
  chuyến`.
- Toạ độ thiếu hoặc sai khoảng → **400** kèm lỗi theo trường (`errors.currentLat` /
  `errors.currentLng`).

> **Vì sao trạm phải thuộc tuyến?** `currentStopId` là mỏ neo cho phép "cách trạm kế tiếp bao xa"
> của service thông báo (Kiên, cùng story): trạm lạ không có `StopOrder` trên tuyến sẽ làm hỏng
> phép tính khoảng cách. Chặn từ nguồn (API ghi) rẻ hơn là để service đọc phải tự lọc.

> **Vì sao chưa đẩy SignalR khi vị trí đổi?** Đây là endpoint GHI — nửa "đọc" (hành khách xem xe
> đang ở đâu theo thời gian thực) là chuyện của US 9 ở Sprint sau. Lúc đó nếu cần đẩy, sẽ bọc
> service này bằng lớp Notifying* (cùng lối NotifyingSeatHold*) chứ không sửa file này.

> **Ghi log thay đổi (US 23).** Mọi request thành công đi qua middleware nhật ký tự động: một dòng
> hành động `Update`, đối tượng `Trips:{id}`, kèm người thực hiện + IP + thời điểm. Request lỗi
> (400/404/409) không sinh dòng nào vì dữ liệu chưa hề đổi.

## Giữ chỗ — `/seat-holds`

> Hai bảng `SeatHolds` + `SeatHoldLogs` đã migrate (Vàng Thị Dăm — docs/26-csdl-so-do-ghe.md).
> Bề mặt này của US 3 *"tự động giữ chỗ trong 10 phút khi khách đang thao tác thanh toán"* gồm
> bốn endpoint, mỗi endpoint một task của bảng phân công Sprint 3 — mục này chốt endpoint ĐÃ có,
> cả bốn endpoint — mục này chốt đủ bốn:

| Method | Endpoint | Task | Trạng thái |
|---|---|---|---|
| GET | `/seat-holds/{sessionCode}` | *"API kiểm tra trạng thái giữ chỗ theo mã phiên"* — Trần Trung Hiếu | ✅ dưới đây |
| POST | `/seat-holds` | *"API giữ ghế tạm thời (khóa ghế theo phiên)"* — Nguyễn Duy Kiên | ✅ dưới đây |
| POST | `/seat-holds/{sessionCode}/release` | *"API nhả ghế khi hết hạn hoặc khách huỷ thao tác"* — Phùng Duy Hoàng | ✅ dưới đây |
| POST | `/seat-holds/{sessionCode}/extend` | *"API gia hạn thời gian giữ chỗ (tối đa 1 lần)"* — Trần Trung Hiếu | ✅ dưới đây |

> Nhả ghế do HẾT HẠN không phải endpoint: job nền `SeatHoldExpiryBackgroundService` (docs/26 §4)
> tự lật `Holding` → `Expired` khi quá `ExpiresAt`. Phần khách chủ động huỷ mới là endpoint
> `release` ở bảng trên.

### `SeatHoldSession` — hình dạng trả về chung của cả bề mặt

Một phiên giữ NHIỀU ghế = nhiều dòng `SeatHolds` cùng `SessionCode` (cột KHÔNG unique —
docs/26 §1), nên đối tượng trả về gom các ghế lại thành một PHIÊN chứ không phải một dòng:
mọi thao tác (tra trạng thái, gia hạn, đếm ngược, nhả cả phiên) đều hỏi theo `SessionCode`.
Cả bốn endpoint của bề mặt đều trả về đúng hình dạng này.

```json
// SeatHoldSessionResponse — ví dụ GET /seat-holds/PHIEN-8f3a2c1d
{
  "sessionCode": "PHIEN-8f3a2c1d",
  "tripId": "a1c7f0e2-0000-0000-0000-000000000000",
  "seatNumbers": ["A1", "A2"],
  "status": "Holding",
  "expiresAt": "2026-10-01T01:10:00Z",
  "canExtend": true
}
```

| Trường | Kiểu | Mô tả |
|---|---|---|
| `sessionCode` | `string` | Mã phiên do API giữ ghế sinh ra — khoá của mọi thao tác theo phiên |
| `tripId` | `string` (GUID) | Chuyến đang giữ ghế |
| `seatNumbers` | `string[]` | Số ghế đang giữ ("A1", "T2-A1"), xếp theo toạ độ sơ đồ — chỉ để hiển thị cho hành khách |
| `status` | `string` | `"Holding"` / `"Confirmed"` / `"Expired"` / `"Released"` — giá trị cột `SeatHolds.Status` |
| `expiresAt` | `string` | Hạn giữ chỗ (ISO 8601, UTC). Mọi dòng cùng phiên cùng hạn — docs/26 §1 |
| `canExtend` | `boolean` | Còn lượt gia hạn không — màn hình dùng để bật/tắt nút "Gia hạn". Xem quy tắc ở mục GET dưới |

### `POST /seat-holds` — giữ ghế tạm thời (khóa ghế theo phiên)

> ✅ Backend đã có (`SeatHoldCreateController` — Nguyễn Duy Kiên, story 3, task *"API giữ ghế tạm
> thời (khóa ghế theo phiên)"*). Đây là endpoint **SINH ra mã phiên**: màn hình chọn ghế gọi nó một
> lần với cả nhóm ghế khách vừa chọn, nhận về `sessionCode` rồi chạy đếm ngược 10 phút trong lúc
> khách thanh toán. Ba endpoint kia của bề mặt đều xoay quanh mã phiên do endpoint này sinh ra —
> chưa gọi được endpoint này thì chưa có phiên nào để tra, gia hạn hay nhả.

- **Yêu cầu đăng nhập** (`[Authorize]` trần, không policy vai trò): hành khách tự giữ ghế cho mình.
  Không có token → **401**. Người giữ là người đang đăng nhập — `userId` KHÔNG có trong body (lấy
  từ claim `NameIdentifier`), nên không ai giữ ghế hộ người khác được.
- **Body**:
```json
// POST /seat-holds
{
  "tripId": "a1c7f0e2-0000-0000-0000-000000000000",
  "seatIds": ["6b1f0c94-0000-0000-0000-000000000000", "9c02a7e1-0000-0000-0000-000000000000"]
}
```
- `tripId` thiếu hẳn trong body → **400** `errors.tripId` (model binding; để `Guid?` + `[Required]`
  chứ không phải `Guid` — thiếu trường thì `Guid` lặng lẽ nhận `Guid.Empty` và `[Required]` không
  bắt được, cùng lối `AssignDriverToTripsRequest.DriverId`). Có giá trị nhưng không trỏ tới chuyến
  nào — kể cả `Guid.Empty` — → **404** `Không tìm thấy chuyến xe`.
- Chuyến đã huỷ → **409** `Chuyến đã hủy, không thể giữ ghế`; chuyến đã chạy xong → **409**
  `Chuyến đã hoàn thành, không thể giữ ghế` — cùng câu chữ và cùng lối chặn của
  `ITripAssignmentService` / `IRouteTripsService` khi đổi xe, đổi tài xế.
- `seatIds` rỗng, thiếu hẳn, chứa `Guid.Empty` hoặc trùng nhau → **400** `errors.seatIds`.
- Ghế không tồn tại, hoặc **không thuộc xe của chuyến** → **400** `errors.seatIds`: dữ liệu gửi lên
  không dựng được một phiên hợp lệ, và không có tham số nào để dò ghế của xe khác. (Ràng buộc "thuộc
  xe của chuyến" cũng là trần số ghế: không thể giữ nhiều ghế hơn sức chứa của xe.)
- **Ghế đang có người giữ** — `SeatHolds` còn dòng `Status = 'Holding'` cho cùng
  `(tripId, seatId)`, bất kể của ai → **409** kèm số ghế vướng, ví dụ
  `Ghế A1, A2 đang được giữ cho chuyến này.` Đây là ca thường gặp nhất của màn hình chọn ghế: hai
  khách cùng bấm giữ một ghế.
- Thành công → **201 Created** kèm đúng hình dạng `SeatHoldSession` ở trên, với `status = "Holding"`,
  `expiresAt` = lúc tạo + **10 phút**, `canExtend = true` (phiên mới chưa dùng lượt gia hạn nào).
- `seatNumbers` xếp theo toạ độ sơ đồ (`Floor` → `RowIndex` → `ColumnIndex`) — cùng thứ tự vẽ của
  `GET /trips/{id}/seats`, nên màn hình hiển thị nhóm ghế vừa giữ đúng chỗ trên sơ đồ.

```json
// 201 — ví dụ POST /seat-holds
{
  "sessionCode": "PHIEN-8f3a2c1d",
  "tripId": "a1c7f0e2-0000-0000-0000-000000000000",
  "seatNumbers": ["A1", "A2"],
  "status": "Holding",
  "expiresAt": "2026-10-01T01:10:00Z",
  "canExtend": true
}
```

> **Vì sao mã phiên sinh ở tầng service chứ không nhận từ client?** Mã phiên là khoá tra cứu của cả
> phiên giữ chỗ (GET, gia hạn, nhả đều hỏi theo nó). Để client tự đặt thì hai khách có thể gửi trùng
> mã và mọi thao tác theo phiên sẽ trộn hai phiên vào nhau. Cột `SessionCode` **KHÔNG unique**
> (docs/26 §1) nên tầng CSDL không đỡ được — chốt phải nằm ở chỗ duy nhất sinh ra mã.

> **Chống hai khách giữ cùng một ghế — chốt ở đâu?** Ở partial unique index
> `IX_SeatHolds_TripId_SeatId` — `UNIQUE ("TripId", "SeatId") WHERE "Status" = 'Holding'` — có thật
> trong migration `20261006122313_Sprint3_Seats_SeatLayouts_SeatHolds` (khối `CreateIndex` với
> `unique: true` và `filter: "\"Status\" = 'Holding'"`), nên chốt này chạy ở mọi môi trường PostgreSQL
> chứ không chỉ nằm trong mô hình EF (AppDbContext.Seat.cs). Service vẫn kiểm tra trước để trả 409 kèm
> số ghế vướng (ca thường gặp, khách hiểu ngay), nhưng phép kiểm ở tầng service **không đủ** cho hai
> request đồng thời: cả hai cùng đọc thấy ghế trống rồi cùng ghi. Request thua đâm vào unique index lúc
> `SaveChanges`, service bắt `DbUpdateException` và trả **409**
> `Một hoặc nhiều ghế vừa được người khác giữ, vui lòng chọn ghế khác.` — câu chung, không kèm số ghế
> vì lúc đó service không còn biết ghế nào vừa bị giành; muốn biết thì phải hỏi lại CSDL, mà khách chỉ
> cần một việc: chọn ghế khác. Cả phiên ghi trong **một** `SaveChangesAsync` nên hỏng là hỏng cả phiên
> — không để lại một phiên giữ được nửa số ghế mà khách tưởng đã giữ đủ.

> ⚠️ Chốt này sống ở tầng PostgreSQL nên **bộ test InMemory không phủ được nó**: provider InMemory bỏ
> qua unique index kể cả khi mô hình có khai — hai dòng `Holding` trùng `(TripId, SeatId)` cùng chèn
> lọt. Một bộ test xanh trên InMemory không nói gì về chốt chống trùng ghế; muốn có bằng chứng thì
> phải chạy trên PostgreSQL thật (task kiểm thử đồng thời của Vàng).

> **Vì sao chống trùng ghế KHÔNG dùng cột row-version?** Vì xung đột ở đây là **hai lượt chèn** hai
> dòng `SeatHolds` khác nhau, không phải hai luồng cùng sửa một dòng đã có: một cột "phiên bản dòng"
> không có gì để so khi dòng còn chưa tồn tại. Index còn chặn được cả khi hai bản app ở hai tiến trình
> cùng ghi — thứ mà một phép kiểm trong bộ nhớ không bao giờ làm được. Đổi lại index không cần thêm
> cột nào, còn dựng một cột row-version là sửa `Entities/SeatHold.cs` + `AppDbContext.Seat.cs` + sinh
> migration — đều thuộc chủ CSDL (docs/01-kien-truc.md).

> ⚠️ **Khe hở khác — đúng chỗ một concurrency token mới có ích: hai luồng cùng sửa MỘT dòng
> `SeatHolds`.** Gia hạn (Trần Trung Hiếu) ghi `ExpiresAt`; nhả ghế (Phùng Duy Hoàng) và job quét
> (Vàng Thị Dăm) ghi `Status`. Cả ba đọc dòng ra rồi ghi lại mà không có chốt phiên bản dòng, nên khi
> chúng chồng lên nhau, lượt ghi sau đè mất thay đổi của lượt ghi trước: dòng có thể ra
> `Status = Expired` kèm `ExpiresAt` vừa được gia hạn, trong khi API gia hạn đã trả **200** `Holding`.
> Ca dễ gặp nhất là gia hạn trong khe ≤ 1 phút giữa lúc hết hạn và lượt quét kế tiếp — gia hạn cố ý
> KHÔNG kiểm `ExpiresAt` (cùng lý do đã ghi ở mục `release`). Unique index `(SeatHoldId, Action)` không
> đỡ được vì ba luồng ghi ba `Action` khác nhau. Bịt khe này cần một concurrency token trên `SeatHolds`
> — việc của chủ CSDL, không thuộc nhiệm vụ giữ ghế; ghi lại đây để nhóm quyết trước khi `Tickets`
> được migrate, vì lúc đó một ghế bị nhả oan là một ghế bán được cho người thứ hai.

> **Vì sao mỗi lượt giữ ghi thêm một dòng `SeatHoldLogs` (`Action = 'Held'`)?** Bảng nhật ký chỉ có
> giá trị nếu **mọi** mốc vòng đời đều được ghi (docs/26 §6) — `Held` là mốc mở đầu. Thiếu nó thì
> câu hỏi "tài khoản này đã giữ chỗ bao nhiêu lần" (task cảnh báo của Dăm, đếm thẳng trên bảng log
> theo chỉ mục `(UserId, CreatedAt)`) đếm thiếu đúng những lượt giữ thành công.

> ⚠️ Ghế đã `Confirmed` **không** chặn lượt giữ mới (partial index chỉ lọc `Status = 'Holding'`,
> đúng đặc tả docs/26 §1) và bảng `Tickets` chưa migrate — khe hở có thật của luồng đặt vé, đã ghi
> ở docs/26 §7, ngoài phạm vi task này.

> **Vì sao đứng ở controller riêng (`SeatHoldCreateController`)?** Cùng lý do hai mục dưới: bốn
> endpoint của bề mặt `api/seat-holds` thuộc bốn task của bốn người, mỗi task một controller.

### `GET /seat-holds/{sessionCode}` — kiểm tra trạng thái giữ chỗ theo mã phiên

> ✅ Backend đã có (`SeatHoldLookupController` — Trần Trung Hiếu, story 3, task *"API kiểm tra trạng
> thái giữ chỗ theo mã phiên"*). Đây là endpoint mà màn hình đếm ngược thời gian giữ chỗ gọi để biết
> phiên còn sống không, còn bao nhiêu thời gian và còn lượt gia hạn không.

- **Yêu cầu đăng nhập** (`[Authorize]` trần, không policy vai trò): hành khách tự xem phiên giữ chỗ
  của mình. Không có token → **401**.
- **Chỉ chủ phiên xem được**: điều kiện `UserId` nằm ngay trong truy vấn — phiên của người khác và
  phiên không tồn tại cùng một câu trả lời **404** `Không tìm thấy phiên giữ chỗ` (cùng lối
  "phản ánh của tôi" — `GET /feedbacks/me`), không phải 403.
- `status` là trạng thái CHUNG của mọi dòng cùng phiên (docs/26 §1: tạo / gia hạn / nhả / hết hạn
  đều đổi theo nhóm). Dữ liệu lệch — khe hở có thật vì CSDL không chặn — thì ưu tiên báo trạng thái
  "đang chặn ghế" trước: **Holding → Confirmed → Released → Expired**.
- `canExtend` = phiên còn `Holding` **và** chưa dòng nào của phiên có log `Extended`. Chốt "gia hạn
  tối đa 1 lần" của US 3 không có cột đếm trên `SeatHolds` — nó nằm ở unique index
  `(SeatHoldId, Action)` của `SeatHoldLogs` (docs/26 §1): dòng `Extended` chỉ ghi được đúng một lần
  cho mỗi lượt giữ, nên hỏi bảng log là đủ biết còn lượt hay không.
- `seatNumbers` xếp theo toạ độ sơ đồ (`Floor` → `RowIndex` → `ColumnIndex`) — cùng thứ tự vẽ của
  `GET /trips/{id}/seats`, màn hình hiển thị ghế đang giữ theo đúng chỗ trên sơ đồ.

> **Vì sao trả 404 cho phiên của người khác mà không phải 403?** Không muốn xác nhận sự tồn tại của
> một mã phiên cho người không sở hữu nó — cùng lý do `GET /feedbacks/me/{id}` và vé tháng của tôi.
> Mã phiên là bí mật nhỏ của một giao dịch đang dang dở.

> **Vì sao `canExtend` suy từ `SeatHoldLogs` mà không thêm cột đếm lên `SeatHolds`?** Thêm cột là sửa
> bảng — việc của Dăm (quy ước 2.3), và câu hỏi "đã gia hạn chưa" thì bảng log đã trả lời được nhờ
> unique index `(SeatHoldId, Action)`: log là nguồn sự thật của vòng đời, cột đếm chỉ là bản sao dễ
> lệch.

> **Vì sao đứng ở controller riêng (`SeatHoldLookupController`)?** Bề mặt `api/seat-holds` sẽ có bốn
> endpoint của bốn task thuộc bốn người (xem bảng đầu mục) — mỗi bề mặt một controller, cùng lối
> `TripSearchController` đứng chung bề mặt `api/trips`.

### `POST /seat-holds/{sessionCode}/extend` — gia hạn thời gian giữ chỗ (tối đa 1 lần)

> ✅ Backend đã có (`SeatHoldExtendController` — Trần Trung Hiếu, story 3, task *"API gia hạn thời
> gian giữ chỗ (tối đa 1 lần)"*). Đây là endpoint mà màn hình gọi khi khách bấm nút "Gia hạn" trong
> modal đếm ngược — kéo dài hạn `ExpiresAt` của cả phiên thêm **10 phút**.

- **Yêu cầu đăng nhập** (`[Authorize]` trần, không policy vai trò) — hành khách tự gia hạn phiên giữ
  chỗ của mình. Không có token → **401**. Không có body: phiên xác định bằng `sessionCode` trên
  đường dẫn, người gia hạn là người đăng nhập.
- **Chỉ chủ phiên gia hạn được**: điều kiện `UserId` nằm ngay trong truy vấn — phiên của người khác
  và phiên không tồn tại cùng một câu trả lời **404** `Không tìm thấy phiên giữ chỗ` (cùng lối mục
  GET ở trên).
- **Hạn mới = thời điểm gia hạn + 10 phút** (UTC), không phải hạn cũ + 10 phút: khách luôn nhận
  trọn vẹn một cửa sổ 10 phút mới kể từ lúc bấm gia hạn — màn hình đếm ngược chỉ cần đặt lại về
  `10:00` khi gọi thành công. Cập nhật **cả nhóm dòng cùng phiên** đang `Holding` (docs/26 §1: gia
  hạn đổi theo nhóm) và ghi **một dòng `SeatHoldLogs`** `Action = 'Extended'` cho mỗi lượt vừa gia
  hạn (docs/26 §6).
- **Chỉ gia hạn tối đa 1 lần** (US 3): chốt nằm ở unique index `(SeatHoldId, Action)` của
  `SeatHoldLogs` (docs/26 §1) — dòng `Extended` thứ hai không lọt được xuống bảng. Tầng service vẫn
  kiểm tra trước để trả lỗi có thông báo, không để khách nhận lỗi unique thô.
- Phiên không còn dòng nào `Holding` (đã `Confirmed` / `Released` / `Expired`) → **409**
  `Phiên giữ chỗ đã kết thúc, không thể gia hạn.`
- Phiên còn `Holding` nhưng đã có log `Extended` → **409**
  `Phiên giữ chỗ đã được gia hạn rồi — mỗi phiên chỉ được gia hạn tối đa 1 lần.`
- Trả về đúng hình dạng `SeatHoldSession` ở trên với `status = "Holding"`, `expiresAt` là hạn mới
  và `canExtend = false` (vừa dùng hết lượt gia hạn).

```json
// 200 — ví dụ POST /seat-holds/PHIEN-8f3a2c1d/extend
{
  "sessionCode": "PHIEN-8f3a2c1d",
  "tripId": "a1c7f0e2-0000-0000-0000-000000000000",
  "seatNumbers": ["A1", "A2"],
  "status": "Holding",
  "expiresAt": "2026-10-01T01:16:00Z",
  "canExtend": false
}
```

> **Vì sao gia hạn xong trả 409 mà không phải 400 khi hết lượt?** 400 là lỗi của dữ liệu gửi lên,
> 409 là xung đột với trạng thái hiện tại của phiên — đúng quy ước D2 cho cả hai ca "phiên đã kết
> thúc" và "đã gia hạn rồi". Hai request gia hạn đồng thời: request thua đâm vào unique index
> `(SeatHoldId, Action)` khi ghi log, service bắt `DbUpdateException` và trả cùng câu 409 "đã được
> gia hạn rồi".

> **Vì sao hạn mới tính từ lúc gia hạn chứ không phải hạn cũ cộng thêm?** Tính từ hạn cũ thì gia
> hạn sớm được thưởng thời gian (gia hạn ở phút thứ 1 thành giữ 19 phút) còn gia hạn muộn bị phạt
> — cùng một nút bấm mà hai khách nhận hai lợi ích khác nhau. Tính từ lúc gia hạn thì mọi khách
> đều nhận đúng lời hứa của US 3: "giữ chỗ trong 10 phút", gia hạn là thêm một lần 10 phút nữa.

> **Vì sao đứng ở controller riêng (`SeatHoldExtendController`)?** Cùng lý do mục GET: bốn endpoint
> của bề mặt `api/seat-holds` thuộc bốn task của bốn người, mỗi task một controller.

### `POST /seat-holds/{sessionCode}/release` — nhả ghế khi khách huỷ thao tác

> ✅ Backend đã có (`SeatHoldReleaseController` — Phùng Duy Hoàng, story 3, task *"API nhả ghế khi
> hết hạn hoặc khách huỷ thao tác"*). Đây là endpoint màn hình gọi khi khách bấm "Huỷ" trong modal
> đếm ngược, hoặc khi đồng hồ về 0 mà modal còn mở — trả ghế về sơ đồ ngay, không đợi job nền.

- **Yêu cầu đăng nhập** (`[Authorize]` trần, không policy vai trò) — hành khách tự nhả phiên giữ
  chỗ của mình. Không có token → **401**. Không có body: phiên xác định bằng `sessionCode` trên
  đường dẫn, người nhả là người đang đăng nhập.
- **Chỉ chủ phiên nhả được**: điều kiện `UserId` nằm ngay trong truy vấn — phiên của người khác và
  phiên không tồn tại cùng một câu trả lời **404** `Không tìm thấy phiên giữ chỗ` (cùng lối mục GET
  và extend ở trên).
- **Phiên còn `Holding`** (kể cả vừa quá `ExpiresAt` mà job nền chưa quét — xem "Vì sao" dưới) →
  lật **cả nhóm dòng cùng phiên** sang `Released` (docs/26 §1: tạo / gia hạn / nhả / hết hạn đều
  đổi theo nhóm) và ghi **một dòng `SeatHoldLogs`** `Action = 'Released'` cho mỗi lượt giữ vừa nhả
  (docs/26 §6) → **200** với `status = "Released"`, `expiresAt` giữ nguyên hạn cũ.
- **Phiên đã `Expired` / `Released` từ trước** → **200** với đúng trạng thái hiện tại của phiên,
  không ghi gì thêm. Nhả là thao tác kết thúc nên gọi lại nhiều lần vẫn 200 — xem "Vì sao" dưới.
- **Phiên đã `Confirmed`** (đã chốt thành vé) → **409** `Phiên giữ chỗ đã chốt thành vé, không thể
  nhả.` — không lật trạng thái, không ghi log.
- Trả về đúng hình dạng `SeatHoldSession` ở trên, `canExtend = false` (phiên đã kết thúc thì không
  còn gì để gia hạn).

```json
// 200 — ví dụ POST /seat-holds/PHIEN-8f3a2c1d/release
{
  "sessionCode": "PHIEN-8f3a2c1d",
  "tripId": "a1c7f0e2-0000-0000-0000-000000000000",
  "seatNumbers": ["A1", "A2"],
  "status": "Released",
  "expiresAt": "2026-10-01T01:10:00Z",
  "canExtend": false
}
```

> **Vì sao phiên đã `Expired` / `Released` vẫn trả 200 chứ không 409 như API gia hạn?** Nhả là thao
> tác KẾT THÚC, không phải thao tác đòi phiên còn sống: đích đến của người gọi — "ghế tự do" — đã
> đạt từ trước, nên câu trả lời đúng là "xong rồi". Ba ca dùng thật đòi hỏi điều này: đồng hồ đếm
> ngược về 0 và modal tự gọi release trong khi job nền có thể đã quét trước đó vài giây; khách bấm
> "Huỷ" hai lần; mạng chập chờn phải gọi lại. Trả 409 ở những ca đó là báo cho khách rằng thao tác
> huỷ của họ thất bại trong khi ghế đã thực sự được trả — sai sự thật. Endpoint nhả vì vậy hành xử
> như một lệnh xoá: gọi lại bao nhiêu lần kết quả vẫn là 200.

> **Vì sao `Confirmed` là 409 mà không nhả?** Dòng `Confirmed` là ghế đã bán — có vé, có tiền. Lật
> nó sang `Released` là viết sai lịch sử giao dịch. Và nhả riêng các dòng `Holding` còn sót của một
> phiên đã chốt cũng là viết vào một phiên đã xong: lần theo vé thì khách đã trả tiền cho CẢ phiên,
> nên dòng `Holding` còn lại là dữ liệu lệch cần con người xem, không phải thứ tự đoán ý. Kiểm
> `Confirmed` TRƯỚC cả dòng `Holding` nên ca lệch bị chặn nguyên phiên, không nhả nửa vời.

> **Vì sao không kiểm tra `ExpiresAt` trước khi nhả?** Job nền `SeatHoldExpiryBackgroundService`
> (docs/26 §4) quét mỗi phút, nên phiên vừa quá hạn có thể còn `Holding` trong CSDL tới gần một
> phút. Trong khe hở đó khách bấm "Huỷ" (hoặc màn hình tự gọi release khi đồng hồ về 0) mà endpoint
> từ chối vì "đã hết hạn" thì ghế bị giữ thêm tới lượt quét kế tiếp — mất đúng thứ mà thao tác nhả
> sinh ra để trả lại. Nhả sớm một phiên đã quá hạn là vô hại: đích đến giống hệt lượt quét của job,
> chỉ khác người ghi log (`Released` thay vì `Expired` — đúng ý nghĩa "khách chủ động huỷ").

> **Vì sao không có 409 "đã nhả rồi" cho hai request đồng thời?** Request thua đâm vào unique index
> `(SeatHoldId, Action)` khi ghi dòng `Released` thứ hai, service bắt `DbUpdateException` — nhưng
> KHÁC API gia hạn (cũng bắt `DbUpdateException` nhưng trả 409 "đã gia hạn rồi"): ở đây đâm unique
> nghĩa là request kia ĐÃ nhả xong, đích đến đã đạt nên trả 200 như ca thành công.

> **Vì sao đứng ở controller riêng (`SeatHoldReleaseController`)?** Cùng lý do mục GET và extend:
> bốn endpoint của bề mặt `api/seat-holds` thuộc bốn task của bốn người, mỗi task một controller.

## Đồng bộ ghế realtime — `/hubs/seat-map`

> ✅ Backend đã có (`SeatMapHub` — task *"Đồng bộ trạng thái ghế realtime giữa các phiên
> (SignalR)"*, dòng 11 Sprint 3, phân công bảng tính: Hoàng Văn Thịnh). Hub KHÔNG thay thế
> `GET /trips/{id}/seats`: nó chỉ là kênh báo "sơ đồ ghế của chuyến này vừa đổi — gọi lại đi",
> còn dữ liệu vẫn đến từ đúng một endpoint đó (xem "Vì sao" dưới).
>
> Đây là endpoint **đầu tiên không nằm dưới tiền tố `/api`**: hub gắn thẳng vào pipeline bằng
> `MapHub` nên sống ở đường dẫn gốc `/hubs/…`, không đi qua controller.

### Kết nối

| Thuộc tính | Giá trị |
|---|---|
| URL | `/hubs/seat-map` |
| Giao thức | SignalR JSON (mặc định) |
| Yêu cầu đăng nhập | **Không** — hub ẩn danh, cùng mức với `GET /trips/{id}/seats` (sơ đồ ghế là thông tin khách tra cứu được trước khi có tài khoản) |
| Nhóm (group) | Mỗi chuyến một nhóm: `trip:{tripId}` |
| Thư viện client | `@microsoft/signalr` (frontend) |

### Method client gọi (invoke)

| Method | Tham số | Mô tả |
|---|---|---|
| `JoinTrip` | `tripId` (GUID dạng chuỗi) | Vào nhóm `trip:{tripId}` — gọi ngay sau khi kết nối và **gọi lại sau mỗi lần tự kết nối lại** (mất kết nối là mất nhóm) |
| `LeaveTrip` | `tripId` (GUID dạng chuỗi) | Rời nhóm — gọi khi rời màn hình. Rời kết nối SignalR cũng tự xoá mọi nhóm nên đây chỉ là dọn sớm |

### Sự kiện server đẩy (event)

| Sự kiện | Payload | Mô tả |
|---|---|---|
| `SeatMapChanged` | `{ "tripId": "<GUID>" }` | Sơ đồ ghế của một chuyến vừa đổi — client gọi lại `GET /trips/{id}/seats` |

```json
// Ví dụ payload sự kiện SeatMapChanged
{ "tripId": "a1c7f0e2-0000-0000-0000-000000000000" }
```

### Khi nào sự kiện được phát

Đúng ba nguồn làm đổi trạng thái ghế, mỗi nguồn phát một lần cho chuyến bị ảnh hưởng:

| Nguồn | Phát khi |
|---|---|
| `POST /seat-holds` (giữ ghế) | Phiên giữ chỗ tạo thành công |
| `POST /seat-holds/{sessionCode}/release` (nhả ghế) | Phiên được nhả lần đầu (gọi lại trên phiên đã kết thúc vẫn 200 — sự kiện đã phát ở lần đầu) |
| Job nền `SeatHoldExpiryBackgroundService` (quét mỗi phút) | Có phiên `Holding` vừa quá `ExpiresAt` — chỉ phát cho những chuyến thật sự có phiên vừa hết hạn |

> **Vì sao sự kiện chỉ báo "đã đổi" mà không gửi kèm sơ đồ ghế?** Gửi kèm thì mỗi lần đổi phải
> dựng lại toàn bộ sơ đồ ở tầng đẩy, và hai kênh (đẩy + gọi lại) có thể lệch nhau — client tin
> kênh nào? Báo "đã đổi" rồi để client gọi lại đúng endpoint cũ giữ **một nguồn sự thật duy
> nhất** (`GET /trips/{id}/seats`): mọi quy tắc suy trạng thái (SeatHolds + Tickets) chỉ sống ở
> một chỗ, không có định nghĩa thứ hai để lệch. Cái giá là một request thừa mỗi lần có sự kiện —
> rẻ hơn nhiều so với hai định nghĩa trạng thái ghế.

> **Vì sao hub ẩn danh?** Cùng lý do `GET /trips/{id}/seats` không đòi đăng nhập: sơ đồ ghế là
> thông tin khách tra cứu được trước khi có tài khoản. Bắt đăng nhập ở đây chỉ khoá đường xem
> một thứ đang mở ở endpoint chính, mà không thêm được bảo vệ nào.

> ⚠️ **Chưa có backplane — giới hạn đã biết.** SignalR không backplane chỉ chạm được client nối
> vào CHÍNH máy chủ đã phát sự kiện. Dự án đang chạy hai bản triển khai (Render + máy thành
> viên) chung một CSDL Supabase: một phiên giữ ghế ở Render sẽ không đẩy được thông báo tới
> trình duyệt đang nối vào bản chạy ở máy. Trong phạm vi đồ án, mỗi bản chạy vẫn tự nhất quán
> (giữ ghế ở đâu thì các phiên nối vào đó thấy ngay), và màn hình luôn gọi lại sơ đồ ghế khi
> tải nên dữ liệu không bao giờ cũ quá một lần tải trang. Muốn xuyên máy chủ thì thêm backplane
> (Azure SignalR / `AddStackExchangeRedis`) — ngoài phạm vi Sprint 3.

## Thông báo kết quả xử lý — `/hubs/notifications`

> ✅ Backend đã nối đủ: `NotificationHub` + `TicketRequestResultNotifier` (task *"Ghi log và thông
> báo kết quả xử lý cho hành khách"*, dòng 7 Sprint 4, phân công bảng tính: Vàng Thị Dăm) đã có
> `AddScoped<ITicketRequestResultNotifier, TicketRequestResultNotifier>()` và
> `MapHub<NotificationHub>("/hubs/notifications")` trong `Program.cs` — hai dòng thuộc file của
> Phùng Duy Hoàng, thêm ở task *"API duyệt / từ chối yêu cầu huỷ vé, đổi vé"* (dòng 5 Sprint 4) vì
> chính hai endpoint PATCH đó là nguồn phát sự kiện. Chi tiết + lý do: `docs/33-thong-bao-ket-qua-huy-doi-ve.md` §3.4.
>
> Kết quả hoàn tiền đi qua CÙNG hub này: `RefundResultNotifier` (task *"Thông báo kết quả hoàn tiền
> qua email và trên web"*, dòng 34 Sprint 4, Phùng Duy Hoàng — bản này làm **trên web**, không gửi
> email, theo quyết định A8.5) phát hai loại `Refund…` từ lớp bọc `NotifyingRefundAttemptService`.
> Chi tiết + lý do: `docs/41-thong-bao-ket-qua-hoan-tien.md`.
>
> Hub KHÔNG phải nguồn dữ liệu: nó chỉ báo "bạn vừa có thông báo mới", còn **danh sách** thông báo
> đến từ endpoint REST `GET /api/notifications/me` (task *"API lịch sử thông báo đã nhận của hành
> khách"*, dòng 19 Sprint 5, Vàng Thị Dăm) — hình dạng đầy đủ ở mục
> **"Thông báo trong ứng dụng — /notifications"**. Mục này chỉ mô tả kênh đẩy.

### Kết nối

| Thuộc tính | Giá trị |
|---|---|
| URL | `/hubs/notifications` |
| Giao thức | SignalR JSON (mặc định) |
| Yêu cầu đăng nhập | **Có** — `[Authorize]`. Không có token hợp lệ thì lượt negotiate trả **401** |
| Token gửi kèm | Query string `?access_token=<JWT>` — xem "Vì sao token đi bằng query string" |
| Nhóm (group) | Mỗi người một nhóm: `user:{userId}` — **server tự thêm khi kết nối**, client không xin được |
| Thư viện client | `@microsoft/signalr` (frontend) |

> **Vì sao hub này bắt đăng nhập, khác `/hubs/seat-map`?** Sơ đồ ghế là thông tin công khai (khách
> tra cứu được trước khi có tài khoản), còn thông báo là **dữ liệu riêng của một người**: kết quả
> duyệt/từ chối yêu cầu huỷ vé của họ. Không xác thực thì không có cách nào biết ai là ai để gửi
> riêng cho đúng người.

### Method client gọi (invoke)

**Không có.** Khác `/hubs/seat-map` (client gọi `JoinTrip`), ở đây **không có method nào để client
gọi** — client chỉ kết nối và lắng nghe.

> **Vì sao không có `JoinUser`?** Nếu để client tự gửi id người dùng lên thì bất kỳ ai cũng xin được
> vào nhóm của người khác và đọc thông báo của họ. Server tự suy nhóm từ chính token đã xác thực
> (`Context.UserIdentifier`), nên **không tồn tại đường nào để đọc nhóm của người khác**. Đây là lý
> do duy nhất khiến hub này khác hình dạng với `SeatMapHub` — không phải để cho gọn.

### Sự kiện server đẩy (event)

| Sự kiện | Payload | Mô tả |
|---|---|---|
| `NotificationReceived` | `{ "notificationId": "<GUID>" }` | Khách vừa có một thông báo mới — client gọi lại API danh sách thông báo |

```json
// Ví dụ payload sự kiện NotificationReceived
{ "notificationId": "8f2b1d64-0000-0000-0000-000000000000" }
```

> **Vì sao payload chỉ có mỗi id, không gửi kèm `title`/`body`?** Gửi kèm thì nội dung thông báo
> tồn tại ở hai nơi (bảng `Notifications` và payload) và hai nơi đó có thể lệch nhau — client phải
> tin cái nào? Cùng lý do `/hubs/seat-map` chỉ báo "đã đổi" thay vì gửi cả sơ đồ ghế: giữ **một
> nguồn sự thật duy nhất**. Cái giá là một request danh sách mỗi lần có sự kiện.
>
> Client **không cần** lấy nội dung từ payload: chúng nằm sẵn trong bảng `Notifications` để khách đọc
> lại kể cả khi offline (quyết định A8.5), nên kênh đẩy chỉ có một việc là đánh thức màn hình.

### Khi nào sự kiện được phát

Có bốn nguồn, mỗi **sự kiện** phát **một lần** cho đúng người nhận (người gửi yêu cầu, chủ khoản
hoàn, hoặc hành khách đã mua vé của chuyến):

| Nguồn | Phát khi |
|---|---|
| `PATCH /cancellation-requests/{id}` (duyệt / từ chối yêu cầu huỷ) | Trạng thái được lật sang `Approved` hoặc `Rejected` và **đã lưu** |
| `PATCH /ticket-changes/{id}` (duyệt / từ chối yêu cầu đổi chuyến) | Trạng thái được lật sang `Approved` hoặc `Rejected` và **đã lưu** |
| Một lượt gọi cổng hoàn tiền (job thử lại theo lịch dòng 30, nút "Thử lại" của màn quản trị dòng 35, lượt hoàn tự động lúc duyệt huỷ dòng 28) | Khoản hoàn có **kết quả cuối đã lưu**: `Succeeded`, hoặc `Failed` và **đã hết lịch thử lại** (`NextRetryAt = null`) |
| `PUT /routes/{routeId}/trips/{id}` (quản lý sửa lịch trình chuyến — dòng 44 Sprint 4) | Giờ khởi hành hoặc giờ tới dự kiến **thực sự đổi** và chuyến đã lưu còn ở `Scheduled` hoặc `Running` |

Hai endpoint PATCH trên là task *"API duyệt / từ chối yêu cầu huỷ vé, đổi vé"* (dòng 5 Sprint 4,
Phùng Duy Hoàng) — hình dạng request/response đầy đủ ở mục
**"Huỷ & Đổi vé — `/cancellation-requests` và `/ticket-changes`"**. Mục này chỉ chốt phần thông báo
của chúng.

> 🔴 **Nguồn thứ ba: kết quả hoàn tiền — chỉ phát ở KẾT QUẢ CUỐI, không phát từng lượt gọi cổng.**
> Một khoản hoàn có thể bị gọi cổng tới sáu lượt (lượt đầu + năm lượt thử lại theo nấc thang
> `RefundRetryPolicy`) trước khi ngã ngũ, nên thông báo chỉ đi khi con số đã ngã ngũ:
>
> | Kết quả lượt gọi | Phát? | Vì sao |
> |---|---|---|
> | `Succeeded` | **Có** | Tiền đã thực sự rời cổng — kết quả cuối |
> | `Failed`, còn lịch (`NextRetryAt` khác null) | **Không** | Hệ thống còn tự thử lại; báo lúc này là báo một kết quả chưa ngã ngũ, và một khoản hỏng tạm thời sẽ dội tới năm dòng giống hệt nhau |
> | `Failed`, hết lịch (`NextRetryAt = null`) | **Có** | Hệ thống đã làm hết phần tự động, khoản nằm chờ quản trị bấm thử tay — khách cần biết để liên hệ nhà xe |
> | `Unresolved` (cổng không trả lời) | **Không** | Chưa có kết quả nào để nói; khoản giữ nguyên `Pending` chờ bước đối soát (dòng 43) — phát ở đây là đoán |
>
> Kết quả **cuối cùng nằm trong CSDL mới là thứ được báo**: lượt gọi thua cuộc đua giành dòng
> (`Claimed = false`) và lượt gọi mà việc ghi dòng `Refunds` bị mất đều **không** phát — không có
> kết quả đã lưu thì không có gì để báo.
>
> Cơ chế: lớp bọc `NotifyingRefundAttemptService` ôm `IRefundAttemptService`, nên **mọi** đường gọi
> cổng đều đi qua đây (job thử lại dòng 30 đã có; nút "Thử lại" dòng 35 và lượt hoàn tự động dòng 28
> khi dựng lên sẽ nối vào đúng service đó) — không đường nào tự phát thông báo riêng.

> 🚌 **Nguồn thứ tư: sửa lịch trình chuyến — báo theo TỪNG LẦN ĐỔI THẬT, không báo theo lượt PUT.**
> Lớp bọc `NotifyingRouteTripsService` ôm `IRouteTripsService`, nên mọi đường sửa chuyến qua
> `PUT /routes/{routeId}/trips/{id}` đều đi qua đây. Người nhận là **mọi hành khách có vé `Paid`
> của chuyến** (mỗi người một thông báo), trỏ về dòng `Trips` qua `RelatedEntityType = Trip`.
>
> | Thay đổi trong lượt PUT | Phát? | Loại |
> |---|---|---|
> | `departureTime` hoặc `arrivalTime` đổi, mọi mốc đổi đều **lùi ra sau** | **Có** | `TripDelayed` |
> | Có mốc **sớm hơn**, hoặc `arrivalTime` từ null thành có / từ có thành null | **Có** | `TripScheduleChanged` |
> | Giờ giữ nguyên (chỉ đổi xe, chỉ đổi `status`, hoặc PUT lại đúng dữ liệu cũ) | **Không** | — |
> | Chuyến đã lưu ở `Cancelled` hoặc `Completed` | **Không** | — |
>
> "Từng lần đổi thật" khác "mỗi quyết định một thông báo" của hai endpoint PATCH: một chuyến bị dời
> giờ hai lần là **hai sự kiện**, khách phải nhận cả hai — chốt đọc-trước-khi-ghi của hai notifier
> kia sẽ nuốt mất lần thứ hai nếu áp vào đây. Vì vậy notifier này so **giờ cũ (chụp trước khi sửa)
> với giờ mới (đọc lại từ CSDL)**, và không có khoá chống trùng trong bảng `Notifications`.
>
> ⚠️ **Huỷ chuyến qua `DELETE /routes/{routeId}/trips/{id}` KHÔNG phát thông báo.** Đó là nghiệp vụ
> huỷ chuyến, không phải trễ/đổi lịch trình — bảng phân công không có dòng nào cho thông báo huỷ
> chuyến, nên không tự đặt ra loại mới. Cùng lý do, đổi `status` sang `Cancelled` bằng PUT cũng im
> lặng (xem bảng trên).

> ⚠️ **Không có sự kiện nào khi khách GỬI yêu cầu.** Gửi yêu cầu là việc của khách, không phải kết
> quả cần báo cho khách. Thông báo chỉ đi ở bước **duyệt hoặc từ chối**.
>
> ⚠️ **Gọi lại lượt duyệt không phát lại sự kiện.** Một quyết định = một thông báo: nếu đã có dòng
> thông báo cho đúng yêu cầu đó thì lượt gọi sau bị bỏ qua hoàn toàn (không ghi, không đẩy). Nhờ vậy
> client retry cũng không làm khách thấy hai thông báo giống nhau.

### Tám loại thông báo

| `type` | `title` |
|---|---|
| `CancellationApproved` | Đã duyệt yêu cầu huỷ vé |
| `CancellationRejected` | Từ chối yêu cầu huỷ vé |
| `TicketChangeApproved` | Đã duyệt yêu cầu đổi chuyến |
| `TicketChangeRejected` | Từ chối yêu cầu đổi chuyến |
| `RefundSucceeded` | Đã hoàn tiền |
| `RefundFailed` | Hoàn tiền thất bại |
| `TripDelayed` | Chuyến xe bị trễ |
| `TripScheduleChanged` | Lịch trình chuyến xe thay đổi |

Hai loại `…Rejected` có lý do từ chối của quản lý **chép nguyên văn** vào cuối `body`; quản lý không
nêu lý do thì body ghi rõ *"Người duyệt không nêu lý do."* chứ không để cụt.

Hai loại `Refund…` nhắm vào **chủ khoản hoàn** (không phải người duyệt) và trỏ vào dòng `Refunds`
qua `RelatedEntityType = Refund` + `RelatedEntityId` — màn danh sách thông báo bấm vào thì mở màn
*"Hoàn tiền của tôi"* (`/my-refunds`). Khoản hoàn không có màn chi tiết riêng: dòng trong danh sách
đã đủ để đọc một mình (số tiền, trạng thái, lý do hỏng).

Hai loại `Trip…` nhắm vào **hành khách đã mua vé `Paid` của chuyến** và trỏ vào dòng `Trips` qua
`RelatedEntityType = Trip` + `RelatedEntityId`. Body phải đọc đứng một mình được: có **tên tuyến**
và **giờ cũ → giờ mới** của các mốc đã đổi, định dạng `dd/MM/yyyy HH:mm` theo **giờ Việt Nam
(UTC+7)** — cùng lối cố định `VietnamUtcOffsetHours` đã dùng ở vé/PDF, không dùng
`TimeZoneInfo.FindSystemTimeZoneById` (id vùng khác nhau giữa Windows và Linux, CI chạy ubuntu).
Cả hai loại nêu **mốc cũ và mốc mới** để khách đối chiếu — không suy ra "trễ bao lâu" hay "sớm bao
lâu" thành câu riêng, vì chuyến có thể đổi cả hai mốc. Không có số tiền nào trong body — cùng luật
với các loại trên.

> 🔴 **Nội dung thông báo KHÔNG chứa số tiền** — không có khoản hoàn nào, không phí huỷ; hai loại
> `Refund…` cũng vậy. Với thông báo huỷ/đổi: việc hoàn tiền do bảng `Refunds` (US 8) thực hiện và có
> thể thất bại rồi thử lại, nên nêu một con số ở đó là hứa trước một việc hệ thống chưa làm. Với
> thông báo hoàn tiền: số tiền nằm ở chính dòng `Refunds` (cột `Amount` — con số **đã thực sự gửi
> lên cổng**), chép vào thông báo là dựng bản sao thứ hai có thể lệch; định dạng tiền cũng là việc
> của frontend. Frontend cần số tiền thì lấy từ chính dòng yêu cầu / dòng khoản hoàn. Có test ghim
> điều này (body không được chứa chữ số nào) ở cả hai notifier — chi tiết:
> `docs/33-thong-bao-ket-qua-huy-doi-ve.md` §6.1 và `docs/41-thong-bao-ket-qua-hoan-tien.md` §6.

### Ví dụ client (frontend)

Client **phải** truyền `accessTokenFactory`, nếu không hub trả 401 và không có thông báo nào tới:

```ts
const connection = new HubConnectionBuilder()
  .withUrl(`${API_BASE_URL}/hubs/notifications`, {
    accessTokenFactory: () => accessToken,   // ⚠️ bắt buộc — xem "Vì sao token đi bằng query string"
  })
  .withAutomaticReconnect()
  .build();

// Không có JoinUser/LeaveUser: nhóm do server tự thêm khi kết nối.
connection.on("NotificationReceived", ({ notificationId }) => {
  // Gọi lại GET /api/notifications/me rồi cập nhật badge — mục "Thông báo trong ứng dụng".
});
```

> **Vì sao không cần gọi lại lệnh vào nhóm sau khi tự kết nối lại?** Khác `SeatMapHub`, ở đây nhóm
> được thêm trong `OnConnectedAsync` — mỗi lần kết nối lại là một lượt `OnConnectedAsync` mới, nên
> server tự thêm lại. `withAutomaticReconnect()` là đủ.

> **Vì sao token đi bằng query string?** Trình duyệt **không đặt được header `Authorization` cho
> WebSocket** — API WebSocket của trình duyệt không có chỗ truyền header. Đây là cách duy nhất, và là
> mặc định của client SignalR JS (`accessTokenFactory`). Phía server, một ngoại lệ duy nhất trong
> `JwtMiddleware` đọc `?access_token=` **chỉ cho đường dẫn `/hubs`**; token vẫn được kiểm tra đầy đủ
> (chữ ký, hạn, issuer, audience) như mọi lượt khác — đổi chỗ đọc chứ không nới lỏng kiểm tra.
>
> ⚠️ **Đánh đổi đã biết:** token nằm trong URL nên log truy cập của hạ tầng (IIS/Nginx/Render) có
> thể ghi lại nó. Giảm nhẹ: `AuditLogMiddleware` đã loại cả tiền tố `/hubs` khỏi nhật ký nên URL
> mang token **không** đi vào bảng `AuditLogs`. Người vận hành phải bảo vệ log truy cập. Cách xử lý
> triệt để (token riêng ngắn hạn cho hub, hoặc xác thực bằng cookie) ngoài phạm vi đồ án.

> ⚠️ **Chưa có backplane — cùng giới hạn như `/hubs/seat-map`.** SignalR không backplane chỉ chạm
> được client nối vào CHÍNH máy chủ đã phát sự kiện. Dự án đang chạy hai bản triển khai (Render + máy
> thành viên) chung một CSDL Supabase: quản lý duyệt yêu cầu ở Render sẽ **không** đẩy được thông
> báo tới trình duyệt đang nối vào bản chạy ở máy. Trong phạm vi đồ án điều này **không làm mất
> thông báo** — dòng `Notifications` đã ghi vào CSDL dùng chung, nên khách vẫn thấy khi mở màn danh
> sách; chỉ mất đúng lời nhắc tức thời. Muốn xuyên máy chủ thì thêm backplane (Azure SignalR /
> `AddStackExchangeRedis`) — ngoài phạm vi.

## Thông báo trong ứng dụng — `/notifications`

> ✅ Backend đã có: `MyNotificationsController` + `IMyNotificationsService`/`MyNotificationsService`
> (task *"API lịch sử thông báo đã nhận của hành khách"*, dòng 19 Sprint 5, phân công bảng tính:
> Vàng Thị Dăm), kèm một dòng
> `builder.Services.AddScoped<IMyNotificationsService, MyNotificationsService>()` trong `Program.cs`
> — dòng thuộc file của Phùng Duy Hoàng, thêm ngay ở task này (cùng lối đã làm ở khối
> `IRefundLookupService`, commit `d5bd049`). Test tích hợp:
> `backend/SmartBus.Tests/MyNotificationsApiTests.cs`.
>
> Đây là mặt **ĐỌC** của thông báo. Kênh **ĐẨY** vẫn là `NotificationHub` ở `/hubs/notifications`
> — mục ngay trên. Hai mặt cùng đọc một bảng `Notifications`, không có bản sao dữ liệu nào: hub
> chỉ đánh thức màn hình, còn nội dung luôn lấy từ đây.

### Endpoint

| Method | Endpoint | Mô tả | Query | Trả về |
|---|---|---|---|---|
| `GET` | `/notifications/me` | Lịch sử thông báo đã nhận của **chính người gọi**, mới nhất trước | *không có* | `200` mảng `Notification` (rỗng nếu chưa nhận thông báo nào) · `401` khi thiếu/không hợp lệ token |

Không có endpoint nào khác trên bề mặt này. Các task Sprint 5 còn lại của US 10 sẽ dựng thêm ở đây:
API đăng ký/huỷ theo dõi trạm (dòng 17, Trần Trung Hiếu), màn quản lý mẫu thông báo (dòng 24).

### Dòng `Notification`

| Trường | Kiểu | Mô tả |
|---|---|---|
| `id` | `string` (GUID) | Khoá chính |
| `type` | `string` | Một trong **tám** loại — bảng "Tám loại thông báo" ở mục trên. Quyết định nhãn và biểu tượng |
| `title` | `string` | Dòng tiêu đề ngắn — dòng đầu của mỗi mục trong danh sách |
| `body` | `string` | Nội dung đầy đủ, **có thể xuống dòng**. Lý do từ chối của quản lý được chép nguyên văn vào đây |
| `relatedEntityType` | `string \| null` | Bảng mà `relatedEntityId` trỏ tới: `CancellationRequest`, `TicketChange`, `Refund` hoặc `Trip`. `null` với thông báo không gắn bản ghi nào |
| `relatedEntityId` | `string \| null` | Id bản ghi mà thông báo nói về; `null` cùng lúc với `relatedEntityType` |
| `createdAt` | `string` (ISO 8601, UTC) | Thời điểm thông báo được sinh — cũng là cột sắp xếp của danh sách |

> 🔴 `type` và `relatedEntityType` là **CHUỖI chữ**, không phải số. Dự án không bật
> `JsonStringEnumConverter`, nên để nguyên enum trong DTO là JSON tuôn ra con số (`0`, `1`, …) —
> bẫy đã ghi ở `RefundResponse` và `TicketResponse`. Service `ToString()` trong bộ nhớ rồi mới dựng
> DTO; có test ghim điều này.

> ⚠️ **Chưa có trường "đã đọc".** Bảng `Notifications` chưa có cột đó (lý do hoãn ghi ở doc-comment
> của entity `Notification`: hình dạng đúng của nó phụ thuộc badge của màn hình, dòng 22). Trả một
> trường `isRead` ở đây là hứa một thứ backend không có.

### Ví dụ

```http
GET /api/notifications/me
Authorization: Bearer <JWT>
```

```json
[
  {
    "id": "8f2b1d64-1111-2222-3333-444455556666",
    "type": "CancellationApproved",
    "title": "Đã duyệt yêu cầu huỷ vé",
    "body": "Yêu cầu huỷ vé của quý khách đã được duyệt.",
    "relatedEntityType": "CancellationRequest",
    "relatedEntityId": "1a2b3c4d-5555-6666-7777-888899990000",
    "createdAt": "2026-10-14T03:20:11.482Z"
  },
  {
    "id": "7e1a0c53-aaaa-bbbb-cccc-ddddeeeeffff",
    "type": "RefundFailed",
    "title": "Hoàn tiền thất bại",
    "body": "Rất tiếc, giao dịch hoàn tiền chưa thực hiện được. Nhà xe sẽ liên hệ quý khách.",
    "relatedEntityType": "Refund",
    "relatedEntityId": "9f8e7d6c-1234-5678-9abc-def012345678",
    "createdAt": "2026-10-13T21:05:40.117Z"
  },
  {
    "id": "5c4b3a29-0000-1111-2222-333344445555",
    "type": "TicketChangeApproved",
    "title": "Đã duyệt yêu cầu đổi chuyến",
    "body": "Yêu cầu đổi chuyến của quý khách đã được duyệt.",
    "relatedEntityType": null,
    "relatedEntityId": null,
    "createdAt": "2026-10-11T06:44:02.900Z"
  }
]
```

> **Vì sao trả MẢNG TRẦN, không phân trang?** Khớp đúng lối các endpoint "của tôi" đang có
> (`GET /tickets/me`, `GET /refunds`, `GET /feedbacks/me`, `GET /monthly-passes/me`), và khớp đúng
> bản nháp frontend đã merge (`frontend/src/api/notificationApi.ts` khai
> `listMine: () => Promise<AppNotification[]>`). Chuông + badge gọi endpoint này mỗi lần có sự kiện
> đẩy, nên thêm một vỏ phân trang là bắt mọi lượt gọi đi qua thêm một tầng mà không lượt nào dùng.
>
> ⚠️ **Đánh đổi đã biết:** bảng chỉ-ghi-thêm nên danh sách của một người **dài dần vô hạn** — mỗi
> quyết định huỷ/đổi và mỗi kết quả cuối của một khoản hoàn là một dòng. Trong phạm vi đồ án con số
> này nhỏ (một khách có bao nhiêu yêu cầu huỷ vé?), nên chấp nhận được. Khi khối lượng thành vấn đề
> thật thì cách sửa là thêm **tham số con trỏ** (`?before=<createdAt>` — đúng chỉ mục
> `IX_Notifications_UserId_CreatedAt`, không cần đếm tổng), **không** phải lặng lẽ cắt bớt danh sách:
> cắt ngầm làm badge đếm sai mà không ai thấy lỗi.

> **Vì sao KHÔNG có `GET /notifications/{id}`?** `body` đã nằm trong **từng dòng** của danh sách.
> A8.5 chốt thông báo phải *đủ để đọc một mình* (lưu lại cho khách xem khi offline), nên không có
> phần nội dung nào bị giữ lại sau một endpoint chi tiết — màn chi tiết (dòng 23) đọc thẳng từ dòng
> đã tải về. Dựng thêm endpoint đó là mở một đường đọc thứ hai vào cùng dữ liệu mà không thêm gì.

> **Vì sao không có tham số `userId`?** Phạm vi "của tôi" nằm **trong truy vấn** (`WHERE UserId`),
> không nằm ở tầng kiểm tra: người gọi lấy từ claim `NameIdentifier` trong token, và **không tồn tại
> tham số nào để hỏi thông báo của người khác**. Thông báo là dữ liệu riêng của một người — nội dung
> là kết quả duyệt yêu cầu huỷ vé *của họ*, kèm cả lý do từ chối — nên đây là chuyện quyền đọc chứ
> không phải chuyện lịch sự. Cùng lối `GET /refunds` và `GET /tickets/me`.

> **Vì sao `[Authorize]` trần, không gắn policy vai trò?** Hành khách tự xem thông báo của mình, mà
> RBAC của dự án chỉ có `AdminOnly`/`ManagerOrAbove` — không có policy `Passenger` nào để gắn. Quyền
> sở hữu kiểm bằng chính điều kiện truy vấn ở trên, mạnh hơn một policy vai trò vì nó chặt theo *từng
> dòng* chứ không theo *loại tài khoản*.

> ✅ **Frontend đã khớp sáu loại / ba target (Băng đồng ý cho sửa `src/api/notificationApi.ts`).**
> `NotificationType` khai đủ **sáu** giá trị (thêm `RefundSucceeded`, `RefundFailed`),
> `NotificationTarget` đủ **ba** (thêm `Refund`), và `notificationApi.listMine()` gọi thẳng
> `GET /notifications/me` — `USE_MOCK_DATA` cùng nhánh dữ liệu giả đã bỏ. `NotificationBell` xếp
> `RefundSucceeded` vào nhóm "thành công" để thông báo hoàn tiền thành công không hiện dấu đỏ.
>
> ⚠️ **Còn thiếu hai loại của dòng 44 — đã báo ở PR, KHÔNG tự sửa (`src/api/` là file của Băng).**
> Backend phát đủ **tám** loại và **bốn** target: hai loại `TripDelayed`/`TripScheduleChanged` do
> `TripScheduleChangeNotifier` phát (mục trên), target `Trip`. `notificationApi.ts` mới khai sáu loại
> / ba target nên union TypeScript chưa phủ hai loại này. `NotificationBell.isPositiveOutcome` xếp mọi
> loại ngoài ba loại "tốt" vào nhóm đỏ — `TripDelayed` sẽ hiện dấu X đỏ "bị từ chối", sai nghĩa; cần
> ánh xạ biểu tượng riêng cho nhóm `Trip…` và điều hướng `relatedEntityType = Trip` (Băng/Hạnh, dòng
> 47–48).

## Xe buýt — `/buses`

> ✅ Backend đã có (`BusesController` — Trần Trung Hiếu, story 14). Frontend chưa có module gọi
> API này — màn hình quản lý đội xe sẽ dựng theo khuôn `frontend/src/api/fareApi.ts`.
>
> **Toàn bộ endpoint dưới đây yêu cầu vai trò `Admin` hoặc `Manager`.** Người đã đăng nhập
> nhưng không đủ quyền nhận **403** kèm body `{ "message": "Bạn không có quyền truy cập tính năng này." }`.

### Entity `Bus`

| Trường | Kiểu | Mô tả |
|---|---|---|
| `id` | `string` (GUID) | Khoá chính |
| `licensePlate` | `string` | Biển số xe — "29B-123.45". Duy nhất toàn hệ thống (khoá nghiệp vụ) |
| `busType` | `string` | Loại xe — "Xe buýt 45 chỗ", "Xe buýt điện" |
| `capacity` | `number` | Sức chứa theo số ghế |
| `status` | `string` | `Active` = đang khai thác · `Maintenance` = bảo dưỡng · `Inactive` = ngừng khai thác (lưu dạng chuỗi — quy ước A3) |
| `createdAt` | `string` (ISO 8601, UTC) | Thời điểm tạo |
| `updatedAt` | `string \| null` | `null` khi xe chưa được sửa lần nào |

```json
// Ví dụ Bus
{
  "id": "1c9a4f05-0000-0000-0000-000000000000",
  "licensePlate": "29B-123.45",
  "busType": "Xe buýt 45 chỗ",
  "capacity": 45,
  "status": "Active",
  "createdAt": "2026-09-29T08:00:00Z",
  "updatedAt": null
}
```

### Endpoints

| Method | Endpoint | Mô tả | Body | Trả về |
|---|---|---|---|---|
| GET | `/buses` | Danh sách + tìm kiếm + lọc trạng thái + phân trang | — | `BusListResponse` |
| GET | `/buses/{id}` | Chi tiết một xe | — | `Bus` |
| POST | `/buses` | Thêm xe | `CreateBus` | `Bus` (201) |
| PUT | `/buses/{id}` | Sửa xe | `UpdateBus` | `Bus` |
| DELETE | `/buses/{id}` | **Xoá mềm** — ngừng khai thác | — | `Bus` |

#### `GET /buses`

Tham số query (đều không bắt buộc):

| Tham số | Kiểu | Mặc định | Mô tả |
|---|---|---|---|
| `search` | `string` | — | Tìm theo biển số hoặc loại xe — **không phân biệt hoa thường** |
| `status` | `string` | — | Lọc theo trạng thái: `Active`, `Maintenance` hoặc `Inactive`. Bỏ trống = lấy cả ba |
| `page` | `number` | `1` | Trang, tính từ 1 |
| `pageSize` | `number` | `10` | Số dòng mỗi trang, tối đa **100** |

```json
// BusListResponse — ví dụ GET /buses?page=1&pageSize=10&status=Active
{
  "items": [ /* Bus[] của trang hiện tại */ ],
  "total": 42,
  "page": 1,
  "pageSize": 10
}
```

- `total` là tổng số xe khớp bộ lọc (không phải số dòng trong `items`) — dùng để vẽ phân trang.
- Thứ tự sắp xếp: `createdAt` giảm dần, xe tạo cùng lúc xếp theo `id` để phân trang ổn định.
- `page` hoặc `pageSize` ngoài khoảng hợp lệ → **400**.
- `status` không khớp ba mã trên → danh sách rỗng (không báo lỗi — cùng lối bộ lọc `status`
  của `/routes`).

#### `POST /buses`

```json
// CreateBus — body
{
  "licensePlate": "29B-123.45",
  "busType": "Xe buýt 45 chỗ",
  "capacity": 45
}
```

| Trường | Bắt buộc | Ràng buộc |
|---|---|---|
| `licensePlate` | ✅ | 2–20 ký tự, duy nhất toàn hệ thống |
| `busType` | ✅ | 2–50 ký tự |
| `capacity` | ✅ | Số nguyên từ 1 đến 200 — trần quy ước của API cho mọi cỡ xe buýt thành phố |

Lỗi thường gặp: trùng biển số → **400** `errors.licensePlate` · `capacity` ngoài khoảng →
**400** `errors.capacity`.

> **Vì sao trùng biển số trả 400 mà không phải 409?** Biển số là khoá nghiệp vụ do quản lý
> nhập tay vào ô form — cùng lý do trùng mã tuyến trả 400: frontend gắn thẳng lỗi vào ô input.
> Ràng buộc unique ở CSDL là lớp chặn cuối cho hai request song song cùng biển số.

Xe mới luôn bắt đầu ở `Active`. Dàn ghế (`Seats`) không do API này quản lý — `capacity` phải
khớp với số ghế của xe, và ghế được sinh từ sơ đồ ghế của **loại xe** (mục
"Sơ đồ ghế — `/seat-layouts`" ngay dưới). Xe thêm mới vào đội chỉ có ghế sau khi sơ đồ của loại
xe đó được lưu lại một lần.

#### `PUT /buses/{id}`

Body gồm đủ 3 trường của `CreateBus` **cộng thêm `status`**:

```json
{ "licensePlate": "29B-123.45", "busType": "Xe buýt 45 chỗ", "capacity": 45, "status": "Maintenance" }
```

| Trường | Bắt buộc | Ràng buộc |
|---|---|---|
| `licensePlate`, `busType`, `capacity` | ✅ | Như POST |
| `status` | — | `Active`, `Maintenance` hoặc `Inactive`. Bỏ trống = giữ nguyên trạng thái hiện tại |

- `status` ngoài ba mã trên → **400** `errors.status`.
- `Maintenance` dùng khi xe đi bảo dưỡng — xe không được gán vào chuyến mới cho tới khi PUT
  trở về `Active` (điều kiện `busId` của `POST /routes/{routeId}/trips` kiểm tra trạng thái `Active`).

#### `DELETE /buses/{id}` — xoá mềm

Chuyển xe về `Inactive`, **không xoá dữ liệu** khỏi CSDL — còn chuyến cũ tham chiếu tới xe
(khoá ngoại đặt `Restrict` theo quy ước A5 nên xoá cứng cũng bị CSDL chặn), và quy ước A4 cấm
thêm cột `IsDeleted`, nên dùng đúng cột trạng thái sẵn có.

Trả **200** kèm `Bus` đã ngừng khai thác. Xe đã `Inactive` gọi lại → **200** không báo lỗi
(idempotent — cùng lối `DELETE /routes/{id}`).

## Sơ đồ ghế — `/seat-layouts`

> ✅ Backend đã có (`SeatLayoutsController` — Phùng Duy Hoàng, story 2). Frontend gọi qua
> `frontend/src/api/seatLayoutApi.ts`, màn cấu hình là `SeatLayoutConfigPage`.
>
> **Toàn bộ endpoint dưới đây yêu cầu vai trò `Admin` hoặc `Manager`.** Người đã đăng nhập
> nhưng không đủ quyền nhận **403** kèm body `{ "message": "Bạn không có quyền truy cập tính năng này." }`.

### Sơ đồ ghế thuộc LOẠI XE, còn ghế thuộc XE

Đây là chỗ dễ hiểu nhầm nhất của mô hình, nên nói trước:

- Một `SeatLayout` gắn với **loại xe** (`busType`), không gắn với từng xe — hai xe cùng loại dùng
  chung một sơ đồ.
- Ghế thật (`Seats`, bảng riêng — không thuộc hợp đồng này) gắn với **từng xe** và được *sinh ra từ*
  sơ đồ. Xe ↔ sơ đồ nối bằng **so khớp chuỗi** `Buses.busType == SeatLayouts.busType`.
- Vì vậy response ở đây **không có mảng `seats`**: sơ đồ là hình học, không phải danh sách ghế.
  Muốn xem ghế thật của một chuyến → `GET /trips/{tripId}/seats` (ghế chưa sinh thì endpoint đó
  trả `floors: 0, seats: []`).

### Entity `SeatLayout`

| Trường | Kiểu | Mô tả |
|---|---|---|
| `id` | `string` (GUID) | Khoá chính |
| `busType` | `string` | Loại xe — "Xe buýt 45 chỗ". Duy nhất toàn hệ thống (khoá nghiệp vụ). **Không sửa được sau khi tạo** |
| `numberOfFloors` | `number` | Số tầng: 1 hoặc 2 |
| `rowsPerFloor` | `number` | Số hàng ghế mỗi tầng |
| `columnsPerRow` | `number` | Số ghế mỗi hàng |
| `totalSeats` | `number` | Tổng số ghế = `numberOfFloors × rowsPerFloor × columnsPerRow` — server tự tính, client **không** gửi |
| `vipSeatPositions` | `string[]` | Toạ độ các ghế VIP, dạng `"tầng-hàng-cột"` |
| `busesSeated` | `number` | **Chỉ có nghĩa ở response của POST/PUT** — số xe vừa được sinh ghế trong lượt lưu này (0 nếu không xe nào cần). GET luôn trả `0` |
| `createdAt` | `string` (ISO 8601, UTC) | Thời điểm tạo |
| `updatedAt` | `string \| null` | `null` khi sơ đồ chưa được sửa lần nào |

```json
// Ví dụ SeatLayout
{
  "id": "0f3d2b7a-0000-0000-0000-000000000000",
  "busType": "Xe buýt 45 chỗ",
  "numberOfFloors": 1,
  "rowsPerFloor": 9,
  "columnsPerRow": 5,
  "totalSeats": 45,
  "vipSeatPositions": ["1-1-1", "1-1-2"],
  "busesSeated": 0,
  "createdAt": "2026-10-12T08:00:00Z",
  "updatedAt": null
}
```

### Quy ước toạ độ và mã ghế — chốt tại đây

`docs/26-csdl-so-do-ghe.md` mục "Việc còn lại" để ngỏ hai điểm này; hợp đồng chốt:

- **Đếm từ 1.** `floor`, `rowIndex`, `columnIndex` đều bắt đầu từ **1**, cả khi hiển thị lẫn khi
  gửi lên. Màn hình vẽ mảng 0-based thì phải `+1` trước khi so với `vipSeatPositions`.
- **Toạ độ VIP** là chuỗi `"tầng-hàng-cột"` — ví dụ `"1-1-1"`, `"2-9-5"`.
- **Mã ghế** (`seatNumber` trong bảng `Seats`) đánh theo hàng: hàng 1 = chữ `A`, cột 1 = số `1`
  → `A1`, `A2`, … Ghế hàng 9 cột 5 là `I5`.
- **Xe hai tầng** thêm tiền tố `T{floor}-`: tầng 1 ra `T1-A1`, tầng 2 ra `T2-A1`. Xe một tầng
  **không** có tiền tố. Bắt buộc phải vậy vì `(busId, seatNumber)` là unique — thiếu tiền tố thì
  hai tầng trùng mã nhau ngay từ ghế đầu tiên.

### Lưu sơ đồ là ghế có ngay (tự sinh)

Không có endpoint riêng để "sinh ghế", và không có nút riêng trên màn hình. **Lưu sơ đồ là sinh
ghế** — vì một sơ đồ không có ghế thật thì hành khách vẫn mở ra màn chọn ghế trống, đúng cái lỗi
mà tính năng này sinh ra để chặn.

Khi `POST`:

1. Tạo `SeatLayout`.
2. Tìm mọi xe có `busType` **đúng bằng** loại xe của sơ đồ.
3. Xe nào **chưa có ghế nào** → sinh đủ `totalSeats` dòng `Seat` cho xe đó.
4. Xe đã có ghế → **không đụng tới** (ghế đang phục vụ giữ chỗ/bán vé).

`busesSeated` trong response cho biết bước 3 đã sinh cho bao nhiêu xe.

Khi `PUT` (sửa sơ đồ):

| Tình huống | Kết quả |
|---|---|
| Hình học và VIP **không đổi** | Chỉ sinh bù cho xe còn thiếu ghế (đúng lối POST bước 3) — đây là cách "gắn" xe mới thêm vào đội |
| Hình học/VIP **có đổi**, chưa ghế nào của loại xe đó bị giữ chỗ hoặc bán | Xoá ghế cũ của các xe đó rồi sinh lại theo lưới mới |
| Hình học/VIP **có đổi**, đã có ghế bị giữ chỗ hoặc bán | **409** — xem bên dưới |

> **Vì sao 409 chứ không sinh đè?** Ghế là thứ phiên giữ chỗ và vé trỏ tới bằng khoá ngoại
> `Restrict` (`SeatHolds.SeatId`, `Tickets.SeatId` — quy ước A5). Xoá một ghế đã có vé là làm vé
> mất chỗ ngồi, và CSDL sẽ chặn ngay ở tầng dưới. Nên API từ chối trước khi chạm vào dữ liệu:
> `{ "message": "Sơ đồ đang được dùng để giữ chỗ hoặc bán vé — không sửa hình học được nữa." }`.

> ⚠️ **Sức chứa xe phải khớp lưới ghế.** Trước khi lưu, server kiểm tra mọi xe cùng `busType` có
> `capacity == totalSeats`; lệch thì **400** với `message` nói rõ phép tính (*"Xe 20B123789 có sức
> chứa 45 nhưng lưới ghế cho ra 24 ghế"*). Lỗi này **không gắn vào ô nhập nào** (`errors` rỗng) vì
> cả hai phía đều sửa được: sức chứa ở màn Quản lý xe, hoặc lưới ghế ở đây. Thiếu phép kiểm này
> thì một xe 45 chỗ có thể mang lưới 4 ghế, và màn chọn ghế sẽ hiện đúng 4 ghế cho khách.
>
> Đổi `busType` của xe ở `PUT /buses/{id}` **không** tự xoá ghế đã sinh của xe đó — ghế vẫn thuộc
> xe, và vẫn trỏ về sơ đồ cũ.

### Endpoints

| Method | Endpoint | Mô tả | Body | Trả về |
|---|---|---|---|---|
| GET | `/seat-layouts` | Danh sách sơ đồ (lọc theo loại xe) | — | `SeatLayout[]` |
| GET | `/seat-layouts/{id}` | Chi tiết một sơ đồ | — | `SeatLayout` |
| POST | `/seat-layouts` | Thêm sơ đồ **+ sinh ghế cho xe cùng loại** | `CreateSeatLayout` | `SeatLayout` (201) |
| PUT | `/seat-layouts/{id}` | Sửa sơ đồ **+ đồng bộ lại ghế** | `UpdateSeatLayout` | `SeatLayout` |
| DELETE | `/seat-layouts/{id}` | Xoá sơ đồ (chỉ khi chưa xe nào có ghế) | — | 204 No Content |

#### `GET /seat-layouts`

| Tham số | Kiểu | Mặc định | Mô tả |
|---|---|---|---|
| `search` | `string` | — | Tìm theo loại xe — **không phân biệt hoa thường** |

Trả mảng `SeatLayout[]` sắp theo `busType` tăng dần, **không phân trang**: số sơ đồ bằng số loại
xe trong đội, chỉ vài dòng.

#### `POST /seat-layouts`

```json
// CreateSeatLayout — body
{
  "busType": "Xe buýt 45 chỗ",
  "numberOfFloors": 1,
  "rowsPerFloor": 9,
  "columnsPerRow": 5,
  "vipSeatPositions": ["1-1-1", "1-1-2"]
}
```

| Trường | Bắt buộc | Ràng buộc |
|---|---|---|
| `busType` | ✅ | 2–50 ký tự, duy nhất toàn hệ thống |
| `numberOfFloors` | ✅ | 1 hoặc 2 |
| `rowsPerFloor` | ✅ | 1–26 (26 = số chữ cái `A`–`Z` dùng đánh mã ghế). Màn hình giới hạn chặt hơn: 13 |
| `columnsPerRow` | ✅ | 1–10. Màn hình giới hạn 6 |
| `vipSeatPositions` | — | Mảng toạ độ `"tầng-hàng-cột"` nằm trong lưới và không trùng nhau. Bỏ trống = không có ghế VIP |

`totalSeats` vượt 200 (trần sức chứa xe của `/buses`) → **400**. Toạ độ VIP ngoài lưới →
**400** `errors.vipSeatPositions`.

Lỗi thường gặp: trùng loại xe → **400** `errors.busType` · lưới ghế lệch sức chứa xe → **400**
`message` (xem khối ⚠️ bên trên).

> **Vì sao trùng loại xe trả 400 mà không phải 409?** Cùng lý do trùng biển số ở `/buses` và trùng
> mã tuyến ở `/routes`: `busType` do quản lý nhập tay vào ô form, frontend gắn thẳng lỗi vào ô
> input. Ràng buộc unique ở CSDL là lớp chặn cuối cho hai request song song cùng loại xe.

Trả **201** kèm header `Location` tới `GET /seat-layouts/{id}`, body là `SeatLayout` vừa tạo với
`busesSeated` = số xe vừa được sinh ghế.

#### `PUT /seat-layouts/{id}`

Body **không có `busType`** — đó là khoá nghiệp vụ, đổi nó là xoá-rồi-tạo:

```json
{ "numberOfFloors": 1, "rowsPerFloor": 9, "columnsPerRow": 5, "vipSeatPositions": [] }
```

Ràng buộc từng trường như POST. `updatedAt` được cập nhật. Trả **200** kèm `SeatLayout` với
`busesSeated` = số xe vừa được sinh (hoặc sinh lại) ghế. Sơ đồ không tồn tại → **404**.

#### `DELETE /seat-layouts/{id}`

Xoá cứng, trả **204**. **409** khi còn ghế nào trỏ về sơ đồ này (ghế là dữ liệu nghiệp vụ — cùng
tinh thần `Restrict` của quy ước A5), `message` nêu rõ còn bao nhiêu ghế. Muốn xoá thì phải xử lý
ghế trước. Sơ đồ không tồn tại → **404**.

## Hồ sơ tài xế — `/drivers`

> ✅ Backend đã có (`DriversController` — Trần Trung Hiếu, story 14). Frontend chưa có module gọi
> API này — màn hình hồ sơ tài xế sẽ dựng theo khuôn `frontend/src/api/busApi.ts`.
>
> **Toàn bộ endpoint dưới đây yêu cầu vai trò `Admin` hoặc `Manager`.** Người đã đăng nhập
> nhưng không đủ quyền nhận **403** kèm body `{ "message": "Bạn không có quyền truy cập tính năng này." }`.

### Tài xế là tài khoản `Users` mang vai trò `Driver` — không có bảng riêng

Backlog ghi task migrate của story 14 là *"Migrate bảng Buses, Drivers, TripAssignments"*, nhưng
quy ước **A8.4** chốt đúng **4 vai trò** (Admin, Manager, Driver, Passenger) và **A9** không có
bảng `Drivers` lẫn `TripAssignments`. Hệ quả trực tiếp lên hợp đồng API:

- **Không có bảng `Drivers`.** Tài xế là một `User` mang vai trò `Driver` — `Trips.DriverId`
  trỏ thẳng vào `Users` (migration của Dăm, PR #52). API này là lớp CRUD hồ sơ chạy trên bảng
  `Users`, luôn gắn vai trò `Driver`, không bao giờ chạm vai trò khác.
- **Không có Phụ xe.** A8.4 ghi rõ *"Không có Phụ xe"* — US 15 nói "phụ xe/tài xế" vẫn thoả với
  một người làm. Bảng `Trips` cũng không có cột phụ xe nên không có gì để quản lý ở đây.
- ⚠️ **Bằng lái đang chờ migration của Dăm.** Task ghi *"hồ sơ tài xế + bằng lái"*, nhưng `Users`
  chưa có cột nào để lưu thông tin bằng lái và chỉ Vàng Thị Dăm được chạy migration (quy ước A7).
  Hợp đồng này tạm thời chưa có trường bằng lái — khi Dăm thêm cột (dự kiến `licenseNumber`,
  `licenseType`, `licenseExpiry`) thì bổ sung vào `Driver` và hai body POST/PUT, không phá
  endpoint hiện có.
- ⚠️ **Nhật ký kiểm toán ghi `target` dạng `Drivers:<id>`** — `AuditLogMiddleware` suy tên bảng từ
  route (`api/drivers/{id}` → "Drivers") chứ bảng thật là `Users`. Cố ý giữ nguyên: đọc nhật ký
  thấy "Drivers" đúng nghiệp vụ hơn, và sửa middleware là sửa file của người khác.

### Entity `Driver`

| Trường | Kiểu | Mô tả |
|---|---|---|
| `id` | `string` (GUID) | Khoá chính — là `id` của dòng `Users` |
| `fullName` | `string` | Họ và tên |
| `phoneNumber` | `string` | SĐT đăng nhập, duy nhất toàn hệ thống |
| `email` | `string \| null` | Không bắt buộc — tài xế vẫn đăng nhập được bằng SĐT |
| `isActive` | `boolean` | `true` = còn hoạt động, `false` = đã bị khóa (xoá mềm) |
| `createdAt` | `string` (ISO 8601, UTC) | Thời điểm tạo hồ sơ |

```json
// Ví dụ Driver
{
  "id": "3f2a1b0c-0000-0000-0000-000000000000",
  "fullName": "Nguyễn Văn An",
  "phoneNumber": "0912345678",
  "email": "an@gmail.com",
  "isActive": true,
  "createdAt": "2026-09-30T03:15:00Z"
}
```

### Endpoints

| Method | Endpoint | Mô tả | Body | Trả về |
|---|---|---|---|---|
| GET | `/drivers` | Danh sách tài xế + tìm kiếm + lọc trạng thái + phân trang | — | `DriverListResponse` |
| GET | `/drivers/{id}` | Chi tiết hồ sơ một tài xế | — | `Driver` |
| POST | `/drivers` | Tạo hồ sơ tài xế mới (tài khoản vai trò Driver) | `CreateDriver` | `Driver` (201) |
| PUT | `/drivers/{id}` | Sửa hồ sơ | `UpdateDriver` | `Driver` |
| DELETE | `/drivers/{id}` | **Xoá mềm** — khóa tài khoản | — | `Driver` |
| GET | `/drivers/{id}/trips` | **Ca làm việc** — danh sách chuyến tài xế được phân công | — | `DriverTripListResponse` |

#### `GET /drivers`

Tham số query (đều không bắt buộc):

| Tham số | Kiểu | Mặc định | Mô tả |
|---|---|---|---|
| `search` | `string` | — | Tìm theo họ tên, SĐT hoặc email — **không phân biệt hoa thường** |
| `isActive` | `boolean` | — | `true` = còn hoạt động, `false` = đã khóa. Bỏ trống = lấy cả hai |
| `page` | `number` | `1` | Trang, tính từ 1 |
| `pageSize` | `number` | `10` | Số dòng mỗi trang, tối đa **100** |

```json
// DriverListResponse — ví dụ GET /drivers?page=1&pageSize=10
{
  "items": [ /* Driver[] của trang hiện tại */ ],
  "total": 42,
  "page": 1,
  "pageSize": 10
}
```

- Chỉ trả về tài khoản **mang vai trò Driver** — vai trò chính hoặc một vai trò trong bảng nối
  `UserRoles` (tài khoản vừa Driver vừa Passenger vẫn là tài xế).
- `total` là tổng số dòng khớp bộ lọc (không phải số dòng trong `items`) — dùng để vẽ phân trang.
- Thứ tự sắp xếp: `createdAt` giảm dần, tạo cùng lúc xếp theo `id` để phân trang ổn định.
- `page` hoặc `pageSize` ngoài khoảng hợp lệ → **400**.

#### `POST /drivers`

```json
// CreateDriver — body
{
  "fullName": "Nguyễn Văn An",
  "phoneNumber": "0912345678",
  "email": "an@gmail.com",
  "password": "matkhau123"
}
```

| Trường | Bắt buộc | Ràng buộc |
|---|---|---|
| `fullName` | ✅ | 2–200 ký tự |
| `phoneNumber` | ✅ | SĐT di động Việt Nam: 10 số, bắt đầu `0[35789]` |
| `email` | — | Đúng định dạng, tối đa 256 ký tự |
| `password` | ✅ | Ít nhất 8 ký tự, gồm **cả chữ và số** — giống hệt form đăng ký |

Không có trường `roleCode` (khác `CreateAdminUser`): hồ sơ tạo từ đây **luôn** là tài xế — màn
hình hồ sơ tài xế không cần biết mã vai trò, và gửi vai trò khác vào đây chỉ là lỗi chờ xảy ra.
Muốn nâng một hành khách thành tài xế thì dùng `PUT /admin/users/{id}/roles` (việc của Admin).

Lỗi thường gặp: trùng SĐT → **400** `errors.phoneNumber` · trùng email → **400** `errors.email`.

> **Vì sao trùng SĐT trả 400 mà không phải 409?** Cùng lý do form đăng ký và `/admin/users`: SĐT
> gõ tay vào ô form, frontend gắn thẳng lỗi vào ô input.

#### `PUT /drivers/{id}`

Body gồm `fullName`, `phoneNumber`, `email` — cùng ràng buộc như POST. **Không** đổi mật khẩu
(nghiệp vụ riêng), **không** đổi trạng thái (khóa tài khoản có `DELETE` riêng).

#### `DELETE /drivers/{id}` — xoá mềm

Khóa tài khoản (`isActive = false`), **không xoá dữ liệu** khỏi CSDL — chuyến đã chạy vẫn tham
chiếu tới tài xế qua `Trips.DriverId`, và quy ước A4 cấm thêm cột `IsDeleted`.

- Trả **200** kèm `Driver` đã khóa — giống `DELETE /admin/users/{id}`.
- Tài xế đã khóa gọi lại → **200** không báo lỗi (idempotent).
- **Tự khóa tài khoản của chính mình → 409** `Không thể tự khóa tài khoản của chính mình` —
  cùng chốt an toàn của `/admin/users`: `JwtMiddleware` đọc lại `IsActive` ở mọi request nên tự
  khóa xong là không còn đường nào mở lại.

#### `GET /drivers/{id}/trips` — ca làm việc

Tham số query giống `GET /routes/{routeId}/trips` — cùng tên `from`, `to`, `status`, `page`,
`pageSize` nên frontend không phải nói hai thứ tiếng:

| Tham số | Kiểu | Mặc định | Mô tả |
|---|---|---|---|
| `from` | `string` (ISO 8601 có múi giờ) | — | Chỉ lấy chuyến khởi hành **từ** thời điểm này, tính luôn mốc |
| `to` | `string` (ISO 8601 có múi giờ) | — | Chỉ lấy chuyến khởi hành **tới** thời điểm này, tính luôn mốc |
| `status` | `string` | — | `Scheduled` / `Running` / `Completed` / `Cancelled`. Bỏ trống = lấy cả bốn |
| `page` | `number` | `1` | Trang, tính từ 1 |
| `pageSize` | `number` | `10` | Số dòng mỗi trang, tối đa **100** |

```json
// DriverTripListResponse — ví dụ GET /drivers/{id}/trips?page=1&pageSize=10
{
  "items": [
    {
      "id": "6b3e8d12-0000-0000-0000-000000000000",
      "routeId": "3f2a1b0c-0000-0000-0000-000000000000",
      "routeCode": "01",
      "routeName": "Bến Thành — Chợ Lớn",
      "busId": "1c9a4f05-0000-0000-0000-000000000000",
      "busLicensePlate": "29B-123.45",
      "departureTime": "2026-10-01T05:00:00Z",
      "arrivalTime": "2026-10-01T06:30:00Z",
      "status": "Scheduled",
      "createdAt": "2026-09-30T03:15:00Z",
      "updatedAt": null
    }
  ],
  "total": 7,
  "page": 1,
  "pageSize": 10
}
```

- `items[]` là các dòng `Trips` có `driverId` trỏ vào tài xế này, kèm sẵn `routeCode`/`routeName`
  để màn hình hiển thị ca làm việc mà không phải gọi thêm API tuyến — cùng lối `RouteStop` kèm
  sẵn tên trạm.
- **Không** trả 4 trường vị trí (`currentStopId`, `currentLat`, `currentLng`,
  `positionUpdatedAt`): vị trí là chuyện của nhóm story theo dõi thời gian thực (Sprint 3),
  cùng lối `TripDetail`.
- Thứ tự sắp xếp: `departureTime` **tăng dần** — màn hình đọc như thời gian biểu của tài xế,
  trùng giờ xếp tiếp theo `id` để phân trang ổn định.
- Tài xế chưa được phân công chuyến nào → `items: []`, `total: 0`, **không** phải 404.
- `to` sớm hơn `from` → **400** `errors.to`.
- `status` không khớp mã nào → danh sách rỗng (không báo lỗi — cùng lối bộ lọc của
  `/routes/{routeId}/trips`).

#### `{id}` phải là tài xế

Mọi endpoint nhận `{id}` đều kiểm tra tài khoản tồn tại **và** mang vai trò Driver. Không tồn tại
hoặc không phải tài xế → **404** `Không tìm thấy tài xế`.

> **Vì sao không phải tài xế cũng trả 404?** Người gọi đang hỏi *"tài xế có id này"* — id không
> phải tài xế thì tài xế đó không tồn tại. Trả 200 kèm hồ sơ một hành khách là rò rỉ thông tin
> qua một endpoint không dành cho việc đó (quản lý cần xem hồ sơ hành khách thì đã có
> `/admin/users`, chỉ dành cho Admin).

### Vì sao có `/drivers` khi đã có `/admin/users`

`/admin/users` là công cụ **quản trị toàn hệ thống**, chỉ Admin dùng, quản lý mọi vai trò kể cả
đổi vai trò của chính mình. `/drivers` phục vụ nghiệp vụ **điều hành** của story 14 (*"Là quản lý,
tôi muốn gán xe buýt và tài xế/phụ xe cho từng chuyến chạy cụ thể"*) nên Admin và **Manager** đều
dùng được — cùng lối `/buses`. Nó chỉ nhìn thấy tài xế, không có endpoint nào đổi vai trò hay
đổi mật khẩu, nên một Manager không thể vô tình hạ Admin thành Hành khách hay tạo ra một Admin
mới. Đây cũng là chỗ treo danh sách ca làm việc (`GET /drivers/{id}/trips`), thứ `/admin/users`
không có.

## Lịch trình chạy xe — `/routes/{routeId}/trips`

> ✅ Backend đã có (`RouteTripsController` — Trần Trung Hiếu, story 13). Frontend chưa có module gọi
> API này — màn hình lập lịch trình sẽ dựng theo khuôn `frontend/src/api/fareApi.ts`.
>
> **Toàn bộ endpoint dưới đây yêu cầu vai trò `Admin` hoặc `Manager`.** Người đã đăng nhập
> nhưng không đủ quyền nhận **403** kèm body `{ "message": "Bạn không có quyền truy cập tính năng này." }`.

Lịch trình theo tuyến **không** tách thành bảng mẫu `Schedule` (quy ước A8.3): mỗi chuyến là một
dòng bảng `Trips`, còn lịch trình định kỳ được lập bằng API **sinh chuyến hàng loạt** — quản lý
chọn tuyến + xe + mốc bắt đầu (**ngày áp dụng** + giờ khởi hành đầu tiên) + mốc kết thúc +
**tần suất** (phút) → hệ thống sinh N dòng `Trips` cách đều tần suất. Sửa một chuyến lẻ thì dùng
các endpoint CRUD phía dưới.

### Entity `Trip`

| Trường | Kiểu | Mô tả |
|---|---|---|
| `id` | `string` (GUID) | Khoá chính |
| `routeId` | `string` (GUID) | Tuyến của chuyến |
| `busId` | `string` (GUID) | Xe chạy chuyến này |
| `busLicensePlate` | `string` | Biển số xe — kèm sẵn để màn hình lập lịch trình hiển thị mà không phải gọi thêm API xe |
| `driverId` | `string \| null` (GUID) | Tài xế được phân công. `null` = chưa phân công — chuyến sinh hàng loạt ra đời ở trạng thái này, việc gán làm sau bằng `PATCH .../trips/driver-assignment` |
| `driverName` | `string \| null` | Họ tên tài xế — kèm sẵn cùng lý do `busLicensePlate`. `null` đúng khi và chỉ khi `driverId` là `null` |
| `departureTime` | `string` (ISO 8601, UTC) | Giờ khởi hành thực tế của chuyến |
| `arrivalTime` | `string \| null` (ISO 8601, UTC) | Giờ dự kiến tới bến cuối. `null` khi chưa chốt |
| `status` | `string` | `Scheduled` = đã sinh, chưa chạy · `Running` = đang chạy · `Completed` = đã chạy xong · `Cancelled` = đã huỷ (lưu dạng chuỗi — quy ước A3) |
| `currentStopId` | `string \| null` | Trạm gần nhất xe vừa đi qua — chỉ có nghĩa khi xe đang chạy (quy ước A8.6) |
| `currentLat` / `currentLng` | `number \| null` | Vị trí hiện tại của xe |
| `positionUpdatedAt` | `string \| null` | Lần cuối vị trí được cập nhật |
| `createdAt` | `string` (ISO 8601, UTC) | Thời điểm tạo |
| `updatedAt` | `string \| null` | `null` khi chưa sửa lần nào |

Bốn trường vị trí (`currentStopId`, `currentLat`, `currentLng`, `positionUpdatedAt`) **có** cột trên
bảng `Trips` (quy ước A8.6) và **có** trong `Trip` của mục này, nhưng **không** nằm trong
`TripDetail` của `GET /trips/{id}` — vì story 13 là *lập lịch trình*, còn vị trí là chuyện của nhóm
story theo dõi thời gian thực (Sprint 3). Xem ghi chú *"Vì sao KHÔNG trả vị trí hiện tại của xe"* ở
mục "Chuyến xe — `/trips`" phía trên.

> **Vì sao trả kèm `driverId`/`driverName`?** Màn hình "Phân công điều xe" phải biết chuyến nào
> **chưa có tài xế** để lọc ra và gán hàng loạt, và phải hiện được tên tài xế mà không gọi thêm
> `GET /drivers/{id}` cho từng dòng — cùng lối `busLicensePlate` kèm sẵn biển số. Không có bảng
> `Drivers`/`TripAssignments` (quy ước A9): tài xế là `User` mang vai trò `Driver`, `Trips.DriverId`
> trỏ thẳng vào `Users` (quy ước A8.4), nên `driverName` đọc từ `Users.FullName`. Chuyến **chưa**
> phân công trả `null` ở cả hai trường — đây là trạng thái hợp lệ, không phải lỗi.
>
> ⚠️ Đổi hình dạng `Trip` là đổi hình dạng API: trường mới đã **thêm vào cuối**, không sửa/xoá
> trường nào, nên client cũ không vỡ.

```json
// Ví dụ Trip
{
  "id": "6b3e8d12-0000-0000-0000-000000000000",
  "routeId": "3f2a1b0c-0000-0000-0000-000000000000",
  "busId": "1c9a4f05-0000-0000-0000-000000000000",
  "busLicensePlate": "29B-123.45",
  "driverId": "8d1b7c34-0000-0000-0000-000000000000",
  "driverName": "Nguyễn Văn An",
  "departureTime": "2026-10-01T05:00:00Z",
  "arrivalTime": "2026-10-01T06:30:00Z",
  "status": "Scheduled",
  "currentStopId": null,
  "currentLat": null,
  "currentLng": null,
  "positionUpdatedAt": null,
  "createdAt": "2026-09-30T03:15:00Z",
  "updatedAt": null
}
```

### Endpoints

| Method | Endpoint | Mô tả | Body | Trả về |
|---|---|---|---|---|
| GET | `/routes/{routeId}/trips` | Danh sách chuyến của tuyến — lọc theo khoảng giờ khởi hành + trạng thái + phân trang | — | `TripListResponse` |
| GET | `/routes/{routeId}/trips/{id}` | Chi tiết một chuyến | — | `Trip` |
| POST | `/routes/{routeId}/trips` | Thêm một chuyến lẻ | `CreateTrip` | `Trip` (201) |
| PUT | `/routes/{routeId}/trips/{id}` | Sửa xe / giờ chạy / trạng thái | `UpdateTrip` | `Trip` |
| DELETE | `/routes/{routeId}/trips/{id}` | **Huỷ chuyến** — chuyển về `Cancelled` | — | `Trip` |
| POST | `/routes/{routeId}/trips/generate` | **Sinh chuyến hàng loạt** theo tần suất (quy ước A8.3) | `GenerateTrips` | `GenerateTripsResponse` |
| PATCH | `/routes/{routeId}/trips/driver-assignment` | **Gán một tài xế cho nhiều chuyến** cùng lúc (màn hình "Phân công điều xe") | `AssignDriverToTrips` | `DriverAssignmentResponse` |

#### `GET /routes/{routeId}/trips`

Tham số query (đều không bắt buộc):

| Tham số | Kiểu | Mặc định | Mô tả |
|---|---|---|---|
| `from` | `string` (ISO 8601 có múi giờ) | — | Chỉ lấy chuyến khởi hành **từ** thời điểm này, tính luôn mốc |
| `to` | `string` (ISO 8601 có múi giờ) | — | Chỉ lấy chuyến khởi hành **tới** thời điểm này, tính luôn mốc |
| `status` | `string` | — | Lọc theo trạng thái: `Scheduled` / `Running` / `Completed` / `Cancelled`. Bỏ trống = lấy cả bốn |
| `page` | `number` | `1` | Trang, tính từ 1 |
| `pageSize` | `number` | `10` | Số dòng mỗi trang, tối đa **100** |

```json
// TripListResponse — ví dụ GET /routes/{routeId}/trips?page=1&pageSize=10
{
  "items": [ /* Trip[] của trang hiện tại */ ],
  "total": 42,
  "page": 1,
  "pageSize": 10
}
```

- `total` là tổng số dòng khớp bộ lọc (không phải số dòng trong `items`) — dùng để vẽ phân trang.
- Thứ tự sắp xếp: `departureTime` **tăng dần** — màn hình lập lịch trình đọc như một cuốn thời
  gian biểu, khác các màn hình danh sách quản trị xếp theo `createdAt` giảm dần. Hai chuyến trùng
  giờ khởi hành xếp tiếp theo `id` để phân trang ổn định.
- Tuyến chưa có chuyến nào → mảng rỗng, **không** phải 404.
- Tuyến không tồn tại → **404**.
- `to` sớm hơn `from` → **400** `errors.to`.
- `status` không khớp mã nào → danh sách rỗng (không báo lỗi — cùng lối bộ lọc `status` của
  `/routes`).
- `page` hoặc `pageSize` ngoài khoảng hợp lệ → **400**.

#### `POST /routes/{routeId}/trips`

```json
// CreateTrip — body
{
  "busId": "1c9a4f05-0000-0000-0000-000000000000",
  "departureTime": "2026-10-01T12:00:00+07:00",
  "arrivalTime": "2026-10-01T13:30:00+07:00"
}
```

| Trường | Bắt buộc | Ràng buộc |
|---|---|---|
| `busId` | ✅ | GUID của xe có thật và đang ở trạng thái `Active` |
| `departureTime` | ✅ | ISO 8601 **có kèm múi giờ** (ví dụ `+07:00` hoặc `Z`) — server quy về UTC khi lưu |
| `arrivalTime` | — | Phải sau `departureTime` |

Chuyến mới luôn ở trạng thái `Scheduled` — muốn đổi trạng thái thì dùng PUT, cùng lối tuyến mới
luôn `Active`. Lỗi thường gặp: tuyến không tồn tại → **404** · xe không tồn tại → **404** · xe
không ở trạng thái khai thác → **409** · `arrivalTime` không sau `departureTime` → **400**
`errors.arrivalTime` · trùng khung giờ → **409** · vượt trần chuyến/ngày → **400**
`errors.departureTime` — hai kiểm tra sau xem mục "Hai kiểm tra khi tạo lịch trình" bên dưới.

#### `PUT /routes/{routeId}/trips/{id}`

Body gồm đủ 2 trường bắt buộc của `CreateTrip` **cộng thêm `status`**:

```json
{
  "busId": "1c9a4f05-0000-0000-0000-000000000000",
  "departureTime": "2026-10-01T12:15:00+07:00",
  "arrivalTime": "2026-10-01T13:45:00+07:00",
  "status": "Scheduled"
}
```

| Trường | Bắt buộc | Ràng buộc |
|---|---|---|
| `busId`, `departureTime` | ✅ | Như POST |
| `arrivalTime` | — | Như POST, nhưng **bỏ trống = bỏ hẳn** (gán null) — đây là PUT sửa toàn phần |
| `status` | — | Một trong 4 mã ở bảng trên. Bỏ trống = giữ nguyên trạng thái hiện tại |

- `status` ngoài 4 mã → **400** `errors.status`.
- Đây cũng là cách **mở lại chuyến đã huỷ**: PUT với `status: "Scheduled"`.
- Đổi xe thì xe mới phải đang `Active` → nếu không **409**. Giữ nguyên xe thì không kiểm tra lại:
  xe chuyển sang bảo dưỡng sau khi sinh chuyến không chặn việc sửa giờ của chuyến đã tồn tại.
- **Sửa `departureTime` / `arrivalTime` sẽ báo cho hành khách đã mua vé `Paid` của chuyến** — mỗi
  lần giờ **thực sự đổi** là một thông báo in-app + một sự kiện SignalR: `TripDelayed` khi mọi mốc
  đổi đều lùi ra sau, `TripScheduleChanged` khi có mốc sớm hơn hoặc `arrivalTime` đổi giữa null và
  có giá trị. Chuyến đã lưu ở `Cancelled`/`Completed` thì im lặng. Chỉ đổi xe / chỉ đổi `status`
  hoặc PUT lại đúng dữ liệu cũ thì không phát. Chi tiết ở mục **"Khi nào sự kiện được phát"** của
  phần thông báo.
- **Huỷ chuyến bằng `DELETE` không phát thông báo** — xem ghi chú ở mục thông báo.

#### `DELETE /routes/{routeId}/trips/{id}` — huỷ chuyến (xoá mềm)

Chuyển chuyến về `Cancelled`, **không xoá dữ liệu** khỏi CSDL — vé đã bán vẫn tham chiếu tới, và
quy ước A4 cấm thêm cột `IsDeleted` nên dùng đúng cột trạng thái sẵn có.

- Trả **200** kèm `Trip` đã huỷ — giống `DELETE /routes/{id}` trả về tuyến đã ngừng khai thác.
- Chuyến đã `Cancelled` gọi lại → **200** không báo lỗi (idempotent).
- Chuyến đã `Completed` → **409** — huỷ chuyến đã chạy xong là viết lại lịch sử, chặn hẳn.

#### `POST /routes/{routeId}/trips/generate` — sinh chuyến hàng loạt (quy ước A8.3)

Đây chính là API "lập lịch trình": **ngày áp dụng** là phần ngày của `startTime`, **giờ khởi hành**
là phần giờ của `startTime` và từng bước `frequencyMinutes`, **tần suất** là khoảng cách giữa hai
chuyến liên tiếp tính bằng phút.

```json
// GenerateTrips — body
{
  "busId": "1c9a4f05-0000-0000-0000-000000000000",
  "startTime": "2026-10-01T05:00:00+07:00",
  "endTime": "2026-10-01T22:00:00+07:00",
  "frequencyMinutes": 15
}
```

| Trường | Bắt buộc | Ràng buộc |
|---|---|---|
| `busId` | ✅ | Như POST |
| `startTime` | ✅ | Mốc bắt đầu — giờ khởi hành của chuyến đầu tiên |
| `endTime` | ✅ | Mốc kết thúc — chuyến cuối được sinh **không vượt quá** mốc này. Phải sau `startTime` |
| `frequencyMinutes` | ✅ | Số nguyên từ **1** đến **1440** |

Chuyến đầu xuất phát đúng `startTime`, mỗi chuyến sau cách chuyến trước đúng `frequencyMinutes`
phút. Ví dụ body trên sinh 69 chuyến: 05:00, 05:15, …, 22:00.

Trả **200** kèm danh sách chuyến **vừa sinh** (không gồm chuyến cũ của tuyến):

```json
// GenerateTripsResponse
{ "items": [ /* Trip[] vừa sinh */ ], "total": 69 }
```

- Một lần gọi sinh **tối đa 500 chuyến**. Vượt → **400** `errors.endTime` kèm gợi ý thu hẹp khoảng
  hoặc tăng tần suất. Lịch trình nhiều ngày thì tách thành nhiều lần gọi, mỗi lần một ngày.
- Thao tác là **nguyên tử**: một chuyến trong dải bị trùng khung giờ thì **không chuyến nào**
  được tạo — không bao giờ sinh lịch trình dở dang.
- Lỗi thường gặp: tuyến không tồn tại → **404** · xe không tồn tại → **404** · xe không Active →
  **409** · `endTime` không sau `startTime` → **400** `errors.endTime` · `frequencyMinutes` ngoài
  khoảng 1..1440 → **400** `errors.frequencyMinutes` · trùng khung giờ với chuyến hiện có →
  **409** · vượt trần chuyến/ngày của tuyến → **400** `errors.endTime`.

#### `PATCH /routes/{routeId}/trips/driver-assignment` — gán tài xế hàng loạt

Phần **"xe"** của việc phân công điều xe đã có sẵn: đổi xe một chuyến là
`PUT /routes/{routeId}/trips/{id}` với `busId` mới. Endpoint này phụ trách phần **"tài xế"**, và làm
theo lô vì màn hình phân công chọn nhiều chuyến rồi gán một tài xế trong một thao tác.

> **Vì sao là `PATCH` chứ không phải `PUT`?** Đây là sửa **một phần** bản ghi chuyến (chỉ đổi
> `driverId`), không thay cả bản ghi như `PUT /routes/{routeId}/trips/{id}`.
>
> **Vì sao nằm dưới `/routes/{routeId}/trips`?** Đi song song với `POST .../trips/generate` (cũng là
> thao tác theo lô trên nhiều chuyến của một tuyến), và nhờ có `{routeId}` trên đường dẫn nên
> `AuditLogMiddleware` suy đúng tên bảng `Trips` cho dòng nhật ký — để ở `/trips/driver-assignment`
> thì middleware lấy đoạn cuối và ghi ra bảng `"DriverAssignment"` không tồn tại (giới hạn đã ghi ở
> `AuditLogMiddleware.ResolveTableName`).
>
> **"Phụ xe" không có ở đây:** quy ước A8.4 chốt đúng 4 vai trò, không có Phụ xe; bảng `Trips` cũng
> không có cột phụ xe. Tên task trong backlog nhắc "phụ xe" là chữ còn sót, không phải yêu cầu.

```json
// AssignDriverToTrips — body
{
  "tripIds": ["6b3e8d12-0000-0000-0000-000000000000", "7c4f9e23-0000-0000-0000-000000000000"],
  "driverId": "8d1b7c34-0000-0000-0000-000000000000"
}
```

| Trường | Bắt buộc | Ràng buộc |
|---|---|---|
| `tripIds` | ✅ | 1–200 GUID chuyến **của đúng tuyến này**, không trùng nhau |
| `driverId` | ✅ | GUID tài khoản **mang vai trò `Driver`** và đang hoạt động (`isActive = true`) |

Trả **200** kèm kết quả từng chuyến:

```json
// DriverAssignmentResponse
{
  "driverId": "8d1b7c34-0000-0000-0000-000000000000",
  "driverName": "Nguyễn Văn An",
  "assignedCount": 2,
  "items": [
    {
      "id": "6b3e8d12-0000-0000-0000-000000000000",
      "routeId": "3f2a1b0c-0000-0000-0000-000000000000",
      "busId": "1c9a4f05-0000-0000-0000-000000000000",
      "busLicensePlate": "29B-123.45",
      "driverId": "8d1b7c34-0000-0000-0000-000000000000",
      "driverName": "Nguyễn Văn An",
      "departureTime": "2026-10-01T05:00:00Z",
      "arrivalTime": "2026-10-01T06:30:00Z",
      "status": "Scheduled",
      "updatedAt": "2026-09-30T03:15:00Z",
      "conflicts": { "busConflicts": [], "driverConflicts": [] }
    }
  ]
}
```

- `assignedCount` = số chuyến **thực sự được đổi** tài xế trong lần gọi này (chuyến đã đúng tài xế
  đó từ trước không tính, và `updatedAt` của nó giữ nguyên).
- `items[]` cùng khuôn với response của `PATCH /trips/{id}/assignment` (API đổi xe/đổi tài xế khi có
  sự cố) để hai endpoint anh em không nói hai thứ tiếng.
- `conflicts` là **cảnh báo, không phải lỗi**: tài xế đang vướng chuyến khác trùng khung giờ thì
  chuyến đó hiện trong `driverConflicts` nhưng thao tác **vẫn thành công (200)** — luồng điều hành
  được phép cố ý chấp nhận trùng (ví dụ đổi tài xế giữa chừng khi có sự cố). Cùng triết lý với
  `PUT /routes/{routeId}/trips/{id}` cố ý không chặn trùng khung giờ.
- `busConflicts` luôn rỗng: endpoint này chỉ đổi tài xế, không soi vế xe — soi thêm sẽ dội lại những
  lần trùng xe có sẵn thành tiếng ồn không liên quan tới thao tác đang làm.
- `driverConflicts` gồm hai nguồn: các chuyến **đã có trong CSDL** mang tài xế này mà trùng khung
  giờ (qua `ITripConflictService` của story 14), và các chuyến **khác trong cùng lô** vừa gán trùng
  khung giờ với nhau. Gộp lại, mỗi chuyến chỉ xuất hiện một lần.
- Thao tác là **nguyên tử**: một chuyến trong lô không hợp lệ thì **không chuyến nào** được gán.
- Gọi lại y hệt lần hai → **200**, `assignedCount` bằng 0, `updatedAt` không đổi (idempotent).

Lỗi thường gặp:

| Tình huống | Kết quả |
|---|---|
| `tripIds` rỗng / nhiều hơn 200 / có GUID trùng nhau | **400** `errors.tripIds` |
| Thiếu `driverId` | **400** `errors.driverId` |
| Tuyến không tồn tại | **404** |
| Chuyến không tồn tại **hoặc thuộc tuyến khác** | **404** (cùng lối "routeId phải khớp" bên dưới) |
| Chuyến đã `Cancelled` hoặc `Completed` | **409** — gán tài xế cho chuyến đã huỷ/đã chạy xong là vô nghĩa |
| `driverId` không phải tài khoản mang vai trò `Driver` | **404** |
| Tài khoản tài xế đang bị khoá (`isActive = false`) | **409** |
| Trùng khung giờ với chuyến khác của tài xế | **200** kèm `conflicts.driverConflicts` — không chặn |

### Hai kiểm tra khi tạo lịch trình

Áp dụng cho cả `POST /routes/{routeId}/trips` lẫn `POST /routes/{routeId}/trips/generate`
(xem chi tiết lỗi ở từng endpoint phía trên):

**1. Trùng khung giờ → 409.** Khung giờ của một chuyến là khoảng [giờ khởi hành, giờ đến];
chuyến chưa có giờ đến thì coi là một mốc (chỉ chặn chuyến trùng đúng giờ khởi hành hoặc nằm
lọt trong khung giờ của chuyến kia). Chuyến mới không được chồng khung giờ với chuyến đang
hoạt động (`Scheduled`/`Running`) **cùng tuyến hoặc cùng xe** — một xe không thể chạy hai
chuyến cùng lúc. Chuyến nối đuôi (chuyến này đến đúng giờ chuyến kia khởi hành) không tính
là trùng. Chuyến đã `Cancelled` hoặc `Completed` không tính — chúng không chiếm chỗ trên
thời gian biểu nữa.

**2. Vượt sức chứa tuyến → 400.** Mỗi ngày (tính theo **UTC**, cùng lối bộ lọc của
`/audit-logs`) một tuyến chỉ có tối đa **200 chuyến đang hoạt động** — `Scheduled` và
`Running`, không tính `Cancelled`/`Completed`. Với generate, kiểm tra từng ngày bị lịch trình
chạm tới: tổng chuyến đã có + sắp sinh của ngày đó vượt 200 thì cả lần gọi bị chặn.

> **Vì sao trùng khung giờ trả 409 mà vượt trần trả 400?** Trùng khung giờ là xung đột với dữ
> liệu đang có — cùng loại trùng giá vé, trùng trạm (mục D2). Vượt trần là tham số của lịch
> trình nằm ngoài giới hạn cho phép — cùng loại trần 500 chuyến/lần ở generate.

> ⚠️ **`PUT /routes/{routeId}/trips/{id}` KHÔNG kiểm tra hai điều kiện này** — cố ý: task chỉ
> phủ "khi tạo lịch trình". Sửa giờ một chuyến đã có vẫn lách được kiểm tra trùng; nếu nhóm
> muốn chặn cả khi sửa thì bổ sung ở task sau (cùng một hàm kiểm tra, chỉ khác điểm gọi).
>
> ➡️ Task sau đó đã làm một phần: **`PATCH /trips/{id}/assignment`** (đổi xe/tài xế khi có sự cố —
> mục "Chuyến xe — `/trips`") gọi đúng phép kiểm tra trùng lịch điều xe này, nhưng chỉ **cảnh
> báo** trong `conflicts` chứ không chặn — chuyến có sự cố cần đổi được ngay. `PUT` giữ nguyên
> hành vi cũ (không kiểm tra gì).

#### `routeId` phải khớp

`GET`/`PUT`/`DELETE` trên `/routes/{routeId}/trips/{id}` đều kiểm tra chuyến có **thuộc đúng**
tuyến đó không. Chuyến của tuyến khác → **404**, không phải 200.

`PATCH /routes/{routeId}/trips/driver-assignment` cũng vậy: **một** chuyến trong `tripIds` thuộc
tuyến khác là **cả lô** bị chặn với **404** — không âm thầm bỏ qua chuyến lạ rồi gán phần còn lại.

#### Giờ gửi lên phải kèm múi giờ

Mọi trường thời gian trong body (`departureTime`, `arrivalTime`, `startTime`, `endTime`) nhận
ISO 8601 **có kèm múi giờ** — ví dụ `2026-10-01T05:00:00+07:00` hoặc `...Z`. Server quy về UTC khi
lưu (cột `timestamptz` — quy ước A3) và mọi trường thời gian trả về đều là UTC. Chuỗi không kèm
múi giờ bị hiểu là giờ máy chủ — frontend luôn gửi kèm offset để không phụ thuộc vào máy chủ.

## Vé tháng — `/monthly-passes`

> ✅ Backend đã đủ cả ba phần của story 16: đăng ký vé tháng `POST /monthly-passes` (Trần Trung
> Hiếu), gia hạn `POST /monthly-passes/{id}/renew` và tra cứu vé đang hoạt động của tôi
> `GET /monthly-passes/me` (đều của Phùng Duy Hoàng). Hai bảng `MonthlyPasses`/`PassTypes` đã
> migrate xong (Vàng Thị Dăm).

Hai luật nền — đọc trước khi dùng bất cứ endpoint nào ở đây:

- **Vé tháng là quyền đi lại trên MỘT tuyến trong một khoảng thời gian, KHÔNG kèm ghế** (A8.2).
  Khách vé tháng không đảm bảo có chỗ ngồi — trade-off đã chốt, không phải thiếu sót.
- **Hiệu lực thật của vé suy từ `validFrom`/`validTo`, KHÔNG đọc cột `status`.** Cột `status` do
  job quét vé hết hạn lật (BackgroundService — Nguyễn Duy Kiên) nên LUÔN có độ trễ: giữa lúc
  `validTo` trôi qua và lúc job chạy, một vé đã hết hạn vẫn còn `status = "Active"`.

| Method | Endpoint | Mô tả | Body | Trả về |
|---|---|---|---|---|
| POST | `/monthly-passes` | Đăng ký vé tháng mới của chính người gọi: chọn tuyến + loại vé (thời hạn và giá theo loại) | `{ routeId, passTypeCode }` | `MonthlyPass` |
| POST | `/monthly-passes/{id}/renew` | Gia hạn vé tháng của chính người gọi — ghi thêm một dòng mới, tính ngày hiệu lực kế tiếp | `{ passTypeCode? }` | `MonthlyPass` (dòng mới) |
| GET | `/monthly-passes/me` | Vé tháng **đang hoạt động** của chính người gọi | — | `MonthlyPass[]` (mảng trần) |

### Entity `MonthlyPass`

```json
// MonthlyPass — hình dạng trả về của mọi endpoint vé tháng
{
  "id": "3f2a1b0c-0000-0000-0000-000000000000",
  "code": "MP-01-A1B2C3",
  "routeId": "9d8c7b6a-0000-0000-0000-000000000000",
  "passTypeCode": "OneMonth",
  "price": 200000,
  "validFrom": "2026-10-01T00:00:00Z",
  "validTo": "2026-11-01T00:00:00Z",
  "status": "Active",
  "createdAt": "2026-09-28T03:20:11Z"
}
```

| Trường | Kiểu | Ghi chú |
|---|---|---|
| `id` | GUID | |
| `code` | string | Mã vé tháng = nội dung mã QR soát vé. Duy nhất toàn hệ thống, khuôn `MP-{mã tuyến}-{6 ký tự A–Z/0–9}` |
| `routeId` | GUID | Tuyến mà vé cho phép đi lại |
| `passTypeCode` | string | Mã loại vé trong bảng `PassTypes` — quyết định thời hạn và giá gói |
| `price` | number | Số tiền THỰC TRẢ lúc đăng ký/gia hạn — **ảnh chụp**, không tra lại bảng giá |
| `validFrom` / `validTo` | ISO 8601 UTC | Khoảng hiệu lực, **tính cả hai mốc**. Nguồn sự thật về hiệu lực của vé |
| `status` | string | `Active` / `Expired` — trạng thái LƯU, có độ trễ (xem trên) |
| `createdAt` | ISO 8601 UTC | |

#### `POST /monthly-passes` — đăng ký vé tháng mới

Đăng ký một vé tháng cho **chính người gọi**: chọn tuyến (`routeId`) và loại vé (`passTypeCode`) —
thời hạn và giá gói do loại vé quyết định (bảng `PassTypes`), không phải tham số riêng: chọn loại
vé chính là chọn thời hạn.

```json
// RegisterMonthlyPassRequest
{ "routeId": "9d8c7b6a-0000-0000-0000-000000000000", "passTypeCode": "OneMonth" }
```

| Trường | Bắt buộc | Ràng buộc |
|---|---|---|
| `routeId` | ✅ | GUID của tuyến muốn đăng ký. Không trỏ tới tuyến nào → **404** |
| `passTypeCode` | ✅ | Mã loại vé trong `PassTypes`, tối đa 20 ký tự. Không khớp loại vé nào → **404** |

**Cách tính khoảng hiệu lực** (phần "thời hạn" của task):

1. `validFrom` = **thời điểm đăng ký** — vé có hiệu lực ngay khi mua, cùng lối gia hạn vé đã hết
   hạn bắt đầu từ bây giờ.
2. `validTo` = `validFrom + durationMonths` của loại vé, cộng theo **tháng lịch**:
   `31/01 + 1 tháng = 28/02` (ngày cuối tháng tự kẹp — cùng lối .NET `AddMonths`).
3. `price` = giá hiện hành của loại vé (**chụp lại** — cùng lý do cột `price` của gia hạn: đổi giá
   gói sau này không làm vé đã bán nhảy theo), `status` = `Active`, `code` do server sinh theo
   khuôn `MP-{mã tuyến}-{6 ký tự A–Z/0–9}` và kiểm duy nhất.

Trả **200** kèm `MonthlyPass` của vé mới:

> **Vì sao 200 mà không phải 201?** Chưa có `GET /monthly-passes/{id}` để trỏ `Location` — cùng
> lý do `POST /monthly-passes/{id}/renew` trả 200. Đổi sang 201 khi có endpoint tra vé theo id.

| Ca lỗi | Mã | Ghi chú |
|---|---|---|
| Không có token / token hết hạn | 401 | |
| `routeId` thiếu hoặc sai định dạng GUID; `passTypeCode` thiếu | 400 | `errors.routeId` / `errors.passTypeCode` |
| Tuyến không tồn tại | 404 | `"Không tìm thấy tuyến đường"` — tham chiếu cứng, cùng câu trả lời của `GET /trips/search` |
| `passTypeCode` không khớp loại vé nào | 404 | `"Không tìm thấy loại vé"` |
| Khoảng hiệu lực mới **chồng lấn** với một vé tháng khác của cùng người trên **cùng tuyến** | 409 | So khoảng `[validFrom, validTo)` — cùng luật gia hạn; vé nối đuôi (mốc này `validTo` = mốc kia `validFrom`) **không** tính chồng |
| `passTypeCode` dài quá 20 ký tự | 400 | `errors.passTypeCode` |

Ghi chú:

- **Yêu cầu đăng nhập, không yêu cầu vai trò cụ thể** — cùng lối gia hạn và `GET /monthly-passes/me`:
  RBAC của dự án không có policy `Passenger`, quyền sở hữu nằm ngay trong luồng (vé luôn gắn
  `userId` của người gọi). Admin/Manager gọi endpoint này cũng chỉ đăng ký cho chính mình.
- **Việc chặn trùng vé tháng đang hoạt động trên cùng tuyến nằm ở đây** (task *"Validate trùng vé
  tháng đang hoạt động trên cùng tuyến"*): so khoảng `[validFrom, validTo)` chồng lấn chứ không so
  cột `status` — cột `status` có độ trễ (job quét của Kiên), so nó là so sai; và không thể khoá
  bằng unique `(userId, routeId)` ở CSDL vì gia hạn cố ý ghi dòng mới nối đuôi kỳ cũ (xem
  `Data/AppDbContext.MonthlyPass.cs`).
- **Không chặn theo trạng thái tuyến** — cùng lý do gia hạn: đây là giao dịch mua bán, tuyến tạm
  ngưng vẫn có thể mở lại.
- **Nhật ký kiểm toán**: middleware tự ghi `Create`, `Target` = `MonthlyPasses:{id vé mới}` — id
  đọc từ body trả về, cùng lối gia hạn.

#### `POST /monthly-passes/{id}/renew` — gia hạn vé tháng

Gia hạn một vé tháng của **chính người gọi**. Thao tác **ghi thêm MỘT dòng mới** với
`validFrom` = `validTo` của vé cũ; **dòng cũ giữ nguyên, không sửa một trường nào** — nó ở lại làm
lịch sử và làm mốc tính kỳ kế tiếp. Sửa tại chỗ thì mất cả hai, nên đây là quy tắc chốt trong
entity `MonthlyPass` (Vàng Thị Dăm).

```
POST /api/monthly-passes/3f2a1b0c-…/renew     ← {id} là vé ĐANG ĐƯỢC GIA HẠN (vé cũ)
```

Body **tùy chọn** — gửi `{}` hoặc không gửi body đều được:

```json
// RenewMonthlyPassRequest
{ "passTypeCode": "ThreeMonths" }
```

| Trường | Bắt buộc | Ràng buộc |
|---|---|---|
| `passTypeCode` | — | Mã loại vé trong `PassTypes`, tối đa 20 ký tự. **Bỏ trống / chuỗi rỗng = giữ nguyên loại vé của vé đang gia hạn.** Không khớp loại vé nào → **404** |

**Cách tính ngày hiệu lực kế tiếp** (đây chính là phần "tính ngày hiệu lực kế tiếp" của task):

1. Vé cũ **còn hạn** (`validTo` > bây giờ): `validFrom (mới) = validTo (cũ)` — kỳ mới nối đuôi kỳ
   cũ liền mạch, không mất ngày nào.
2. Vé cũ **đã hết hạn**: `validFrom (mới) = bây giờ` — gia hạn lúc nào hiệu lực từ lúc đó,
   **không hồi tố**.
3. `validTo (mới) = validFrom (mới) + durationMonths` của loại vé, cộng theo **tháng lịch**:
   `31/01 + 1 tháng = 28/02` (ngày cuối tháng tự kẹp — cùng lối .NET `AddMonths`).
4. `price` = giá hiện hành của loại vé (**chụp lại** — đổi giá gói sau này không làm vé đã gia hạn
   nhảy theo), `status` = `Active`, `code` do server sinh và kiểm duy nhất.

Trả **200** kèm `MonthlyPass` của **dòng mới** (id mới — không phải vé cũ):

> **Vì sao 200 mà không phải 201?** Chưa có `GET /monthly-passes/{id}` để trỏ `Location` — cùng lối
> `POST /routes/{routeId}/trips/generate` trả 200 kèm kết quả. Đổi sang 201 khi có endpoint tra vé
> theo id.

| Ca lỗi | Mã | Ghi chú |
|---|---|---|
| Không có token / token hết hạn | 401 | |
| `{id}` không tồn tại **hoặc vé của người khác** | 404 | Cùng một câu `"Không tìm thấy vé tháng"` — không phân biệt để không lộ vé của người khác có tồn tại |
| `passTypeCode` không khớp loại vé nào | 404 | |
| Khoảng hiệu lực mới **chồng lấn** với một vé tháng KHÁC của cùng người trên **cùng tuyến** | 409 | So khoảng `[validFrom, validTo)` — vé nối đuôi (mốc này `validTo` = mốc kia `validFrom`) **không** tính chồng |
| `passTypeCode` dài quá 20 ký tự | 400 | `errors.passTypeCode` |

Ghi chú:

- **Yêu cầu đăng nhập, không yêu cầu vai trò cụ thể** — RBAC của dự án chỉ có `AdminOnly`/
  `ManagerOrAbove`, không có policy `Passenger`; quyền sở hữu kiểm ở tầng service (vé của người
  khác → 404). Admin/Manager gọi endpoint này cũng chỉ gia hạn được vé của chính mình.
- **Không chặn theo trạng thái tuyến**: đây là giao dịch mua bán, tuyến tạm ngưng vẫn có thể mở
  lại — chặn ở đây sẽ khoá luôn vé đã bán hợp lệ. Task không yêu cầu; muốn chặn thì bổ sung ở
  task sau.
- **Nhật ký kiểm toán**: middleware tự ghi `Create`, `Target` = `MonthlyPasses:{id vé MỚI}` — id
  đọc từ body trả về (POST → Create), không phải `{id}` trên đường dẫn. Tra vết một lần gia hạn:
  tra theo id vé mới, rồi ngược về vé cũ bằng `validFrom` của nó = `validTo` của vé cũ.
- Gia hạn vé **còn hạn** tạo một dòng **tương lai** (`validFrom` > bây giờ) — hợp lệ, và là lý do
  bảng không có unique `(userId, routeId)`: hai dòng cùng người cùng tuyến là chuyện bình thường
  khi kỳ sau đã đăng ký trước.

#### `GET /monthly-passes/me` — vé tháng đang hoạt động của tôi

Trả về **mảng trần** (không phân trang, có thể rỗng) các vé tháng của **chính người gọi** đang **có
hiệu lực ngay lúc này** — trả lời câu hỏi "bây giờ tôi đi được tuyến nào bằng vé tháng?".

Không tham số, không body. Không có token → **401**. Không có vé nào đang hoạt động → **200** kèm
`[]` — mảng rỗng là câu trả lời hợp lệ, không phải 404. **Không yêu cầu vai trò cụ thể** (cùng lối
`POST .../renew`): ai đăng nhập cũng chỉ thấy vé của chính mình.

**"Đang hoạt động" = `validFrom <= bây giờ <= validTo`** — tính cả hai mốc, hiệu lực suy từ cặp mốc
theo luật nền thứ hai ở đầu mục; cột `status` **không tham gia** câu trả lời. Hệ quả, cả hai đều
cố ý:

- Vé đã quá `validTo` nhưng job quét chưa chạy tới (cột vẫn `"Active"`) → **không** trả về.
- Cột `status` nói gì cũng không làm vé rơi khỏi kết quả khi cặp mốc còn hiệu lực.

Vé của **kỳ sau** (dòng gia hạn có `validFrom` ở tương lai) chưa "đang hoạt động" nên **không** nằm
trong kết quả cho tới ngày hiệu lực — cố ý: đây là câu hỏi lúc lên xe, không phải lịch sử đăng ký.

Sắp theo `validTo` **tăng dần** (vé sắp hết hạn đứng đầu — màn hình nhắc gia hạn đọc dòng đầu),
phụ theo `code` cho ổn định. Mỗi phần tử đúng hình dạng `MonthlyPass` ở trên; frontend ghép
`routeId` → tên tuyến bằng `GET /routes` như các màn hình khác.

> **Vì sao mảng trần mà không `{ items, total }` như các danh sách khác?** Một người chỉ có vài vé
> đang hoạt động (mỗi tuyến một vé) — phân trang là nghi thức thừa, và màn hình chỉ cần đúng mảng.

## Nhật ký kiểm toán — `/audit-logs`

> ✅ Backend đã đủ cả hai phần của story 23: **truy vấn danh sách** (`AuditLogsController` —
> Nguyễn Duy Kiên) và **xuất Excel** (`AuditLogExportController` — Phùng Duy Hoàng). Hai API dùng
> chung tiền tố `api/audit-logs` nhưng nằm ở hai controller khác nhau; đoạn literal `export` thắng
> đoạn tham số nên chúng không nuốt lẫn nhau.
> **Cả hai endpoint chỉ dành cho vai trò `Admin`.** Người đã đăng nhập nhưng không đủ quyền nhận
> **403** kèm body `{ "message": "Bạn không có quyền truy cập tính năng này." }`.
>
> Hai API dùng **chung bốn tên tham số lọc** `from`, `to`, `userId`, `action` — frontend không phải
> nói hai thứ tiếng.

Bảng `AuditLogs` là nhật ký **chỉ ghi thêm**: `AuditLogMiddleware` tự ghi mọi thao tác thay đổi dữ
liệu thành công, còn `AuthController` ghi riêng đăng nhập / đăng xuất / đăng nhập thất bại. Không
có endpoint nào sửa hay xoá bản ghi — sửa được nhật ký thì nhật ký mất giá trị kiểm toán.

### Entity `AuditLog`

| Trường | Kiểu | Mô tả |
|---|---|---|
| `id` | `string` (GUID) | Khoá chính |
| `userId` | `string \| null` | Người thao tác. `null` khi không xác định được — đăng nhập thất bại với SĐT không tồn tại, hoặc hành động do hệ thống tự làm |
| `action` | `string` | Một trong 7 mã bên dưới |
| `target` | `string \| null` | Đối tượng bị tác động, dạng `<Tên bảng>:<Id>` — ví dụ `"Routes:3f2a1b0c-…"`. `null` với đăng nhập / đăng xuất |
| `ipAddress` | `string \| null` | Địa chỉ IP của người gọi. `null` khi không lấy được |
| `createdAt` | `string` (ISO 8601, UTC) | Thời điểm hành động xảy ra |

Mã `action` hợp lệ — đúng **7** giá trị, khớp enum `AuditAction` của backend:

| Mã | Nghĩa |
|---|---|
| `Login` | Đăng nhập thành công |
| `Logout` | Đăng xuất |
| `LoginFailed` | Đăng nhập thất bại |
| `Create` | Tạo bản ghi mới |
| `Update` | Sửa bản ghi đã có |
| `Delete` | Xoá bản ghi |
| `Warning` | Cảnh báo do **hệ thống tự phát hiện**, không gắn HTTP verb nào |

Middleware suy `Create` / `Update` / `Delete` từ HTTP verb, nên màn hình mới không cần thêm mã —
chi tiết nằm ở `target`.

`Warning` là mã duy nhất **không** do một request nào sinh ra: job nền ghi nó khi phát hiện bất
thường. Hiện có đúng một nguồn — `SeatHoldAbuseBackgroundService` ghi một dòng `Warning` với
`userId` là tài khoản bị gắn cờ và `target = "Users:<id>"` khi tài khoản đó giữ chỗ quá nhiều lần
(ngưỡng và cửa sổ: `docs/26-csdl-so-do-ghe.md` §8). `ipAddress` luôn `null` ở nhóm này vì job nền
không có request. Vì vậy màn hình nhật ký lọc `action=Warning` là ra thẳng danh sách tài khoản
đáng xem.

### Endpoints

| Method | Endpoint | Mô tả | Body | Trả về |
|---|---|---|---|---|
| GET | `/audit-logs` | Truy vấn danh sách nhật ký — lọc theo người dùng, hành động, khoảng thời gian | — | JSON `{ items, total, page, pageSize }` |
| GET | `/audit-logs/export` | Tải nhật ký ra file Excel | — | file `.xlsx` (nhị phân) |

#### `GET /audit-logs`

Danh sách bản ghi khớp bộ lọc, **mới nhất trước**, có phân trang.

| Tham số | Kiểu | Mặc định | Mô tả |
|---|---|---|---|
| `from` | `string` (`yyyy-MM-dd`) | 29 ngày trước hôm nay | Ngày bắt đầu, theo **UTC**, tính **trọn ngày** |
| `to` | `string` (`yyyy-MM-dd`) | hôm nay (UTC) | Ngày kết thúc, theo **UTC**, tính **trọn ngày** |
| `userId` | `string` (GUID) | — | Chỉ lấy thao tác của một người |
| `action` | `string` | — | Chỉ lấy một loại hành động — một trong 6 mã ở bảng trên |
| `page` | `số nguyên` ≥ 1 | `1` | Trang hiện tại |
| `pageSize` | `số nguyên` 1..100 | `10` | Số dòng mỗi trang |

Bỏ trống cả `from` lẫn `to` → lấy **30 ngày gần nhất** tính cả hôm nay, y hệt `GET /audit-logs/export`.
Cả hai đầu mút được tính **trọn ngày** theo UTC: `to=2026-09-26` bao gồm mọi bản ghi tới hết
`2026-09-26T23:59:59.9999999Z`.

**Kết quả** — JSON, không phải file:

```json
{
  "items": [
    {
      "id": "9c1f8a44-…",
      "userId": "3f2a1b0c-…",
      "userFullName": "Nguyễn Văn A",
      "userPhoneNumber": "0912345678",
      "action": "Update",
      "target": "Routes:3f2a1b0c-…",
      "ipAddress": "203.0.113.5",
      "createdAt": "2026-09-26T03:12:44.123Z"
    }
  ],
  "total": 137,
  "page": 1,
  "pageSize": 10
}
```

`items` là trang hiện tại, `total` là **tổng số dòng khớp bộ lọc** (không phải số dòng trong trang) —
frontend dùng `total` để vẽ phân trang của AntD Table. Cùng khuôn với `GET /routes` và
`GET /admin/users`.

`userFullName` và `userPhoneNumber` là hai trường **chỉ để hiển thị**, ghép từ bảng `Users`. Cả hai
là `null` khi `userId` là `null`.

Bộ lọc không khớp bản ghi nào → **200** kèm `items: []` và `total: 0`, **không** phải 404 — cùng
lối với file Excel rỗng ở mục dưới.

Lỗi thường gặp: không đăng nhập → **401** · không phải `Admin` → **403** · `to` sớm hơn `from` →
**400** `errors.to` · `userId` sai định dạng GUID → **400** `errors.userId` · `page` nhỏ hơn 1 hoặc
`pageSize` ngoài khoảng 1..100 → **400** `errors.page` / `errors.pageSize`.

> **Vì sao `action` gõ sai trả danh sách rỗng, còn `userId` gõ sai trả 400?** Cùng lý do đã ghi ở
> mục xuất file bên dưới: `action` là **mã** — một mã lạ có thể là giá trị hợp lệ trong tương lai;
> `userId` là **định danh** — GUID gõ sai là request hỏng, trả danh sách rỗng cho nó là che mất lỗi.

> **Vì sao trả kèm họ tên và số điện thoại?** Endpoint chỉ dành cho `Admin`, và Admin vốn xem được
> cả hai qua `GET /admin/users`. Không kèm thì bảng nhật ký chỉ có GUID trần, mà `GET /admin/users`
> **có phân trang** nên frontend không thể tự ghép tên cho mọi bản ghi trong trang. File Excel xuất
> ra cũng kèm đúng hai trường này — hai đường xem nhật ký cho ra cùng một thông tin.

> **Vì sao không có lọc theo `target` hay ô tìm kiếm tự do?** Phạm vi story 23 chốt ba bộ lọc: người
> dùng, hành động, khoảng thời gian. Thêm tham số sau này là thay đổi **cộng thêm**, không phá hợp
> đồng hiện tại.

> ⚠️ **Bản ghi có `userId` là `null` không lọc được theo `userId`.** Đó là các ca đăng nhập thất bại
> với số điện thoại không tồn tại, hoặc hành động do hệ thống tự làm. Muốn xem chúng thì để trống bộ
> lọc người dùng — đây không phải dữ liệu bị thiếu.

> 📌 **Chưa có `GET /audit-logs/{id}`.** Màn hình chi tiết
> (`frontend/src/components/AuditLogDetailModal.tsx`) nhận nguyên bản ghi từ danh sách qua prop nên
> không cần endpoint riêng — danh sách đã trả đủ mọi trường màn hình hiển thị. Nếu sau này thêm thì
> **bắt buộc** đặt tham số là `{id:guid}`, để không nuốt mất đường dẫn `/audit-logs/export`.

#### `GET /audit-logs/export`

Trả về **file `.xlsx`**, không phải JSON — đây là endpoint nhị phân duy nhất của hệ thống. File
chứa toàn bộ bản ghi khớp bộ lọc, **mới nhất trước**.

| Tham số | Kiểu | Mặc định | Mô tả |
|---|---|---|---|
| `from` | `string` (`yyyy-MM-dd`) | 29 ngày trước hôm nay | Ngày bắt đầu, theo **UTC**, tính **trọn ngày** |
| `to` | `string` (`yyyy-MM-dd`) | hôm nay (UTC) | Ngày kết thúc, theo **UTC**, tính **trọn ngày** |
| `userId` | `string` (GUID) | — | Chỉ lấy thao tác của một người |
| `action` | `string` | — | Chỉ lấy một loại hành động — một trong 6 mã ở bảng trên |

Bỏ trống cả `from` lẫn `to` → lấy **30 ngày gần nhất** tính cả hôm nay. Cả hai đầu mút được tính
**trọn ngày**: `to=2026-09-26` bao gồm mọi bản ghi tới hết `2026-09-26T23:59:59.9999999Z`.

**Nội dung file** — hàng 1 tiêu đề tài liệu, hàng 2 ghi khoảng thời gian / số dòng / thời điểm
xuất / người xuất, hàng 3 trống, **hàng 4 là tiêu đề cột**, dữ liệu từ hàng 5:

| # | Cột | Ghi chú |
|---|---|---|
| 1 | `Thời gian (UTC)` | Ô kiểu ngày thật, sắp và lọc được theo thời gian |
| 2 | `Người thao tác` | Họ tên; `(không xác định)` khi `userId` là `null` |
| 3 | `Số điện thoại` | Để trống khi không xác định được |
| 4 | `Hành động` | Một trong 6 mã trên |
| 5 | `Đối tượng` | Giá trị `target` |
| 6 | `Địa chỉ IP` | Để trống khi không lấy được |
| 7 | `Mã người dùng` | GUID — khoá ổn định để tra ngược, vì SĐT đổi được và họ tên trùng được |

Khoảng lọc không có bản ghi nào → **200** kèm file chỉ có tiêu đề, **không** phải 404.

Giới hạn **10.000 dòng mỗi lần xuất**. Vượt → **400** `errors.from` và `errors.to` kèm gợi ý thu
hẹp khoảng — **không bao giờ trả về file thiếu dòng**, vì một chứng từ cụt mà trông như thành công
còn tệ hơn một dòng báo lỗi.

Lỗi thường gặp: không đăng nhập → **401** · không phải `Admin` → **403** · `to` sớm hơn `from` →
**400** `errors.to` · `userId` sai định dạng GUID → **400** `errors.userId` · vượt 10.000 dòng →
**400** `errors.from`, `errors.to`.

> **Vì sao trả 200 + file rỗng mà không phải 404?** Câu hỏi kiểm toán hay gặp nhất là *"tuần đó có
> ai xoá gì không?"* — và "không" là một **câu trả lời**, không phải một lỗi. Trả 404 thì trình duyệt
> tải về một file JSON lỗi đội tên `.xlsx`, người kiểm toán mở ra thấy rác. Cùng lối "tuyến chưa
> cấu hình giá → mảng rỗng" ở mục Bảng giá vé.

> **Vì sao `action` gõ sai trả file rỗng, còn `userId` gõ sai trả 400?** `action` là **mã** — một
> mã lạ có thể là giá trị hợp lệ trong tương lai, nên trả rỗng là hợp lý, cùng lối `status` của
> `GET /routes`. `userId` là **định danh**: GUID sai định dạng là request hỏng chứ không phải "bộ
> lọc không khớp gì", và trả file rỗng cho một GUID gõ sai là che mất lỗi.

> **Vì sao giữ UTC mà không đổi sang giờ Việt Nam?** Bộ lọc `from`/`to` và cột `Thời gian` trong
> file dùng **cùng một khung giờ**, nên file và bộ lọc không bao giờ nói hai chuyện khác nhau —
> tính chất quan trọng nhất với một chứng từ kiểm toán. Đánh đổi đã biết: chọn ngày theo giờ Việt
> Nam sẽ lệch tối đa 7 giờ ở hai đầu mút. Nhãn cột ghi rõ `(UTC)` để không ai phải đoán.

> **Vì sao file chứa số điện thoại?** Endpoint chỉ dành cho `Admin`, và Admin vốn xem được họ tên
> lẫn SĐT qua `GET /admin/users`. Bản xuất chỉ còn GUID thì người ngồi đọc không tra được gì —
> mất đúng mục đích của story.

⚠️ **Cho người viết frontend:** đây là endpoint nhị phân đầu tiên nên phải gọi với
`responseType: 'blob'`. Khi đó body lỗi cũng về dưới dạng `Blob`, khiến `error.response.data.message`
trong `axiosClient.ts` là `undefined` và người dùng chỉ thấy câu thông báo chung. Màn hình tải file
cần tự `await blob.text()` rồi `JSON.parse` cho nhánh lỗi mới đọc được `message`.

## Phản ánh — `/feedbacks`

> 🚧 Story 24 đã có **phần quản lý xử lý** (Phùng Duy Hoàng), **danh sách phản ánh của hành khách**
> và **thống kê theo loại/tuyến** (Nguyễn Duy Kiên), **gửi phản ánh** (Trần Trung Hiếu — mục
> "Gửi phản ánh" ở cuối phần này). Hai bảng `Feedbacks`/`FeedbackReplies` **đã có
> migration** (Vàng Thị Dăm — `20261003124532_Sprint2_Feedbacks_FeedbackReplies`); còn thiếu bước
> chạy `dotnet ef database update` lên CSDL.
>
> **Toàn bộ endpoint `/admin/feedbacks` ở mục "Endpoints" dưới đây yêu cầu vai trò `Manager` hoặc
> `Admin`** — xử lý phản ánh là nghiệp vụ vận hành của nhà xe. Người đã đăng nhập nhưng không đủ
> quyền nhận **403** kèm body `{ "message": "Bạn không có quyền truy cập tính năng này." }`. Cùng
> quyền đó cho mục "Thống kê phản ánh", còn hai endpoint `/feedbacks/me` ở mục cuối thì ngược lại —
> hành khách tự xem phản ánh của mình, xem "Phản ánh của tôi".

Hai luật nền — đọc trước khi dùng bất cứ endpoint nào ở đây:

- **Phản ánh có thể KHÔNG gắn với chuyến nào** — `tripId` cho phép `null` (A9 #20). Khách khiếu nại
  về tuyến, về giá vé, hay về ứng dụng thì không có chuyến để trỏ vào; bắt buộc `tripId` là chặn
  luôn các ca đó.
- **Phản hồi là bảng chỉ ghi thêm, không sửa** (`FeedbackReplies`). Một phản ánh có thể được trả
  lời nhiều lần; sửa tại chỗ thì mất lịch sử đối thoại — cùng lối "ghi thêm dòng mới" của gia hạn
  vé tháng.

### Entity `Feedback`

```json
{
  "id": "3f2a1b0c-0000-0000-0000-000000000000",
  "userId": "9d8c7b6a-0000-0000-0000-000000000000",
  "userFullName": "Nguyễn Văn A",
  "tripId": null,
  "type": "Complaint",
  "content": "Xe chạy trễ 30 phút so với giờ trên vé, tài xế không thông báo gì.",
  "attachmentUrl": null,
  "rating": null,
  "status": "InProgress",
  "createdAt": "2026-10-03T02:11:00Z",
  "updatedAt": "2026-10-03T04:00:00Z",
  "replies": [
    {
      "id": "c1a2b3d4-0000-0000-0000-000000000000",
      "userId": "77aa66bb-0000-0000-0000-000000000000",
      "userFullName": "Trần Thị B",
      "content": "Nhà xe xin lỗi vì sự cố hôm đó, đã nhắc nhở tài xế và hoàn 20% giá vé.",
      "createdAt": "2026-10-03T04:00:00Z"
    }
  ]
}
```

| Trường | Kiểu | Ghi chú |
|---|---|---|
| `id` | GUID | |
| `userId` / `userFullName` | GUID / string | Hành khách gửi phản ánh. `userFullName` chỉ để hiển thị, ghép từ `Users` — `null` khi không tìm thấy tài khoản |
| `tripId` | GUID \| null | Chuyến bị phản ánh. **`null` là hợp lệ** — phản ánh về tuyến/dịch vụ/ứng dụng |
| `type` | string | `Complaint` (khiếu nại) · `Compliment` (khen ngợi) · `Suggestion` (góp ý) |
| `content` | string | Nội dung phản ánh — cột `text` |
| `attachmentUrl` | string \| null | Ảnh đính kèm do hành khách gửi |
| `rating` | number \| null | 1–5 sao, mức độ hài lòng. `null` với phản ánh không chấm điểm |
| `status` | string | `New` / `InProgress` / `Resolved` — xem bảng nghĩa bên dưới |
| `createdAt` / `updatedAt` | ISO 8601 UTC | `updatedAt` chỉ đổi khi `status` đổi. **Thêm phản hồi không chạm vào dòng `Feedbacks`** — dòng đó là bản ghi của hành khách |
| `replyCount` | number | **Chỉ có ở dòng danh sách** (thay cho `replies`) |
| `replies` | `FeedbackReply[]` | **Chỉ có ở chi tiết** và ở response của `POST .../replies`; sắp **cũ → mới** |

Nghĩa ba giá trị trạng thái — cột `Status` lưu chuỗi tiếng Anh, màn hình hiển thị nhãn tiếng Việt:

| Giá trị | Nhãn hiển thị | Nghĩa |
|---|---|---|
| `New` | Mới | Vừa tiếp nhận, chưa ai xử lý — giá trị khởi tạo |
| `InProgress` | Đang xử lý | Có người đã nhận, đang xử lý |
| `Resolved` | Đã xử lý | Đã trả lời / xử lý xong |

### Entity `FeedbackReply`

| Trường | Kiểu | Ghi chú |
|---|---|---|
| `id` | GUID | |
| `userId` / `userFullName` | GUID / string | Quản lý đã phản hồi — ghép từ `Users`, chỉ để hiển thị |
| `content` | string | Nội dung phản hồi của nhà xe — cột `text` |
| `createdAt` | ISO 8601 UTC | Bảng chỉ ghi thêm nên **không có** `UpdatedAt` (A4) |

### Endpoints

| Method | Endpoint | Mô tả | Body | Trả về |
|---|---|---|---|---|
| GET | `/admin/feedbacks` | Danh sách phản ánh — lọc theo trạng thái/loại, có phân trang | — | `{ items, total, page, pageSize }` |
| GET | `/admin/feedbacks/{id:guid}` | Chi tiết một phản ánh kèm toàn bộ luồng phản hồi | — | `Feedback` (đầy đủ) |
| POST | `/admin/feedbacks/{id:guid}/replies` | Phản hồi hành khách — ghi thêm một dòng vào luồng | `{ content }` | `Feedback` (kèm phản hồi mới) |
| PATCH | `/admin/feedbacks/{id:guid}` | Đổi trạng thái xử lý | `{ status }` | `Feedback` (đầy đủ) |

#### `GET /admin/feedbacks`

Danh sách phản ánh khớp bộ lọc, **mới nhất trước** (`createdAt` giảm dần, phụ `id` cho ổn định),
có phân trang — cùng khuôn `GET /routes` và `GET /audit-logs`.

| Tham số | Kiểu | Mặc định | Mô tả |
|---|---|---|---|
| `status` | `string` | — | Chỉ lấy một trạng thái — `New` / `InProgress` / `Resolved` |
| `type` | `string` | — | Chỉ lấy một loại — `Complaint` / `Compliment` / `Suggestion` |
| `page` | số nguyên ≥ 1 | `1` | Trang hiện tại |
| `pageSize` | số nguyên 1..100 | `10` | Số dòng mỗi trang |

```json
// FeedbackListResponse — ví dụ GET /admin/feedbacks?status=New&page=1&pageSize=10
{
  "items": [
    {
      "id": "3f2a1b0c-…",
      "userId": "9d8c7b6a-…",
      "userFullName": "Nguyễn Văn A",
      "tripId": null,
      "type": "Complaint",
      "content": "Xe chạy trễ 30 phút…",
      "attachmentUrl": null,
      "rating": null,
      "status": "New",
      "createdAt": "2026-10-03T02:11:00Z",
      "updatedAt": null,
      "replyCount": 0
    }
  ],
  "total": 42,
  "page": 1,
  "pageSize": 10
}
```

Dòng trong `items` là `Feedback` **không kèm `replies`**, thay bằng `replyCount`. Luồng phản hồi đầy
đủ nằm ở `GET /admin/feedbacks/{id}` — kéo cả luồng vào mọi dòng danh sách thì một trang 10 dòng có
thể kèm hàng trăm phản hồi mà bảng không hiển thị.

Không khớp phản ánh nào → **200** kèm `items: []`, `total: 0` — **không** phải 404.

Lỗi thường gặp: không đăng nhập → **401** · không đủ quyền → **403** · `page` < 1 hoặc `pageSize`
ngoài 1..100 → **400** `errors.page` / `errors.pageSize`. `status`/`type` gõ sai **không** lỗi — trả
danh sách rỗng, cùng lối `GET /routes?status=` (mã lạ có thể là giá trị hợp lệ trong tương lai).

#### `GET /admin/feedbacks/{id:guid}`

Chi tiết một phản ánh, kèm **toàn bộ** luồng phản hồi sắp **cũ → mới**. Không tìm thấy → **404**
kèm `{ "message": "Không tìm thấy phản ánh" }`.

#### `POST /admin/feedbacks/{id:guid}/replies` — phản hồi hành khách

Ghi thêm **một dòng mới** vào luồng phản hồi; **không sửa** phản ánh cũng như các phản hồi trước —
chúng ở lại làm lịch sử đối thoại. Endpoint **không tự đổi trạng thái**: trả lời xong mà còn chờ
khách phản hồi lại là ca có thật, muốn chuyển trạng thái thì gọi `PATCH` — tự đổi là nói sai giúp
người dùng.

| Trường | Bắt buộc | Ràng buộc |
|---|---|---|
| `content` | ✅ | 1–2000 ký tự |

Trả **200** kèm `Feedback` **đầy đủ** (danh sách `replies` đã có dòng mới) để màn hình chi tiết cập
nhật ngay, không phải gọi lại `GET`.

> **Vì sao 200 mà không phải 201?** Chưa có endpoint tra một phản hồi theo id để trỏ `Location` —
> cùng lối `POST /monthly-passes/{id}/renew` và `POST /routes/{routeId}/trips/generate`.

| Ca lỗi | Mã | Ghi chú |
|---|---|---|
| Không có token | 401 | |
| Không đủ quyền | 403 | |
| `{id}` không tồn tại | 404 | `{ "message": "Không tìm thấy phản ánh" }` |
| `content` trống hoặc quá 2000 ký tự | 400 | `errors.content` |

#### `PATCH /admin/feedbacks/{id:guid}` — đổi trạng thái xử lý

| Trường | Bắt buộc | Ràng buộc |
|---|---|---|
| `status` | ✅ | Một trong `New` / `InProgress` / `Resolved` |

Trả **200** kèm `Feedback` đầy đủ. **Không có máy trạng thái**: mọi chiều chuyển đều được, kể cả mở
lại `Resolved` → `InProgress` khi khách phản hồi thêm — story không yêu cầu chặn chiều nào, mà chặn
sai chiều thì màn hình không còn đường sửa.

| Ca lỗi | Mã | Ghi chú |
|---|---|---|
| Không có token | 401 | |
| Không đủ quyền | 403 | |
| `{id}` không tồn tại | 404 | |
| `status` thiếu hoặc không thuộc ba giá trị | 400 | `errors.status` |

Ghi chú chung:

- **Nhật ký kiểm toán**: middleware tự ghi, không ai phải gọi log tay — `PATCH` → `Update` và
  `POST .../replies` → `Create`, cả hai đều `Target` = `Feedbacks:{id phản ánh}` (id đọc từ body trả
  về, chính là id phản ánh). Tra vết một phản ánh: lọc nhật ký theo `target`.
- **`{id:guid}` có ràng buộc định dạng**: GUID sai định dạng không khớp route → **404**, không phải 500.

### Thống kê phản ánh — `/admin/feedbacks/statistics`

> Task *"API thống kê phản ánh theo loại và theo tuyến"* (US 24, Sprint 2, Nguyễn Duy Kiên).

Một endpoint duy nhất, trả về hai bảng đếm của **toàn bộ** phản ánh trong hệ thống: theo **loại** và
theo **tuyến**.

| Method | Endpoint | Mô tả | Trả về |
|---|---|---|---|
| GET | `/admin/feedbacks/statistics` | Đếm phản ánh theo loại và theo tuyến | `FeedbackStatistics` |

Yêu cầu vai trò `Manager` hoặc `Admin` như nhóm `/admin/feedbacks` ở trên — thống kê là số liệu vận
hành của nhà xe, không phải dữ liệu của một hành khách. Không đăng nhập → **401**; không đủ quyền →
**403**. Endpoint **không có tham số nào**, nên không có nhánh 400/404: đúng quyền là luôn **200**,
kể cả khi hệ thống chưa có phản ánh nào (mọi con đếm bằng 0, `byType` vẫn đủ ba dòng).

```json
// FeedbackStatistics — ví dụ GET /admin/feedbacks/statistics
{
  "total": 42,
  "byType": [
    { "type": "Complaint", "count": 20 },
    { "type": "Compliment", "count": 7 },
    { "type": "Suggestion", "count": 15 }
  ],
  "byRoute": [
    { "routeId": "b7c1d2e3-…", "routeCode": "01", "routeName": "Bến Thành — Chợ Lớn", "count": 12 },
    { "routeId": "c8d2e3f4-…", "routeCode": "B10", "routeName": "Cầu Giấy — Bờ Hồ", "count": 9 }
  ],
  "withoutTrip": 21
}
```

| Trường | Kiểu | Ghi chú |
|---|---|---|
| `total` | number | Tổng số phản ánh trong hệ thống |
| `byType` | `{ type, count }[]` | Đếm theo `type` |
| `byRoute` | `{ routeId, routeCode, routeName, count }[]` | Đếm theo tuyến của chuyến bị phản ánh |
| `withoutTrip` | number | Số phản ánh **không gắn chuyến** (`tripId` null) nên không quy được về tuyến nào |

Ba luật của hình dạng này:

- **`byType` LUÔN đủ ba dòng**, kể cả loại chưa có phản ánh nào (`count: 0`). Biểu đồ theo loại có
  trục cố định ba giá trị; tháng không có khiếu nại nào thì màn hình phải vẽ cột 0, chứ không phải
  thiếu mất một cột. Thứ tự cố định: `Complaint` → `Compliment` → `Suggestion`.
- **`byRoute` chỉ có những tuyến ĐÃ có phản ánh** — không liệt kê tuyến chưa từng bị phản ánh (đó là
  câu hỏi khác: "mọi tuyến, kể cả tuyến sạch"). Sắp theo `count` giảm dần — tuyến bị phản ánh nhiều
  nhất lên đầu, vì đó chính là điều bảng này để trả lời — trùng số thì theo `routeCode` tăng dần cho
  thứ tự tất định.
- **`tripId` null không quy được về tuyến** — phản ánh về giá vé/ứng dụng/dịch vụ chung không gắn
  chuyến nào (A9 #20), nên chúng nằm ở `withoutTrip` chứ không thành một dòng `routeId: null` trong
  `byRoute`: trộn hai loại khác nhau vào một mảng thì màn hình phải tự đoán cách vẽ.

Hai đẳng thức luôn đúng — màn hình được phép dựa vào, test khoá lại:

```
sum(byType[].count)                 == total
sum(byRoute[].count) + withoutTrip  == total
```

**Cố ý CHƯA có:** lọc theo khoảng thời gian, lọc theo trạng thái, phân trang. Task chỉ hỏi "theo loại
và theo tuyến". Thêm tham số lọc là **đổi hình dạng API** — phải bàn ở nhóm rồi sửa mục này trước khi
code (⛔5), đừng tự thêm.

### Phản ánh của tôi — `/feedbacks/me` (hành khách)

Hai endpoint dưới đây **KHÔNG** nằm sau policy `ManagerOrAbove` như nhóm `/admin/feedbacks` ở trên:
đây là hành khách tự xem phản ánh của chính mình. Yêu cầu **đăng nhập** (`[Authorize]` trần — RBAC
của dự án chỉ có `AdminOnly`/`ManagerOrAbove`, không có policy `Passenger`), và **phạm vi "của tôi"
nằm ngay trong truy vấn theo `userId` đọc từ JWT** — không có tham số nào để dò phản ánh của người
khác. Cùng lối `GET /monthly-passes/me`.

| Method | Endpoint | Mô tả | Trả về |
|---|---|---|---|
| GET | `/feedbacks/me` | Phản ánh của chính người gọi — lọc theo trạng thái | `MyFeedback[]` (mảng trần) |
| GET | `/feedbacks/me/{id:guid}` | Chi tiết một phản ánh của chính người gọi, kèm luồng phản hồi | `MyFeedback` (đầy đủ) |

#### `GET /feedbacks/me`

Danh sách phản ánh của chính người gọi, **mới nhất trước** (`createdAt` giảm dần, phụ `id` cho ổn
định). Trả **mảng trần** — không bọc `{ items, total, page, pageSize }` và **không phân trang**: một
hành khách chỉ có vài phản ánh, phân trang là nghi thức thừa, cùng lối `GET /monthly-passes/me`.

| Tham số | Kiểu | Mặc định | Mô tả |
|---|---|---|---|
| `status` | `string` | — | Chỉ lấy một trạng thái — `New` / `InProgress` / `Resolved`. Bỏ trống = mọi trạng thái |

```json
// ví dụ GET /feedbacks/me?status=InProgress
[
  {
    "id": "3f2a1b0c-…",
    "tripId": "b7c1d2e3-…",
    "routeCode": "01",
    "routeName": "Bến Thành — Chợ Lớn",
    "departureTime": "2026-10-01T22:00:00Z",
    "type": "Complaint",
    "content": "Xe chạy trễ 30 phút so với giờ trên vé.",
    "attachmentUrl": null,
    "rating": null,
    "status": "InProgress",
    "createdAt": "2026-10-03T02:11:00Z",
    "updatedAt": "2026-10-03T04:00:00Z",
    "replyCount": 1
  }
]
```

Dòng danh sách khác hình dạng `Feedback` của nhóm admin đúng hai chỗ:

- **Thêm `routeCode` / `routeName` / `departureTime`** — thông tin chuyến bị phản ánh, ghép từ
  `Trips` → `Routes`, chỉ để hiển thị. Cả ba đều `null` khi `tripId` là `null`, và cũng `null` khi
  không tìm thấy chuyến. Phải có ở đây vì hành khách **không tự tra được**: `GET /api/trips/{id}`
  nằm sau policy `ManagerOrAbove`, họ chỉ có `tripId` thô, không cách nào biết đó là tuyến nào.
- **Không có `userId` / `userFullName`** — với phản ánh của chính mình thì hai trường này là hằng số
  (luôn là người đang gọi); mang theo chỉ tổ thừa.

`replyCount` giữ đúng luật của mục "Entity `Feedback`": **danh sách chỉ có con đếm, KHÔNG có
`replies`**. Muốn đọc nội dung nhà xe trả lời thì gọi `GET /feedbacks/me/{id}`.

Không có phản ánh nào khớp → **200** kèm `[]`, **không** phải 404. `status` gõ sai **không** lỗi —
cũng trả `[]`, cùng lối `GET /routes?status=` và `GET /admin/feedbacks?status=` (mã lạ có thể là giá
trị hợp lệ trong tương lai).

| Ca lỗi | Mã | Ghi chú |
|---|---|---|
| Không có token | 401 | |
| `status` dài quá 20 ký tự | 400 | `errors.status` |

#### `GET /feedbacks/me/{id:guid}`

Chi tiết một phản ánh **của chính người gọi**, kèm **toàn bộ** luồng phản hồi sắp **cũ → mới** —
khác dòng danh sách đúng một chỗ: mang mảng `replies` thay cho con đếm `replyCount`. Các trường
chuyến (`routeCode` / `routeName` / `departureTime`) như ở dòng danh sách.

Phản ánh **không tồn tại HOẶC thuộc hành khách khác** → **404** kèm
`{ "message": "Không tìm thấy phản ánh" }` — **không** phải 403. Hai ca trả về cùng một câu là cố ý:
phân biệt chúng là xác nhận với người đang dò rằng id đó có thật trên hệ thống.

| Ca lỗi | Mã | Ghi chú |
|---|---|---|
| Không có token | 401 | |
| `{id}` không tồn tại, hoặc thuộc hành khách khác | 404 | `{ "message": "Không tìm thấy phản ánh" }` |

### Gửi phản ánh — `POST /feedbacks` (hành khách)

Hành khách đã đăng nhập gửi một phản ánh mới về một chuyến đi — hoặc phản ánh chung không gắn
chuyến nào. Yêu cầu **đăng nhập** (`[Authorize]` trần, cùng lối `/feedbacks/me`): `userId` đọc từ
JWT, **không có trong body** — không ai gửi phản ánh hộ người khác được.

| Method | Endpoint | Mô tả | Body | Trả về |
|---|---|---|---|---|
| POST | `/feedbacks` | Gửi một phản ánh mới của chính người gọi | `CreateFeedbackRequest` | `FeedbackSubmission` — **201** |

```json
// CreateFeedbackRequest — ví dụ body gửi lên
{
  "tripId": "b7c1d2e3-0000-0000-0000-000000000000",
  "type": "Complaint",
  "content": "Xe chạy trễ 30 phút so với giờ trên vé, tài xế không thông báo gì.",
  "rating": 2,
  "attachmentUrl": "https://img.example.com/xe-tre.jpg"
}
```

| Trường | Kiểu | Ghi chú |
|---|---|---|
| `tripId` | GUID \| null | Chuyến bị phản ánh. **`null` là hợp lệ** (A9 #20) — phản ánh về tuyến/dịch vụ/ứng dụng. Có giá trị mà chuyến không tồn tại → **404** |
| `type` | string | Bắt buộc — `Complaint` / `Compliment` / `Suggestion`. Mã lạ → **400** `errors.type`, thông báo liệt kê đủ ba mã chấp nhận |
| `content` | string | Bắt buộc, 1–2000 ký tự — cùng giới hạn nội dung phản hồi của quản lý |
| `rating` | number \| null | 1–5 sao mức độ hài lòng. `null` khi không chấm sao |
| `attachmentUrl` | string \| null | Ảnh đính kèm do hành khách gửi, tối đa 2000 ký tự. `null` khi không đính kèm; chuỗi trắng/rỗng lưu thành `null`. Hiện chưa có endpoint upload ảnh riêng — client tự chuẩn bị đường dẫn |

```json
// FeedbackSubmission — response 201
{
  "id": "3f2a1b0c-0000-0000-0000-000000000000",
  "tripId": "b7c1d2e3-0000-0000-0000-000000000000",
  "type": "Complaint",
  "content": "Xe chạy trễ 30 phút so với giờ trên vé, tài xế không thông báo gì.",
  "attachmentUrl": "https://img.example.com/xe-tre.jpg",
  "rating": 2,
  "status": "New",
  "createdAt": "2026-10-04T02:11:00Z"
}
```

Khác `Feedback` của nhóm admin và `MyFeedback` của "phản ánh của tôi" đúng ba chỗ, cả ba đều vì
phản ánh **vừa được tạo ra**: không `userId`/`userFullName` (tác giả là chính người gọi), không
`replyCount`/`replies` (chưa có phản hồi nào), không `updatedAt` (chưa ai đổi trạng thái).

`status` luôn là `New` — giá trị khởi tạo của entity. Trạng thái chỉ đổi khi quản lý xử lý ở
`PATCH /admin/feedbacks/{id}`.

Nhật ký (US 23): middleware ghi `Create` với `Target` = `Feedbacks:{id phản ánh}` — id đọc từ body
trả về.

| Ca lỗi | Mã | Ghi chú |
|---|---|---|
| Không có token | 401 | |
| `tripId` trỏ chuyến không tồn tại | 404 | `{ "message": "Không tìm thấy chuyến đã chọn." }` |
| `type` thiếu, quá 20 ký tự, hoặc không phải ba mã trên | 400 | `errors.type` |
| `content` trống hoặc quá 2000 ký tự | 400 | `errors.content` |
| `rating` ngoài khoảng 1..5 | 400 | `errors.rating` |
| `attachmentUrl` quá 2000 ký tự | 400 | `errors.attachmentUrl` |

## Vé điện tử — `/tickets`

> Story 4 *"Vé điện tử QR"*. ✅ **Bảng `Tickets` — migration ĐÃ SINH, ĐÃ SOÁT VÀ ĐÃ MERGE (PR #144)**
> (task *"Migrate bảng Tickets, TicketQRCodes"* — Vàng Thị Dăm, Sprint 3). Bảng ra đời trong migration
> `Sprint3_Payments_Vouchers_Tickets`, gộp cả nợ của dòng 27 (`Payments`) và dòng 50 (`Vouchers`,
> `VoucherUsages`) vì EF diff toàn bộ model trong một lệnh — xem `docs/27` và `docs/28` §8. Bảng chỉ
> có thật trên CSDL chung **sau khi chạy `database update`** (thứ tự ở `docs/25` §4);
> trước đó entity đã sẵn để người khác code và test trên InMemory.
> ⚠️ Chỉ **một** bảng: mã QR là cột `Code` của `Tickets`, KHÔNG có bảng `TicketQRCodes` riêng —
> A6 đặt khoá duy nhất `QrCode` trên chính bảng vé, và A9 không có bảng đó. Lý do đầy đủ:
> `docs/28-csdl-ve-dien-tu.md` §2. Mục này chốt hình dạng để Hiếu dựng endpoint và frontend
> (`frontend/src/api/ticketApi.ts` — Băng/Hạnh đã viết bản nháp mock) cùng dựa vào.

### Hình dạng `Ticket` — chốt theo bản nháp FE

| Trường | Kiểu | Mô tả |
|---|---|---|
| `id` | `string` (GUID) | Khoá chính |
| `code` | `string` | Mã vé = nội dung mã QR soát vé. **Duy nhất toàn hệ thống** (A6) |
| `routeCode` | `string` | Mã tuyến hiển thị — `"01"`, `"B10"` |
| `routeName` | `string` | Tên tuyến |
| `origin` / `destination` | `string` | Tên điểm đầu / điểm cuối |
| `departureTime` | `string` | Giờ khởi hành (ISO 8601) — căn cứ suy "hết hạn": chuyến đã đi mà chưa soát |
| `seatNumber` | `string` | Số ghế đã chọn |
| `boardingStopName` / `alightingStopName` | `string \| null` | Tên trạm lên/xuống — lưu để hiển thị, không phải khoá nghiệp vụ (A8.1). Null khi vé không có trạm lên/xuống (hai cột nguồn `BoardingStopId`/`AlightingStopId` vốn nullable — đổi từ bản chốt trước khi làm endpoint, xem ⚠️ dưới bảng Endpoints) |
| `price` | `number` | Giá đã thanh toán (VND) — `numeric(12,2)` |
| `status` | `string` | `"Paid"` / `"Used"` / `"Cancelled"` — do nghiệp vụ đặt (A3) |
| `createdAt` | `string` | Thời điểm phát hành |
| `usedAt` | `string \| null` | Thời điểm soát vé — chỉ khi `status = 'Used'` |

### Endpoints

| Method | Endpoint | Task | Trạng thái |
|---|---|---|---|
| GET | `/tickets/me` | *"API tra cứu vé của tôi (đã mua / đã dùng / đã huỷ)"* — Trần Trung Hiếu | ✅ mục dưới |
| GET | `/tickets/{id}` | Một vé của chính người gọi (màn QR mở trực tiếp một vé) | ✅ mục dưới — cùng task tra cứu vé của tôi |
| POST | `/tickets/validate` | *"API soát vé: nhận mã QR, xác thực chữ ký, trả kết quả hợp lệ/không"* — Trần Trung Hiếu (US 15, Sprint 4, dòng 15 + dòng 19) | ✅ mục "Soát vé QR" dưới |
| — | *phát hành vé* | *"API phát hành vé điện tử sau khi thanh toán thành công"* — Trần Trung Hiếu | ⚠️ xem luồng dưới |

⚠️ **Bảng trường Ticket (ở trên) đổi một dòng so với bản đã chốt:** `boardingStopName` và
`alightingStopName` chuyển thành `string | null`. Bản chốt cũ ghi `string` trong khi hai cột nguồn
(`Tickets.BoardingStopId`, `AlightingStopId`) vốn nullable (vé mua ở màn chọn ghế có thể không chọn
trạm cụ thể — lên ở bến đầu). Response trả null khi vé không có trạm lên/xuống; FE hiển thị rỗng
khi null. Đổi để contract khớp sự thật của entity thay vì ép service bịa một tên trạm.

#### `GET /tickets/me` — danh sách vé của chính người gọi

> Task *"API tra cứu vé của tôi (đã mua / đã dùng / đã huỷ)"* (dòng 45, US 4, Sprint 3, Trần Trung
> Hiếu). Hai endpoint này dùng chung bề mặt `api/tickets` với các task soát vé của Sprint sau — mỗi
> task một controller, cùng lối `api/seat-holds` và `api/vouchers`.

- `[Authorize]` trần (không policy): hành khách tự xem vé của mình — RBAC của dự án chỉ có
  AdminOnly/ManagerOrAbove, không có policy Passenger. Phạm vi "của tôi" nằm ở truy vấn theo
  `UserId`, không có tham số nào để dò vé người khác.
- Trả về **mảng trần** — không bọc `{ items, total }`, không phân trang: một người chỉ có vài chục
  vé (cùng lý do `GET /monthly-passes/me` và `GET /feedbacks/me`). Không có vé nào → **mảng rỗng**
  (200) — "chưa có vé" là câu trả lời, không phải lỗi.
- **Không hứa thứ tự** — màn "Vé của tôi" tự sắp lại (vé chưa khởi hành trước, rồi mới tới vé đã
  đi / đã huỷ — xem `MyTicketsPage.sortByDeparture`).
- Không nhận tham số lọc trạng thái: màn "Vé của tôi" lọc `Paid`/`Used`/`Cancelled` ngay trên
  client (bản nháp `ticketApi.ts` cũng gọi không tham số). "Hết hạn" không phải trạng thái lưu —
  FE suy từ `departureTime`, đúng như `TicketStatus` đã chốt.
- Không có token → 401. Không có nhánh lỗi nào khác.

```json
// 200 — mảng trần, mỗi phần tử đúng bảng trường "Hình dạng Ticket" ở trên
[
  {
    "id": "b7c1d2e3-0000-0000-0000-000000000000",
    "code": "SBT1:…",
    "routeCode": "01",
    "routeName": "Bến xe Mỹ Đình — Bến xe Gia Lâm",
    "origin": "Bến xe Mỹ Đình",
    "destination": "Bến xe Gia Lâm",
    "departureTime": "2026-10-12T01:00:00Z",
    "seatNumber": "A12",
    "boardingStopName": null,
    "alightingStopName": null,
    "price": 12000,
    "status": "Paid",
    "createdAt": "2026-10-10T08:30:00Z",
    "usedAt": null
  }
]
```

#### `GET /tickets/{id}` — một vé của chính người gọi

Màn "Mã QR vé" mở trực tiếp một vé trong danh sách bằng id trên URL (`/my-tickets/{id}/qr`).

- `[Authorize]` trần; vé không tồn tại **hoặc thuộc người khác** → **404** (không phải 403 — không
  để lộ vé nào đang tồn tại, cùng lối `GET /seat-holds/{sessionCode}` và `GET /feedbacks/me/{id}`).
- Ràng buộc `{id:guid}` — cùng lý do các bề mặt có đoạn literal (`validate`, `statistics`): id
  không phải GUID thì routing trả 404 chứ không chui vào endpoint.
- Response là một đối tượng Ticket đúng bảng trường ở trên.

### Soát vé QR — `POST /tickets/validate`

> Task *"API soát vé: nhận mã QR, xác thực chữ ký, trả kết quả hợp lệ/không"* (dòng 15, US 15,
> Sprint 4) + *"Phân quyền: chỉ tài xế/phụ xe được soát vé"* (dòng 19) — Trần Trung Hiếu. Hình dạng
> này là bản chốt của đề xuất ở `docs/29-ban-giao-api-kiem-tra-hieu-luc-qr.md` §3 và `docs/34` §3,
> đã khớp bảng `TicketValidations` (PR #188) và service ghi nhật ký `ITicketValidationRecorder`
> (dòng 18). Luồng ba bước — xác thực chữ ký → tra vé → ghi nhật ký — đặc tả ở
> `docs/41-api-soat-ve-qr.md`.

#### Phân quyền

- `[Authorize(Roles = "Driver,Manager")]` — chỉ Tài xế/Quản lý được soát (docs/34 §6). Không có
  token → 401; vai trò khác → 403.
- Không kiểm "đúng tài xế của chuyến này" — mọi Tài xế đều soát được mọi chuyến (phụ xe phụ trách
  nhiều chuyến là chuyện thường; dòng 19 chỉ chốt vai trò).

#### Request

```
POST /api/tickets/validate
Authorization: Bearer <JWT>          ← Driver hoặc Manager
Body:  { "code": "SBT1:…", "tripId": "…uuid…", "clientScanId": "…uuid…", "scannedAt": "…ISO-8601…" }
```

| Trường request | Kiểu | Ghi chú |
|---|---|---|
| `code` | `string`, bắt buộc | NGUYÊN VĂN chuỗi mã QR quét được, định dạng `SBT1:{ticketId}.{blob}` (106 ký tự, docs/30). Server tự xác thực chữ ký. Thiếu/rỗng → 400 `errors.code`; dài quá 200 ký tự → 400 `errors.scannedCode` |
| `tripId` | `string` (GUID), tuỳ chọn | Chuyến phụ xe ĐANG CHẠY lúc quét — nguồn duy nhất kết luận được `WrongTrip`. Chuyến không tồn tại → **404**. Thiếu hẳn → bỏ phép đối chiếu chuyến |
| `clientScanId` | `string` (GUID), tuỳ chọn | Mã lượt soát do MÁY QUÉT sinh NGAY LÚC QUÉT — khoá chống trùng khi hàng đợi ngoại tuyến đồng bộ lại (docs/39 §4.1). Vắng cả hai trường ngoại tuyến = lượt online kiểu cũ |
| `scannedAt` | `string` (ISO-8601), tuỳ chọn | Giờ máy quét ghi nhận lượt soát. Ngoài cửa sổ hợp lý (nhanh hơn server quá 5 phút / cũ hơn 72 giờ) → 400 `errors.scannedAt` |

#### Response — mọi kết quả soát đều là 200

"Vé không hợp lệ" là **câu trả lời**, không phải lỗi HTTP (cùng lối `POST /vouchers/validate`):

```jsonc
// 200 — hợp lệ, được lên xe
{ "valid": true, "reason": null, "ticket": { /* ValidatedTicket */ } }

// 200 — bị từ chối, kèm lý do (năm giá trị, đã chốt với màn quét QR)
{ "valid": false, "reason": "NotFound",    "ticket": null }
{ "valid": false, "reason": "AlreadyUsed", "ticket": { /* ValidatedTicket, kèm usedAt */ } }
```

| `reason` | Nghĩa | `ticket` |
|---|---|---|
| `null` | Vé hợp lệ, được lên xe | Có |
| `NotFound` | Không tra ra vé — **gộp cả** mã rác lẫn mã sai chữ ký lẫn mã ký cho vé không tồn tại (docs/34 §3) | `null` |
| `Cancelled` | `Status = 'Cancelled'` | Có |
| `AlreadyUsed` | `Status = 'Used'` — `ticket.usedAt` kèm theo để phụ xe đối chiếu | Có |
| `WrongTrip` | Vé đúng nhưng của chuyến KHÁC `tripId` đã gửi — chỉ kết luận được khi có `tripId` | Có |
| `Expired` | Chuyến đã khởi hành (quá `departureTime`), hoặc chuyến `Completed`/`Cancelled` mà vé còn `Paid` | Có |

Thứ tự xét: `NotFound` → `Cancelled` → `AlreadyUsed` → `WrongTrip` → `Expired` → `Valid`.

**Mọi lượt quét đều được ghi** một dòng vào bảng `TicketValidations` — kể cả lượt bị từ chối —
qua `ITicketValidationRecorder` (docs/34 §2.2). Endpoint **KHÔNG** lật `Ticket.Status = Used` —
việc của dòng 16 (Kiên); cho tới khi dòng 16 có, hai lượt soát cùng một vé còn `Paid` đều trả
`Valid` và mỗi lượt ghi một dòng.

**Gửi lại cùng `clientScanId`** (mất mạng, app bị kill, thử lại) → 200 với **kết quả ĐÃ GHI của lần
đầu**, không tính lại — kể cả khi trạng thái vé đã đổi từ lần đầu (docs/39 §4.2).

| Lỗi thật sự | Mã |
|---|---|
| Thiếu/rỗng `code`, sai khuôn body | 400 `{ message, errors }` |
| `scannedCode` quá dài, `scannedAt` ngoài cửa sổ | 400 `{ message, errors }` (do recorder) |
| `tripId` trỏ chuyến không tồn tại | 404 |
| Hết phiên đăng nhập | 401 |
| Vai trò không phải Tài xế/Quản lý | 403 |

#### Hình dạng `ValidatedTicket` — vé kèm kết quả soát

Khớp interface `ValidatedTicket` đã viết sẵn ở `frontend/src/api/ticketValidationApi.ts` (Băng, dòng
20) CỘNG thêm `usedAt` (docs/29 §3: lý do `AlreadyUsed` phải kèm giờ đã soát). Khác bảng trường
Ticket của `/tickets/me` ở hai điểm, đều là chủ ý của màn soát: có `passengerName` (phụ xe đối chiếu
khách), bỏ `origin`/`destination`/`price`/`createdAt` (không cần lúc soát).

| Trường | Kiểu | Mô tả |
|---|---|---|
| `id` | `string` (GUID) | Khoá chính vé |
| `code` | `string` | Mã vé = nội dung mã QR (đúng chuỗi đã lưu trên vé) |
| `routeCode` / `routeName` | `string` | Tuyến của chuyến |
| `departureTime` | `string` | Giờ khởi hành (ISO 8601) |
| `seatNumber` | `string` | Số ghế |
| `boardingStopName` / `alightingStopName` | `string \| null` | Trạm lên/xuống — null khi vé không có trạm cụ thể |
| `passengerName` | `string \| null` | Họ tên chủ vé — null khi không tra được tài khoản |
| `status` | `string` | `"Paid"` / `"Used"` / `"Cancelled"` |
| `usedAt` | `string \| null` | Giờ đã soát — chỉ khi `status = 'Used'` |

#### Câu hỏi mở — vé tháng

`MonthlyPasses.Code` **cũng** là mã QR soát vé (A6). Endpoint này **chỉ tra bảng `Tickets`** — mã vé
tháng quét vào trả `NotFound` (vé tháng hiện chưa có định dạng `SBT1:` nên chữ ký không khớp). Đây là
câu hỏi treo từ `docs/29` §4 câu (4), `docs/34` §7.5 chưa ai chốt — **nhóm cần quyết**: phụ xe quét
vé tháng thì dùng chung endpoint này (khi đó bảng `TicketValidations` cần thêm cột trỏ
`MonthlyPasses` — việc của Dăm, và cần thêm lý do mới cho "vé tháng hết hạn/sai tuyến" — đổi hợp đồng
với màn quét) hay tách endpoint riêng. Ghi rõ ở `docs/41-api-soat-ve-qr.md` §6.

### Phát hành vé sau khi thanh toán — KHÔNG phải endpoint riêng

Đây là **service nội bộ**, gọi từ hai nơi khi giao dịch về `Success`: callback của cổng (IPN) và
job đối soát (mục "Thanh toán" dưới). Mỗi lần gọi làm một chuỗi:

1. Chống phát hành trùng: mỗi `paymentCode` chỉ phát hành **một lần** — kiểm tra trạng thái giao
   dịch trước khi đổi (idempotency, task của Hoàng — xem mục Thanh toán).
2. Sinh vé: tạo `Ticket` với `status = 'Paid'`, `code` = mã QR duy nhất — **service sinh mã QR là
   task của Nguyễn Duy Kiên** (*"Service sinh mã QR duy nhất + ký số chống làm giả"*); nếu chưa có,
   tạm dùng `Guid` để đứng vững khung luồng rồi nối service của Kiên sau.
3. Chốt chỗ: lật `SeatHolds` của phiên sang `Confirmed` + ghi `SeatHoldLogs` `Action = 'Confirmed'`
   (docs/26 §1) — đây là lúc ghế chính thức thuộc về vé.
4. Trả vé để FE hiển thị QR ngay sau khi khách quay về từ cổng.

> **Vì sao không phải endpoint?** Không có người gọi hợp lệ nào: hành khách không tự phát hành vé
> (chỉ cổng mới biết tiền đã về), và FE không nên là nơi kích hoạt việc sinh vé — vé phải tồn tại
> NGAY KHI giao dịch thành công, kể cả lúc khách tắt máy chưa quay về. Nơi duy nhất biết "tiền đã
> về" là IPN và đối soát, cả hai đều ở backend.

### Chống bán trùng ghế (A6)

Partial unique index trên `Tickets` — cùng tinh thần index đang giữ `SeatHolds`:

```sql
CREATE UNIQUE INDEX "IX_Tickets_TripId_SeatId_Active"
    ON "Tickets" ("TripId", "SeatId")
    WHERE "Status" IN ('Held', 'Paid');
```

Vé đã huỷ (`Cancelled`) không chặn ghế. EF Core không sinh được partial index bằng attribute —
phải viết trong `OnModelCreating` (file của Dăm).

⚠️ Nhánh `'Held'` trong điều kiện trên là **dự phòng**: enum `TicketStatus` chỉ có
`Paid`/`Used`/`Cancelled` (đúng bảng trường ở trên), vé sinh thẳng ở `Paid` nên không dòng nào mang
`'Held'`. Giữ nguyên chữ của A6 — ai truy vấn ghế đã bán chỉ cần hỏi `'Paid'`.

## Huỷ & Đổi vé — `/cancellation-requests` và `/ticket-changes`

> Story 5 *"Hủy & Đổi vé"*. ✅ **Hai bảng đã có thật**: migration
> `Sprint4_CancellationRequests_TicketChanges` (Vàng Thị Dăm, task dòng 2 Sprint 4) đã merge và **đã
> áp lên CSDL chung**. Thiết kế đầy đủ — vì sao hai bảng, vì sao điều kiện thời gian, vì sao vé cũ
> bị HUỶ chứ không bị sửa: `docs/32-csdl-huy-doi-ve.md`.
>
> Mục này chốt **ba endpoint**: hai endpoint duyệt — task *"API duyệt / từ chối yêu cầu huỷ - đổi vé"*
> (dòng 5 Sprint 4, Phùng Duy Hoàng) — và một endpoint gửi yêu cầu đổi chuyến — task *"API gửi yêu
> cầu đổi chuyến + tính chênh lệch giá"* (dòng 4, Nguyễn Duy Kiên). `POST /cancellation-requests`
> (Trần Trung Hiếu, dòng 3) vẫn **chưa có trong tài liệu này**. Cả bốn endpoint dùng chung **một hình
> dạng dòng** ở dưới, nên màn duyệt của Admin và màn theo dõi của khách đọc được cả hai loại yêu cầu
> bằng một khuôn.

### Hình dạng một dòng yêu cầu — chốt theo bản nháp FE

Hai bảng có tên cột giống nhau ở phần chung (đúng chủ ý của Dăm — xem `docs/32` §3.4), nên hình dạng
response cũng là một. Trường nào của riêng một loại thì **null ở loại kia**, không lược bỏ khỏi JSON
(giữ đúng lối `boardingStopName` của mục Vé điện tử).

| Trường | Kiểu | Mô tả |
|---|---|---|
| `id` | `string` (GUID) | Khoá chính |
| `type` | `string` | `"Cancel"` / `"Change"` — **suy từ endpoint, không có cột trong CSDL**: nó là tên bảng, không phải dữ liệu |
| `ticketId` | `string` (GUID) | Vé gốc — FE mở thẳng `GET /tickets/{ticketId}` từ đây |
| `ticketCode` | `string` | Mã vé gốc (nội dung QR). Với `"Change"` **đã duyệt** thì vé này đã là `Cancelled` |
| `status` | `string` | `"Pending"` / `"Approved"` / `"Rejected"` |
| `price` | `number` | Giá vé GỐC đã thanh toán (VND) — chụp lúc phát hành vé |
| `reason` | `string \| null` | Lý do khách gửi kèm |
| `cancellationFee` | `number \| null` | **Chỉ `"Cancel"`** — phí huỷ, chụp lúc khách gửi; `0` = huỷ miễn phí. Có giá trị ngay cả khi còn `Pending` |
| `refundAmount` | `number \| null` | **Chỉ `"Cancel"` VÀ `status = "Approved"`** — `price - cancellationFee`. Null ở mọi ca còn lại, kể cả `"Rejected"` (từ chối thì không khoản nào di chuyển — trả `price - fee` ở đó là nói dối) |
| `newTripId` | `string \| null` | **Chỉ `"Change"`** — chuyến khách xin đổi sang |
| `newSeatId` | `string \| null` | **Chỉ `"Change"`** — ghế khách xin ngồi ở chuyến mới |
| `newPrice` | `number \| null` | **Chỉ `"Change"`** — giá chuyến mới, chụp lúc khách gửi |
| `priceDifference` | `number \| null` | **Chỉ `"Change"`** — `newPrice - price`, **có dấu**: dương = khách trả thêm, âm = nhà xe hoàn lại, `0` = ngang giá |
| `newTicketId` | `string \| null` | **Chỉ `"Change"` ĐÃ DUYỆT** — vé mới phát hành lúc duyệt; null khi `Pending`/`Rejected`. Mở vé mới bằng `GET /tickets/{newTicketId}` |
| `reviewedByUserId` | `string \| null` | Quản lý đã xử lý |
| `reviewedAt` | `string \| null` | Mốc bấm duyệt / từ chối |
| `rejectReason` | `string \| null` | Lý do từ chối do quản lý nhập — chỉ có nghĩa khi `"Rejected"` |
| `createdAt` | `string` | Thời điểm khách gửi |
| `updatedAt` | `string \| null` | Lần sửa cuối (null khi còn `Pending`) |

> ⚠️ **Hai trường đổi tên so với bản nháp `frontend/src/api/ticketRequestApi.ts`** (bản nháp tự ghi
> *"DỰ KIẾN … CHƯA cam kết"*, nên đây là chốt chứ không phải phá hợp đồng): `reviewNote` →
> **`rejectReason`** (khớp nguyên tên cột `RejectReason` và tên trường mà thông báo đọc — xem mục
> `/hubs/notifications`), và `fee` tách thành **`cancellationFee`** / **`priceDifference`** (một cái
> tên mang hai nghĩa khác nhau tuỳ `type` là thứ sẽ cắn người đọc code sau này). Bản nháp cũng
> **thiếu** `price`, `newPrice`, `ticketId`, `reviewedByUserId`, `reviewedAt`, `newTicketId` — đều là
> trường màn duyệt của Admin cần (Hạnh, dòng 11).

> **Vì sao `refundAmount` là trường SUY RA, không phải cột:** `docs/32` §3.1 đã chốt số phải hoàn là
> `Ticket.Price - CancellationRequest.CancellationFee`, và cố ý KHÔNG đặt cột thứ ba — cả hai vế đều
> bất biến nên phép trừ không bao giờ lệch, còn thêm cột thì có thêm một chỗ để ba con số nói khác
> nhau. Ở đây nó được tính trong cùng một response với hai vế của nó, nên không có khe thời gian nào
> để lệch. Số tiền **đã** hoàn thật nằm ở bảng `Refunds` (US 8, sprint khác) — không phải trường này.

### Endpoints

| Method | Endpoint | Task | Trạng thái |
|---|---|---|---|
| PATCH | `/cancellation-requests/{id}` | *"API duyệt / từ chối yêu cầu huỷ - đổi vé"* — Phùng Duy Hoàng (dòng 5) | ✅ mục dưới |
| PATCH | `/ticket-changes/{id}` | cùng task | ✅ mục dưới |
| POST | `/cancellation-requests` | Trần Trung Hiếu (dòng 3) | ⛔ chưa có trong tài liệu này |
| POST | `/ticket-changes` | Nguyễn Duy Kiên (dòng 4) | ✅ mục "Gửi yêu cầu đổi chuyến" bên dưới |

### Body của cả hai endpoint — giống hệt nhau

```json
// Duyệt
{ "status": "Approved" }

// Từ chối, có nêu lý do
{ "status": "Rejected", "rejectReason": "Chuyến khởi hành dưới 2 giờ nên không đủ điều kiện huỷ." }
```

| Trường | Bắt buộc | Mô tả |
|---|---|---|
| `status` | **Có** | Đúng hai giá trị `"Approved"` / `"Rejected"`. Giá trị khác → 400 kèm `errors.status` |
| `rejectReason` | Không | Chỉ có nghĩa khi `"Rejected"`; tối đa 2000 ký tự. Bỏ trống vẫn hợp lệ — thông báo ghi rõ *"Người duyệt không nêu lý do."* thay vì để cụt |

⚠️ **Gửi `rejectReason` kèm `"Approved"` KHÔNG phải lỗi 400** — service bỏ qua và **không lưu** (cột
`RejectReason` chỉ có nghĩa ở nhánh từ chối; lưu rồi để màn hình hiện "lý do từ chối" trên một yêu
cầu đã duyệt là dữ liệu sai). Cố ý: client đang cầm sẵn một object có cả hai trường (form giữ state
cũ) mà bị chặn ở một lượt gọi hoàn toàn hợp lệ là cái bẫy không đáng có.

### Gửi yêu cầu đổi chuyến — `POST /ticket-changes`

Task *"API gửi yêu cầu đổi chuyến + tính chênh lệch giá"* — Nguyễn Duy Kiên (dòng 4 Sprint 4).

`[Authorize]` trần (không policy): hành khách gửi yêu cầu cho **vé của chính mình**. Người gửi lấy
từ token (`ClaimTypes.NameIdentifier`), **không có trong body**. Vé không thuộc người gọi → **404**
chứ không phải 403 — cùng lối `GET /tickets/{id}`, không tiết lộ vé đó có tồn tại.

**Body:**

```json
{
  "ticketId": "0f5c…",
  "newTripId": "9a1e…",
  "newSeatId": "7b3d…",
  "reason": "Có việc gia đình, muốn đi chuyến sớm hơn."
}
```

| Trường | Bắt buộc | Mô tả |
|---|---|---|
| `ticketId` | **Có** | Vé gốc khách muốn đổi |
| `newTripId` | **Có** | Chuyến muốn đổi sang |
| `newSeatId` | **Có** | Ghế muốn ngồi ở chuyến mới — phải thuộc xe của `newTripId` |
| `reason` | Không | Lý do khách nêu, tối đa 2000 ký tự |

**Endpoint làm gì — và KHÔNG làm gì:**

1. Kiểm vé: tồn tại, **thuộc người gọi** (khác → 404) và còn `status = "Paid"` (đã huỷ / đã soát vé
   → 409).
2. Kiểm **không có yêu cầu đang chờ** trên cùng vé — cả bảng `CancellationRequests` lẫn
   `TicketChanges`. Đây là ràng buộc liên bảng mà hai chỉ mục duy nhất điều kiện không diễn đạt được
   (`docs/32` §4.4): một vé vừa xin huỷ vừa xin đổi thì duyệt cả hai sẽ hoàn tiền cho một vé đã chết
   và phát hành thêm một vé mới từ nó.
3. Kiểm chuyến mới: tồn tại (404), còn `Scheduled` và chưa tới giờ khởi hành (409).
4. Kiểm ghế mới: tồn tại (404), thuộc **xe của chuyến mới** (404), và còn trống — không có lượt
   `SeatHolds` nào đang `Holding` và không có vé `Paid` nào khác chiếm ghế (409).
5. Chụp giá: `newPrice` = giá `Standard` của tuyến mà chuyến mới chạy — **cùng nguồn với
   `GET /trips/search` và `GET /trips/{id}/seats`**. Tuyến chưa cấu hình giá → 409 (không có con số
   để khách đồng ý). Con số được **chụp lại** vào `TicketChanges.NewPrice`, không tra lại lúc duyệt.
6. Ghi một dòng `TicketChanges` ở trạng thái `Pending`. **Không đụng dòng vé**: vé gốc vẫn `Paid`,
   khách còn nguyên chuyến cũ. **Không giữ ghế mới** — chủ ý của thiết kế (`docs/32` §3.3), nên
   `PATCH /ticket-changes/{id}` phải kiểm lại ghế ngay lúc duyệt.

> 🔴 **Chênh lệch giá KHÔNG ghi vào CSDL.** Nó là `newPrice - price` trả về trong response, **có
> dấu**: dương = khách trả thêm, âm = nhà xe hoàn lại, `0` = đổi ngang giá. Hai vế đều bất biến (giá
> vé chụp lúc phát hành, `newPrice` chụp lúc gửi), nên thêm cột thứ ba chỉ tạo thêm một chỗ để các
> con số nói khác nhau. Tiền chênh thật đi qua `Payments` (thu thêm) hoặc `Refunds` (hoàn lại) ở
> sprint tương ứng — endpoint này **không** ghi dòng thanh toán/hoàn tiền nào: yêu cầu còn `Pending`,
> chưa ai duyệt, và một khoản tiền ghi trước khi duyệt là khoản tiền thu/hoàn sai.

**Response — `201 Created`, đúng hình dạng dòng chung ở trên:**

```json
{
  "id": "…",
  "type": "Change",
  "ticketId": "0f5c…",
  "ticketCode": "SBT1:…",
  "status": "Pending",
  "price": 250000,
  "reason": "Có việc gia đình, muốn đi chuyến sớm hơn.",
  "cancellationFee": null,
  "refundAmount": null,
  "newTripId": "9a1e…",
  "newSeatId": "7b3d…",
  "newPrice": 300000,
  "priceDifference": 50000,
  "newTicketId": null,
  "reviewedByUserId": null,
  "reviewedAt": null,
  "rejectReason": null,
  "createdAt": "2026-10-17T…Z",
  "updatedAt": null
}
```

| Mã | Khi nào |
|---|---|
| 201 | Đã ghi yêu cầu `Pending` — trả dòng vừa tạo |
| 400 | Thiếu `ticketId`/`newTripId`/`newSeatId`; `reason` quá 2000 ký tự |
| 401 | Không có token |
| 404 | Vé không tồn tại **hoặc không thuộc người gọi**; chuyến mới không tồn tại; ghế mới không tồn tại hoặc không thuộc xe của chuyến mới |
| 409 | Vé không còn `Paid`; vé đang có yêu cầu huỷ/đổi chờ duyệt; chuyến mới đã huỷ/đã khởi hành; ghế mới đã có người đặt; tuyến của chuyến mới chưa có giá |

Chỉ mục `IX_TicketChanges_TicketId_Pending` vẫn là lưới thật cho nhánh đua "hai tab cùng gửi": hai
lượt cùng đọc "chưa có yêu cầu nào" rồi cùng ghi, lượt thua đâm chỉ mục và service bắt
`DbUpdateException` → **409** cùng câu "đang có yêu cầu chờ duyệt", không phải 500.

### Duyệt / từ chối yêu cầu HUỶ — `PATCH /cancellation-requests/{id}`

`[Authorize(Policy = RbacPolicies.ManagerOrAbove)]` — Admin và Manager. Người duyệt lấy từ token
(`ClaimTypes.NameIdentifier`), **không nhận từ body**: để client gửi lên là mở đường cho người này
ghi danh người khác vào `ReviewedByUserId`.

**Nhánh `Approved`:**
1. Lật `status` → `Approved`, ghi `reviewedByUserId` + `reviewedAt` + `updatedAt`.
2. Lật vé gốc `Ticket.Status` → `Cancelled` (kèm `Ticket.UpdatedAt` — A4 buộc, vì đây là sửa dòng).
   **Ghế trở về chợ**: chỉ mục duy nhất điều kiện của A6 (`WHERE "Status" IN ('Held', 'Paid')`)
   không còn thấy vé này, nên khách khác đặt lại được chính ghế đó. Xem
   `CancellationRequestStatus.Approved` để biết vì sao vé CHỈ bị huỷ ở bước này chứ không phải lúc
   khách gửi.
3. KHÔNG ghi dòng `Refunds` nào — bảng đó thuộc US 8 và **chưa tồn tại trong repo**. Tiền hoàn là
   việc của sprint khác; duyệt xong mà lượt hoàn còn chưa chạy là chuyện bình thường.

**Nhánh `Rejected`:** lật `status` + ghi `rejectReason` + `reviewedAt`/`reviewedByUserId`/`updatedAt`.
**Không đụng dòng vé** — vé giữ `Paid`, ghế vẫn của khách, không khoản tiền nào di chuyển.

### Duyệt / từ chối yêu cầu ĐỔI — `PATCH /ticket-changes/{id}`

Cùng quyền, cùng body, cùng hình dạng response.

**Nhánh `Approved` — ba việc, không phải hai:**
1. 🔴 **Kiểm lại ghế mới còn trống NGAY LÚC DUYỆT.** Lúc khách gửi, ghế mới **chưa bị giữ** (không có
   dòng `SeatHolds` nào cho nó — xem `TicketChangeStatus.Pending`), nên giữa hai thời điểm đó người
   khác hoàn toàn có thể đã mua mất. Tin dữ liệu đã kiểm lúc gửi là **bán trùng ghế**. Ghế đã có
   người → **409**, màn duyệt nói rõ để quản lý từ chối và mời khách chọn chuyến khác.
2. **Huỷ vé cũ**: `Ticket.Status` → `Cancelled` + `UpdatedAt` (ghế cũ về chợ).
3. **Phát hành vé MỚI** cho chuyến mới: `TripId = newTripId`, `SeatId = newSeatId`, `UserId` = chủ vé
   gốc, `Status = Paid`, `Price = newPrice` (đúng con số khách đã nhìn thấy và đã đồng ý — **không
   tra lại `Fares`**), `Code = ITicketQrService.GenerateCode(newTicketId)`. Rồi ghi `NewTicketId` vào
   dòng yêu cầu.

> 🔴 **Vì sao HUỶ vé cũ chứ không SỬA nó** (chốt 12/10/2026, `docs/32` §3.1): sửa thẳng `Trips`/`Seats`
> trên dòng vé cũ là viết lại lịch sử — chuyến CŨ mất dòng đó khỏi báo cáo doanh thu (US 19) dù tiền
> đã thực thu, và hoá đơn đã xuất theo `Payments.TicketId` không còn khớp (US 7). Vé cũ phải giữ
> nguyên con số của nó.

**Nhánh `Rejected`:** không đụng vé, không kiểm ghế, `NewTicketId` vẫn null.

> ⚠️ **Vé mới phát hành ở đây KHÔNG đi qua bảng `Payments`.** Chênh lệch giá (`newPrice - price`) do
> Kiên tính ở `POST /ticket-changes` (dòng 4), và tiền chênh đi qua `Payments` (thu thêm) hoặc
> `Refunds` (hoàn lại) — hai bảng đã có, không phải việc của endpoint duyệt. Endpoint này chỉ phát
> hành vé đúng theo con số đã chốt trong dòng yêu cầu.

### Trạng thái HTTP — giống nhau ở cả hai endpoint

| Mã | Khi nào |
|---|---|
| 200 | Xử lý xong — trả dòng yêu cầu đã cập nhật |
| 400 | `status` thiếu / sai giá trị; `rejectReason` quá dài |
| 401 | Không có token |
| 403 | Có token nhưng không phải Admin/Manager |
| 404 | Không có yêu cầu với `{id}` đó |
| 409 | Yêu cầu **không còn `Pending`** (đã có người xử lý) — xem dưới |
| 409 | Vé của yêu cầu **không còn `Paid`** (đã bị huỷ, hoặc đã soát ở cổng) — xem dưới |
| 409 | *(chỉ `/ticket-changes`)* Chuyến xin đổi sang **đã huỷ hoặc đã chạy xong** |
| 409 | *(chỉ `/ticket-changes`)* Ghế mới đã có người đặt trong lúc chờ duyệt |

### Hai lớp chống xử lý trùng — cùng lối A6

Hai quản lý cùng mở một yêu cầu `Pending` rồi cùng bấm Duyệt là chuyện xảy ra được. Chỉ mục duy nhất
điều kiện (`WHERE "Status" = 'Pending'`) **không cứu được** lượt này: lúc cả hai ghi thì cả hai đều
đã rời khỏi tập `Pending`. Nên có hai lớp:

1. **Kiểm trước**: đọc thấy `status != Pending` → **409** kèm câu nói rõ yêu cầu đã được xử lý.
2. **Khoá lạc hậu `xmin`**: hai bảng này có concurrency token (khác `Tickets` — bảng vé cố ý không
   có, xem `AppDbContext.Ticket.cs`). Lượt thua nhận `DbUpdateConcurrencyException`; service bắt và
   trả **cùng một câu 409** — ⛔ **tuyệt đối không để nó thành 500**. Đây chính là điều `docs/32` §6
   dặn riêng cho task này.

Cùng lối, lượt duyệt đổi vé đâm vào chỉ mục A6 của bảng `Tickets` sẽ ném `DbUpdateException`; bắt và
trả **409** "ghế đã có người đặt", không phải 500.

### Hai phép kiểm mà CSDL không làm hộ

Lượt duyệt còn phải tự kiểm hai điều dưới đây — **không chỉ mục nào chặn được chúng**, nên bỏ qua là
ghi ra dữ liệu sai chứ không phải ném lỗi:

- **Vé gốc còn `Paid` không** (cả hai endpoint). Hai đường dẫn tới cùng chỗ hỏng:
  1. Một vé có thể có **đồng thời** một yêu cầu huỷ `Pending` và một yêu cầu đổi `Pending` — chỉ mục
     duy nhất điều kiện nằm *trong từng bảng*, không với được sang bảng kia; luật "một vé chỉ một yêu
     cầu đang chờ" là việc của service (chú thích ngay tại `AppDbContext.CancellationRequest.cs`).
     Duyệt cả hai thì vé bị huỷ lần thứ hai và lượt đổi **phát hành vé mới cho một vé đã chết**.
  2. Vé bị **soát ở cổng** trong lúc yêu cầu nằm chờ duyệt: huỷ một vé đã dùng là hoàn tiền cho
     chuyến khách đã đi.
- **Chuyến xin đổi sang chưa huỷ và chưa chạy xong** (`/ticket-changes`). Vé mới là vé thật, in QR
  được — phát hành nó cho một chuyến không bao giờ chạy là bán một chỗ không tồn tại.

Cả hai đều trả **409** với câu nói rõ đường lui ("hãy từ chối yêu cầu và mời khách chọn chuyến/ghế
khác"), không phải 400: yêu cầu gửi lên hợp lệ, chỉ có thế giới đã đổi kể từ lúc khách gửi.

> ⚠️ **Bộ test chạy EF InMemory KHÔNG dựng unique index lẫn khoá ngoại** (`docs/32` §4.4), nên hai
> nhánh `xmin` và A6 **chỉ chạy thật trên PostgreSQL**. Test xanh trên InMemory không chứng minh hai
> nhánh đó đúng — muốn kiểm phải dựng DDL bằng `GenerateCreateScript()` với chuỗi kết nối Npgsql giả.

### Thông báo cho khách — việc nối tiếp, không phải việc của endpoint

Duyệt/từ chối xong thì gọi `ITicketRequestResultNotifier` (`NotifyCancellationReviewedAsync` /
`NotifyTicketChangeReviewedAsync`) — hợp đồng đầy đủ ở mục `/hubs/notifications` phía trên. Ba điều
bắt buộc ở chỗ gọi:

1. Gọi **SAU khi đã lưu** trạng thái — hàm tự kiểm và bỏ qua kèm cảnh báo nếu còn `Pending`.
2. Hàm **không bao giờ ném** và **không nhận `CancellationToken`**; nó mở lượt lưu riêng, không chung
   transaction với lượt PATCH. Thông báo hỏng không được làm hỏng lượt duyệt đã thành công.
3. Nội dung thông báo **không chứa số tiền** — lý do ở mục `/hubs/notifications`.

## Thanh toán — `/payments`

> Story 6 *"Cổng thanh toán"*. ✅ **Bảng `Payments` — migration ĐÃ SINH, ĐÃ SOÁT VÀ ĐÃ MERGE (PR #144)**
> — entity `Payment` + cấu hình `AppDbContext.Payment.cs` + service chốt idempotent
> (`PaymentSettlementService`) — task *"Xử lý idempotency: chống trừ tiền 2 lần khi callback trùng"*
> (Phùng Duy Hoàng, dựng bảng theo uỷ quyền của Dăm 10/10/2026). Bảng ra đời trong migration
> `Sprint3_Payments_Vouchers_Tickets` (Vàng Thị Dăm), **cùng lệnh** với `Tickets` và `Vouchers`; FK
> `TicketId` → `Tickets` cũng nối trong chính migration đó (`ON DELETE RESTRICT`). Có thật trên CSDL
> chung **sau khi chạy `database update`** — xem `docs/27` và `docs/28` §8.
> ⚠️ **`Transactions` và `PaymentLogs` KHÔNG được dựng** — cả hai không nằm trong danh sách bảng
> đóng băng A9 và không có entity/cấu hình nào trong repo; muốn có thì nhóm phải bổ sung A9 trước.
> Xem `docs/28-csdl-ve-dien-tu.md` §7.
> Phần **client cổng MoMo đã có** trên backend
> (`MoMoGatewayService` — Trần Trung Hiếu): tạo giao dịch, kiểm chữ ký IPN, hỏi trạng thái cổng —
> test bằng HTTP giả, chưa đụng CSDL. **Client cổng VNPay cũng đã có** (`VnPayGatewayService` —
> Nguyễn Duy Kiên, chi tiết ở mục "Cấu hình cổng VNPay" cuối mục này): dựng URL thanh toán ký
> HMAC-SHA512 + kiểm chữ ký tham số cổng trả về — không gọi mạng nên test được ngay. **Client cổng
> ZaloPay cũng đã có** (`ZaloPayGatewayService` — Nguyễn Duy Kiên, mục "Cấu hình cổng ZaloPay"):
> tạo đơn, xác thực callback, hỏi trạng thái — test bằng HTTP giả như MoMo. Endpoint dưới
> đây sẽ có khi migration xong; mục này chốt hình dạng để FE (`frontend/src/api/paymentApi.ts` —
> Băng đã code màn chờ, Hạnh đang code màn chọn phương thức) dựa vào.

> ⚠️ **Việc cần báo nhóm (xung đột tài liệu):** bảng phân công Sprint 3 giao migration Payments ở
> Sprint 3, nhưng quy ước A9 xếp bảng `Payments` ở **Sprint 4**, và `Transactions`/`PaymentLogs`
> **không nằm trong 20 bảng đóng băng của A9**. `docs/02-sprint-roadmap.md` cũng xếp story 4 và 6
> vào Sprint 4. Nhóm cần chốt một bảng phân công duy nhất (cùng cách xử lý "Xung đột #7" trong
> quy ước mục J). Mục này viết theo bảng phân công Sprint 3 mà Hiếu nhận.

### Enum chốt — gộp hai bản nháp FE

Hai bản nháp FE đang lệch nhau, chốt như sau (FE đổi theo mục này):

| Khái niệm | Bản nháp Băng (màn chờ) | Bản nháp Hạnh (màn chọn) | **Chốt** | Vì sao |
|---|---|---|---|---|
| Mã phương thức | `'MoMo'\|'VNPay'\|'ZaloPay'\|'BankCard'` | `'VnPay'\|'Momo'\|'ZaloPay'\|'BankTransfer'\|'Cash'` | **`'MoMo'\|'VNPay'\|'ZaloPay'\|'BankCard'`** | Đúng danh sách US 6; `BankTransfer`/`Cash` không có cổng nào triển khai trong dự án; viết hoa CamelCase đúng A3 |
| Trạng thái giao dịch | `'Pending'\|'Success'\|'Failed'` | `'Paid'` | **`'Pending'\|'Success'\|'Failed'`** | Khớp màn chờ: giao dịch có trạng thái trung gian Pending; `Paid` là trạng thái của **vé**, không phải giao dịch |
| Khoá hỏi trạng thái | `paymentCode` | `transactionId` | **`paymentCode`** (nội bộ) + **`gatewayTransactionId`** (của cổng) | Cần cả hai: mã nội bộ là khoá chống trùng (A9), mã cổng để đối soát; Băng đã code đủ hai trường |

#### Bốn mã phương thức đi cổng nào — chốt

`'MoMo'|'VNPay'|'ZaloPay'|'BankCard'` là **mã phương thức của hợp đồng, KHÔNG phải tên cổng**: ba
mã đầu trùng tên cổng, mã thứ tư thì không. Chốt ở task *"Tích hợp ZaloPay và thẻ ngân hàng"*
(Nguyễn Duy Kiên):

| `methodCode` | Cổng thật | Kênh thanh toán |
|---|---|---|
| `MoMo` | MoMo | ví MoMo (`requestType = captureWallet`) |
| `ZaloPay` | ZaloPay | ví ZaloPay (`embed_data.preferred_payment_method`) |
| `VNPay` | VNPay | để trống `vnp_BankCode` — **khách tự chọn** trên trang VNPay (QR, thẻ ATM, thẻ quốc tế) |
| `BankCard` | **VNPay** | ghim `vnp_BankCode = VNBANK` — **thẻ ATM nội địa / tài khoản ngân hàng** |

**Vì sao `BankCard` chạy qua VNPay:** dự án không có cổng riêng nào cho "thẻ ngân hàng", còn VNPay
đã có sẵn kênh thẻ trong cùng một cổng. Nhờ vậy hai mã khác nhau thật: `VNPay` để khách tự chọn,
`BankCard` ghim thẳng kênh thẻ nội địa. VNPay còn mã `INTCARD` cho thẻ quốc tế — muốn tách "thẻ
quốc tế" thành mã riêng thì thêm một adapter nữa, **không đổi bốn mã này**.

Hệ quả ở tầng adapter: `BankCard` là một adapter riêng (`BankCardGatewayAdapter`) nhưng **đứng trên
đúng client VNPay** — chỉ khác tham số lúc khởi tạo, còn phần đọc callback dùng chung, vì cổng trả
về y hệt nhau (cùng `vnp_SecureHash`, cùng `vnp_TxnRef`). Endpoint không cần biết điều này: nó tra
`methodCode` như mọi cổng khác.

### Endpoints

| Method | Endpoint | Task | Trạng thái |
|---|---|---|---|
| GET | `/payment-methods` | Danh sách phương thức đang mở — chưa có chủ trong bảng phân công (gợi ý: thuộc phần adapter của Hoàng, màn của Hạnh) | Chưa làm |
| POST | `/payments` | Tạo giao dịch + gọi cổng — Trần Trung Hiếu | Chờ bảng |
| POST | `/payments/momo/callback` | IPN MoMo — Trần Trung Hiếu | Chờ bảng |
| GET | `/payments/vnpay/callback` | IPN VNPay (**GET + query string**, khác MoMo POST JSON) — Nguyễn Duy Kiên | Chờ bảng |
| GET | `/payments/{paymentCode}` | *"API kiểm tra trạng thái giao dịch"* — Trần Trung Hiếu | Chờ bảng |
| — | *job đối soát* | *"đối soát tự động"* — Trần Trung Hiếu | Chờ bảng — chốt dưới |

#### `POST /payments` — tạo giao dịch (đã có client cổng, chờ bảng để lưu)

```json
// CreatePaymentRequest — body
{
  "methodCode": "MoMo",
  "tripId": "b7c1d2e3-0000-0000-0000-000000000000",
  "seatNumbers": ["A1", "A2"],
  "total": 48000,
  "voucherCode": "SUMMER10"
}
```

| Trường | Bắt buộc | Ràng buộc |
|---|---|---|
| `methodCode` | ✅ | `'MoMo'\|'VNPay'\|'ZaloPay'\|'BankCard'` — **cả bốn mã đã có adapter**, `BankCard` đi qua kênh thẻ của VNPay (bảng ánh xạ ở mục Enum chốt trên). Mã ngoài bốn mã này → **400** "chưa mở" kèm danh sách cổng đã mở. Cổng có adapter nhưng **thiếu cấu hình khoá** → **502** nêu đúng tên khoá thiếu (lỗi triển khai, khác hẳn "chưa mở") |
| `tripId` | ✅ | Chuyến tồn tại |
| `seatNumbers` | ✅ | Các ghế đang giữ thuộc phiên của người gọi — chỉ để lưu vết, không phải khoá nghiệp vụ |
| `total` | ✅ | Tổng tiền **TRƯỚC giảm giá**, khớp tổng giá các ghế đã chọn. Mọi ràng buộc giá trị đơn (`minOrderValue` của voucher, trần/sàn của cổng) đều so với con số NÀY, không phải số phải trả |
| `voucherCode` | ❌ | Mã voucher khách **đã bấm xác nhận** ở màn thanh toán (khác `POST /vouchers/validate` — đó là xem trước). Vắng/null = không dùng mã. **Có mà không dùng được → 409** kèm `errors.voucherCode`, xem 🔴 dưới |

> 🔴 **Voucher không hợp lệ ở `POST /payments` trả 409, KHÔNG phải 200 kèm `valid: false`** — ngược
> hẳn với `POST /vouchers/validate`. Lý do: ở `/vouchers/validate` khách còn đang gõ mã, "mã không
> dùng được" là **câu trả lời**; còn ở đây khách đã bấm xác nhận, nên âm thầm thu **đủ tiền** trong
> khi khách tưởng đã được giảm là lỗi nặng nhất của luồng này. Thà chặn ra 409 để FE bắt khách sửa
> mã. Đây cũng là lối `ServiceErrorKind.Conflict` đã dùng cho mọi xung đột nghiệp vụ khác.
>
> Server **kiểm lại voucher từ đầu** lúc tạo giao dịch (`IVoucherRedemptionService` /
> `IVoucherValidationService`), **không tin** `discountAmount` mà FE gửi lên — FE gửi được gì thì
> người ngoài cũng gửi được nấy.

```json
// 200 — CreatePaymentResult: FE dùng payUrl để chuyển khách sang MoMo, rồi poll GET /payments/{paymentCode}
{
  "paymentCode": "PM-8f3a2c1d",
  "originalAmount": 48000,
  "discountAmount": 4800,
  "voucherCode": "SUMMER10",
  "amount": 43200,
  "method": "MoMo",
  "status": "Pending",
  "payUrl": "https://test-payment.momo.vn/v2/gateway/pay?t=..."
}
```

`amount` là **số tiền PHẢI TRẢ, đã trừ giảm giá** — đây là con số gửi lên cổng và là con số khách
thấy ở màn chờ. Không dùng mã thì `originalAmount == amount` và `discountAmount = 0`,
`voucherCode = null` — hình dạng trả về không đổi, FE không phải phân nhánh.

> ⚠️ **Sàn của cổng là ràng buộc của CỔNG, không phải của voucher.** MoMo chỉ nhận `total` từ
> **10.000đ** trở lên. Voucher làm `amount` tụt dưới sàn thì cổng từ chối (MoMo `resultCode != 0`),
> tức khách ăn một lỗi khó hiểu từ cổng chứ không phải một câu rõ nghĩa từ ta. Vì vậy endpoint
> `POST /payments` phải kiểm `amount` theo sàn của **chính cổng khách chọn** và trả **409** kèm
> `errors.voucherCode` ("Mã giảm quá nhiều, số tiền còn lại dưới mức tối thiểu của cổng") trước khi
> gọi cổng. Voucher **không** tự biết sàn này — cắt giảm cho vừa sàn là áp một luật của MoMo lên
> VNPay/ZaloPay.

> 📌 **Số tiền giảm ghi ở đâu.** Entity `Payment` hiện **không có** cột voucher — bản ghi của khoản
> giảm nằm ở `VoucherUsages` (mục "Voucher" dưới), mỗi lượt tiêu thụ một dòng khoá theo
> `paymentCode`. Muốn in "đã giảm 4.800đ" trên hoá đơn thì đọc `VoucherUsages` theo `paymentCode`.
> Task này **cố ý không thêm cột** vào `Payment`: `Entities/Payment.cs` + `AppDbContext.Payment.cs`
> là bàn giao của task dòng 33 (dựng theo uỷ quyền của Dăm) — sửa hình dạng bảng của người khác
> phải bàn trước (⛔4). Nhóm cần chốt có nên đẩy `VoucherCode` + `DiscountAmount` lên thẳng
> `Payments` cho tiện đối soát hay không — xem "Câu hỏi mở" cuối mục "Voucher".

Luồng: backend sinh `paymentCode` (khoá chống trùng — A9), lưu `Payment` `status = 'Pending'`,
gọi `MoMoGatewayService.CreatePaymentAsync` với `orderId = paymentCode` → trả `payUrl` cho FE.
Cổng từ chối (`resultCode != 0`) → **502** kèm `message` của cổng (lỗi phía cổng, không phải lỗi
dữ liệu gửi lên).

Nhánh VNPay: không gọi cổng để XIN URL — gọi `VnPayGatewayService.CreatePaymentUrl` (hàm thuần,
không gọi mạng; URL đã ký sẵn bằng HMAC-SHA512) rồi trả `payUrl` y hệt hình dạng trên, với
`vnp_TxnRef` = `paymentCode`. "Cổng từ chối" với VNPay không xảy ra lúc tạo — khách đứng ngay
trên trang cổng, kết quả biết được qua chữ ký ở Return URL/IPN (mục dưới).

#### `POST /payments/momo/callback` — IPN của MoMo

- **CÔNG KHAI** (không `[Authorize]`): người gọi là máy chủ MoMo, không phải người dùng đăng nhập
  — tin request chỉ dựa trên **chữ ký**, không dựa trên token.
- Hai kiểm tra bắt buộc của tài liệu MoMo, theo đúng thứ tự:
  1. **Chữ ký** `MoMoGatewayService.IsValidCallback` (HMAC-SHA256, so sánh hằng thời gian) —
     sai → **400**.
  2. **Đối chiếu bảng Payments**: `partnerCode`/`orderId`/`amount` khớp bản ghi `Pending` —
     lệch hoặc không tìm thấy → **404** (không xác nhận sự tồn tại cho người ngoài).
- `resultCode = 0` → giao dịch `Success`: lưu `gatewayTransactionId` = `transId`, `paidAt` =
  `responseTime`, rồi gọi service **phát hành vé** (mục "Vé điện tử" trên). `resultCode != 0` →
  `Failed` kèm `message` của cổng.
- **Tiền về thì mới tiêu thụ voucher** — cùng nhánh `resultCode = 0` (và nhánh tương ứng của job
  đối soát), sau khi đã lật `Success`, gọi `IVoucherRedemptionService.RedeemAsync` với
  `paymentCode` của giao dịch. Giao dịch không có mã thì bỏ qua bước này.
  - ⚠️ **Kết quả bước này KHÔNG được làm hỏng callback.** Cổng chỉ chờ 15 giây và gửi lại nếu ta
    trả lỗi; mà giao dịch đã `Success` trong CSDL rồi thì lần gửi lại không chạy lại bước này (nhánh
    idempotency ở trên đã chặn). Nên hỏng ở đây chỉ được **ghi log rồi vẫn trả 204** — bản ghi
    `VoucherUsages` thiếu thì vá được bằng tay, còn tiền thì không.
  - `RedeemAsync` **idempotent** theo `paymentCode`: gọi lại (callback trùng, job đối soát chạy
    đè) trả đúng bản ghi cũ, **không** cộng `usedCount` lần hai. Nhờ vậy hai đường này không cần
    phối hợp với nhau.
- Trả **204 No Content trong 15 giây** — yêu cầu của MoMo; trả lời chậm thì MoMo gửi lại, mà
  gửi lại là trường hợp idempotency (dưới) phải nuốt gọn.
- Idempotency (task của Hoàng): callback trùng / gửi lại → kiểm tra `Payment` đã `Success` hay
  `Failed` rồi thì **bỏ qua, vẫn trả 204** — không trừ tiền 2 lần, không phát hành vé 2 lần.
  Chốt CSDL: `paymentCode` unique trên `Payments` + kiểm trạng thái trước khi đổi.
  Đã cài đặt ở `PaymentSettlementService.SettleAsync` — một cửa chung cho cả callback lẫn job đối
  soát: chữ ký sai → 400 trước khi chạm CSDL; lệch mã đơn/cổng/số tiền → 404; chỉ lật khi còn
  `Pending`, bản gửi lại trả `SettledNow = false`; lượt đua song song chặn ở tầng CSDL bằng
  concurrency token (`xmin` PostgreSQL).

#### `GET /payments/{paymentCode}` — kiểm tra trạng thái giao dịch (task "API kiểm tra trạng thái giao dịch")

Khớp đúng bản nháp Băng đã code (`PaymentStatusResult`):

```json
// 200
{
  "paymentCode": "PM-8f3a2c1d",
  "amount": 48000,
  "method": "MoMo",
  "status": "Pending",
  "paidAt": null,
  "gatewayTransactionId": null,
  "message": null
}
```

- Yêu cầu đăng nhập, chỉ chủ giao dịch xem được (điều kiện `UserId` trong truy vấn — phiên/giao
  dịch của người khác trả **404**, cùng lối `/seat-holds/{sessionCode}`).
- `status` chỉ lật qua **callback hoặc job đối soát** — endpoint này là cửa sổ đọc, không tự hỏi
  cổng (hỏi cổng là việc của job, không phải của từng lần poll của FE).
- Màn chờ poll mỗi 3 giây, tự hết thời gian chờ sau 60 giây — timeout là khái niệm PHÍA FRONTEND,
  không có trạng thái "Timeout" trong CSDL.

#### Job đối soát tự động (task "đối soát tự động")

IPN có thể lọt mất (mạng nghẽn, app restart đúng lúc callback bay tới). Job nền — cùng khuôn
`SeatHoldExpiryBackgroundService` — quét `Payments` còn `Pending` **quá 5 phút**, gọi
`MoMoGatewayService.QueryTransactionAsync(orderId = paymentCode)`:

- `resultCode = 0` → lật `Success`, lưu `transId`, phát hành vé — y hệt nhánh IPN thành công.
- `resultCode != 0` → lật `Failed` kèm `message` của cổng.
- Cổng không trả lời được → bỏ qua lượt này, lượt sau quét tiếp (không đổi trạng thái — không
  biết thì không đoán).

| Chốt | Giá trị | Vì sao |
|---|---|---|
| Nhịp quét | **1 phút** | Khách ngồi màn chờ tối đa 60 giây rồi bỏ cuộc — đối soát chậm hơn một nhịp là bắt khách bỏ cuộc oan |
| Ngưỡng quá hạn | **5 phút** | IPN bình thường về trong vài giây; chờ 5 phút tránh hỏi cổng trùng lúc IPN còn đang bay |
| Hằng số trong code | có | Không phải tham số của story nào — cùng lối nhịp job quét hold hết hạn (docs/26 §4) |

### Cấu hình cổng MoMo

`MoMoOptions` đọc section `MoMo`: `PartnerCode` / `AccessKey` / `SecretKey` / `Endpoint` (mặc
định sandbox `https://test-payment.momo.vn`). 🔴 **LUẬT 2 — repo PUBLIC**: ba giá trị đối tác đặt
qua `dotnet user-secrets` hoặc biến môi trường (`MoMo__PartnerCode`…), KHÔNG commit giá trị thật;
khuôn trống đã có trong `appsettings.Development.json.example`. Thiếu cấu hình thì lần gọi đầu
tiên báo lỗi nêu đúng tên khoá thiếu.

### Cấu hình cổng VNPay

Khác MoMo một điểm bản chất: **VNPay không có API server-to-server để tạo giao dịch** — backend
dựng URL `vpcpay.html` đã ký HMAC-SHA512 rồi chuyển khách sang cổng (task *"Tích hợp VNPay: tạo
URL thanh toán + verify chữ ký"* — Nguyễn Duy Kiên). Cổng trả kết quả về bằng **GET kèm tham số
`vnp_` trên query string** ở cả hai đường — Return URL (khách quay về) và IPN (cổng gọi thẳng
backend) — nên hai đường dùng chung một phép kiểm chữ ký `IsValidSignature`. Client thuần tính
toán: không cần `HttpClient`, chưa đụng CSDL.

`VnPayOptions` đọc section `VnPay`: `TmnCode` / `HashSecret` / `PaymentUrl` (mặc định sandbox
`https://sandbox.vnpayment.vn/paymentv2/vpcpay.html`). 🔴 **LUẬT 2 — repo PUBLIC**: hai giá trị
đối tác đặt qua `dotnet user-secrets` hoặc biến môi trường (`VnPay__TmnCode`, `VnPay__HashSecret`),
KHÔNG commit giá trị thật; khuôn trống đã có trong `appsettings.Development.json.example`. Thiếu
cấu hình thì lần dựng service đầu tiên báo lỗi nêu đúng tên khoá thiếu (cùng lối `MoMoOptions`).

Ba điểm dễ sai của chuẩn ký — ghim trong `VnPayGatewayService`, test ghim bằng vector tính NGOÀI
code C# (Python, như bộ test MoMo):

| Điểm | Chuẩn VNPay | Sai thì hỏng thế nào |
|---|---|---|
| Chuỗi ký | `key=value` xếp theo bảng chữ cái, giá trị mã hoá kiểu PHP urlencode (dấu cách → `+`, `~` → `%7E`, hex chữ HOA) | Dùng `Uri.EscapeDataString` trần thì dấu cách ra `%20` — chữ ký lệch với chuỗi cổng tính lại, cổng báo sai chữ ký |
| `vnp_Amount` | Số tiền **× 100** (VNPay tính theo đơn vị nhỏ nhất) | Cổng ghi nhận sai số tiền |
| `vnp_CreateDate`/`vnp_ExpireDate` | Giờ **GMT+7**, định dạng `yyyyMMddHHmmss` | Cổng từ chối giao dịch |

### Lớp adapter thống nhất — thêm cổng mới

Task *"Adapter pattern thống nhất cổng thanh toán (dễ thêm cổng mới)"* — Phùng Duy Hoàng. Endpoint
`POST /payments` và hai endpoint callback **không gọi thẳng client từng cổng**: chúng tra
`IPaymentGatewayResolver` theo `methodCode` rồi nói chuyện qua `IPaymentGateway` bằng DTO trung
tính (`Dtos/Payments`: `PaymentInitiationRequest`/`Result`, `PaymentCallbackInput`/`Result`). Mỗi
cổng một adapter mỏng bọc client sẵn có — `MoMoGatewayAdapter` bọc `MoMoGatewayService` (Hiếu),
`VnPayGatewayAdapter` bọc `VnPayGatewayService` (Kiên), `ZaloPayGatewayAdapter` bọc
`ZaloPayGatewayService` (Kiên), `BankCardGatewayAdapter` cũng đứng trên client VNPay (Kiên); bốn
client cổng **không đổi một dòng nào** khi lắp adapter.

- Bốn mã của hợp đồng (`MoMo`, `VNPay`, `ZaloPay`, `BankCard`) **đều đã có adapter**. Mã NGOÀI bốn
  mã đó là **chưa mở** — endpoint trả **400** kèm danh sách `SupportedProviders`.
- Mã cổng khớp **KHÔNG phân biệt hoa thường** (`vnpay` = `VNPay`). Cổng có adapter nhưng **thiếu
  cấu hình** thì ném ngay khi dựng adapter, nêu đúng tên khoá thiếu (luật 2) — cố ý khác hẳn "chưa
  mở", vì đó là lỗi triển khai chứ không phải cổng không tồn tại.
- Adapter chuẩn hoá **kết luận**, không chỉ hình dạng: `PaymentCallbackResult.Succeeded` **luôn
  false khi chữ ký chưa hợp lệ** — kể cả cổng báo mã thành công (không xác nhận tiền trên dữ liệu
  chưa kiểm chữ ký). Hai endpoint callback vẫn phải **đối chiếu `PaymentCode`/`Amount` với bản ghi
  Payments** trước khi lật trạng thái — adapter không thay bước đó (bước này cần CSDL).
- Thêm cổng mới = **3 việc, không đụng endpoint**: (1) viết client cổng + adapter cài
  `IPaymentGateway`, (2) thêm hằng mã vào `PaymentProviderCodes`, (3) thêm một dòng vào bảng đăng
  ký trong `PaymentGatewayResolver` + một dòng `AddScoped` adapter trong `Program.cs`. ZaloPay và
  `BankCard` đã đi đúng con đường đó — không file nào của lớp adapter phải sửa thêm.
- Cố ý **KHÔNG** đưa "hỏi trạng thái cổng" vào interface chung: hiện MoMo và **ZaloPay** có API
  hỏi trạng thái, còn **VNPay v2.1.0 không có** — đưa vào interface là bắt VNPay cài một hàm ném
  `NotSupportedException`, che mất sự thật rằng cổng đó không hỏi được. Job đối soát (bảng trên)
  gọi thẳng client của cổng hỗ trợ. Khi có cổng thứ ba hỗ trợ hỏi trạng thái thì mở rộng interface
  sau, lúc đó mới đủ cơ sở để chốt hình dạng chung.

Hai adapter của task *"Tích hợp ZaloPay và thẻ ngân hàng"* (Kiên) — chỗ endpoint cần biết:

- **`ZaloPayGatewayAdapter`** — endpoint vẫn chỉ thấy `paymentCode` trần như mọi cổng: adapter
  **ghép** tiền tố `yymmdd_` lúc gửi và **bỏ** tiền tố lúc nhận callback, nên `PaymentCode` trong
  `PaymentCallbackResult` là mã nội bộ, KHÔNG phải `261010_PM-…`. `app_user` hiện là hằng
  `"smartbus"` (cổng bắt buộc có trường này, MoMo không có): muốn gửi mã hành khách thật thì phải
  thêm trường vào `PaymentInitiationRequest` — tức **đổi hình dạng API, luật 5**, sửa file này trước.
- **`BankCardGatewayAdapter`** — ghim `vnp_BankCode = VNBANK` lúc dựng URL, còn `VerifyCallback` thì
  **uỷ thác nguyên cho `VnPayGatewayAdapter`** (cùng cổng, cùng tham số `vnp_`, cùng chữ ký SHA512)
  nên hai adapter kết luận giống nhau từng trường. Hệ quả cho endpoint: **kênh thẻ gọi về CHÍNH
  endpoint callback của VNPay** (`/payments/vnpay/callback`) — không có endpoint riêng cho
  `BankCard`; phân biệt giao dịch nào là thẻ là việc của cột `method` trong bảng Payments.

### Cấu hình cổng ZaloPay

`ZaloPayOptions` đọc section `ZaloPay`: `AppId` / `Key1` / `Key2` / `Endpoint` (mặc định sandbox
`https://sb-openapi.zalopay.vn`). 🔴 **LUẬT 2 — repo PUBLIC**: **cả `Key1` lẫn `Key2` đều là bí
mật** — `Key1` ký request gửi đi, `Key2` xác thực callback cổng gửi về; hai khoá KHÁC NHAU, dùng
lẫn là chữ ký sai. Đặt qua `dotnet user-secrets` hoặc biến môi trường (`ZaloPay__AppId`,
`ZaloPay__Key1`, `ZaloPay__Key2`), KHÔNG commit giá trị thật; khuôn trống đã có trong
`appsettings.Development.json.example`.

Khác MoMo ở chỗ ZaloPay ký **hai khoá cho hai chiều**, và cấu trúc callback khác hẳn:

| Điểm | Chuẩn ZaloPay | Ghi chú |
|---|---|---|
| Tạo đơn | HMAC-SHA256, khoá `Key1`, chuỗi nối bằng `\|`: `app_id\|app_trans_id\|app_user\|amount\|app_time\|embed_data\|item` | `embed_data` và `item` là **chuỗi JSON** lồng trong chuỗi ký — giữ nguyên đúng chuỗi đã gửi trong body |
| Xác thực callback | HMAC-SHA256, khoá **`Key2`**, ký trên **chính chuỗi `data` thô** | Callback chỉ có 3 trường `{data, mac, type}`; `data` là một CHUỖI JSON, không phải object |
| Hỏi trạng thái | HMAC-SHA256, khoá `Key1`, chuỗi `app_id\|app_trans_id\|key1` | ⚠️ Xem cảnh báo dưới |
| Merchant trả lời cổng | `{"return_code": 1, "return_message": "Success"}` | Khác MoMo (204) và VNPay (`RspCode`) |
| `return_code` của cổng | `1` thành công · `2` thất bại · `3` đang xử lý | Bộ mã KHÁC MoMo (`resultCode = 0` là thành công) — đừng so với 0 |

Ba kiểu dữ liệu dễ khai sai, đã ghim bằng test:

- **`app_id` là SỐ (`int32`), không phải chuỗi** — ở cả body gửi đi lẫn trong chuỗi ký. Cấu hình
  `ZaloPay:AppId` vẫn là chuỗi (mọi nguồn cấu hình .NET đều ra chuỗi) nhưng client **kiểm ngay lúc
  dựng** rằng nó là số nguyên không có số 0 thừa, vì `"02553"` sẽ thành `2553` trong body mà
  `"02553"` trong chữ ký — hai giá trị khác nhau, cổng từ chối.
- **`type` của vỏ callback là SỐ** (`1` = callback giao dịch), còn **`data` là CHUỖI** JSON. Khai
  `type` là `string` thì `JsonSerializer` ném khi gặp `"type":1` và **mọi callback thật đều bị từ
  chối** — hỏng cả đường tiền.
- **Mọi trường trong `data` là snake_case** (`app_trans_id`, `zp_trans_id`, `server_time`…), phải
  ghim `[JsonPropertyName]`: quy ước camelCase của .NET chỉ nối được `appTransId`, KHÔNG nối được
  `app_trans_id`. Thiếu ghim thì chuỗi vẫn giải mã "thành công" nhưng mọi trường về mặc định —
  callback đọc ra rỗng mà không có lỗi nào để lần theo.

`app_trans_id` = **`{yymmdd}_{paymentCode}`** (ghép/bỏ tiền tố ở tầng adapter, xem dưới), dài **tối
đa 40 ký tự**, tiền tố `yymmdd` tính theo **giờ Việt Nam (GMT+7)**; gửi trùng mã bị cổng trả
`-68 DUPLICATE_APPS_TRANS_ID`.

> ⚠️ **Một chỗ tài liệu ZaloPay mâu thuẫn — đã chọn và ghim lại:** trang spec hiện hành ghi chuỗi
> MAC của `/v2/query` là `app_id|app_trans_id|key1` (phần tử thứ ba **là chính khoá `Key1`**), còn
> bản PDF "ZaloPay APIs Integration Document" đời cũ lại đặt `app_time` ở vị trí đó. Code viết theo
> bản hiện hành và tách hàm dựng chuỗi MAC ra riêng, nên đổi sang biến thể kia chỉ là **một dòng**.
> Muốn chắc phải chạy thử sandbox với khoá đối tác thật — nhóm chưa có khoá. Test hiện tại là test
> chống hồi quy theo đặc tả, **không phải** bằng chứng cổng thật đã chấp nhận.

> ⚠️ **Chọn kênh thanh toán — KHÔNG có trường `method`:** `/v2/create` không có tham số nào tên
> `method`. Kênh chọn qua `bank_code` và/hoặc `embed_data.preferred_payment_method`
> (`["domestic_card"]`, `["international_card"]`, `["zalopay_wallet"]`, `["vietqr"]`…). Adapter
> ZaloPay **ghim `preferred_payment_method = ["zalopay_wallet"]`** — đúng nghĩa mã phương thức
> `ZaloPay` ở bảng enum chốt trên (khác `BankCard` ghim `vnp_BankCode` bên VNPay). Muốn để khách tự
> chọn kênh trên trang ZaloPay thì bỏ đúng dòng gán đó trong `ZaloPayGatewayAdapter`.

> 🔴 **Callback ZaloPay KHÔNG mang mã kết quả — chữ ký hợp lệ NGHĨA LÀ đã thu tiền.** Tài liệu
> ZaloPay: *"Khi và chỉ khi Zalopay nhận tín hiệu khách hàng thành công thì mới thông báo kết quả."*
> Vỏ `{data, mac, type}` không có `return_code`/`status`, và ruột `data` cũng không — nên
> `ZaloPayGatewayAdapter` đặt `Succeeded = chữ ký hợp lệ && đọc được ruột`, còn
> `ProviderResponseCode` **cố ý để trống** thay vì bịa một con số. Đây là điểm KHÁC MoMo
> (`resultCode`) và VNPay (`vnp_ResponseCode`): đừng đi tìm trường trạng thái trong `data`. Cần mã
> kết quả thì job đối soát gọi `QueryOrderAsync` (`return_code` 1/2/3).

## Báo cáo doanh thu — `/reports/revenue`

> Story 19 *"Doanh thu"*. ✅ Backend đã có (`RevenueReportController` — Trần Trung Hiếu, task
> *"API báo cáo doanh thu theo ngày / tuần / tháng"*; `RevenueComparisonController` — Phùng Duy
> Hoàng, task *"API so sánh doanh thu giữa các kỳ"*; cùng Sprint 4). Mục này là nửa "theo thời
> gian" của story; **theo tuyến / theo xe / theo phương thức** (Kiên) và **xuất Excel/PDF** (các
> task sau) sẽ mở rộng trên cùng bề mặt `/reports/revenue`.

**Endpoint yêu cầu vai trò `Admin` hoặc `Manager`** (story 19 là nghiệp vụ của quản lý). Người đã
đăng nhập nhưng không đủ quyền nhận **403** kèm body
`{ "message": "Bạn không có quyền truy cập tính năng này." }`.

#### `GET /reports/revenue` — tổng hợp doanh thu theo ngày / tuần / tháng

Query — **mọi tham số bỏ trống được**:

| Tham số | Mặc định | Ý nghĩa |
|---|---|---|
| `granularity` | `day` | Độ mịn: `day` / `week` / `month` (không phân biệt hoa/thường) |
| `from` | 29 ngày trước `to` | Ngày bắt đầu (lịch **Việt Nam**, `yyyy-MM-dd`), TÍNH CẢ |
| `to` | hôm nay (giờ VN) | Ngày kết thúc (lịch Việt Nam), TÍNH CẢ |
| `routeId` | — | Chỉ tính các chuyến thuộc tuyến này; bỏ trống = toàn công ty |

```json
// RevenueReport — ví dụ gộp theo ngày, 3 ngày
{
  "granularity": "day",
  "from": "2026-10-12",
  "to": "2026-10-14",
  "totalRevenue": 123456789,
  "totalPayments": 456,
  "buckets": [
    { "periodStart": "2026-10-12", "revenue": 35000000, "payments": 120 },
    { "periodStart": "2026-10-13", "revenue": 0, "payments": 0 },
    { "periodStart": "2026-10-14", "revenue": 88456789, "payments": 336 }
  ]
}
```

| Trường | Ý nghĩa |
|---|---|
| `totalRevenue` / `totalPayments` | Tổng cả kỳ — hai con số cho thẻ số liệu của dashboard |
| `buckets[].periodStart` | Điểm bắt đầu bucket, dạng `yyyy-MM-dd`: chính ngày đó (`day`) · thứ Hai của tuần (`week`) · ngày 1 của tháng (`month`) |
| `buckets[].revenue` / `payments` | Doanh thu (VND) / số giao dịch của bucket |

- **Nền số liệu là bảng `Payments`, chỉ đếm `Success`** — doanh thu là tiền ĐÃ THU THỰC, để kiểm
  thử chéo "số liệu doanh thu khớp với giao dịch thực tế" có đối tượng để đối chiếu. Vé `Paid`/
  `Used` không phải nền: vé là hàng hoá phát hành kèm tiền, tiền mới là thứ được đối soát.
- **Mốc thời gian của giao dịch là `paidAt`** (cổng ghi nhận thu tiền); giao dịch `Success` thiếu
  `paidAt` (dữ liệu cũ/lỗi) thì tính theo `createdAt` — tiền đã thu không được biến mất khỏi báo
  cáo chỉ vì thiếu một cột thời gian.
- **Trục thời gian theo giờ Việt Nam (UTC+7)** — giao dịch 00:30 giờ VN thuộc ngày HÔM ĐÓ, không
  phải ngày hôm trước theo UTC. `from`/`to` là ngày lịch Việt Nam; tuần bắt đầu **thứ Hai** (ISO),
  tháng theo lịch dương.
- **Bucket LIÊN TỤC, không bỏ ngày rỗng** — biểu đồ cần trục đầy đủ: khoảng không có giao dịch
  thì `revenue`/`payments` bằng 0. Bucket đầu có thể bắt đầu TRƯỚC `from` (tuần lấy từ thứ Hai,
  tháng lấy từ ngày 1).
- Khoảng `from` → `to` tối đa **366 ngày** (trọn một năm) · `to` trước `from` → **400** — lỗi gắn
  ở cả `errors.from` lẫn `errors.to` · `granularity` lạ → **400** `errors.granularity` ·
  `routeId` không tồn tại → **404** `Không tìm thấy tuyến đường`.

#### `GET /reports/revenue/comparison` — so sánh kỳ hiện tại với kỳ liền trước

Query — **mọi tham số bỏ trống được**, cùng quy tắc mặc định với `GET /reports/revenue`:

| Tham số | Mặc định | Ý nghĩa |
|---|---|---|
| `from` | 29 ngày trước `to` | Ngày bắt đầu KỲ HIỆN TẠI (lịch **Việt Nam**, `yyyy-MM-dd`), TÍNH CẢ |
| `to` | hôm nay (giờ VN) | Ngày kết thúc kỳ hiện tại (lịch Việt Nam), TÍNH CẢ |
| `routeId` | — | Chỉ tính các chuyến thuộc tuyến này; bỏ trống = toàn công ty |

**Kỳ liền trước** là khoảng **cùng số ngày**, kết thúc ngay trước `from` (tức `from - 1`).
Ví dụ `from=2026-10-11&to=2026-10-17` (7 ngày) → kỳ trước `2026-10-04 → 2026-10-10` (7 ngày).

```json
// RevenueComparison — kỳ hiện tại 7 ngày, doanh thu tăng 25%
{
  "from": "2026-10-11",
  "to": "2026-10-17",
  "previousFrom": "2026-10-04",
  "previousTo": "2026-10-10",
  "currentRevenue": 150000000,
  "previousRevenue": 120000000,
  "revenueChange": 30000000,
  "revenueChangePercent": 25.00,
  "currentPayments": 600,
  "previousPayments": 500,
  "paymentsChange": 100,
  "paymentsChangePercent": 20.00
}
```

| Trường | Ý nghĩa |
|---|---|
| `from` / `to` | Kỳ hiện tại sau khi áp dụng mặc định |
| `previousFrom` / `previousTo` | Kỳ liền trước, cùng độ dài, kết thúc ngay trước `from` |
| `currentRevenue` / `previousRevenue` | Doanh thu từng kỳ (VND) |
| `revenueChange` | `currentRevenue - previousRevenue` (VND; âm = giảm) |
| `revenueChangePercent` | % thay đổi doanh thu, làm tròn **2 chữ số thập phân**; **`null` khi `previousRevenue` = 0** (không có nền để chia — FE hiển thị "—", KHÔNG phải 0%) |
| `currentPayments` / `previousPayments` | Số giao dịch `Success` từng kỳ |
| `paymentsChange` / `paymentsChangePercent` | Mức thay đổi số giao dịch; cùng quy tắc `null` khi kỳ trước = 0 |

- **Cùng nền số liệu và cùng trục thời gian với `GET /reports/revenue`**: bảng `Payments` chỉ đếm
  `Success`, mốc `paidAt` (thiếu thì `createdAt`), ngày theo giờ Việt Nam UTC+7. Endpoint này gọi
  lại chính service báo cáo gốc hai lần nên hai endpoint không thể lệch số cho cùng một kỳ.
- **Không có tham số `granularity`.** "Kỳ" ở đây là khoảng `from` → `to` do người gọi chọn; kỳ
  trước luôn cùng số ngày. Muốn so tuần/tháng, người gọi truyền khoảng ngày tương ứng (tuần bắt
  đầu thứ Hai, tháng theo lịch dương — đúng quy tắc bucket ở trên).
- Khoảng kỳ hiện tại tối đa **366 ngày** · `to` trước `from` → **400**, lỗi gắn ở cả `errors.from`
  lẫn `errors.to` · `routeId` không tồn tại → **404** `Không tìm thấy tuyến đường`.
- Khoảng kỳ trước vượt giới hạn lịch (chỉ xảy ra khi `from` rơi vào đầu năm 1) → **400**, lỗi gắn
  ở `errors.from`/`errors.to`.

> **Cho task "API doanh thu theo tuyến, theo xe, theo phương thức thanh toán" (Kiên):** dùng cùng
> nền `Payments` `Success` và cùng trục thời gian ở trên — mở rộng bằng endpoint mới trên bề mặt
> `/reports/revenue` (ví dụ `GET /reports/revenue/by-route`) thay vì dựng một nền tính khác, nếu
> không hai báo cáo sẽ ra hai con số tổng khác nhau cho cùng một kỳ.

> **Vì sao không trả kèm số vé?** Story chỉ hỏi doanh thu; vé đã bán là chuyện của báo cáo tỷ lệ
> lấp đầy (US 20) — mỗi báo cáo một câu hỏi, trộn lại thì không báo cáo nào đọc được.

## Voucher — `/vouchers`

> Story 18 *"Quản lý Voucher"*. Bảng phân công Sprint 3 tách story này thành năm dòng; mục này chốt
> phần **kiểm tra và áp dụng vào đơn hàng**:
>
> | Dòng | Việc | Người | Trạng thái |
> |---|---|---|---|
> | 50 | Migrate bảng `Vouchers`, `VoucherUsages` | Vàng Thị Dăm | ✅ Đã merge (PR #144) — còn bước `database update` trên CSDL chung |
> | 51 | API CRUD voucher | Trần Trung Hiếu | ✅ mục "CRUD voucher" dưới — dùng chung bề mặt `api/vouchers` |
> | **52** | **API kiểm tra và áp dụng voucher vào đơn hàng** | **Nguyễn Duy Kiên** | ✅ mục này |
> | 53 | Validate điều kiện voucher: thời gian, tuyến, giá trị đơn tối thiểu | Phùng Duy Hoàng | ⚠️ **gộp vào dòng 52** — xem dưới |
> | **54** | **API thống kê hiệu quả voucher** | **Nguyễn Duy Kiên** | ✅ mục "GET /vouchers/statistics" dưới đây |
>
> ⚠️ **Dòng 53 đã nằm trọn trong dòng 52.** "Kiểm tra voucher" và "validate điều kiện voucher" là
> **một việc**: không có phần kiểm tra thì không có gì để áp dụng, và không có luật nào để kiểm thì
> endpoint kiểm tra trả về cái gì? Bảng phân công tách đôi vì hai dòng thuộc hai người, nhưng phần
> ruột chỉ có một. Nhánh này làm trọn cả hai — **Phùng Duy Hoàng dừng dòng 53** để khỏi viết trùng.
> (Cùng dạng trùng lặp đã gặp ở các dòng 14/16, 15 với 4/18/20, 29/30.)

> ✅ **Bảng `Vouchers` / `VoucherUsages` — migration đã merge (PR #144).**
> Entity + cấu hình EF do Nguyễn Duy Kiên dựng (task dòng 52, Dăm đã cho phép dựng bảng); **Dăm đã
> soát lại toàn bộ 10/10/2026: không thiếu mục nào** (bảng, cột, kiểu, 3 FK `Restrict`, 6 index,
> `xmin`, `UpdatedAt`), và file migration sinh ra đã đối chiếu đạt cả checklist của
> `docs/27-huong-dan-migrate-vouchers.md`.
>
> ⏳ **Còn nợ đúng một bước: `dotnet ef database update`** trên CSDL chung (`docs/25` §4). Bảng chỉ
> có **thật** sau bước đó — trước đó thì mọi endpoint dưới đây vẫn **500 trên CSDL thật**. Test tích
> hợp của dự án chạy trên EF InMemory nên **không chờ migration**, đổi lại bộ test xanh **không**
> chứng minh được bảng đã tồn tại.

### Entity `Voucher`

| Trường | Kiểu | Mô tả |
|---|---|---|
| `id` | guid | |
| `code` | varchar(20) | **Duy nhất toàn hệ thống**, lưu CHỮ HOA. Mọi đường đọc/ghi đều chuẩn hoá `trim().ToUpperInvariant()` trước khi tra — CSDL chặn bằng unique index |
| `name` | varchar(200) | Tên hiển thị ở màn quản lý |
| `discountType` | varchar(20) | `'Percent' \| 'FixedAmount'` |
| `discountValue` | numeric(12,2) | `Percent`: 1–100. `FixedAmount`: số tiền VND |
| `minOrderValue` | numeric(12,2) | Giá trị đơn tối thiểu; `0` = không yêu cầu. So với **tổng tiền TRƯỚC giảm giá** |
| `maxDiscount` | numeric(12,2) nullable | Trần giảm; chỉ có nghĩa khi `Percent`. `null` = không trần |
| `routeId` | guid **nullable** | 🔹 điều kiện "tuyến" của dòng 53: `null` = **mọi tuyến**, có giá trị = chỉ tuyến đó |
| `quantity` | int | Tổng số lượt phát hành |
| `usedCount` | int | Số lượt đã tiêu thụ. **Backend tăng, FE chỉ đọc** — FE gửi lên cũng bị bỏ qua |
| `validFrom` | timestamptz | Bắt đầu hiệu lực — **tính cả hai đầu mút** (`>=`) |
| `validUntil` | timestamptz | Kết thúc hiệu lực — **tính cả hai đầu mút** (`<=`) |
| `status` | varchar(20) | `'Active' \| 'Inactive'` |
| `createdAt` / `updatedAt` | timestamptz / nullable | Bảng có sửa (A4) nên có `updatedAt` |

Hình dạng này khớp bản nháp FE `frontend/src/api/voucherApi.ts` (Hạnh) **trừ `routeId`** — bản nháp
đó chưa có trường tuyến, nên màn quản lý voucher hiện không nhập được điều kiện tuyến. FE cần bổ
sung; đây là việc của màn quản lý (dòng 51/56–58), không phải của endpoint kiểm tra.

### Entity `VoucherUsage`

Bản ghi **một lượt tiêu thụ** voucher — mỗi giao dịch thành công có mã tối đa một dòng. Bảng chỉ
GHI THÊM: **không** `updatedAt` (A4), **không** `isDeleted`.

| Trường | Kiểu | Mô tả |
|---|---|---|
| `id` | guid | |
| `voucherId` | guid | FK → `Vouchers`, **Restrict** |
| `userId` | guid | FK → `Users`, **Restrict** — ai đã dùng |
| `paymentCode` | varchar(64) | Mã giao dịch (`Payments.PaymentCode`) — **duy nhất**, vừa là khoá nghiệp vụ của đơn vừa là **chốt idempotency** |
| `orderAmount` | numeric(12,2) | Tổng tiền TRƯỚC giảm, ảnh chụp lúc áp |
| `discountAmount` | numeric(12,2) | Số tiền đã giảm |
| `createdAt` | timestamptz | |

> 📌 **Vì sao `paymentCode` là cột trần, KHÔNG phải FK sang `Payments`.** Lý do ban đầu là ràng buộc
> thứ tự: `Payments` khi đó **chưa migrate** (Dăm còn nợ `Sprint3_Payments`), nên đặt FK sẽ buộc
> migration `Vouchers` phải chạy **sau** migration `Payments` — một ràng buộc thứ tự giữa hai việc của
> hai người, đổi lấy đúng một cột.
>
> 🟡 **Cập nhật 10/10/2026 — ràng buộc đó nay không còn, nhưng quyết định giữ nguyên.** Cả `Payments`,
> `Vouchers`/`VoucherUsages` lẫn `Tickets` ra đời trong **cùng một** migration
> (`Sprint3_Payments_Vouchers_Tickets`), nên nối FK không còn vướng thứ tự. Chọn giữ `paymentCode` là
> **cột trần** vì `Payment.TicketId` — lối so sánh cũ — nay **đã nối FK** (`Restrict`), còn việc nối
> `VoucherUsages.PaymentCode` → `Payments.PaymentCode` là **bàn riêng giữa Dăm và Kiên**, không tự
> đổi trong lúc soát migration (`docs/27` mục "Nhắc nhóm" số 2). Muốn nối thì sửa mục này trước rồi
> mới code — luật 5.

### Endpoints

| Method | Endpoint | Task | Trạng thái |
|---|---|---|---|
| POST | `/vouchers/validate` | *"API kiểm tra và áp dụng voucher vào đơn hàng"* — Nguyễn Duy Kiên (dòng 52 + 53) | ✅ mục này |
| GET | `/vouchers` | Danh sách + lọc (màn quản trị — `voucherApi.list`) | ✅ mục "CRUD voucher" dưới |
| GET | `/vouchers/{id}` | Chi tiết một voucher | ✅ mục "CRUD voucher" dưới |
| POST | `/vouchers` | Thêm voucher | ✅ mục "CRUD voucher" dưới |
| PUT | `/vouchers/{id}` | Sửa voucher | ✅ mục "CRUD voucher" dưới |
| PATCH | `/vouchers/{id}/status` | Bật/tắt áp dụng | ✅ mục "CRUD voucher" dưới |
| GET | `/vouchers/statistics` | *"API thống kê hiệu quả voucher"* — Nguyễn Duy Kiên (dòng 54) | ✅ mục này |
| — | *áp dụng vào đơn* | Phần "áp dụng" của dòng 52 | ✅ **không phải endpoint riêng** — service nội bộ, `POST /payments` gọi, xem dưới |

Bốn dòng của dòng 51, `POST /vouchers/validate` và `GET /vouchers/statistics` **dùng chung bề mặt
`api/vouchers` nhưng không tranh chấp**: `validate` và `statistics` là hai đoạn đường dẫn cố định,
`{id}` là tham số, còn `POST /` ở gốc chỉ đụng `POST /vouchers/validate` nếu ai đó đặt tên đoạn là
`validate` — đừng đặt. Mỗi task một file controller, cùng lối `api/seat-holds` và `api/trips` đã làm.

🔴 **`GET /vouchers/{id}` của dòng 51 PHẢI giữ ràng buộc `{id:guid}`.** Bề mặt này nay có **hai**
đoạn literal (`validate`, `statistics`); nới `:guid` thành `{id}` trần là `statistics` bị nuốt và
endpoint chi tiết nhận một chuỗi không phải GUID — đúng cái bẫy mà `{id:guid}` của
`/admin/feedbacks` đang tránh (xem "Thống kê phản ánh").

#### CRUD voucher — năm endpoint của dòng 51

> Task *"API CRUD voucher: mã, loại giảm giá, điều kiện áp dụng, số lượng"* (dòng 51, US 18,
> Sprint 3, Trần Trung Hiếu). Năm endpoint này dùng chung bề mặt `api/vouchers` với `validate`
> (Kiên) và `statistics` (Kiên) — mỗi task một controller, cùng lối `api/seat-holds`.
>
> Quyền: `ManagerOrAbove` — màn quản lý voucher là của nhân viên Marketing/quản lý, cùng quyền
> `GET /vouchers/statistics`. Không có token → 401; thiếu quyền → 403.

Response của mọi endpoint ở đây là **`VoucherResponse`** — đúng bảng trường entity `Voucher` ở trên,
trừ `routeId` kèm thêm `routeCode`/`routeName` (nối sẵn tên tuyến để màn quản lý hiển thị, null khi
voucher dùng cho mọi tuyến — cùng lối `GET /vouchers/statistics`):

| Trường | Kiểu | Ghi chú |
|---|---|---|
| `id` | `string` (GUID) | |
| `code` | `string` | Đã chuẩn hoá **CHỮ HOA** |
| `name` | `string` | |
| `discountType` | `string` | `'Percent'` / `'FixedAmount'` |
| `discountValue` | `number` | |
| `minOrderValue` | `number` | |
| `maxDiscount` | `number \| null` | |
| `routeId` / `routeCode` / `routeName` | `string \| null` | Điều kiện tuyến; null = mọi tuyến |
| `quantity` / `usedCount` | `number` | `usedCount` do backend giữ — **không có trong request**, gửi lên cũng bị bỏ qua |
| `validFrom` / `validUntil` | `string` | ISO 8601 (UTC) — tính CẢ hai đầu mút |
| `status` | `string` | `'Active'` / `'Inactive'` |
| `createdAt` / `updatedAt` | `string` / `string \| null` | |

##### `GET /vouchers` — danh sách + lọc + phân trang (màn quản trị)

| Tham số | Mô tả |
|---|---|
| `search` | Tìm theo mã hoặc tên — không phân biệt hoa thường. Bỏ trống = tất cả |
| `status` | `'Active'` / `'Inactive'`. Bỏ trống = cả hai |
| `page` | Mặc định 1 |
| `pageSize` | Mặc định 10, tối đa 100 |

Trả `{ items: VoucherResponse[], total, page, pageSize }` — `total` là tổng số dòng **khớp bộ lọc**
(không phải số dòng trong `items`), sắp theo `createdAt` giảm dần (mới tạo trước). Giá trị `status`
ngoài hai giá trị chốt → 400 (lỗi bind query).

##### `GET /vouchers/{id}` — chi tiết một voucher

Ràng buộc `{id:guid}` (xem 🔴 ngay trên). Không tồn tại → 404.

##### `POST /vouchers` — tạo voucher

```json
// CreateVoucherRequest — body
{
  "code": "SUMMER10",
  "name": "Giảm giá mùa hè 10%",
  "discountType": "Percent",
  "discountValue": 10,
  "minOrderValue": 0,
  "maxDiscount": 20000,
  "routeId": null,
  "quantity": 500,
  "validFrom": "2026-10-12T00:00:00Z",
  "validUntil": "2026-11-30T23:59:59Z"
}
```

| Trường | Bắt buộc | Ràng buộc |
|---|---|---|
| `code` | ✅ | 2–20 ký tự. Chuẩn hoá `Trim().ToUpperInvariant()`. Trùng mã đã có (sau chuẩn hoá) → **409** |
| `name` | ✅ | 1–200 ký tự |
| `discountType` | ✅ | `'Percent'` / `'FixedAmount'` |
| `discountValue` | ✅ | `Percent`: **1–100** · `FixedAmount`: **> 0** và ≤ 9.999.999.999,99 — sai theo kiểu → 400 kèm `errors.discountValue` |
| `minOrderValue` | ❌ (mặc định 0) | 0 … 9.999.999.999,99 — so với tổng tiền TRƯỚC giảm |
| `maxDiscount` | ❌ | Chỉ có nghĩa khi `Percent`; `FixedAmount` thì backend **ép null** (trần với số tiền cố định là khái niệm vô nghĩa). `Percent` mà gửi 0/âm → 400 kèm `errors.maxDiscount` (trần 0đ = vô hiệu hoá voucher) |
| `routeId` | ❌ | Điều kiện tuyến: null = mọi tuyến. Tuyến không tồn tại → **404** (lỗi phía gọi — cùng lối `tripId` của `POST /vouchers/validate`) |
| `quantity` | ✅ | ≥ 1 — tổng số lượt phát hành |
| `validFrom` | ✅ | |
| `validUntil` | ✅ | Phải **sau** `validFrom` (bằng nhau cũng bị từ chối — khoảng hiệu lực rỗng) → 400 kèm `errors.validUntil` |
| `status` | — | **KHÔNG nhận** — voucher mới luôn `Active` (đổi trạng thái qua PATCH) |
| `usedCount` | — | **KHÔNG nhận** — backend giữ, chỉ tăng khi tiền về (mục "Áp dụng voucher vào giao dịch") |

Trả **201** + `VoucherResponse` (kèm header `Location`). Body sai khuôn → 400 `{ message, errors }`.

##### `PUT /vouchers/{id}` — sửa voucher

Cùng bảng ràng buộc của POST, thêm ba điều:

- Không tồn tại → 404; trùng mã với voucher **khác** → 409.
- `quantity` phải ≥ `usedCount` — hạ tổng lượt xuống dưới số đã tiêu thụ là dữ liệu tự mâu thuẫn
  → 400 kèm `errors.quantity`.
- `usedCount`/`status` trong body bị bỏ qua (đổi trạng thái qua PATCH). Sửa thành công thì
  `updatedAt` được làm mới.

##### `PATCH /vouchers/{id}/status` — bật/tắt áp dụng

```json
// UpdateVoucherStatusRequest — body
{ "status": "Inactive" }
```

Bật/tắt là quyết định của người vận hành, KHÔNG phải trạng thái hiệu lực — voucher `Active` vẫn có
thể chưa tới `validFrom`/đã quá `validUntil`/hết lượt, những thứ đó `POST /vouchers/validate` tự
suy (xem `VoucherStatus`). Không tồn tại → 404; `status` ngoài hai giá trị → 400. Trả 200 +
`VoucherResponse` đã đổi (kèm `updatedAt` mới).

> 📌 **Màn quản lý voucher cần hiển thị "hết hạn"/"hết lượt"** thì tự suy từ `validUntil`/`quantity`
> vs `usedCount` — backend không trả trạng thái thứ ba (xem `VoucherStatus`, không có
> `Expired`/`OutOfStock`).

#### `POST /vouchers/validate` — kiểm tra mã và tính số tiền giảm

Màn thanh toán gọi endpoint này **trong lúc khách gõ mã** (debounce) để hiện số tiền được giảm.

```json
// ValidateVoucherRequest — body
{
  "code": "SUMMER10",
  "tripId": "b7c1d2e3-0000-0000-0000-000000000000",
  "orderAmount": 150000
}
```

| Trường | Bắt buộc | Ràng buộc |
|---|---|---|
| `code` | ✅ | 2–20 ký tự. Backend `Trim().ToUpperInvariant()` trước khi tra — gõ `summer10` vẫn ra |
| `tripId` | ✅ | Chuyến tồn tại. Dùng để tra `Trip.RouteId` cho điều kiện tuyến. **Không tồn tại → 404** (lỗi phía gọi, KHÁC hẳn "voucher bị từ chối") |
| `orderAmount` | ✅ | Tổng tiền **trước** giảm giá; `0` … `9999999999.99` |

```json
// 200 — mã dùng được
{
  "valid": true,
  "reasonCode": null,
  "message": null,
  "voucherId": "9a1b2c3d-0000-0000-0000-000000000000",
  "code": "SUMMER10",
  "discountType": "Percent",
  "discountAmount": 15000,
  "finalAmount": 135000,
  "maxDiscount": 20000
}
```

```json
// 200 — mã KHÔNG dùng được (vẫn là 200, xem vì sao bên dưới)
{
  "valid": false,
  "reasonCode": "BelowMinOrder",
  "message": "Đơn hàng chưa đạt giá trị tối thiểu 100.000đ",
  "voucherId": "3f4e5d6c-0000-0000-0000-000000000000",
  "code": "FIX20K",
  "discountType": "FixedAmount",
  "discountAmount": 0,
  "finalAmount": 80000,
  "maxDiscount": null
}
```

> 📌 **Vì sao mã bị từ chối trả `200` kèm `valid: false`, KHÔNG phải 400/404/409.** Màn thanh toán
> gọi endpoint này **mỗi lần khách gõ**. "Mã này không dùng được" là **câu trả lời** của một câu hỏi
> hợp lệ, không phải request hỏng — trả 4xx sẽ buộc FE phân nhánh theo HTTP status cho một luồng
> hoàn toàn bình thường, làm ngập log lỗi và lệch với cách `GET /payments/{paymentCode}` trả 200 kèm
> `status: 'Failed'`. Chỉ **body sai khuôn** (thiếu `code`/`tripId`/`orderAmount`, `code` ngoài
> 2–20 ký tự) mới là **400** theo khuôn `{ message, errors }`; `tripId` không tồn tại là **404**.
>
> 🔴 **Ở `POST /payments` thì ngược lại — mã không hợp lệ là 409.** Ở đó khách đã **bấm xác nhận**,
> nên âm thầm thu đủ tiền là lỗi nặng; xem 🔴 ở mục "Thanh toán".

**Bảng mã từ chối (`reasonCode`)** — hằng trong `Dtos/Vouchers/VoucherReasonCodes.cs`. FE **dịch
theo `reasonCode`**, KHÔNG so chuỗi `message` (câu chữ sẽ còn đổi):

| `reasonCode` | Điều kiện | `message` |
|---|---|---|
| `NotFound` | Không có voucher với `code` (sau chuẩn hoá HOA) | Mã voucher không tồn tại |
| `Inactive` | `status = 'Inactive'` | Mã voucher đã ngừng áp dụng |
| `NotStarted` | `now < validFrom` | Mã voucher chưa tới thời gian áp dụng |
| `Expired` | `now > validUntil` | Mã voucher đã hết hiệu lực |
| `OutOfStock` | `usedCount >= quantity` | Mã voucher đã hết lượt sử dụng |
| `WrongRoute` | `routeId != null && routeId != trip.routeId` | Mã voucher không áp dụng cho tuyến này |
| `BelowMinOrder` | `orderAmount < minOrderValue` | Đơn hàng chưa đạt giá trị tối thiểu {minOrderValue}đ |

**Thứ tự kiểm tra — thứ tự này LÀ một phần của hợp đồng**, vì câu trả về là điều kiện **đầu tiên**
không thoả, nên nó quyết định câu khách nhìn thấy:

```
NotFound → Inactive → NotStarted → Expired → OutOfStock → WrongRoute → BelowMinOrder
```

> **Vì sao thứ tự này?** Năm điều kiện đầu là **sự thật tuyệt đối về voucher** — khách có sửa gì
> trong giỏ hàng cũng không cứu được, nên hỏi trước. Hai điều kiện cuối **phụ thuộc giỏ hàng**, hỏi
> sau. Trong nhóm tuyệt đối, `Inactive` đứng trước nhóm thời gian vì "ngừng áp dụng" là quyết định
> hiện tại của người vận hành, mạnh hơn một mốc ngày có thể họ chưa xem lại. `OutOfStock` đứng
> trước hai điều kiện giỏ hàng vì bảo khách "thêm 100.000đ nữa đi" cho một mã **đã hết lượt** là câu
> sai đường tệ nhất có thể trả. Trong nhóm giỏ hàng, tuyến trước giá trị đơn: tuyến là **dùng được
> hay không** (mã vốn không thuộc chuyến này), còn giá trị đơn là **gợi ý khách làm được gì đó**.

**Cách tính tiền giảm** — chốt ở đây, không chỉ ở code:

```
Percent:      raw      = orderAmount * discountValue / 100
              rounded  = Round(raw, 0, MidpointRounding.AwayFromZero)   // đồng nguyên
              discount = maxDiscount != null ? Min(rounded, maxDiscount) : rounded
FixedAmount:  discount = discountValue

Cả hai:       discount    = Min(discount, orderAmount)   // không bao giờ âm
              finalAmount = orderAmount - discount       // >= 0
```

> 🔴 **Làm tròn về ĐỒNG NGUYÊN, không giữ phần lẻ.** VND không có đơn vị lẻ, và quan trọng hơn:
> `PaymentInitiationRequest.Amount` là `long` — số gửi lên cổng buộc phải nguyên. Giữ 2 số thập
> phân ở đây thì `amount` gửi cổng (đã làm tròn) **không khớp** `payment.Amount` trong CSDL, và
> `PaymentSettlementService.SettleAsync` so `payment.Amount != callback.Amount` sẽ **404 mọi
> callback** — không giao dịch nào chốt được. Làm tròn ngay tại đây giữ cho
> `originalAmount - discountAmount == amount` đúng tuyệt đối, tức hoá đơn cộng lại khớp.

> 📌 **`Min(discount, orderAmount)` là dòng bắt buộc.** Không có nó thì một mã `FixedAmount`
> 50.000đ áp lên đơn 20.000đ cho ra `finalAmount = -30.000` — số tiền âm chảy thẳng xuống cổng.
> `finalAmount` **không bao giờ âm**, và `discountAmount` không bao giờ lớn hơn đơn.

> ⚠️ **`validate` là XEM TRƯỚC, không phải CHỐT.** Giữa lúc khách thấy "giảm 15.000đ" và lúc bấm
> xác nhận, mã có thể hết lượt (người khác vừa dùng) hoặc hết hạn. Số tiền giảm chỉ được **chốt
> lại** ở tầng thanh toán. FE **không** được coi kết quả `validate` là bảo đảm.

#### Áp dụng voucher vào giao dịch — phần "áp dụng" của dòng 52

**Không có endpoint riêng.** "Áp dụng vào đơn hàng" xảy ra ở hai chỗ, cả hai đều **không phải**
endpoint mới:

| Chỗ | Việc | Ai gọi |
|---|---|---|
| `POST /payments` lúc **tạo** giao dịch | Kiểm lại voucher, trừ vào `total` để ra `amount` gửi cổng. Mã không hợp lệ → **409** | Endpoint của Hiếu |
| Callback/job đối soát lúc tiền **về** (`Success`) | Gọi `IVoucherRedemptionService.RedeemAsync` để ghi `VoucherUsages` + tăng `usedCount` | Endpoint của Hiếu |

Hai thời điểm **khác nhau và không được gộp**: lúc tạo giao dịch thì voucher mới chỉ được **giữ
chỗ** (khách còn có thể bỏ ngang), chỉ khi tiền về mới thực sự **tiêu thụ**. Gộp lại thì mọi giao
dịch khách bỏ ngang đều ăn mất một lượt voucher.

`RedeemAsync(paymentCode, voucherCode, ...)`:

1. **Idempotent theo `paymentCode`** — đã có `VoucherUsage` cùng `paymentCode` thì trả bản ghi cũ,
   **không** tăng `usedCount`. Đây là chốt cho callback trùng của cổng (MoMo gửi lại tới khi nhận
   204) và cho job đối soát chạy đè.
2. **Kiểm lại từ đầu** (gọi lại `IVoucherValidationService`) — không tin kết quả `validate` trước đó,
   không tin con số FE gửi lên.
3. Ghi `VoucherUsage` + tăng `Vouchers.UsedCount` trong **một** `SaveChangesAsync`.

Kết quả: `Ok` (kể cả lượt gửi lại) · `Conflict` khi mã vừa hết lượt hoặc vừa bị người khác dùng.

#### `GET /vouchers/statistics` — thống kê hiệu quả voucher đã phát hành

> Task *"API thống kê hiệu quả voucher"* (dòng 54, US 18, Sprint 3, Nguyễn Duy Kiên).

Một endpoint duy nhất, trả về **một dòng cho mỗi voucher đã phát hành** — kể cả voucher **chưa ai
dùng** — cộng các con số tổng của cả chương trình khuyến mãi.

| Method | Endpoint | Mô tả | Trả về |
|---|---|---|---|
| GET | `/vouchers/statistics` | Hiệu quả của từng voucher đã phát hành | `VoucherStatistics` |

Yêu cầu vai trò `Manager` hoặc `Admin` — cùng lý do nhóm `/admin/feedbacks`: đây là số liệu vận hành
của nhà xe, không phải dữ liệu của một hành khách. Không đăng nhập → **401**; không đủ quyền →
**403**. Endpoint **không có tham số nào**, nên không có nhánh 400/404: đúng quyền là luôn **200**,
kể cả khi chưa có voucher nào (`items` rỗng, mọi con đếm bằng 0).

```json
// VoucherStatistics — ví dụ GET /vouchers/statistics
{
  "totalVouchers": 8,
  "totalIssued": 3600,
  "totalUsed": 1025,
  "totalOrderAmount": 51200000,
  "totalDiscountAmount": 6400000,
  "neverUsedVouchers": 3,
  "items": [
    {
      "voucherId": "9a1b2c3d-0000-0000-0000-000000000000",
      "code": "SUMMER10",
      "name": "Giảm 10% mùa hè",
      "status": "Active",
      "routeId": null,
      "routeCode": null,
      "routeName": null,
      "quantity": 500,
      "usedCount": 320,
      "uniqueCustomers": 210,
      "totalOrderAmount": 48000000,
      "totalDiscountAmount": 4800000,
      "firstUsedAt": "2026-06-01T02:15:00Z",
      "lastUsedAt": "2026-08-30T11:40:00Z",
      "validFrom": "2026-06-01T00:00:00Z",
      "validUntil": "2026-08-31T23:59:59Z"
    }
  ]
}
```

| Trường | Kiểu | Ghi chú |
|---|---|---|
| `totalVouchers` | number | Tổng số voucher đã phát hành — **mọi trạng thái**, kể cả `Inactive` |
| `totalIssued` | number | Tổng `quantity` — tổng số lượt đã phát hành |
| `totalUsed` | number | Tổng lượt đã tiêu thụ. Nguồn: `VoucherUsages` — xem 📌 |
| `totalOrderAmount` | numeric | Tổng tiền **trước** giảm của mọi lượt đã tiêu thụ |
| `totalDiscountAmount` | numeric | Tổng tiền đã giảm |
| `neverUsedVouchers` | number | Số voucher đã phát hành mà **chưa ai dùng** (`usedCount = 0`) |
| `items` | mảng | Một dòng mỗi voucher đã phát hành, kể cả voucher chưa ai dùng |

Một dòng của `items`:

| Trường | Kiểu | Ghi chú |
|---|---|---|
| `voucherId`, `code`, `name` | guid / string / string | |
| `status` | varchar(20) | `'Active' \| 'Inactive'` |
| `routeId` | guid nullable | `null` = **mọi tuyến** |
| `routeCode`, `routeName` | string nullable | Ghép sẵn để màn hình không phải gọi `GET /routes/{id}` cho từng dòng, cùng lối `byRoute` của thống kê phản ánh |
| `quantity` / `usedCount` | number | `usedCount` **đếm từ `VoucherUsages`**, không đọc cột `Vouchers.UsedCount` — xem 📌 |
| `uniqueCustomers` | number | Số khách **khác nhau** đã dùng mã này. **Nhỏ hơn `usedCount` là bình thường** — luật "mỗi khách một lượt" chưa được chốt (xem "Câu hỏi mở") |
| `totalOrderAmount` / `totalDiscountAmount` | numeric | Của riêng voucher này |
| `firstUsedAt` / `lastUsedAt` | timestamptz nullable | **`null` khi `usedCount = 0`** |
| `validFrom` / `validUntil` | timestamptz | Khoảng hiệu lực — đọc kèm để biết **vì sao** một dòng bằng 0 |

Năm đẳng thức luôn đúng — màn hình được phép dựa vào, test khoá lại:

```
sum(items[].usedCount)            == totalUsed
sum(items[].totalOrderAmount)     == totalOrderAmount
sum(items[].totalDiscountAmount)  == totalDiscountAmount
count(items)                      == totalVouchers
count(items where usedCount == 0) == neverUsedVouchers
```

> 📌 **Vì sao đẳng thức ở đây là phép kiểm THẬT, không phải chuyện đương nhiên.** Sáu con số tổng
> đến từ **truy vấn gộp độc lập** trên hai bảng, **không** cộng lại từ `items` — nếu cộng từ `items`
> thì mọi đẳng thức đúng theo định nghĩa và không test nào có thể đỏ. Đây đúng lối
> `FeedbackStatisticsService` đã làm cho thống kê phản ánh: đếm thẳng bằng một truy vấn riêng thì
> đẳng thức mới bắt được lỗi lọc rơi dòng.

> 📌 **Vì sao voucher chưa ai dùng VẪN có một dòng.** Đó là câu trả lời đắt nhất của một bảng "thống
> kê hiệu quả": mã đã phát hành mà không ai dùng là **hiệu quả bằng 0**. Một phép gộp ngây thơ trên
> `VoucherUsages` sẽ **làm rơi** đúng những dòng đó. Nên nguồn của `items` là bảng `Vouchers` (mọi
> dòng, **KHÔNG lọc `status`**), còn `VoucherUsages` chỉ ghép số liệu vào. Cùng lối lập luận đã dùng
> cho `byType` của thống kê phản ánh: trục cố định — tháng không có khiếu nại nào thì màn hình phải
> vẽ cột 0 chứ không phải thiếu mất một cột.
>
> Hệ quả: nếu ai đó lọc `items` chỉ lấy `Active`, `sum(items[].usedCount)` sẽ **tụt xuống dưới**
> `totalUsed` và đẳng thức đỏ ngay.

> 📌 **Vì sao `usedCount` đếm từ `VoucherUsages`, KHÔNG đọc cột `Vouchers.UsedCount`.**
> `Vouchers.UsedCount` là **bộ đếm denormalized** do `VoucherRedemptionService` tăng, tồn tại để
> đường kiểm mã (`usedCount >= quantity` → `OutOfStock`) không phải `COUNT(*)` mỗi lần khách gõ. Nó
> **không phải bản ghi sự thật** — `VoucherUsages` mới là, và hai cột tiền cũng nằm ở đó. `usedCount`
> với tiền **phải cùng một nguồn**, nếu không một dòng có thể tự mâu thuẫn (`usedCount: 5` nhưng tiền
> chỉ cộng từ 4 dòng). Hệ quả **có chủ ý**: nếu bộ đếm vênh, bảng thống kê là **bên đúng** và **phơi
> ra** chỗ vênh, thay vì nhân bản nó thêm một lần nữa.

> 📌 **Vì sao KHÔNG join `Payments`.** `VoucherUsage.PaymentCode` là **cột trần không FK** (xem 📌 ở
> mục Entity `VoucherUsage`), nên join là **inner join** và sẽ **làm rơi** mọi dòng usage không có
> payment tương ứng. Hai cột tiền đã có sẵn ngay trên `VoucherUsages` (`orderAmount`/`discountAmount`
> là ảnh chụp lúc áp) — không cần đi đâu xa. Hợp đồng đã chốt đúng điều này ở câu hỏi mở *"Đẩy
> `VoucherCode` + `DiscountAmount` lên `Payments`?"*: **chưa, hiện đọc qua `VoucherUsages`**.

**Sắp xếp:** `usedCount` giảm dần — mã hiệu quả nhất lên đầu, đúng lối `byRoute` sắp theo `count`
giảm dần; trùng số thì theo `code` tăng dần (ordinal) cho thứ tự tất định. Hệ quả **đã biết**: voucher
chưa ai dùng nằm **cuối** bảng, còn `neverUsedVouchers` cho màn hình biết có bao nhiêu để tự lọc.
Muốn đảo thành "mã bị bỏ xó lên đầu" thì chốt ở nhóm rồi sửa mục này trước (⛔5).

**Cố ý CHƯA có:** lọc theo khoảng thời gian, lọc theo trạng thái, lọc theo tuyến, phân trang, và
**trường tỉ lệ tiêu thụ tính sẵn ở server**. Task chỉ hỏi "hiệu quả voucher"; khoảng
`validFrom..validUntil` của **chính voucher** đã là khoảng chiến dịch của nó (một lượt chỉ ghi được
khi voucher còn hiệu lực — xem bảng `reasonCode` ở `POST /vouchers/validate`), nên số liệu trọn đời
của voucher **chính là** số liệu chiến dịch. Thêm tham số lọc hay thêm trường là **đổi hình dạng
API** — phải bàn ở nhóm rồi sửa mục này trước khi code (⛔5), đừng tự thêm.

> 🔹 **Tỉ lệ tiêu thụ là giá trị DẪN XUẤT, FE tự tính** (`usedCount / quantity`) — **không** phải
> trường của API. Cặp `quantity`/`usedCount` đã chính xác và không thể hiểu sai, còn một trường tỉ lệ
> mở ra ba tranh luận không cần thiết: chia cho 0 (`quantity = 0`), đơn vị (phân số hay phần trăm), và
> **tỉ lệ vượt 100%** khi dòng 51 sửa giảm `quantity` xuống dưới `usedCount` — chuyện có thật, vì CRUD
> cho sửa `quantity`. Cùng luật đã chốt cho thống kê phản ánh: `FeedbackTypeCountResponse` không có
> trường nào ngoài cặp mã/đếm.
>
> 🔹 **Các trường cấu hình `discountType` / `discountValue` / `minOrderValue` / `maxDiscount` cố ý
> KHÔNG có ở đây** — chúng thuộc `GET /vouchers` (dòng 51). Bảng này trả lời "hiệu quả thế nào",
> không trả lời "luật của mã là gì".
>
> 🔹 Index `VoucherUsages (VoucherId, CreatedAt)` được dựng **có chủ ý** cho việc lọc theo khoảng
> ngày, nên ngày nhóm chốt thêm bộ lọc thì hạ tầng đã sẵn — chỉ còn sửa mục này trước. Lưu ý khi đó:
> rút ngắn `validUntil` sau khi đã có lượt dùng làm những lượt cũ rơi **ra ngoài** khoảng hiệu lực
> mới, nên bộ lọc mặc định lấy `[validFrom, validUntil]` sẽ **mất** chúng.

### Chống tiêu thụ quá `quantity` — hai lớp

| Lớp | Cơ chế | Chặn được gì |
|---|---|---|
| Kiểm trong service | `usedCount >= quantity` → `OutOfStock`; lượt ghi cũng kiểm lại | Đường đi thông thường |
| **Concurrency token `xmin`** trên `Vouchers` | EF thêm `WHERE xmin = …` vào câu `UPDATE`; lượt thua nhận `DbUpdateConcurrencyException` → **409** | Hai lượt áp dụng **song song** cùng đọc `usedCount = quantity - 1` rồi cùng ghi |

`xmin` là **cột hệ thống của PostgreSQL**, không sinh cột mới trong migration — cùng đúng một dòng
mà `AppDbContext.Payment.cs` đã dùng cho nhánh đua callback trùng (dòng 33, Hoàng). Ở đây nó chặn
đúng ca: hai khách cùng bấm xác nhận lượt voucher **cuối cùng**.

✅ **Đã kiểm bằng SQL sinh ra 10/10/2026** (`dotnet ef migrations script` rồi `grep -c xmin` → `0`):
file migration `.cs` **có** dòng `xmin = table.Column<uint>(type: "xid", rowVersion: true, …)` — đó là
EF ghi đủ cột của model, **bình thường, đừng xoá** — còn SQL gửi xuống PostgreSQL thì **không** có
`xmin`, đúng như câu trên.

> ⚠️ **Provider InMemory BỎ QUA concurrency token** — bộ test của dự án **không chứng minh được**
> chốt này, đúng như mục "Giữ chỗ" đã ghi cho partial unique index chống trùng ghế. Test chỉ ghim
> được **hành vi của service** (thấy `Conflict` thì không ghi gì); phần thật sự đua nhau phải tin
> vào PostgreSQL. Đừng đọc "test xanh" thành "đã chống được đua".

> 📌 **Vì sao KHÔNG kẹp `usedCount <= quantity` bằng check constraint.** Đó sẽ là check constraint
> **đầu tiên** của dự án (hiện có 0) — một lối mới cho cả nhóm, đổi lấy một ràng buộc mà `xmin` đã
> phủ. Muốn thêm thì bàn ở tầng nhóm, không tự ý đưa vào migration.

### Câu hỏi mở cho nhóm — chưa chốt, chưa cài

| Câu hỏi | Hiện trạng |
|---|---|
| **Mỗi khách chỉ dùng một lần?** Luật "mỗi người một lượt" chưa có trong bảng phân công, bản nháp FE cũng chỉ có `quantity`/`usedCount` **toàn cục** (không phải mỗi người) | **Chưa cài.** `VoucherUsage.UserId` đã ghi sẵn nên thêm luật này sau **không phải migrate lại** — chỉ thêm một điều kiện `EXISTS` vào chuỗi kiểm tra |
| **Một đơn dùng được nhiều mã?** Hợp đồng và bản nháp FE đều là **một** `code` mỗi đơn | Một mã. `VoucherUsages` nhiều dòng cho một `paymentCode` là chuyện chưa chốt — unique index trên `paymentCode` đang **chặn cứng** điều đó |
| **Đẩy `VoucherCode` + `DiscountAmount` lên `Payments`?** | Chưa — xem 📌 ở mục "Thanh toán". Hiện đọc qua `VoucherUsages` |
| **Voucher sinh từ đâu?** Bảng phân công chỉ có CRUD tay (dòng 51); không có story nào cho mã tự sinh hay phát theo chiến dịch | Ngoài phạm vi — CRUD tay |
| **US 18 thuộc sprint nào?** Bảng tính xếp **Sprint 3**; `docs/02-sprint-roadmap.md` lại đề xuất chuyển US 18 từ Sprint 5 sang **Sprint 4**; quy ước A9 xếp nhóm bảng này ở Sprint 4 | Nhóm cần chốt **một** bảng phân công (cùng dạng xung đột đã ghi ở mục "Thanh toán") |

### Việc của Dăm — migration ✅ đã xong, còn `database update`

Migration đã sinh, đã soát và **đã merge** (`20261010105248_Sprint3_Payments_Vouchers_Tickets`,
PR #144) — **đừng chạy lại `migrations add`**, sẽ sinh migration trùng.

⏳ Việc còn lại **đúng một lệnh**, trên máy có `dotnet-ef` và chuỗi kết nối:

```bash
cd backend/SmartBus.Api
dotnet ef database update
```

Checklist soát migration, danh sách file đã có sẵn, và các chốt cần đối chiếu: xem
**`docs/27-huong-dan-migrate-vouchers.md`**.

---

## Hoàn tiền — `/refunds`

> Story 8 *"Hoàn tiền tự động"*. 🟢 **Bảng `Refunds` + `RefundLogs` — migration ĐÃ SINH, ĐÃ SOÁT**
> (nhánh `feature/8-migrate-refunds-refundlogs`, Vàng Thị Dăm — task *"Migrate bảng Refunds,
> RefundLogs"*, dòng 38 Sprint 4). Entity + cấu hình: `AppDbContext.Refund.cs`; lý do từng cột, chỉ
> mục và bất biến: **`docs/40-csdl-hoan-tien.md`**.
>
> 🔴 **A9 KHÔNG có hai bảng này.** Đây là nhóm bảng thứ tư ngoài danh sách 20 bảng đóng băng, sau
> `CancellationRequests` + `TicketChanges` (docs/32 §1), `Notifications` (docs/33 §1) và
> `TicketValidations` (docs/34 §1). Nhóm cần bổ sung A9 **một lượt** cho cả bốn nhóm.
>
> Mục này chốt phần **ĐỌC** của hoàn tiền — task dòng 42 *"API tra cứu trạng thái hoàn tiền của
> giao dịch"* (Vàng Thị Dăm). Ba mảnh còn lại của story **chưa có trong tài liệu này** và chưa có
> chủ ở tầng API: `POST /admin/refunds/{id}/retry` (dòng 41), `GET /admin/refunds` có lọc + phân
> trang (màn dòng 46–47), và `GET /admin/refunds/export` (dòng 47). Hình dạng **dự kiến** của ba
> endpoint đó nằm ở đầu `frontend/src/api/refundAdminApi.ts` — **chưa cam kết** cho tới khi có mục
> trong file này.

### Vì sao hoàn tiền là bảng RIÊNG, không phải một cột của `CancellationRequests`

Bảng yêu cầu huỷ trả lời *"có cho huỷ không"*; bảng hoàn tiền trả lời *"đã trả được tiền chưa"*.
Hai câu hỏi khác nhau và **trả lời lệch nhau được trong nhiều ngày**: quản lý duyệt huỷ xong mà
cổng hoàn tiền lỗi thì yêu cầu đã `Approved` trong khi khoản hoàn còn `Pending` — có khi `Failed`
rồi phải thử lại. Gộp hai thứ vào một cột trạng thái là bịt mất đúng khoảng lệch đó.

Hệ quả trực tiếp cho mục này: **một khoản hoàn không có trạng thái "đã huỷ"** hay bất kỳ giá trị
nào suy từ yêu cầu huỷ. Ai đọc hợp đồng này để vẽ màn hình thì đọc `status` của khoản hoàn, đừng
suy từ trạng thái yêu cầu huỷ.

### `Refund` — hình dạng trả về

Khớp đúng interface `Refund` trong bản nháp FE đã merge (`frontend/src/api/refundAdminApi.ts`,
PR #187, màn quản trị của Băng) — trường **giữ nguyên tên**, không đổi một chữ nào:

| Trường | Kiểu | Ghi chú |
|---|---|---|
| `id` | GUID | Khoá dòng |
| `refundCode` | string | `"RF-xxxxxxxx"` — duy nhất, **khoá tra cứu bằng mắt** |
| `source` | string | `"PaymentFailure"` \| `"TicketCancellation"` |
| `ticketCode` | string \| **null** | ⚠️ **nullable** — xem mục "Bảy trường nullable" bên dưới |
| `routeCode` | string \| **null** | ⚠️ nullable, cùng lý do |
| `routeName` | string \| **null** | ⚠️ nullable |
| `origin` | string \| **null** | ⚠️ nullable |
| `destination` | string \| **null** | ⚠️ nullable |
| `departureTime` | ISO 8601 \| **null** | ⚠️ nullable — giờ khởi hành của chuyến (UTC) |
| `seatNumber` | string \| **null** | ⚠️ nullable |
| `passengerName` | string | Họ tên người **nhận lại** tiền (`Users.FullName`) |
| `amount` | number | Số tiền hoàn (VND), `numeric(12,2)` |
| `methodName` | string | Mã phương thức của giao dịch gốc — `"MoMo"` \| `"VNPay"` \| `"ZaloPay"` \| `"BankCard"` |
| `paymentCode` | string | `"PM-xxxxxxxx"` — **khoá tra theo giao dịch** |
| `gatewayTransactionId` | string \| null | Mã giao dịch phía cổng của lượt hoàn **thành công**; null khi chưa có |
| `status` | string | `"Pending"` \| `"Succeeded"` \| `"Failed"` |
| `failReason` | string \| null | Lý do lượt gọi **gần nhất** thất bại; chỉ khác null khi `status` = `"Failed"` |
| `retryCount` | number | Số lượt **gọi lại** cổng (không tính lượt gọi đầu) — xem mục riêng |
| `createdAt` | ISO 8601 | Lúc hệ thống tạo khoản hoàn |
| `processedAt` | ISO 8601 \| null | Lúc cổng chốt kết quả cuối; null khi còn `Pending` |
| `updatedAt` | ISO 8601 \| null | Chỉ đổi khi dòng bị sửa (thử lại, lật trạng thái) |

#### Bảy trường nullable — ⚠️ FE phải nới kiểu

Bản nháp `refundAdminApi.ts` khai `ticketCode`, `routeCode`, `routeName`, `origin`, `destination`,
`departureTime`, `seatNumber` là `string` **không nullable**. Hợp đồng này khai **nullable**, và đây
là chủ ý, không phải sót:

Khoản hoàn nguồn `PaymentFailure` **thường không có vé nào**. Vé chỉ được phát hành sau khi tiền về
thành công (mục "Phát hành vé sau khi thanh toán"), nên giao dịch hỏng thì chưa có vé — và đó chính
là nhánh mà US 8 nêu ra **đầu tiên**. Bảy trường trên đều suy từ `Tickets → Trips → Routes →
Seats`, nên chúng null **cùng lúc** với `ticketCode`, không bao giờ lệch nhau.

> 🔴 Đừng "sửa" bằng chuỗi rỗng. `departureTime: ""` đi qua `new Date("")` ra `Invalid Date` — màn
> hình hiện `NaN` chứ không hiện ô trống. Null là giá trị duy nhất nói đúng "không có chuyến nào
> gắn với khoản hoàn này", cùng lối `boardingStopName` của `/tickets`.

#### `retryCount` — con số này đếm ở đâu ra

`Refunds` **cố ý không có cột đếm** (lý do: docs/40 §2.4 — số lượt gọi là phép đếm trên nhật ký,
không phải trạng thái của khoản hoàn). Giá trị này là:

```
COUNT(*) trên RefundLogs WHERE RefundId = <khoản> AND Trigger <> 'Initial'
```

Nghĩa là **số lượt gọi LẠI**, không phải tổng số lượt gọi: lượt gọi đầu tiên (`Initial`, do service
hoàn tiền tự động thực hiện) không tính. Khớp đúng ngữ nghĩa bản nháp FE — dòng vừa tạo có
`retryCount = 0`.

#### `status` và số dòng — đọc `status`, ĐỪNG đếm dòng

🔴 Một giao dịch **có thể có nhiều dòng** trong `Refunds` theo thời gian: khoản đầu `Failed` hết lịch
thử lại rồi bị bỏ, sau đó phát sinh khoản mới. Chỉ mục duy nhất `IX_Refunds_PaymentId_Active` chỉ
chặn **hai khoản chưa thất bại** cùng lúc, cố ý **không** chặn dòng `Failed` cũ.

Nên câu hỏi *"giao dịch này đã được hoàn tiền chưa"* trả lời bằng **`status` của dòng**, không bằng
số dòng trả về. FE lọc theo `status` chứ đừng suy từ `items.length`.

### Endpoints

| Method | Endpoint | Task | Trạng thái |
|---|---|---|---|
| GET | `/refunds/{refundCode}` | *"API tra cứu trạng thái hoàn tiền của giao dịch"* (dòng 42) — Vàng Thị Dăm | ✅ đã làm |
| GET | `/refunds?paymentCode=&status=` | *"API tra cứu trạng thái hoàn tiền của giao dịch"* (dòng 42) — Vàng Thị Dăm | ✅ đã làm |
| POST | `/admin/refunds/{id}/retry` | *"Xử lý hoàn tiền thất bại + retry theo lịch"* (dòng 41) — Phùng Duy Hoàng | Chưa làm |
| GET | `/admin/refunds` | màn *"Admin quản lý yêu cầu hoàn tiền"* (dòng 46–47) — Nguyễn Đình Băng | Chưa làm |
| GET | `/admin/refunds/export` | *"Component lọc và xuất danh sách hoàn tiền"* (dòng 47) — Nguyễn Đình Băng | Chưa làm |

Cả hai endpoint đã làm đều **yêu cầu đăng nhập** (`[Authorize]` trần, **không** policy vai trò) và
**chỉ trả khoản hoàn của chính người gọi** — phạm vi "của tôi" nằm NGAY TRONG TRUY VẤN theo
`Refunds.UserId`, cùng lối `/seat-holds/{sessionCode}`, `/tickets/me` và `/feedbacks/me`. Không có
tham số nào để dò khoản hoàn của người khác, và ca "của người khác" trả **404** chứ không phải 403 —
không để lộ khoản hoàn nào đang tồn tại.

> **Vì sao không gắn policy `ManagerOrAbove`?** Vì đây là cửa của **hành khách**: người vừa bị trừ
> tiền là người cần tra nhất. Nhóm quản trị tra khoản hoàn của người khác bằng `GET /admin/refunds`
> (chưa làm) — hai đối tượng, hai bề mặt, cùng lối `/feedbacks` (của tôi) và `/admin/feedbacks`
> (toàn hệ thống).

#### `GET /refunds/{refundCode}` — tra theo mã hoàn tiền

```json
// 200
{
  "id": "9d5f1e2c-0000-0000-0000-000000000000",
  "refundCode": "RF-9d5f1e2c",
  "source": "TicketCancellation",
  "ticketCode": "TK-08-9B0C1D",
  "routeCode": "08",
  "routeName": "Cầu Giấy — Bờ Hồ Hoàn Kiếm",
  "origin": "Cầu Giấy",
  "destination": "Bờ Hồ Hoàn Kiếm",
  "departureTime": "2026-10-13T10:30:00Z",
  "seatNumber": "A01",
  "passengerName": "Nguyễn Văn An",
  "amount": 7200.00,
  "methodName": "MoMo",
  "paymentCode": "PM-9d3e6a0b",
  "gatewayTransactionId": "MM-99182220",
  "status": "Succeeded",
  "failReason": null,
  "retryCount": 0,
  "createdAt": "2026-10-11T08:00:00Z",
  "processedAt": "2026-10-11T10:00:00Z",
  "updatedAt": "2026-10-11T10:00:00Z"
}
```

```json
// 200 — nguồn PaymentFailure: không có vé nào, bảy trường vé/chuyến đều null
{
  "id": "4c1e7a2b-0000-0000-0000-000000000000",
  "refundCode": "RF-4c1e7a2b",
  "source": "PaymentFailure",
  "ticketCode": null,
  "routeCode": null,
  "routeName": null,
  "origin": null,
  "destination": null,
  "departureTime": null,
  "seatNumber": null,
  "passengerName": "Trần Thị Bình",
  "amount": 12000.00,
  "methodName": "VNPay",
  "paymentCode": "PM-8f2b1d64",
  "gatewayTransactionId": null,
  "status": "Failed",
  "failReason": "VNPay hoàn tiền quá hạn chờ phản hồi (timeout).",
  "retryCount": 2,
  "createdAt": "2026-10-14T05:00:00Z",
  "processedAt": "2026-10-14T07:00:00Z",
  "updatedAt": "2026-10-14T07:00:00Z"
}
```

| Mã | Khi nào |
|---|---|
| 200 | Tìm thấy khoản hoàn thuộc chính người gọi |
| 401 | Không có token |
| 404 | Mã không tồn tại **hoặc** thuộc người khác — cùng một câu, không phân biệt |

**Không có nhánh 400.** `refundCode` là đoạn đường dẫn nên không có gì để validate: mã dài bao nhiêu
cũng chỉ là một chuỗi tra không ra, và tra không ra là 404.

So khớp mã là **đúng từng ký tự**, không bỏ qua hoa/thường — cùng lối `/seat-holds/{sessionCode}`
so `SessionCode`. Mã do hệ thống sinh (`RF-` + hex) nên chỗ sinh và chỗ tra dùng chung một nguồn;
muốn tra kiểu bỏ qua hoa/thường thì phải thêm chỉ mục hàm (`lower(RefundCode)`) và **bỏ được** chỉ
mục unique hiện có — không đáng, và làm lệch với mọi mã khác của dự án.

#### `GET /refunds` — tra theo mã giao dịch, và danh sách khoản hoàn của tôi

Đây là endpoint chính của task dòng 42: *"trạng thái hoàn tiền **của giao dịch**"*.

| Query | Bắt buộc | Ràng buộc |
|---|---|---|
| `paymentCode` | ❌ | Mã giao dịch gốc (`Payments.PaymentCode`), dạng `"PM-xxxxxxxx"`. Tối đa 64 ký tự. **Bỏ trống = mọi khoản hoàn của tôi** |
| `status` | ❌ | `Pending` \| `Succeeded` \| `Failed`. Tối đa 20 ký tự |

```json
// 200 — GET /refunds?paymentCode=PM-8f2b1d64 : trạng thái hoàn tiền của MỘT giao dịch
[
  {
    "id": "4c1e7a2b-0000-0000-0000-000000000000",
    "refundCode": "RF-4c1e7a2b",
    "source": "PaymentFailure",
    "status": "Failed",
    "retryCount": 2,
    "paymentCode": "PM-8f2b1d64",
    "amount": 12000.00
  }
]
```

> Ví dụ trên lược bớt trường cho dễ đọc — **mọi dòng đều đủ 21 trường** như bảng "Hình dạng `Refund`".

- Trả **mảng trần, không phân trang**, **mới nhất trước**. Một hành khách có vài khoản hoàn, và một
  giao dịch có vài dòng — phân trang là nghi thức thừa (cùng lối `/feedbacks/me`,
  `/monthly-passes/me`, `/tickets/me`).
- 🔴 **Giao dịch không có khoản hoàn nào → 200 kèm `[]`, KHÔNG phải 404.** *"Giao dịch này không
  phát sinh khoản hoàn nào"* là một **câu trả lời**, không phải lỗi — và là câu trả lời thường gặp
  nhất (mọi giao dịch thành công đều rơi vào ca này). Trả 404 ở đây là bắt frontend coi "không có
  hoàn tiền" như một sự cố.
- 🔴 **`paymentCode` không tồn tại, hoặc là giao dịch của người khác → cũng 200 kèm `[]`.** Cùng một
  câu trả lời cho cả ba ca, nên endpoint này **không dùng được để dò** xem một mã giao dịch có tồn
  tại hay không. Phạm vi lọc theo `Refunds.UserId` đã chặn từ trong truy vấn.
- `status` **gõ sai → 200 kèm `[]`**, không phải 400 (mục D3 của docs/03-quy-uoc.md): mã lạ có thể là
  trạng thái hợp lệ trong tương lai, mà 400 ở đây làm màn hình cũ vỡ khi backend thêm giá trị mới.
  Cùng lối `/feedbacks/me`.
- Mã quá dài (>64 ký tự với `paymentCode`, >20 với `status`) → **400** theo cấu trúc lỗi chung
  `{ message, errors }` — đây là ràng buộc hình dạng, khác hẳn "gõ sai mã".

**Vì sao không có `GET /refunds/mine`.** Bản nháp `frontend/src/api/myRefundApi.ts` (nhánh
`feature/8-man-hinh-theo-doi-hoan-tien`, task dòng 44 của Hạnh) ghi endpoint dự kiến là
`GET /refunds/mine?status=`. Hợp đồng này **không** có đoạn `/mine`: danh sách này **vốn đã** là "của
tôi" — phạm vi nằm trong truy vấn, không nằm ở đoạn đường dẫn. Thêm `/mine` là dựng hai đường dẫn
cho cùng một câu hỏi, rồi phải trả lời câu tiếp theo *"vậy `/refunds` trần để làm gì?"* — và câu trả
lời duy nhất hợp lý là "trả khoản hoàn của mọi người", tức là mở một cửa hành khách nhìn thấy dữ
liệu của người khác. Danh sách toàn hệ thống là việc của `GET /admin/refunds`.

> ⚠️ **Việc của Hạnh (dòng 44):** bản nháp `myRefundApi.ts` chưa merge — khi nối API thật thì đổi
> `/refunds/mine` thành `/refunds` và **nới kiểu bảy trường nullable** ở mục trên. File đó đã ghi rõ
> hình dạng endpoint của nó là *"CHƯA cam kết"*, nên đây là chỗ chốt lại chứ không phải đổi hợp đồng
> đã hứa.

### Bốn câu hỏi nhóm phải chốt — chưa có câu trả lời trong code

| Câu hỏi | Hiện trạng |
|---|---|
| **Thông báo kết quả hoàn tiền đi kênh nào?** Bảng phân công dòng 45 ghi *"qua **email** và trên web"*, nhưng A8.5 chốt thông báo **chỉ in-app qua SignalR** + bảng `Notifications`, không email/SMS/Web Push | **Chưa cài.** Cùng dạng xung đột đã gặp ở dòng 7 (docs/33 §1) — nhóm nên chốt **một lần** cho cả hai |
| **Cổng sandbox nào hoàn tiền được?** Dòng 40 (Hiếu) gọi cổng để hoàn; MoMo/VNPay/ZaloPay sandbox có API hoàn hay không chưa ai kiểm | ✅ **Đã kiểm** (dòng 40 đã dựng, xem `docs/43-api-goi-cong-hoan-tien.md` §4): MoMo/ZaloPay sandbox CÓ API hoàn (khoá test công khai); **VNPay sandbox HẠN CHẾ** hoàn tiền — cần liên hệ VNPAY mở. Nhóm vẫn cần chốt cách xử lý với VNPay |
| **`Refunds` + `RefundLogs` bổ sung vào A9?** | Nhóm cần chốt — lần thứ **tư** việc "A9 lệch thực tế" được nhắc |
| **Ai làm `GET /admin/refunds`?** Bảng phân công có màn (dòng 46–47) nhưng **không có dòng nào** giao API danh sách ở tầng backend | Chưa có chủ |

## Khung giờ cao điểm — `/reports/peak-hours`

> Story 20 *"Tỷ lệ lấp đầy"*. ✅ Backend đã có (`PeakHoursReportController` — Trần Trung Hiếu,
> task *"API phân tích khung giờ cao điểm theo mật độ hành khách"*, Sprint 4). Mục này trả lời
> câu hỏi **"giờ nào trong ngày đông khách nhất"**; **tỷ lệ lấp đầy theo chuyến/tuyến/khung giờ**
> (Kiên) và **đề xuất tần suất** (Hoàng) là các task khác cùng story. Mục "Báo cáo doanh thu —
> /reports/revenue" (US 19, nhánh `feature/19-api-bao-cao-doanh-thu` — chưa merge lúc viết mục
> này) là mục chị em trên cùng bề mặt `/reports`. Chốt nghiệp vụ đầy đủ: `docs/38-api-khung-gio-cao-diem.md`.

**Endpoint yêu cầu vai trò `Admin` hoặc `Manager`** (story 20 là nghiệp vụ của quản lý). Người đã
đăng nhập nhưng không đủ quyền nhận **403** kèm body
`{ "message": "Bạn không có quyền truy cập tính năng này." }`.

#### `GET /reports/peak-hours` — khung giờ cao điểm theo mật độ hành khách

Query — **mọi tham số bỏ trống được**:

| Tham số | Mặc định | Ý nghĩa |
|---|---|---|
| `from` | 29 ngày trước `to` | Ngày bắt đầu (lịch **Việt Nam**, `yyyy-MM-dd`), TÍNH CẢ |
| `to` | hôm nay (giờ VN) | Ngày kết thúc (lịch Việt Nam), TÍNH CẢ |
| `routeId` | — | Chỉ tính các chuyến thuộc tuyến này; bỏ trống = toàn công ty |

```json
// PeakHoursReport — ví dụ khoảng 14 ngày
{
  "from": "2026-10-01",
  "to": "2026-10-14",
  "totalPassengers": 5230,
  "totalTrips": 412,
  "averagePassengersPerTrip": 12.69,
  "peakHours": [7, 8, 17, 18],
  "hours": [
    { "hour": 0,  "passengers": 0,   "trips": 0,  "averagePassengersPerTrip": 0,    "isPeak": false },
    { "hour": 7,  "passengers": 780, "trips": 41, "averagePassengersPerTrip": 19.02, "isPeak": true },
    { "hour": 8,  "passengers": 902, "trips": 44, "averagePassengersPerTrip": 20.5,  "isPeak": true },
    { "hour": 12, "passengers": 205, "trips": 31, "averagePassengersPerTrip": 6.61,  "isPeak": false }
    // … đủ 24 giờ, giờ rỗng vẫn hiện diện với số 0 …
  ]
}
```

| Trường | Ý nghĩa |
|---|---|
| `totalPassengers` / `totalTrips` | Tổng lượt khách / tổng chuyến khởi hành cả kỳ |
| `averagePassengersPerTrip` | Mật độ trung bình cả kỳ, làm tròn 2 chữ số — **chính là ngưỡng** đánh dấu cao điểm |
| `hours[]` | **Đủ 24 phần tử** (giờ 0–23, tăng dần) — trục biểu đồ cột theo khung giờ |
| `hours[].hour` | Giờ trong ngày (**giờ Việt Nam**), 0–23 |
| `hours[].passengers` / `trips` | Tổng lượt khách / số chuyến **khác nhau** khởi hành trong giờ đó |
| `hours[].averagePassengersPerTrip` | Mật độ giờ = passengers / trips, làm tròn 2 chữ số — 0 khi giờ không có chuyến |
| `hours[].isPeak` | `true` khi giờ **có chuyến**, cả kỳ **có khách**, và mật độ ≥ `averagePassengersPerTrip` |
| `peakHours` | Danh sách giờ có `isPeak = true`, tăng dần — lọc nhanh cho FE |

- **Nền số liệu là bảng `Tickets`, đếm vé `Paid`/`Used` của các chuyến KHỞI HÀNH trong kỳ.** Mật
  độ hành khách = khách đã trả tiền và lên xe, không phải số ghế hay số chuyến. Vé `Cancelled`
  bỏ (khách không lên xe); vé đã soát (`Used`) vẫn tính (người đó đã đi thật).
- **Khung giờ = giờ khởi hành của chuyến, theo giờ Việt Nam (UTC+7).** Chuyến 00:30 giờ VN thuộc
  giờ 0 của ngày HÔM ĐÓ; trục là giờ chạy chứ không phải giờ bán vé — báo cáo phục vụ điều chỉnh
  tần suất chạy xe.
- **Mật độ = khách trung bình mỗi chuyến.** Hai giờ cùng 100 khách nhưng một giờ có 20 chuyến,
  giờ kia có 4 — chỗ đông là giờ 4 chuyến. Chia cho số chuyến khiến "cao điểm" phản ánh độ chật
  của xe.
- **So sánh trên giá trị ĐÃ LÀM TRÒN 2 chữ số** — con số hiển thị chính là con số được so sánh,
  người đọc phản hồi tự kiểm lại được. Kỳ không có chuyến hoặc không có khách nào → `peakHours`
  rỗng (không đánh dấu cao điểm khi ngưỡng bằng 0).
- Khoảng `from` → `to` tối đa **366 ngày** · `to` trước `from` → **400** — lỗi gắn ở cả
  `errors.from` lẫn `errors.to` · `routeId` không tồn tại → **404** `Không tìm thấy tuyến đường`.

> **Cho task "API thống kê tỷ lệ lấp đầy theo chuyến, tuyến, khung giờ" (Kiên):** mục này đã gộp
> khách theo giờ KHỞI HÀNH; tỷ lệ lấp đầy cần thêm mẫu số **sức chứa** (ghế của xe) — đơn vị
> khác hẳn (phần trăm), đừng đổi ngữ nghĩa của `passengers` ở đây, mở endpoint riêng trên bề mặt
> `/reports` (ví dụ `GET /reports/fill-rate`).
>
> **Đề xuất tần suất:** đã có endpoint riêng — xem mục
> **"Đề xuất tần suất chạy xe — /reports/frequency-proposals"** ngay dưới. Endpoint đó đọc thẳng
> `peakHours` của mục này, không dựng lại nền tính.

## Đề xuất tần suất chạy xe — `/reports/frequency-proposals`

> Story 20 *"Tỷ lệ lấp đầy"*. ✅ Backend đã có (`FrequencyProposalController` — Phùng Duy Hoàng,
> task *"API đề xuất tăng/giảm tần suất chạy xe theo dữ liệu"*, Sprint 4 dòng 68). Mục này biến
> báo cáo khung giờ cao điểm thành **đề xuất tăng/giảm số chuyến theo từng giờ**; nó **không** thay
> đổi báo cáo gốc và **không** tự đổi lịch chạy — quản lý xem rồi tự quyết định.
>
> **Endpoint yêu cầu vai trò `Admin` hoặc `Manager`** (như báo cáo gốc). Thiếu token → 401; vai trò
> khác → 403.

### `GET /reports/frequency-proposals` — đề xuất tần suất theo dữ liệu

Query — **mọi tham số bỏ trống được**, cùng ngữ nghĩa với `/reports/peak-hours`:

| Tham số | Mặc định | Ý nghĩa |
|---|---|---|
| `from` | 29 ngày trước `to` | Ngày bắt đầu (lịch Việt Nam, `yyyy-MM-dd`), TÍNH CẢ |
| `to` | hôm nay (giờ VN) | Ngày kết thúc (lịch Việt Nam), TÍNH CẢ |
| `routeId` | — | Chỉ tính các chuyến thuộc tuyến này; bỏ trống = toàn công ty |

```json
// FrequencyProposal — ví dụ kỳ 1 ngày, toàn công ty
{
  "from": "2026-10-17",
  "to": "2026-10-17",
  "periodDays": 1,
  "hasEnoughData": true,
  "totalPassengers": 100,
  "totalTrips": 20,
  "averagePassengersPerTrip": 5,
  "increaseHours": [17],
  "decreaseHours": [3, 11, 22],
  "hours": [
    {
      "hour": 3,
      "passengers": 0,
      "trips": 2,
      "averagePassengersPerTrip": 0,
      "isPeak": false,
      "currentTripsPerDay": 2,
      "currentFrequencyMinutes": 30,
      "suggestedTripsInPeriod": 0,
      "suggestedTripsPerDay": 0,
      "suggestedFrequencyMinutes": null,
      "changePercent": -100,
      "action": "Decrease"
    },
    {
      "hour": 6,
      "passengers": 30,
      "trips": 6,
      "averagePassengersPerTrip": 5,
      "isPeak": true,
      "currentTripsPerDay": 6,
      "currentFrequencyMinutes": 10,
      "suggestedTripsInPeriod": 6,
      "suggestedTripsPerDay": 6,
      "suggestedFrequencyMinutes": 10,
      "changePercent": 0,
      "action": "Keep"
    },
    {
      "hour": 11,
      "passengers": 10,
      "trips": 4,
      "averagePassengersPerTrip": 2.5,
      "isPeak": false,
      "currentTripsPerDay": 4,
      "currentFrequencyMinutes": 15,
      "suggestedTripsInPeriod": 2,
      "suggestedTripsPerDay": 2,
      "suggestedFrequencyMinutes": 30,
      "changePercent": -50,
      "action": "Decrease"
    },
    {
      "hour": 17,
      "passengers": 60,
      "trips": 4,
      "averagePassengersPerTrip": 15,
      "isPeak": true,
      "currentTripsPerDay": 4,
      "currentFrequencyMinutes": 15,
      "suggestedTripsInPeriod": 12,
      "suggestedTripsPerDay": 12,
      "suggestedFrequencyMinutes": 5,
      "changePercent": 200,
      "action": "Increase"
    },
    {
      "hour": 22,
      "passengers": 0,
      "trips": 4,
      "averagePassengersPerTrip": 0,
      "isPeak": false,
      "currentTripsPerDay": 4,
      "currentFrequencyMinutes": 15,
      "suggestedTripsInPeriod": 0,
      "suggestedTripsPerDay": 0,
      "suggestedFrequencyMinutes": null,
      "changePercent": -100,
      "action": "Decrease"
    }
    // … đủ 24 giờ; giờ không có chuyến: action "NoService", các trường đề xuất null …
  ]
}
```

| Trường | Ý nghĩa |
|---|---|
| `from` / `to` | Khoảng ngày **đã áp mặc định**, lặp lại từ báo cáo gốc (lịch Việt Nam) |
| `periodDays` | Số ngày lịch trong kỳ, tính cả hai đầu |
| `hasEnoughData` | `false` khi kỳ không có chuyến, không có khách, hoặc mật độ trung bình làm tròn bằng 0 — khi đó **không đề xuất gì** |
| `totalPassengers` / `totalTrips` / `averagePassengersPerTrip` | Ba số gốc, **giống hệt** `/reports/peak-hours` cùng tham số |
| `increaseHours` / `decreaseHours` | Giờ có `action` tương ứng, tăng dần — lọc nhanh cho FE |
| `hours[]` | **Đủ 24 phần tử** (giờ 0–23, tăng dần), cùng trục với báo cáo gốc |
| `hours[].passengers` / `trips` / `averagePassengersPerTrip` / `isPeak` | Bốn số gốc của giờ đó, không tính lại |
| `hours[].currentTripsPerDay` | `trips / periodDays`, làm tròn 2 chữ số — tần suất hiện tại theo số chuyến/ngày |
| `hours[].currentFrequencyMinutes` | `60 / currentTripsPerDay`, làm tròn phút; **null khi < 1 chuyến/ngày** (không có giãn cách trong giờ để nói) |
| `hours[].suggestedTripsInPeriod` | Số chuyến đề xuất cho **cả kỳ** (số nguyên) — xem quy tắc bên dưới |
| `hours[].suggestedTripsPerDay` | `suggestedTripsInPeriod / periodDays`, làm tròn 2 chữ số |
| `hours[].suggestedFrequencyMinutes` | `60 / suggestedTripsPerDay`, làm tròn phút; **null khi suggestion = 0 hoặc < 1 chuyến/ngày** |
| `hours[].changePercent` | `(suggestedTripsInPeriod - trips) / trips × 100`, làm tròn 2 chữ số; `0` khi Keep; null khi NoService/NoData |
| `hours[].action` | `Increase` · `Decrease` · `Keep` · `NoService` · `NoData` |

**Quy tắc đề xuất — mục tiêu là kéo mọi giờ về mật độ trung bình của kỳ:**

- **Đọc thẳng báo cáo khung giờ cao điểm**, không truy vấn lại `Tickets`/`Trips`: cùng tham số thì
  `passengers`, `trips`, `averagePassengersPerTrip`, `isPeak` và ba số tổng **khớp từng con số** với
  `/reports/peak-hours`. Mọi thay đổi nền số liệu (ví dụ có tính vé huỷ hay không) chỉ sửa một chỗ ở
  báo cáo gốc, endpoint này tự theo.
- **Mật độ mục tiêu = `averagePassengersPerTrip` của cả kỳ.** Số chuyến đề xuất cho một giờ là
  `round(passengers / mật độ mục tiêu)`: giờ có mật độ cao hơn trung bình cần **nhiều chuyến hơn**
  để hạ tải mỗi xe; giờ thưa khách cần **ít chuyến hơn**. Không dùng một tỷ lệ tăng/giảm cố định
  (ví dụ ±20%) — tỷ lệ đó không đến từ dữ liệu và sẽ sai khác nhau ở mỗi tuyến.
- **Không mâu thuẫn với `isPeak`:** giờ cao điểm không bao giờ bị đề xuất giảm; giờ không cao điểm
  không bao giờ được đề xuất tăng. Con số làm tròn có thể rơi đúng bằng hiện tại — khi đó là `Keep`,
  không phải tăng/giảm 0%.
- **Giờ có chuyến nhưng không có khách:** mật độ 0 → đề xuất **0 chuyến** (`Decrease`, `-100%`).
  Đây là tín hiệu để quản lý cân nhắc bỏ khung giờ; API không tự bỏ.
- **Giờ không có chuyến:** `NoService`, không đề xuất — không có chuyến thì không có dữ liệu khách
  để nói nên tăng hay giảm. Muốn mở giờ mới phải dựa vào dữ liệu khác, ngoài phạm vi endpoint này.
- **Kỳ không đủ dữ liệu** (`hasEnoughData = false`): giờ có chuyến nhận `NoData`, giờ rỗng nhận
  `NoService`; mọi trường đề xuất (kể cả `changePercent`) là null. Cố ý không đề xuất giảm sạch khi
  cả kỳ chưa có khách — dữ liệu mới hoặc seeder rỗng không phải bằng chứng để cắt chuyến.
- **Làm tròn:** mọi phép chia làm tròn **2 chữ số**, `MidpointRounding.AwayFromZero`; số chuyến đề
  xuất là số nguyên. `currentTripsPerDay`/`suggestedTripsPerDay` có thể lẻ vì kỳ nhiều ngày; phút
  chỉ được quy đổi khi tần suất đạt ít nhất 1 chuyến/ngày trong giờ đó.
- **Chỉ đọc, không ghi:** không sửa chuyến, không tạo lịch, không migration. Đề xuất là con số tham
  khảo; quản lý áp dụng bằng màn hình quản lý chuyến hiện có.

**Lỗi** — giống hệt báo cáo gốc vì endpoint gọi thẳng service của báo cáo:

| Tình huống | Mã | Ghi chú |
|---|---|---|
| Không gửi token | 401 | middleware xác thực |
| Không phải Admin/Quản lý | 403 | policy `ManagerOrAbove` |
| `from` sau `to` | 400 | lỗi gắn vào **cả** `errors.from` và `errors.to` |
| Khoảng quá 366 ngày | 400 | cùng trần với báo cáo gốc |
| `routeId` không tồn tại | 404 | `Không tìm thấy tuyến đường.` |

> **Bàn giao:** frontend đọc `hours[].action` để tô màu tăng/giảm và `suggestedFrequencyMinutes`
> (null → hiển thị `—`) để gợi ý giãn cách. `NoService`/`NoData` cần trạng thái rỗng, đừng hiển thị
> 0 phút. Endpoint không có task UI riêng trong sprint — màn hình nào dùng thì nối tại chỗ.

## Theo dõi thông báo — `/subscriptions`

> Story 10 *"Thông báo trạm"*. 🟢 **Bảng `Subscriptions` — migration ĐÃ SINH, ĐÃ SOÁT** (nhánh
> `feature/8-migrate-notification-templates-subscriptions`, Vàng Thị Dăm — task *"Migrate bảng
> Notifications, NotificationTemplates, Subscriptions"*, Sprint 5 dòng 14). Chi tiết CSDL:
> `docs/37-csdl-mau-thong-bao-va-theo-doi.md` (bảng này **không** nằm trong A9 — nhóm cần bổ sung).
>
> Mục này chốt bề mặt REST của task *"API đăng ký / huỷ theo dõi thông báo theo tuyến và trạm"*
> (Sprint 5 dòng 17 — Trần Trung Hiếu): một khách đăng ký nhận thông báo khi xe sắp đến **một
> trạm**, hoặc **cả tuyến** (`stopId` bỏ trống) — cùng một hành động, chỉ khác độ chi tiết
> (docs/37 §3.1). Người tiêu thụ dòng `Subscriptions`:
> - service bắn thông báo khi xe cách trạm dưới 500m (dòng 15 — Kiên, chưa làm);
> - component bật/tắt nhận thông báo theo trạm (dòng 20 — Thịnh, chưa làm) — đọc/ghi **chính**
>   ba endpoint dưới đây;
> - service tự động đẩy sự cố đến khách theo dõi tuyến (dòng 29 — Kiên, chưa làm).

### Hình dạng `Subscription`

```json
// Một lượt theo dõi — stopId null nghĩa là theo dõi CẢ TUYẾN (mọi trạm + sự cố của tuyến)
{
  "id": "9d5f1e2c-0000-0000-0000-000000000000",
  "routeId": "3c7b2a1d-0000-0000-0000-000000000000",
  "routeCode": "08",
  "routeName": "Cầu Giấy — Bờ Hồ Hoàn Kiếm",
  "stopId": "6a1f0b2e-0000-0000-0000-000000000000",
  "stopName": "Bờ Hồ",
  "createdAt": "2026-10-16T08:00:00Z"
}
```

| Trường | Kiểu | Ghi chú |
|---|---|---|
| `id` | string (GUID) | khoá của lượt theo dõi — dùng cho `DELETE /subscriptions/{id}` |
| `routeId` | string (GUID) | tuyến được theo dõi — luôn có (mọi lượt theo dõi đều gắn một tuyến) |
| `routeCode` / `routeName` | string | chép từ bảng `Routes` để công tắc hiển thị được một mình, không phải gọi thêm |
| `stopId` | string (GUID) `\|` null | `null` = theo dõi **cả tuyến** (docs/37 §3.1) |
| `stopName` | string `\|` null | tên trạm; `null` cùng `stopId` |
| `createdAt` | string (ISO 8601 UTC) | thời điểm bấm theo dõi — sắp danh sách mới nhất trước |

- `routeCode`/`routeName`/`stopName` là **bản chép để hiển thị** — khoá nghiệp vụ vẫn là
  `routeId`/`stopId`. Khoá ngoại `Restrict` (docs/37 §3.4) bảo đảm tuyến/trạm được trỏ tới luôn
  còn, nên ba trường này không bao giờ null.
- Một khách có thể **vừa** theo dõi cả tuyến **vừa** theo dõi riêng một trạm của tuyến đó — hai
  dòng khác nhau, không xung đột. Service gửi thông báo (dòng 15) phải tự khử trùng để khách
  không nhận hai thông báo cho một lượt xe (docs/37 §7).

| Method | Endpoint | Trạng thái |
|---|---|---|
| GET | `/subscriptions` | ✅ đã làm (dòng 17) |
| POST | `/subscriptions` | ✅ đã làm (dòng 17) |
| DELETE | `/subscriptions/{id}` | ✅ đã làm (dòng 17) |

Cả ba endpoint **yêu cầu đăng nhập** (`[Authorize]` trần, **không** policy vai trò — thông báo
trạm là tính năng của hành khách, còn chuyện *"chỉ quản lý mới được"* không đúng với ai) và
**chỉ chạm lượt theo dõi của chính người gọi** — phạm vi "của tôi" nằm ngay trong truy vấn theo
`Subscriptions.UserId`, cùng lối `/refunds`, `/tickets/me`. Không có tham số nào để đọc lượt
theo dõi của người khác, và ca "của người khác" trả **404** chứ không phải 403 — không để lộ
lượt theo dõi nào đang tồn tại.

#### `GET /subscriptions` — danh sách theo dõi của tôi

```json
// 200 — mảng trần, mới nhất trước
[
  { "id": "…", "routeId": "…", "routeCode": "08", "routeName": "…",
    "stopId": "…", "stopName": "Bờ Hồ", "createdAt": "2026-10-16T08:00:00Z" },
  { "id": "…", "routeId": "…", "routeCode": "01", "routeName": "…",
    "stopId": null, "stopName": null, "createdAt": "2026-10-15T07:30:00Z" }
]
```

| Mã | Khi nào |
|---|---|
| 200 | Luôn trả về — chưa theo dõi gì thì mảng rỗng, **không** phải 404 |
| 401 | Không có token |

- **Không phân trang** — một khách có vài lượt theo dõi, phân trang là nghi thức thừa (cùng lối
  `/refunds`, `/tickets/me`).

#### `POST /subscriptions` — đăng ký theo dõi

```json
// Request — stopId bỏ trống hoặc null = theo dõi cả tuyến
{ "routeId": "3c7b2a1d-0000-0000-0000-000000000000", "stopId": "6a1f0b2e-0000-0000-0000-000000000000" }
```

```json
// 201 — trả nguyên dòng vừa tạo (hình dạng Subscription ở trên)
{ "id": "9d5f1e2c-0000-0000-0000-000000000000", "routeId": "…", "routeCode": "08",
  "routeName": "…", "stopId": "…", "stopName": "Bờ Hồ", "createdAt": "…" }
```

```json
// 400 — trạm tồn tại nhưng KHÔNG thuộc tuyến đã chọn (khoá ngoại không lo hộ — docs/37 §3.1)
{ "message": "Trạm dừng không thuộc tuyến đã chọn", "errors": { "stopId": ["Trạm dừng không thuộc tuyến đã chọn"] } }
```

| Mã | Khi nào |
|---|---|
| 201 | Đã tạo dòng theo dõi |
| 400 | Thiếu `routeId`, hoặc `stopId` không thuộc tuyến đã chọn — lỗi gắn ở `errors.stopId` |
| 401 | Không có token |
| 404 | Tuyến không tồn tại, hoặc trạm không tồn tại — hai ca **khác nhau về lời**, cùng mã |
| 409 | Đã theo dõi đúng tuyến/trạm này rồi — bấm hai lần không tạo dòng thứ hai |

- **`stopId` không thuộc tuyến → 400 chứ không phải 404**: trạm CÓ tồn tại, cái sai nằm ở dữ liệu
  gửi lên (chọn nhầm tuyến) — cùng lối `PUT /trips/{id}/location` (`currentStopId` không thuộc
  tuyến → 400 theo trường).
- **Chống trùng ở cả hai tầng**: tầng nghiệp vụ kiểm trước rồi trả 409 kèm lời rõ; tầng CSDL có
  hai chỉ mục duy nhất bộ phận (docs/37 §3.3) chặn cuộc đua thật — lượt thua nhận lỗi chỉ mục và
  **cũng được dịch thành 409**, không bao giờ nổi lên thành 500. Trên PostgreSQL `NULL` không
  bằng `NULL` nên phải là chỉ mục CÓ điều kiện, không phải UNIQUE thường.
- **"Huỷ theo dõi" là xoá dòng**, không phải lật cờ — xem `DELETE` dưới đây và docs/37 §3.2.
  Bấm theo dõi lại sau khi huỷ thì ghi dòng mới, `createdAt` mang nghĩa *"theo dõi từ khi nào"*.

#### `DELETE /subscriptions/{id}` — huỷ theo dõi

| Mã | Khi nào |
|---|---|
| 204 | Đã xoá dòng theo dõi |
| 401 | Không có token |
| 404 | Lượt theo dõi không tồn tại **hoặc** thuộc người khác — cùng một câu, không phân biệt |

- **Không có cờ `IsActive`**: tắt công tắc (dòng 20) và huỷ theo dõi (dòng 17) đều là xoá dòng —
  bảng không có cột nào sửa được, không có `UpdatedAt` (docs/37 §3.2).
- Gọi lại DELETE cùng id lần nữa → **404** (không idempotent, cố ý: 404 là câu trả lời đúng của
  "không còn gì để xoá", và nó không lộ dòng của người khác).
- Ghi nhật ký: `POST /subscriptions` và `DELETE /subscriptions/{id}` đi qua `AuditLogMiddleware`
  (POST → Create với Target là id dòng mới; DELETE → Delete với Target `Subscriptions:{id}`).

> **Cho task "API lịch sử thông báo đã nhận" (dòng 19 — Dăm):** endpoint `GET /notifications/me`
> mà `frontend/src/api/notificationApi.ts` đã draft KHÔNG thuộc mục này — bề mặt `/subscriptions`
> chỉ trả lời *"đang theo dõi gì"*, không trả lời *"đã nhận những gì"*.
