using Microsoft.EntityFrameworkCore;
using SmartBus.Api.Data;
using SmartBus.Api.Entities;
using SmartBus.Api.Seed;
using SmartBus.Api.Services;

namespace SmartBus.Tests;

/// <summary>
/// Test cho bộ seed CSDL chung (chạy qua <c>backend/SmartBus.Seed</c> → <see cref="SampleDataSeeder"/>).
///
/// Chạy trên InMemory nên nhánh TRUNCATE của <c>--reset</c> (SQL của PostgreSQL) không chạy ở đây —
/// thứ được ghim là hành vi nghiệp vụ, đúng những gì cả nhóm phụ thuộc:
///   - 4 tài khoản mỗi vai trò một cái, mật khẩu băm bằng đúng PasswordService của API (đăng nhập được);
///   - phản ánh mẫu của hành khách: đủ ba trạng thái, có phản hồi của quản lý, một phản ánh không gắn chuyến;
///   - chạy lại không nhân bản dữ liệu (seed là idempotent — cả nhóm chạy chung một CSDL);
///   - --reset dọn sạch dữ liệu nghiệp vụ rồi dựng lại dữ liệu nền.
/// </summary>
public class SampleDataSeederTests
{
    /// <summary>Mật khẩu giả của test — mật khẩu seed thật lấy ở chat nhóm, không nằm trong repo.</summary>
    private const string MatKhau = "mat-khau-seed-test-123";

