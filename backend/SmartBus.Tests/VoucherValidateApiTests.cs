using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartBus.Api.Data;
using SmartBus.Api.Entities;
using UserEntity = SmartBus.Api.Entities.User;

namespace SmartBus.Tests;

/// <summary>
/// Test tích hợp cho <c>POST /api/vouchers/validate</c> (dòng 52 + 53, US 18 — Nguyễn Duy Kiên).
///
/// Dùng lại <see cref="TestAppFactory"/> của JwtAuthTests: chạy trên app thật, mỗi test một CSDL
/// InMemory riêng. Endpoint yêu cầu đăng nhập nên mọi ca đều sign in trước (trừ ca 401).
///
/// 🔴 Ca quan trọng nhất của file này là <see cref="Ma_khong_dung_duoc_van_tra_200"/>: hình dạng API
/// của endpoint này KHÁC khuôn lỗi chung <c>{ message, errors }</c> của dự án. Mã bị từ chối trả
/// <b>200</b> kèm <c>valid: false</c>, không phải 400/404 — vì đây là endpoint xem trước, màn thanh
/// toán gọi mỗi lần khách gõ thêm một ký tự, nên "mã này không dùng được" là câu trả lời chứ không
/// phải request hỏng. Đổi ca này thành 4xx là làm hỏng màn thanh toán.
///
/// ⚠️ Bảng <c>Vouchers</c> chưa migrate (việc của Vàng Thị Dăm — docs/27-huong-dan-migrate-vouchers.md).
/// Bộ test chạy trên InMemory nên không chờ migration; đổi lại, InMemory không dựng unique index nên
/// chốt "mã duy nhất" không được phủ ở đây.
/// </summary>
public class VoucherValidateApiTests
{
    private const string ValidateUrl = "/api/vouchers/validate";

    private static readonly DateTime TuNgay = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime DenNgay = new(2026, 10, 31, 23, 59, 59, DateTimeKind.Utc);

