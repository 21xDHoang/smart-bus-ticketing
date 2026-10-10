using Microsoft.EntityFrameworkCore;
using SmartBus.Api.Data;
using SmartBus.Api.Dtos.Vouchers;
using SmartBus.Api.Entities;
using SmartBus.Api.Services;

namespace SmartBus.Tests;

/// <summary>
/// Test bộ luật voucher và số học tiền giảm — <see cref="VoucherValidationService"/>, phần ruột của
/// dòng 52 + 53 (US 18, Nguyễn Duy Kiên).
///
/// Hai tầng, cố ý tách nhau:
///   • <see cref="VoucherValidationService.ValidateAsync"/> trên InMemory — phần phải chạm CSDL:
///     tra chuyến, tra mã, chuẩn hoá chữ HOA, và hình dạng câu trả lời.
///   • <see cref="VoucherValidationService.ReasonFor"/> và
///     <see cref="VoucherValidationService.DiscountFor"/> gọi THẲNG với một mốc <c>now</c> ghim
///     sẵn — hai hàm thuần, nên ghim được đúng hai đầu mút của khoảng hiệu lực mà không phải chờ
///     đồng hồ hệ thống, và ghim được ĐÚNG thứ tự ưu tiên (thứ tự là một phần của hợp đồng).
///
/// Số học ghim bằng SỐ TÍNH TAY trong <c>Assert.Equal</c>: đổi công thức là đỏ ngay. Đây là chỗ
/// thay cho "vector kiểm thử" mà bên cổng thanh toán có — voucher không có tham chiếu ngoài C#.
/// </summary>
public class VoucherValidationServiceTests
{
    /// <summary>
    /// Mốc "bây giờ" cố định cho các phép kiểm gọi thẳng hàm thuần. Đặt giữa một khoảng hiệu lực
    /// rộng để ca nào không cố ý chạm hai đầu mút thì không bị ảnh hưởng.
    /// </summary>
    private static readonly DateTime BayGio = new(2026, 10, 10, 9, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime TuNgay = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime DenNgay = new(2026, 10, 31, 23, 59, 59, DateTimeKind.Utc);

    /// <summary>
    /// Tên CSDL InMemory riêng cho từng ca test — xUnit dựng một instance lớp test mới cho mỗi ca,
    /// cùng tên là dùng chung dữ liệu.
    /// </summary>
    private readonly string _tenCsdl = $"voucher-kiem-tra-{Guid.NewGuid()}";

    // ---------------------------------------------------------------------------------------
    // Số học tiền giảm — những con số này là hợp đồng, không phải chi tiết cài đặt
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void Percent_tinh_tren_tong_tien_truoc_giam()
    {
        var voucher = TaoVoucher(VoucherDiscountType.Percent, discountValue: 10m);

        // 175.000 × 10% = 17.500
        Assert.Equal(17500m, VoucherValidationService.DiscountFor(voucher, 175000m));
    }

    [Fact]
    public void Percent_lam_tron_ve_DONG_NGUYEN_chu_khong_giu_phan_le()
    {
        var voucher = TaoVoucher(VoucherDiscountType.Percent, discountValue: 15m);

        // 33.333 × 15% = 4.999,95 → 5.000. Đây là ca chốt của cả tính năng: PaymentInitiationRequest
        // .Amount là long nên số gửi cổng buộc phải nguyên, mà PaymentSettlementService so
        // payment.Amount != callback.Amount rồi trả 404 nếu lệch — để lọt phần lẻ là MỌI callback
        // của giao dịch đó không chốt được. Làm tròn tại đây giữ hoá đơn cộng lại khớp tuyệt đối.
        Assert.Equal(5000m, VoucherValidationService.DiscountFor(voucher, 33333m));
    }

    [Fact]
    public void Percent_dung_tran_MaxDiscount_thi_bi_cat_ve_dung_tran()
    {
        var voucher = TaoVoucher(
            VoucherDiscountType.Percent,
            discountValue: 25m,
            maxDiscount: 25000m);

        // 200.000 × 25% = 50.000, nhưng trần là 25.000.
        Assert.Equal(25000m, VoucherValidationService.DiscountFor(voucher, 200000m));
    }

    [Fact]
    public void Percent_chua_cham_tran_thi_khong_bi_cat()
    {
        var voucher = TaoVoucher(
            VoucherDiscountType.Percent,
            discountValue: 10m,
            maxDiscount: 25000m);

        // 100.000 × 10% = 10.000 < trần 25.000 — trần không được kéo số giảm LÊN.
        Assert.Equal(10000m, VoucherValidationService.DiscountFor(voucher, 100000m));
    }

    [Fact]
    public void FixedAmount_bo_qua_tran_MaxDiscount()
    {
        var voucher = TaoVoucher(
            VoucherDiscountType.FixedAmount,
            discountValue: 50000m,
            maxDiscount: 10000m);

        // Trần chỉ có nghĩa với Percent. Áp nó cho FixedAmount là để một cấu hình sai còn sót lại
        // âm thầm cắt mất 40.000đ khách đáng được giảm.
        Assert.Equal(50000m, VoucherValidationService.DiscountFor(voucher, 200000m));
    }

    [Fact]
    public void FixedAmount_lon_hon_don_thi_bi_kep_ve_dung_bang_don()
    {
        var voucher = TaoVoucher(VoucherDiscountType.FixedAmount, discountValue: 15000m);

        // Giảm 15.000 cho đơn 10.000 ⇒ chỉ được giảm 10.000. Thiếu phép kẹp này thì finalAmount
        // = -5.000 và số tiền ÂM chảy thẳng xuống cổng thanh toán.
        Assert.Equal(10000m, VoucherValidationService.DiscountFor(voucher, 10000m));
    }

    [Fact]
    public void Percent_mot_tram_lam_don_ve_khong()
    {
        var voucher = TaoVoucher(VoucherDiscountType.Percent, discountValue: 100m);

        Assert.Equal(50000m, VoucherValidationService.DiscountFor(voucher, 50000m));
    }

    [Fact]
    public void Percent_vuot_mot_tram_van_khong_lam_don_am()
    {
        // Dữ liệu bẩn (ai đó nhập 150 vào ô phần trăm): phép kẹp cuối cùng vẫn phải giữ
        // finalAmount >= 0. Service CRUD của dòng 51 lo phần chặn 1..100 ở đầu vào; đây là lưới
        // an toàn thứ hai, vì con số này đi thẳng vào số tiền khách trả.
        var voucher = TaoVoucher(VoucherDiscountType.Percent, discountValue: 150m);

        Assert.Equal(50000m, VoucherValidationService.DiscountFor(voucher, 50000m));
    }

    // ---------------------------------------------------------------------------------------
    // Thứ tự ưu tiên của bộ luật — câu trả về là điều kiện ĐẦU TIÊN không thoả
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void Khong_tim_thay_voucher_la_ly_do_dau_tien()
    {
        Assert.Equal(
            VoucherReasonCodes.NotFound,
            VoucherValidationService.ReasonFor(null, tripRouteId: null, orderAmount: 0m, now: BayGio));
    }

    [Fact]
    public void Voucher_da_tat_thang_moi_ly_do_khac()
    {
        // Vừa Inactive, vừa hết hạn, vừa hết lượt, vừa sai tuyến, vừa dưới giá tối thiểu — câu trả
        // về phải là Inactive: quyết định hiện tại của người vận hành đứng trên mọi thứ khác.
        var voucher = TaoVoucher(
            VoucherDiscountType.Percent,
            discountValue: 10m,
            status: VoucherStatus.Inactive,
            validFrom: TuNgay.AddDays(-30),
            validUntil: TuNgay.AddDays(-1),
            quantity: 5,
            usedCount: 5,
            minOrderValue: 500000m);
        voucher.RouteId = Guid.NewGuid();

        Assert.Equal(
            VoucherReasonCodes.Inactive,
            VoucherValidationService.ReasonFor(voucher, Guid.NewGuid(), 1000m, BayGio));
    }

    [Fact]
    public void Chua_toi_ngay_hieu_luc_dung_truoc_het_han()
    {
        var voucher = TaoVoucher(
            VoucherDiscountType.Percent,
            discountValue: 10m,
            validFrom: BayGio.AddDays(1),
            validUntil: BayGio.AddDays(10));

        Assert.Equal(
            VoucherReasonCodes.NotStarted,
            VoucherValidationService.ReasonFor(voucher, null, 100000m, BayGio));
    }

    [Fact]
    public void Het_luot_dung_truoc_sai_tuyen_va_duoi_gia_toi_thieu()
    {
        // Ba điều kiện cùng sai. "Đã hết lượt" là sự thật tuyệt đối — bảo khách "thêm tiền nữa đi"
        // hay "mã này của tuyến khác" cho một mã không còn lượt nào là câu sai đường.
        var voucher = TaoVoucher(
            VoucherDiscountType.Percent,
            discountValue: 10m,
            quantity: 3,
            usedCount: 3,
            minOrderValue: 500000m);
        voucher.RouteId = Guid.NewGuid();

        Assert.Equal(
            VoucherReasonCodes.OutOfStock,
            VoucherValidationService.ReasonFor(voucher, Guid.NewGuid(), 1000m, BayGio));
    }

    [Fact]
    public void Sai_tuyen_dung_truoc_duoi_gia_toi_thieu()
    {
        // Tuyến là "dùng được hay không", giá trị đơn là "gợi ý khách làm gì tiếp". Dùng được hay
        // không phải trả lời trước.
        var voucher = TaoVoucher(
            VoucherDiscountType.Percent,
            discountValue: 10m,
            minOrderValue: 500000m);
        voucher.RouteId = Guid.NewGuid();

        Assert.Equal(
            VoucherReasonCodes.WrongRoute,
            VoucherValidationService.ReasonFor(voucher, Guid.NewGuid(), 1000m, BayGio));
    }

    [Fact]
    public void Duoi_gia_tri_don_toi_thieu()
    {
        var voucher = TaoVoucher(
            VoucherDiscountType.Percent,
            discountValue: 10m,
            minOrderValue: 100000m);

        Assert.Equal(
            VoucherReasonCodes.BelowMinOrder,
            VoucherValidationService.ReasonFor(voucher, null, 99999m, BayGio));
    }

    [Fact]
    public void Dung_bang_gia_tri_don_toi_thieu_thi_dung_duoc()
    {
        var voucher = TaoVoucher(
            VoucherDiscountType.Percent,
            discountValue: 10m,
            minOrderValue: 100000m);

        // "Tối thiểu 100.000đ" là ĐẠT khi đơn đúng 100.000đ — điều kiện là < chứ không phải <=.
        Assert.Null(VoucherValidationService.ReasonFor(voucher, null, 100000m, BayGio));
    }

    // ---------------------------------------------------------------------------------------
    // Hai đầu mút của khoảng hiệu lực — TÍNH CẢ HAI (hợp đồng chốt "tính cả hai đầu mút")
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void Dung_moc_ValidFrom_thi_da_hieu_luc()
    {
        var voucher = TaoVoucher(
            VoucherDiscountType.FixedAmount,
            discountValue: 10000m,
            validFrom: BayGio);

        Assert.Null(VoucherValidationService.ReasonFor(voucher, null, 100000m, BayGio));
    }

    [Fact]
    public void Truoc_moc_ValidFrom_mot_giay_thi_chua_hieu_luc()
    {
        var voucher = TaoVoucher(
            VoucherDiscountType.FixedAmount,
            discountValue: 10000m,
            validFrom: BayGio);

        Assert.Equal(
            VoucherReasonCodes.NotStarted,
            VoucherValidationService.ReasonFor(voucher, null, 100000m, BayGio.AddSeconds(-1)));
    }

    [Fact]
    public void Dung_moc_ValidUntil_van_con_hieu_luc()
    {
        var voucher = TaoVoucher(
            VoucherDiscountType.FixedAmount,
            discountValue: 10000m,
            validUntil: BayGio);

        // Đây là ca dễ viết sai nhất: ValidUntil là mốc CUỐI CÒN dùng được, không phải mốc hết
        // hiệu lực. Điều kiện phải là now > ValidUntil chứ không phải >=.
        Assert.Null(VoucherValidationService.ReasonFor(voucher, null, 100000m, BayGio));
    }

    [Fact]
    public void Sau_moc_ValidUntil_mot_giay_thi_het_hieu_luc()
    {
        var voucher = TaoVoucher(
            VoucherDiscountType.FixedAmount,
            discountValue: 10000m,
            validUntil: BayGio);

        Assert.Equal(
            VoucherReasonCodes.Expired,
            VoucherValidationService.ReasonFor(voucher, null, 100000m, BayGio.AddSeconds(1)));
    }

    // ---------------------------------------------------------------------------------------
    // Điều kiện tuyến của dòng 53 — RouteId null nghĩa là MỌI tuyến
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void Voucher_khong_gan_tuyen_ap_cho_moi_tuyen()
    {
        var voucher = TaoVoucher(VoucherDiscountType.FixedAmount, discountValue: 10000m);
        Assert.Null(voucher.RouteId);

        Assert.Null(VoucherValidationService.ReasonFor(voucher, Guid.NewGuid(), 100000m, BayGio));
    }

    [Fact]
    public void Voucher_gan_tuyen_dung_thi_dung_duoc()
    {
        var tuyen = Guid.NewGuid();
        var voucher = TaoVoucher(VoucherDiscountType.FixedAmount, discountValue: 10000m);
        voucher.RouteId = tuyen;

        Assert.Null(VoucherValidationService.ReasonFor(voucher, tuyen, 100000m, BayGio));
    }

    [Fact]
    public void Voucher_gan_tuyen_khac_thi_bi_tu_choi()
    {
        var voucher = TaoVoucher(VoucherDiscountType.FixedAmount, discountValue: 10000m);
        voucher.RouteId = Guid.NewGuid();

        Assert.Equal(
            VoucherReasonCodes.WrongRoute,
            VoucherValidationService.ReasonFor(voucher, Guid.NewGuid(), 100000m, BayGio));
    }

    // ---------------------------------------------------------------------------------------
    // Chạy thật qua ValidateAsync — tra CSDL, chuẩn hoá mã, hình dạng câu trả lời
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Ma_hop_le_tra_ve_so_tien_duoc_giam_va_so_phai_tra()
    {
        using var db = TaoDb();
        var tuyen = await SeedTuyenAsync(db);
        var chuyen = await SeedChuyenAsync(db, tuyen);
        await SeedVoucherAsync(db, TaoVoucher(
            VoucherDiscountType.Percent,
            discountValue: 20m,
            maxDiscount: 30000m));

        var ketQua = await new VoucherValidationService(db).ValidateAsync(new ValidateVoucherRequest
        {
            Code = "SUMMER10",
            TripId = chuyen,
            OrderAmount = 150000m,
        });

        Assert.True(ketQua.Success);
        var data = ketQua.Data!;

        Assert.True(data.Valid);
        Assert.Null(data.ReasonCode);
        Assert.Null(data.Message);
        Assert.Equal("SUMMER10", data.Code);
        Assert.Equal("Percent", data.DiscountType);
        // 150.000 × 20% = 30.000, đúng bằng trần nên không bị cắt.
        Assert.Equal(30000m, data.DiscountAmount);
        Assert.Equal(120000m, data.FinalAmount);
        Assert.Equal(30000m, data.MaxDiscount);
    }

    [Fact]
    public async Task Khach_go_chu_thuong_van_tra_ra_ma()
    {
        using var db = TaoDb();
        var tuyen = await SeedTuyenAsync(db);
        var chuyen = await SeedChuyenAsync(db, tuyen);
        await SeedVoucherAsync(db, TaoVoucher(VoucherDiscountType.FixedAmount, discountValue: 10000m));

        var ketQua = await new VoucherValidationService(db).ValidateAsync(new ValidateVoucherRequest
        {
            Code = "  summer10  ",
            TripId = chuyen,
            OrderAmount = 100000m,
        });

        var data = ketQua.Data!;

        // Mã trong CSDL là CHỮ HOA; không chuẩn hoá trước khi tra thì khách gõ chữ thường nhận
        // "mã không tồn tại" cho một mã có thật — câu trả lời sai mà không có lỗi nào để lần theo.
        Assert.True(data.Valid);
        Assert.Equal("SUMMER10", data.Code);
        Assert.Equal(10000m, data.DiscountAmount);
    }

    [Fact]
    public async Task Ma_khong_ton_tai_van_la_Ok_kem_valid_false()
    {
        using var db = TaoDb();
        var tuyen = await SeedTuyenAsync(db);
        var chuyen = await SeedChuyenAsync(db, tuyen);

        var ketQua = await new VoucherValidationService(db).ValidateAsync(new ValidateVoucherRequest
        {
            Code = "KHONG-CO",
            TripId = chuyen,
            OrderAmount = 100000m,
        });

        // 🔴 Đây là chốt quan trọng nhất về hình dạng API: thành công ở tầng ServiceResult, thất bại
        // ở tầng nghiệp vụ. Trả NotFound/Invalid ở đây là biến mọi lần khách gõ sai thành lỗi hệ
        // thống ở màn thanh toán.
        Assert.True(ketQua.Success);
        var data = ketQua.Data!;

        Assert.False(data.Valid);
        Assert.Equal(VoucherReasonCodes.NotFound, data.ReasonCode);
        Assert.Null(data.VoucherId);
        Assert.Null(data.DiscountType);
        // FE không phải rẽ nhánh: mã hỏng thì giảm 0 và khách trả đúng tổng tiền.
        Assert.Equal(0m, data.DiscountAmount);
        Assert.Equal(100000m, data.FinalAmount);
    }

    [Fact]
    public async Task Ma_bi_tu_choi_tra_ve_cau_tieng_Viet_va_van_giu_so_tien_goc()
    {
        using var db = TaoDb();
        var tuyen = await SeedTuyenAsync(db);
        var chuyen = await SeedChuyenAsync(db, tuyen);
        await SeedVoucherAsync(db, TaoVoucher(
            VoucherDiscountType.Percent,
            discountValue: 10m,
            minOrderValue: 200000m));

        var ketQua = await new VoucherValidationService(db).ValidateAsync(new ValidateVoucherRequest
        {
            Code = "SUMMER10",
            TripId = chuyen,
            OrderAmount = 150000m,
        });

        var data = ketQua.Data!;

        Assert.False(data.Valid);
        Assert.Equal(VoucherReasonCodes.BelowMinOrder, data.ReasonCode);
        Assert.Equal("Đơn hàng chưa đạt giá trị tối thiểu 200.000đ", data.Message);
        Assert.Equal(0m, data.DiscountAmount);
        Assert.Equal(150000m, data.FinalAmount);
        // Voucher CÓ tồn tại — mã và id vẫn trả về để FE hiện được "mã SUMMER10 chưa dùng được vì…".
        Assert.Equal("SUMMER10", data.Code);
        Assert.NotNull(data.VoucherId);
    }

    [Fact]
    public async Task Chuyen_khong_ton_tai_tra_NotFound_chu_khong_phai_valid_false()
    {
        using var db = TaoDb();

        var ketQua = await new VoucherValidationService(db).ValidateAsync(new ValidateVoucherRequest
        {
            Code = "SUMMER10",
            TripId = Guid.NewGuid(),
            OrderAmount = 100000m,
        });

        // Chuyến không tồn tại là lỗi phía GỌI — khác hẳn "voucher bị từ chối". Lẫn hai ca này là
        // FE hiện "mã không dùng được" cho một request sai chuyến.
        Assert.False(ketQua.Success);
        Assert.Equal(ServiceErrorKind.NotFound, ketQua.ErrorKind);
        Assert.Equal("Không tìm thấy chuyến xe", ketQua.Error);
    }

    [Fact]
    public async Task Voucher_cua_tuyen_khac_bi_tu_choi_qua_duong_that()
    {
        using var db = TaoDb();
        var tuyenCuaChuyen = await SeedTuyenAsync(db);
        var tuyenKhac = await SeedTuyenAsync(db);
        var chuyen = await SeedChuyenAsync(db, tuyenCuaChuyen);

        var voucher = TaoVoucher(VoucherDiscountType.FixedAmount, discountValue: 10000m);
        voucher.RouteId = tuyenKhac;
        await SeedVoucherAsync(db, voucher);

        var ketQua = await new VoucherValidationService(db).ValidateAsync(new ValidateVoucherRequest
        {
            Code = "SUMMER10",
            TripId = chuyen,
            OrderAmount = 100000m,
        });

        Assert.True(ketQua.Success);
        Assert.Equal(VoucherReasonCodes.WrongRoute, ketQua.Data!.ReasonCode);
    }

    [Fact]
    public async Task Kiem_tra_khong_ghi_gi_xuong_csdl()
    {
        using var db = TaoDb();
        var tuyen = await SeedTuyenAsync(db);
        var chuyen = await SeedChuyenAsync(db, tuyen);
        await SeedVoucherAsync(db, TaoVoucher(VoucherDiscountType.FixedAmount, discountValue: 10000m));

        var service = new VoucherValidationService(db);

        await service.ValidateAsync(new ValidateVoucherRequest
        {
            Code = "SUMMER10",
            TripId = chuyen,
            OrderAmount = 100000m,
        });
        await service.ValidateAsync(new ValidateVoucherRequest
        {
            Code = "SUMMER10",
            TripId = chuyen,
            OrderAmount = 100000m,
        });

        // Endpoint này bị gọi mỗi lần khách gõ thêm một ký tự (debounce) — nó phải là phép ĐỌC thuần.
        // Gọi hai lần mà UsedCount nhích lên là màn thanh toán tự tiêu thụ sạch voucher của khách.
        Assert.Equal(0, await db.Vouchers.Select(v => v.UsedCount).SingleAsync());
    }

    // ---------------------------------------------------------------------------------------
    // Helper — mỗi lớp test tự chép, không có lớp base chung (đúng lệ đang có của dự án)
    // ---------------------------------------------------------------------------------------

    private AppDbContext TaoDb() => new(
        new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(_tenCsdl)
            .Options);

    /// <summary>
    /// Voucher hợp lệ quanh mốc <see cref="BayGio"/>: đang Active, còn lượt, không gắn tuyến, không
    /// yêu cầu giá trị đơn tối thiểu. Ca nào cần khác thì truyền tường minh — nhờ vậy mỗi test chỉ
    /// đọc ra đúng MỘT điều kiện nó đang kiểm.
    /// </summary>
    private static Voucher TaoVoucher(
        VoucherDiscountType discountType,
        decimal discountValue,
        decimal? maxDiscount = null,
        decimal minOrderValue = 0m,
        VoucherStatus status = VoucherStatus.Active,
        DateTime? validFrom = null,
        DateTime? validUntil = null,
        int quantity = 100,
        int usedCount = 0,
        string code = "SUMMER10")
        => new()
        {
            Code = code,
            Name = "Voucher kiểm thử",
            DiscountType = discountType,
            DiscountValue = discountValue,
            MaxDiscount = maxDiscount,
            MinOrderValue = minOrderValue,
            Status = status,
            ValidFrom = validFrom ?? TuNgay,
            ValidUntil = validUntil ?? DenNgay,
            Quantity = quantity,
            UsedCount = usedCount,
            CreatedAt = TuNgay,
        };

    private static async Task<Guid> SeedTuyenAsync(AppDbContext db)
    {
        var tuyen = new Route
        {
            // Mã tuyến duy nhất theo từng lượt seed: Route.Code là duy nhất toàn hệ thống, và ca
            // "voucher của tuyến khác" cần HAI tuyến phân biệt được trong cùng một CSDL.
            Code = $"TUYEN-{Guid.NewGuid().ToString("N")[..8]}",
            Name = "Tuyến kiểm thử",
            CreatedAt = TuNgay,
        };

        db.Routes.Add(tuyen);
        await db.SaveChangesAsync();

        return tuyen.Id;
    }

    private static async Task<Guid> SeedChuyenAsync(AppDbContext db, Guid routeId)
    {
        var chuyen = new Trip
        {
            RouteId = routeId,
            BusId = Guid.NewGuid(),
            DepartureTime = TuNgay,
            CreatedAt = TuNgay,
        };

        db.Trips.Add(chuyen);
        await db.SaveChangesAsync();

        return chuyen.Id;
    }

    private static async Task SeedVoucherAsync(AppDbContext db, Voucher voucher)
    {
        db.Vouchers.Add(voucher);
        await db.SaveChangesAsync();
    }
}
