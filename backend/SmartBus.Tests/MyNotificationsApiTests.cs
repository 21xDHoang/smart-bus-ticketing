using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using SmartBus.Api.Entities;
using UserEntity = SmartBus.Api.Entities.User;

namespace SmartBus.Tests;

/// <summary>
/// Test tích hợp cho API lịch sử thông báo đã nhận của hành khách — <c>GET /api/notifications/me</c>
/// (US 10, task *"API lịch sử thông báo đã nhận của hành khách"*, dòng 19 Sprint 5 — Vàng Thị Dăm).
/// Hợp đồng đầy đủ ở mục "Thông báo trong ứng dụng — /notifications" của docs/api-contract.md.
///
/// Dùng lại <see cref="TestAppFactory"/> của JwtAuthTests: chạy trên app thật, mỗi test một CSDL
/// InMemory riêng. Endpoint yêu cầu đăng nhập nên mọi ca đều sign in trước (trừ ca 401).
/// Helper seed chép lại trong từng lớp test, không có lớp base chung (đúng lệ đang có của dự án).
///
/// ⚠️ Giới hạn của provider InMemory ảnh hưởng tới cách đọc kết quả ở đây:
///   • KHÔNG dựng khoá ngoại, nên seed được dòng Notification trỏ tới người dùng không tồn tại —
///     không ảnh hưởng endpoint này vì nó chỉ ĐỌC.
///   • KHÔNG dựng chỉ mục <c>IX_Notifications_UserId_CreatedAt</c>. Việc truy vấn dùng đúng chỉ mục
///     đó là chuyện của migration, không test nào phủ ở đây; muốn kiểm thì xem
///     <c>EXPLAIN</c> trên PostgreSQL thật.
///   • <c>OrderByDescending(CreatedAt).ThenBy(Id)</c> chạy trong bộ nhớ của provider, nên thứ tự
///     TẤT ĐỊNH kiểm được, còn thứ tự vật lý của PostgreSQL thì không.
/// </summary>
public class MyNotificationsApiTests
{
    private const string ListUrl = "/api/notifications/me";

