using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using SmartBus.Api.Entities;
using SmartBus.Api.Services;
using UserEntity = SmartBus.Api.Entities.User;

namespace SmartBus.Tests;

/// <summary>
/// Phân quyền bề mặt <c>/api/pass-types</c> — ranh giới đọc/ghi khác nhau có chủ ý:
///   • <c>GET</c> chỉ yêu cầu đăng nhập: màn đăng ký vé tháng của hành khách đọc danh mục thật
///     (tên/thời hạn/giá) từ bảng PassTypes thay vì giữ bảng hằng ở client.
///   • <c>POST</c>/<c>PUT</c>/<c>DELETE</c> vẫn sau policy <c>ManagerOrAbove</c> — sửa giá gói là
///     quyết định kinh doanh.
///
/// Hai chiều đều phải khoá: nới GET mà vô tình nới luôn quyền ghi là lỗ hổng, còn siết lại cả
/// GET (gắn policy ManagerOrAbove ở mức lớp như trước) thì hành khách không đăng ký được vé.
/// Hợp đồng ở mục "Loại vé tháng — /pass-types" của docs/api-contract.md.
/// </summary>
public class PassTypeAccessApiTests
{
    private const string Url = "/api/pass-types";

    [Fact]
    public async Task Khong_gui_token_thi_tra_401()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync(Url);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(RoleCodes.Passenger)]
    [InlineData(RoleCodes.Driver)]
    public async Task Vai_tro_khong_thuoc_quan_tri_van_doc_duoc_danh_muc(string roleCode)
    {
        using var factory = new TestAppFactory();
        var client = ClientWith(factory, factory.CreateTokenFor(
            await SeedUserAsync(factory, RoleIdFor(roleCode), roleCode)));
        await SeedPassTypeAsync(factory, "OneMonth", durationMonths: 1, price: 200_000m);

        var response = await client.GetAsync(Url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Đọc đúng dữ liệu CSDL, không phải bảng hằng của client: tên/thời hạn/giá đều của loại
        // vé vừa seed.
        var item = Assert.Single((await ReadJsonAsync(response)).EnumerateArray().ToArray());
        Assert.Equal("OneMonth", item.GetProperty("code").GetString());
        Assert.Equal("Vé OneMonth", item.GetProperty("name").GetString());
        Assert.Equal(1, item.GetProperty("durationMonths").GetInt32());
        Assert.Equal(200_000m, item.GetProperty("price").GetDecimal());
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    public async Task Hanh_khach_khong_ghi_duoc_danh_muc(string method)
    {
        using var factory = new TestAppFactory();
        var client = ClientWith(factory, factory.CreateTokenFor(
            await SeedUserAsync(factory, RoleIds.Passenger, RoleCodes.Passenger)));
        var passType = await SeedPassTypeAsync(factory, "OneMonth", durationMonths: 1, price: 200_000m);

        var request = new HttpRequestMessage(
            new HttpMethod(method),
            method == "POST" ? Url : $"{Url}/{passType.Id}");

        if (method != "DELETE")
        {
            request.Content = JsonContent.Create(new
            {
                code = "ThreeMonths",
                name = "Vé tháng 3 tháng",
                durationMonths = 3,
                price = 550_000m,
            });
        }

        var response = await client.SendAsync(request);

        // 403 chứ không phải 400/404: quyền bị chặn TRƯỚC khi controller đọc body hay tra id.
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
        => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static HttpClient ClientWith(TestAppFactory factory, string accessToken)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        return client;
    }

    private static Guid RoleIdFor(string roleCode) => roleCode switch
    {
        RoleCodes.Admin => RoleIds.Admin,
        RoleCodes.Manager => RoleIds.Manager,
        RoleCodes.Driver => RoleIds.Driver,
        _ => RoleIds.Passenger,
    };

    private static async Task<UserEntity> SeedUserAsync(
        TestAppFactory factory,
        Guid roleId,
        string roleCode)
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
            // Vai trò chính phải luôn có mặt ở bảng nối — đúng bất biến mà AuthService giữ.
            UserRoles = [new UserRole { RoleId = roleId }],
        };

        await factory.SeedAsync(db => db.Users.Add(user));

        return user;
    }

    private static async Task<PassType> SeedPassTypeAsync(
        TestAppFactory factory,
        string code,
        int durationMonths,
        decimal price)
    {
        var passType = new PassType
        {
            Id = Guid.NewGuid(),
            Code = code,
            Name = $"Vé {code}",
            DurationMonths = durationMonths,
            Price = price,
        };

        await factory.SeedAsync(db => db.PassTypes.Add(passType));

        return passType;
    }
}
