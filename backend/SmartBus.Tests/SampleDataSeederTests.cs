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
///   - sơ đồ ghế + dàn ghế của từng xe mẫu: số ghế khớp Buses.Capacity, VIP khớp chuỗi vị trí của sơ đồ;
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
    public async Task Seed_sinh_du_ghe_cho_tung_xe_mau_theo_so_do_cua_loai_xe()
    {
        await using var db = MoDb();

        var ketQua = await SampleDataSeeder.RunAsync(db, reset: false, MatKhau);

        // Hai loại xe trong đội xe mẫu, mỗi loại đúng MỘT sơ đồ (A6 — BusType unique):
        // 45 chỗ một tầng (9×5) và 2 tầng 60 chỗ (2×6×5). Ba xe ⇒ 45 + 45 + 60 = 150 ghế.
        Assert.Equal(2, ketQua.SeatLayoutsCreated);
        Assert.Equal(150, ketQua.SeatsCreated);

        var soDo = await db.SeatLayouts.ToListAsync();
        var ghe = await db.Seats.ToListAsync();

        Assert.Equal(2, soDo.Count);
        Assert.Equal(150, ghe.Count);

        // Bất biến của sơ đồ: lưới không khuyết ô ⇒ TotalSeats = số tầng × số hàng × số cột.
        Assert.All(soDo, l =>
            Assert.Equal(l.NumberOfFloors * l.RowsPerFloor * l.ColumnsPerRow, l.TotalSeats));

        foreach (var bus in await db.Buses.ToListAsync())
        {
            var soDoCuaLoai = soDo.Single(l => l.BusType == bus.BusType);
            var gheCuaXe = ghe.Where(s => s.BusId == bus.Id).ToList();

            // Ràng buộc đã ghi ở Buses.Capacity: sức chứa phải khớp số dòng Seat của chính xe đó.
            Assert.Equal(bus.Capacity, gheCuaXe.Count);
            Assert.Equal(soDoCuaLoai.TotalSeats, gheCuaXe.Count);
            Assert.All(gheCuaXe, s => Assert.Equal(soDoCuaLoai.Id, s.SeatLayoutId));

            // Mọi toạ độ đều nằm trong lưới, và mỗi ô của lưới có đúng một ghế.
            Assert.All(gheCuaXe, s => Assert.InRange(s.Floor, 1, soDoCuaLoai.NumberOfFloors));
            Assert.All(gheCuaXe, s => Assert.InRange(s.RowIndex, 1, soDoCuaLoai.RowsPerFloor));
            Assert.All(gheCuaXe, s => Assert.InRange(s.ColumnIndex, 1, soDoCuaLoai.ColumnsPerRow));
            Assert.Equal(
                gheCuaXe.Count,
                gheCuaXe.Select(s => (s.Floor, s.RowIndex, s.ColumnIndex)).Distinct().Count());

            // Mã ghế không trùng trong cùng một xe (A6) — xe hai tầng là ca dễ trùng nhất.
            Assert.Equal(gheCuaXe.Count, gheCuaXe.Select(s => s.SeatNumber).Distinct().Count());
        }
    }

    [Fact]
    public async Task Ghe_VIP_khop_dung_chuoi_vi_tri_cua_so_do()
    {
        await using var db = MoDb();

        await SampleDataSeeder.RunAsync(db, reset: false, MatKhau);

        var ghe = await db.Seats.ToListAsync();

        // Xe 45 chỗ một tầng: VIP là hai hàng đầu, hai cột trái — "1-1-1;1-1-2;1-2-1;1-2-2".
        var xe45 = await db.Buses.FirstAsync(b => b.BusType == "Xe buýt 45 chỗ");
        var vip45 = ghe.Where(s => s.BusId == xe45.Id && s.SeatType == SeatType.Vip).ToList();

        Assert.Equal(4, vip45.Count);
        Assert.Equal(
            new[] { "1-1-1", "1-1-2", "1-2-1", "1-2-2" },
            vip45.Select(s => $"{s.Floor}-{s.RowIndex}-{s.ColumnIndex}").OrderBy(v => v).ToArray());

        // Xe hai tầng: VIP chỉ nằm ở tầng 1 (trọn hàng đầu) — tầng trên không có ghế VIP nào.
        var xeHaiTang = await db.Buses.FirstAsync(b => b.BusType == "Xe buýt 2 tầng 60 chỗ");
        var gheHaiTang = ghe.Where(s => s.BusId == xeHaiTang.Id).ToList();
        var vipHaiTang = gheHaiTang.Where(s => s.SeatType == SeatType.Vip).ToList();

        Assert.Equal(5, vipHaiTang.Count);
        Assert.All(vipHaiTang, s => Assert.Equal(1, s.Floor));
        Assert.All(vipHaiTang, s => Assert.Equal(1, s.RowIndex));

        // Xe hai tầng phải có tiền tố tầng trong mã ghế, không thì ghế tầng 1 và tầng 2 trùng mã.
        Assert.Equal(30, gheHaiTang.Count(s => s.SeatNumber.StartsWith("T1-", StringComparison.Ordinal)));
        Assert.Equal(30, gheHaiTang.Count(s => s.SeatNumber.StartsWith("T2-", StringComparison.Ordinal)));
        Assert.Contains(gheHaiTang, s => s.SeatNumber == "T2-A1");

        // Xe một tầng giữ mã ghế trần — đúng mã màn chọn ghế đang dựng ("A1" là hàng đầu, cột trái).
        Assert.Contains(ghe, s => s.BusId == xe45.Id && s.SeatNumber == "A1");
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
        Assert.Equal(0, ketQua.SeatLayoutsCreated);
        Assert.Equal(0, ketQua.SeatsCreated);
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
        Assert.Equal(2, ketQua.SeatLayoutsCreated);
        Assert.Equal(150, ketQua.SeatsCreated);
        Assert.Equal(3, ketQua.FeedbacksCreated);

        Assert.False(await db.Stops.AnyAsync(s => s.Name == "Trạm tự thêm"));
        Assert.Equal(7, await db.Stops.CountAsync());
        Assert.Equal(4, await db.Users.CountAsync());
        Assert.Equal(3, await db.Feedbacks.CountAsync());
        Assert.Equal(2, await db.SeatLayouts.CountAsync());
        Assert.Equal(150, await db.Seats.CountAsync());
    }

    [Fact]
    public async Task Mat_khau_ngan_hon_8_ky_tu_bi_tu_choi()
    {
        await using var db = MoDb();

        await Assert.ThrowsAsync<ArgumentException>(
            () => SampleDataSeeder.RunAsync(db, reset: false, "ngan"));
    }

    private static async Task<(int Users, int Stops, int Routes, int Buses, int SeatLayouts, int Seats, int Trips, int Feedbacks)>
        DemAsync(AppDbContext db)
        => (await db.Users.CountAsync(),
            await db.Stops.CountAsync(),
            await db.Routes.CountAsync(),
            await db.Buses.CountAsync(),
            await db.SeatLayouts.CountAsync(),
            await db.Seats.CountAsync(),
            await db.Trips.CountAsync(),
            await db.Feedbacks.CountAsync());
}
