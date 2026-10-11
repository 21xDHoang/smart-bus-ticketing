using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using SmartBus.Api.Data;
using SmartBus.Api.Entities;
using UserEntity = SmartBus.Api.Entities.User;

namespace SmartBus.Tests;

/// <summary>
/// Test tích hợp cho <b>bảy điều kiện chặn</b> của <c>POST /api/vouchers/validate</c> (US 18 —
/// dòng 59, *"Test API áp dụng voucher và các điều kiện chặn (xUnit)"*).
///
/// <para>
/// <b>Vì sao có file riêng thay vì thêm vào <see cref="VoucherValidateApiTests"/>?</b> File đó là của
/// Nguyễn Duy Kiên (dòng 52 + 53) — quy ước 2.4 cấm tôi sửa file của người khác. Đây là file MỚI,
/// không thay thế file nào.
/// </para>
///
/// <para>
/// <b>Ba thứ file này thêm:</b>
/// </para>
/// <list type="number">
/// <item>
/// <b>Năm chuỗi <c>reasonCode</c> chưa từng được ghim ở đâu.</b> Trên dây của endpoint này mới chỉ
/// <c>"NotFound"</c> (<c>VoucherValidateApiTests.cs:159</c>) và <c>"Expired"</c> (<c>:188</c>) được
/// ghim bằng chuỗi thật; năm mã còn lại chỉ được so ở tầng service với <b>chính hằng số của chúng</b>
/// (<c>VoucherReasonCodes.OutOfStock</c> v.v.) — phép so hằng-với-chính-nó luôn xanh, kể cả khi ai đó
/// đổi giá trị chuỗi. (Hai chỗ khác trong bộ test cũng chứa chuỗi <c>"Inactive"</c>, nhưng đó là
/// trạng thái điểm dừng của tuyến — không liên quan voucher.) Câu <c>message</c>
/// <c>"Mã voucher chưa tới thời gian áp dụng"</c> cũng chưa được ghim ở đâu cả. Hợp đồng viết rõ FE
/// <b>dịch theo <c>reasonCode</c></b>, nên đây là bề mặt công khai — đổi là đổi API (⛔5).
/// </item>
/// <item>
/// <b>Thứ tự kiểm tra trên DÂY.</b> Ở tầng hàm thuần <c>ReasonFor(...)</c> thứ tự đã được ghim khá
/// đủ bằng các ca ghép đôi — <c>Khong_tim_thay_voucher_la_ly_do_dau_tien</c>,
/// <c>Voucher_da_tat_thang_moi_ly_do_khac</c>, <c>Chua_toi_ngay_hieu_luc_dung_truoc_het_han</c>,
/// <c>Het_luot_dung_truoc_sai_tuyen_va_duoi_gia_toi_thieu</c>,
/// <c>Sai_tuyen_dung_truoc_duoi_gia_toi_thieu</c>. File này KHÔNG thay chúng: nó chạy cả sáu bậc qua
/// pipeline thật trong một mạch, và thêm bậc cuối "không vi phạm gì" để một service luôn trả về mã
/// từ chối nào đó không thể làm sáu bậc trên xanh.
/// </item>
/// <item>
/// <b>Biên của bộ kiểm tra dữ liệu vào</b> — chưa ca nào ở tầng HTTP phủ: độ dài <c>code</c> 1 / 20 /
/// 21, <c>orderAmount</c> bằng 0, đúng trần <c>numeric(12,2)</c>, vượt trần, và bỏ hẳn
/// <c>orderAmount</c>.
/// </item>
/// </list>
///
/// <para>
/// <b>Và KHÔNG hứa gì thêm.</b> Năm câu <c>message</c> còn lại (Inactive, OutOfStock, WrongRoute,
/// Expired, BelowMinOrder) <b>đã</b> được ghim — chỉ là ở đường khác: <c>VoucherRedemptionServiceTests</c>
/// và <c>VoucherValidationServiceTests</c>, qua <c>VoucherValidationService.MessageFor</c> mà
/// <c>VoucherRedemptionService</c> dùng chung (<c>VoucherRedemptionService.cs:124</c>). Kể cả việc
/// định dạng tiền theo <c>vi-VN</c> cũng đã chạy qua đường đó rồi (<c>"…tối thiểu 200.000đ"</c>). Việc
/// của file này ở nhóm ấy là ghim chúng trên <b>đúng dây của endpoint validate</b>, cùng hình dạng đầy
/// đủ của một response bị chặn khi voucher <b>CÓ thật</b> (<c>discountType</c>/<c>code</c>/<c>voucherId</c>):
/// ca <c>NotFound</c> của file kia chỉ với tới nhánh voucher KHÔNG tồn tại, nơi mọi trường đều null.
/// </para>
///
/// <para>
/// 🔴 Chuỗi trong file này cố ý viết <b>thẳng bằng chữ</b>, KHÔNG dùng <c>VoucherReasonCodes.X</c>.
/// Xin đừng "dọn gọn" chúng thành hằng số: làm vậy là trả lại đúng khoảng trống mà file này sinh ra
/// để bịt.
/// </para>
///
/// <para>
/// ⚠️ <b>Cố ý KHÔNG phủ ở đây:</b> hai đầu mút hiệu lực <c>&gt;= ValidFrom</c> và
/// <c>&lt;= ValidUntil</c> (hợp đồng chốt cả hai đầu mút đều TÍNH LÀ trong hiệu lực). Lý do là kỹ
/// thuật: service đọc <c>DateTime.UtcNow</c> bên trong nó, nên một voucher đặt mốc đúng bằng thời điểm
/// test chạy sẽ hoặc qua hoặc không qua tuỳ vài mili-giây — test sẽ chập chờn. Đầu mút chỉ ghim được ở
/// hàm thuần <c>ReasonFor(...)</c> (nó nhận <c>now</c> làm tham số), và <b>đã được ghim ở đó</b>:
/// <c>Dung_moc_ValidFrom_thi_da_hieu_luc</c>, <c>Dung_moc_ValidUntil_van_con_hieu_luc</c>,
/// <c>Dung_bang_gia_tri_don_toi_thieu_thi_dung_duoc</c>.
/// </para>
/// </summary>
public class VoucherBlockingConditionsApiTests
{
    private const string ValidateUrl = "/api/vouchers/validate";

