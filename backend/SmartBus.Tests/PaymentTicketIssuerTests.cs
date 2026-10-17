using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SmartBus.Api.Data;
using SmartBus.Api.Entities;
using SmartBus.Api.Services;
using RouteEntity = SmartBus.Api.Entities.Route;
using UserEntity = SmartBus.Api.Entities.User;

namespace SmartBus.Tests;

/// <summary>
/// Test cho <see cref="PaymentTicketIssuer"/> — phát hành vé khi tiền về (US 4/6, task *"API phát
/// hành vé điện tử sau khi thanh toán thành công"* — Trần Trung Hiếu).
///
/// Trọng tâm là chốt idempotency hai lớp: gọi lại thì không phát hành thêm; lượt phát hành đứt
/// giữa đường (vé đã ghi mà Payment.TicketId chưa ghi) thì lượt sau VÁ TicketId thay vì sinh vé
/// thứ hai. Provider InMemory không cưỡng chế chỉ mục unique nên ca "đâm chỉ mục" được dựng bằng
/// tay: gieo sẵn vé Paid rồi gọi issuer.
/// </summary>
public class PaymentTicketIssuerTests
{
    private const string KhoaQr = "khoa-ky-ma-qr-cho-test-dai-hon-32-ky-tu";

    private readonly string _tenCsdl = $"phat-hanh-ve-{Guid.NewGuid():N}";

    // ------------------------------------------------------------------

    [Fact]
    public async Task Giao_dich_Success_chua_phat_hanh_thi_tao_ve_chot_giu_cho_va_ghi_TicketId()
    {
        using var db = TaoDb();
        var (payment, seats) = await SeedAsync(db, PaymentStatus.Success);
        await SeedHoldAsync(db, payment.UserId, payment.TripId, seats[0].Id);

        var phátHành = await TaoIssuer(db).IssueAsync(payment.PaymentCode);

        Assert.True(phátHành);

        using var dbDoc = TaoDb();
        var ve = await dbDoc.Tickets.AsNoTracking().Where(t => t.UserId == payment.UserId).ToListAsync();
        Assert.Equal(2, ve.Count);
        Assert.All(ve, t => Assert.Equal(TicketStatus.Paid, t.Status));
        Assert.All(ve, t => Assert.StartsWith("SBT1:", t.Code));
        Assert.All(ve, t => Assert.Equal(24_000m, t.Price));

        var sau = await dbDoc.Payments.AsNoTracking().SingleAsync(p => p.Id == payment.Id);
        Assert.Contains(ve, t => t.Id == sau.TicketId);   // danh sách vé không hứa thứ tự — chỉ cần trỏ đúng một vé

        var hold = await dbDoc.SeatHolds.AsNoTracking().SingleAsync(h => h.SeatId == seats[0].Id);
        Assert.Equal(SeatHoldStatus.Confirmed, hold.Status);
        Assert.Single(await dbDoc.SeatHoldLogs.Where(l => l.SeatHoldId == hold.Id && l.Action == SeatHoldLogAction.Confirmed).ToListAsync());
    }

    [Fact]
    public async Task Da_phat_hanh_roi_thi_goi_lai_khong_lam_gi()
    {
        using var db = TaoDb();
        var (payment, _) = await SeedAsync(db, PaymentStatus.Success);
        var issuer = TaoIssuer(db);

        Assert.True(await issuer.IssueAsync(payment.PaymentCode));
        Assert.False(await issuer.IssueAsync(payment.PaymentCode));

        using var dbDoc = TaoDb();
        Assert.Equal(2, await dbDoc.Tickets.CountAsync(t => t.UserId == payment.UserId));
    }

    [Fact]
    public async Task Giao_dich_chua_Success_thi_khong_phat_hanh()
    {
        using var db = TaoDb();
        var (payment, _) = await SeedAsync(db, PaymentStatus.Pending);

        Assert.False(await TaoIssuer(db).IssueAsync(payment.PaymentCode));

        using var dbDoc = TaoDb();
        Assert.Equal(0, await dbDoc.Tickets.CountAsync());
    }

    [Fact]
    public async Task Phat_hanh_truoc_dut_giua_duong_thi_luot_sau_va_TicketId_khong_sinh_ve_moi()
    {
        // Dựng ca "đứt giữa đường": vé Paid đã tồn tại (lượt trước ghi được vé, hỏng trước khi ghi
        // TicketId) — lượt gọi sau phải đâm chỉ mục unique (TripId, SeatId) rồi VÁ TicketId thay vì
        // sinh vé thứ hai. Chạy trên SQLite vì nhánh này đứng trên chỉ mục unique THẬT mà InMemory
        // không cưỡng chế (cùng lối RefundClaimConcurrencyTests Phần B).
        var tepCsdl = Path.Combine(Path.GetTempPath(), $"smartbus-phat-hanh-ve-{Guid.NewGuid():N}.db");
        var chuoiKetNoi = $"Data Source={tepCsdl};Default Timeout=30";

        try
        {
            using var db = new AppDbContext(
                new DbContextOptionsBuilder<AppDbContext>().UseSqlite(chuoiKetNoi).Options);
            await db.Database.EnsureCreatedAsync();

            var (payment, seats) = await SeedAsync(db, PaymentStatus.Success, ghiPaymentBangSql: true);

            using (var dbGieo = new AppDbContext(
                new DbContextOptionsBuilder<AppDbContext>().UseSqlite(chuoiKetNoi).Options))
            {
                foreach (var seat in seats)
                {
                    dbGieo.Tickets.Add(new Ticket
                    {
                        TripId = payment.TripId,
                        SeatId = seat.Id,
                        UserId = payment.UserId,
                        Price = 24_000m,
                        Status = TicketStatus.Paid,
                        Code = $"SBT1:{Guid.NewGuid()}",
                    });
                }

                await dbGieo.SaveChangesAsync();
            }

            Assert.True(await TaoIssuer(db).IssueAsync(payment.PaymentCode));

            using var dbDoc = new AppDbContext(
                new DbContextOptionsBuilder<AppDbContext>().UseSqlite(chuoiKetNoi).Options);
            Assert.Equal(2, await dbDoc.Tickets.CountAsync(t => t.UserId == payment.UserId));   // không có vé thứ ba
            Assert.NotNull((await dbDoc.Payments.AsNoTracking().SingleAsync(p => p.Id == payment.Id)).TicketId);
        }
        finally
        {
            // Pool kết nối SQLite còn giữ file mở sau khi context đã dispose — đóng pool trước khi xoá.
            SqliteConnection.ClearAllPools();

            if (File.Exists(tepCsdl))
            {
                File.Delete(tepCsdl);
            }
        }
    }

