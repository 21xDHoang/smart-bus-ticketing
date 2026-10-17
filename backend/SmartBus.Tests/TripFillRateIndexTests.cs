using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using SmartBus.Api.Data;
using SmartBus.Api.Entities;

namespace SmartBus.Tests;

/// <summary>
/// Ghim chỉ mục phục vụ báo cáo tỷ lệ lấp đầy theo chuyến — US 20, task *"Tối ưu truy vấn thống kê
/// tỷ lệ lấp đầy theo chuyến"* (Vàng Thị Dăm).
///
/// Chỉ mục chỉ có tác dụng thật ở PostgreSQL; InMemory bỏ qua toàn bộ nên test qua HTTP không nói
/// được gì về chúng. Test này soi **mô hình EF** cho cấu trúc cột khoá và soi **thao tác migration**
/// cho các cột <c>INCLUDE</c> (annotation Npgsql chỉ hiện đầy đủ trên thao tác migration, không đọc
/// lại được qua <c>IIndex.GetIncludeProperties()</c> ở runtime). Ai xoá chỉ mục, đổi thứ tự cột
/// hoặc làm mất chỉ mục A6 một cột <c>TripId</c> thì đỏ ngay.
/// </summary>
public class TripFillRateIndexTests
{
    private const string MigrationId = "20261017035449_Sprint5_TripFillRateIndexes";

    [Fact]
    public void Chi_muc_quet_chuyen_theo_khoang_thoi_gian_ton_tai_va_khong_unique()
    {
        using var db = TaoDbContext();

        var trip = db.Model.FindEntityType(typeof(Trip));
        Assert.NotNull(trip);

        // Nhánh "toàn công ty": WHERE DepartureTime >= @from AND DepartureTime < @to.
        var index = Assert.Single(trip!.GetIndexes(), i =>
            i.Properties.Select(p => p.Name).SequenceEqual(new[] { nameof(Trip.DepartureTime) }));

        Assert.False(index.IsUnique);
        Assert.Null(index.GetFilter());
    }

    [Fact]
    public void Chi_muc_loc_tuyen_van_giu_dung_thu_tu_hai_cot()
    {
        using var db = TaoDbContext();

        var trip = db.Model.FindEntityType(typeof(Trip));
        Assert.NotNull(trip);

        // Nhánh "một tuyến": WHERE RouteId = @routeId AND DepartureTime trong kỳ.
        var index = Assert.Single(trip!.GetIndexes(), i =>
            i.Properties.Select(p => p.Name).SequenceEqual(new[] { nameof(Trip.RouteId), nameof(Trip.DepartureTime) }));

        Assert.False(index.IsUnique);
        Assert.Null(index.GetFilter());
    }

    [Fact]
    public void Chi_muc_dem_ve_cua_chuyen_gom_trang_thai_va_van_giu_chi_muc_A6()
    {
        using var db = TaoDbContext();

        var ticket = db.Model.FindEntityType(typeof(Ticket));
        Assert.NotNull(ticket);

        var indexes = ticket!.GetIndexes().ToArray();

        // A6 — chỉ mục một cột TripId, không điều kiện: "danh sách khách của chuyến" và khoá ngoại.
        // Vẫn phải còn vì test DDL của Giàng A Vàng ghim nguyên văn câu CREATE INDEX này.
        var motCot = Assert.Single(indexes, i =>
            i.Properties.Select(p => p.Name).SequenceEqual(new[] { nameof(Ticket.TripId) }));

        Assert.False(motCot.IsUnique);
        Assert.Null(motCot.GetFilter());

        // Đếm vé Paid/Used của từng chuyến: WHERE TripId = @t AND Status IN (...).
        var composite = Assert.Single(indexes, i =>
            i.Properties.Select(p => p.Name).SequenceEqual(new[] { nameof(Ticket.TripId), nameof(Ticket.Status) }));

        Assert.False(composite.IsUnique);
        Assert.Null(composite.GetFilter());
    }

