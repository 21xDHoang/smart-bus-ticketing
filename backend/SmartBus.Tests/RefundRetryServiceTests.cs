using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SmartBus.Api.Data;
using SmartBus.Api.Dtos.Payments;
using SmartBus.Api.Dtos.Refunds;
using SmartBus.Api.Entities;
using SmartBus.Api.Services;

namespace SmartBus.Tests;

/// <summary>
/// Test một lượt quét của <see cref="RefundRetryService"/> (task *"Xử lý hoàn tiền thất bại + retry
/// theo lịch"*, US 8): chọn đúng khoản tới hạn, gọi qua scope riêng của từng khoản, và trả về số
/// LƯỢT GỌI CỔNG (không phải số khoản tới hạn, không phải số khoản thành công).
///
/// File này gọi thẳng <c>SweepAsync</c>, KHÔNG dựng host — vòng lặp <c>PeriodicTimer</c> của
/// <see cref="RefundRetryBackgroundService"/> không có gì để hẹn giờ trong test (cùng lối
/// <see cref="SeatHoldExpiryServiceTests"/>: test ruột, không test nhịp).
///
/// ⚠️ InMemory KHÔNG cưỡng chế xmin / unique index / khoá ngoại. Vì vậy ở đây "thua cuộc đua" được
/// dàn dựng bằng một lượt ghi chen trước (xem <see cref="AttemptServiceChenNguoc"/>) — ra đúng KẾT
/// CỤC của lượt thua (bỏ qua, không gọi cổng, không ghi gì), nhưng không chạm vào cơ chế
/// <c>DbUpdateConcurrencyException</c> thật. Cơ chế đó sống ở <see cref="RefundClaimConcurrencyTests"/>.
/// </summary>
public class RefundRetryServiceTests
{
    private const decimal SoTien = 175_000m;

    private static readonly DateTime BayGio = new(2026, 10, 14, 8, 0, 0, DateTimeKind.Utc);

    private readonly string _tenCsdl = $"hoan-tien-quet-{Guid.NewGuid():N}";

    // ------------------------------------------------------------------
    // Chọn khoản
    // ------------------------------------------------------------------

    [Fact]
    public async Task Khong_co_khoan_nao_toi_han_thi_khong_goi_cong()
    {
        var gateway = new FakeRefundGatewayCaller();
        using var db = TaoDb();
        await SeedRefundAsync(db, "RF-B", RefundStatus.Failed,
            nextRetryAt: BayGio.AddMinutes(10), failReason: "Cổng bận");

        var soLuotGoi = await TaoSweep(gateway).SweepAsync(BayGio);

        Assert.Equal(0, soLuotGoi);
        Assert.Equal(0, gateway.SoLanGoi);
    }

    [Fact]
    public async Task Chi_goi_cong_cho_khoan_Failed_toi_han()
    {
        // Bốn trạng thái cùng nằm trong bảng — lượt quét chỉ được chạm đúng một dòng. Điều kiện quét
        // là (Status = Failed ∧ NextRetryAt ≠ null ∧ NextRetryAt ≤ now); lọt bất kỳ vế nào thì hoặc
        // khoản Pending bị gọi cổng giữa lúc lượt khác đang chạy, hoặc khoản đã xong bị hoàn lần hai.
        var gateway = new FakeRefundGatewayCaller();
        using var db = TaoDb();
        var khoanA = await SeedRefundAsync(db, "RF-A", RefundStatus.Failed,
            nextRetryAt: BayGio.AddMinutes(-1), failReason: "Cổng bận");
        var khoanB = await SeedRefundAsync(db, "RF-B", RefundStatus.Failed,
            nextRetryAt: BayGio.AddMinutes(10), failReason: "Cổng bận");
        var khoanC = await SeedRefundAsync(db, "RF-C", RefundStatus.Pending);
        var khoanD = await SeedRefundAsync(db, "RF-D", RefundStatus.Succeeded);   // đã xong từ trước

        var soLuotGoi = await TaoSweep(gateway).SweepAsync(BayGio);

        Assert.Equal(1, soLuotGoi);
        Assert.Equal("RF-A", Assert.Single(gateway.YeuCau).RefundCode);

        var sauA = await DocLaiAsync(khoanA.Id);
        Assert.Equal(RefundStatus.Succeeded, sauA.Status);
        Assert.Null(sauA.NextRetryAt);

        var sauB = await DocLaiAsync(khoanB.Id);
        Assert.Equal(RefundStatus.Failed, sauB.Status);
        Assert.Equal(BayGio.AddMinutes(10), sauB.NextRetryAt);   // lịch còn nguyên
        Assert.Null(sauB.UpdatedAt);

        Assert.Equal(RefundStatus.Pending, (await DocLaiAsync(khoanC.Id)).Status);
        Assert.Equal(RefundStatus.Succeeded, (await DocLaiAsync(khoanD.Id)).Status);
    }

