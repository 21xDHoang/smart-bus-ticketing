using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using SmartBus.Api.Entities;
using SmartBus.Api.Services;
using UserEntity = SmartBus.Api.Entities.User;

namespace SmartBus.Tests;

/// <summary>
/// Test tích hợp cho nhóm API Admin xử lý phản ánh (US 24, Sprint 2) — /api/admin/feedbacks…
/// (task "API Admin phản hồi và đổi trạng thái phản ánh" — Phùng Duy Hoàng).
///
/// Dùng lại <see cref="TestAppFactory"/> của JwtAuthTests: chạy trên app thật, mỗi test một CSDL
/// InMemory riêng dựng từ entity — KHÔNG cần migration của Dăm mới chạy được.
///
/// ⚠️ Bốn luật nền của hợp đồng mà bộ test này khoá lại:
///   • Phản hồi KHÔNG tự đổi trạng thái và KHÔNG chạm UpdatedAt của phản ánh — dòng Feedbacks là
///     bản ghi của hành khách, phản hồi chỉ ghi thêm dòng mới vào bảng riêng.
///   • status/type lọc sai mã trả danh sách RỖNG chứ không phải 400 (mã lạ có thể là giá trị hợp
///     lệ trong tương lai — cùng lối GET /routes?status=).
///   • Không có máy trạng thái: mở lại Resolved → InProgress là hợp lệ.
///   • Dòng danh sách có replyCount và KHÔNG có replies; chi tiết thì ngược lại — hai hình dạng
///     khác nhau, đổi một bên là đổi hình dạng API (⛔5).
/// </summary>
public class FeedbackAdminApiTests
{
    private const string ListUrl = "/api/admin/feedbacks";

    /// <summary>Mốc thời gian cố định cho các ca sắp xếp — không dùng UtcNow để thứ tự tất định.</summary>
    private static readonly DateTime Moc1 = new(2026, 10, 1, 1, 0, 0, DateTimeKind.Utc);

