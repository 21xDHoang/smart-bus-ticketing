using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SmartBus.Api.Data;
using SmartBus.Api.Dtos.Payments;
using SmartBus.Api.Dtos.Refunds;
using SmartBus.Api.Entities;
using SmartBus.Api.Services;

namespace SmartBus.Tests;

/// <summary>
/// Test tuần tự cho <see cref="RefundAttemptService"/> (task *"Xử lý hoàn tiền thất bại + retry theo
/// lịch"*, US 8): điều kiện được chạy theo từng loại lượt gọi, giao thức giành dòng, các trường được
/// ghi sau mỗi kết cục, nấc thang thử lại, và hình dạng yêu cầu gửi cổng.
///
/// ⚠️ <b>InMemory KHÔNG cưỡng chế gì cả:</b> không có xmin, không có chỉ mục duy nhất
/// IX_Refunds_PaymentId_Active, không có khoá ngoại (cùng lời cảnh báo đã ghi ở
/// <see cref="PaymentSettlementServiceTests"/>). Vì vậy nhánh THUA CUỘC ĐUA thật — lượt
/// <c>SaveChanges()</c> giành dòng ném <c>DbUpdateConcurrencyException</c> khi một lượt ghi khác đã
/// chạm dòng trước — KHÔNG có test nào ở file này phủ. Nó được dựng lại trên SQLite thật ở
/// <see cref="RefundClaimConcurrencyTests"/>, còn chốt thật của PostgreSQL (cột hệ thống <c>xmin</c>)
/// chỉ sống ở tầng CSDL thật. Đừng đọc file này xanh mà tưởng nhánh đua đã được kiểm.
///
/// Mọi mốc thời gian đi qua tham số <c>now</c> — không đồng hồ thật, không chờ, không chập chờn.
/// </summary>
public class RefundAttemptServiceTests
{
    private const string MaHoan = "RF-8f3a2c1d";

    private const decimal SoTien = 175_000m;

    /// <summary>Mốc "bây giờ" cố định của cả file — mọi assert là mốc này cộng/trừ một khoảng.</summary>
    private static readonly DateTime BayGio = new(2026, 10, 14, 8, 0, 0, DateTimeKind.Utc);

    /// <summary>Tên CSDL InMemory riêng cho từng test — xUnit dựng lớp test mới cho mỗi test.</summary>
    private readonly string _tenCsdl = $"hoan-tien-{Guid.NewGuid():N}";

    // ------------------------------------------------------------------
    // Lượt đầu (Initial)
    // ------------------------------------------------------------------

    [Fact]
    public async Task Luot_dau_thanh_cong_lat_sang_Succeeded_va_xoa_sach_dau_vet_lan_hong_cu()
    {
        var gateway = new FakeRefundGatewayCaller
        {
            KetQua = new() { Result = RefundLogResult.Succeeded, GatewayTransactionId = "GW-777" },
        };
        using var db = TaoDb();
        var khoan = await SeedRefundAsync(db, failReason: "lần hỏng trước");
        var service = TaoService(db, gateway);

        var ketQua = await service.ExecuteAsync(khoan.Id, RefundLogTrigger.Initial, BayGio);

        Assert.True(ketQua.Claimed);
        Assert.Equal(RefundLogResult.Succeeded, ketQua.Result);
        Assert.Null(ketQua.NextRetryAt);
        Assert.Equal(1, gateway.SoLanGoi);

        var sau = await DocLaiAsync(khoan.Id);
        Assert.Equal(RefundStatus.Succeeded, sau.Status);
        Assert.Equal("GW-777", sau.GatewayTransactionId);
        Assert.Null(sau.FailReason);          // bất biến §4.3: FailReason null ⟺ Status != Failed
        Assert.Null(sau.NextRetryAt);         // bất biến §4.3: NextRetryAt chỉ sống khi Status = Failed
        Assert.Equal(BayGio, sau.ProcessedAt);
        Assert.Equal(BayGio, sau.UpdatedAt);

        var nhatKy = Assert.Single(await DocNhatKyAsync(khoan.Id));
        Assert.Equal(RefundLogResult.Succeeded, nhatKy.Result);
        Assert.Equal(RefundLogTrigger.Initial, nhatKy.Trigger);
        Assert.Equal("GW-777", nhatKy.GatewayTransactionId);
        Assert.Equal(BayGio, nhatKy.CreatedAt);
    }