    [Fact]
    public async Task Khoan_cham_lau_nhat_duoc_goi_truoc()
    {
        // OrderBy NextRetryAt — hàng đợi dài thì khoản chờ lâu nhất được gọi trước, và thứ tự này
        // cố định nên log lượt quét đọc lại được. Bỏ OrderBy là thứ tự rơi vào tay nhà cung cấp CSDL.
        var gateway = new FakeRefundGatewayCaller();
        using var db = TaoDb();
        await SeedRefundAsync(db, "RF-CU", RefundStatus.Failed,
            nextRetryAt: BayGio.AddMinutes(-5), failReason: "Cổng bận");
        await SeedRefundAsync(db, "RF-MOI", RefundStatus.Failed,
            nextRetryAt: BayGio.AddMinutes(-1), failReason: "Cổng bận");

        var soLuotGoi = await TaoSweep(gateway).SweepAsync(BayGio);

        Assert.Equal(2, soLuotGoi);
        Assert.Equal(new[] { "RF-CU", "RF-MOI" }, gateway.YeuCau.Select(c => c.RefundCode));
    }

    [Fact]
    public async Task Khoan_bi_luot_khac_gianh_truoc_thi_bo_qua_khong_goi_cong()
    {
        // Cửa sổ đua thật: quét đọc ID lúc T, gọi từng khoản lúc T+ε — giữa hai mốc đó quản trị có
        // thể vừa bấm thử tay xong một khoản. Lượt quét phải nhận ra và bỏ qua, không gọi cổng lần
        // hai cho cùng một khoản (bất biến §4.3: mỗi khoản một lượt gọi tại một thời điểm).
        var gateway = new FakeRefundGatewayCaller();
        using var db = TaoDb();
        var khoanA = await SeedRefundAsync(db, "RF-A", RefundStatus.Failed,
            nextRetryAt: BayGio.AddMinutes(-2), failReason: "Cổng bận");
        await SeedRefundAsync(db, "RF-B", RefundStatus.Failed,
            nextRetryAt: BayGio.AddMinutes(-1), failReason: "Cổng bận");

        var soLuotGoi = await TaoSweep(gateway, refundBiChen: khoanA.Id).SweepAsync(BayGio);

        // A đã bị giành trước → không tính là lượt gọi; chỉ B ra ngoài.
        Assert.Equal(1, soLuotGoi);
        Assert.Equal("RF-B", Assert.Single(gateway.YeuCau).RefundCode);

        Assert.Equal(RefundStatus.Succeeded, (await DocLaiAsync(khoanA.Id)).Status);   // dấu vết lượt chen
    }

    // ------------------------------------------------------------------
    // Mốc thời gian + giá trị trả về
    // ------------------------------------------------------------------

    [Fact]
    public async Task Moc_thoi_gian_di_xuyen_suot_luot_quet()
    {
        // Cả lượt quét dùng ĐÚNG một mốc `now` do người gọi truyền: mốc hẹn kế tiếp tính từ nó, dòng
        // nhật ký mang nó. Nếu trong hàm có chỗ nào lỡ gọi DateTime.UtcNow, mốc hẹn sẽ rơi về quanh
        // "bây giờ thật" (15/10) và lệch hẳn mốc ghim (14/10) — test đỏ ngay.
        var gateway = new FakeRefundGatewayCaller
        {
            KetQua = new() { Result = RefundLogResult.Failed, Message = "Cổng bận" },
        };
        using var db = TaoDb();
        var khoan = await SeedRefundAsync(db, "RF-A", RefundStatus.Failed,
            nextRetryAt: BayGio.AddMinutes(-1), failReason: "Cổng bận");

        var soLuotGoi = await TaoSweep(gateway).SweepAsync(BayGio);

        Assert.Equal(1, soLuotGoi);

        var sau = await DocLaiAsync(khoan.Id);
        // Nấc của lượt THỬ LẠI thứ nhất (k = 1) → +30 phút. Nấc +5 phút không phải của lượt này: nó
        // là mốc do lượt Initial hỏng tự đặt (k = 0 — xem RefundAttemptServiceTests); lượt quét đã là
        // ScheduledRetry nên tiêu nấc 1. Ghim đúng con số để mốc hẹn không lặng lẽ trôi nấc.
        Assert.Equal(BayGio.AddMinutes(30), sau.NextRetryAt);
        Assert.Equal("Cổng bận", sau.FailReason);
        Assert.Equal(BayGio, sau.UpdatedAt);

        var nhatKy = Assert.Single(await DocNhatKyAsync(khoan.Id));
        Assert.Equal(BayGio, nhatKy.CreatedAt);
    }