    /// <summary>Câu báo lỗi của bộ kiểm tra dữ liệu vào — hai attribute trong <c>ValidateVoucherRequest</c>.</summary>
    private const string LoiCodeSaiDoDai = "Mã voucher phải từ 2 đến 20 ký tự";
    private const string LoiOrderAmount = "Giá trị đơn hàng không hợp lệ";

    /// <summary>Đơn hàng dùng chung: lớn hơn mọi mức tối thiểu nhỏ, nhỏ hơn mọi mức tối thiểu lớn.</summary>
    private const decimal DonHang = 150_000m;

    // ---------------------------------------------------------------------------------------
    // 1. BỐN mã từ chối chưa từng được ghim qua HTTP (Expired đã có ở file của Kiên)
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Ma_bi_tat_thi_tra_200_kem_reasonCode_Inactive()
    {
        using var factory = new TestAppFactory();
        var user = await SeedUserAsync(factory);
        var trip = await SeedTripAsync(factory);
        await SeedVoucherAsync(factory, VoucherHopLe("SAPNGUNG", v => v.Status = VoucherStatus.Inactive));

        var body = await ValidateAsync(factory, user, "SAPNGUNG", trip.Id, DonHang);

        Assert.False(body.GetProperty("valid").GetBoolean());
        Assert.Equal("Inactive", body.GetProperty("reasonCode").GetString());
        Assert.Equal("Mã voucher đã ngừng áp dụng", body.GetProperty("message").GetString());
        AssertBiChan(body, DonHang);
    }

    [Fact]
    public async Task Ma_chua_toi_ngay_thi_tra_200_kem_reasonCode_NotStarted()
    {
        using var factory = new TestAppFactory();
        var user = await SeedUserAsync(factory);
        var trip = await SeedTripAsync(factory);
        await SeedVoucherAsync(factory, VoucherHopLe("SAPMOI", v => v.ValidFrom = DateTime.UtcNow.AddDays(5)));

        var body = await ValidateAsync(factory, user, "SAPMOI", trip.Id, DonHang);

        Assert.False(body.GetProperty("valid").GetBoolean());
        Assert.Equal("NotStarted", body.GetProperty("reasonCode").GetString());
        Assert.Equal("Mã voucher chưa tới thời gian áp dụng", body.GetProperty("message").GetString());
        AssertBiChan(body, DonHang);
    }

