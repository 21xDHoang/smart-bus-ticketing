using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using SmartBus.Api.Entities;
using SmartBus.Api.Services;
using RouteEntity = SmartBus.Api.Entities.Route;
using UserEntity = SmartBus.Api.Entities.User;

namespace SmartBus.Tests;

/// <summary>
/// Test tích hợp cho API thống kê hiệu quả voucher (dòng 54, US 18) —
/// /api/vouchers/statistics (task "API thống kê hiệu quả voucher" — Nguyễn Duy Kiên).
///
/// Dùng lại <see cref="TestAppFactory"/> của JwtAuthTests: chạy trên app thật, mỗi test một CSDL
/// InMemory riêng dựng từ entity.
///
/// ⚠️ Bảy luật nền của hợp đồng mà bộ test này khoá lại:
///   • Thống kê trên TOÀN BỘ voucher đã phát hành, MỌI trạng thái (kể cả Inactive), không theo người gọi.
///   • Voucher chưa ai dùng VẪN có một dòng với usedCount 0 — không bị GroupBy làm rơi.
///   • usedCount/tiền đếm từ VoucherUsages, KHÔNG đọc cột denormalized Vouchers.UsedCount.
///   • uniqueCustomers là số KHÁCH khác nhau, không phải số lượt.
///   • firstUsedAt/lastUsedAt null khi chưa ai dùng.
///   • Năm đẳng thức tổng — và chúng là phép kiểm THẬT vì sáu con số tổng đến từ truy vấn độc lập.
///   • Sắp usedCount giảm dần, trùng thì theo code tăng dần.
///
/// ⚠️ Bảng <c>Vouchers</c>/<c>VoucherUsages</c> đã migrate (PR #144) nhưng bộ test chạy trên InMemory
/// nên KHÔNG chờ migration; đổi lại, test xanh ở đây không chứng minh bảng đã có thật trên CSDL chung.
/// </summary>
public class VoucherStatisticsApiTests
{
    private const string Url = "/api/vouchers/statistics";