    [Fact]
    public async Task Cong_khong_tra_loi_van_tinh_la_mot_luot_goi()
    {
        // Giá trị trả về đếm LƯỢT GỌI CỔNG ĐÃ THỰC HIỆN, không phải số khoản thành công: Unresolved
        // vẫn là một lời gọi đã ra ngoài (và là một khoản sắp có việc cho bước đối soát), nên lượt
        // quét phải báo về đúng như vậy. Đếm theo "thành công" là job im lặng bỏ qua đúng những lượt
        // đáng nhìn nhất.
        var gateway = new FakeRefundGatewayCaller
        {
            KetQua = new() { Result = RefundLogResult.Unresolved, Message = "Cổng không phản hồi" },
        };
        using var db = TaoDb();
        var khoan = await SeedRefundAsync(db, "RF-A", RefundStatus.Failed,
            nextRetryAt: BayGio.AddMinutes(-1), failReason: "Cổng bận");

        var soLuotGoi = await TaoSweep(gateway).SweepAsync(BayGio);

        Assert.Equal(1, soLuotGoi);
        Assert.Equal(1, gateway.SoLanGoi);

        var sau = await DocLaiAsync(khoan.Id);
        Assert.Equal(RefundStatus.Pending, sau.Status);   // giữ nguyên cho đối soát, không xếp lịch
        Assert.Null(sau.NextRetryAt);
    }

    [Fact]
    public async Task Luot_quet_sau_khong_goi_lai_khoan_da_xong()
    {
        var gateway = new FakeRefundGatewayCaller();
        using var db = TaoDb();
        await SeedRefundAsync(db, "RF-A", RefundStatus.Failed,
            nextRetryAt: BayGio.AddMinutes(-1), failReason: "Cổng bận");
        var sweep = TaoSweep(gateway);

        Assert.Equal(1, await sweep.SweepAsync(BayGio));

        // Lượt kế tiếp (nhịp 1 phút sau): khoản đã Succeeded, không còn là ứng viên — không gọi lại.
        Assert.Equal(0, await sweep.SweepAsync(BayGio.AddMinutes(1)));
        Assert.Equal(1, gateway.SoLanGoi);
    }

    // ------------------------------------------------------------------
    // Hạ tầng test
    // ------------------------------------------------------------------

