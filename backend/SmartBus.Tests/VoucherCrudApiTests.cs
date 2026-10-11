using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartBus.Api.Data;
using SmartBus.Api.Entities;
using SmartBus.Api.Services;
using RouteEntity = SmartBus.Api.Entities.Route;
using UserEntity = SmartBus.Api.Entities.User;

namespace SmartBus.Tests;

/// <summary>
/// Test tích hợp cho CRUD voucher — GET/POST /api/vouchers, GET/PUT /api/vouchers/{id} và
/// PATCH /api/vouchers/{id}/status (US 18, dòng 51 — Trần Trung Hiếu). Hợp đồng đầy đủ ở mục
/// "Voucher — /vouchers", phần "CRUD voucher" của docs/api-contract.md.
///
/// Mỗi test dựng một app riêng với CSDL InMemory riêng để không lẫn dữ liệu sang nhau.
/// Helper seed chép lại trong từng lớp test, không có lớp base chung (đúng lệ đang có của dự án).
/// </summary>
public class VoucherCrudApiTests
{
    // -------------------------------------------------------------- Quyền và xác thực

    [Fact]
    public async Task Khong_gui_token_thi_tra_401()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(VouchersUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync(VouchersUrl, BodyMau())).StatusCode);
    }

    [Fact]
    public async Task Hanh_khach_tra_403_vi_man_quan_ly_la_cua_quan_ly()
    {
        using var factory = new TestAppFactory();
        var hanhKhach = await SeedPassengerAsync(factory);
        var client = ClientWith(factory, factory.CreateTokenFor(hanhKhach));

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(VouchersUrl)).StatusCode);
    }

    // -------------------------------------------------------------- Danh sách + lọc + phân trang

    [Fact]
    public async Task Danh_sach_sap_theo_createdAt_giam_dan_va_tra_du_phan_trang()
    {
        using var factory = new TestAppFactory();
        var client = await ClientAsManagerAsync(factory);

        await SeedVoucherAsync(factory, "AAA", createdAt: Moc(1));
        await SeedVoucherAsync(factory, "BBB", createdAt: Moc(3));
        await SeedVoucherAsync(factory, "CCC", createdAt: Moc(2));

        var body = await GetJsonAsync(client, VouchersUrl);

        var items = body.GetProperty("items").EnumerateArray().ToArray();

        Assert.Equal(3, body.GetProperty("total").GetInt32());
        Assert.Equal(1, body.GetProperty("page").GetInt32());
        Assert.Equal(10, body.GetProperty("pageSize").GetInt32());
        Assert.Equal("BBB", items[0].GetProperty("code").GetString());
        Assert.Equal("CCC", items[1].GetProperty("code").GetString());
        Assert.Equal("AAA", items[2].GetProperty("code").GetString());
    }

    [Fact]
    public async Task Danh_sach_loc_theo_trang_thai_va_tim_khong_phan_biet_hoa_thuong()
    {
        using var factory = new TestAppFactory();
        var client = await ClientAsManagerAsync(factory);

        await SeedVoucherAsync(factory, "SUMMER10", status: VoucherStatus.Active);
        await SeedVoucherAsync(factory, "WEEKEND5K", status: VoucherStatus.Inactive);

        // Lọc trạng thái.
        var bodyTheoTrangThai = await GetJsonAsync(client, $"{VouchersUrl}?status=Inactive");
        var dong = Assert.Single(bodyTheoTrangThai.GetProperty("items").EnumerateArray());
        Assert.Equal("WEEKEND5K", dong.GetProperty("code").GetString());
        Assert.Equal(1, bodyTheoTrangThai.GetProperty("total").GetInt32());

        // Tìm theo mã — gõ thường vẫn ra (khớp cả mã lẫn tên).
        var bodyTimKiem = await GetJsonAsync(client, $"{VouchersUrl}?search=summer10");
        var dongTim = Assert.Single(bodyTimKiem.GetProperty("items").EnumerateArray());
        Assert.Equal("SUMMER10", dongTim.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Loc_trang_thai_sai_gia_tri_tra_400_kem_errors_status()
    {
        using var factory = new TestAppFactory();
        var client = await ClientAsManagerAsync(factory);

        var response = await client.GetAsync($"{VouchersUrl}?status=BANANA");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await BodyAsync(response);
        AssertLoiTheoField(body, "status");
    }

    // -------------------------------------------------------------- Tạo voucher

    [Fact]
    public async Task Tao_thanh_cong_tra_201_chuan_hoa_ma_HOA_va_ghi_nhat_ky()
    {
        using var factory = new TestAppFactory();
        var (client, quanLy) = await SignInAsManagerAsync(factory);

        var response = await client.PostAsJsonAsync(VouchersUrl, BodyMau(code: "summer10"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await BodyAsync(response);
        var id = body.GetProperty("id").GetGuid();

        // Mã chuẩn hoá CHỮ HOA; voucher mới luôn Active và chưa ai dùng.
        Assert.Equal("SUMMER10", body.GetProperty("code").GetString());
        Assert.Equal("Active", body.GetProperty("status").GetString());
        Assert.Equal(0, body.GetProperty("usedCount").GetInt32());
        Assert.Equal("Percent", body.GetProperty("discountType").GetString());

        // Location trỏ tới GET chi tiết — cùng lối AdminUserController.Create.
        Assert.Equal($"/api/vouchers/{id}", response.Headers.Location?.AbsolutePath);

        // Nhật ký kiểm toán: Create, đúng bảng Vouchers, đúng bản ghi vừa tạo (id đọc từ body trả về).
        var log = Assert.Single(await LogsAsync(factory));
        Assert.Equal(AuditAction.Create, log.Action);
        Assert.Equal($"Vouchers:{id}", log.Target);
        Assert.Equal(quanLy.Id, log.UserId);
    }

    [Fact]
    public async Task Tao_voi_dieu_kien_tuyen_tra_kem_routeCode_routeName_con_moi_tuyen_thi_null()
    {
        using var factory = new TestAppFactory();
        var client = await ClientAsManagerAsync(factory);

        var route = await SeedRouteAsync(factory, "01");

        var coTuyen = await BodyAsync(await client.PostAsJsonAsync(VouchersUrl, BodyMau(routeId: route.Id)));
        var moiTuyen = await BodyAsync(await client.PostAsJsonAsync(VouchersUrl, BodyMau(code: "ALLROUTES")));

        Assert.Equal(route.Id, coTuyen.GetProperty("routeId").GetGuid());
        Assert.Equal("01", coTuyen.GetProperty("routeCode").GetString());
        Assert.Equal("Tuyến 01", coTuyen.GetProperty("routeName").GetString());

        Assert.Equal(JsonValueKind.Null, moiTuyen.GetProperty("routeId").ValueKind);
        Assert.Equal(JsonValueKind.Null, moiTuyen.GetProperty("routeCode").ValueKind);
        Assert.Equal(JsonValueKind.Null, moiTuyen.GetProperty("routeName").ValueKind);
    }

    [Fact]
    public async Task Tao_trung_ma_tra_409_khong_phan_biet_hoa_thuong()
    {
        using var factory = new TestAppFactory();
        var client = await ClientAsManagerAsync(factory);

        await SeedVoucherAsync(factory, "SUMMER10");

        // "summer10" sau chuẩn hoá HOA chính là mã đã có — index của CSDL phân biệt hoa thường nên
        // lớp chuẩn hoá này là thứ giữ "duy nhất" cho cả đường gõ thường.
        var response = await client.PostAsJsonAsync(VouchersUrl, BodyMau(code: "summer10"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("Mã voucher đã tồn tại", (await BodyAsync(response)).GetProperty("message").GetString());
    }

    [Fact]
    public async Task Tao_Percent_sai_khoang_tra_400_kem_errors_discountValue()
    {
        using var factory = new TestAppFactory();
        var client = await ClientAsManagerAsync(factory);

        var response = await client.PostAsJsonAsync(VouchersUrl, BodyMau(discountValue: 150m));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AssertLoiTheoField(await BodyAsync(response), "discountValue");
    }

    [Fact]
    public async Task Tao_FixedAmount_bang_0_tra_400_va_tran_bi_ep_null()
    {
        using var factory = new TestAppFactory();
        var client = await ClientAsManagerAsync(factory);

        // Số tiền giảm phải lớn hơn 0.
        var sai = await client.PostAsJsonAsync(
            VouchersUrl, BodyMau(discountType: "FixedAmount", discountValue: 0m));

        Assert.Equal(HttpStatusCode.BadRequest, sai.StatusCode);
        AssertLoiTheoField(await BodyAsync(sai), "discountValue");

        // Trần với FixedAmount là khái niệm vô nghĩa — backend ép null dù client gửi lên.
        var dung = await client.PostAsJsonAsync(
            VouchersUrl, BodyMau(code: "FIX20K", discountType: "FixedAmount", discountValue: 20_000m, maxDiscount: 5000m));

        Assert.Equal(HttpStatusCode.Created, dung.StatusCode);
        Assert.Equal(JsonValueKind.Null, (await BodyAsync(dung)).GetProperty("maxDiscount").ValueKind);
    }

    [Fact]
    public async Task Tao_Percent_voi_tran_0_tra_400_kem_errors_maxDiscount()
    {
        using var factory = new TestAppFactory();
        var client = await ClientAsManagerAsync(factory);

        // Trần 0đ vô hiệu hoá luôn voucher (Min(rounded, 0) = 0) — từ chối ngay từ cửa.
        var response = await client.PostAsJsonAsync(VouchersUrl, BodyMau(maxDiscount: 0m));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AssertLoiTheoField(await BodyAsync(response), "maxDiscount");
    }

    [Fact]
    public async Task Tao_het_han_truoc_bat_dau_tra_400_kem_errors_validUntil()
    {
        using var factory = new TestAppFactory();
        var client = await ClientAsManagerAsync(factory);

        var response = await client.PostAsJsonAsync(
            VouchersUrl, BodyMau(validFrom: Moc(10), validUntil: Moc(10)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AssertLoiTheoField(await BodyAsync(response), "validUntil");
    }

    [Fact]
    public async Task Tao_voi_tuyen_khong_ton_tai_tra_404()
    {
        using var factory = new TestAppFactory();
        var client = await ClientAsManagerAsync(factory);

        // Tuyến là tham chiếu cứng — GUID trỏ tới tuyến không tồn tại là lỗi phía gọi (404),
        // cùng lối tripId của POST /vouchers/validate.
        var response = await client.PostAsJsonAsync(VouchersUrl, BodyMau(routeId: Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Tao_thieu_ma_tra_400_kem_errors_code()
    {
        using var factory = new TestAppFactory();
        var client = await ClientAsManagerAsync(factory);

        var response = await client.PostAsJsonAsync(VouchersUrl, new { name = "Thiếu mã", discountType = "Percent", discountValue = 10m, quantity = 10, validFrom = Moc(1), validUntil = Moc(30) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AssertLoiTheoField(await BodyAsync(response), "code");
    }

    [Fact]
    public async Task Tao_loai_giam_gia_sai_tra_400_kem_errors_discountType()
    {
        using var factory = new TestAppFactory();
        var client = await ClientAsManagerAsync(factory);

        var response = await client.PostAsJsonAsync(VouchersUrl, BodyMau(discountType: "Banana"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AssertLoiTheoField(await BodyAsync(response), "discountType");
    }

    // -------------------------------------------------------------- Chi tiết voucher

    [Fact]
    public async Task Chi_tiet_tra_200_con_khong_ton_tai_tra_404()
    {
        using var factory = new TestAppFactory();
        var client = await ClientAsManagerAsync(factory);

        var voucher = await SeedVoucherAsync(factory, "SUMMER10");

        var dung = await client.GetAsync($"{VouchersUrl}/{voucher.Id}");
        Assert.Equal(HttpStatusCode.OK, dung.StatusCode);
        Assert.Equal("SUMMER10", (await BodyAsync(dung)).GetProperty("code").GetString());

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"{VouchersUrl}/{Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public async Task Chi_tiet_id_khong_phai_guid_tra_404_do_rang_buoc_route()
    {
        using var factory = new TestAppFactory();
        var client = await ClientAsManagerAsync(factory);

        // Ràng buộc {id:guid} chặn từ routing — "abc" không khớp route chi tiết nên 404; đây chính
        // là thứ giữ GET /vouchers/statistics không bị route chi tiết nuốt (xem hợp đồng).
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"{VouchersUrl}/abc")).StatusCode);
    }

    // -------------------------------------------------------------- Sửa voucher

    [Fact]
    public async Task Sua_thanh_cong_giu_nguyen_usedCount_va_ghi_nhat_ky()
    {
        using var factory = new TestAppFactory();
        var (client, quanLy) = await SignInAsManagerAsync(factory);

        var voucher = await SeedVoucherAsync(factory, "SUMMER10", usedCount: 7);

        var response = await client.PutAsJsonAsync(
            $"{VouchersUrl}/{voucher.Id}", BodyMau(code: "NEWNAME", quantity: 10, usedCountPhu: 999));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await BodyAsync(response);
        Assert.Equal("NEWNAME", body.GetProperty("code").GetString());
        Assert.Equal(10, body.GetProperty("quantity").GetInt32());

        // usedCount do backend giữ: gửi lên bị bỏ qua, giá trị cũ không bị đổi.
        Assert.Equal(7, body.GetProperty("usedCount").GetInt32());
        Assert.NotEqual(JsonValueKind.Null, body.GetProperty("updatedAt").ValueKind);

        var log = Assert.Single(await LogsAsync(factory));
        Assert.Equal(AuditAction.Update, log.Action);
        Assert.Equal($"Vouchers:{voucher.Id}", log.Target);
        Assert.Equal(quanLy.Id, log.UserId);
    }

    [Fact]
    public async Task Sua_quantity_duoi_usedCount_tra_400_kem_errors_quantity()
    {
        using var factory = new TestAppFactory();
        var client = await ClientAsManagerAsync(factory);

        var voucher = await SeedVoucherAsync(factory, "SUMMER10", quantity: 5, usedCount: 5);

        var response = await client.PutAsJsonAsync($"{VouchersUrl}/{voucher.Id}", BodyMau(quantity: 4));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AssertLoiTheoField(await BodyAsync(response), "quantity");
    }

    [Fact]
    public async Task Sua_trung_ma_voucher_khac_tra_409_con_giu_nguyen_ma_cua_minh_van_200()
    {
        using var factory = new TestAppFactory();
        var client = await ClientAsManagerAsync(factory);

        var voucher = await SeedVoucherAsync(factory, "SUMMER10");
        await SeedVoucherAsync(factory, "WEEKEND5K");

        // Trùng mã với voucher KHÁC → 409.
        var trung = await client.PutAsJsonAsync($"{VouchersUrl}/{voucher.Id}", BodyMau(code: "weekend5k"));
        Assert.Equal(HttpStatusCode.Conflict, trung.StatusCode);

        // Giữ nguyên mã của chính mình (gõ thường) → 200 — kiểm trùng phải loại trừ bản ghi đang sửa.
        var giuNguyen = await client.PutAsJsonAsync($"{VouchersUrl}/{voucher.Id}", BodyMau(code: "summer10"));
        Assert.Equal(HttpStatusCode.OK, giuNguyen.StatusCode);
        Assert.Equal("SUMMER10", (await BodyAsync(giuNguyen)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Sua_voucher_khong_ton_tai_tra_404()
    {
        using var factory = new TestAppFactory();
        var client = await ClientAsManagerAsync(factory);

        var response = await client.PutAsJsonAsync($"{VouchersUrl}/{Guid.NewGuid()}", BodyMau());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // -------------------------------------------------------------- Bật/tắt áp dụng

    [Fact]
    public async Task Doi_trang_thai_thanh_cong_va_ghi_nhat_ky()
    {
        using var factory = new TestAppFactory();
        var (client, quanLy) = await SignInAsManagerAsync(factory);

        var voucher = await SeedVoucherAsync(factory, "SUMMER10", status: VoucherStatus.Active);

        var tat = await client.PatchAsJsonAsync($"{VouchersUrl}/{voucher.Id}/status", new { status = "Inactive" });
        Assert.Equal(HttpStatusCode.OK, tat.StatusCode);
        Assert.Equal("Inactive", (await BodyAsync(tat)).GetProperty("status").GetString());

        var batLai = await client.PatchAsJsonAsync($"{VouchersUrl}/{voucher.Id}/status", new { status = "Active" });
        Assert.Equal(HttpStatusCode.OK, batLai.StatusCode);
        Assert.Equal("Active", (await BodyAsync(batLai)).GetProperty("status").GetString());

        // Hai lần đổi = hai dòng nhật ký Update, đúng bảng và đúng bản ghi.
        var logs = await LogsAsync(factory);
        Assert.Equal(2, logs.Count);
        Assert.All(logs, log =>
        {
            Assert.Equal(AuditAction.Update, log.Action);
            Assert.Equal($"Vouchers:{voucher.Id}", log.Target);
            Assert.Equal(quanLy.Id, log.UserId);
        });
    }

    [Fact]
    public async Task Doi_trang_thai_sai_gia_tri_tra_400_kem_errors_status()
    {
        using var factory = new TestAppFactory();
        var client = await ClientAsManagerAsync(factory);

        var voucher = await SeedVoucherAsync(factory, "SUMMER10");

        var response = await client.PatchAsJsonAsync($"{VouchersUrl}/{voucher.Id}/status", new { status = "Banana" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AssertLoiTheoField(await BodyAsync(response), "status");
    }

    [Fact]
    public async Task Doi_trang_thai_voucher_khong_ton_tai_tra_404()
    {
        using var factory = new TestAppFactory();
        var client = await ClientAsManagerAsync(factory);

        var response = await client.PatchAsJsonAsync($"{VouchersUrl}/{Guid.NewGuid()}/status", new { status = "Inactive" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // =======================================================================================
    // Helper — mỗi lớp test tự chép, không có lớp base chung (đúng lệ đang có của dự án)
    // =======================================================================================

    private const string VouchersUrl = "/api/vouchers";

    /// <summary>Body tạo/sửa hợp lệ — các trường trùng khớp bảng ràng buộc của hợp đồng mục CRUD voucher.</summary>
    private static object BodyMau(
        string code = "SUMMER10",
        string name = "Giảm giá mùa hè 10%",
        string discountType = "Percent",
        decimal discountValue = 10m,
        decimal? maxDiscount = 20_000m,
        Guid? routeId = null,
        int quantity = 500,
        DateTime? validFrom = null,
        DateTime? validUntil = null,
        int usedCountPhu = 0)
        => new
        {
            code,
            name,
            discountType,
            discountValue,
            minOrderValue = 0m,
            maxDiscount,
            routeId,
            quantity,
            validFrom = validFrom ?? Moc(1),
            validUntil = validUntil ?? Moc(30),
            // Trường lạ với backend — chứng minh usedCount gửi lên bị bỏ qua (khớp hợp đồng).
            usedCount = usedCountPhu,
        };

    private static HttpClient ClientWith(TestAppFactory factory, string accessToken)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        return client;
    }

    private static async Task<HttpClient> ClientAsManagerAsync(TestAppFactory factory)
        => (await SignInAsManagerAsync(factory)).Client;

    private static async Task<(HttpClient Client, UserEntity User)> SignInAsManagerAsync(TestAppFactory factory)
    {
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager);

        // Token phát thẳng bằng ITokenService, KHÔNG qua /api/auth/login — để nền nhật ký sạch,
        // không lẫn bản ghi Login vào phép đếm của từng ca (cùng lối AuditLogCoverageTests).
        return (ClientWith(factory, factory.CreateTokenFor(manager)), manager);
    }

    private static async Task<UserEntity> SeedPassengerAsync(TestAppFactory factory)
        => await SeedUserAsync(factory, RoleIds.Passenger, RoleCodes.Passenger);

    private static async Task<UserEntity> SeedUserAsync(TestAppFactory factory, Guid roleId, string roleCode)
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
            FullName = "Người Dùng Test",
            PasswordHash = PasswordService.HashPassword("matkhau123"),
            IsActive = true,
            RoleId = roleId,
            UserRoles = [new UserRole { RoleId = roleId }],
        };

        await factory.SeedAsync(db => db.Users.Add(user));

        return user;
    }

    private static async Task<RouteEntity> SeedRouteAsync(TestAppFactory factory, string code)
    {
        var route = new RouteEntity
        {
            Id = Guid.NewGuid(),
            Code = code,
            Name = $"Tuyến {code}",
            Origin = "Bến Thành",
            Destination = "Chợ Lớn",
        };

        await factory.SeedAsync(db => db.Routes.Add(route));

        return route;
    }

    private static async Task<Voucher> SeedVoucherAsync(
        TestAppFactory factory,
        string code,
        int quantity = 100,
        int usedCount = 0,
        VoucherStatus status = VoucherStatus.Active,
        DateTime? createdAt = null)
    {
        var voucher = new Voucher
        {
            Id = Guid.NewGuid(),
            Code = code,
            Name = $"Voucher {code}",
            DiscountType = VoucherDiscountType.Percent,
            DiscountValue = 10m,
            MinOrderValue = 0m,
            Quantity = quantity,
            UsedCount = usedCount,
            ValidFrom = Moc(1),
            ValidUntil = Moc(30),
            Status = status,
            CreatedAt = createdAt ?? Moc(1),
        };

        await factory.SeedAsync(db => db.Vouchers.Add(voucher));

        return voucher;
    }

    /// <summary>Mốc UTC — `day` càng lớn càng muộn.</summary>
    private static DateTime Moc(int day)
        => new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(day - 1);

    private static async Task<JsonElement> GetJsonAsync(HttpClient client, string url)
        => await BodyAsync(await client.GetAsync(url));

    private static async Task<JsonElement> BodyAsync(HttpResponseMessage response)
        => await response.Content.ReadFromJsonAsync<JsonElement>();

    /// <summary>
    /// Khẳng định body lỗi theo đúng khuôn { message, errors } có trường <paramref name="field"/> —
    /// JsonElement là kiểu giá trị nên không dùng Assert.NotNull được (xUnit2002); TryGetProperty
    /// vừa đủ để chứng minh trường lỗi tồn tại, kèm cả body thật khi đỏ cho dễ lần.
    /// </summary>
    private static void AssertLoiTheoField(JsonElement body, string field)
        => Assert.True(
            body.GetProperty("errors").TryGetProperty(field, out _),
            $"Kỳ vọng lỗi kèm trường {field} nhưng không thấy trong: {body.GetRawText()}");

    private static async Task<List<AuditLog>> LogsAsync(TestAppFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Sắp xếp tiếp theo Id vì nhiều bản ghi sinh trong cùng một mili giây nên CreatedAt bằng
        // nhau — chỉ OrderBy(CreatedAt) là thứ tự không xác định (cùng lối AuditLogCoverageTests).
        return await db.AuditLogs
            .AsNoTracking()
            .OrderBy(log => log.CreatedAt)
            .ThenBy(log => log.Id)
            .ToListAsync();
    }
}