    /// <summary>Mốc thời gian cố định — không dùng UtcNow để dữ liệu seed tất định.</summary>
    private static readonly DateTime Moc1 = new(2026, 10, 1, 1, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime TuNgay = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime DenNgay = new(2026, 12, 31, 23, 59, 59, DateTimeKind.Utc);

    // ---------------------------------------------------------------------------------------
    // Phân quyền
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Khong_gui_token_thi_tra_401()
    {
        using var factory = new TestAppFactory();

        var response = await factory.CreateClient().GetAsync(Url);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(RoleCodes.Passenger)]
    [InlineData(RoleCodes.Driver)]
    public async Task Hanh_khach_va_tai_xe_goi_thi_tra_403(string roleCode)
    {
        using var factory = new TestAppFactory();
        var (client, _) = await SignInAsync(factory, RoleIdsFor(roleCode), roleCode, "Người Dùng Test");

        // Đây là số liệu kinh doanh trên TOÀN BỘ voucher — doanh thu đã giảm giá và mã nào bị bỏ xó.
        // Body 403 là câu cố định của RbacMiddleware.
        var response = await client.GetAsync(Url);
        var body = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("Bạn không có quyền truy cập tính năng này.", body.GetProperty("message").GetString());
    }

    [Theory]
    [InlineData(RoleCodes.Manager)]
    [InlineData(RoleCodes.Admin)]
    public async Task Quan_ly_va_admin_goi_duoc(string roleCode)
    {
        using var factory = new TestAppFactory();
        var (client, _) = await SignInAsync(factory, RoleIdsFor(roleCode), roleCode, "Quản Lý Test");

        var response = await client.GetAsync(Url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ---------------------------------------------------------------------------------------
    // Hình dạng — chưa có dữ liệu, và hình dạng hợp đồng
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Chua_co_voucher_nao_thi_tra_200_moi_con_dem_bang_0_va_mang_rong()
    {
        using var factory = new TestAppFactory();
        var (client, _) = await SignInAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");

        var response = await client.GetAsync(Url);
        var body = await ReadJsonAsync(response);

        // Hệ thống chưa có voucher nào KHÔNG phải lỗi — "chưa có dữ liệu" là một câu trả lời. Endpoint
        // không nhận tham số nên không có nhánh 400/404 nào.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            [
                "items",
                "neverUsedVouchers",
                "totalDiscountAmount",
                "totalIssued",
                "totalOrderAmount",
                "totalUsed",
                "totalVouchers",
            ],
            PropertyNamesOf(body));

        Assert.Equal(0, body.GetProperty("totalVouchers").GetInt32());
        Assert.Equal(0, body.GetProperty("totalIssued").GetInt32());
        Assert.Equal(0, body.GetProperty("totalUsed").GetInt32());
        Assert.Equal(0m, body.GetProperty("totalOrderAmount").GetDecimal());
        Assert.Equal(0m, body.GetProperty("totalDiscountAmount").GetDecimal());
        Assert.Equal(0, body.GetProperty("neverUsedVouchers").GetInt32());
        Assert.Empty(body.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task Hinh_dang_khoa_lai_dung_hop_dong()
    {
        using var factory = new TestAppFactory();
        var (client, _) = await SignInAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var khach = await SeedUserAsync(factory, RoleIds.Passenger, RoleCodes.Passenger);
        var voucher = await SeedVoucherAsync(factory, code: "SUMMER10", quantity: 100);
        await SeedUsageAsync(factory, voucher.Id, khach.Id, orderAmount: 150000m, discountAmount: 15000m);

        var body = await ReadJsonAsync(await client.GetAsync(Url));

        // Thêm một trường vào response là ĐỔI HÌNH DẠNG API (⛔5): phải sửa api-contract.md trước rồi
        // báo người viết frontend. Ca này làm đổ test ngay lúc đó, để việc sửa hợp đồng là một quyết
        // định có ý thức. Bốn trường cấu hình của voucher (discountType/discountValue/minOrderValue/
        // maxDiscount) CỐ Ý không có ở đây — chúng thuộc GET /vouchers (dòng 51).
        var dong = body.GetProperty("items").EnumerateArray().First();

        Assert.Equal(
            [
                "code",
                "firstUsedAt",
                "lastUsedAt",
                "name",
                "quantity",
                "routeCode",
                "routeId",
                "routeName",
                "status",
                "totalDiscountAmount",
                "totalOrderAmount",
                "uniqueCustomers",
                "usedCount",
                "validFrom",
                "validUntil",
                "voucherId",
            ],
            PropertyNamesOf(dong));
    }

    // ---------------------------------------------------------------------------------------
    // 🔴 Quyết định 1 — voucher chưa ai dùng VẪN có dòng
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Voucher_phat_hanh_chua_ai_dung_van_co_dong_voi_usedCount_0()
    {
        using var factory = new TestAppFactory();
        var (client, _) = await SignInAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var khach = await SeedUserAsync(factory, RoleIds.Passenger, RoleCodes.Passenger);

        var coDung = await SeedVoucherAsync(factory, code: "CODUNG", quantity: 100);
        var boXo = await SeedVoucherAsync(factory, code: "BOX0", quantity: 500);

        await SeedUsageAsync(factory, coDung.Id, khach.Id, orderAmount: 200000m, discountAmount: 20000m);
        // BOX0 cố ý KHÔNG có lượt tiêu thụ nào.

        var body = await ReadJsonAsync(await client.GetAsync(Url));
        var items = body.GetProperty("items").EnumerateArray().ToArray();

        // Đây là ca quan trọng nhất của file. Một GroupBy ngây thơ trên VoucherUsages sẽ làm rơi đúng
        // dòng này — mà "mã phát hành rồi bỏ xó" chính là câu trả lời đắt nhất của bảng hiệu quả.
        Assert.Equal(2, items.Length);

        var dongBoXo = items.Single(row => row.GetProperty("code").GetString() == "BOX0");
        Assert.Equal(0, dongBoXo.GetProperty("usedCount").GetInt32());
        Assert.Equal(0, dongBoXo.GetProperty("uniqueCustomers").GetInt32());
        Assert.Equal(0m, dongBoXo.GetProperty("totalOrderAmount").GetDecimal());
        Assert.Equal(0m, dongBoXo.GetProperty("totalDiscountAmount").GetDecimal());
        Assert.Equal(500, dongBoXo.GetProperty("quantity").GetInt32());
        Assert.Equal(boXo.Id, dongBoXo.GetProperty("voucherId").GetGuid());

        Assert.Equal(1, body.GetProperty("neverUsedVouchers").GetInt32());
    }

    [Fact]
    public async Task Voucher_chua_dung_thi_firstUsedAt_va_lastUsedAt_null()
    {
        using var factory = new TestAppFactory();
        var (client, _) = await SignInAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var khach = await SeedUserAsync(factory, RoleIds.Passenger, RoleCodes.Passenger);

        var coDung = await SeedVoucherAsync(factory, code: "CODUNG", quantity: 100);
        await SeedVoucherAsync(factory, code: "BOX0", quantity: 100);

        await SeedUsageAsync(factory, coDung.Id, khach.Id, createdAt: Moc1);
        await SeedUsageAsync(
            factory, coDung.Id, khach.Id, orderAmount: 100000m, discountAmount: 10000m,
            createdAt: Moc1.AddDays(3), paymentCode: "PAY-002");

        var items = (await ReadJsonAsync(await client.GetAsync(Url))).GetProperty("items")
            .EnumerateArray().ToArray();

        var dongBox0 = items.Single(row => row.GetProperty("code").GetString() == "BOX0");
        Assert.Equal(JsonValueKind.Null, dongBox0.GetProperty("firstUsedAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, dongBox0.GetProperty("lastUsedAt").ValueKind);

        var dongCoDung = items.Single(row => row.GetProperty("code").GetString() == "CODUNG");
        Assert.Equal(Moc1, dongCoDung.GetProperty("firstUsedAt").GetDateTime());
        Assert.Equal(Moc1.AddDays(3), dongCoDung.GetProperty("lastUsedAt").GetDateTime());
    }

    // ---------------------------------------------------------------------------------------
    // 🔴 Quyết định 2 — đếm từ VoucherUsages, không đọc cột denormalized UsedCount
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Tien_va_luot_dem_tu_VoucherUsages_khong_doc_cot_UsedCount()
    {
        using var factory = new TestAppFactory();
        var (client, _) = await SignInAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var khach = await SeedUserAsync(factory, RoleIds.Passenger, RoleCodes.Passenger);

        // Seed CỐ Ý lệch: cột denormalized nói 99 lượt, bảng lượt tiêu thụ chỉ có 2 dòng.
        var voucher = await SeedVoucherAsync(factory, code: "LECH", quantity: 100, usedCount: 99);
        await SeedUsageAsync(factory, voucher.Id, khach.Id, orderAmount: 150000m, discountAmount: 15000m);
        await SeedUsageAsync(
            factory, voucher.Id, khach.Id, orderAmount: 250000m, discountAmount: 25000m,
            paymentCode: "PAY-002");

        var body = await ReadJsonAsync(await client.GetAsync(Url));
        var dong = body.GetProperty("items").EnumerateArray().Single();

        // VoucherUsages là bản ghi sự thật, cột UsedCount chỉ là bộ đếm để đường validate khỏi COUNT(*).
        // Hai bên vênh thì bảng thống kê là bên ĐÚNG và phơi ra chỗ vênh — đọc cột kia là nhân bản con
        // số sai thêm một lần nữa. Con số 2 dưới đây chính là chốt đó.
        Assert.Equal(2, dong.GetProperty("usedCount").GetInt32());
        Assert.Equal(400000m, dong.GetProperty("totalOrderAmount").GetDecimal());
        Assert.Equal(40000m, dong.GetProperty("totalDiscountAmount").GetDecimal());

        // Con số tổng cũng phải theo nguồn thật, không theo cột denormalized.
        Assert.Equal(2, body.GetProperty("totalUsed").GetInt32());
    }

    [Fact]
    public async Task uniqueCustomers_dem_nguoi_khac_nhau_khong_phai_so_luot()
    {
        using var factory = new TestAppFactory();
        var (client, _) = await SignInAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var khachA = await SeedUserAsync(factory, RoleIds.Passenger, RoleCodes.Passenger, "Nguyễn Văn A");
        var khachB = await SeedUserAsync(factory, RoleIds.Passenger, RoleCodes.Passenger, "Trần Thị B");

        var voucher = await SeedVoucherAsync(factory, code: "NHIEULUOT", quantity: 100);

        // A dùng 2 lần (luật "mỗi khách một lượt" CHƯA chốt, nên đây là dữ liệu hợp lệ), B dùng 1 lần.
        await SeedUsageAsync(factory, voucher.Id, khachA.Id);
        await SeedUsageAsync(factory, voucher.Id, khachA.Id, paymentCode: "PAY-002");
        await SeedUsageAsync(factory, voucher.Id, khachB.Id, paymentCode: "PAY-003");

        var dong = (await ReadJsonAsync(await client.GetAsync(Url))).GetProperty("items")
            .EnumerateArray().Single();

        // 3 lượt nhưng chỉ 2 khách. Hai con số này KHÁC nhau là chuyện bình thường, không phải lỗi dữ
        // liệu — đừng "sửa" thành cùng một con.
        Assert.Equal(3, dong.GetProperty("usedCount").GetInt32());
        Assert.Equal(2, dong.GetProperty("uniqueCustomers").GetInt32());
    }

    // ---------------------------------------------------------------------------------------
    // Thứ tự tất định
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Sap_theo_luot_dung_giam_dan_trung_thi_theo_ma_tang_dan()
    {
        using var factory = new TestAppFactory();
        var (client, _) = await SignInAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var khach = await SeedUserAsync(factory, RoleIds.Passenger, RoleCodes.Passenger);

        // Seed cố ý NGƯỢC cả hai chiều: mã lớn trước, và nhóm điểm thấp trước.
        var b10 = await SeedVoucherAsync(factory, code: "B10", quantity: 100);
        var a01 = await SeedVoucherAsync(factory, code: "01", quantity: 100);
        var top = await SeedVoucherAsync(factory, code: "TOP", quantity: 100);

        await SeedUsageAsync(factory, b10.Id, khach.Id);
        await SeedUsageAsync(factory, a01.Id, khach.Id, paymentCode: "PAY-002");
        for (var lan = 0; lan < 3; lan++)
        {
            await SeedUsageAsync(factory, top.Id, khach.Id, paymentCode: $"PAY-TOP-{lan}");
        }

        var items = (await ReadJsonAsync(await client.GetAsync(Url))).GetProperty("items")
            .EnumerateArray().ToArray();

        // Nhiều lượt nhất lên đầu — đó chính là điều bảng này để trả lời. Hai mã cùng 1 lượt phải ra
        // theo mã tăng dần (ordinal: "01" < "B10"): thiếu vế thứ hai thì hai lần gọi cho hai thứ tự
        // khác nhau và không ai so sánh được gì.
        Assert.Equal(["TOP", "01", "B10"], CodesOf(items));
        Assert.Equal([3, 1, 1], UsedCountsOf(items));
    }

    // ---------------------------------------------------------------------------------------
    // Năm đẳng thức — phép kiểm THẬT vì sáu con số tổng đến từ truy vấn độc lập
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Nam_dang_thuc_cong_lai_bang_tong()
    {
        using var factory = new TestAppFactory();
        var (client, _) = await SignInAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var khachA = await SeedUserAsync(factory, RoleIds.Passenger, RoleCodes.Passenger, "Nguyễn Văn A");
        var khachB = await SeedUserAsync(factory, RoleIds.Passenger, RoleCodes.Passenger, "Trần Thị B");

        // Dữ liệu trộn đủ ca: voucher dùng nhiều lượt / dùng một lượt / chưa ai dùng, và cả Inactive.
        var nhieu = await SeedVoucherAsync(factory, code: "NHIEU", quantity: 500);
        var mot = await SeedVoucherAsync(factory, code: "MOT", quantity: 200);
        await SeedVoucherAsync(factory, code: "KHONG", quantity: 300);
        var tat = await SeedVoucherAsync(
            factory, code: "TAT", quantity: 50, status: VoucherStatus.Inactive);

        await SeedUsageAsync(factory, nhieu.Id, khachA.Id, orderAmount: 150000m, discountAmount: 15000m);
        await SeedUsageAsync(
            factory, nhieu.Id, khachB.Id, orderAmount: 250000m, discountAmount: 25000m,
            paymentCode: "PAY-002");
        await SeedUsageAsync(
            factory, nhieu.Id, khachA.Id, orderAmount: 120000m, discountAmount: 12000m,
            paymentCode: "PAY-003");
        await SeedUsageAsync(
            factory, mot.Id, khachA.Id, orderAmount: 90000m, discountAmount: 9000m, paymentCode: "PAY-004");
        // Voucher TAT (Inactive) VẪN có lượt tiêu thụ cũ — giữ bản ghi làm lịch sử.
        await SeedUsageAsync(
            factory, tat.Id, khachB.Id, orderAmount: 40000m, discountAmount: 4000m, paymentCode: "PAY-005");

        var body = await ReadJsonAsync(await client.GetAsync(Url));
        var items = body.GetProperty("items").EnumerateArray().ToArray();

        Assert.Equal(4, body.GetProperty("totalVouchers").GetInt32());
        Assert.Equal(1050, body.GetProperty("totalIssued").GetInt32());
        Assert.Equal(5, body.GetProperty("totalUsed").GetInt32());
        Assert.Equal(650000m, body.GetProperty("totalOrderAmount").GetDecimal());
        Assert.Equal(65000m, body.GetProperty("totalDiscountAmount").GetDecimal());
        Assert.Equal(1, body.GetProperty("neverUsedVouchers").GetInt32());

        // Năm đẳng thức hợp đồng. Đây là loại sai sót màn hình không tự phát hiện được: nó chỉ vẽ ra
        // một bảng thiếu một mẩu mà không báo gì. Chúng là phép kiểm THẬT (không phải hằng đúng) vì
        // sáu con số tổng ở trên đến từ sáu truy vấn gộp RIÊNG, không cộng lại từ items.
        Assert.Equal(body.GetProperty("totalUsed").GetInt32(), UsedCountsOf(items).Sum());
        Assert.Equal(
            body.GetProperty("totalOrderAmount").GetDecimal(),
            OrderAmountsOf(items).Sum());
        Assert.Equal(
            body.GetProperty("totalDiscountAmount").GetDecimal(),
            DiscountAmountsOf(items).Sum());
        Assert.Equal(body.GetProperty("totalVouchers").GetInt32(), items.Length);
        Assert.Equal(
            body.GetProperty("neverUsedVouchers").GetInt32(),
            items.Count(row => row.GetProperty("usedCount").GetInt32() == 0));
    }

    // ---------------------------------------------------------------------------------------
    // Nền không được lọc trạng thái
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Thong_ke_gom_ca_voucher_Inactive()
    {
        using var factory = new TestAppFactory();
        var (client, _) = await SignInAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var khach = await SeedUserAsync(factory, RoleIds.Passenger, RoleCodes.Passenger);

        await SeedVoucherAsync(factory, code: "DANGCHAY", quantity: 100);
        var tat = await SeedVoucherAsync(
            factory, code: "DATAT", quantity: 100, status: VoucherStatus.Inactive);

        await SeedUsageAsync(factory, tat.Id, khach.Id);

        var body = await ReadJsonAsync(await client.GetAsync(Url));
        var items = body.GetProperty("items").EnumerateArray().ToArray();

        // Lọc mất voucher Inactive là sum(items[].usedCount) tụt xuống dưới totalUsed và đẳng thức đỏ
        // ngay. Trạng thái Inactive là chuyện người vận hành tắt tay — voucher vẫn đã phát hành và
        // những lượt đã tiêu thụ của nó vẫn là tiền đã giảm, không được biến mất khỏi thống kê.
        Assert.Equal(2, body.GetProperty("totalVouchers").GetInt32());
        Assert.Equal(2, items.Length);

        var dongTat = items.Single(row => row.GetProperty("code").GetString() == "DATAT");
        Assert.Equal("Inactive", dongTat.GetProperty("status").GetString());
        Assert.Equal(1, dongTat.GetProperty("usedCount").GetInt32());
        Assert.Equal(body.GetProperty("totalUsed").GetInt32(), UsedCountsOf(items).Sum());
    }

    // ---------------------------------------------------------------------------------------
    // Không join sang Payments — lượt tiêu thụ mồ côi vẫn được đếm
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Usage_khong_co_Payment_tuong_ung_van_duoc_dem()
    {
        using var factory = new TestAppFactory();
        var (client, _) = await SignInAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var khach = await SeedUserAsync(factory, RoleIds.Passenger, RoleCodes.Passenger);

        var voucher = await SeedVoucherAsync(factory, code: "MOC0I", quantity: 100);

        // PaymentCode trỏ vào hư không: bảng Payments KHÔNG có dòng nào trong cả CSDL test này.
        // VoucherUsage.PaymentCode là cột trần không FK, nên một phép join sang Payments sẽ là INNER
        // JOIN và làm rơi đúng dòng này khỏi thống kê — test này khoá lại rằng không có join nào.
        await SeedUsageAsync(
            factory, voucher.Id, khach.Id, orderAmount: 110000m, discountAmount: 11000m,
            paymentCode: "PAY-KHONG-TON-TAI");

        var body = await ReadJsonAsync(await client.GetAsync(Url));
        var dong = body.GetProperty("items").EnumerateArray().Single();

        Assert.Equal(1, dong.GetProperty("usedCount").GetInt32());
        Assert.Equal(110000m, dong.GetProperty("totalOrderAmount").GetDecimal());
        Assert.Equal(1, body.GetProperty("totalUsed").GetInt32());
        Assert.Equal(11000m, body.GetProperty("totalDiscountAmount").GetDecimal());
    }

    // ---------------------------------------------------------------------------------------
    // Tuyến — ghép sẵn, nullable
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Voucher_theo_tuyen_tra_kem_routeCode_routeName_con_moi_tuyen_thi_null()
    {
        using var factory = new TestAppFactory();
        var (client, _) = await SignInAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var tuyen = await SeedRouteAsync(factory, "01", "Bến Thành — Chợ Lớn");

        await SeedVoucherAsync(factory, code: "THEOTUYEN", quantity: 100, routeId: tuyen.Id);
        await SeedVoucherAsync(factory, code: "MOITUYEN", quantity: 100);

        var items = (await ReadJsonAsync(await client.GetAsync(Url))).GetProperty("items")
            .EnumerateArray().ToArray();

        // routeCode/routeName ghép sẵn để màn hình khỏi gọi GET /routes/{id} cho từng dòng — cùng lý
        // do bảng theo tuyến của thống kê phản ánh ghép sẵn hai trường này.
        var dongTheoTuyen = items.Single(row => row.GetProperty("code").GetString() == "THEOTUYEN");
        Assert.Equal(tuyen.Id, dongTheoTuyen.GetProperty("routeId").GetGuid());
        Assert.Equal("01", dongTheoTuyen.GetProperty("routeCode").GetString());
        Assert.Equal("Bến Thành — Chợ Lớn", dongTheoTuyen.GetProperty("routeName").GetString());

        // null = áp dụng cho mọi tuyến; ba trường cùng null, màn hình vẽ được "mọi tuyến".
        var dongMoiTuyen = items.Single(row => row.GetProperty("code").GetString() == "MOITUYEN");
        Assert.Equal(JsonValueKind.Null, dongMoiTuyen.GetProperty("routeId").ValueKind);
        Assert.Equal(JsonValueKind.Null, dongMoiTuyen.GetProperty("routeCode").ValueKind);
        Assert.Equal(JsonValueKind.Null, dongMoiTuyen.GetProperty("routeName").ValueKind);
    }

    // ---------------------------------------------------------------------------------------
    // Phạm vi — toàn hệ thống, không theo người gọi
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Thong_ke_tren_toan_bo_voucher_khong_theo_nguoi_goi()
    {
        using var factory = new TestAppFactory();
        var (client, _) = await SignInAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var khachA = await SeedUserAsync(factory, RoleIds.Passenger, RoleCodes.Passenger, "Nguyễn Văn A");
        var khachB = await SeedUserAsync(factory, RoleIds.Passenger, RoleCodes.Passenger, "Trần Thị B");

        var voucherA = await SeedVoucherAsync(factory, code: "CUAA", quantity: 100);
        var voucherB = await SeedVoucherAsync(factory, code: "CUAB", quantity: 100);
        await SeedVoucherAsync(factory, code: "CUAC", quantity: 100);

        await SeedUsageAsync(factory, voucherA.Id, khachA.Id);
        await SeedUsageAsync(factory, voucherB.Id, khachB.Id, paymentCode: "PAY-002");

        var body = await ReadJsonAsync(await client.GetAsync(Url));

        // Người gọi (quản lý) không dùng voucher nào, nhưng thống kê vẫn đếm của mọi khách và mọi
        // voucher. Nếu ai đó dán nhầm điều kiện UserId vào service này, test đỏ ngay.
        Assert.Equal(3, body.GetProperty("totalVouchers").GetInt32());
        Assert.Equal(2, body.GetProperty("totalUsed").GetInt32());
        Assert.Equal(3, body.GetProperty("items").GetArrayLength());
    }

    // ---------------------------------------------------------------------------------------
    // Helper — mỗi lớp test tự chép, không có lớp base chung (đúng lệ đang có của dự án)
    // ---------------------------------------------------------------------------------------

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
        => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static string[] PropertyNamesOf(JsonElement element)
        => element.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray();

    private static string[] CodesOf(JsonElement[] items)
        => items.Select(row => row.GetProperty("code").GetString() ?? string.Empty).ToArray();

    private static int[] UsedCountsOf(JsonElement[] items)
        => items.Select(row => row.GetProperty("usedCount").GetInt32()).ToArray();

    private static decimal[] OrderAmountsOf(JsonElement[] items)
        => items.Select(row => row.GetProperty("totalOrderAmount").GetDecimal()).ToArray();

    private static decimal[] DiscountAmountsOf(JsonElement[] items)
        => items.Select(row => row.GetProperty("totalDiscountAmount").GetDecimal()).ToArray();

    private static HttpClient ClientWith(TestAppFactory factory, string accessToken)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        return client;
    }

    private static Guid RoleIdsFor(string roleCode) => roleCode switch
    {
        RoleCodes.Admin => RoleIds.Admin,
        RoleCodes.Manager => RoleIds.Manager,
        RoleCodes.Driver => RoleIds.Driver,
        _ => RoleIds.Passenger,
    };

    private static async Task<(HttpClient Client, UserEntity User)> SignInAsync(
        TestAppFactory factory,
        Guid roleId,
        string roleCode,
        string fullName)
    {
        await EnsureAllRolesAsync(factory);
        var user = await SeedUserAsync(factory, roleId, roleCode, fullName);

        return (ClientWith(factory, factory.CreateTokenFor(user)), user);
    }

    /// <summary>Bốn vai trò migration seed sẵn — InMemory KHÔNG chạy <c>HasData</c>.</summary>
    private static readonly (Guid Id, string Code)[] CanonicalRoles =
    [
        (RoleIds.Admin, RoleCodes.Admin),
        (RoleIds.Manager, RoleCodes.Manager),
        (RoleIds.Driver, RoleCodes.Driver),
        (RoleIds.Passenger, RoleCodes.Passenger),
    ];

    private static async Task EnsureAllRolesAsync(TestAppFactory factory)
    {
        await factory.SeedAsync(db =>
        {
            foreach (var (id, code) in CanonicalRoles)
            {
                if (!db.Roles.Any(r => r.Id == id || r.Code == code))
                {
                    db.Roles.Add(new Role { Id = id, Code = code, Name = code });
                }
            }
        });
    }

    private static async Task<UserEntity> SeedUserAsync(
        TestAppFactory factory,
        Guid roleId,
        string roleCode,
        string fullName = "Người Dùng Test")
    {
        await factory.SeedAsync(db =>
        {
            if (!db.Roles.Any(r => r.Id == roleId || r.Code == roleCode))
            {
                db.Roles.Add(new Role { Id = roleId, Code = roleCode, Name = roleCode });
            }
        });

        var user = new UserEntity
        {
            Id = Guid.NewGuid(),
            PhoneNumber = "09" + Random.Shared.Next(10_000_000, 99_999_999),
            FullName = fullName,
            PasswordHash = PasswordService.HashPassword("matkhau123"),
            IsActive = true,
            RoleId = roleId,
            // Vai trò chính phải luôn có mặt ở bảng nối — đúng bất biến mà AuthService giữ.
            UserRoles = [new UserRole { RoleId = roleId }],
        };

        await factory.SeedAsync(db => db.Users.Add(user));

        return user;
    }

    private static async Task<RouteEntity> SeedRouteAsync(
        TestAppFactory factory,
        string code = "01",
        string name = "Bến Thành — Chợ Lớn")
    {
        var route = new RouteEntity
        {
            Id = Guid.NewGuid(),
            Code = code,
            Name = name,
            Origin = "Bến Thành",
            Destination = "Chợ Lớn",
        };

        await factory.SeedAsync(db => db.Routes.Add(route));

        return route;
    }

    private static async Task<Voucher> SeedVoucherAsync(
        TestAppFactory factory,
        string code,
        int quantity,
        int usedCount = 0,
        VoucherStatus status = VoucherStatus.Active,
        Guid? routeId = null)
    {
        var voucher = new Voucher
        {
            Id = Guid.NewGuid(),
            Code = code,
            Name = $"Voucher {code}",
            DiscountType = VoucherDiscountType.Percent,
            DiscountValue = 10m,
            MinOrderValue = 0m,
            RouteId = routeId,
            Quantity = quantity,
            UsedCount = usedCount,
            ValidFrom = TuNgay,
            ValidUntil = DenNgay,
            Status = status,
            CreatedAt = Moc1,
        };

        await factory.SeedAsync(db => db.Vouchers.Add(voucher));

        return voucher;
    }

    private static async Task SeedUsageAsync(
        TestAppFactory factory,
        Guid voucherId,
        Guid userId,
        decimal orderAmount = 150000m,
        decimal discountAmount = 15000m,
        string paymentCode = "PAY-001",
        DateTime? createdAt = null)
    {
        var usage = new VoucherUsage
        {
            Id = Guid.NewGuid(),
            VoucherId = voucherId,
            UserId = userId,
            PaymentCode = paymentCode,
            OrderAmount = orderAmount,
            DiscountAmount = discountAmount,
            CreatedAt = createdAt ?? Moc1,
        };

        await factory.SeedAsync(db => db.VoucherUsages.Add(usage));
    }
}
