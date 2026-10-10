using Microsoft.EntityFrameworkCore;
using SmartBus.Api.Data;
using SmartBus.Api.Dtos.Vouchers;
using SmartBus.Api.Entities;
using SmartBus.Api.Services;

namespace SmartBus.Tests;

/// <summary>
/// Test phần "áp dụng" voucher — <see cref="VoucherRedemptionService"/> (dòng 52, US 18, Nguyễn Duy
/// Kiên). Đây là service mà luồng thanh toán gọi khi tiền đã về.
///
/// Gọi thẳng service trên InMemory, KHÔNG dựng host: đây không phải endpoint, người gọi duy nhất là
/// nhánh thành công của callback cổng và của job đối soát.
///
/// ⚠️ <b>HAI CHỐT CỦA FILE NÀY CHỈ SỐNG Ở POSTGRESQL, InMemory KHÔNG CHỨNG MINH ĐƯỢC.</b>
///   • Unique index <c>VoucherUsage.PaymentCode</c> — chốt idempotency ở tầng CSDL. Nhánh
///     <c>catch (DbUpdateException)</c> trong service (đọc lại bản ghi của bên thắng) KHÔNG có ca test
///     nào phủ ở đây. Phần được phủ là phép kiểm ở đầu hàm — nó chặn ca tuần tự, còn ca đua nhau thì
///     không.
///   • Concurrency token <c>xmin</c> của <c>Vouchers</c> — chốt chống tiêu thụ quá <c>Quantity</c>
///     khi hai lượt chạy song song. Nhánh <c>catch (DbUpdateConcurrencyException)</c> cũng vậy.
/// Vì thế file này KHÔNG có ca "nhiều lượt song song" theo nghĩa đua thật: viết một ca như vậy trên
/// InMemory sẽ XANH mà không chứng minh gì — đúng lối ghi chú đã có ở mục "Giữ chỗ" của hợp đồng.
/// Đua thật phải kiểm trên PostgreSQL, và việc đó thuộc về Dăm sau khi migration chạy (docs/27).
/// </summary>
public class VoucherRedemptionServiceTests
{
    private static readonly DateTime TuNgay = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime DenNgay = new(2026, 10, 31, 23, 59, 59, DateTimeKind.Utc);

    private readonly string _tenCsdl = $"voucher-ap-dung-{Guid.NewGuid()}";

    // ---------------------------------------------------------------------------------------
    // Áp dụng thành công
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Ap_dung_thanh_cong_thi_tang_UsedCount_dung_mot_va_ghi_mot_dong_usage()
    {
        using var db = TaoDb();
        var (chuyen, voucher) = await SeedSanAsync(db, quantity: 10);
        var userId = Guid.NewGuid();

        var ketQua = await new VoucherRedemptionService(db).RedeemAsync(new VoucherRedemptionRequest
        {
            PaymentCode = "PAY-0001",
            VoucherCode = "summer10",
            UserId = userId,
            TripId = chuyen,
            OrderAmount = 175000m,
        });

        Assert.True(ketQua.Success);
        var data = ketQua.Data!;

        Assert.False(data.AlreadyRedeemed);
        Assert.Equal(voucher.Id, data.VoucherId);
        // Mã trả về là dạng CHUẨN đã lưu, không phải chuỗi thô khách gửi lên.
        Assert.Equal("SUMMER10", data.VoucherCode);
        Assert.Equal(175000m, data.OrderAmount);
        // 175.000 × 10% = 17.500 — cùng con số mà VoucherValidationServiceTests đã ghim.
        Assert.Equal(17500m, data.DiscountAmount);

        // Đọc lại bằng context KHÁC để chắc chắn thay đổi đã xuống CSDL, không chỉ nằm trong change
        // tracker của context vừa dùng.
        using var dbDoc = TaoDb();

        Assert.Equal(1, await dbDoc.Vouchers.Where(v => v.Id == voucher.Id).Select(v => v.UsedCount).SingleAsync());

        var usage = await dbDoc.VoucherUsages.SingleAsync();
        Assert.Equal(voucher.Id, usage.VoucherId);
        Assert.Equal(userId, usage.UserId);
        Assert.Equal("PAY-0001", usage.PaymentCode);
        Assert.Equal(175000m, usage.OrderAmount);
        Assert.Equal(17500m, usage.DiscountAmount);
    }

