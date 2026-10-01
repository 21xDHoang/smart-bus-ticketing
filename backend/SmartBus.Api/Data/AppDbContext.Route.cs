using Microsoft.EntityFrameworkCore;
using SmartBus.Api.Entities;

// Web SDK tự thêm implicit using Microsoft.AspNetCore.Routing, trong đó có lớp Route —
// trùng tên với entity Route của mình. Alias để mọi chỗ trong file này trỏ về entity.
// Ai viết Controller cho tuyến đường cũng sẽ gặp lại chuyện này.
using Route = SmartBus.Api.Entities.Route;

namespace SmartBus.Api.Data;

/// <summary>
/// Phần DbContext của nhóm nghiệp vụ Tuyến đường (Routes, Stops, RouteStops, Fares).
/// Vàng Thị Dăm sửa file này — chủ CSDL của nhóm.
/// </summary>
public partial class AppDbContext
{
    public DbSet<Route> Routes => Set<Route>();
    public DbSet<Stop> Stops => Set<Stop>();
    public DbSet<RouteStop> RouteStops => Set<RouteStop>();
    public DbSet<Fare> Fares => Set<Fare>();

    partial void ConfigureRoute(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Route>(e =>
        {
            e.Property(r => r.Code).HasMaxLength(20).IsRequired();
            e.Property(r => r.Name).HasMaxLength(200).IsRequired();
            e.Property(r => r.Origin).HasMaxLength(200).IsRequired();
            e.Property(r => r.Destination).HasMaxLength(200).IsRequired();

            // Quãng đường numeric(6,2) — tối đa 9999.99 km. Không dùng float/double (A3).
            e.Property(r => r.DistanceKm).HasPrecision(6, 2);

            // Trạng thái lưu dạng chuỗi, không phải số nguyên (A3).
            e.Property(r => r.Status).HasConversion<string>().HasMaxLength(20).IsRequired();

            // Mã tuyến là khoá nghiệp vụ: hai tuyến trùng mã là dữ liệu sai, chặn ở tầng CSDL.
            e.HasIndex(r => r.Code).IsUnique();
        });

        modelBuilder.Entity<Stop>(e =>
        {
            e.Property(s => s.Name).HasMaxLength(200).IsRequired();
            e.Property(s => s.Address).HasMaxLength(300).IsRequired();
        });

        modelBuilder.Entity<RouteStop>(e =>
        {
            // Khai báo tường minh, nếu không EF sinh ra numeric trần (không giới hạn
            // precision/scale) — cùng đơn vị km với Route.DistanceKm.
            e.Property(rs => rs.DistanceKm).HasPrecision(6, 2);

            // Một trạm không xuất hiện hai lần trên cùng một tuyến (A6).
            // Chỉ mục này cũng phục vụ luôn truy vấn "các trạm của tuyến" theo RouteId —
            // RouteId là cột đầu của chỉ mục nên không cần thêm chỉ mục riêng cho nó.
            e.HasIndex(rs => new { rs.RouteId, rs.StopId }).IsUnique();

            // ── Chỉ mục cho truy vấn "tìm tuyến theo điểm đi - điểm đến" (US 1) ──
            //
            // Câu hỏi cần trả lời: tuyến nào chứa CẢ trạm đi lẫn trạm đến, và trạm đi đứng
            // TRƯỚC trạm đến trên tuyến. Dạng câu lệnh là self-join ngay trên bảng nối:
            //
            //   FROM "RouteStops" a
            //   JOIN "RouteStops" b ON b."RouteId" = a."RouteId" AND b."StopId" = @to
            //   JOIN "Routes"      r ON r."Id"      = a."RouteId"
            //   WHERE a."StopId" = @from AND a."StopOrder" < b."StopOrder"
            //
            // Chỉ mục này BAO PHỦ nhánh tra theo trạm đi: Postgres đọc được cả RouteId lẫn
            // StopOrder ngay trong chỉ mục, không phải nhảy về heap lấy từng dòng. Chỉ mục FK
            // lẻ trên StopId mà EF tự sinh không làm được — nó chỉ có một cột.
            //
            // KHÔNG unique: một trạm nằm trên nhiều tuyến là bình thường; ràng buộc "một trạm
            // không lặp trên CÙNG một tuyến" đã do chỉ mục (RouteId, StopId) ở trên lo.
            //
            // Khai báo chỉ mục này cũng làm EF thôi sinh chỉ mục FK riêng cho StopId (quy ước
            // ForeignKeyIndexConvention: đã có chỉ mục lấy StopId làm tiền tố thì không sinh
            // thêm) — migration đi kèm sẽ DROP IX_RouteStops_StopId. Cố ý: giữ lại thành chỉ
            // mục thừa, vừa tốn chỗ vừa làm chậm mọi INSERT vào bảng nối.
            e.HasIndex(rs => new { rs.StopId, rs.RouteId, rs.StopOrder });

            // Chỉ mục cho "các trạm của tuyến, theo đúng thứ tự chạy" và cho MAX(StopOrder)
            // của tuyến (RouteStopService.OrderedByRouteQuery và NextStopOrderAsync — chạy mỗi
            // lần gán thêm một trạm). Chỉ mục (RouteId, StopId) lọc được theo RouteId nhưng
            // không sắp được theo StopOrder nên vẫn phải sort; có chỉ mục này thì
            // MAX(StopOrder) chỉ còn là một lần seek vào đầu chỉ mục.
            //
            // KHÔNG unique, dù đọc lên thấy "thứ tự trạm phải duy nhất": RouteStopService ghi
            // rõ hai request POST chạy song song có thể cùng đọc max(StopOrder) rồi cùng ghi
            // max+1, nên hai dòng cùng thứ tự LÀ chuyện có thật. Đặt unique ở đây sẽ biến cuộc
            // đua đó thành DbUpdateException không ai bắt — tức lỗi 500 cho người dùng.
            e.HasIndex(rs => new { rs.RouteId, rs.StopOrder });

            // Bảng nối thật sự thuộc cha — xoá tuyến thì các dòng RouteStops đi theo (A5).
            e.HasOne(rs => rs.Route)
             .WithMany(r => r.RouteStops)
             .HasForeignKey(rs => rs.RouteId)
             .OnDelete(DeleteBehavior.Cascade);

            // Chiều còn lại là FK nghiệp vụ: xoá một trạm đang nằm trên tuyến phải bị chặn,
            // không được âm thầm rút trạm khỏi mọi tuyến (A5).
            e.HasOne(rs => rs.Stop)
             .WithMany(s => s.RouteStops)
             .HasForeignKey(rs => rs.StopId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Fare>(e =>
        {
            e.Property(f => f.PassengerType).HasConversion<string>().HasMaxLength(20).IsRequired();
            e.Property(f => f.Price).HasPrecision(12, 2);

            // Mỗi tuyến + đối tượng chỉ có một giá (A6).
            e.HasIndex(f => new { f.RouteId, f.PassengerType }).IsUnique();

            // Không nằm trong danh sách Cascade của A5 nên theo mặc định: Restrict.
            e.HasOne(f => f.Route)
             .WithMany(r => r.Fares)
             .HasForeignKey(f => f.RouteId)
             .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
