using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartBus.Api.Data;
using SmartBus.Api.Entities;
using SmartBus.Api.Services;

namespace SmartBus.Tests;

/// <summary>
/// Ghim chỗ nối CỔNG THÔNG BÁO KẾT QUẢ XỬ LÝ yêu cầu huỷ/đổi vé vào app — hai dòng <c>Program.cs</c>
/// mà <c>docs/33-thong-bao-ket-qua-huy-doi-ve.md</c> §3.4 giao cho Hoàng (dòng 5 Sprint 4), và §3.5
/// chỉ đích danh file này. Cùng loại với <see cref="SeatHoldAbuseWiringTests"/> ("job nền có thật sự
/// được đăng ký") và <see cref="SeatMapRealtimeTests"/> ("ba interface phải trỏ vào ba lớp bọc").
///
/// Phần hành vi của cổng (chống ghi trùng, nội dung, nuốt lỗi) đã có
/// <see cref="TicketRequestResultNotifierTests"/> lo — file này chỉ trả lời câu mà test đơn vị
/// KHÔNG NHÌN TỚI: app có thật sự nối cổng đó vào không.
///
/// <para>
/// 🔴 Cả hai dòng thiếu đều là lỗi IM LẶNG, và đó là lý do phải có test:
/// </para>
/// <list type="bullet">
///   <item>Thiếu <c>AddScoped&lt;ITicketRequestResultNotifier, …&gt;</c> — không có triệu chứng nào ở
///   lúc build; endpoint duyệt ném <c>InvalidOperationException</c> ngay lượt PATCH đầu tiên (không
///   resolve được tham số constructor), tức là đỏ ở GIỮA luồng nghiệp vụ chứ không ở chỗ khai thiếu.</item>
///   <item>Thiếu <c>MapHub&lt;NotificationHub&gt;("/hubs/notifications")</c> — dòng <c>Notifications</c>
///   vẫn được ghi đầy đủ (khách còn đọc lại được), nhưng sự kiện SignalR đẩy vào một hub KHÔNG TỒN TẠI
///   nên <c>IHubContext</c> ném — và cổng nuốt lỗi theo hợp đồng, nên triệu chứng duy nhất là một dòng
///   log warning mỗi lượt duyệt. Không ai đọc log thì không ai biết phần realtime đã chết.</item>
/// </list>
/// </summary>
public class TicketRequestResultWiringTests
{
    private const string NegotiateUrl = "/hubs/notifications/negotiate?negotiateVersion=1";

    [Fact]
    public void Cong_duoc_dang_ky_bang_dung_lop_that()
    {
        using var factory = new TestAppFactory();

        // Qua scope chứ không resolve thẳng từ factory.Services — cùng lối SeatMapRealtimeTests, và
        // cũng đúng hình dạng lượt gọi thật (endpoint duyệt nằm trong một request scope).
        using var scope = factory.Services.CreateScope();

        // IsType chứ NotNull: một đăng ký trỏ nhầm lớp khác vẫn resolve được, vẫn biên dịch, mọi
        // endpoint vẫn 200 — chỉ có thông báo là ghi sai nội dung hoặc không đẩy. Phép so lớp là thứ
        // duy nhất bắt được ca đó.
        Assert.IsType<TicketRequestResultNotifier>(
            scope.ServiceProvider.GetRequiredService<ITicketRequestResultNotifier>());
    }

    [Fact]
    public async Task Post_negotiate_hub_thong_bao_doi_token_va_doc_token_tu_query_string()
    {
        using var factory = new TestAppFactory();
        using var client = factory.CreateClient();

        // Vế 1 — hub này có [Authorize], KHÁC /hubs/seat-map (khách chưa đăng nhập vẫn xem được sơ đồ
        // ghế, nên hub đó cố ý không gác). Thông báo là dữ liệu riêng của một người; bỏ [Authorize]
        // không làm đỏ thứ gì mà chỉ khiến ai cũng nối được vào kênh thông báo của người khác.
        // 401 ở đây cũng là bằng chứng MapHub đã chạy: hub không được map thì negotiate trả 404.
        var khongToken = await client.PostAsync(NegotiateUrl, content: null);
        Assert.Equal(HttpStatusCode.Unauthorized, khongToken.StatusCode);

        var khach = await TaoKhachAsync(factory);

        // Vế 2 — token đi bằng QUERY STRING chứ không phải header: trình duyệt không đặt được header
        // Authorization cho WebSocket, nên đây là đường duy nhất client SignalR JS đi được. Phép ghim
        // này phủ luôn ReadAccessTokenFromHubQueryAsync của JwtMiddleware — bỏ nhánh đọc đó thì mọi
        // lượt negotiate quay về 401 và KHÔNG client nào nối được (docs/33 §3.6, bảng ba dòng).
        //
        // Token phải kèm một người dùng CÓ THẬT trong CSDL: JwtMiddleware gắn user bằng cách đọc lại
        // từ bảng Users ở mỗi request (để khoá tài khoản có hiệu lực ngay), nên token đúng chữ ký mà
        // người dùng không tồn tại vẫn bị 401 — đúng lý do TaoKhachAsync seed trước khi ký.
        var token = factory.CreateTokenFor(khach);

        var coToken = await client.PostAsync(
            $"{NegotiateUrl}&access_token={Uri.EscapeDataString(token)}", content: null);
        Assert.Equal(HttpStatusCode.OK, coToken.StatusCode);
    }

    /// <summary>
    /// Một hành khách đủ để token hợp lệ — chép hình dạng của <c>TestAppFactory.SeedUserAsync</c> bên
    /// <see cref="TicketRequestReviewWiringTests"/>: vai trò phải có mặt trong CSDL trước, vì RBAC và
    /// middleware đọc vai trò ở tầng dữ liệu chứ không tin claim trong token.
    /// </summary>
    private static async Task<User> TaoKhachAsync(TestAppFactory factory)
    {
        await factory.SeedAsync(db =>
        {
            if (!db.Roles.Any(r => r.Id == RoleIds.Passenger))
            {
                db.Roles.Add(new Role
                {
                    Id = RoleIds.Passenger,
                    Code = RoleCodes.Passenger,
                    Name = RoleCodes.Passenger,
                });
            }
        });

        var khach = new User
        {
            Id = Guid.NewGuid(),
            PhoneNumber = "09" + Random.Shared.Next(10_000_000, 99_999_999),
            FullName = "Khách Nhận Thông Báo",
            PasswordHash = PasswordService.HashPassword("matkhau123"),
            IsActive = true,
            RoleId = RoleIds.Passenger,
            UserRoles = [new UserRole { RoleId = RoleIds.Passenger }],
        };

        await factory.SeedAsync(db => db.Users.Add(khach));

        return khach;
    }
}