    [Fact]
    public async Task So_tien_ghi_vao_usage_bang_dung_so_tien_luot_kiem_tra_da_tra_ve()
    {
        using var db = TaoDb();
        var (chuyen, _) = await SeedSanAsync(db);

        // Hai service dùng CHUNG hàm DiscountFor, nên hai con số phải bằng nhau TUYỆT ĐỐI. Lệch nhau
        // nghĩa là số tiền khách nhìn thấy lúc bấm nút khác số tiền ghi vào hoá đơn — và ca này đổ
        // ngay khi ai đó chép công thức sang nhánh tiêu thụ thay vì gọi chung.
        var xemTruoc = await new VoucherValidationService(db).ValidateAsync(new ValidateVoucherRequest
        {
            Code = "SUMMER10",
            TripId = chuyen,
            OrderAmount = 33333m,
        });

        var apDung = await new VoucherRedemptionService(db).RedeemAsync(new VoucherRedemptionRequest
        {
            PaymentCode = "PAY-LECH",
            VoucherCode = "SUMMER10",
            UserId = Guid.NewGuid(),
            TripId = chuyen,
            OrderAmount = 33333m,
        });

        Assert.Equal(xemTruoc.Data!.DiscountAmount, apDung.Data!.DiscountAmount);
        // 33.333 × 10% = 3.333,3 → 3.333 (đồng nguyên, xem chú thích của DiscountFor).
        Assert.Equal(3333m, apDung.Data.DiscountAmount);
    }

    // ---------------------------------------------------------------------------------------
    // 🔴 Idempotency theo PaymentCode — chốt quan trọng nhất của phần "áp dụng"
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Goi_lai_cung_PaymentCode_khong_tang_them_luot_nao()
    {
        using var db = TaoDb();
        var (chuyen, voucher) = await SeedSanAsync(db, quantity: 10);
        var service = new VoucherRedemptionService(db);

        var request = new VoucherRedemptionRequest
        {
            PaymentCode = "PAY-0001",
            VoucherCode = "SUMMER10",
            UserId = Guid.NewGuid(),
            TripId = chuyen,
            OrderAmount = 175000m,
        };

        var lanDau = await service.RedeemAsync(request);

        // Cổng gửi lại callback (MoMo retry tới khi nhận 204) hoặc job đối soát chạy đè.
        var lanHai = await service.RedeemAsync(request);
        var lanBa = await service.RedeemAsync(request);

        Assert.True(lanDau.Success);
        Assert.True(lanHai.Success);
        Assert.True(lanBa.Success);

        Assert.False(lanDau.Data!.AlreadyRedeemed);
        Assert.True(lanHai.Data!.AlreadyRedeemed);
        Assert.True(lanBa.Data!.AlreadyRedeemed);

        // Bản gửi lại trả ĐÚNG số tiền đã chốt, không tính lại rồi trả con số khác.
        Assert.Equal(lanDau.Data.DiscountAmount, lanHai.Data.DiscountAmount);
        Assert.Equal(lanDau.Data.VoucherCode, lanHai.Data.VoucherCode);

        using var dbDoc = TaoDb();

        Assert.Equal(1, await dbDoc.Vouchers.Where(v => v.Id == voucher.Id).Select(v => v.UsedCount).SingleAsync());
        Assert.Equal(1, await dbDoc.VoucherUsages.CountAsync());
    }

    [Fact]
    public async Task Ban_gui_lai_thieu_VoucherCode_van_tra_dung_ban_ghi_da_co()
    {
        using var db = TaoDb();
        var (chuyen, _) = await SeedSanAsync(db);
        var service = new VoucherRedemptionService(db);

        await service.RedeemAsync(new VoucherRedemptionRequest
        {
            PaymentCode = "PAY-0001",
            VoucherCode = "SUMMER10",
            UserId = Guid.NewGuid(),
            TripId = chuyen,
            OrderAmount = 175000m,
        });

        // Job đối soát dựng lại request từ bản ghi Payment, mà Payment KHÔNG có cột voucherCode (xem
        // hợp đồng) — nên lượt gọi lại có thể tới với mã rỗng. Câu trả lời đúng nằm ở dòng đã ghi,
        // không phải ở tham số của lượt gọi mới, nên phép kiểm idempotency phải đứng TRƯỚC phép kiểm
        // "có mã không".
        var lai = await service.RedeemAsync(new VoucherRedemptionRequest
        {
            PaymentCode = "PAY-0001",
            VoucherCode = string.Empty,
            UserId = Guid.NewGuid(),
            TripId = chuyen,
            OrderAmount = 175000m,
        });

        Assert.True(lai.Success);
        Assert.True(lai.Data!.AlreadyRedeemed);
        Assert.Equal("SUMMER10", lai.Data.VoucherCode);
        Assert.Equal(17500m, lai.Data.DiscountAmount);
    }