    [Fact]
    public async Task Ma_het_luot_thi_tra_200_kem_reasonCode_OutOfStock()
    {
        using var factory = new TestAppFactory();
        var user = await SeedUserAsync(factory);
        var trip = await SeedTripAsync(factory);
        await SeedVoucherAsync(factory, VoucherHopLe("CHAULUOT", v =>
        {
            v.Quantity = 10;
            v.UsedCount = 10;
        }));

        var body = await ValidateAsync(factory, user, "CHAULUOT", trip.Id, DonHang);

        Assert.False(body.GetProperty("valid").GetBoolean());
        Assert.Equal("OutOfStock", body.GetProperty("reasonCode").GetString());
        Assert.Equal("Mã voucher đã hết lượt sử dụng", body.GetProperty("message").GetString());
        AssertBiChan(body, DonHang);
    }

    [Fact]
    public async Task Ma_gan_tuyen_khac_thi_tra_200_kem_reasonCode_WrongRoute()
    {
        using var factory = new TestAppFactory();
        var user = await SeedUserAsync(factory);
        var trip = await SeedTripAsync(factory);
        // Tuyến của voucher là tuyến KHÁC với tuyến của chuyến khách đang đặt.
        await SeedVoucherAsync(factory, VoucherHopLe("KHACTUYEN", v => v.RouteId = Guid.NewGuid()));

        var body = await ValidateAsync(factory, user, "KHACTUYEN", trip.Id, DonHang);

        Assert.False(body.GetProperty("valid").GetBoolean());
        Assert.Equal("WrongRoute", body.GetProperty("reasonCode").GetString());
        Assert.Equal("Mã voucher không áp dụng cho tuyến này", body.GetProperty("message").GetString());
        AssertBiChan(body, DonHang);
    }

    // ---------------------------------------------------------------------------------------
    // 2. BelowMinOrder — ghim nguyên văn VÍ DỤ CỦA HỢP ĐỒNG, kể cả dấu phân cách nghìn
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Dùng lại đúng ví dụ của hợp đồng (mục *Voucher — /vouchers*): <c>FIX20K</c> giảm 20.000đ, đơn
    /// tối thiểu 100.000đ, đơn thật 80.000đ.
    ///
    /// <para>
    /// Câu <c>message</c> nội suy tiền tệ <b>đã</b> được ghim ở đường khác
    /// (<c>"…tối thiểu 200.000đ"</c>: <c>VoucherRedemptionServiceTests.cs:493</c>,
    /// <c>VoucherValidationServiceTests.cs:437</c>), nên ca này <b>không</b> phải chỗ duy nhất bắt được
    /// lỗi culture — đừng ghi công nó như vậy. Ba thứ nó thật sự thêm:
    /// </para>
    /// <list type="number">
    /// <item>chuỗi <c>"BelowMinOrder"</c> trên dây — chưa từng được ghim ở đâu;</item>
    /// <item>câu <c>message</c> trên <b>đúng dây</b> của endpoint validate;</item>
    /// <item>hình dạng ĐẦY ĐỦ của một response bị chặn: hợp đồng nói rõ <c>discountType</c> vẫn là kiểu
    /// thật của voucher (<c>FixedAmount</c>) và <c>code</c> vẫn trả về, để FE hiện được "mã FIX20K
    /// chưa dùng được vì…". Ca <c>NotFound</c> của file kia chỉ kiểm được nhánh voucher KHÔNG tồn
    /// tại, tức mọi trường đều <c>null</c> — không kiểm được nhánh này.</item>
    /// </list>
    /// </summary>
    [Fact]
    public async Task Don_duoi_gia_tri_toi_thieu_thi_tra_200_kem_reasonCode_BelowMinOrder()
    {
        using var factory = new TestAppFactory();
        var user = await SeedUserAsync(factory);
        var trip = await SeedTripAsync(factory);
        await SeedVoucherAsync(factory, VoucherHopLe("FIX20K", v =>
        {
            v.DiscountType = VoucherDiscountType.FixedAmount;
            v.DiscountValue = 20_000m;
            v.MinOrderValue = 100_000m;
        }));

        var body = await ValidateAsync(factory, user, "FIX20K", trip.Id, 80_000m);

        Assert.False(body.GetProperty("valid").GetBoolean());
        Assert.Equal("BelowMinOrder", body.GetProperty("reasonCode").GetString());
        Assert.Equal("Đơn hàng chưa đạt giá trị tối thiểu 100.000đ", body.GetProperty("message").GetString());

        // Voucher CÓ thật nên FE vẫn nhận được mã và id để hiển thị câu "mã FIX20K …".
        Assert.Equal("FIX20K", body.GetProperty("code").GetString());
        Assert.Equal(JsonValueKind.String, body.GetProperty("voucherId").ValueKind);
        Assert.Equal("FixedAmount", body.GetProperty("discountType").GetString());

        AssertBiChan(body, 80_000m);
    }