    // ------------------------------------------------------------------

    private AppDbContext TaoDb() => new(
        new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_tenCsdl).Options);

    private static PaymentTicketIssuer TaoIssuer(AppDbContext db) => new(
        db,
        new TicketQrService(Options.Create(new TicketQrOptions { SigningKey = KhoaQr })));

    private static async Task<(Payment Payment, List<Seat> Seats)> SeedAsync(
        AppDbContext db,
        PaymentStatus status,
        bool ghiPaymentBangSql = false)
    {
        var route = new RouteEntity { Code = "08", Name = "Tuyến 08", Origin = "Bến Thành", Destination = "Chợ Lớn" };
        var bus = new Bus { LicensePlate = "51B-12345", BusType = "Xe 45 chỗ", Capacity = 45 };
        var layout = new SeatLayout { BusType = bus.BusType, NumberOfFloors = 1, RowsPerFloor = 10, ColumnsPerRow = 4, TotalSeats = 40 };
        var trip = new Trip { RouteId = route.Id, BusId = bus.Id, DepartureTime = DateTime.UtcNow.AddHours(2) };
        var seats = new List<Seat>
        {
            new() { BusId = bus.Id, SeatNumber = "A1", Floor = 1, RowIndex = 1, ColumnIndex = 1, SeatType = SeatType.Standard, SeatLayoutId = layout.Id },
            new() { BusId = bus.Id, SeatNumber = "A2", Floor = 1, RowIndex = 1, ColumnIndex = 2, SeatType = SeatType.Standard, SeatLayoutId = layout.Id },
        };

        // Khách sở hữu giao dịch — cần cả bản ghi Role vì SQLite của ca "đứt giữa đường" cưỡng chế
        // khoá ngoại thật (InMemory thì bỏ qua, nhưng seed đủ cho cả hai).
        var role = new Role { Id = RoleIds.Passenger, Code = RoleCodes.Passenger, Name = RoleCodes.Passenger };
        if (!db.Roles.Any(r => r.Id == role.Id || r.Code == role.Code))
        {
            db.Roles.Add(role);
        }

        var user = new UserEntity
        {
            Id = Guid.NewGuid(),
            PhoneNumber = "09" + Random.Shared.Next(10_000_000, 99_999_999),
            FullName = "Hành Khách Test",
            PasswordHash = PasswordService.HashPassword("matkhau123"),
            IsActive = true,
            RoleId = role.Id,
            UserRoles = [new UserRole { RoleId = role.Id }],
        };

        var payment = new Payment
        {
            PaymentCode = "PM-8f3a2c1d",
            MethodCode = "MoMo",
            Amount = 48_000m,
            Status = status,
            UserId = user.Id,
            TripId = trip.Id,
            SeatNumbers = "A1;A2",
            CreatedAt = DateTime.UtcNow.AddMinutes(-10),
        };

        db.Routes.Add(route);
        db.Buses.Add(bus);
        db.SeatLayouts.Add(layout);
        db.Trips.Add(trip);
        db.Seats.AddRange(seats);
        db.Users.Add(user);

        if (!ghiPaymentBangSql)
        {
            db.Payments.Add(payment);
        }

        await db.SaveChangesAsync();

        if (ghiPaymentBangSql)
        {
            // xmin là cột HỆ THỐNG của PostgreSQL, tự tăng mỗi lượt ghi — SQLite không có, và EF
            // không gửi giá trị đặt tay vào cột IsRowVersion (ValueGenerated.OnAddOrUpdate). Ca
            // "đứt giữa đường" chạy trên SQLite nên gieo dòng Payments bằng SQL thô kèm xmin = 1
            // (đúng lối RefundClaimConcurrencyTests Phần B).
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "Payments"
                    ("Id", "PaymentCode", "MethodCode", "Amount", "Status", "UserId",
                     "TripId", "SeatNumbers", "CreatedAt", "xmin")
                VALUES
                    ({payment.Id}, {payment.PaymentCode}, {"MoMo"}, {payment.Amount}, {status.ToString()},
                     {payment.UserId}, {payment.TripId}, {"A1;A2"}, {payment.CreatedAt}, 1)
                """);
        }

        return (payment, seats);
    }

    private static async Task SeedHoldAsync(AppDbContext db, Guid userId, Guid tripId, Guid seatId)
    {
        db.SeatHolds.Add(new SeatHold
        {
            TripId = tripId,
            SeatId = seatId,
            UserId = userId,
            SessionCode = $"SS-{Guid.NewGuid():N}",
            Status = SeatHoldStatus.Holding,
            ExpiresAt = DateTime.UtcNow.AddMinutes(10),
        });
        await db.SaveChangesAsync();
    }
}
