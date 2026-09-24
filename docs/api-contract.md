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

> ⏳ Backend đang làm (Trần Trung Hiếu). Frontend (Băng) dùng **dữ liệu giả** cho tới khi
> endpoint có thật — bật/tắt qua `USE_MOCK_DATA` trong `frontend/src/api/stopApi.ts`.

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

## Đang chờ bổ sung (nhóm khác)

- `/routes` — CRUD tuyến đường (Hiếu).
- `/routes/{id}/stops` — gán trạm vào tuyến + sắp xếp thứ tự (Kiên).
- `/admin/users` — quản trị người dùng (Kiên / Hạnh / Thịnh).
