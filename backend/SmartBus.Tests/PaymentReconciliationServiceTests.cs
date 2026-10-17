using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SmartBus.Api.Data;
using SmartBus.Api.Dtos.Payments;
using SmartBus.Api.Entities;
using SmartBus.Api.Services;
using RouteEntity = SmartBus.Api.Entities.Route;

namespace SmartBus.Tests;

/// <summary>
/// Test cho <see cref="PaymentReconciliationService"/> — lưới thứ hai khi IPN lọt mất (US 6, task
/// *"đối soát tự động"* — Trần Trung Hiếu). Hai client cổng là giả có hẹn trước câu trả lời; cửa
/// chốt (<see cref="PaymentSettlementService"/>) và bước phát hành vé là THẬT — lời hứa "hỏi cổng →
/// chốt → phát hành vé" chạy xuyên tầng, không dựng tay kết quả nào.
///
/// Mốc "bây giờ" đi qua tham số — không đồng hồ thật, không chờ 5 phút.
/// </summary>
public class PaymentReconciliationServiceTests
{
    private static readonly DateTime BayGio = new(2026, 10, 16, 8, 0, 0, DateTimeKind.Utc);

    private const string KhoaQr = "khoa-ky-ma-qr-cho-test-dai-hon-32-ky-tu";

    private readonly string _tenCsdl = $"doi-soat-{Guid.NewGuid():N}";

    // ------------------------------------------------------------------

    [Fact]
    public async Task MoMo_tra_thanh_cong_thi_chot_Success_va_phat_hanh_ve()
    {
        using var db = TaoDb();
        var (payment, _) = await SeedAsync(db, "MoMo", BayGio.AddMinutes(-6));
        var momo = new FakeMoMo { TraLoi = new MoMoQueryTransactionResult { Success = true, TransId = 2757953842, Amount = 175_000, ResultCode = 0, Message = "Success" } };

        var service = TaoService(db, momo: momo);

        var xuLy = await service.SweepOnceAsync(BayGio);

        Assert.Equal(1, xuLy);
        Assert.Equal("63e61ad2318d66f22ecad8f7", momo.HoiMaDon);   // hỏi đúng orderId = paymentCode

        using var dbDoc = TaoDb();
        var sau = await dbDoc.Payments.AsNoTracking().SingleAsync(p => p.Id == payment.Id);
        Assert.Equal(PaymentStatus.Success, sau.Status);
        Assert.Equal("2757953842", sau.GatewayTransactionId);
        Assert.Equal(2, await dbDoc.Tickets.CountAsync(t => t.UserId == payment.UserId));
    }

    [Fact]
    public async Task MoMo_tra_that_bai_thi_chot_Failed_kem_loi_cong()
    {
        using var db = TaoDb();
        var (payment, _) = await SeedAsync(db, "MoMo", BayGio.AddMinutes(-6));
        var momo = new FakeMoMo { TraLoi = new MoMoQueryTransactionResult { Success = false, ResultCode = 1001, Message = "Giao dịch không tồn tại" } };

        await TaoService(db, momo: momo).SweepOnceAsync(BayGio);

        using var dbDoc = TaoDb();
        var sau = await dbDoc.Payments.AsNoTracking().SingleAsync(p => p.Id == payment.Id);
        Assert.Equal(PaymentStatus.Failed, sau.Status);
        Assert.Contains("không tồn tại", sau.Message);
        Assert.Equal(0, await dbDoc.Tickets.CountAsync());   // thất bại không phát hành vé
    }

    [Fact]
    public async Task ZaloPay_dang_xu_ly_thi_bo_qua_khong_doan()
    {
        using var db = TaoDb();
        var (payment, _) = await SeedAsync(db, "ZaloPay", BayGio.AddMinutes(-6));
        var zaloPay = new FakeZaloPay { TraLoi = new ZaloPayQueryResult { Success = false, IsProcessing = true, ReturnCode = 3, Message = "Đang xử lý" } };

        await TaoService(db, zaloPay: zaloPay).SweepOnceAsync(BayGio);

        using var dbDoc = TaoDb();
        Assert.Equal(PaymentStatus.Pending, (await dbDoc.Payments.AsNoTracking().SingleAsync(p => p.Id == payment.Id)).Status);
    }

    [Fact]
    public async Task Chua_qua_5_phut_thi_khong_hoi_cong()
    {
        using var db = TaoDb();
        await SeedAsync(db, "MoMo", BayGio.AddMinutes(-4));
        var momo = new FakeMoMo();

        await TaoService(db, momo: momo).SweepOnceAsync(BayGio);

        Assert.Equal(0, momo.SoLanHoi);
    }

    [Fact]
    public async Task VNPay_khong_co_API_hoi_trang_thai_thi_bo_qua()
    {
        using var db = TaoDb();
        await SeedAsync(db, "VNPay", BayGio.AddMinutes(-6));
        var momo = new FakeMoMo();

        var xuLy = await TaoService(db, momo: momo).SweepOnceAsync(BayGio);

        Assert.Equal(0, xuLy);
        Assert.Equal(0, momo.SoLanHoi);
    }

