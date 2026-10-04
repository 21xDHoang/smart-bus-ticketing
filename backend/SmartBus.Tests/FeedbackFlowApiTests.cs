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
/// Test tích hợp CHÉO BỀ MẶT cho luồng phản ánh (US 24, Sprint 2) — task "Test API gửi / phản hồi /
/// thống kê phản ánh (xUnit)" (dòng r59).
///
/// <para>
/// <b>Vì sao có file này.</b> Ba file test anh em — <see cref="FeedbackAdminApiTests"/>,
/// <see cref="FeedbackLookupApiTests"/>, <see cref="FeedbackStatisticsApiTests"/> — do chính ba tác
/// giả API viết. Mỗi file chỉ chạm MỘT bề mặt: file admin chỉ gọi <c>/api/admin/feedbacks*</c>, file
/// lookup chỉ gọi <c>/api/feedbacks/me*</c>, và cả ba đều SEED dữ liệu trực tiếp xuống CSDL thay vì
/// tạo nó qua API. Hệ quả: chưa có ca nào chạy hai bề mặt trong cùng một luồng, nên chưa có gì khoá
/// lại việc <i>thứ quản lý làm qua API admin có thật sự đến được mắt hành khách qua API lookup hay
/// không</i>. Đó đúng là khoảng trống file này lấp.
/// </para>
///
/// <para>
/// <b>Ba nhóm ca mới so với ba file kia:</b>
/// </para>
/// <list type="number">
///   <item>
///     <b>Phân quyền trên endpoint GHI.</b> Ba file kia chỉ kiểm quyền trên endpoint ĐỌC (danh sách,
///     chi tiết, thống kê). Chưa ca nào kiểm <c>POST /{id}/replies</c> và <c>PATCH /{id}</c> — đúng
///     hai endpoint làm thay đổi dữ liệu. Một policy bị gỡ nhầm ở đó thì hành khách tự phản hồi được
///     chính mình mà không test nào đỏ.
///   </item>
///   <item>
///     <b>Luồng chéo bề mặt.</b> Quản lý ghi qua API admin → hành khách đọc qua API lookup, cả hai
///     đều là request HTTP thật, không seed chen giữa.
///   </item>
///   <item>
///     <b>Thống kê bất biến với thao tác xử lý.</b> Thống kê đếm phản ánh theo loại/tuyến, KHÔNG lọc
///     theo trạng thái — nên phản hồi và đổi trạng thái không được làm con số nào nhúc nhích.
///   </item>
/// </list>
///
/// <para>
/// ⚠️ <b>Phần "gửi phản ánh" của task CHƯA kiểm được:</b> <c>POST /api/feedbacks</c> (task r51 của
/// Trần Trung Hiếu) chưa tồn tại — không có route, không có mục trong <c>docs/api-contract.md</c>.
/// Dựng ca cho endpoint đó lúc này là dựng test trên một hình dạng API chưa ai chốt (⛔5). Xem báo
/// cáo kèm theo nhánh này.
/// </para>
///
/// <para>
/// Dùng lại <see cref="TestAppFactory"/> của JwtAuthTests: chạy trên app thật + middleware thật, mỗi
/// test một CSDL InMemory riêng dựng từ entity nên không chờ migration.
/// </para>
/// </summary>
public class FeedbackFlowApiTests
{
    private const string AdminListUrl = "/api/admin/feedbacks";
    private const string MyListUrl = "/api/feedbacks/me";
    private const string StatisticsUrl = "/api/admin/feedbacks/statistics";

    private static string AdminDetailUrl(Guid id) => $"{AdminListUrl}/{id}";
    private static string ReplyUrl(Guid id) => $"{AdminListUrl}/{id}/replies";
    private static string MyDetailUrl(Guid id) => $"{MyListUrl}/{id}";