    /// <summary>
    /// Cột <c>INCLUDE</c> là thứ biến phép quét chuyến thành index-only scan. Chúng chỉ tồn tại
    /// trong thao tác migration (Npgsql gắn annotation <c>Npgsql:IndexInclude</c>), nên ghim ở đây
    /// thay vì đọc lại mô hình runtime.
    /// </summary>
    [Fact]
    public void Migration_sinh_include_du_cot_cho_bao_cao_lap_day()
    {
        using var db = TaoDbContextMigration();

        var migrationsAssembly = db.GetService<IMigrationsAssembly>();
        Assert.Contains(MigrationId, migrationsAssembly.Migrations.Keys);

        var migration = (Migration)migrationsAssembly.CreateMigration(
            migrationsAssembly.Migrations[MigrationId], db.Database.ProviderName!);

        var operations = migration.UpOperations
            .OfType<CreateIndexOperation>()
            .ToArray();

        // Giữ nguyên chỉ mục A6 một cột TripId — bỏ nó là làm đỏ test DDL của Giàng A Vàng.
        Assert.DoesNotContain(
            migration.UpOperations.OfType<DropIndexOperation>(),
            o => o.Name == "IX_Tickets_TripId" && o.Table == "Tickets");
        Assert.DoesNotContain(
            migration.DownOperations.OfType<CreateIndexOperation>(),
            o => o.Name == "IX_Tickets_TripId");

        // Nhánh toàn công ty — quét khoảng DepartureTime, cần Id/RouteId/BusId/Status ngay trong chỉ mục.
        Assert.Equal(
            new[] { "Id", "RouteId", "BusId", "Status" },
            IncludedColumns(Assert.Single(operations, o => o.Name == "IX_Trips_DepartureTime")));

        // Nhánh một tuyến — cùng bộ cột báo cáo, trừ RouteId vốn đã là cột khoá.
        Assert.Equal(
            new[] { "Id", "BusId", "Status" },
            IncludedColumns(Assert.Single(operations, o => o.Name == "IX_Trips_RouteId_DepartureTime")));

        // Phép đếm vé theo chuyến + trạng thái.
        var demVe = Assert.Single(operations, o => o.Name == "IX_Tickets_TripId_Status");
        Assert.Equal(new[] { "TripId", "Status" }, demVe.Columns);
    }

    private static string[] IncludedColumns(CreateIndexOperation operation)
        => Assert.IsType<string[]>(operation.GetAnnotation("Npgsql:IndexInclude")?.Value);

    /// <summary>
    /// Dùng Npgsql (không kết nối) chứ không dùng InMemory: chỉ provider quan hệ mới giữ cấu hình
    /// chỉ mục của Npgsql. Chuỗi kết nối không được mở — test chỉ đọc mô hình/migration.
    ///
    /// Lớp con <see cref="NpgsqlModelContext"/> là CỐ Ý: EF cache mô hình theo kiểu context, nên
    /// nhiều test khác trong cùng tiến trình đã dựng <see cref="AppDbContext"/> bằng InMemory trước;
    /// dùng lại đúng kiểu đó sẽ nhận mô hình của InMemory. Kiểu context riêng buộc EF dựng mô hình
    /// mới với provider Npgsql.
    /// </summary>
    private static AppDbContext TaoDbContext() => new NpgsqlModelContext(
        new DbContextOptionsBuilder<AppDbContext>()
            // Context là lớp con nằm ở assembly test, còn migration nằm ở SmartBus.Api — chỉ rõ để
            // IMigrationsAssembly không tìm nhầm sang assembly của test.
            .UseNpgsql(
                "Host=khong-ket-noi;Database=chi-soi-metadata;Username=khong;Password=khong",
                npgsql => npgsql.MigrationsAssembly("SmartBus.Api"))
            .Options);

    /// <summary>
    /// Context GỐC (không phải lớp con) cho test migration: assembly chứa context trùng với assembly
    /// chứa migration nên EF tự tìm thấy, không phụ thuộc cấu hình <c>MigrationsAssembly</c>.
    /// </summary>
    private static AppDbContext TaoDbContextMigration() => new(
        new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=khong-ket-noi;Database=chi-soi-metadata;Username=khong;Password=khong")
            .Options);

    private sealed class NpgsqlModelContext(DbContextOptions<AppDbContext> options) : AppDbContext(options);
}