    [Fact]
    public async Task Hai_giao_dich_khac_nhau_dung_cung_voucher_thi_tang_hai_luot()
    {
        using var db = TaoDb();
        var (chuyen, voucher) = await SeedSanAsync(db, quantity: 10);
        var service = new VoucherRedemptionService(db);

        var lanMot = await service.RedeemAsync(new VoucherRedemptionRequest
        {
            PaymentCode = "PAY-0001",
            VoucherCode = "SUMMER10",
            UserId = Guid.NewGuid(),
            TripId = chuyen,
            OrderAmount = 100000m,
        });

        var lanHai = await service.RedeemAsync(new VoucherRedemptionRequest
        {
            PaymentCode = "PAY-0002",
            VoucherCode = "SUMMER10",
            UserId = Guid.NewGuid(),
            TripId = chuyen,
            OrderAmount = 200000m,
        });

        // Idempotency theo PaymentCode KHÔNG được biến thành "một voucher chỉ dùng được một lần":
        // hai giao dịch khác nhau là hai lượt tiêu thụ thật.
        Assert.False(lanMot.Data!.AlreadyRedeemed);
        Assert.False(lanHai.Data!.AlreadyRedeemed);
        Assert.Equal(10000m, lanMot.Data.DiscountAmount);
        Assert.Equal(20000m, lanHai.Data.DiscountAmount);

        using var dbDoc = TaoDb();

        Assert.Equal(2, await dbDoc.Vouchers.Where(v => v.Id == voucher.Id).Select(v => v.UsedCount).SingleAsync());
        Assert.Equal(2, await dbDoc.VoucherUsages.CountAsync());
    }

    // ---------------------------------------------------------------------------------------
    // Giao dịch không dùng mã — nhánh để người gọi gọi được VÔ ĐIỀU KIỆN
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Khong_co_ma_voucher_thi_thanh_cong_rong_va_khong_ghi_gi()
    {
        using var db = TaoDb();
        var (chuyen, _) = await SeedSanAsync(db);

        var ketQua = await new VoucherRedemptionService(db).RedeemAsync(new VoucherRedemptionRequest
        {
            PaymentCode = "PAY-KHONG-MA",
            VoucherCode = string.Empty,
            UserId = Guid.NewGuid(),
            TripId = chuyen,
            OrderAmount = 175000m,
        });

        // Nhờ nhánh này, luồng thanh toán gọi RedeemAsync vô điều kiện — không phải tự nhớ "chỉ gọi
        // khi có voucherCode". Một điều kiện nhớ hộ là một chỗ để quên.
        Assert.True(ketQua.Success);
        Assert.False(ketQua.Data!.AlreadyRedeemed);
        Assert.Equal(0m, ketQua.Data.DiscountAmount);
        Assert.Equal(175000m, ketQua.Data.OrderAmount);

        Assert.Empty(db.VoucherUsages);

        using var dbDoc = TaoDb();
        Assert.Equal(0, await dbDoc.Vouchers.Select(v => v.UsedCount).SingleAsync());
    }

    [Fact]
    public async Task VoucherCode_toan_khoang_trang_cung_la_khong_dung_ma()
    {
        using var db = TaoDb();
        var (chuyen, _) = await SeedSanAsync(db);

        // Chuỗi khoảng trắng lọt qua IsNullOrWhiteSpace ở tầng gọi này là mã rỗng sau khi Trim() —
        // không được biến thành "không tìm thấy voucher SUMMER10".
        var ketQua = await new VoucherRedemptionService(db).RedeemAsync(new VoucherRedemptionRequest
        {
            PaymentCode = "PAY-KHONG-MA",
            VoucherCode = "   ",
            UserId = Guid.NewGuid(),
            TripId = chuyen,
            OrderAmount = 175000m,
        });

        Assert.True(ketQua.Success);
        Assert.Equal(0m, ketQua.Data!.DiscountAmount);
        Assert.Empty(db.VoucherUsages);
    }