    /// <summary>Mốc thời gian cố định — không dùng UtcNow để thứ tự và so sánh tất định.</summary>
    private static readonly DateTime Moc1 = new(2026, 10, 1, 1, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime GioKhoiHanh = new(2026, 10, 1, 22, 0, 0, DateTimeKind.Utc);

    // =======================================================================================
    // A. Phân quyền trên endpoint GHI — khoảng trống của cả ba file anh em
    // =======================================================================================

    [Theory]
    [InlineData(RoleCodes.Passenger)]
    [InlineData(RoleCodes.Driver)]
    public async Task Hanh_khach_va_tai_xe_phan_hoi_thi_tra_403(string roleCode)
    {
        using var factory = new TestAppFactory();
        var (client, _) = await SignInAsync(factory, RoleIdsFor(roleCode), roleCode, "Người Dùng Test");
        var hanhKhach = await SeedUserAsync(factory, RoleIds.Passenger, RoleCodes.Passenger, "Nguyễn Văn A");
        var phanAnh = await SeedFeedbackAsync(factory, hanhKhach.Id);

        var response = await client.PostAsJsonAsync(
            ReplyUrl(phanAnh.Id),
            new { content = "Hành khách tự trả lời phản ánh của mình." });

        // Trả lời phản ánh là nghiệp vụ vận hành — chỉ Manager/Admin. Test quyền phải phủ cả endpoint
        // GHI: gỡ nhầm policy ở đây là hành khách tự đóng phản ánh của mình, mà ba file anh em chỉ
        // kiểm quyền trên endpoint đọc nên sẽ không có gì đỏ.
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData(RoleCodes.Passenger)]
    [InlineData(RoleCodes.Driver)]
    public async Task Hanh_khach_va_tai_xe_doi_trang_thai_thi_tra_403(string roleCode)
    {
        using var factory = new TestAppFactory();
        var (client, _) = await SignInAsync(factory, RoleIdsFor(roleCode), roleCode, "Người Dùng Test");
        var (quanLyClient, _) = await SignInAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var hanhKhach = await SeedUserAsync(factory, RoleIds.Passenger, RoleCodes.Passenger, "Nguyễn Văn A");
        var phanAnh = await SeedFeedbackAsync(factory, hanhKhach.Id, status: FeedbackStatus.New);

        var response = await client.PatchAsJsonAsync(
            AdminDetailUrl(phanAnh.Id),
            new { status = "Resolved" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        // Không chỉ khoá mã trả về — khoá luôn việc dữ liệu KHÔNG đổi, kiểm bằng mắt của quản lý.
        // Một middleware trả 403 mà service vẫn kịp chạy thì test chỉ so mã trả về sẽ xanh trong khi
        // trạng thái đã bị sửa sau lưng hành khách.
        var benAdmin = await ReadJsonAsync(await quanLyClient.GetAsync(AdminDetailUrl(phanAnh.Id)));
        Assert.Equal("New", benAdmin.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, benAdmin.GetProperty("updatedAt").ValueKind);
    }

    [Theory]
    [InlineData(RoleCodes.Manager)]
    [InlineData(RoleCodes.Admin)]
    public async Task Quan_ly_va_admin_phan_hoi_duoc(string roleCode)
    {
        using var factory = new TestAppFactory();
        var (client, quanLy) = await SignInAsync(factory, RoleIdsFor(roleCode), roleCode, "Quản Lý Test");
        var hanhKhach = await SeedUserAsync(factory, RoleIds.Passenger, RoleCodes.Passenger, "Nguyễn Văn A");
        var phanAnh = await SeedFeedbackAsync(factory, hanhKhach.Id);

        var response = await client.PostAsJsonAsync(ReplyUrl(phanAnh.Id), new { content = "Đã tiếp nhận." });
        var body = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(quanLy.Id, body.GetProperty("replies")[0].GetProperty("userId").GetGuid());
    }

    [Fact]
    public async Task Khong_gui_token_khi_phan_hoi_thi_tra_401()
    {
        using var factory = new TestAppFactory();
        var hanhKhach = await SeedUserAsync(factory, RoleIds.Passenger, RoleCodes.Passenger, "Nguyễn Văn A");
        var phanAnh = await SeedFeedbackAsync(factory, hanhKhach.Id);

        var response = await factory.CreateClient()
            .PostAsJsonAsync(ReplyUrl(phanAnh.Id), new { content = "Không có token." });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Khong_gui_token_khi_doi_trang_thai_thi_tra_401()
    {
        using var factory = new TestAppFactory();
        var hanhKhach = await SeedUserAsync(factory, RoleIds.Passenger, RoleCodes.Passenger, "Nguyễn Văn A");
        var phanAnh = await SeedFeedbackAsync(factory, hanhKhach.Id);

        var response = await factory.CreateClient()
            .PatchAsJsonAsync(AdminDetailUrl(phanAnh.Id), new { status = "Resolved" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // =======================================================================================
    // B. Phản hồi chéo bề mặt — quản lý ghi qua API admin, hành khách đọc qua API lookup
    // =======================================================================================

    [Fact]
    public async Task Phan_hoi_qua_api_admin_hien_ra_ngay_cho_hanh_khach()
    {
        using var factory = new TestAppFactory();
        var (adminClient, quanLy) = await SignInAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var (khachClient, hanhKhach) = await SignInAsync(factory, RoleIds.Passenger, RoleCodes.Passenger, "Nguyễn Văn A");
        var phanAnh = await SeedFeedbackAsync(factory, hanhKhach.Id, content: "Xe chạy trễ 30 phút.");

        // Ghi qua API admin — KHÔNG seed dòng phản hồi xuống CSDL.
        var gui = await adminClient.PostAsJsonAsync(
            ReplyUrl(phanAnh.Id), new { content = "Nhà xe xin lỗi vì sự cố." });
        Assert.Equal(HttpStatusCode.OK, gui.StatusCode);

        // Đọc qua API lookup của chính hành khách đã gửi phản ánh.
        var body = await ReadJsonAsync(await khachClient.GetAsync(MyDetailUrl(phanAnh.Id)));

        var replies = body.GetProperty("replies");
        Assert.Equal(1, replies.GetArrayLength());
        Assert.Equal("Nhà xe xin lỗi vì sự cố.", replies[0].GetProperty("content").GetString());
        // Tên người trả lời ghép từ bảng Users ở CẢ HAI service — hành khách phải đọc được tên hiển
        // thị của quản lý, không phải một GUID vô nghĩa.
        Assert.Equal(quanLy.FullName, replies[0].GetProperty("userFullName").GetString());
        Assert.Equal(quanLy.Id, replies[0].GetProperty("userId").GetGuid());
    }

    [Fact]
    public async Task Phan_hoi_cat_khoang_trang_hien_dung_cho_hanh_khach()
    {
        using var factory = new TestAppFactory();
        var (adminClient, _) = await SignInAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var (khachClient, hanhKhach) = await SignInAsync(factory, RoleIds.Passenger, RoleCodes.Passenger, "Nguyễn Văn A");
        var phanAnh = await SeedFeedbackAsync(factory, hanhKhach.Id);

        // Service cắt khoảng trắng hai đầu TRƯỚC khi lưu. Test ở tầng admin chỉ thấy chuỗi đã cắt
        // trong response admin; ca này xác nhận chuỗi ĐÃ LƯU cũng là chuỗi đã cắt — tức hành khách
        // đọc lại cũng thấy đúng như vậy, không có khoảng trắng nào sống sót trong CSDL.
        await adminClient.PostAsJsonAsync(ReplyUrl(phanAnh.Id), new { content = "   Đã xử lý xong.   " });

        var body = await ReadJsonAsync(await khachClient.GetAsync(MyDetailUrl(phanAnh.Id)));

        Assert.Equal("Đã xử lý xong.", body.GetProperty("replies")[0].GetProperty("content").GetString());
    }

    [Fact]
    public async Task ReplyCount_cua_hanh_khach_tang_theo_tung_phan_hoi_that()
    {
        using var factory = new TestAppFactory();
        var (adminClient, _) = await SignInAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var (khachClient, hanhKhach) = await SignInAsync(factory, RoleIds.Passenger, RoleCodes.Passenger, "Nguyễn Văn A");
        var phanAnh = await SeedFeedbackAsync(factory, hanhKhach.Id, createdAt: Moc1);

        // Danh sách của hành khách mang replyCount nhưng KHÔNG mang replies — con đếm đó phải khớp
        // với số phản hồi thật đã ghi qua API admin, nếu không thì màn hình "phản ánh của tôi" hiện
        // một con số không có gì đứng sau.
        Assert.Equal(0, ReplyCountInMyList(await MyListAsync(khachClient), phanAnh.Id));

        await adminClient.PostAsJsonAsync(ReplyUrl(phanAnh.Id), new { content = "Phản hồi 1." });
        Assert.Equal(1, ReplyCountInMyList(await MyListAsync(khachClient), phanAnh.Id));

        await adminClient.PostAsJsonAsync(ReplyUrl(phanAnh.Id), new { content = "Phản hồi 2." });
        Assert.Equal(2, ReplyCountInMyList(await MyListAsync(khachClient), phanAnh.Id));
    }

    [Fact]
    public async Task Nhieu_phan_hoi_xep_cu_den_moi_o_ca_hai_ben()
    {
        using var factory = new TestAppFactory();
        var (adminClient, quanLy) = await SignInAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var (khachClient, hanhKhach) = await SignInAsync(factory, RoleIds.Passenger, RoleCodes.Passenger, "Nguyễn Văn A");
        var phanAnh = await SeedFeedbackAsync(factory, hanhKhach.Id);

        // Ba mốc thời gian TƯỜNG MINH, không post qua API: hai lần post liên tiếp có thể rơi vào
        // cùng một tick đồng hồ (DateTime.UtcNow trên Windows chỉ mịn tới ~15 ms), khi đó service xếp
        // tiếp theo Id — mà Id là GUID ngẫu nhiên, nên thứ tự sẽ đảo và ca test chập chờn. Gieo mốc
        // cố định thì phép kiểm thứ tự là tất định, còn đường "post qua API rồi hành khách đọc được"
        // đã có ca Phan_hoi_qua_api_admin_hien_ra_ngay_cho_hanh_khach phủ.
        await SeedReplyAsync(factory, phanAnh.Id, quanLy.Id, "Phản hồi thứ nhất.", Moc1);
        await SeedReplyAsync(factory, phanAnh.Id, quanLy.Id, "Phản hồi thứ hai.", Moc1.AddHours(1));
        await SeedReplyAsync(factory, phanAnh.Id, quanLy.Id, "Phản hồi thứ ba.", Moc1.AddHours(2));

        var benAdmin = ReplyContents(await ReadJsonAsync(await adminClient.GetAsync(AdminDetailUrl(phanAnh.Id))));
        var benKhach = ReplyContents(await ReadJsonAsync(await khachClient.GetAsync(MyDetailUrl(phanAnh.Id))));

        // Hai service chép lại cùng một truy vấn sắp xếp (cũ → mới, trùng thời điểm thì theo Id). Ca
        // này khoá việc hai bản chép đó cho ra CÙNG một thứ tự — lệch nhau là màn hình quản lý và màn
        // hình hành khách kể hai câu chuyện khác nhau về cùng một cuộc trao đổi.
        var cuDenMoi = new[] { "Phản hồi thứ nhất.", "Phản hồi thứ hai.", "Phản hồi thứ ba." };
        Assert.Equal(cuDenMoi, benKhach);
        Assert.Equal(cuDenMoi, benAdmin);
    }

    [Fact]
    public async Task Phan_hoi_khong_doi_trang_thai_cung_khong_dong_dau_UpdatedAt_nhin_tu_phia_hanh_khach()
    {
        using var factory = new TestAppFactory();
        var (adminClient, _) = await SignInAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var (khachClient, hanhKhach) = await SignInAsync(factory, RoleIds.Passenger, RoleCodes.Passenger, "Nguyễn Văn A");
        var phanAnh = await SeedFeedbackAsync(factory, hanhKhach.Id, status: FeedbackStatus.InProgress, createdAt: Moc1);

        await adminClient.PostAsJsonAsync(ReplyUrl(phanAnh.Id), new { content = "Đang kiểm tra." });

        var body = await ReadJsonAsync(await khachClient.GetAsync(MyDetailUrl(phanAnh.Id)));

        // Luật hợp đồng: dòng Feedbacks là bản ghi của hành khách, phản hồi nằm ở bảng riêng. Trả lời
        // xong mà còn chờ khách phản hồi lại là ca có thật, nên tự chuyển trạng thái là nói sai giúp
        // người dùng. Kiểm từ phía hành khách — phía chịu ảnh hưởng nếu luật này bị vi phạm.
        Assert.Equal("InProgress", body.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("updatedAt").ValueKind);
    }

    // =======================================================================================
    // C. Đổi trạng thái chéo bề mặt
    // =======================================================================================

    [Fact]
    public async Task Doi_trang_thai_qua_api_admin_doi_duoc_bo_loc_cua_hanh_khach()
    {
        using var factory = new TestAppFactory();
        var (adminClient, _) = await SignInAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var (khachClient, hanhKhach) = await SignInAsync(factory, RoleIds.Passenger, RoleCodes.Passenger, "Nguyễn Văn A");
        var phanAnh = await SeedFeedbackAsync(factory, hanhKhach.Id, status: FeedbackStatus.New, createdAt: Moc1);

        await adminClient.PatchAsJsonAsync(AdminDetailUrl(phanAnh.Id), new { status = "InProgress" });

        // Bộ lọc trạng thái của hành khách phải bám theo thay đổi quản lý vừa làm — đây là điều màn
        // hình "theo dõi trạng thái xử lý" hứa với người dùng.
        var dangXuLy = await ReadJsonAsync(await khachClient.GetAsync($"{MyListUrl}?status=InProgress"));
        var conMoi = await ReadJsonAsync(await khachClient.GetAsync($"{MyListUrl}?status=New"));

        Assert.Equal([phanAnh.Id], IdsOfArray(dangXuLy));
        Assert.Empty(IdsOfArray(conMoi));
    }

    [Fact]
    public async Task Doi_trang_thai_hien_cho_hanh_khach_kem_UpdatedAt()
    {
        using var factory = new TestAppFactory();
        var (adminClient, _) = await SignInAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var (khachClient, hanhKhach) = await SignInAsync(factory, RoleIds.Passenger, RoleCodes.Passenger, "Nguyễn Văn A");
        var phanAnh = await SeedFeedbackAsync(factory, hanhKhach.Id, status: FeedbackStatus.New, createdAt: Moc1);

        var response = await adminClient.PatchAsJsonAsync(AdminDetailUrl(phanAnh.Id), new { status = "Resolved" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await ReadJsonAsync(await khachClient.GetAsync(MyDetailUrl(phanAnh.Id)));

        // Hành khách thấy cả trạng thái mới lẫn dấu thời gian đổi — dấu này CHỈ được đóng khi trạng
        // thái thật sự đổi (khác phản hồi, vốn không chạm UpdatedAt).
        Assert.Equal("Resolved", body.GetProperty("status").GetString());
        Assert.Equal(phanAnh.Id, body.GetProperty("id").GetGuid());
        Assert.NotEqual(JsonValueKind.Null, body.GetProperty("updatedAt").ValueKind);

        // Dòng Feedbacks vẫn là bản ghi của hành khách: đổi trạng thái không được đổi người gửi.
        // Trường userId chỉ có ở bề mặt admin nên kiểm ở đó.
        var benAdmin = await ReadJsonAsync(await adminClient.GetAsync(AdminDetailUrl(phanAnh.Id)));
        Assert.Equal(hanhKhach.Id, benAdmin.GetProperty("userId").GetGuid());
        Assert.Equal(hanhKhach.FullName, benAdmin.GetProperty("userFullName").GetString());
    }

    [Fact]
    public async Task Doi_lai_dung_trang_thai_cu_thi_hanh_khach_khong_thay_UpdatedAt_moi()
    {
        using var factory = new TestAppFactory();
        var (adminClient, _) = await SignInAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var (khachClient, hanhKhach) = await SignInAsync(factory, RoleIds.Passenger, RoleCodes.Passenger, "Nguyễn Văn A");
        var phanAnh = await SeedFeedbackAsync(factory, hanhKhach.Id, status: FeedbackStatus.New, createdAt: Moc1);

        await adminClient.PatchAsJsonAsync(AdminDetailUrl(phanAnh.Id), new { status = "InProgress" });
        var lanDau = (await ReadJsonAsync(await khachClient.GetAsync(MyDetailUrl(phanAnh.Id))))
            .GetProperty("updatedAt").GetDateTime();

        // Gọi lại đúng trạng thái cũ là no-op: vẫn 200 nhưng KHÔNG đóng dấu thời gian giả. Nếu ai đó
        // sau này bỏ phép so sánh `Status != status` trong service, dấu sẽ nhảy và ca này đỏ.
        var lanHai = await adminClient.PatchAsJsonAsync(AdminDetailUrl(phanAnh.Id), new { status = "InProgress" });
        var sauCung = (await ReadJsonAsync(await khachClient.GetAsync(MyDetailUrl(phanAnh.Id))))
            .GetProperty("updatedAt").GetDateTime();

        Assert.Equal(HttpStatusCode.OK, lanHai.StatusCode);
        Assert.Equal(lanDau, sauCung);
    }

    // =======================================================================================
    // D. Hai bề mặt phải nhất quán trên cùng một dòng
    // =======================================================================================

    [Fact]
    public async Task Hai_be_mat_doc_cung_mot_dong_ra_cung_du_lieu()
    {
        using var factory = new TestAppFactory();
        var (adminClient, _) = await SignInAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var (khachClient, hanhKhach) = await SignInAsync(factory, RoleIds.Passenger, RoleCodes.Passenger, "Nguyễn Văn A");

        var tuyen = await SeedRouteAsync(factory);
        var xe = await SeedBusAsync(factory);
        var chuyen = await SeedTripAsync(factory, tuyen.Id, xe.Id, GioKhoiHanh);

        var phanAnh = await SeedFeedbackAsync(
            factory, hanhKhach.Id,
            type: FeedbackType.Compliment,
            content: "Tài xế chạy cẩn thận, xe sạch sẽ.",
            tripId: chuyen.Id,
            rating: 5,
            attachmentUrl: "https://example.com/anh.jpg",
            createdAt: Moc1);

        await adminClient.PostAsJsonAsync(ReplyUrl(phanAnh.Id), new { content = "Cảm ơn bạn đã góp ý." });

        var benAdmin = await ReadJsonAsync(await adminClient.GetAsync(AdminDetailUrl(phanAnh.Id)));
        var benKhach = await ReadJsonAsync(await khachClient.GetAsync(MyDetailUrl(phanAnh.Id)));

        // Sáu trường lõi phải giống nhau từng chữ. Hai service dựng hai DTO khác nhau (bên admin có
        // userId/userFullName, bên khách có routeCode/routeName/departureTime) nhưng cùng đọc một
        // dòng — lệch một trường ở đây nghĩa là một trong hai projection đọc nhầm cột.
        foreach (var truong in new[] { "id", "tripId", "type", "content", "attachmentUrl", "rating", "status", "createdAt" })
        {
            Assert.Equal(benAdmin.GetProperty(truong).GetRawText(), benKhach.GetProperty(truong).GetRawText());
        }

        Assert.Equal(phanAnh.Id, benKhach.GetProperty("id").GetGuid());
        Assert.Equal(5, benKhach.GetProperty("rating").GetInt32());
    }

    [Fact]
    public async Task Phan_hoi_admin_viet_hien_o_ca_hai_chi_tiet_voi_cung_id()
    {
        using var factory = new TestAppFactory();
        var (adminClient, _) = await SignInAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var (khachClient, hanhKhach) = await SignInAsync(factory, RoleIds.Passenger, RoleCodes.Passenger, "Nguyễn Văn A");
        var phanAnh = await SeedFeedbackAsync(factory, hanhKhach.Id);

        await adminClient.PostAsJsonAsync(ReplyUrl(phanAnh.Id), new { content = "Đã nhắc nhở tài xế." });

        var benAdmin = await ReadJsonAsync(await adminClient.GetAsync(AdminDetailUrl(phanAnh.Id)));
        var benKhach = await ReadJsonAsync(await khachClient.GetAsync(MyDetailUrl(phanAnh.Id)));

        // Cùng một dòng FeedbackReply, hai đường đọc khác nhau — phải ra cùng id, cùng nội dung,
        // cùng tác giả. Đây là ca bắt được lỗi "một bên quên ghép bảng Users" hoặc "một bên đọc
        // nhầm phản hồi của phản ánh khác".
        Assert.Equal(
            benAdmin.GetProperty("replies")[0].GetProperty("id").GetGuid(),
            benKhach.GetProperty("replies")[0].GetProperty("id").GetGuid());
        Assert.Equal(
            benAdmin.GetProperty("replies")[0].GetProperty("userFullName").GetString(),
            benKhach.GetProperty("replies")[0].GetProperty("userFullName").GetString());
    }

    [Fact]
    public async Task Hanh_khach_khac_khong_thay_phan_anh_cung_phan_hoi_cua_nguoi_khac()
    {
        using var factory = new TestAppFactory();
        var (adminClient, _) = await SignInAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var (_, khachA) = await SignInAsync(factory, RoleIds.Passenger, RoleCodes.Passenger, "Nguyễn Văn A");
        var (clientB, _) = await SignInAsync(factory, RoleIds.Passenger, RoleCodes.Passenger, "Trần Thị B");

        var phanAnhCuaA = await SeedFeedbackAsync(factory, khachA.Id, content: "Phản ánh của A.", createdAt: Moc1);
        await adminClient.PostAsJsonAsync(ReplyUrl(phanAnhCuaA.Id), new { content = "Trả lời riêng cho A." });

        // Chi tiết: 404 với đúng câu của ca "không tồn tại" — không phải 403, vì phân biệt hai ca là
        // xác nhận id đó có thật trên hệ thống.
        var chiTiet = await clientB.GetAsync(MyDetailUrl(phanAnhCuaA.Id));
        Assert.Equal(HttpStatusCode.NotFound, chiTiet.StatusCode);
        Assert.Equal("Không tìm thấy phản ánh", (await ReadJsonAsync(chiTiet)).GetProperty("message").GetString());

        // Danh sách của B không được lộ phản ánh của A — kể cả con đếm phản hồi cũng không.
        var danhSachB = await clientB.GetAsync(MyListUrl);
        Assert.Equal(0, (await ReadJsonAsync(danhSachB)).GetArrayLength());
    }

    // =======================================================================================
    // E. Thống kê bất biến với thao tác xử lý
    // =======================================================================================

    [Fact]
    public async Task Phan_hoi_khong_lam_thay_doi_thong_ke()
    {
        using var factory = new TestAppFactory();
        var (adminClient, _) = await SignInAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var hanhKhach = await SeedUserAsync(factory, RoleIds.Passenger, RoleCodes.Passenger, "Nguyễn Văn A");

        var tuyen = await SeedRouteAsync(factory);
        var xe = await SeedBusAsync(factory);
        var chuyen = await SeedTripAsync(factory, tuyen.Id, xe.Id, GioKhoiHanh);

        var coChuyen = await SeedFeedbackAsync(factory, hanhKhach.Id, tripId: chuyen.Id, createdAt: Moc1);
        await SeedFeedbackAsync(factory, hanhKhach.Id, type: FeedbackType.Suggestion, createdAt: Moc1.AddHours(1));

        var truoc = await ReadJsonAsync(await adminClient.GetAsync(StatisticsUrl));

        await adminClient.PostAsJsonAsync(ReplyUrl(coChuyen.Id), new { content = "Đã xử lý." });

        var sau = await ReadJsonAsync(await adminClient.GetAsync(StatisticsUrl));

        // Thống kê đếm PHẢN ÁNH theo loại và theo tuyến — phản hồi là bảng riêng, không phải một
        // phản ánh mới. Nếu ai đó sau này cho phản hồi "đóng góp" vào con đếm, hai lần gọi này lệch
        // nhau và ca này đỏ.
        Assert.Equal(truoc.GetProperty("total").GetInt32(), sau.GetProperty("total").GetInt32());
        Assert.Equal(truoc.GetProperty("withoutTrip").GetInt32(), sau.GetProperty("withoutTrip").GetInt32());
        Assert.Equal(TypeCounts(sau), TypeCounts(truoc));
        Assert.Equal(2, sau.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Doi_trang_thai_khong_lam_thay_doi_thong_ke()
    {
        using var factory = new TestAppFactory();
        var (adminClient, _) = await SignInAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var hanhKhach = await SeedUserAsync(factory, RoleIds.Passenger, RoleCodes.Passenger, "Nguyễn Văn A");

        var tuyen = await SeedRouteAsync(factory);
        var xe = await SeedBusAsync(factory);
        var chuyen = await SeedTripAsync(factory, tuyen.Id, xe.Id, GioKhoiHanh);

        var coChuyen = await SeedFeedbackAsync(factory, hanhKhach.Id, tripId: chuyen.Id, createdAt: Moc1);
        await SeedFeedbackAsync(factory, hanhKhach.Id, type: FeedbackType.Suggestion, createdAt: Moc1.AddHours(1));

        var truoc = await ReadJsonAsync(await adminClient.GetAsync(StatisticsUrl));

        // Đi hết vòng đời New → InProgress → Resolved rồi mở lại Resolved → InProgress: không bước
        // nào được làm con số nhúc nhích, vì thống kê KHÔNG lọc theo trạng thái (task chỉ hỏi "theo
        // loại và theo tuyến").
        foreach (var trangThai in new[] { "InProgress", "Resolved", "InProgress" })
        {
            var doi = await adminClient.PatchAsJsonAsync(AdminDetailUrl(coChuyen.Id), new { status = trangThai });
            Assert.Equal(HttpStatusCode.OK, doi.StatusCode);
        }

        var sau = await ReadJsonAsync(await adminClient.GetAsync(StatisticsUrl));

        Assert.Equal(truoc.GetProperty("total").GetInt32(), sau.GetProperty("total").GetInt32());
        Assert.Equal(truoc.GetProperty("withoutTrip").GetInt32(), sau.GetProperty("withoutTrip").GetInt32());
        Assert.Equal(TypeCounts(sau), TypeCounts(truoc));
        Assert.Equal(
            truoc.GetProperty("byRoute").GetRawText(),
            sau.GetProperty("byRoute").GetRawText());
    }

    [Fact]
    public async Task Thong_ke_khop_voi_du_lieu_hanh_khach_doc_duoc()
    {
        using var factory = new TestAppFactory();
        var (adminClient, _) = await SignInAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var (khachClient, hanhKhach) = await SignInAsync(factory, RoleIds.Passenger, RoleCodes.Passenger, "Nguyễn Văn A");

        await SeedFeedbackAsync(factory, hanhKhach.Id, createdAt: Moc1);
        await SeedFeedbackAsync(factory, hanhKhach.Id, type: FeedbackType.Compliment, createdAt: Moc1.AddHours(1));
        await SeedFeedbackAsync(factory, hanhKhach.Id, type: FeedbackType.Suggestion, createdAt: Moc1.AddHours(2));

        var thongKe = await ReadJsonAsync(await adminClient.GetAsync(StatisticsUrl));
        var danhSachKhach = await ReadJsonAsync(await khachClient.GetAsync(MyListUrl));

        // Con số tổng của bảng thống kê phải bằng đúng số dòng hành khách tự đếm được ở danh sách
        // của mình — hai đường đọc khác nhau, cùng một sự thật. Trong bộ dữ liệu này hành khách là
        // người duy nhất gửi phản ánh nên hai con số phải trùng.
        Assert.Equal(3, thongKe.GetProperty("total").GetInt32());
        Assert.Equal(3, danhSachKhach.GetArrayLength());
    }

    // =======================================================================================
    // F. Thông tin chuyến/tuyến còn nguyên sau khi quản lý thao tác
    // =======================================================================================

    [Fact]
    public async Task Sau_khi_admin_phan_hoi_hanh_khach_van_thay_du_thong_tin_chuyen()
    {
        using var factory = new TestAppFactory();
        var (adminClient, _) = await SignInAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var (khachClient, hanhKhach) = await SignInAsync(factory, RoleIds.Passenger, RoleCodes.Passenger, "Nguyễn Văn A");

        var tuyen = await SeedRouteAsync(factory, code: "01", name: "Bến Thành — Chợ Lớn");
        var xe = await SeedBusAsync(factory);
        var chuyen = await SeedTripAsync(factory, tuyen.Id, xe.Id, GioKhoiHanh);

        var phanAnh = await SeedFeedbackAsync(factory, hanhKhach.Id, tripId: chuyen.Id, createdAt: Moc1);

        await adminClient.PostAsJsonAsync(ReplyUrl(phanAnh.Id), new { content = "Đã tiếp nhận." });

        var body = await ReadJsonAsync(await khachClient.GetAsync(MyDetailUrl(phanAnh.Id)));

        // Ba trường chuyến được ghép từ Trips → Routes vì hành khách không tự tra được chuyến
        // (GET /api/trips/{id} nằm sau policy ManagerOrAbove). Chúng phải còn nguyên sau khi luồng
        // phản hồi chạy qua — nếu projection bị đụng, hành khách chỉ còn một GUID vô nghĩa.
        Assert.Equal("01", body.GetProperty("routeCode").GetString());
        Assert.Equal("Bến Thành — Chợ Lớn", body.GetProperty("routeName").GetString());
        Assert.Equal(GioKhoiHanh, body.GetProperty("departureTime").GetDateTime());
    }

    // =======================================================================================
    // Helper — mỗi lớp test tự chép, không có lớp base chung (đúng lệ đang có của dự án)
    // =======================================================================================

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
        => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task<JsonElement> MyListAsync(HttpClient client)
        => await ReadJsonAsync(await client.GetAsync(MyListUrl));

    private static Guid[] IdsOfArray(JsonElement body)
        => body.EnumerateArray().Select(item => item.GetProperty("id").GetGuid()).ToArray();

    private static string[] ReplyContents(JsonElement body)
        => body.GetProperty("replies")
            .EnumerateArray()
            .Select(reply => reply.GetProperty("content").GetString() ?? string.Empty)
            .ToArray();

    private static int ReplyCountInMyList(JsonElement list, Guid feedbackId)
        => list.EnumerateArray()
            .First(item => item.GetProperty("id").GetGuid() == feedbackId)
            .GetProperty("replyCount")
            .GetInt32();

    private static string[] TypeCounts(JsonElement statistics)
        => statistics.GetProperty("byType")
            .EnumerateArray()
            .Select(row => $"{row.GetProperty("type").GetString()}={row.GetProperty("count").GetInt32()}")
            .ToArray();

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

    private static async Task<Feedback> SeedFeedbackAsync(
        TestAppFactory factory,
        Guid userId,
        FeedbackType type = FeedbackType.Complaint,
        FeedbackStatus status = FeedbackStatus.New,
        string content = "Xe chạy trễ 30 phút so với giờ trên vé.",
        Guid? tripId = null,
        int? rating = null,
        string? attachmentUrl = null,
        DateTime? createdAt = null)
    {
        var feedback = new Feedback
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TripId = tripId,
            Type = type,
            Content = content,
            AttachmentUrl = attachmentUrl,
            Rating = rating,
            Status = status,
            CreatedAt = createdAt ?? DateTime.UtcNow,
        };

        await factory.SeedAsync(db => db.Feedbacks.Add(feedback));

        return feedback;
    }

    /// <summary>
    /// Phản hồi gieo thẳng xuống CSDL, chỉ dùng cho ca cần mốc thời gian tường minh để kiểm thứ tự —
    /// các ca luồng chéo bề mặt đi qua <c>POST /{id}/replies</c> thật, không dùng helper này.
    /// </summary>
    private static async Task<FeedbackReply> SeedReplyAsync(
        TestAppFactory factory,
        Guid feedbackId,
        Guid userId,
        string content,
        DateTime createdAt)
    {
        var reply = new FeedbackReply
        {
            Id = Guid.NewGuid(),
            FeedbackId = feedbackId,
            UserId = userId,
            Content = content,
            CreatedAt = createdAt,
        };

        await factory.SeedAsync(db => db.FeedbackReplies.Add(reply));

        return reply;
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

    private static async Task<Bus> SeedBusAsync(TestAppFactory factory, string licensePlate = "29B-123.45")
    {
        var bus = new Bus
        {
            Id = Guid.NewGuid(),
            LicensePlate = licensePlate,
            BusType = "Hyundai County 29 chỗ",
            Capacity = 29,
            Status = BusStatus.Active,
        };

        await factory.SeedAsync(db => db.Buses.Add(bus));

        return bus;
    }

    private static async Task<Trip> SeedTripAsync(
        TestAppFactory factory,
        Guid routeId,
        Guid busId,
        DateTime departureTime)
    {
        // Chỉ gán khoá ngoại, KHÔNG gán navigation: Route/Bus được seed ở scope khác, gán navigation
        // vào đây sẽ khiến EF tưởng chúng là bản ghi mới và chèn trùng khoá chính.
        var trip = new Trip
        {
            Id = Guid.NewGuid(),
            RouteId = routeId,
            BusId = busId,
            DepartureTime = departureTime,
            Status = TripStatus.Scheduled,
        };

        await factory.SeedAsync(db => db.Trips.Add(trip));

        return trip;
    }
}