    // ---------------------------------------------------------------------------------------
    // 3. THỨ TỰ kiểm tra — hợp đồng ghi rõ "thứ tự này LÀ một phần của hợp đồng"
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Bậc thang đi xuống: mỗi bậc cố tình vi phạm <b>mọi điều kiện từ bậc đó trở đi</b>, nên câu
    /// trả về bắt buộc phải là điều kiện của chính bậc đó. Đây là cách đo <b>thứ tự</b>
    /// <c>Inactive → NotStarted → Expired → OutOfStock → WrongRoute → BelowMinOrder</c> trên dây, chứ
    /// không chỉ đo từng điều kiện riêng lẻ: một điều kiện riêng lẻ vẫn xanh kể cả khi service đảo
    /// thứ tự, mà đảo thứ tự thì khách nhận câu sai đường (ví dụ bị bảo "thêm 100.000đ nữa đi" cho
    /// một mã đã hết lượt).
    ///
    /// <para>
    /// Bậc cuối cố ý để <b>không vi phạm gì</b> và kỳ vọng <c>reasonCode</c> null — nếu thiếu bậc này
    /// thì một service luôn trả về mã từ chối nào đó cũng làm sáu bậc trên xanh.
    /// </para>
    ///
    /// <para>
    /// Mỗi bậc một voucher riêng với mã riêng, <b>không</b> dùng chung một voucher rồi sửa dần: sửa
    /// dần thì phải seed lại cùng một mã, mà provider InMemory không dựng unique index nên
    /// <c>FirstOrDefault</c> sẽ bốc đại một trong các bản ghi trùng mã.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Vi_pham_nhieu_dieu_kien_thi_tra_dieu_kien_dau_tien_theo_thu_tu_hop_dong()
    {
        using var factory = new TestAppFactory();
        var user = await SeedUserAsync(factory);
        var trip = await SeedTripAsync(factory);

        var tuyenKhac = Guid.NewGuid();
        var quaKhu = DateTime.UtcNow.AddDays(-5);
        var tuongLai = DateTime.UtcNow.AddDays(5);
        const decimal TrenMucToiThieu = 999_999m;

        var bacThang = new (string Code, Action<Voucher> ViPham, string? MongDoi)[]
        {
            ("TT-B1", v =>
            {
                v.Status = VoucherStatus.Inactive;
                v.UsedCount = v.Quantity;
                v.RouteId = tuyenKhac;
                v.MinOrderValue = TrenMucToiThieu;
            }, "Inactive"),
            ("TT-B2", v =>
            {
                v.ValidFrom = tuongLai;
                v.UsedCount = v.Quantity;
                v.RouteId = tuyenKhac;
                v.MinOrderValue = TrenMucToiThieu;
            }, "NotStarted"),
            ("TT-B3", v =>
            {
                v.ValidUntil = quaKhu;
                v.UsedCount = v.Quantity;
                v.RouteId = tuyenKhac;
                v.MinOrderValue = TrenMucToiThieu;
            }, "Expired"),
            ("TT-B4", v =>
            {
                v.UsedCount = v.Quantity;
                v.RouteId = tuyenKhac;
                v.MinOrderValue = TrenMucToiThieu;
            }, "OutOfStock"),
            ("TT-B5", v =>
            {
                v.RouteId = tuyenKhac;
                v.MinOrderValue = TrenMucToiThieu;
            }, "WrongRoute"),
            ("TT-B6", v => v.MinOrderValue = TrenMucToiThieu, "BelowMinOrder"),
            ("TT-B7", v => { }, null),
        };

        foreach (var (code, viPham, mongDoi) in bacThang)
        {
            await SeedVoucherAsync(factory, VoucherHopLe(code, viPham));

            var body = await ValidateAsync(factory, user, code, trip.Id, DonHang);
            var nhanDuoc = body.GetProperty("reasonCode").GetString();

            // So tay để câu báo lỗi nói được bậc nào hỏng — Assert.Equal trong vòng lặp không cho biết.
            if (nhanDuoc != mongDoi)
            {
                Assert.Fail($"Bậc {code}: kỳ vọng reasonCode = {mongDoi ?? "null"} nhưng nhận {nhanDuoc ?? "null"}");
            }

            Assert.Equal(mongDoi is null, body.GetProperty("valid").GetBoolean());

            if (mongDoi is null)
            {
                // Bậc cuối: voucher thật sự dùng được — ghim luôn con số, 10% của 150.000.
                Assert.Equal(15_000m, body.GetProperty("discountAmount").GetDecimal());
                Assert.Equal(135_000m, body.GetProperty("finalAmount").GetDecimal());
            }
            else
            {
                AssertBiChan(body, DonHang);
            }
        }
    }