    private static AppDbContext MoDb() => new(
        new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"smartbus-seed-{Guid.NewGuid()}")
            .Options);

    [Fact]
    public async Task Seed_tao_bon_tai_khoan_moi_vai_tro_mot_cai_dang_nhap_duoc()
    {
        await using var db = MoDb();

        var ketQua = await SampleDataSeeder.RunAsync(db, reset: false, MatKhau);

        Assert.Equal(4, ketQua.AccountsCreated);
        Assert.Equal(0, ketQua.AccountsSkipped);
        Assert.True(ketQua.TripsCreated > 0);

        var taiKhoan = await db.Users.Include(u => u.UserRoles).ToListAsync();

        Assert.Equal(4, taiKhoan.Count);
        Assert.Equal(
            new[] { "0900000001", "0900000002", "0900000003", "0900000004" },
            taiKhoan.Select(u => u.PhoneNumber).OrderBy(p => p).ToArray());
        // Thứ tự sắp xếp theo giá trị Guid của 4 RoleIds: 1111 (Admin) → 2222 (Manager) →
        // 3333 (Driver) → 4444 (Passenger).
        Assert.Equal(
            new[] { RoleIds.Admin, RoleIds.Manager, RoleIds.Driver, RoleIds.Passenger },
            taiKhoan.Select(u => u.RoleId).OrderBy(id => id).ToArray());

        foreach (var user in taiKhoan)
        {
            // Băm bằng chính PasswordService của API — tài khoản seed phải đăng nhập được y hệt
            // tài khoản đăng ký qua API, không có đường băm riêng nào để lệch.
            Assert.True(PasswordService.Verify(MatKhau, user.PasswordHash));
            Assert.True(user.IsActive);

            // RBAC đọc cả bảng nối UserRoles — thiếu dòng này thì vai trò chỉ có trên giấy.
            Assert.Contains(user.UserRoles, ur => ur.RoleId == user.RoleId);
        }

        // Chuyến sinh ra đều nằm trong CSDL — đếm khớp với kết quả báo về.
        Assert.Equal(ketQua.TripsCreated, await db.Trips.CountAsync());
    }

    [Fact]
    public async Task Seed_tao_ba_phan_anh_mau_cho_hanh_khach()
    {
        await using var db = MoDb();

        var ketQua = await SampleDataSeeder.RunAsync(db, reset: false, MatKhau);

        Assert.Equal(3, ketQua.FeedbacksCreated);

        var hanhKhach = await db.Users.SingleAsync(u => u.PhoneNumber == "0900000004");
        var phanAnh = await db.Feedbacks
            .Where(f => f.UserId == hanhKhach.Id)
            .Include(f => f.Replies)
            .ToListAsync();

        Assert.Equal(3, phanAnh.Count);

        // Đủ ba trạng thái và đủ ba loại — màn "Phản ánh của tôi" có dữ liệu cho mọi nhánh lọc.
        Assert.Equal(
            new[] { FeedbackStatus.New, FeedbackStatus.InProgress, FeedbackStatus.Resolved },
            phanAnh.Select(f => f.Status).OrderBy(s => s).ToArray());
        Assert.Equal(
            new[] { FeedbackType.Complaint, FeedbackType.Compliment, FeedbackType.Suggestion },
            phanAnh.Select(f => f.Type).OrderBy(t => t).ToArray());

        // Một phản ánh KHÔNG gắn chuyến (nhánh "Không kèm chuyến"), hai phản ánh gắn chuyến mẫu
        // (chuyến đã seed nên tripId phải trỏ vào chuyến có thật).
        Assert.Single(phanAnh, f => f.TripId is null);
        Assert.Equal(2, phanAnh.Count(f => f.TripId is not null));

        // Phản hồi của quản lý mẫu: 2 + 1 + 0, và đều thuộc tài khoản quản lý.
        var quanLy = await db.Users.SingleAsync(u => u.PhoneNumber == "0900000002");
        Assert.Equal(3, phanAnh.Sum(f => f.Replies.Count));
        Assert.All(
            phanAnh.SelectMany(f => f.Replies),
            reply => Assert.Equal(quanLy.Id, reply.UserId));

        // Chỉ đổi khi trạng thái đổi: phản ánh New chưa ai chạm thì updatedAt phải trống.
        Assert.Null(phanAnh.Single(f => f.Status == FeedbackStatus.New).UpdatedAt);
        Assert.All(
            phanAnh.Where(f => f.Status != FeedbackStatus.New),
            f => Assert.NotNull(f.UpdatedAt));
    }

    [Fact]
    public async Task Chay_lai_lan_hai_khong_nhan_ban_du_lieu()
    {
        await using var db = MoDb();

        await SampleDataSeeder.RunAsync(db, reset: false, MatKhau);
        var soLuongTruoc = await DemAsync(db);

        var ketQua = await SampleDataSeeder.RunAsync(db, reset: false, MatKhau);

        Assert.Equal(0, ketQua.AccountsCreated);
        Assert.Equal(0, ketQua.StopsCreated);
        Assert.Equal(0, ketQua.RoutesCreated);
        Assert.Equal(0, ketQua.BusesCreated);
        Assert.Equal(0, ketQua.TripsCreated);
        Assert.Equal(0, ketQua.FeedbacksCreated);
        Assert.Equal(4, ketQua.AccountsSkipped);

        Assert.Equal(soLuongTruoc, await DemAsync(db));
    }

    [Fact]
    public async Task Reset_xoa_du_lieu_nghiep_vu_roi_seed_lai_tu_dau()
    {
        await using var db = MoDb();

        await SampleDataSeeder.RunAsync(db, reset: false, MatKhau);

        // Dữ liệu "của người khác" — đúng thứ mà --reset phải dọn sạch.
        db.Stops.Add(new Stop { Name = "Trạm tự thêm", Address = "Đâu đó", Latitude = 0, Longitude = 0 });
        await db.SaveChangesAsync();

        var ketQua = await SampleDataSeeder.RunAsync(db, reset: true, MatKhau);

        Assert.True(ketQua.ResetPerformed);
        Assert.Equal(4, ketQua.AccountsCreated);
        Assert.Equal(7, ketQua.StopsCreated);
        Assert.Equal(2, ketQua.RoutesCreated);
        Assert.Equal(3, ketQua.BusesCreated);
        Assert.Equal(3, ketQua.FeedbacksCreated);

        Assert.False(await db.Stops.AnyAsync(s => s.Name == "Trạm tự thêm"));
        Assert.Equal(7, await db.Stops.CountAsync());
        Assert.Equal(4, await db.Users.CountAsync());
        Assert.Equal(3, await db.Feedbacks.CountAsync());
    }

    [Fact]
    public async Task Mat_khau_ngan_hon_8_ky_tu_bi_tu_choi()
    {
        await using var db = MoDb();

        await Assert.ThrowsAsync<ArgumentException>(
            () => SampleDataSeeder.RunAsync(db, reset: false, "ngan"));
    }

    private static async Task<(int Users, int Stops, int Routes, int Buses, int Trips, int Feedbacks)>
        DemAsync(AppDbContext db)
        => (await db.Users.CountAsync(),
            await db.Stops.CountAsync(),
            await db.Routes.CountAsync(),
            await db.Buses.CountAsync(),
            await db.Trips.CountAsync(),
            await db.Feedbacks.CountAsync());
}