    [Fact]
    public async Task Luot_dau_tren_khoan_da_Failed_khong_chay_khong_goi_cong()
    {
        // Failed = đã có lượt gọi thất bại rồi. Chạy lại bằng Initial là bỏ qua thang thử lại —
        // nguồn gốc hợp lệ duy nhất của một lượt gọi trên khoản Failed là lịch (ScheduledRetry)
        // hoặc quản trị bấm tay (ManualRetry).
        var gateway = new FakeRefundGatewayCaller();
        using var db = TaoDb();
        var khoan = await SeedRefundAsync(
            db,
            trangThai: RefundStatus.Failed,
            failReason: "Cổng bận",
            nextRetryAt: BayGio.AddMinutes(5));
        var service = TaoService(db, gateway);

        var ketQua = await service.ExecuteAsync(khoan.Id, RefundLogTrigger.Initial, BayGio);

        Assert.False(ketQua.Claimed);
        Assert.Null(ketQua.Result);
        Assert.Equal(0, gateway.SoLanGoi);

        var sau = await DocLaiAsync(khoan.Id);
        Assert.Equal(RefundStatus.Failed, sau.Status);
        Assert.Equal("Cổng bận", sau.FailReason);
        Assert.Equal(BayGio.AddMinutes(5), sau.NextRetryAt);   // mốc hẹn còn nguyên
        Assert.Null(sau.UpdatedAt);                            // chưa hề bị ghi đè
        Assert.Empty(await DocNhatKyAsync(khoan.Id));
    }

    // ------------------------------------------------------------------
    // Lượt theo lịch (ScheduledRetry)
    // ------------------------------------------------------------------

    [Fact]
    public async Task Luot_theo_lich_truoc_moc_hen_khong_chay()
    {
        var gateway = new FakeRefundGatewayCaller();
        using var db = TaoDb();
        var khoan = await SeedRefundAsync(
            db,
            trangThai: RefundStatus.Failed,
            failReason: "Cổng bận",
            nextRetryAt: BayGio.AddMinutes(1));
        var service = TaoService(db, gateway);

        var ketQua = await service.ExecuteAsync(khoan.Id, RefundLogTrigger.ScheduledRetry, BayGio);

        Assert.False(ketQua.Claimed);
        Assert.Equal(0, gateway.SoLanGoi);
        Assert.Empty(await DocNhatKyAsync(khoan.Id));
    }

    [Fact]
    public async Task Luot_theo_lich_dung_ngay_moc_hen_thi_chay()
    {
        // Biên <= của điều kiện: đúng mốc hẹn là được chạy. Nhịp quét 1 phút chỉ có nghĩa khi biên
        // này rộng đúng một điểm — lệch thành < là mọi mốc hẹn trượt thêm một vòng quét.
        var gateway = new FakeRefundGatewayCaller();
        using var db = TaoDb();
        var khoan = await SeedRefundAsync(
            db,
            trangThai: RefundStatus.Failed,
            failReason: "Cổng bận",
            nextRetryAt: BayGio);
        var service = TaoService(db, gateway);

        var ketQua = await service.ExecuteAsync(khoan.Id, RefundLogTrigger.ScheduledRetry, BayGio);

        Assert.True(ketQua.Claimed);
        Assert.Equal(RefundLogResult.Succeeded, ketQua.Result);
        Assert.Equal(1, gateway.SoLanGoi);

        var nhatKy = Assert.Single(await DocNhatKyAsync(khoan.Id));
        Assert.Equal(RefundLogTrigger.ScheduledRetry, nhatKy.Trigger);
    }