    // ---------------------------------------------------------------------------------------
    // Phân quyền
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Khong_gui_token_thi_tra_401()
    {
        using var factory = new TestAppFactory();

        var response = await factory.CreateClient().PostAsJsonAsync(
            ValidateUrl, new { code = "SUMMER10", tripId = Guid.NewGuid(), orderAmount = 150000 });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ---------------------------------------------------------------------------------------
    // Body sai khuôn — 400, và đây mới là chỗ dùng khuôn { message, errors } chung
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Thieu_code_thi_tra_400()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsPassengerAsync(factory);

        var response = await client.PostAsJsonAsync(
            ValidateUrl, new { tripId = Guid.NewGuid(), orderAmount = 150000 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await ReadJsonAsync(response);

        Assert.Equal("Dữ liệu đầu vào không hợp lệ", body.GetProperty("message").GetString());
        Assert.Equal(["Mã voucher không được để trống"], ErrorMessagesOf(body, "code"));
    }

    [Fact]
    public async Task Thieu_tripId_thi_tra_400()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsPassengerAsync(factory);

        var response = await client.PostAsJsonAsync(
            ValidateUrl, new { code = "SUMMER10", orderAmount = 150000 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await ReadJsonAsync(response);
        Assert.Equal(["Chuyến xe không được để trống"], ErrorMessagesOf(body, "tripId"));
    }

    [Fact]
    public async Task Code_rong_thi_tra_400_kem_errors_code()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsPassengerAsync(factory);

        var response = await client.PostAsJsonAsync(
            ValidateUrl, new { code = "A", tripId = Guid.NewGuid(), orderAmount = 150000 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        // [StringLength(20, MinimumLength = 2)] của ValidateVoucherRequest — cùng khoảng mà bản nháp
        // VoucherFormModal.tsx (Hạnh) đang chặn ở FE, nên không có mã nào FE cho nhập mà API từ chối.
        var body = await ReadJsonAsync(response);
        Assert.Equal(["Mã voucher phải từ 2 đến 20 ký tự"], ErrorMessagesOf(body, "code"));
    }

    [Fact]
    public async Task OrderAmount_am_thi_tra_400()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsPassengerAsync(factory);

        var response = await client.PostAsJsonAsync(
            ValidateUrl, new { code = "SUMMER10", tripId = Guid.NewGuid(), orderAmount = -1 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await ReadJsonAsync(response);
        Assert.Equal(["Giá trị đơn hàng không hợp lệ"], ErrorMessagesOf(body, "orderAmount"));
    }

    // ---------------------------------------------------------------------------------------
    // Chuyến không tồn tại — 404, KHÁC hẳn "voucher bị từ chối"
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Chuyen_khong_ton_tai_thi_tra_404()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsPassengerAsync(factory);

        var response = await client.PostAsJsonAsync(
            ValidateUrl, new { code = "SUMMER10", tripId = Guid.NewGuid(), orderAmount = 150000 });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Không tìm thấy chuyến xe", await MessageAsync(response));
    }

    // ---------------------------------------------------------------------------------------
    // 🔴 Mã không dùng được = 200 kèm valid:false — chốt hình dạng API của cả tính năng
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Ma_khong_dung_duoc_van_tra_200()
    {
        using var factory = new TestAppFactory();
        var user = await SeedUserAsync(factory);
        var trip = await SeedTripAsync(factory);

        var client = ClientWith(factory, factory.CreateTokenFor(user));
        var response = await client.PostAsJsonAsync(
            ValidateUrl, new { code = "KHONG-CO", tripId = trip.Id, orderAmount = 150000 });

        // 200 chứ không phải 404: mã không tồn tại cũng là một CÂU TRẢ LỜI của phép kiểm. Màn thanh
        // toán gọi endpoint này trong lúc khách gõ; trả 404 ở đây là biến mỗi lần gõ dở thành lỗi hệ
        // thống. (404 chỉ dành cho tripId — xem ca trên.)
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await ReadJsonAsync(response);

        Assert.False(body.GetProperty("valid").GetBoolean());
        Assert.Equal("NotFound", body.GetProperty("reasonCode").GetString());
        Assert.Equal("Mã voucher không tồn tại", body.GetProperty("message").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("voucherId").ValueKind);
        Assert.Equal(0m, body.GetProperty("discountAmount").GetDecimal());
        Assert.Equal(150000m, body.GetProperty("finalAmount").GetDecimal());
    }

    [Fact]
    public async Task Ma_het_hieu_luc_van_tra_200_kem_reasonCode_Expired()
    {
        using var factory = new TestAppFactory();
        var user = await SeedUserAsync(factory);
        var trip = await SeedTripAsync(factory);
        await SeedVoucherAsync(factory, new Voucher
        {
            Code = "HETHAN",
            Name = "Voucher hết hạn",
            DiscountType = VoucherDiscountType.Percent,
            DiscountValue = 10m,
            Quantity = 100,
            ValidFrom = TuNgay.AddDays(-30),
            ValidUntil = TuNgay.AddDays(-1),
        });

        var client = ClientWith(factory, factory.CreateTokenFor(user));
        var body = await ReadJsonAsync(await client.PostAsJsonAsync(
            ValidateUrl, new { code = "HETHAN", tripId = trip.Id, orderAmount = 150000 }));

        Assert.False(body.GetProperty("valid").GetBoolean());
        Assert.Equal("Expired", body.GetProperty("reasonCode").GetString());
        // Voucher CÓ thật: mã và id vẫn trả về để FE hiện được "mã HETHAN đã hết hiệu lực".
        Assert.Equal("HETHAN", body.GetProperty("code").GetString());
        Assert.Equal(JsonValueKind.String, body.GetProperty("voucherId").ValueKind);
    }

    // ---------------------------------------------------------------------------------------
    // Mã dùng được — số tiền trả về đúng bằng số test thuần đã ghim
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Ma_hop_le_tra_ve_so_tien_duoc_giam()
    {
        using var factory = new TestAppFactory();
        var user = await SeedUserAsync(factory);
        var trip = await SeedTripAsync(factory);
        await SeedVoucherAsync(factory, new Voucher
        {
            Code = "SUMMER10",
            Name = "Giảm 10%",
            DiscountType = VoucherDiscountType.Percent,
            DiscountValue = 10m,
            MinOrderValue = 100000m,
            Quantity = 100,
            ValidFrom = TuNgay,
            ValidUntil = DenNgay,
        });

        var client = ClientWith(factory, factory.CreateTokenFor(user));
        var response = await client.PostAsJsonAsync(
            ValidateUrl, new { code = "summer10", tripId = trip.Id, orderAmount = 175000 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await ReadJsonAsync(response);

        Assert.True(body.GetProperty("valid").GetBoolean());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("reasonCode").ValueKind);
        // Gõ chữ thường vẫn ra, và mã trả về là dạng CHUẨN đã lưu — FE hiện lại đúng mã đang áp dụng.
        Assert.Equal("SUMMER10", body.GetProperty("code").GetString());
        Assert.Equal("Percent", body.GetProperty("discountType").GetString());
        // 175.000 × 10% = 17.500
        Assert.Equal(17500m, body.GetProperty("discountAmount").GetDecimal());
        Assert.Equal(157500m, body.GetProperty("finalAmount").GetDecimal());
    }

    [Fact]
    public async Task FixedAmount_lon_hon_don_thi_khach_tra_khong_dong_nao()
    {
        using var factory = new TestAppFactory();
        var user = await SeedUserAsync(factory);
        var trip = await SeedTripAsync(factory);
        await SeedVoucherAsync(factory, new Voucher
        {
            Code = "GIAM50K",
            Name = "Giảm 50.000",
            DiscountType = VoucherDiscountType.FixedAmount,
            DiscountValue = 50000m,
            Quantity = 100,
            ValidFrom = TuNgay,
            ValidUntil = DenNgay,
        });

        var client = ClientWith(factory, factory.CreateTokenFor(user));
        var body = await ReadJsonAsync(await client.PostAsJsonAsync(
            ValidateUrl, new { code = "GIAM50K", tripId = trip.Id, orderAmount = 20000 }));

        // Phép kẹp cuối cùng của DiscountFor: giảm 50.000 cho đơn 20.000 ⇒ giảm đúng 20.000, khách
        // trả 0. Thiếu nó thì finalAmount = -30.000 và số tiền âm chảy xuống cổng.
        Assert.Equal(20000m, body.GetProperty("discountAmount").GetDecimal());
        Assert.Equal(0m, body.GetProperty("finalAmount").GetDecimal());
    }

    [Fact]
    public async Task Kiem_tra_hai_lan_lien_tiep_khong_tieu_thu_luot_nao()
    {
        using var factory = new TestAppFactory();
        var user = await SeedUserAsync(factory);
        var trip = await SeedTripAsync(factory);
        await SeedVoucherAsync(factory, new Voucher
        {
            Code = "SUMMER10",
            Name = "Giảm 10%",
            DiscountType = VoucherDiscountType.Percent,
            DiscountValue = 10m,
            Quantity = 100,
            ValidFrom = TuNgay,
            ValidUntil = DenNgay,
        });

        var client = ClientWith(factory, factory.CreateTokenFor(user));

        for (var lan = 0; lan < 2; lan++)
        {
            var response = await client.PostAsJsonAsync(
                ValidateUrl, new { code = "SUMMER10", tripId = trip.Id, orderAmount = 150000 });

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Endpoint này bị gọi mỗi lần khách gõ thêm một ký tự. Nếu nó ghi UsedCount thì một khách gõ
        // mã 10 ký tự là tiêu thụ sạch 10 lượt của voucher.
        Assert.Equal(0, await db.Vouchers.Select(v => v.UsedCount).SingleAsync());
        Assert.Empty(db.VoucherUsages);
    }

    // ---------------------------------------------------------------------------------------
    // Hình dạng response — khoá lại đúng những gì docs/api-contract.md đã chốt
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Response_chi_tra_dung_cac_truong_trong_hop_dong()
    {
        using var factory = new TestAppFactory();
        var user = await SeedUserAsync(factory);
        var trip = await SeedTripAsync(factory);

        var client = ClientWith(factory, factory.CreateTokenFor(user));
        var body = await ReadJsonAsync(await client.PostAsJsonAsync(
            ValidateUrl, new { code = "KHONG-CO", tripId = trip.Id, orderAmount = 150000 }));

        // Thêm một trường vào response là ĐỔI HÌNH DẠNG API (⛔5): phải sửa api-contract.md trước rồi
        // báo người viết frontend. Ca này làm đổ test ngay lúc đó, để việc sửa hợp đồng là một quyết
        // định có ý thức. VoucherValidationResponse là hình dạng mà BA màn thanh toán (dòng 56–58)
        // cùng đọc, nên đổi ở đây là đổi cả ba.
        Assert.Equal(
            [
                "code",
                "discountAmount",
                "discountType",
                "finalAmount",
                "maxDiscount",
                "message",
                "reasonCode",
                "valid",
                "voucherId",
            ],
            PropertyNamesOf(body));
    }

    // ---------------------------------------------------------------------------------------
    // Helper — mỗi lớp test tự chép, không có lớp base chung (đúng lệ đang có của dự án)
    // ---------------------------------------------------------------------------------------

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
        => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task<string> MessageAsync(HttpResponseMessage response)
    {
        var body = await ReadJsonAsync(response);
        return body.GetProperty("message").GetString() ?? string.Empty;
    }

    private static string[] ErrorMessagesOf(JsonElement body, string field)
        => body.GetProperty("errors").GetProperty(field).EnumerateArray()
            .Select(message => message.GetString() ?? string.Empty)
            .ToArray();

    private static string[] PropertyNamesOf(JsonElement element)
        => element.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray();

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

    /// <summary>
    /// Chuyến kèm tuyến của nó — điều kiện "tuyến" của dòng 53 đọc thẳng <c>Trip.RouteId</c>, nên
    /// chuyến ở đây phải có tuyến thật (khác lệ "seed dòng mồ côi" ở vài file test khác).
    /// </summary>
    private static async Task<Trip> SeedTripAsync(TestAppFactory factory, Guid? routeId = null)
    {
        var trip = new Trip
        {
            Id = Guid.NewGuid(),
            RouteId = routeId ?? Guid.NewGuid(),
            BusId = Guid.NewGuid(),
            DepartureTime = TuNgay,
        };

        await factory.SeedAsync(db => db.Trips.Add(trip));

        return trip;
    }

    private static async Task SeedVoucherAsync(TestAppFactory factory, Voucher voucher)
    {
        voucher.CreatedAt = TuNgay;
        await factory.SeedAsync(db => db.Vouchers.Add(voucher));
    }
}