    // ---------------------------------------------------------------------------------------
    // 4. Biên của bộ kiểm tra dữ liệu vào — chưa ca nào ở tầng HTTP phủ
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Code_mot_ky_tu_thi_tra_400()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsPassengerAsync(factory);

        var response = await client.PostAsJsonAsync(
            ValidateUrl, new { code = "A", tripId = Guid.NewGuid(), orderAmount = DonHang });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal([LoiCodeSaiDoDai], ErrorMessagesOf(await ReadJsonAsync(response), "code"));
    }

    [Fact]
    public async Task Code_hai_muoi_mot_ky_tu_thi_tra_400()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsPassengerAsync(factory);

        var response = await client.PostAsJsonAsync(
            ValidateUrl, new { code = new string('A', 21), tripId = Guid.NewGuid(), orderAmount = DonHang });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal([LoiCodeSaiDoDai], ErrorMessagesOf(await ReadJsonAsync(response), "code"));
    }

    /// <summary>
    /// Đầu mút TRÊN của ràng buộc 2–20 phải TÍNH LÀ hợp lệ: 20 ký tự đi qua được bộ kiểm tra và tới
    /// tầng nghiệp vụ, nên câu trả về là <c>NotFound</c> (200) chứ không phải 400. Thiếu ca này thì
    /// một bộ kiểm tra siết nhầm thành 2–19 vẫn làm hai ca 400 ở trên xanh.
    /// </summary>
    [Fact]
    public async Task Code_dung_hai_muoi_ky_tu_thi_qua_duoc_bo_kiem_tra()
    {
        using var factory = new TestAppFactory();
        var user = await SeedUserAsync(factory);
        var trip = await SeedTripAsync(factory);

        var body = await ValidateAsync(factory, user, new string('A', 20), trip.Id, DonHang);

        Assert.Equal("NotFound", body.GetProperty("reasonCode").GetString());
    }

    [Fact]
    public async Task Thieu_orderAmount_thi_tra_400()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsPassengerAsync(factory);

        // Cố ý bỏ hẳn trường: DTO khai decimal? đúng để phân biệt "không gửi" với "gửi số 0"
        // (hai ca phải khác nhau — xem ca ngay dưới).
        var response = await client.PostAsJsonAsync(
            ValidateUrl, new { code = "SUMMER10", tripId = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            ["Giá trị đơn hàng không được để trống"],
            ErrorMessagesOf(await ReadJsonAsync(response), "orderAmount"));
    }

    /// <summary>
    /// <c>orderAmount = 0</c> là dữ liệu vào <b>HỢP LỆ</b> (đơn rỗng) — nó phải đi qua bộ kiểm tra rồi
    /// được trả lời bằng nghiệp vụ <c>BelowMinOrder</c>, KHÁC hẳn <c>-1</c> vốn bị 400 (ca
    /// <c>OrderAmount_am_thi_tra_400</c> của file kia). Hai ca cạnh nhau ghim đúng biên dưới của
    /// <c>[Range(typeof(decimal), "0", …)]</c>.
    /// </summary>
    [Fact]
    public async Task OrderAmount_bang_khong_qua_bo_kiem_tra_roi_thanh_BelowMinOrder()
    {
        using var factory = new TestAppFactory();
        var user = await SeedUserAsync(factory);
        var trip = await SeedTripAsync(factory);
        await SeedVoucherAsync(factory, VoucherHopLe("FIX20K", v => v.MinOrderValue = 100_000m));

        var body = await ValidateAsync(factory, user, "FIX20K", trip.Id, 0m);

        Assert.False(body.GetProperty("valid").GetBoolean());
        Assert.Equal("BelowMinOrder", body.GetProperty("reasonCode").GetString());
        AssertBiChan(body, 0m);
    }

    /// <summary>
    /// Đầu mút TRÊN của trần <c>numeric(12,2)</c> — 9.999.999.999,99 phải được nhận, và phép tính
    /// phần trăm trên nó phải cho ra số vẫn nằm trong 12 chữ số, nên ca này ghim cả hai con số chứ
    /// không chỉ ghim "không bị 400".
    ///
    /// <para>
    /// Đây là chỗ duy nhất lộ được một cái bẫy culture của DTO: <c>[Range(typeof(decimal), "0",
    /// "9999999999.99")]</c> nhận tham số dạng <b>CHUỖI</b>, nên nếu chuỗi trần được phân tích bằng
    /// một culture coi dấu phẩy là dấu thập phân thì trần bị hiểu sai. <b>Đo được: không xảy ra</b> —
    /// biên dưới (<c>"0"</c>) không có dấu thập phân nên không lộ được, còn biên trên thì chính ca này
    /// đi qua bình thường. Ghi lại đây để lần sau ai đọc code không phải đo lại.
    /// </para>
    /// </summary>
    [Fact]
    public async Task OrderAmount_dung_bang_tran_numeric_12_2_thi_duoc_chap_nhan()
    {
        using var factory = new TestAppFactory();
        var user = await SeedUserAsync(factory);
        var trip = await SeedTripAsync(factory);
        await SeedVoucherAsync(factory, VoucherHopLe("MUOIPHANTRAM", v =>
        {
            v.DiscountType = VoucherDiscountType.Percent;
            v.DiscountValue = 10m;
        }));

        var body = await ValidateAsync(factory, user, "MUOIPHANTRAM", trip.Id, 9_999_999_999.99m);

        Assert.True(body.GetProperty("valid").GetBoolean());
        // 9.999.999.999,99 × 10% = 999.999.999,999 → làm tròn AwayFromZero về đồng nguyên = 1.000.000.000
        Assert.Equal(1_000_000_000m, body.GetProperty("discountAmount").GetDecimal());
        Assert.Equal(8_999_999_999.99m, body.GetProperty("finalAmount").GetDecimal());
    }

    [Fact]
    public async Task OrderAmount_vuot_tran_numeric_12_2_thi_tra_400()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsPassengerAsync(factory);

        var response = await client.PostAsJsonAsync(
            ValidateUrl, new { code = "SUMMER10", tripId = Guid.NewGuid(), orderAmount = 10_000_000_000m });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal([LoiOrderAmount], ErrorMessagesOf(await ReadJsonAsync(response), "orderAmount"));
    }

    // ---------------------------------------------------------------------------------------
    // Helper — mỗi lớp test tự chép, không có lớp base chung (đúng lệ đang có của dự án)
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Voucher dùng được ngay tại thời điểm chạy: đang bật, hiệu lực trong khoảng ±5 ngày, còn lượt,
    /// không gắn tuyến, không đặt mức tối thiểu, giảm 10%. Tham số <paramref name="chinh"/> để mỗi ca
    /// chỉ phải nói ra điều kiện nó cố tình vi phạm.
    ///
    /// <para>
    /// Cố ý neo vào <c>DateTime.UtcNow</c> chứ không vào một mốc ngày cố định: mốc cứng biến file test
    /// thành quả bom hẹn giờ — qua ngày đó là cả file đỏ vì lý do không liên quan tới nó.
    /// </para>
    /// </summary>
    private static Voucher VoucherHopLe(string code, Action<Voucher>? chinh = null)
    {
        var voucher = new Voucher
        {
            Id = Guid.NewGuid(),
            Code = code,
            Name = "Voucher " + code,
            DiscountType = VoucherDiscountType.Percent,
            DiscountValue = 10m,
            MinOrderValue = 0m,
            MaxDiscount = null,
            RouteId = null,
            Quantity = 100,
            UsedCount = 0,
            ValidFrom = DateTime.UtcNow.AddDays(-5),
            ValidUntil = DateTime.UtcNow.AddDays(5),
            Status = VoucherStatus.Active,
        };

        chinh?.Invoke(voucher);

        return voucher;
    }

    /// <summary>
    /// Bất biến của MỌI ca bị chặn, ghim một chỗ cho cả bảy mã: hợp đồng hứa <c>discountAmount = 0</c>
    /// và <c>finalAmount</c> bằng đúng <c>orderAmount</c> để FE không phải rẽ nhánh khi hiển thị. Một
    /// mã bị chặn mà trả về số tiền giảm khác 0 là màn thanh toán hiện số tiền khách KHÔNG được giảm.
    /// </summary>
    private static void AssertBiChan(JsonElement body, decimal orderAmount)
    {
        Assert.Equal(0m, body.GetProperty("discountAmount").GetDecimal());
        Assert.Equal(orderAmount, body.GetProperty("finalAmount").GetDecimal());
        Assert.Equal(JsonValueKind.String, body.GetProperty("message").ValueKind);
    }

    private static async Task<JsonElement> ValidateAsync(
        TestAppFactory factory, UserEntity user, string code, Guid tripId, decimal orderAmount)
    {
        var client = ClientWith(factory, factory.CreateTokenFor(user));
        var response = await client.PostAsJsonAsync(ValidateUrl, new { code, tripId, orderAmount });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return await ReadJsonAsync(response);
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
        => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static string[] ErrorMessagesOf(JsonElement body, string field)
        => body.GetProperty("errors").GetProperty(field).EnumerateArray()
            .Select(message => message.GetString() ?? string.Empty)
            .ToArray();

    private static async Task<HttpClient> SignInAsPassengerAsync(TestAppFactory factory)
    {
        var user = await SeedUserAsync(factory);

        return ClientWith(factory, factory.CreateTokenFor(user));
    }

    private static HttpClient ClientWith(TestAppFactory factory, string accessToken)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        return client;
    }

    /// <summary>
    /// Tài khoản hành khách kèm vai trò. Provider InMemory KHÔNG chạy <c>HasData</c> nên phải seed
    /// vai trò trước — không thì token hợp lệ mà người dùng trỏ tới vai trò không tồn tại.
    /// </summary>
    private static async Task<UserEntity> SeedUserAsync(TestAppFactory factory)
    {
        await factory.SeedAsync(db =>
        {
            if (!db.Roles.Any(r => r.Id == RoleIds.Passenger || r.Code == RoleCodes.Passenger))
            {
                db.Roles.Add(new Role { Id = RoleIds.Passenger, Code = RoleCodes.Passenger, Name = RoleCodes.Passenger });
            }
        });

        var user = new UserEntity
        {
            Id = Guid.NewGuid(),
            PhoneNumber = "09" + Random.Shared.Next(10_000_000, 99_999_999),
            FullName = "Hành Khách Test",
            PasswordHash = "hash-khong-dung-toi-trong-test",
            IsActive = true,
            RoleId = RoleIds.Passenger,
            UserRoles = [new UserRole { RoleId = RoleIds.Passenger }],
        };

        await factory.SeedAsync(db => db.Users.Add(user));

        return user;
    }

    /// <summary>Chuyến kèm tuyến của nó — điều kiện "tuyến" đọc thẳng <c>Trip.RouteId</c>.</summary>
    private static async Task<Trip> SeedTripAsync(TestAppFactory factory, Guid? routeId = null)
    {
        var trip = new Trip
        {
            Id = Guid.NewGuid(),
            RouteId = routeId ?? Guid.NewGuid(),
            BusId = Guid.NewGuid(),
            DepartureTime = DateTime.UtcNow.AddDays(1),
        };

        await factory.SeedAsync(db => db.Trips.Add(trip));

        return trip;
    }

    private static async Task SeedVoucherAsync(TestAppFactory factory, Voucher voucher)
    {
        voucher.CreatedAt = DateTime.UtcNow;
        await factory.SeedAsync(db => db.Vouchers.Add(voucher));
    }
}
