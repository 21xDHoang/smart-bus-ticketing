# CSDL dùng chung của nhóm — hướng dẫn thành viên (Supabase)

> Từ 03/10/2026 cả nhóm dùng **chung một CSDL PostgreSQL trên Supabase** thay vì mỗi máy một bản
> riêng. Ai cũng thấy cùng dữ liệu — hết cảnh "máy tôi chạy được mà máy bạn không".
>
> Chuỗi kết nối và mật khẩu seed **không nằm trong repo** (repo public) — lấy ở **tin nhắn ghim
> trong chat nhóm**. Đừng dán chúng vào bất kỳ file nào được commit.

## 1. Cài đặt một lần

1. Mở chat nhóm, copy **nguyên** chuỗi kết nối trong tin nhắn ghim.
2. Tạo file cấu hình từ file mẫu:

   ```bash
   cd backend
   cp SmartBus.Api/appsettings.Development.json.example SmartBus.Api/appsettings.Development.json
   ```

   **Cách nhanh hơn** — chuỗi kết nối đang trong clipboard, chạy:

   ```bash
   bash scripts/setup-csdl.sh
   ```

   Script tự tạo file từ mẫu và điền chuỗi vào (không in chuỗi ra màn hình; chạy lại thì bỏ qua).
   Ai dùng PowerShell: `powershell -ExecutionPolicy Bypass -File scripts\setup-csdl.ps1`.

   Mở file vừa tạo, dán chuỗi vào `ConnectionStrings:Default`. **Đừng tự sửa** Host/Port/Username —
   chuỗi đó là session pooler của Supabase (chuỗi "direct" chỉ chạy được trên mạng IPv6).
3. Chạy:

   ```bash
   dotnet restore
   dotnet ef database update --project backend/SmartBus.Api   # áp migration còn thiếu, chạy lại vô hại
   dotnet run --project backend/SmartBus.Api
   ```

   API ở `http://localhost:5080`, đặc tả OpenAPI ở `/openapi/v1.json`.

Không cần cài PostgreSQL trên máy.

## 2. Tài khoản mẫu để đăng nhập

| Số điện thoại | Vai trò |
|---|---|
| `0900000001` | Admin |
| `0900000002` | Manager |
| `0900000003` | Driver |
| `0900000004` | Passenger |

Cả 4 tài khoản dùng chung **một mật khẩu** — mật khẩu seed trong chat nhóm.

## 3. Dữ liệu nền

CSDL chung đã được nạp sẵn: 7 trạm ở TP.HCM, 2 tuyến (`01` Bến Thành — Chợ Lớn, `02` Bến Thành —
Suối Tiên), 3 xe, các chuyến của hôm nay + 2 ngày kế tiếp, và 3 phản ánh mẫu của tài khoản hành
khách (đủ 3 trạng thái, có phản hồi của quản lý — màn "Phản ánh của tôi" mở lên là có dữ liệu).
Khi API chạy, một job nền tự nhân chuyến thêm cho các ngày sau — cứ để nó chạy.

(Chưa có bảng vé/ghế — Sprint 3 mới làm — nên `seatsRemaining` trong kết quả tìm chuyến luôn bằng
sức chứa. Đúng thiết kế hiện tại, không phải lỗi.)

Cần nạp lại (CSDL trống, lỡ xoá dữ liệu…):

```bash
dotnet run --project backend/SmartBus.Seed -- --password "<mật khẩu seed>"
```

Tool đọc đúng file cấu hình mà API đang dùng (không phải khai chuỗi kết nối hai nơi) và chạy được
nhiều lần — thứ gì có rồi thì bỏ qua, không nhân bản.

⚠️ **`--reset` xoá TOÀN BỘ dữ liệu nghiệp vụ** rồi nạp lại từ đầu — dữ liệu người khác đang thử
cũng mất. Chỉ chạy khi cả nhóm thống nhất, và **báo nhóm trước**:

```bash
dotnet run --project backend/SmartBus.Seed -- --password "<mật khẩu seed>" --reset
```

## 4. Đổi cấu trúc bảng — quy trình vẫn như cũ

**Chỉ Vàng Thị Dăm được tạo migration** (`dotnet ef migrations add`) — lý do ở README mục "Git — 5
quy tắc bắt buộc". CSDL chung không đổi quy tắc đó, chỉ đổi chỗ áp dụng:

1. Ai cần bảng/cột mới → nhắn Dăm (hoặc sửa file `AppDbContext.<NghiệpVụ>.cs` của mình rồi nhờ Dăm
   sinh migration, như các sprint trước).
2. Dăm tạo migration trên nhánh riêng → PR → merge vào main.
3. Sau khi merge, áp lên CSDL chung — ai cũng chạy được:

   ```bash
   dotnet ef database update --project backend/SmartBus.Api
   ```

   Lệnh chỉ áp những migration CSDL chưa có; chạy lại vô hại.
4. Cả nhóm `git pull` main cho schema trong code khớp.