    [Fact]
    public async Task Cong_khong_tra_loi_duoc_thi_bo_qua_luot_quay_khong_keo_sap()
    {
        using var db = TaoDb();
        var (payment, _) = await SeedAsync(db, "MoMo", BayGio.AddMinutes(-6));
        var momo = new FakeMoMo { NemRa = new HttpRequestException("mạng chết") };

        var xuLy = await TaoService(db, momo: momo).SweepOnceAsync(BayGio);

        Assert.Equal(0, xuLy);

        using var dbDoc = TaoDb();
        Assert.Equal(PaymentStatus.Pending, (await dbDoc.Payments.AsNoTracking().SingleAsync(p => p.Id == payment.Id)).Status);
    }

    // ------------------------------------------------------------------

    private AppDbContext TaoDb() => new(
        new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_tenCsdl).Options);

    private PaymentReconciliationService TaoService(
        AppDbContext db,
        FakeMoMo? momo = null,
        FakeZaloPay? zaloPay = null)
    {
        var issuer = new PaymentTicketIssuer(
            db,
            new TicketQrService(Options.Create(new TicketQrOptions { SigningKey = KhoaQr })));

        return new PaymentReconciliationService(
            db,
            momo ?? new FakeMoMo(),
            zaloPay ?? new FakeZaloPay(),
            new PaymentSettlementService(db),
            issuer,
            NullLogger<PaymentReconciliationService>.Instance);
    }

    private static async Task<(Payment Payment, List<Seat> Seats)> SeedAsync(
        AppDbContext db,
        string methodCode,
        DateTime createdAt)
    {
        var route = new RouteEntity { Code = "08", Name = "Tuyến 08", Origin = "Bến Thành", Destination = "Chợ Lớn" };
        var bus = new Bus { LicensePlate = "51B-12345", BusType = "Xe 45 chỗ", Capacity = 45 };
        var trip = new Trip { RouteId = route.Id, BusId = bus.Id, DepartureTime = DateTime.UtcNow.AddHours(2) };
        var seats = new List<Seat>
        {
            new() { BusId = bus.Id, SeatNumber = "A1", Floor = 1, RowIndex = 1, ColumnIndex = 1, SeatType = SeatType.Standard },
            new() { BusId = bus.Id, SeatNumber = "A2", Floor = 1, RowIndex = 1, ColumnIndex = 2, SeatType = SeatType.Standard },
        };
        var payment = new Payment
        {
            PaymentCode = methodCode == "ZaloPay" ? "PM-8f3a2c1d" : "63e61ad2318d66f22ecad8f7",
            MethodCode = methodCode,
            Amount = 175_000m,
            Status = PaymentStatus.Pending,
            UserId = Guid.NewGuid(),
            TripId = trip.Id,
            SeatNumbers = "A1;A2",
            CreatedAt = createdAt,
        };

        db.Routes.Add(route);
        db.Buses.Add(bus);
        db.Trips.Add(trip);
        db.Seats.AddRange(seats);
        db.Payments.Add(payment);
        await db.SaveChangesAsync();

        return (payment, seats);
    }

    private sealed class FakeMoMo : IMoMoGatewayService
    {
        public MoMoQueryTransactionResult? TraLoi { get; set; }

        public Exception? NemRa { get; set; }

        public int SoLanHoi { get; private set; }

        public string? HoiMaDon { get; private set; }

        public Task<MoMoQueryTransactionResult> QueryTransactionAsync(
            string orderId,
            CancellationToken cancellationToken = default)
        {
            SoLanHoi++;
            HoiMaDon = orderId;
            return NemRa is not null
                ? Task.FromException<MoMoQueryTransactionResult>(NemRa)
                : Task.FromResult(TraLoi ?? new MoMoQueryTransactionResult());
        }

        // Các hàm không thuộc task này — không bao giờ được gọi trong file này.
        public Task<MoMoCreatePaymentResult> CreatePaymentAsync(MoMoCreatePaymentRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public bool IsValidCallback(MoMoCallback callback) => throw new NotSupportedException();
    }

    private sealed class FakeZaloPay : IZaloPayGatewayService
    {
        public ZaloPayQueryResult? TraLoi { get; set; }

        public Task<ZaloPayQueryResult> QueryOrderAsync(string appTransId, CancellationToken cancellationToken = default)
            => Task.FromResult(TraLoi ?? new ZaloPayQueryResult());

        public Task<ZaloPayCreateOrderResult> CreateOrderAsync(ZaloPayCreateOrderRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public bool IsValidCallback(ZaloPayCallback callback) => throw new NotSupportedException();

        public ZaloPayCallbackData? ParseCallbackData(string data) => throw new NotSupportedException();
    }
}