    // ---------------------------------------------------------------------------------------
    // Thiếu PaymentCode — lỗi lập trình ở phía gọi, không phải ca nghiệp vụ
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Thieu_PaymentCode_thi_tra_Invalid_va_khong_ghi_gi()
    {
        using var db = TaoDb();
        var (chuyen, _) = await SeedSanAsync(db);

        var ketQua = await new VoucherRedemptionService(db).RedeemAsync(new VoucherRedemptionRequest
        {
            PaymentCode = "   ",
            VoucherCode = "SUMMER10",
            UserId = Guid.NewGuid(),
            TripId = chuyen,
            OrderAmount = 175000m,
        });

        // Không có mã giao dịch thì không có chốt idempotency: lượt gọi này không phân biệt được
        // "lần đầu" với "bản gửi lại", nên cho qua là mở đường trừ hai lần.
        Assert.False(ketQua.Success);
        Assert.Equal(ServiceErrorKind.Invalid, ketQua.ErrorKind);
        Assert.Equal(["Mã giao dịch không được để trống"], ketQua.Errors!["paymentCode"]);

        Assert.Empty(db.VoucherUsages);
    }

    // ---------------------------------------------------------------------------------------
    // Bị từ chối — và điều quan trọng: KHÔNG có gì được ghi
    // ---------------------------------------------------------------------------------------

    [Theory]
    [InlineData(VoucherStatus.Inactive, 100, 0, "Mã voucher đã ngừng áp dụng")]
    [InlineData(VoucherStatus.Active, 5, 5, "Mã voucher đã hết lượt sử dụng")]
    public async Task Voucher_khong_dung_duoc_thi_tra_Conflict_va_khong_ghi_gi(
        VoucherStatus trangThai,
        int quantity,
        int usedCount,
        string cauMongDoi)
    {
        using var db = TaoDb();
        var (chuyen, voucher) = await SeedSanAsync(db, quantity: quantity, usedCount: usedCount, status: trangThai);

        var ketQua = await new VoucherRedemptionService(db).RedeemAsync(new VoucherRedemptionRequest
        {
            PaymentCode = "PAY-0001",
            VoucherCode = "SUMMER10",
            UserId = Guid.NewGuid(),
            TripId = chuyen,
            OrderAmount = 175000m,
        });

        Assert.False(ketQua.Success);
        Assert.Equal(ServiceErrorKind.Conflict, ketQua.ErrorKind);
        Assert.Equal(cauMongDoi, ketQua.Error);

        // Tiền đã về nhưng mã không áp được: người gọi ghi log rồi vẫn trả 204 cho cổng. Điều KHÔNG
        // được xảy ra là ghi một dòng usage cho lượt bị từ chối — hoá đơn sẽ ghi giảm một khoản
        // chưa từng được giảm.
        Assert.Empty(db.VoucherUsages);

        using var dbDoc = TaoDb();
        Assert.Equal(usedCount, await dbDoc.Vouchers.Where(v => v.Id == voucher.Id).Select(v => v.UsedCount).SingleAsync());
    }

    [Fact]
    public async Task Voucher_cua_tuyen_khac_bi_tu_choi()
    {
        using var db = TaoDb();

        // KHÔNG dùng SeedSanAsync ở đây: nó đã seed sẵn một voucher "SUMMER10" không gắn tuyến, và
        // tra theo mã sẽ vớ phải chính nó — ca này cần voucher gắn tuyến KHÁC là mã duy nhất có.
        var chuyen = await SeedChuyenAsync(db);

        var voucher = TaoVoucher(quantity: 10);
        voucher.RouteId = Guid.NewGuid();
        db.Vouchers.Add(voucher);
        await db.SaveChangesAsync();

        var ketQua = await new VoucherRedemptionService(db).RedeemAsync(new VoucherRedemptionRequest
        {
            PaymentCode = "PAY-0001",
            VoucherCode = "SUMMER10",
            UserId = Guid.NewGuid(),
            TripId = chuyen,
            OrderAmount = 175000m,
        });

        Assert.False(ketQua.Success);
        Assert.Equal("Mã voucher không áp dụng cho tuyến này", ketQua.Error);
        Assert.Empty(db.VoucherUsages);
    }

