using Microsoft.EntityFrameworkCore;
using SmartBus.Api.Data;
using SmartBus.Api.Dtos.Payments;
using SmartBus.Api.Entities;
using SmartBus.Api.Services;

namespace SmartBus.Tests;

/// <summary>
/// Test phần ruột chốt thanh toán idempotent — <see cref="PaymentSettlementService"/>
/// (task *"Xử lý idempotency: chống trừ tiền 2 lần khi callback trùng"* — Phùng Duy Hoàng).
///
/// Gọi thẳng service, không dựng host: endpoint callback chưa dựng, mà thứ cần ghim nằm ở đây —
/// cổng gửi LẠI callback (MoMo retry tới khi nhận 204, job đối soát chạy trùng) không được lật
/// dòng lần hai, không được ghi đè mã giao dịch / mốc thu tiền của lượt thắng đầu. Phần "service
/// có thật sự được đăng ký vào app không" nằm ở <see cref="PaymentSettlementWiringTests"/>.
///
/// Dùng provider InMemory như phần còn lại của bộ test. Không seed Users/Trips: InMemory không
/// cưỡng chế khoá ngoài, mà truy vấn của service chỉ đọc đúng bảng Payments.
///
/// ⚠️ InMemory CŨNG KHÔNG cưỡng chế concurrency token xmin (AppDbContext.Payment.cs), nên nhánh
/// đua THẬT — hai lượt chốt chạy song song, lượt thua nhận DbUpdateConcurrencyException — KHÔNG
/// có test nào phủ ở đây; nó chỉ tồn tại ở tầng PostgreSQL. Bù lại, đường "bản gửi lại" (tuần tự)
/// được phủ đầy đủ bên dưới. Đừng đọc file này xanh mà tưởng nhánh đua đã được kiểm.
/// </summary>
public class PaymentSettlementServiceTests
{
    private const string MaDon = "PM-8f3a2c1d";
    private const decimal SoTien = 175_000m;