    private AppDbContext TaoDb() => new(
        new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_tenCsdl).Options);

    /// <summary>
    /// Dựng DI THẬT cho lượt quét: <see cref="RefundRetryService"/> tự mở scope cho từng khoản qua
    /// <see cref="IServiceScopeFactory"/>, nên <see cref="IRefundAttemptService"/> phải resolve được
    /// từ scope y như trong app. Không dùng mock framework — đúng lối các file test hiện có.
    /// </summary>
    /// <param name="refundBiChen">
    /// Khi có giá trị: khoản này bị một lượt ghi khác giành trước ngay trước khi
    /// <see cref="RefundAttemptService"/> của lượt quét kịp đọc (xem <see cref="AttemptServiceChenNguoc"/>).
    /// </param>
    private ServiceProvider TaoProvider(FakeRefundGatewayCaller gateway, Guid? refundBiChen = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(_tenCsdl));
        services.AddSingleton<IRefundGatewayCaller>(gateway);

        services.AddScoped<IRefundAttemptService>(sp =>
        {
            var inner = new RefundAttemptService(
                sp.GetRequiredService<AppDbContext>(),
                gateway,
                NullLogger<RefundAttemptService>.Instance);

            return refundBiChen is { } id
                ? new AttemptServiceChenNguoc(sp.GetRequiredService<IServiceScopeFactory>(), inner, id)
                : inner;
        });

        return services.BuildServiceProvider();
    }

    private RefundRetryService TaoSweep(FakeRefundGatewayCaller gateway, Guid? refundBiChen = null)
    {
        var provider = TaoProvider(gateway, refundBiChen);
        return new RefundRetryService(
            TaoDb(),
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<RefundRetryService>.Instance);
    }

    private static async Task<Refund> SeedRefundAsync(
        AppDbContext db,
        string refundCode,
        RefundStatus trangThai,
        DateTime? nextRetryAt = null,
        string? failReason = null)
    {
        var giaoDich = new Payment
        {
            PaymentCode = $"PM-{refundCode}",
            MethodCode = PaymentProviderCodes.MoMo,
            Amount = SoTien,
            Status = PaymentStatus.Success,
            UserId = Guid.NewGuid(),
            TripId = Guid.NewGuid(),
            SeatNumbers = "A1",
            PaidAt = BayGio.AddHours(-2),
            CreatedAt = BayGio.AddHours(-2),
        };
        db.Payments.Add(giaoDich);

        var khoanHoan = new Refund
        {
            RefundCode = refundCode,
            Source = RefundSource.PaymentFailure,
            PaymentId = giaoDich.Id,
            UserId = giaoDich.UserId,
            Amount = SoTien,
            Status = trangThai,
            NextRetryAt = nextRetryAt,
            FailReason = failReason,
            CreatedAt = BayGio.AddHours(-1),
        };
        db.Refunds.Add(khoanHoan);
        await db.SaveChangesAsync();
        return khoanHoan;
    }

    private async Task<Refund> DocLaiAsync(Guid refundId)
    {
        using var dbDoc = TaoDb();
        return await dbDoc.Refunds.SingleAsync(r => r.Id == refundId);
    }

    private async Task<List<RefundLog>> DocNhatKyAsync(Guid refundId)
    {
        using var dbDoc = TaoDb();
        return await dbDoc.RefundLogs.Where(l => l.RefundId == refundId).ToListAsync();
    }
}

/// <summary>
/// Bọc <see cref="IRefundAttemptService"/> để dàn dựng một lượt ghi CHEN TRƯỚC cho đúng một khoản:
/// trước khi lượt quét kịp đọc khoản đó, mở scope riêng, lật khoản sang <c>Succeeded</c> (dấu vết
/// của quản trị vừa bấm thử tay thành công) rồi mới chuyển tiếp lời gọi vào bản thật.
///
/// Vì sao scope riêng: bản thật đọc khoản bằng context của scope lượt quét, còn lượt chen phải ghi
/// bằng một context KHÁC — đúng như lượt chen thật đến từ một request/phiên bản job khác, không phải
/// từ chính context đang đọc. Cùng context là bản thật đọc trúng bản tracked cũ và cuộc chen biến mất.
///
/// ⚠️ Đây là kết cục của lượt thua (bỏ qua, không gọi cổng), KHÔNG phải cơ chế thua: lượt thua thật
/// ném <c>DbUpdateConcurrencyException</c> ở <c>SaveChanges()</c> giành dòng. Cơ chế đó chỉ chạy được
/// trên nhà cung cấp có xmin — xem <see cref="RefundClaimConcurrencyTests"/>.
/// </summary>
internal sealed class AttemptServiceChenNguoc : IRefundAttemptService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IRefundAttemptService _inner;
    private readonly Guid _refundBiChen;

    public AttemptServiceChenNguoc(
        IServiceScopeFactory scopeFactory,
        IRefundAttemptService inner,
        Guid refundBiChen)
    {
        _scopeFactory = scopeFactory;
        _inner = inner;
        _refundBiChen = refundBiChen;
    }

    public async Task<RefundAttemptOutcome> ExecuteAsync(
        Guid refundId,
        RefundLogTrigger trigger,
        DateTime now,
        CancellationToken cancellationToken = default)
    {
        if (refundId == _refundBiChen)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var khoan = await db.Refunds.SingleAsync(r => r.Id == refundId, cancellationToken);
            khoan.Status = RefundStatus.Succeeded;
            khoan.ProcessedAt = now;
            khoan.UpdatedAt = now;
            await db.SaveChangesAsync(cancellationToken);
        }

        return await _inner.ExecuteAsync(refundId, trigger, now, cancellationToken);
    }
}