    // ---------------------------------------------------------------------------------------
    // Xác thực
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Khong_gui_token_thi_tra_401()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(ListUrl)).StatusCode);
    }

    // ---------------------------------------------------------------------------------------
    // Danh sách của tôi — mảng trần, không phân trang, rỗng cũng là 200
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Chua_nhan_thong_bao_nao_thi_tra_mang_rong_khong_phai_404()
    {
        using var factory = new TestAppFactory();
        var client = await ClientAsPassengerAsync(factory);

        var response = await client.GetAsync(ListUrl);

        // 🔴 Tài khoản vừa đăng ký là ca phổ biến nhất của endpoint này, và "tôi chưa có thông báo
        // nào" là một câu trả lời bình thường — không phải sự cố. Trả 404 ở đây là bắt frontend đi
        // phân biệt "chưa có gì" với "gọi sai đường dẫn", mà chuông thì không có gì để phân biệt.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Array, body.RootElement.ValueKind);
        Assert.Equal(0, body.RootElement.GetArrayLength());
    }

    [Fact]
    public async Task Danh_sach_chi_tra_thong_bao_cua_chinh_nguoi_goi()
    {
        using var factory = new TestAppFactory();
        var chuHopThu = await SeedUserAsync(factory, "Nguyễn Văn Chủ");
        var nguoiKhac = await SeedUserAsync(factory, "Trần Thị Khác");
        var client = ClientWith(factory, factory.CreateTokenFor(chuHopThu));

        await SeedNotificationAsync(factory, chuHopThu.Id, "TB-CUA-TOI", createdAt: Ngay(3));
        await SeedNotificationAsync(factory, nguoiKhac.Id, "TB-CUA-NGUOI-KHAC", createdAt: Ngay(4));

        var items = (await ReadJsonAsync(await client.GetAsync(ListUrl))).EnumerateArray().ToArray();

        // Đúng một dòng của chủ hộp thư — thông báo của người khác không lọt vào, và KHÔNG có tham số
        // nào để hỏi thông báo của người khác được (phạm vi "của tôi" nằm trong truy vấn theo userId).
        // Đây là chuyện quyền đọc chứ không phải chuyện lịch sự: thông báo là kết quả duyệt yêu cầu
        // huỷ vé CỦA MỘT NGƯỜI, kèm cả lý do từ chối.
        Assert.Single(items);
        Assert.Equal("TB-CUA-TOI", items[0].GetProperty("title").GetString());

        // Và thông báo của người khác vẫn nằm nguyên trong bảng, không bị test này xoá nhầm.
        var itemsCuaNguoiKhac = (await ReadJsonAsync(
            await ClientWith(factory, factory.CreateTokenFor(nguoiKhac)).GetAsync(ListUrl)))
            .EnumerateArray().ToArray();
        Assert.Equal("TB-CUA-NGUOI-KHAC", itemsCuaNguoiKhac[0].GetProperty("title").GetString());
    }

    // ---------------------------------------------------------------------------------------
    // Thứ tự — mới nhất trước
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Sap_moi_nhat_truoc()
    {
        using var factory = new TestAppFactory();
        var chuHopThu = await SeedUserAsync(factory, "Nguyễn Văn Chủ");
        var client = ClientWith(factory, factory.CreateTokenFor(chuHopThu));

        await SeedNotificationAsync(factory, chuHopThu.Id, "TB-CU-NHAT", createdAt: Ngay(1));
        await SeedNotificationAsync(factory, chuHopThu.Id, "TB-MOI-NHAT", createdAt: Ngay(9));
        await SeedNotificationAsync(factory, chuHopThu.Id, "TB-GIUA", createdAt: Ngay(5));

        var items = (await ReadJsonAsync(await client.GetAsync(ListUrl))).EnumerateArray().ToArray();

        // Màn danh sách đọc mới nhất trước — chuông vừa reo thì thông báo vừa reo phải đứng đầu.
        Assert.Equal(
            ["TB-MOI-NHAT", "TB-GIUA", "TB-CU-NHAT"],
            items.Select(item => item.GetProperty("title").GetString()!).ToArray());
    }

    [Fact]
    public async Task Trung_CreatedAt_thi_thu_tu_on_dinh_giua_cac_luot_goi()
    {
        using var factory = new TestAppFactory();
        var chuHopThu = await SeedUserAsync(factory, "Nguyễn Văn Chủ");
        var client = ClientWith(factory, factory.CreateTokenFor(chuHopThu));

        // Ca có thật: một lượt duyệt phát thông báo rồi service hoàn tiền phát tiếp trong cùng một
        // giây. Bảng chỉ-ghi-thêm nên CreatedAt là cột duy nhất để sắp, và hai dòng bằng nhau thì
        // PostgreSQL được phép trả về thứ tự nào cũng đúng — ThenBy(Id) là thứ duy nhất chặn điều đó.
        var moc = Ngay(4);
        await SeedNotificationAsync(factory, chuHopThu.Id, "TB-CUNG-GIAY-A", createdAt: moc);
        await SeedNotificationAsync(factory, chuHopThu.Id, "TB-CUNG-GIAY-B", createdAt: moc);
        await SeedNotificationAsync(factory, chuHopThu.Id, "TB-CUNG-GIAY-C", createdAt: moc);

        var luot1 = (await ReadJsonAsync(await client.GetAsync(ListUrl)))
            .EnumerateArray().Select(item => item.GetProperty("title").GetString()).ToArray();
        var luot2 = (await ReadJsonAsync(await client.GetAsync(ListUrl)))
            .EnumerateArray().Select(item => item.GetProperty("title").GetString()).ToArray();

        // Không ghim thứ tự CỤ THỂ: Guid so sánh theo từng byte chứ không theo chuỗi, nên viết ra
        // đây một thứ tự mong đợi là ghim nhầm chi tiết cài đặt. Cái hợp đồng hứa — và cái làm test
        // của người khác khỏi chập chờn — là hai lượt gọi cùng dữ liệu phải ra cùng thứ tự.
        Assert.Equal(luot1, luot2);
        Assert.Equal(3, luot1.Length);
    }

    // ---------------------------------------------------------------------------------------
    // Hình dạng JSON — khoá chặt theo bản nháp frontend đã merge
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Tra_ve_dung_bay_truong_ma_frontend_dang_cho()
    {
        using var factory = new TestAppFactory();
        var chuHopThu = await SeedUserAsync(factory, "Nguyễn Văn Chủ");
        var client = ClientWith(factory, factory.CreateTokenFor(chuHopThu));

        await SeedNotificationAsync(factory, chuHopThu.Id, "TB-01");

        var item = (await ReadJsonAsync(await client.GetAsync(ListUrl))).EnumerateArray().Single();

        // Interface AppNotification trong frontend/src/api/notificationApi.ts (Nguyễn Đình Băng,
        // commit 5be51ed) đã code xong chuông + badge dựa trên ĐÚNG bộ tên này, viết thường chữ đầu.
        // TypeScript của họ không đọc được NotificationResponse.cs, nên đổi một chữ ở đây không có
        // lỗi biên dịch nào — chỉ có màn hình trắng. Ca này là hàng rào duy nhất, và nó ghim cả THỨ
        // TỰ khai báo trong NotificationResponse (System.Text.Json giữ nguyên thứ tự đó).
        string[] bayTruong =
            ["id", "type", "title", "body", "relatedEntityType", "relatedEntityId", "createdAt"];

        Assert.Equal(bayTruong, item.EnumerateObject().Select(p => p.Name).ToArray());
    }

    [Fact]
    public async Task Type_va_relatedEntityType_ra_chuoi_khong_ra_so()
    {
        using var factory = new TestAppFactory();
        var chuHopThu = await SeedUserAsync(factory, "Nguyễn Văn Chủ");
        var client = ClientWith(factory, factory.CreateTokenFor(chuHopThu));

        var yeuCauHuy = Guid.NewGuid();
        await SeedNotificationAsync(
            factory, chuHopThu.Id, "TB-01",
            type: NotificationType.CancellationApproved,
            relatedEntityType: NotificationTarget.CancellationRequest,
            relatedEntityId: yeuCauHuy);

        var item = (await ReadJsonAsync(await client.GetAsync(ListUrl))).EnumerateArray().Single();

        // 🔴 Bẫy đã ghi ở RefundResponse và TicketResponse: dự án KHÔNG bật JsonStringEnumConverter,
        // nên để enum trong DTO là JSON tuôn ra con số (0, 1, 2...) — frontend nhận số rồi so với
        // union chuỗi của họ thì luôn trượt, và badge im lặng không hiện gì. Service phải ToString()
        // trong bộ nhớ trước khi dựng DTO; ca này ghim đúng chỗ đó.
        Assert.Equal(JsonValueKind.String, item.GetProperty("type").ValueKind);
        Assert.Equal("CancellationApproved", item.GetProperty("type").GetString());

        Assert.Equal(JsonValueKind.String, item.GetProperty("relatedEntityType").ValueKind);
        Assert.Equal("CancellationRequest", item.GetProperty("relatedEntityType").GetString());
        Assert.Equal(yeuCauHuy, item.GetProperty("relatedEntityId").GetGuid());
    }

    [Fact]
    public async Task Thong_bao_khong_gan_ban_ghi_nao_thi_hai_truong_lien_ket_null()
    {
        using var factory = new TestAppFactory();
        var chuHopThu = await SeedUserAsync(factory, "Nguyễn Văn Chủ");
        var client = ClientWith(factory, factory.CreateTokenFor(chuHopThu));

        // Thông báo hệ thống của Sprint 5 (bảo trì, đổi chính sách) không trỏ vào bản ghi nào.
        await SeedNotificationAsync(
            factory, chuHopThu.Id, "TB-HE-THONG",
            relatedEntityType: null, relatedEntityId: null);

        var item = (await ReadJsonAsync(await client.GetAsync(ListUrl))).EnumerateArray().Single();

        // 🔴 Null, KHÔNG phải chuỗi rỗng và KHÔNG phải Guid.Empty: frontend phân biệt "thông báo này
        // không có gì để mở" với "bấm vào thì mở màn X". Trả Guid.Empty là bắt họ đi mở một bản ghi
        // không tồn tại rồi nhận 404.
        Assert.Equal(JsonValueKind.Null, item.GetProperty("relatedEntityType").ValueKind);
        Assert.Equal(JsonValueKind.Null, item.GetProperty("relatedEntityId").ValueKind);
    }

    [Fact]
    public async Task Body_nhieu_dong_giu_nguyen_ky_tu_xuong_dong()
    {
        using var factory = new TestAppFactory();
        var chuHopThu = await SeedUserAsync(factory, "Nguyễn Văn Chủ");
        var client = ClientWith(factory, factory.CreateTokenFor(chuHopThu));

        const string lyDoTuChoi = "Yêu cầu huỷ vé đã bị từ chối.\nLý do: quá sát giờ khởi hành.\n"
            + "Vé vẫn còn hiệu lực, quý khách vui lòng dùng vé như đã đặt.";

        await SeedNotificationAsync(
            factory, chuHopThu.Id, "TB-01",
            type: NotificationType.CancellationRejected,
            body: lyDoTuChoi);

        var item = (await ReadJsonAsync(await client.GetAsync(ListUrl))).EnumerateArray().Single();

        // A8.5: thông báo phải ĐỦ ĐỂ ĐỌC MỘT MÌNH — lý do từ chối do quản lý nhập được chép nguyên
        // văn vào body, và phần thân đi kèm ngay trong danh sách chứ không nằm sau một endpoint chi
        // tiết. Xuống dòng là phần của nội dung, không phải rác định dạng.
        Assert.Equal(lyDoTuChoi, item.GetProperty("body").GetString());
    }

    [Fact]
    public async Task Ca_sau_gia_tri_NotificationType_deu_di_qua_duoc()
    {
        using var factory = new TestAppFactory();
        var chuHopThu = await SeedUserAsync(factory, "Nguyễn Văn Chủ");
        var client = ClientWith(factory, factory.CreateTokenFor(chuHopThu));

        var tatCa = Enum.GetValues<NotificationType>();
        var ngay = Ngay(1);
        foreach (var type in tatCa)
        {
            await SeedNotificationAsync(factory, chuHopThu.Id, $"TB-{type}", type: type, createdAt: ngay);
        }

        var items = (await ReadJsonAsync(await client.GetAsync(ListUrl))).EnumerateArray().ToArray();

        // Đủ cả SÁU giá trị, kể cả RefundSucceeded và RefundFailed mà frontend CHƯA khai trong union
        // của họ (đã báo ở PR). Endpoint trả đủ; việc thiếu nhãn là phía màn hình, không phải phía
        // API — nếu ca này đỏ vì ai đó lọc bớt loại thì đó là hồi quy thật.
        Assert.Equal(
            tatCa.Select(type => type.ToString()).OrderBy(name => name, StringComparer.Ordinal).ToArray(),
            items.Select(item => item.GetProperty("type").GetString()!)
                .OrderBy(name => name, StringComparer.Ordinal).ToArray());

        // Và không giá trị nào tuôn ra dạng số.
        Assert.All(items, item => Assert.Equal(JsonValueKind.String, item.GetProperty("type").ValueKind));
    }

    [Fact]
    public async Task CreatedAt_ra_chuoi_ISO_8601()
    {
        using var factory = new TestAppFactory();
        var chuHopThu = await SeedUserAsync(factory, "Nguyễn Văn Chủ");
        var client = ClientWith(factory, factory.CreateTokenFor(chuHopThu));

        var moc = new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc);
        await SeedNotificationAsync(factory, chuHopThu.Id, "TB-01", createdAt: moc);

        var item = (await ReadJsonAsync(await client.GetAsync(ListUrl))).EnumerateArray().Single();

        // System.Text.Json tự ra ISO 8601 — không có converter riêng nào cho trường này. Ca này chỉ
        // ghim rằng nó vẫn còn là chuỗi (frontend gọi new Date(...) thẳng lên đó).
        var chuoi = item.GetProperty("createdAt").GetString();
        Assert.Equal(JsonValueKind.String, item.GetProperty("createdAt").ValueKind);
        Assert.StartsWith("2026-10-01T08:00:00", chuoi);
    }

    // ---------------------------------------------------------------------------------------
    // Helper — chép lại trong lớp test, không có lớp base chung (đúng lệ đang có của dự án)
    // ---------------------------------------------------------------------------------------

    private static async Task<HttpClient> ClientAsPassengerAsync(TestAppFactory factory)
        => ClientWith(factory, factory.CreateTokenFor(await SeedUserAsync(factory, "Hành Khách Test")));

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
    private static async Task<UserEntity> SeedUserAsync(TestAppFactory factory, string fullName)
    {
        var role = new Role { Id = RoleIds.Passenger, Code = RoleCodes.Passenger, Name = RoleCodes.Passenger };

        await factory.SeedAsync(db =>
        {
            if (!db.Roles.Any(r => r.Id == role.Id || r.Code == role.Code))
            {
                db.Roles.Add(role);
            }
        });

        var user = new UserEntity
        {
            Id = Guid.NewGuid(),
            PhoneNumber = "09" + Random.Shared.Next(10_000_000, 99_999_999),
            FullName = fullName,
            PasswordHash = "hash-khong-dung-toi-trong-test",
            IsActive = true,
            RoleId = role.Id,
            UserRoles = [new UserRole { RoleId = role.Id }],
        };

        await factory.SeedAsync(db => db.Users.Add(user));

        return user;
    }

    /// <summary>
    /// Một dòng thông báo. Không gán navigation <c>User</c>: InMemory không cưỡng chế khoá ngoại nên
    /// seed được dòng mồ côi (đúng lệ "seed dòng mồ côi" của MyTicketsApiTests và
    /// RefundLookupApiTests) — endpoint này chỉ đọc theo <c>UserId</c> nên không cần bảng Users.
    /// </summary>
    private static async Task<Notification> SeedNotificationAsync(
        TestAppFactory factory,
        Guid userId,
        string title,
        NotificationType type = NotificationType.CancellationApproved,
        string body = "Nội dung thông báo.",
        NotificationTarget? relatedEntityType = NotificationTarget.CancellationRequest,
        Guid? relatedEntityId = null,
        DateTime? createdAt = null)
    {
        var notification = new Notification
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Type = type,
            Title = title,
            Body = body,
            RelatedEntityType = relatedEntityType,
            RelatedEntityId = relatedEntityId,
            CreatedAt = createdAt ?? Ngay(1),
        };

        await factory.SeedAsync(db => db.Notifications.Add(notification));

        return notification;
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
        => await response.Content.ReadFromJsonAsync<JsonElement>();

    /// <summary>Mốc UTC trong quá khứ xa — `day` càng lớn càng gần hiện tại.</summary>
    private static DateTime Ngay(int day)
        => new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc).AddDays(day);
}