    /// <summary>Mốc "bây giờ" cố định — CreatedAt của mọi bản ghi seed quanh mốc này.</summary>
    private static readonly DateTime BayGio = new(2026, 10, 10, 8, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// Thời điểm cổng ghi nhận thu tiền — đúng giá trị adapter VNPay đổi từ vnp_PayDate
    /// "20261010153200" (GMT+7) về UTC trong bộ test của adapter.
    /// </summary>
    private static readonly DateTime MocCong = new(2026, 10, 10, 8, 32, 0, DateTimeKind.Utc);

    /// <summary>
    /// Tên CSDL InMemory riêng cho từng ca test. xUnit dựng một instance lớp test mới cho mỗi ca nên
    /// mỗi ca có CSDL sạch — cùng tên là dùng chung dữ liệu.
    /// </summary>
    private readonly string _tenCsdl = $"chot-thanh-toan-{Guid.NewGuid()}";

    // ---------------------------------------------------------------------------------------
    // Lượt chốt thắng — lật Pending lần đầu
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Callback_thanh_cong_lat_Pending_sang_Success_va_ghi_du_truong()
    {
        using var db = TaoDb();
        var giaoDich = await SeedPaymentAsync(db);

        var ketQua = await new PaymentSettlementService(db).SettleAsync(
            PaymentProviderCodes.MoMo,
            Callback(gatewayTransactionId: "3456789", paidAt: MocCong));

        Assert.True(ketQua.Success);
        var ket = ketQua.Data!;
        Assert.True(ket.SettledNow);
        Assert.Equal(PaymentStatus.Success, ket.Status);
        Assert.True(ket.ShouldIssueTickets);

        // Kết quả mang đủ dữ liệu cho bước phát hành vé — người gọi không phải hỏi lại CSDL.
        Assert.Equal(giaoDich.Id, ket.PaymentId);
        Assert.Equal(MaDon, ket.PaymentCode);
        Assert.Equal(giaoDich.UserId, ket.UserId);
        Assert.Equal(giaoDich.TripId, ket.TripId);

        // Đọc lại bằng context KHÁC để chắc chắn thay đổi đã xuống CSDL, không chỉ nằm trong change
        // tracker của context vừa dùng.
        using var dbDoc = TaoDb();
        var sauKhiChot = await dbDoc.Payments.SingleAsync(p => p.Id == giaoDich.Id);

        Assert.Equal(PaymentStatus.Success, sauKhiChot.Status);
        Assert.Equal("3456789", sauKhiChot.GatewayTransactionId);
        Assert.Equal(MocCong, sauKhiChot.PaidAt);
        Assert.Null(sauKhiChot.Message);
    }

    [Fact]
    public async Task Callback_thanh_cong_khong_kem_thoi_diem_cong_thi_lay_gio_he_thong()
    {
        using var db = TaoDb();
        var giaoDich = await SeedPaymentAsync(db);
        var truoc = DateTime.UtcNow;

        var ketQua = await new PaymentSettlementService(db).SettleAsync(
            PaymentProviderCodes.MoMo,
            Callback(paidAt: null));

        Assert.True(ketQua.Success);

        using var dbDoc = TaoDb();
        var sauKhiChot = await dbDoc.Payments.SingleAsync(p => p.Id == giaoDich.Id);

        // Cổng hiếm khi không kèm thời điểm, nhưng thiếu thì lấy giờ hệ thống chứ KHÔNG được để
        // null — vé đã trả tiền mà không có mốc thu tiền nào thì job đối soát mù.
        Assert.NotNull(sauKhiChot.PaidAt);
        Assert.True(sauKhiChot.PaidAt >= truoc);
    }

    [Fact]
    public async Task Callback_that_bai_lat_sang_Failed_va_luu_thong_diep_cong()
    {
        using var db = TaoDb();
        var giaoDich = await SeedPaymentAsync(db);

        var ketQua = await new PaymentSettlementService(db).SettleAsync(
            PaymentProviderCodes.MoMo,
            Callback(succeeded: false, message: "Khách huỷ giao dịch"));

        Assert.True(ketQua.Success);
        Assert.True(ketQua.Data!.SettledNow);
        Assert.Equal(PaymentStatus.Failed, ketQua.Data!.Status);
        Assert.False(ketQua.Data!.ShouldIssueTickets);

        using var dbDoc = TaoDb();
        var sauKhiChot = await dbDoc.Payments.SingleAsync(p => p.Id == giaoDich.Id);

        Assert.Equal(PaymentStatus.Failed, sauKhiChot.Status);
        Assert.Equal("Khách huỷ giao dịch", sauKhiChot.Message);
        // Không có tiền vào thì không có mã giao dịch cổng lẫn mốc thu tiền.
        Assert.Null(sauKhiChot.GatewayTransactionId);
        Assert.Null(sauKhiChot.PaidAt);
    }

    [Fact]
    public async Task Thong_diep_cong_dai_hon_500_ky_tu_bi_cat_bot()
    {
        using var db = TaoDb();
        var giaoDich = await SeedPaymentAsync(db);

        var ketQua = await new PaymentSettlementService(db).SettleAsync(
            PaymentProviderCodes.MoMo,
            Callback(succeeded: false, message: new string('x', 700)));

        Assert.True(ketQua.Success);

        using var dbDoc = TaoDb();
        var sauKhiChot = await dbDoc.Payments.SingleAsync(p => p.Id == giaoDich.Id);

        // Cột Message là varchar(500) — không cắt là PostgreSQL ném lỗi ngay lúc chốt, biến giao
        // dịch hỏng thành callback 500 và cổng gửi lại vô hạn.
        Assert.Equal(500, sauKhiChot.Message!.Length);
    }

    [Fact]
    public async Task Thong_diep_cong_rong_thi_de_null()
    {
        using var db = TaoDb();
        var giaoDich = await SeedPaymentAsync(db);

        var ketQua = await new PaymentSettlementService(db).SettleAsync(
            PaymentProviderCodes.MoMo,
            Callback(succeeded: false, message: "   "));

        Assert.True(ketQua.Success);

        using var dbDoc = TaoDb();
        var sauKhiChot = await dbDoc.Payments.SingleAsync(p => p.Id == giaoDich.Id);

        // Chuỗi toàn khoảng trắng không phải thông điệp — lưu null để màn chờ biết "cổng không nói gì".
        Assert.Null(sauKhiChot.Message);
    }

    // ---------------------------------------------------------------------------------------
    // Trái tim của task — bản gửi lại không được lật dòng lần hai
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Callback_lap_lai_sau_thanh_cong_khong_lat_lan_hai_va_khong_ghi_de()
    {
        using var db = TaoDb();
        var giaoDich = await SeedPaymentAsync(db);
        var service = new PaymentSettlementService(db);

        var lanDau = await service.SettleAsync(
            PaymentProviderCodes.MoMo, Callback(gatewayTransactionId: "3456789", paidAt: MocCong));
        Assert.True(lanDau.Data!.SettledNow);

        // Cổng gửi LẠI — đúng cơ chế retry của MoMo tới khi nhận 204. Bản gửi lại mang giá trị khác
        // (transId khác, giờ khác) để chứng minh không gì bị ghi đè, không tiền nào bị trừ lần hai.
        var lanHai = await service.SettleAsync(
            PaymentProviderCodes.MoMo,
            Callback(gatewayTransactionId: "9999999", paidAt: MocCong.AddHours(1)));

        Assert.True(lanHai.Success);
        Assert.False(lanHai.Data!.SettledNow);
        Assert.Equal(PaymentStatus.Success, lanHai.Data!.Status);
        // Vé chưa phát hành ở test này (TicketId còn null) nên bản gửi lại vẫn phải gợi ý phát hành
        // — nhờ vậy cổng retry cứu được vé khi lần phát hành trước đứt giữa đường.
        Assert.True(lanHai.Data!.ShouldIssueTickets);

        using var dbDoc = TaoDb();
        var sauKhiChot = await dbDoc.Payments.SingleAsync(p => p.Id == giaoDich.Id);

        Assert.Equal(PaymentStatus.Success, sauKhiChot.Status);
        Assert.Equal("3456789", sauKhiChot.GatewayTransactionId);
        Assert.Equal(MocCong, sauKhiChot.PaidAt);
    }

    [Fact]
    public async Task Callback_lap_lai_sau_that_bai_giu_nguyen_thong_diep_lan_dau()
    {
        using var db = TaoDb();
        var giaoDich = await SeedPaymentAsync(db);
        var service = new PaymentSettlementService(db);

        var lanDau = await service.SettleAsync(
            PaymentProviderCodes.MoMo, Callback(succeeded: false, message: "Giao dịch hết hạn"));
        Assert.True(lanDau.Data!.SettledNow);

        // Cổng thử lại nhưng vẫn hỏng, lần này kèm thông điệp khác — lý do hỏng THẬT là lần đầu;
        // đè lên là màn chờ của khách đổi lý do sau khi sự việc đã kết thúc.
        var lanHai = await service.SettleAsync(
            PaymentProviderCodes.MoMo, Callback(succeeded: false, message: "Khách huỷ giao dịch"));

        Assert.True(lanHai.Success);
        Assert.False(lanHai.Data!.SettledNow);

        using var dbDoc = TaoDb();
        var sauKhiChot = await dbDoc.Payments.SingleAsync(p => p.Id == giaoDich.Id);

        Assert.Equal(PaymentStatus.Failed, sauKhiChot.Status);
        Assert.Equal("Giao dịch hết hạn", sauKhiChot.Message);
    }

    [Fact]
    public async Task Thanh_cong_muon_sau_khi_da_Failed_bi_bo_qua()
    {
        using var db = TaoDb();
        var giaoDich = await SeedPaymentAsync(db);
        var service = new PaymentSettlementService(db);

        await service.SettleAsync(
            PaymentProviderCodes.MoMo, Callback(succeeded: false, message: "Ngân hàng từ chối"));

        // Callback báo thành công tới sau (cổng xử lý lệch nhịp) — trạng thái đã ngã ngũ là chốt:
        // không lật ngược Failed → Success, không đụng tới tiền.
        var muon = await service.SettleAsync(
            PaymentProviderCodes.MoMo, Callback(gatewayTransactionId: "9999999", paidAt: MocCong));

        Assert.True(muon.Success);
        Assert.False(muon.Data!.SettledNow);
        Assert.Equal(PaymentStatus.Failed, muon.Data!.Status);

        using var dbDoc = TaoDb();
        var sauKhiChot = await dbDoc.Payments.SingleAsync(p => p.Id == giaoDich.Id);

        Assert.Equal(PaymentStatus.Failed, sauKhiChot.Status);
        Assert.Null(sauKhiChot.PaidAt);
        Assert.Null(sauKhiChot.GatewayTransactionId);
        Assert.Equal("Ngân hàng từ chối", sauKhiChot.Message);
    }

    // ---------------------------------------------------------------------------------------
    // Cổng chặn trước khi chạm CSDL
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Chu_ky_khong_hop_le_tra_Invalid_va_khong_cham_CSDL()
    {
        using var db = TaoDb();
        var giaoDich = await SeedPaymentAsync(db);

        var ketQua = await new PaymentSettlementService(db).SettleAsync(
            PaymentProviderCodes.MoMo,
            Callback(isValid: false, gatewayTransactionId: "9999999", paidAt: MocCong));

        Assert.False(ketQua.Success);
        Assert.Equal(ServiceErrorKind.Invalid, ketQua.ErrorKind);
        Assert.NotNull(ketQua.Errors);
        Assert.Contains("signature", ketQua.Errors!.Keys);

        // Dữ liệu người ngoài gửi tới không được chạm vào dòng nào — bản ghi còn nguyên Pending.
        using var dbDoc = TaoDb();
        var sauKhiGoi = await dbDoc.Payments.SingleAsync(p => p.Id == giaoDich.Id);

        Assert.Equal(PaymentStatus.Pending, sauKhiGoi.Status);
        Assert.Null(sauKhiGoi.GatewayTransactionId);
        Assert.Null(sauKhiGoi.PaidAt);
    }

    [Fact]
    public async Task Khong_co_ban_ghi_khop_ma_don_thi_NotFound()
    {
        using var db = TaoDb();
        await SeedPaymentAsync(db);

        var ketQua = await new PaymentSettlementService(db).SettleAsync(
            PaymentProviderCodes.MoMo, Callback(paymentCode: "PM-khong-ton-tai"));

        Assert.False(ketQua.Success);
        Assert.Equal(ServiceErrorKind.NotFound, ketQua.ErrorKind);
    }

    [Fact]
    public async Task Sai_cong_thi_NotFound()
    {
        using var db = TaoDb();
        await SeedPaymentAsync(db); // giao dịch tạo qua MoMo

        // Cùng mã đơn nhưng callback tự nhận là VNPay — không thuộc về bản ghi này.
        var ketQua = await new PaymentSettlementService(db).SettleAsync(
            PaymentProviderCodes.VnPay, Callback());

        Assert.False(ketQua.Success);
        Assert.Equal(ServiceErrorKind.NotFound, ketQua.ErrorKind);
    }

    [Fact]
    public async Task Lech_so_tien_du_nua_dong_thi_NotFound()
    {
        using var db = TaoDb();
        await SeedPaymentAsync(db);

        // Vector lệch nửa đồng (175.000,50) — đúng ca "chia lại tiền VNPay" sinh ra; phép đối chiếu
        // phải so CHÍNH XÁC, không được làm tròn thành khớp.
        var ketQua = await new PaymentSettlementService(db).SettleAsync(
            PaymentProviderCodes.MoMo, Callback(amount: 175_000.50m));

        Assert.False(ketQua.Success);
        Assert.Equal(ServiceErrorKind.NotFound, ketQua.ErrorKind);
    }

    [Fact]
    public async Task Ma_cong_khop_khong_phan_biet_hoa_thuong()
    {
        using var db = TaoDb();
        await SeedPaymentAsync(db);

        // Hợp đồng ghi "MoMo"/"VNPay" nhưng so khớp không phân biệt hoa thường — biến thể chữ của
        // cùng một cổng không được biến callback thật thành 404.
        var ketQua = await new PaymentSettlementService(db).SettleAsync("momo", Callback());

        Assert.True(ketQua.Success);
        Assert.True(ketQua.Data!.SettledNow);
    }

    // ---------------------------------------------------------------------------------------
    // Cờ phát hành vé — hợp đồng với bước "Vé điện tử" (chưa dựng)
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Giao_dich_da_Success_nhung_chua_gan_ve_thi_van_goi_y_phat_hanh()
    {
        using var db = TaoDb();
        await SeedPaymentAsync(
            db, trangThai: PaymentStatus.Success, gatewayTransactionId: "3456789", paidAt: MocCong);

        var ketQua = await new PaymentSettlementService(db).SettleAsync(
            PaymentProviderCodes.MoMo, Callback());

        Assert.True(ketQua.Success);
        Assert.False(ketQua.Data!.SettledNow);
        // Đã Success từ lượt trước nhưng TicketId còn null = lần phát hành trước đứt giữa đường —
        // bản gửi lại phải vẫn gợi ý phát hành, nếu không vé của khách mất vĩnh viễn.
        Assert.True(ketQua.Data!.ShouldIssueTickets);
    }

    [Fact]
    public async Task Giao_dich_da_Success_va_da_gan_ve_thi_khong_goi_y_nua()
    {
        using var db = TaoDb();
        await SeedPaymentAsync(
            db,
            trangThai: PaymentStatus.Success,
            gatewayTransactionId: "3456789",
            paidAt: MocCong,
            ticketId: Guid.NewGuid());

        var ketQua = await new PaymentSettlementService(db).SettleAsync(
            PaymentProviderCodes.MoMo, Callback());

        Assert.True(ketQua.Success);
        Assert.False(ketQua.Data!.SettledNow);
        // Vé đã phát hành rồi — gợi ý nữa là mời gọi phát hành trùng, đúng thứ task này chống.
        Assert.False(ketQua.Data!.ShouldIssueTickets);
    }

    // ---------------------------------------------------------------------------------------
    // Helper — mỗi lớp test tự chép, không có lớp base chung (đúng lệ đang có của dự án)
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Context mới trên cùng một CSDL InMemory. Gọi nhiều lần được để đọc lại bằng context sạch —
    /// cùng tên CSDL nên dữ liệu dùng chung.
    /// </summary>
    private AppDbContext TaoDb() => new(
        new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(_tenCsdl)
            .Options);

    /// <summary>
    /// Một giao dịch quanh mốc <see cref="BayGio"/>. Mặc định là giao dịch bình thường: Pending,
    /// tạo qua MoMo, chưa có mã cổng. Ca cần trạng thái khác truyền tường minh.
    /// </summary>
    private static async Task<Payment> SeedPaymentAsync(
        AppDbContext db,
        PaymentStatus trangThai = PaymentStatus.Pending,
        string paymentCode = MaDon,
        decimal amount = SoTien,
        string methodCode = PaymentProviderCodes.MoMo,
        string? gatewayTransactionId = null,
        DateTime? paidAt = null,
        Guid? ticketId = null)
    {
        var giaoDich = new Payment
        {
            PaymentCode = paymentCode,
            MethodCode = methodCode,
            Amount = amount,
            Status = trangThai,
            UserId = Guid.NewGuid(),
            TripId = Guid.NewGuid(),
            SeatNumbers = "A1;A2",
            GatewayTransactionId = gatewayTransactionId,
            PaidAt = paidAt,
            TicketId = ticketId,
            CreatedAt = BayGio.AddMinutes(-10),
        };

        db.Payments.Add(giaoDich);
        await db.SaveChangesAsync();

        return giaoDich;
    }

    /// <summary>
    /// Kết quả ĐÃ CHUẨN HOÁ mà adapter cổng sẽ đưa vào — ở đây dựng thẳng, không qua adapter
    /// (adapter có bộ test riêng của nó).
    /// </summary>
    private static PaymentCallbackResult Callback(
        bool succeeded = true,
        bool isValid = true,
        string paymentCode = MaDon,
        decimal amount = SoTien,
        string gatewayTransactionId = "3456789",
        DateTime? paidAt = null,
        string message = "")
        => new()
        {
            IsValid = isValid,
            Succeeded = succeeded,
            PaymentCode = paymentCode,
            Amount = amount,
            GatewayTransactionId = gatewayTransactionId,
            PaidAt = paidAt,
            ProviderResponseCode = succeeded ? "0" : "1006",
            Message = message,
        };
}
