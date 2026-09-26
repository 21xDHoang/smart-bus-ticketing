# Hợp đồng API — Smart Bus Ticketing

> Đây là NGUỒN CHÍNH THỨC cho mọi endpoint. Cả frontend và backend đều đọc file này
> trước khi code — không ai tự bịa tên API (xem Quy tắc vàng trong Hướng dẫn thành viên).

## Quy ước chung

- Base URL (dev): `http://localhost:5080/api` — frontend đọc qua `VITE_API_URL`.
- Định dạng: JSON, mọi tên thuộc tính dùng **camelCase** (do .NET `System.Text.Json`
  với `PropertyNamingPolicy = CamelCase`).
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

## Đang chờ bổ sung (nhóm khác)

- `/routes/{id}/stops` — gán trạm vào tuyến + sắp xếp thứ tự (Kiên).