    [Fact]
    public async Task Luot_theo_lich_tren_khoan_Pending_khong_chay()
    {
        // Pending = đang có lượt gọi dở hoặc mới tạo chờ gọi — lịch không được chen ngang.
        var gateway = new FakeRefundGatewayCaller();
        using var db = TaoDb();
        var khoan = await SeedRefundAsync(db, nextRetryAt: null);
        var service = TaoService(db, gateway);

        var ketQua = await service.ExecuteAsync(khoan.Id, RefundLogTrigger.ScheduledRetry, BayGio);

        Assert.False(ketQua.Claimed);
        Assert.Equal(0, gateway.SoLanGoi);
    }

    // ------------------------------------------------------------------
    // Quản trị bấm tay (ManualRetry)
    // ------------------------------------------------------------------

    [Fact]
    public async Task Bấm_tay_tren_khoan_da_het_thang_van_chay()
    {
        // Hết thang (NextRetryAt = null) là "hết lịch TỰ ĐỘNG", không phải "cấm gọi". Đây là lối
        // thoát duy nhất cho khoản kẹt — màn quản trị (dòng 46) nút "Thử lại" đi đúng vào đường này.
        var gateway = new FakeRefundGatewayCaller();
        using var db = TaoDb();
        var khoan = await SeedRefundAsync(
            db,
            trangThai: RefundStatus.Failed,
            failReason: "Cổng bận",
            nextRetryAt: null);
        var service = TaoService(db, gateway);

        var ketQua = await service.ExecuteAsync(khoan.Id, RefundLogTrigger.ManualRetry, BayGio);

        Assert.True(ketQua.Claimed);
        Assert.Equal(1, gateway.SoLanGoi);

        var sau = await DocLaiAsync(khoan.Id);
        Assert.Equal(RefundStatus.Succeeded, sau.Status);

        var nhatKy = Assert.Single(await DocNhatKyAsync(khoan.Id));
        Assert.Equal(RefundLogTrigger.ManualRetry, nhatKy.Trigger);
    }

    [Fact]
    public async Task Bấm_tay_truoc_moc_hen_cung_van_chay()
    {
        // Bấm tay không đọc NextRetryAt (khác ScheduledRetry) — quản trị được phép chạy SỚM hơn lịch.
        var gateway = new FakeRefundGatewayCaller();
        using var db = TaoDb();
        var khoan = await SeedRefundAsync(
            db,
            trangThai: RefundStatus.Failed,
            failReason: "Cổng bận",
            nextRetryAt: BayGio.AddMinutes(30));
        var service = TaoService(db, gateway);

        var ketQua = await service.ExecuteAsync(khoan.Id, RefundLogTrigger.ManualRetry, BayGio);

        Assert.True(ketQua.Claimed);
        Assert.Equal(1, gateway.SoLanGoi);
    }

    [Fact]
    public async Task Bấm_tay_tren_khoan_Pending_khong_chay()
    {
        var gateway = new FakeRefundGatewayCaller();
        using var db = TaoDb();
        var khoan = await SeedRefundAsync(db);
        var service = TaoService(db, gateway);

        var ketQua = await service.ExecuteAsync(khoan.Id, RefundLogTrigger.ManualRetry, BayGio);

        Assert.False(ketQua.Claimed);
        Assert.Equal(0, gateway.SoLanGoi);
    }

    [Fact]
    public async Task Khong_tim_thay_khoan_thi_tra_ve_em_khong_nem_khong_goi()
    {
        // Job quét đọc id xong mới gọi từng khoản; giữa hai bước khoản có thể biến mất (đối soát
        // dọn tay). Ném ở đây là cả lượt quét chết theo, trong khi việc đúng là bỏ qua id đó.
        var gateway = new FakeRefundGatewayCaller();
        using var db = TaoDb();
        var service = TaoService(db, gateway);

        var ketQua = await service.ExecuteAsync(Guid.NewGuid(), RefundLogTrigger.ScheduledRetry, BayGio);

        Assert.False(ketQua.Claimed);
        Assert.Null(ketQua.Result);
        Assert.Equal(string.Empty, ketQua.RefundCode);
        Assert.Equal(0, gateway.SoLanGoi);
    }

