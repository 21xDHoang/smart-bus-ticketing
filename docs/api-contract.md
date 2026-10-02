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
  `/auth/login` và `/auth/register`.
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
| GET | `/trips/search` | Tìm chuyến cho hành khách (**công khai**): giờ chạy, giá vé, số ghế còn trống | — | `TripSearchResult[]` |
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
> **Đây là endpoint CÔNG KHAI duy nhất của mục `/trips`** — gọi không cần đăng nhập. Story 1 mở đầu
> bằng *"Là hành khách, tôi muốn tìm kiếm tuyến xe…"*: tra cứu chuyến là việc trước khi đăng nhập,
> đăng nhập là bước của màn hình đặt vé. Mọi endpoint còn lại của mục này (kể cả `GET /trips` ngay
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
> những tuyến nào?"*, endpoint này trả lời *"tuyến X trong khoảng ngày → những chuyến nào, giá bao
> nhiêu, còn mấy ghế?"*. Màn hình kết quả (Băng, đã xong) ghép hai bước lại. Làm gộp cả hai vào một
> endpoint thì task của Hiếu không còn gì để làm — và ngược lại; mỗi bên giữ đúng phần mình.

> **Vì sao đứng ở controller riêng (`TripSearchController`)?** Cùng lối `TripLookupController` ở
> trên: mỗi bề mặt một controller, hai task thuộc hai người. Route template khác nhau
> (`api/trips/search` so với `api/trips` và `api/trips/{id:guid}`) nên không tranh chấp — đoạn
> literal `search` không thể khớp `{id:guid}`.

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
khớp với số dòng `Seat` của xe, và API tạo dàn ghế sẽ đến ở sprint làm story 2 (chọn vị trí ghế).

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

```json
// Ví dụ Trip
{
  "id": "6b3e8d12-0000-0000-0000-000000000000",
  "routeId": "3f2a1b0c-0000-0000-0000-000000000000",
  "busId": "1c9a4f05-0000-0000-0000-000000000000",
  "busLicensePlate": "29B-123.45",
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

#### Giờ gửi lên phải kèm múi giờ

Mọi trường thời gian trong body (`departureTime`, `arrivalTime`, `startTime`, `endTime`) nhận
ISO 8601 **có kèm múi giờ** — ví dụ `2026-10-01T05:00:00+07:00` hoặc `...Z`. Server quy về UTC khi
lưu (cột `timestamptz` — quy ước A3) và mọi trường thời gian trả về đều là UTC. Chuỗi không kèm
múi giờ bị hiểu là giờ máy chủ — frontend luôn gửi kèm offset để không phụ thuộc vào máy chủ.

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
| `action` | `string` | Một trong 6 mã bên dưới |
| `target` | `string \| null` | Đối tượng bị tác động, dạng `<Tên bảng>:<Id>` — ví dụ `"Routes:3f2a1b0c-…"`. `null` với đăng nhập / đăng xuất |
| `ipAddress` | `string \| null` | Địa chỉ IP của người gọi. `null` khi không lấy được |
| `createdAt` | `string` (ISO 8601, UTC) | Thời điểm hành động xảy ra |

Mã `action` hợp lệ — đúng **6** giá trị, khớp enum `AuditAction` của backend:

| Mã | Nghĩa |
|---|---|
| `Login` | Đăng nhập thành công |
| `Logout` | Đăng xuất |
| `LoginFailed` | Đăng nhập thất bại |
| `Create` | Tạo bản ghi mới |
| `Update` | Sửa bản ghi đã có |
| `Delete` | Xoá bản ghi |

Middleware suy `Create` / `Update` / `Delete` từ HTTP verb, nên màn hình mới không cần thêm mã —
chi tiết nằm ở `target`.

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