> Ai đang viết migration **chưa merge** thì đừng trỏ vào CSDL chung (bảng còn đang đổi) — trỏ tạm
> về PostgreSQL local trên máy mình; kẹt thì hỏi Hoàng. Trường hợp này hiếm, bình thường cứ dùng
> Supabase.

## 5. Lỗi hay gặp

| Hiện tượng | Nguyên nhân | Cách sửa |
|---|---|---|
| `relation "…" does not exist` | Schema trên CSDL chung mới hơn code của bạn | `git pull` main rồi `dotnet ef database update --project backend/SmartBus.Api` |
| `password authentication failed` (28P01) | Chuỗi kết nối dán sai hoặc thiếu | Copy lại nguyên chuỗi trong chat nhóm — đừng gõ tay |
| Kết nối treo / timeout | Đang dùng chuỗi direct (chỉ IPv6) | Dùng đúng chuỗi trong chat nhóm — đó là session pooler |
| Seed báo "đã có sẵn bỏ qua 4" | Tài khoản đã tồn tại từ lần seed trước | Bình thường — seed là idempotent |
| Màn "Tra cứu tuyến" báo không đủ quyền (403) | Màn này đọc `GET /routes` — chỉ Manager/Admin | Đăng nhập `0900000001`/`0900000002`; tìm chuyến công khai nằm ở màn "Kết quả tìm kiếm" |
| API thoát ngay khi khởi động, in hướng dẫn cấu hình CSDL | Máy chưa có `appsettings.Development.json` (hoặc còn nguyên chỗ dán mẫu) | Làm theo hướng dẫn in ra — nhanh nhất: `bash scripts/setup-csdl.sh` (mục 1) |

## 6. Chạy demo toàn bộ tính năng đã có

Chạy ngay trên máy bạn, dữ liệu là CSDL chung — **hai terminal**:

```bash
# Terminal 1 — API (để nguyên suốt buổi demo; job nền tự nhân chuyến cho ngày sau)
dotnet run --project backend/SmartBus.Api
```

```bash
# Terminal 2 — giao diện
cd frontend
npm install     # chỉ lần đầu
npm run dev
```

Mở `http://localhost:5173` (Vite in địa chỉ chính xác ở terminal 2). Giao diện gọi API ở
`http://localhost:5080/api`; chỉ khi API chạy địa chỉ khác mới cần tạo `frontend/.env.local` với
dòng `VITE_API_URL=<địa chỉ API>`.

Một lượt demo gợi ý, đi theo thứ tự:

1. **Đăng nhập `0900000002` (Manager)**
   - **Tra cứu tuyến**: điểm đi "Bến Thành", điểm đến "Chợ Lớn", chọn ngày hôm nay → **Tìm tuyến**.
     ⚠️ Màn này đọc danh sách tuyến từ `GET /routes` nên **cần Manager/Admin** cho tới khi có API
     tra cứu công khai (task của Hiếu).
   - Bấm **Xem chuyến** trên một tuyến → màn **Kết quả tìm kiếm**: chuyến thật của ngày đã chọn
     (giá, ghế còn trống, loại xe), sắp xếp theo giờ/giá. Bước này gọi `GET /trips/search` công
     khai — hành khách cũng xem được.
   - Các màn quản trị: **Tuyến đường · Trạm dừng · Đội xe · Gán trạm vào tuyến · Cấu hình giá vé ·
     Chuyến theo ngày · Lịch trình · Tần suất chạy xe · Phân công điều xe**.
2. **Đăng xuất, đăng nhập `0900000004` (Passenger)**
   - **Phản ánh của tôi**: 3 phản ánh mẫu đủ 3 trạng thái — mở rộng một dòng để xem luồng phản hồi
     của nhà xe (dòng "Khiếu nại · Đã xử lý" có 2 phản hồi).
   - **Gửi phản ánh**, **Vé tháng**: giao diện chạy được nhưng hai nút gửi còn dùng **dữ liệu giả** —
     backend chưa có `POST /feedbacks` và `POST /monthly-passes` (task của Hiếu).
3. **Đăng xuất, đăng nhập `0900000001` (Admin)** — thêm hai màn riêng của Admin: **Người dùng**,
   **Nhật ký**.

Màn nào gọi API thật, màn nào còn giả:

| Màn hình | Nguồn dữ liệu |
|---|---|
| Phản ánh của tôi (kèm luồng phản hồi) | **API thật** — `GET /feedbacks/me`, `GET /feedbacks/me/{id}` |
| Kết quả tìm kiếm chuyến | **API thật** — `GET /trips/search` (công khai) |
| Các màn quản trị (tuyến, trạm, xe, giá vé, chuyến, người dùng, nhật ký) | **API thật** |
| Tra cứu tuyến | **API thật nhưng cần Manager/Admin** (chờ API tra cứu công khai của Hiếu) |
| Gửi phản ánh (nút Gửi) | Dữ liệu giả — chờ `POST /feedbacks` |
| Vé tháng (đăng ký) | Dữ liệu giả — chờ `POST /monthly-passes` |
