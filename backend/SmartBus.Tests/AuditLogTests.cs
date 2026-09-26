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
/// Test tích hợp cho middleware tự động ghi nhật ký mọi thao tác thay đổi dữ liệu
/// (task story 23 — Vàng Thị Dăm). Dùng lại <see cref="TestAppFactory"/>: chạy trên app thật,
/// CSDL InMemory riêng cho mỗi test.
/// </summary>
public class AuditLogTests
{
    private const string WidgetsUrl = "/api/_test/audit/widgets";

    [Fact]
    public async Task Post_tao_moi_ghi_nhat_ky_kem_id_cua_ban_ghi_vua_tao()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        var response = await client.PostAsync(WidgetsUrl, content: null);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var createdId = await IdFromAsync(response);

        var log = Assert.Single(await LogsAsync(factory));
        Assert.Equal(AuditAction.Create, log.Action);

        // Id bản ghi vừa tạo chỉ nằm trong body trả về, không có trên đường dẫn. Đây là chốt chặn
        // cho cả cách làm: middleware phải đọc được id mà vẫn để nguyên response cho client.
        Assert.Equal($"Widgets:{createdId}", log.Target);
    }

    [Fact]
    public async Task Post_khong_co_id_trong_body_thi_van_ghi_nhat_ky_voi_ten_bang()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        var response = await client.PostAsync("/api/_test/audit/widgets-no-id", content: null);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        // Bảng AuditLogs không có cột đường dẫn nên Target là chỗ duy nhất nói thao tác chạm vào đâu.
        // Thiếu id là chuyện nhỏ; mất luôn tên bảng mới làm dòng nhật ký hết giá trị.
        var log = Assert.Single(await LogsAsync(factory));
        Assert.Equal("WidgetsNoId", log.Target);
    }

    [Fact]
    public async Task Put_va_Patch_deu_ghi_thanh_Update_voi_id_tren_duong_dan()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();
        var id = Guid.NewGuid();

        await client.PutAsync($"{WidgetsUrl}/{id}", content: null);
        await client.PatchAsync($"{WidgetsUrl}/{id}", content: null);

        var logs = await LogsAsync(factory);

        Assert.Equal(2, logs.Count);
        Assert.All(logs, log => Assert.Equal(AuditAction.Update, log.Action));
        Assert.All(logs, log => Assert.Equal($"Widgets:{id}", log.Target));
    }

    [Fact]
    public async Task Delete_ghi_thanh_Delete()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();
        var id = Guid.NewGuid();

        var response = await client.DeleteAsync($"{WidgetsUrl}/{id}");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var log = Assert.Single(await LogsAsync(factory));
        Assert.Equal(AuditAction.Delete, log.Action);
        Assert.Equal($"Widgets:{id}", log.Target);
    }

    [Fact]
    public async Task Get_khong_ghi_nhat_ky()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        await client.GetAsync($"{WidgetsUrl}/{Guid.NewGuid()}");

        // Nhật ký là "thao tác THAY ĐỔI dữ liệu". Ghi cả lượt đọc thì bảng phình lên vì
        // những thao tác không làm gì đổi, và bản ghi đáng chú ý bị chìm giữa rác.
        Assert.Empty(await LogsAsync(factory));
    }

    [Fact]
    public async Task Thao_tac_that_bai_khong_ghi_nhat_ky()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        var response = await client.PostAsync($"{WidgetsUrl}/that-bai", content: null);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        Assert.Empty(await LogsAsync(factory));
    }

    [Fact]
    public async Task Response_cua_Post_van_nguyen_ven_sau_khi_middleware_doc_id()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        var response = await client.PostAsync(WidgetsUrl, content: null);

        // Middleware tạm thay stream của response để đọc id. Quên trả lại nội dung là hỏng nặng
        // nhất mà cách làm này có thể gây ra, nên body phải được khẳng định còn nguyên vẹn.
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Widget vừa tạo", body.RootElement.GetProperty("name").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("id").GetString()));
    }

    [Fact]
    public async Task Duong_dan_long_nhau_ghi_dung_ten_bang_cua_ban_ghi()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        var response = await client.PostAsync(
            $"/api/_test/audit/routes/{Guid.NewGuid()}/widgets",
            content: null);

        var createdId = await IdFromAsync(response);

        // Lấy đoạn đầu tiên của đường dẫn sẽ ra "Routes" — sai bảng, vì thao tác này tạo một Widget.
        // Đây là ca thật của dự án: PUT /api/routes/{routeId}/fares/{id} tác động lên bảng Fares.
        var log = Assert.Single(await LogsAsync(factory));
        Assert.Equal($"Widgets:{createdId}", log.Target);
    }

    [Fact]
    public async Task Doan_cuoi_la_hanh_dong_thi_ghi_ten_tai_nguyen_khong_phai_ten_hanh_dong()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        var response = await client.PutAsync(
            $"/api/_test/audit/routes/{Guid.NewGuid()}/widgets/order",
            content: null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Cùng họ với ca trên, nhưng khó hơn: đường dẫn có tham số cha mà KHÔNG có {id}, đoạn cuối
        // lại là tên hành động. Lấy đoạn tĩnh cuối cùng sẽ ra Target là "Order" — không ứng với
        // bảng nào, và vì không có {id} nên mất luôn id đối tượng; dòng nhật ký chỉ còn "có người
        // đã sửa một thứ gì đó". Đây là ca thật của dự án:
        // PUT /api/routes/{routeId}/stops/order.
        var log = Assert.Single(await LogsAsync(factory));
        Assert.Equal(AuditAction.Update, log.Action);
        Assert.Equal("Widgets", log.Target);
    }

    [Fact]
    public async Task Post_vao_api_auth_khong_bi_ghi_lan_hai()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        // Đăng xuất với refresh token không tồn tại vẫn trả 204 (đăng xuất phải idempotent),
        // tức đây là một POST THÀNH CÔNG nằm dưới /api/auth.
        var response = await client.PostAsJsonAsync("/api/auth/logout", new { refreshToken = "khong-ton-tai" });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // Đăng nhập/đăng xuất là hành động Login/Logout, không suy ra được từ HTTP verb nên do
        // luồng xác thực tự ghi (task của Hiếu). Middleware ghi thêm sẽ tạo bản ghi thứ hai,
        // với đối tượng là "Auth" — một bảng không tồn tại.
        Assert.Empty(await LogsAsync(factory));
    }

    [Fact]
    public async Task Khong_xac_dinh_duoc_nguoi_thuc_hien_thi_van_ghi_nhat_ky_voi_UserId_null()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        await client.PostAsync(WidgetsUrl, content: null);

        // Không có token nên không biết ai làm. Nhưng dấu vết "có người đã đổi dữ liệu lúc nào"
        // vẫn phải còn — bỏ hẳn bản ghi vì thiếu người thực hiện là mất luôn dấu vết đó.
        var log = Assert.Single(await LogsAsync(factory));
        Assert.Null(log.UserId);
    }

    [Fact]
    public async Task Co_token_thi_ghi_duoc_nguoi_thuc_hien()
    {
        using var factory = new TestAppFactory();
        var role = NewRole(RoleCodes.Passenger);
        var user = NewUser(role);
        await factory.SeedAsync(db =>
        {
            db.Roles.Add(role);
            db.Users.Add(user);
        });

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", factory.CreateTokenFor(user));

        await client.PostAsync(WidgetsUrl, content: null);

        // Người thực hiện lấy từ entity mà JwtMiddleware đã nạp vào HttpContext.Items — không
        // truy vấn lại CSDL, và cũng không tin claim trong token (claim cũ sau khi đổi vai trò).
        var log = Assert.Single(await LogsAsync(factory));
        Assert.Equal(user.Id, log.UserId);
    }

    /// <summary>Đọc nhật ký bằng scope mới — middleware ghi bằng scope riêng của nó.</summary>
    private static async Task<List<AuditLog>> LogsAsync(TestAppFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await db.AuditLogs
            .AsNoTracking()
            .OrderBy(log => log.CreatedAt)
            .ToListAsync();
    }

    private static async Task<string> IdFromAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("id").GetString()!;
    }

    private static Role NewRole(string code) => new() { Id = Guid.NewGuid(), Code = code, Name = code };

    private static UserEntity NewUser(Role role) => new()
    {
        Id = Guid.NewGuid(),
        PhoneNumber = "09" + Guid.NewGuid().ToString("N")[..8],
        FullName = "Người Dùng Test",
        PasswordHash = "hash-khong-dung-toi-trong-test",
        IsActive = true,
        RoleId = role.Id,
        Role = role,
    };
}