    [Fact]
    public async Task Ma_khong_ton_tai_thi_tra_NotFound()
    {
        using var db = TaoDb();
        var (chuyen, _) = await SeedSanAsync(db);

        var ketQua = await new VoucherRedemptionService(db).RedeemAsync(new VoucherRedemptionRequest
        {
            PaymentCode = "PAY-0001",
            VoucherCode = "KHONG-CO",
            UserId = Guid.NewGuid(),
            TripId = chuyen,
            OrderAmount = 175000m,
        });

        Assert.False(ketQua.Success);
        Assert.Equal(ServiceErrorKind.NotFound, ketQua.ErrorKind);
        Assert.Equal("Mã voucher không tồn tại", ketQua.Error);
        Assert.Empty(db.VoucherUsages);
    }

    [Fact]
    public async Task Chuyen_khong_ton_tai_thi_tra_NotFound()
    {
        using var db = TaoDb();
        await SeedSanAsync(db);

        var ketQua = await new VoucherRedemptionService(db).RedeemAsync(new VoucherRedemptionRequest
        {
            PaymentCode = "PAY-0001",
            VoucherCode = "SUMMER10",
            UserId = Guid.NewGuid(),
            TripId = Guid.NewGuid(),
            OrderAmount = 175000m,
        });

        Assert.False(ketQua.Success);
        Assert.Equal(ServiceErrorKind.NotFound, ketQua.ErrorKind);
        Assert.Equal("Không tìm thấy chuyến xe", ketQua.Error);
    }

    [Fact]
    public async Task Luot_bi_tu_choi_khong_chan_giao_dich_sau()
    {
        using var db = TaoDb();
        var (chuyen, voucher) = await SeedSanAsync(db, quantity: 1, usedCount: 1);
        var service = new VoucherRedemptionService(db);

        var biTuChoi = await service.RedeemAsync(new VoucherRedemptionRequest
        {
            PaymentCode = "PAY-0001",
            VoucherCode = "SUMMER10",
            UserId = Guid.NewGuid(),
            TripId = chuyen,
            OrderAmount = 175000m,
        });

        Assert.False(biTuChoi.Success);

        // Lượt bị từ chối không được để lại mã giao dịch nào đã "chiếm chỗ": nếu nhánh từ chối có ghi
        // một dòng usage rỗng, giao dịch đó về sau có được cấp thêm lượt cũng không bao giờ áp được
        // mã nữa — và tệ hơn, UsedCount đã nhích mà tiền thì chưa giảm.
        using var dbDoc = TaoDb();
        Assert.Empty(dbDoc.VoucherUsages);
        Assert.Equal(1, await dbDoc.Vouchers.Where(v => v.Id == voucher.Id).Select(v => v.UsedCount).SingleAsync());
    }

    // ---------------------------------------------------------------------------------------
    // Điều kiện thời gian vẫn được kiểm LẠI ở đây, không tin kết quả validate trước đó
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Voucher_het_han_giua_luc_kiem_tra_va_luc_tra_tien_thi_bi_tu_choi()
    {
        using var db = TaoDb();
        var chuyen = await SeedChuyenAsync(db);

        // Mã còn hiệu lực lúc khách bấm nút, nhưng đã hết hạn lúc tiền về — service phải tự kiểm lại
        // trên đồng hồ hiện tại chứ không tin kết quả validate mà khách đã thấy.
        var voucher = TaoVoucher(quantity: 10);
        voucher.ValidUntil = DateTime.UtcNow.AddSeconds(-1);
        db.Vouchers.Add(voucher);
        await db.SaveChangesAsync();

        var ketQua = await new VoucherRedemptionService(db).RedeemAsync(new VoucherRedemptionRequest
        {
            PaymentCode = "PAY-0001",
            VoucherCode = "SUMMER10",
            UserId = Guid.NewGuid(),
            TripId = chuyen,
            OrderAmount = 175000m,
        });

        Assert.False(ketQua.Success);
        Assert.Equal("Mã voucher đã hết hiệu lực", ketQua.Error);
        Assert.Empty(db.VoucherUsages);
    }