    // ------------------------------------------------------------------
    // Kết cục thất bại + không trả lời
    // ------------------------------------------------------------------

    [Fact]
    public async Task That_bai_ghi_ly_do_va_hen_moc_ke_tiep()
    {
        var gateway = new FakeRefundGatewayCaller
        {
            KetQua = new() { Result = RefundLogResult.Failed, Message = "Ngân hàng từ chối" },
        };
        using var db = TaoDb();
        var khoan = await SeedRefundAsync(db);
        var service = TaoService(db, gateway);

        var ketQua = await service.ExecuteAsync(khoan.Id, RefundLogTrigger.Initial, BayGio);

        Assert.True(ketQua.Claimed);
        Assert.Equal(RefundLogResult.Failed, ketQua.Result);
        Assert.Equal("Ngân hàng từ chối", ketQua.Message);          // thông điệp chuẩn hoá
        Assert.Equal(BayGio.AddMinutes(5), ketQua.NextRetryAt);     // nấc đầu của thang

        var sau = await DocLaiAsync(khoan.Id);
        Assert.Equal(RefundStatus.Failed, sau.Status);
        Assert.Equal("Ngân hàng từ chối", sau.FailReason);
        Assert.Equal(BayGio.AddMinutes(5), sau.NextRetryAt);
        Assert.Null(sau.GatewayTransactionId);                      // chưa có mã nào để giữ
        Assert.Equal(BayGio, sau.ProcessedAt);
        Assert.Equal(BayGio, sau.UpdatedAt);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task That_bai_khong_neu_ly_do_thi_dung_chuoi_du_phong(string? thongDiep)
    {
        // Cổng im lặng lúc từ chối là chuyện có thật; FailReason rỗng sẽ làm màn quản trị hiện
        // một dòng trắng — tệ hơn hẳn một câu nói rõ "không rõ vì sao".
        const string DuPhong = "Cổng từ chối mà không nêu lý do.";
        var gateway = new FakeRefundGatewayCaller
        {
            KetQua = new() { Result = RefundLogResult.Failed, Message = thongDiep },
        };
        using var db = TaoDb();
        var khoan = await SeedRefundAsync(db);
        var service = TaoService(db, gateway);

        var ketQua = await service.ExecuteAsync(khoan.Id, RefundLogTrigger.Initial, BayGio);

        // Chuỗi dự phòng vào hai chỗ ĐỌC ĐỂ HIỂN THỊ/ĐỐI CHIẾU: cột FailReason (màn quản trị đọc) và
        // outcome.Message (người gọi ghi log) — hai chỗ đó luôn phải có một câu đọc được.
        Assert.Equal(DuPhong, ketQua.Message);
        Assert.Equal(DuPhong, (await DocLaiAsync(khoan.Id)).FailReason);

        // Dòng nhật ký thì ngược lại: giữ NGUYÊN VĂN cổng nói gì, kể cả null/rỗng — nó là bằng chứng
        // thô cho bước đối soát (dòng 43) đối chiếu với sao kê, không phải chỗ diễn giải. Chuẩn hoá cả
        // nhật ký là xoá mất đúng dữ liệu mà lượt tra sao kê cần.
        Assert.Equal(thongDiep, Assert.Single(await DocNhatKyAsync(khoan.Id)).Message);
    }

    [Fact]
    public async Task Cong_khong_tra_loi_giu_nguyen_Pending_va_khong_xep_lich()
    {
        // Unresolved = "không biết tiền đã đi chưa". Xếp lịch thử lại ở đây là gọi cổng lần hai cho
        // một khoản CÓ THỂ đã hoàn — đường lùi duy nhất đúng là để nguyên cho bước đối soát
        // (dòng 43) soi sao kê. Khoản vẫn nằm lại trong tầm quét.
        var gateway = new FakeRefundGatewayCaller
        {
            KetQua = new() { Result = RefundLogResult.Unresolved, Message = "Cổng không phản hồi" },
        };
        using var db = TaoDb();
        var khoan = await SeedRefundAsync(db);
        var service = TaoService(db, gateway);

        var ketQua = await service.ExecuteAsync(khoan.Id, RefundLogTrigger.Initial, BayGio);

        Assert.True(ketQua.Claimed);          // ĐÃ gọi cổng — có tính là một lượt gọi
        Assert.Equal(RefundLogResult.Unresolved, ketQua.Result);
        Assert.Null(ketQua.NextRetryAt);

        var sau = await DocLaiAsync(khoan.Id);
        Assert.Equal(RefundStatus.Pending, sau.Status);
        Assert.Null(sau.FailReason);          // chưa phải thất bại
        Assert.Null(sau.NextRetryAt);
        Assert.Null(sau.ProcessedAt);         // chưa xử lý xong
        Assert.Null(sau.GatewayTransactionId);

        var nhatKy = Assert.Single(await DocNhatKyAsync(khoan.Id));
        Assert.Equal(RefundLogResult.Unresolved, nhatKy.Result);
    }

    [Fact]
    public async Task Cong_nem_ngoai_le_cung_thanh_Unresolved_chu_khong_sap_luot_quet()
    {
        var gateway = new FakeRefundGatewayCaller
        {
            NemRa = new InvalidOperationException("timeout nội bộ"),
        };
        using var db = TaoDb();
        var khoan = await SeedRefundAsync(db);
        var service = TaoService(db, gateway);

        var ketQua = await service.ExecuteAsync(khoan.Id, RefundLogTrigger.Initial, BayGio);

        Assert.True(ketQua.Claimed);
        Assert.Equal(RefundLogResult.Unresolved, ketQua.Result);
        Assert.StartsWith("Lỗi khi gọi cổng hoàn tiền:", ketQua.Message);

        var sau = await DocLaiAsync(khoan.Id);
        Assert.Equal(RefundStatus.Pending, sau.Status);
        Assert.Null(sau.NextRetryAt);

        var nhatKy = Assert.Single(await DocNhatKyAsync(khoan.Id));
        Assert.Equal(RefundLogResult.Unresolved, nhatKy.Result);
        Assert.StartsWith("Lỗi khi gọi cổng hoàn tiền:", nhatKy.Message);
    }

    [Fact]
    public async Task Chuoi_qua_dai_bi_cat_dung_gioi_han_cot()
    {
        // Cột là varchar(50)/varchar(500) — để EF tự đẩy chuỗi dài hơn xuống là lỗi CSDL ở tầng
        // PostgreSQL (Npgsql ném 22001), tức lượt ghi nhật ký chết theo và mất luôn dấu vết lượt gọi.
        var gateway = new FakeRefundGatewayCaller
        {
            KetQua = new()
            {
                Result = RefundLogResult.Failed,
                Message = new string('x', 700),
                GatewayTransactionId = new string('m', 60),
            },
        };
        using var db = TaoDb();
        var khoan = await SeedRefundAsync(db);
        var service = TaoService(db, gateway);

        var ketQua = await service.ExecuteAsync(khoan.Id, RefundLogTrigger.Initial, BayGio);

        var sau = await DocLaiAsync(khoan.Id);
        Assert.Equal(500, sau.FailReason!.Length);

        var nhatKy = Assert.Single(await DocNhatKyAsync(khoan.Id));
        Assert.Equal(500, nhatKy.Message!.Length);
        Assert.Equal(50, nhatKy.GatewayTransactionId!.Length);
        Assert.Equal(500, ketQua.Message!.Length);
    }

    // ------------------------------------------------------------------
    // Hình dạng yêu cầu gửi cổng
    // ------------------------------------------------------------------

    [Fact]
    public async Task Yeu_cau_gui_cong_lay_phuong_thuc_tu_giao_dich_goc_khong_phai_tham_so_nguoi_dung()
    {
        // docs/35 §6: phương thức hoàn BẮT BUỘC lấy từ Payments.MethodCode của giao dịch gốc.
        // Không có tham số nào để người gọi chọn phương thức — đó là chốt chống hoàn nhầm kênh.
        var gateway = new FakeRefundGatewayCaller();
        using var db = TaoDb();
        var khoan = await SeedRefundAsync(db, methodCode: PaymentProviderCodes.VnPay);
        var service = TaoService(db, gateway);

        await service.ExecuteAsync(khoan.Id, RefundLogTrigger.Initial, BayGio);

        var yeuCau = Assert.Single(gateway.YeuCau);
        Assert.Equal(khoan.Id, yeuCau.RefundId);
        Assert.Equal(MaHoan, yeuCau.RefundCode);
        Assert.Equal("VNPay", yeuCau.MethodCode);
        Assert.Equal(SoTien, yeuCau.Amount);
    }

    // ------------------------------------------------------------------
    // Cả thang: 5 nấc rồi dừng
    // ------------------------------------------------------------------

    [Fact]
    public async Task Thang_thu_lai_di_het_5_nac_roi_het_lich_tu_dong()
    {
        // Cả vòng đời của một khoản hoàn xấu số: 1 lượt đầu + 5 lượt theo lịch, mỗi lượt hỏng lại
        // rơi xuống nấc kế tiếp, hết thang thì NextRetryAt = null. Test này là chốt của công thức
        // đếm k trong RefundAttemptService: k = số dòng RefundLogs đã ghi có Trigger != Initial
        // (+1 cho chính lượt đang chạy nếu không phải Initial) — đếm sai một đơn vị là khoản hoàn
        // hoặc dừng sớm một nấc, hoặc thử lại vô hạn.
        var gateway = new FakeRefundGatewayCaller
        {
            KetQua = new() { Result = RefundLogResult.Failed, Message = "Cổng bận" },
        };
        using var db = TaoDb();
        var khoan = await SeedRefundAsync(db);
        var service = TaoService(db, gateway);

        var moc = BayGio;
        var ketQua = await service.ExecuteAsync(khoan.Id, RefundLogTrigger.Initial, moc);
        Assert.True(ketQua.Claimed);
        Assert.Equal(moc.AddMinutes(5), ketQua.NextRetryAt);

        moc = moc.AddMinutes(5);
        ketQua = await service.ExecuteAsync(khoan.Id, RefundLogTrigger.ScheduledRetry, moc);
        Assert.True(ketQua.Claimed);
        Assert.Equal(moc.AddMinutes(30), ketQua.NextRetryAt);

        moc = moc.AddMinutes(30);
        ketQua = await service.ExecuteAsync(khoan.Id, RefundLogTrigger.ScheduledRetry, moc);
        Assert.True(ketQua.Claimed);
        Assert.Equal(moc.AddMinutes(120), ketQua.NextRetryAt);

        moc = moc.AddMinutes(120);
        ketQua = await service.ExecuteAsync(khoan.Id, RefundLogTrigger.ScheduledRetry, moc);
        Assert.True(ketQua.Claimed);
        Assert.Equal(moc.AddMinutes(720), ketQua.NextRetryAt);

        moc = moc.AddMinutes(720);
        ketQua = await service.ExecuteAsync(khoan.Id, RefundLogTrigger.ScheduledRetry, moc);
        Assert.True(ketQua.Claimed);
        Assert.Equal(moc.AddMinutes(1440), ketQua.NextRetryAt);

        // Nấc thứ 5 là nấc cuối: hết thang, không hẹn nữa.
        moc = moc.AddMinutes(1440);
        ketQua = await service.ExecuteAsync(khoan.Id, RefundLogTrigger.ScheduledRetry, moc);
        Assert.True(ketQua.Claimed);
        Assert.Null(ketQua.NextRetryAt);
        Assert.Equal(6, gateway.SoLanGoi);

        var sau = await DocLaiAsync(khoan.Id);
        Assert.Equal(RefundStatus.Failed, sau.Status);
        Assert.Null(sau.NextRetryAt);
        Assert.Equal("Cổng bận", sau.FailReason);
        Assert.Equal(6, (await DocNhatKyAsync(khoan.Id)).Count);

        // Hết lịch thì lượt quét không còn chạm tới khoản này nữa (điều kiện NextRetryAt is not null).
        moc = moc.AddDays(1);
        ketQua = await service.ExecuteAsync(khoan.Id, RefundLogTrigger.ScheduledRetry, moc);
        Assert.False(ketQua.Claimed);
        Assert.Equal(6, gateway.SoLanGoi);

        // Nhưng quản trị vẫn bấm tay được — và bấm tay hỏng thì vẫn KHÔNG sinh lịch mới (k = 6 ≥ 5).
        ketQua = await service.ExecuteAsync(khoan.Id, RefundLogTrigger.ManualRetry, moc);
        Assert.True(ketQua.Claimed);
        Assert.Null(ketQua.NextRetryAt);
        Assert.Equal(7, gateway.SoLanGoi);
        Assert.Equal(7, (await DocNhatKyAsync(khoan.Id)).Count);
    }

    // ------------------------------------------------------------------
    // Hạ tầng test
    // ------------------------------------------------------------------

    private AppDbContext TaoDb() => new(
        new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_tenCsdl).Options);

