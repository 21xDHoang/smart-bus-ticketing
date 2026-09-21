# Kiến trúc & quy ước code

## Sơ đồ tầng

```
React (frontend/)  ──HTTP/JSON──▶  SmartBus.Api (backend/)
                                        │
                                        ├─ Controllers/   nhận request, trả response
                                        ├─ Services/      nghiệp vụ
                                        ├─ Data/          AppDbContext + cấu hình bảng
                                        └─ Entities/      lớp ánh xạ bảng CSDL
                                        │
                                        ▼
                                   PostgreSQL (Supabase)
```

## Quy ước đặt tên

| Loại | Quy ước | Ví dụ |
|---|---|---|
| Controller | `<Danh từ>Controller` | `RoutesController`, `AuthController` |
| Service | `I<Danh từ>Service` + `<Danh từ>Service` | `ITicketService`, `TicketService` |
| Entity | Danh từ số ít, PascalCase | `Route`, `Stop`, `Ticket` |
| DTO request | `<Hành động>Request` | `CreateRouteRequest` |
| DTO response | `<Danh từ>Response` | `RouteResponse` |
| Route API | danh từ số nhiều, kebab-case | `GET /api/routes`, `POST /api/routes/{id}/stops` |

## ⚠️ File dùng chung — đọc kỹ trước khi sửa

Đây là nguồn conflict lớn nhất khi 8 người cùng commit. Mỗi file dưới đây **chỉ một người được sửa**:

| File | Người sở hữu | Ai cần đổi thì làm gì |
|---|---|---|
| `SmartBus.Api.csproj` | Phùng Duy Hoàng | Nhắn Hoàng thêm package |
| `Program.cs` | Phùng Duy Hoàng | Nhắn Hoàng đăng ký service |
| `Migrations/*` + `ModelSnapshot.cs` | Vàng Thị Dăm | **Không ai được tự chạy `dotnet ef migrations add`** |
| `frontend/package.json` | Nguyễn Đình Băng | Nhắn Băng cài thêm thư viện |
| `frontend/src/main.tsx`, `App.tsx` | Nguyễn Đình Băng | Nhắn Băng thêm route |

## Chia `AppDbContext` theo nghiệp vụ

Thay vì tất cả cùng sửa một file `AppDbContext.cs` (conflict mỗi lần commit), tách thành nhiều file `partial`:

```csharp
// Data/AppDbContext.cs — chỉ Vàng Thị Dăm sửa
public partial class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        ConfigureAuth(modelBuilder);     // AppDbContext.Auth.cs
        ConfigureRoute(modelBuilder);    // AppDbContext.Route.cs
        ConfigureTicket(modelBuilder);   // AppDbContext.Ticket.cs
    }

    partial void ConfigureAuth(ModelBuilder modelBuilder);
    partial void ConfigureRoute(ModelBuilder modelBuilder);
    partial void ConfigureTicket(ModelBuilder modelBuilder);
}
```

```csharp
// Data/AppDbContext.Auth.cs — Trần Trung Hiếu sửa file này
public partial class AppDbContext
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();

    partial void ConfigureAuth(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(e =>
        {
            e.HasIndex(u => u.PhoneNumber).IsUnique();
            e.Property(u => u.PasswordHash).IsRequired();
        });
    }
}
```

Nhờ vậy mỗi người chỉ chạm file của mình, gần như không bao giờ conflict.

## Thứ tự phụ thuộc giữa các phần

```
Migration (Dăm)  ──▶  API (Hiếu / Kiên / Hoàng)  ──▶  UI nối API thật (Băng / Hạnh / Thịnh)
                                  │
                     Frontend vẫn làm song song bằng DỮ LIỆU GIẢ
```

**Frontend không được ngồi chờ backend.** Dựng màn hình với dữ liệu giả trước, API xong chỉ đổi URL.

## Xử lý lỗi — trả về thống nhất

Mọi lỗi API trả cùng một cấu trúc để frontend xử lý một lần:

```json
{
  "message": "Số điện thoại đã được đăng ký",
  "errors": { "phoneNumber": ["Số điện thoại đã được đăng ký"] }
}
```

| Mã | Khi nào |
|---|---|
| 400 | Dữ liệu đầu vào sai |
| 401 | Chưa đăng nhập / token hết hạn |
| 403 | Đã đăng nhập nhưng không đủ quyền |
| 404 | Không tìm thấy |
| 409 | Xung đột (trùng ghế, trùng vé tháng) |