    [Fact]
    public async Task Giao_dich_duoi_gia_tri_don_toi_thieu_bi_tu_choi()
    {
        using var db = TaoDb();
        var chuyen = await SeedChuyenAsync(db);

        var voucher = TaoVoucher(quantity: 10);
        voucher.MinOrderValue = 200000m;
        db.Vouchers.Add(voucher);
        await db.SaveChangesAsync();

        var ketQua = await new VoucherRedemptionService(db).RedeemAsync(new VoucherRedemptionRequest
        {
            PaymentCode = "PAY-0001",
            VoucherCode = "SUMMER10",
            UserId = Guid.NewGuid(),
            TripId = chuyen,
            OrderAmount = 150000m,
        });

        Assert.False(ketQua.Success);
        Assert.Equal("Đơn hàng chưa đạt giá trị tối thiểu 200.000đ", ketQua.Error);
    }

    [Fact]
    public async Task Giao_dich_dung_bang_gia_tri_don_toi_thieu_thi_ap_duoc()
    {
        using var db = TaoDb();
        var chuyen = await SeedChuyenAsync(db);

        var voucher = TaoVoucher(quantity: 10);
        voucher.MinOrderValue = 100000m;
        db.Vouchers.Add(voucher);
        await db.SaveChangesAsync();

        var ketQua = await new VoucherRedemptionService(db).RedeemAsync(new VoucherRedemptionRequest
        {
            PaymentCode = "PAY-0001",
            VoucherCode = "SUMMER10",
            UserId = Guid.NewGuid(),
            TripId = chuyen,
            OrderAmount = 100000m,
        });

        // Cùng ca biên với lượt kiểm tra, nhưng ở đây hậu quả nặng hơn: từ chối nhầm một giao dịch
        // ĐÃ thu tiền nghĩa là khách bị trừ tiền mà không được giảm.
        Assert.True(ketQua.Success);
        Assert.Equal(10000m, ketQua.Data!.DiscountAmount);
    }

    // ---------------------------------------------------------------------------------------
    // Helper — mỗi lớp test tự chép, không có lớp base chung (đúng lệ đang có của dự án)
    // ---------------------------------------------------------------------------------------

    private AppDbContext TaoDb() => new(
        new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(_tenCsdl)
            .Options);

    private static Voucher TaoVoucher(
        int quantity = 100,
        int usedCount = 0,
        VoucherStatus status = VoucherStatus.Active)
        => new()
        {
            Code = "SUMMER10",
            Name = "Giảm 10%",
            DiscountType = VoucherDiscountType.Percent,
            DiscountValue = 10m,
            MinOrderValue = 0m,
            Status = status,
            ValidFrom = TuNgay,
            ValidUntil = DenNgay,
            Quantity = quantity,
            UsedCount = usedCount,
            CreatedAt = TuNgay,
        };

    /// <summary>
    /// Một chuyến + một voucher dùng được cho chuyến đó. Không seed Route/Bus thật: InMemory không
    /// cưỡng chế khoá ngoại, mà service chỉ đọc đúng <c>Trip.RouteId</c>.
    /// </summary>
    private static async Task<(Guid Chuyen, Voucher Voucher)> SeedSanAsync(
        AppDbContext db,
        int quantity = 100,
        int usedCount = 0,
        VoucherStatus status = VoucherStatus.Active)
    {
        var chuyen = await SeedChuyenAsync(db);

        var voucher = TaoVoucher(quantity, usedCount, status);
        db.Vouchers.Add(voucher);
        await db.SaveChangesAsync();

        return (chuyen, voucher);
    }

    private static async Task<Guid> SeedChuyenAsync(AppDbContext db)
    {
        var chuyen = new Trip
        {
            RouteId = Guid.NewGuid(),
            BusId = Guid.NewGuid(),
            DepartureTime = TuNgay,
            CreatedAt = TuNgay,
        };

        db.Trips.Add(chuyen);
        await db.SaveChangesAsync();

        return chuyen.Id;
    }
}