    private static RefundAttemptService TaoService(AppDbContext db, IRefundGatewayCaller gateway) =>
        new(db, gateway, NullLogger<RefundAttemptService>.Instance);

    /// <summary>
    /// Seed một giao dịch gốc (Success, có MethodCode) + một khoản hoàn trỏ vào nó. Phương thức
    /// mặc định là MoMo để test nào cần kiểm "lấy từ giao dịch gốc" phải seed phương thức KHÁC.
    /// </summary>
    private static async Task<Refund> SeedRefundAsync(
        AppDbContext db,
        RefundStatus trangThai = RefundStatus.Pending,
        DateTime? nextRetryAt = null,
        string? failReason = null,
        string refundCode = MaHoan,
        decimal amount = SoTien,
        string methodCode = PaymentProviderCodes.MoMo)
    {
        var giaoDich = new Payment
        {
            PaymentCode = "PM-8f3a2c1d",
            MethodCode = methodCode,
            Amount = amount,
            Status = PaymentStatus.Success,
            UserId = Guid.NewGuid(),
            TripId = Guid.NewGuid(),
            SeatNumbers = "A1;A2",
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
            Amount = amount,
            Status = trangThai,
            NextRetryAt = nextRetryAt,
            FailReason = failReason,
            CreatedAt = BayGio.AddHours(-1),
        };
        db.Refunds.Add(khoanHoan);
        await db.SaveChangesAsync();
        return khoanHoan;
    }