    // ---------------------------------------------------------------------------------------
    // Phân quyền
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Khong_gui_token_thi_tra_401()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync(ListUrl);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(RoleCodes.Passenger)]
    [InlineData(RoleCodes.Driver)]
    public async Task Hanh_khach_va_tai_xe_goi_thi_tra_403(string roleCode)
    {
        using var factory = new TestAppFactory();
        var (client, _) = await SignInAsync(factory, RoleIdsFor(roleCode), roleCode, "Người Dùng Test");

        var response = await client.GetAsync(ListUrl);
        var body = await ReadJsonAsync(response);

        // Xử lý phản ánh là nghiệp vụ vận hành — chỉ Manager/Admin. Body 403 là câu cố định của
        // RbacMiddleware (trùng câu frontend dùng mặc định), khoá luôn hình dạng JSON của 403.
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

        var response = await client.GetAsync(ListUrl);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ---------------------------------------------------------------------------------------
    // Danh sách — lọc, phân trang, hình dạng
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Chua_co_phan_anh_nao_thi_tra_200_voi_items_rong_va_total_0()
    {
        using var factory = new TestAppFactory();
        var (client, _) = await SignInAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");

        var response = await client.GetAsync(ListUrl);
        var body = await ReadJsonAsync(response);

        // "Chưa có phản ánh nào" là câu trả lời hợp lệ — không phải 404: người gọi có thật, bộ lọc
        // không khớp gì. Vỏ danh sách giữ đúng khuôn { items, total, page, pageSize } của dự án.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, body.GetProperty("items").GetArrayLength());
        Assert.Equal(0, body.GetProperty("total").GetInt32());
        Assert.Equal(["items", "page", "pageSize", "total"], PropertyNamesOf(body));
    }

    [Fact]
    public async Task Danh_sach_tra_du_truong_va_chi_co_replyCount_khong_co_replies()
    {
        using var factory = new TestAppFactory();
        var (client, quanLy) = await SignInAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var hanhKhach = await SeedUserAsync(factory, RoleIds.Passenger, RoleCodes.Passenger, "Nguyễn Văn A");

        var phanAnh = await SeedFeedbackAsync(
            factory, hanhKhach.Id,
            content: "Xe chạy trễ 30 phút so với giờ trên vé.",
            createdAt: Moc1);
        await SeedReplyAsync(factory, phanAnh.Id, quanLy.Id, "Nhà xe xin lỗi vì sự cố.", Moc1.AddHours(1));
        await SeedReplyAsync(factory, phanAnh.Id, quanLy.Id, "Đã nhắc nhở tài xế.", Moc1.AddHours(2));

        var body = await ReadJsonAsync(await client.GetAsync(ListUrl));

        var item = Assert.Single(body.GetProperty("items").EnumerateArray().ToArray());
        Assert.Equal(phanAnh.Id, item.GetProperty("id").GetGuid());
        Assert.Equal(hanhKhach.Id, item.GetProperty("userId").GetGuid());
        Assert.Equal("Nguyễn Văn A", item.GetProperty("userFullName").GetString());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("tripId").ValueKind);
        Assert.Equal("Complaint", item.GetProperty("type").GetString());
        Assert.Equal("New", item.GetProperty("status").GetString());
        Assert.Equal(2, item.GetProperty("replyCount").GetInt32());

        // Hình dạng khoá lại đúng hợp đồng: dòng danh sách có replyCount và KHÔNG có replies —
        // kéo cả luồng vào mọi dòng thì một trang 10 dòng có thể kèm hàng trăm phản hồi mà bảng
        // không hiển thị (lý do ghi trong hợp đồng).
        Assert.Equal(
            [
                "attachmentUrl", "content", "createdAt", "id", "rating", "replyCount",
                "status", "tripId", "type", "updatedAt", "userFullName", "userId",
            ],
            PropertyNamesOf(item));
    }

    [Fact]
    public async Task Danh_sach_sap_moi_nhat_truoc()
    {
        using var factory = new TestAppFactory();
        var (client, _) = await SignInAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var hanhKhach = await SeedUserAsync(factory, RoleIds.Passenger, RoleCodes.Passenger);

        // Seed cố ý lộn xộn: cũ nhất trước.
        await SeedFeedbackAsync(factory, hanhKhach.Id, content: "Cũ nhất", createdAt: Moc1);
        await SeedFeedbackAsync(factory, hanhKhach.Id, content: "Mới nhất", createdAt: Moc1.AddHours(2));
        await SeedFeedbackAsync(factory, hanhKhach.Id, content: "Ở giữa", createdAt: Moc1.AddHours(1));

        var body = await ReadJsonAsync(await client.GetAsync(ListUrl));

        Assert.Equal(["Mới nhất", "Ở giữa", "Cũ nhất"], ContentsOf(body));
    }

    [Fact]
    public async Task Loc_theo_trang_thai_va_theo_loai()
    {
        using var factory = new TestAppFactory();
        var (client, _) = await SignInAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var hanhKhach = await SeedUserAsync(factory, RoleIds.Passenger, RoleCodes.Passenger);

        await SeedFeedbackAsync(factory, hanhKhach.Id, content: "Đang xử lý", status: FeedbackStatus.InProgress);
        await SeedFeedbackAsync(factory, hanhKhach.Id, content: "Mới", status: FeedbackStatus.New);
        await SeedFeedbackAsync(
            factory, hanhKhach.Id, content: "Khen ngợi",
            type: FeedbackType.Compliment, status: FeedbackStatus.Resolved);

        var theoTrangThai = await ReadJsonAsync(await client.GetAsync($"{ListUrl}?status=InProgress"));
        Assert.Equal(["Đang xử lý"], ContentsOf(theoTrangThai));
        Assert.Equal(1, theoTrangThai.GetProperty("total").GetInt32());

        var theoLoai = await ReadJsonAsync(await client.GetAsync($"{ListUrl}?type=Compliment"));
        Assert.Equal(["Khen ngợi"], ContentsOf(theoLoai));
    }

    [Fact]
    public async Task Loc_theo_ma_la_tra_danh_sach_rong_chu_khong_phai_loi()
    {
        using var factory = new TestAppFactory();
        var (client, _) = await SignInAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var hanhKhach = await SeedUserAsync(factory, RoleIds.Passenger, RoleCodes.Passenger);
        await SeedFeedbackAsync(factory, hanhKhach.Id);

        // Mã lạ có thể là trạng thái hợp lệ trong tương lai — 400 ở đây làm màn hình cũ vỡ khi
        // backend thêm giá trị mới. Cùng lối GET /routes?status=.
        var responseTrangThai = await client.GetAsync($"{ListUrl}?status=KhongCo");
        var theoTrangThai = await ReadJsonAsync(responseTrangThai);
        Assert.Equal(HttpStatusCode.OK, responseTrangThai.StatusCode);
        Assert.Equal(0, theoTrangThai.GetProperty("items").GetArrayLength());

        var responseLoai = await client.GetAsync($"{ListUrl}?type=KhongCo");
        var theoLoai = await ReadJsonAsync(responseLoai);
        Assert.Equal(HttpStatusCode.OK, responseLoai.StatusCode);
        Assert.Equal(0, theoLoai.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task Phan_trang_tra_dung_trang_va_total_la_tong_khong_phai_so_dong_trang()
    {
        using var factory = new TestAppFactory();
        var (client, _) = await SignInAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var hanhKhach = await SeedUserAsync(factory, RoleIds.Passenger, RoleCodes.Passenger);

        await SeedFeedbackAsync(factory, hanhKhach.Id, content: "Ba", createdAt: Moc1.AddHours(2));
        await SeedFeedbackAsync(factory, hanhKhach.Id, content: "Hai", createdAt: Moc1.AddHours(1));
        await SeedFeedbackAsync(factory, hanhKhach.Id, content: "Một", createdAt: Moc1);

        var body = await ReadJsonAsync(await client.GetAsync($"{ListUrl}?page=2&pageSize=2"));

        Assert.Equal(["Một"], ContentsOf(body));
        Assert.Equal(3, body.GetProperty("total").GetInt32());
        Assert.Equal(2, body.GetProperty("page").GetInt32());
        Assert.Equal(2, body.GetProperty("pageSize").GetInt32());
    }

    [Theory]
    [InlineData("?page=0", "page")]
    [InlineData("?pageSize=101", "pageSize")]
    [InlineData("?pageSize=0", "pageSize")]
    public async Task Phan_trang_ngoai_khoang_thi_tra_400(string query, string field)
    {
        using var factory = new TestAppFactory();
        var (client, _) = await SignInAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");

        var response = await client.GetAsync(ListUrl + query);
        var body = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotEmpty(ErrorsOf(body, field));
    }

    // ---------------------------------------------------------------------------------------
    // Chi tiết
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Chi_tiet_tra_du_truong_va_luong_phan_hoi_cu_den_moi()
    {
        using var factory = new TestAppFactory();
        var (client, quanLy) = await SignInAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var hanhKhach = await SeedUserAsync(factory, RoleIds.Passenger, RoleCodes.Passenger, "Nguyễn Văn A");

        var phanAnh = await SeedFeedbackAsync(
            factory, hanhKhach.Id,
            content: "Xe chạy trễ 30 phút.",
            rating: 2,
            attachmentUrl: "https://example.com/anh.jpg",
            createdAt: Moc1);

        // Seed cố ý ngược thứ tự thời gian: câu trả lời phải đọc cũ → mới.
        await SeedReplyAsync(factory, phanAnh.Id, quanLy.Id, "Đã nhắc nhở tài xế.", Moc1.AddHours(2));
        await SeedReplyAsync(factory, phanAnh.Id, quanLy.Id, "Nhà xe xin lỗi vì sự cố.", Moc1.AddHours(1));

        var response = await client.GetAsync($"/api/admin/feedbacks/{phanAnh.Id}");
        var body = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, body.GetProperty("rating").GetInt32());
        Assert.Equal("https://example.com/anh.jpg", body.GetProperty("attachmentUrl").GetString());

        var replies = body.GetProperty("replies").EnumerateArray().ToArray();
        Assert.Equal(
            ["Nhà xe xin lỗi vì sự cố.", "Đã nhắc nhở tài xế."],
            replies.Select(r => r.GetProperty("content").GetString() ?? string.Empty).ToArray());
        Assert.Equal(quanLy.Id, replies[0].GetProperty("userId").GetGuid());
        Assert.Equal("Quản Lý Test", replies[0].GetProperty("userFullName").GetString());
        Assert.Equal(
            ["content", "createdAt", "id", "userFullName", "userId"],
            PropertyNamesOf(replies[0]));

        // Chi tiết có replies và KHÔNG có replyCount — ngược lại dòng danh sách (hai hình dạng
        // khác nhau, cùng lý do đã ghi ở test danh sách).
        Assert.Equal(
            [
                "attachmentUrl", "content", "createdAt", "id", "rating", "replies",
                "status", "tripId", "type", "updatedAt", "userFullName", "userId",
            ],
            PropertyNamesOf(body));
    }

    [Fact]
    public async Task Phan_anh_khong_gan_chuyen_tra_tripId_null()
    {
        using var factory = new TestAppFactory();
        var (client, _) = await SignInAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var hanhKhach = await SeedUserAsync(factory, RoleIds.Passenger, RoleCodes.Passenger);

        // A9 #20: phản ánh về tuyến/dịch vụ không có chuyến để trỏ vào — tripId null là hợp lệ,
        // KHÔNG phải dữ liệu thiếu.
        var phanAnh = await SeedFeedbackAsync(factory, hanhKhach.Id, tripId: null);

        var body = await ReadJsonAsync(await client.GetAsync($"/api/admin/feedbacks/{phanAnh.Id}"));

        Assert.Equal(JsonValueKind.Null, body.GetProperty("tripId").ValueKind);
    }

    [Fact]
    public async Task Chi_tiet_khong_ton_tai_thi_tra_404()
    {
        using var factory = new TestAppFactory();
        var (client, _) = await SignInAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");

        var response = await client.GetAsync($"/api/admin/feedbacks/{Guid.NewGuid()}");
        var body = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Không tìm thấy phản ánh", body.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Chi_tiet_voi_id_sai_dinh_dang_thi_tra_404()
    {
        using var factory = new TestAppFactory();
        var (client, _) = await SignInAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");

        // {id:guid} không khớp "khong-phai-guid" → không có route nào nhận → 404, không phải 500.
        var response = await client.GetAsync("/api/admin/feedbacks/khong-phai-guid");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---------------------------------------------------------------------------------------
    // Phản hồi — ghi thêm, không tự đổi trạng thái
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Phan_hoi_ghi_them_dong_moi_va_khong_cham_trang_thai_hay_UpdatedAt()
    {
        using var factory = new TestAppFactory();
        var (client, quanLy) = await SignInAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var hanhKhach = await SeedUserAsync(factory, RoleIds.Passenger, RoleCodes.Passenger, "Nguyễn Văn A");
        var phanAnh = await SeedFeedbackAsync(factory, hanhKhach.Id, createdAt: Moc1);

        var response = await client.PostAsJsonAsync(
            $"/api/admin/feedbacks/{phanAnh.Id}/replies",
            new { content = "Nhà xe xin lỗi vì sự cố, đã nhắc nhở tài xế." });
        var body = await ReadJsonAsync(response);

        // Trả 200 kèm phản ánh ĐẦY ĐỦ (không phải 201): chưa có endpoint tra một phản hồi theo id
        // để trỏ Location — cùng lối POST /monthly-passes/{id}/renew.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var reply = Assert.Single(body.GetProperty("replies").EnumerateArray().ToArray());
        Assert.Equal("Nhà xe xin lỗi vì sự cố, đã nhắc nhở tài xế.", reply.GetProperty("content").GetString());
        Assert.Equal(quanLy.Id, reply.GetProperty("userId").GetGuid());
        Assert.Equal("Quản Lý Test", reply.GetProperty("userFullName").GetString());

        // Trả lời xong mà còn chờ khách phản hồi lại là ca có thật — tự chuyển trạng thái là nói
        // sai giúp người dùng; UpdatedAt cũng không đổi vì dòng Feedbacks là bản ghi của hành khách.
        Assert.Equal("New", body.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("updatedAt").ValueKind);
    }

    [Fact]
    public async Task Phan_hoi_cat_khoang_trang_hai_dau()
    {
        using var factory = new TestAppFactory();
        var (client, _) = await SignInAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var hanhKhach = await SeedUserAsync(factory, RoleIds.Passenger, RoleCodes.Passenger);
        var phanAnh = await SeedFeedbackAsync(factory, hanhKhach.Id);

        var body = await ReadJsonAsync(await client.PostAsJsonAsync(
            $"/api/admin/feedbacks/{phanAnh.Id}/replies",
            new { content = "  Nhà xe xin lỗi.  " }));

        var reply = Assert.Single(body.GetProperty("replies").EnumerateArray().ToArray());
        Assert.Equal("Nhà xe xin lỗi.", reply.GetProperty("content").GetString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Phan_hoi_noi_dung_trong_thi_tra_400(string content)
    {
        using var factory = new TestAppFactory();
        var (client, _) = await SignInAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var hanhKhach = await SeedUserAsync(factory, RoleIds.Passenger, RoleCodes.Passenger);
        var phanAnh = await SeedFeedbackAsync(factory, hanhKhach.Id);

        var response = await client.PostAsJsonAsync(
            $"/api/admin/feedbacks/{phanAnh.Id}/replies",
            new { content });
        var body = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotEmpty(ErrorsOf(body, "content"));
    }

    [Fact]
    public async Task Phan_hoi_qua_2000_ky_tu_thi_tra_400()
    {
        using var factory = new TestAppFactory();
        var (client, _) = await SignInAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var hanhKhach = await SeedUserAsync(factory, RoleIds.Passenger, RoleCodes.Passenger);
        var phanAnh = await SeedFeedbackAsync(factory, hanhKhach.Id);

        var response = await client.PostAsJsonAsync(
            $"/api/admin/feedbacks/{phanAnh.Id}/replies",
            new { content = new string('a', 2001) });
        var body = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotEmpty(ErrorsOf(body, "content"));
    }

    [Fact]
    public async Task Phan_hoi_phan_anh_khong_ton_tai_thi_tra_404()
    {
        using var factory = new TestAppFactory();
        var (client, _) = await SignInAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");

        var response = await client.PostAsJsonAsync(
            $"/api/admin/feedbacks/{Guid.NewGuid()}/replies",
            new { content = "Nội dung hợp lệ." });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---------------------------------------------------------------------------------------
    // Đổi trạng thái
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Doi_trang_thai_tra_200_va_dong_dau_UpdatedAt()
    {
        using var factory = new TestAppFactory();
        var (client, _) = await SignInAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var hanhKhach = await SeedUserAsync(factory, RoleIds.Passenger, RoleCodes.Passenger);
        var phanAnh = await SeedFeedbackAsync(factory, hanhKhach.Id);

        var response = await client.PatchAsJsonAsync(
            $"/api/admin/feedbacks/{phanAnh.Id}",
            new { status = "InProgress" });
        var body = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("InProgress", body.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.String, body.GetProperty("updatedAt").ValueKind);

        // GET lại phải thấy đúng trạng thái vừa ghi — response của PATCH không phải ảnh chụp rời.
        var docLai = await ReadJsonAsync(await client.GetAsync($"/api/admin/feedbacks/{phanAnh.Id}"));
        Assert.Equal("InProgress", docLai.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Mo_lai_phan_anh_da_xu_ly_la_hop_le()
    {
        using var factory = new TestAppFactory();
        var (client, _) = await SignInAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var hanhKhach = await SeedUserAsync(factory, RoleIds.Passenger, RoleCodes.Passenger);
        var phanAnh = await SeedFeedbackAsync(factory, hanhKhach.Id, status: FeedbackStatus.Resolved);

        // Không có máy trạng thái: khách phản hồi thêm thì mở lại là ca có thật — chặn sai chiều
        // thì màn hình không còn đường sửa (quyết định ghi trong hợp đồng).
        var body = await ReadJsonAsync(await client.PatchAsJsonAsync(
            $"/api/admin/feedbacks/{phanAnh.Id}",
            new { status = "InProgress" }));

        Assert.Equal("InProgress", body.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Doi_lai_dung_trang_thai_cu_thi_khong_dong_dau_UpdatedAt()
    {
        using var factory = new TestAppFactory();
        var (client, _) = await SignInAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var hanhKhach = await SeedUserAsync(factory, RoleIds.Passenger, RoleCodes.Passenger);
        var phanAnh = await SeedFeedbackAsync(factory, hanhKhach.Id, status: FeedbackStatus.New);

        // Idempotent: gọi lại đúng trạng thái cũ vẫn 200 nhưng KHÔNG đóng dấu thời gian giả —
        // UpdatedAt chỉ đổi khi dữ liệu thật sự đổi.
        var body = await ReadJsonAsync(await client.PatchAsJsonAsync(
            $"/api/admin/feedbacks/{phanAnh.Id}",
            new { status = "New" }));

        Assert.Equal("New", body.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("updatedAt").ValueKind);
    }

    [Theory]
    [InlineData("Done")]
    [InlineData("3")] // chuỗi số: Enum.TryParse sẽ nhận "3" thành giá trị thứ tư — parser so tên nên chặn (A3)
    public async Task Doi_trang_thai_khong_hop_le_thi_tra_400(string status)
    {
        using var factory = new TestAppFactory();
        var (client, _) = await SignInAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var hanhKhach = await SeedUserAsync(factory, RoleIds.Passenger, RoleCodes.Passenger);
        var phanAnh = await SeedFeedbackAsync(factory, hanhKhach.Id);

        var response = await client.PatchAsJsonAsync(
            $"/api/admin/feedbacks/{phanAnh.Id}",
            new { status });
        var body = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            "Trạng thái không hợp lệ. Chấp nhận: New, InProgress, Resolved",
            Assert.Single(ErrorsOf(body, "status")));
    }

    [Fact]
    public async Task Doi_trang_thai_doc_hoa_thuong_deu_nhan_va_luu_ten_chuan()
    {
        using var factory = new TestAppFactory();
        var (client, _) = await SignInAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var hanhKhach = await SeedUserAsync(factory, RoleIds.Passenger, RoleCodes.Passenger);
        var phanAnh = await SeedFeedbackAsync(factory, hanhKhach.Id);

        // Cùng lối RouteService/FareService/TripLookupService: đọc bỏ qua hoa/thường, nhưng giá
        // trị LƯU xuống CSDL luôn là tên chuẩn của enum — trong bảng không bao giờ có hai kiểu
        // viết của cùng một trạng thái.
        var body = await ReadJsonAsync(await client.PatchAsJsonAsync(
            $"/api/admin/feedbacks/{phanAnh.Id}",
            new { status = "inprogress" }));

        Assert.Equal("InProgress", body.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Doi_trang_thai_thieu_thi_tra_400()
    {
        using var factory = new TestAppFactory();
        var (client, _) = await SignInAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var hanhKhach = await SeedUserAsync(factory, RoleIds.Passenger, RoleCodes.Passenger);
        var phanAnh = await SeedFeedbackAsync(factory, hanhKhach.Id);

        var response = await client.PatchAsJsonAsync($"/api/admin/feedbacks/{phanAnh.Id}", new { });
        var body = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotEmpty(ErrorsOf(body, "status"));
    }

    [Fact]
    public async Task Doi_trang_thai_phan_anh_khong_ton_tai_thi_tra_404()
    {
        using var factory = new TestAppFactory();
        var (client, _) = await SignInAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");

        var response = await client.PatchAsJsonAsync(
            $"/api/admin/feedbacks/{Guid.NewGuid()}",
            new { status = "Resolved" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---------------------------------------------------------------------------------------
    // Helper — mỗi lớp test tự chép, không có lớp base chung (đúng lệ đang có của dự án)
    // ---------------------------------------------------------------------------------------

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
        => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static string[] PropertyNamesOf(JsonElement element)
        => element.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray();

    private static string[] ContentsOf(JsonElement body)
        => body.GetProperty("items")
            .EnumerateArray()
            .Select(item => item.GetProperty("content").GetString() ?? string.Empty)
            .ToArray();

    private static string[] ErrorsOf(JsonElement body, string field)
        => body.GetProperty("errors").GetProperty(field)
            .EnumerateArray()
            .Select(error => error.GetString() ?? string.Empty)
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

    /// <summary>
    /// Phản ánh seed sẵn ở trạng thái mặc định New. <c>tripId</c> để nguyên null trừ ca cần kiểm
    /// chuyến — API không join bảng Trips (chỉ trả id thô), nên test không phải dựng chuyến.
    /// </summary>
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

    private static async Task<FeedbackReply> SeedReplyAsync(
        TestAppFactory factory,
        Guid feedbackId,
        Guid userId,
        string content = "Nhà xe xin lỗi vì sự cố.",
        DateTime? createdAt = null)
    {
        var reply = new FeedbackReply
        {
            Id = Guid.NewGuid(),
            FeedbackId = feedbackId,
            UserId = userId,
            Content = content,
            CreatedAt = createdAt ?? DateTime.UtcNow,
        };

        await factory.SeedAsync(db => db.FeedbackReplies.Add(reply));

        return reply;
    }
}
