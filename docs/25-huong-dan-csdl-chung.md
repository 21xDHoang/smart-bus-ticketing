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

   Mở file vừa tạo, dán chuỗi vào `ConnectionStrings:Default`. **Đừng tự sửa** Host/Port/Username —
   chuỗi đó là session pooler của Supabase (chuỗi "direct" chỉ chạy được trên mạng IPv6).
3. Chạy:

   ```bash
   dotnet restore
   dotnet ef database update --project backend/SmartBus.Api   # áp migration còn thiếu, chạy lại vô hại
   dotnet run --project backend/SmartBus.Api
   ```

   API ở `http://localhost:5080`, Swagger ở `/swagger`.

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
Suối Tiên), 3 xe, và các chuyến của hôm nay + 2 ngày kế tiếp. Khi API chạy, một job nền tự nhân
chuyến thêm cho các ngày sau — cứ để nó chạy.

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