    /// <summary>
    /// Đọc lại dòng bằng context MỚI — không đọc trên context của service, vì context đó còn giữ
    /// bản tracked trong bộ nhớ và sẽ "xác nhận" đúng thứ vừa ghi kể cả khi lượt ghi xuống CSDL
    /// đã hỏng. Cùng lối đã ghi ở PaymentSettlementServiceTests.
    /// </summary>
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
/// Cổng hoàn tiền giả: trả về đúng <see cref="KetQua"/> đã hẹn, hoặc ném <see cref="NemRa"/> nếu
/// được gài, và ghi lại mọi yêu cầu để assert hình dạng. Không bao giờ tự bịa kết quả — test nào
/// muốn kết cục nào phải nói rõ.
/// </summary>
internal sealed class FakeRefundGatewayCaller : IRefundGatewayCaller
{
    public RefundGatewayCallOutcome KetQua { get; set; } = new()
    {
        Result = RefundLogResult.Succeeded,
        GatewayTransactionId = "GW-0001",
    };

    public Exception? NemRa { get; set; }

    public int SoLanGoi { get; private set; }

    public List<RefundGatewayCallRequest> YeuCau { get; } = [];

    public Task<RefundGatewayCallOutcome> CallAsync(
        RefundGatewayCallRequest request,
        CancellationToken cancellationToken = default)
    {
        SoLanGoi++;
        YeuCau.Add(request);

        return NemRa is not null
            ? Task.FromException<RefundGatewayCallOutcome>(NemRa)
            : Task.FromResult(KetQua);
    }
}
