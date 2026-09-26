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
/// Test tích hợp cho CRUD ba nhóm tài nguyên của story 12 (Quản lý tuyến &amp; trạm):
/// <c>/api/routes</c>, <c>/api/stops</c> và <c>/api/routes/{routeId}/fares</c>.
///
/// <para>
/// <see cref="RouteStopApiTests"/> đã phủ nhóm thứ tư — <c>/routes/{routeId}/stops</c> — nên
/// lớp này không lặp lại. Bốn nhóm tách thành hai lớp vì mỗi lớp đã dài; gộp cả bốn vào một
/// file thì lúc đỏ không biết đỏ ở mảng nào.
/// </para>
///
/// <para>
/// Dùng lại <see cref="TestAppFactory"/> của JwtAuthTests: chạy trên app thật, mỗi test một
/// CSDL InMemory riêng.
/// </para>
///
/// <para>
/// ⚠️ Provider InMemory KHÔNG dựng unique index <c>Routes.Code</c> lẫn <c>(RouteId, PassengerType)</c>
/// của Fares. Nên các ca "trùng mã tuyến" / "trùng đối tượng hành khách" ở đây kiểm chứng lớp
/// kiểm tra trong <c>RouteService</c> và <c>FareService</c> — chính là lớp giữ cho thông báo lỗi
/// đúng ở production; còn ràng buộc dưới CSDL (lớp chặn cuối khi hai request chạy song song) thì
/// chỉ chạy được với PostgreSQL thật.
/// </para>
/// </summary>
public class RouteStopFareCrudApiTests
{
    // ---------------------------------------------------------------------------------------
    // Phân quyền
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Không gửi token thì middleware JWT chặn trước khi vào tới controller — 401, không phải 403.
    /// Phân biệt đúng hai mã này quan trọng với frontend: 401 thì đăng nhập lại, 403 thì báo
    /// "không có quyền".
    /// </summary>
    [Fact]
    public async Task Khong_gui_token_thi_moi_nhom_deu_tra_401()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        var responses = await SendAllAsync(client, Guid.NewGuid(), Guid.NewGuid());

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode));
    }

    [Theory]
    [InlineData(RoleCodes.Driver)]
    [InlineData(RoleCodes.Passenger)]
    public async Task Vai_tro_duoi_quan_ly_goi_moi_endpoint_thi_tra_403(string roleCode)
    {
        using var factory = new TestAppFactory();

        var client = await SignInAsync(factory, RoleIdsFor(roleCode), roleCode);

        // Cả ba controller đều gác bằng ManagerOrAbove ở mức lớp, nên phải kiểm tra từng action —
        // sót một attribute ở một action là một lỗ hổng phân quyền, không phải một thiếu sót nhỏ.
        var responses = await SendAllAsync(client, Guid.NewGuid(), Guid.NewGuid());

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode));
    }

    /// <summary>
    /// 403 phải kèm thông báo đọc được, đúng câu hợp đồng ghi ở mục "Trạm dừng — /stops".
    /// Frontend hiển thị thẳng chuỗi này cho người dùng.
    /// </summary>
    [Fact]
    public async Task Vai_tro_duoi_quan_ly_nhan_403_kem_thong_bao_doc_duoc()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsync(factory, RoleIds.Driver, RoleCodes.Driver);

        var response = await client.GetAsync(RoutesUrl);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("không có quyền", await MessageAsync(response));
    }

    [Theory]
    [InlineData(RoleCodes.Admin)]
    [InlineData(RoleCodes.Manager)]
    public async Task Admin_va_quan_ly_deu_doc_duoc_ca_ba_nhom(string roleCode)
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsync(factory, RoleIdsFor(roleCode), roleCode);

        var route = await SeedRouteAsync(factory);

        var responses = new[]
        {
            await client.GetAsync(RoutesUrl),
            await client.GetAsync(StopsUrl),
            await client.GetAsync(FaresUrl(route.Id)),
        };

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
    }

    // ---------------------------------------------------------------------------------------
    // GET /api/routes — danh sách, tìm kiếm, lọc, phân trang
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Get_danh_sach_tuyen_tra_ve_dung_hinh_dang_phan_trang()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        await SeedRouteAsync(factory, code: "01");

        var body = await ReadJsonAsync(await client.GetAsync(RoutesUrl));

        // total là tổng số dòng khớp lọc, frontend dùng nó để vẽ phân trang của AntD Table.
        Assert.Equal(1, body.GetProperty("total").GetInt32());
        Assert.Equal(1, body.GetProperty("page").GetInt32());
        Assert.Equal(1, ItemsOf(body).GetArrayLength());
    }

    [Fact]
    public async Task Get_danh_sach_tuyen_khong_truyen_gi_thi_mac_dinh_trang_1_moi_trang_10()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var body = await ReadJsonAsync(await client.GetAsync(RoutesUrl));

        Assert.Equal(1, body.GetProperty("page").GetInt32());
        Assert.Equal(10, body.GetProperty("pageSize").GetInt32());
    }

    [Fact]
    public async Task Get_danh_sach_tuyen_tra_ve_tuyen_moi_nhat_truoc()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        // Ghi đè createdAt thay vì để DateTime.UtcNow: ba bản ghi seed trong cùng một mili giây
        // sẽ trùng createdAt và thứ tự rơi hết về khoá phụ Id — bài test mất khả năng bắt lỗi
        // quên OrderByDescending.
        var baseTime = new DateTime(2026, 9, 20, 8, 0, 0, DateTimeKind.Utc);
        await SeedRouteAsync(factory, code: "01", createdAt: baseTime);
        await SeedRouteAsync(factory, code: "03", createdAt: baseTime.AddMinutes(2));
        await SeedRouteAsync(factory, code: "02", createdAt: baseTime.AddMinutes(1));

        var body = await ReadJsonAsync(await client.GetAsync(RoutesUrl));

        Assert.Equal(["03", "02", "01"], CodesOf(ItemsOf(body)));
    }

    [Theory]
    [InlineData("b10", "B10")]
    [InlineData("CHỢ LỚN", "01")]
    [InlineData("hà đông", "03")]
    public async Task Get_danh_sach_tuyen_tim_khong_phan_biet_hoa_thuong(string search, string expectedCode)
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        await SeedRouteAsync(factory, code: "01", name: "Bến Thành — Chợ Lớn", origin: "Bến Thành", destination: "Chợ Lớn");
        await SeedRouteAsync(factory, code: "B10", name: "Cầu Giấy — Long Biên", origin: "Cầu Giấy", destination: "Long Biên");
        await SeedRouteAsync(factory, code: "03", name: "Hà Đông — Mỹ Đình", origin: "Hà Đông", destination: "Mỹ Đình");

        var response = await client.GetAsync($"{RoutesUrl}?search={Uri.EscapeDataString(search)}");
        var body = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal([expectedCode], CodesOf(ItemsOf(body)));
    }

    [Fact]
    public async Task Get_danh_sach_tuyen_tim_theo_ma_ten_diem_dau_diem_cuoi_deu_khop()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        // Bốn trường hợp dùng chung một tuyến và mỗi ca chỉ khớp đúng MỘT trường, để bài test
        // bắt được lỗi bỏ sót một nhánh trong câu Where của RouteService.ListAsync.
        await SeedRouteAsync(factory, code: "01", name: "Bến Thành — Chợ Lớn", origin: "Bến Thành", destination: "Chợ Lớn");

        var byCode = await ReadJsonAsync(await client.GetAsync($"{RoutesUrl}?search=01"));
        var byName = await ReadJsonAsync(await client.GetAsync($"{RoutesUrl}?search={Uri.EscapeDataString("Bến Thành — Chợ Lớn")}"));
        var byOrigin = await ReadJsonAsync(await client.GetAsync($"{RoutesUrl}?search={Uri.EscapeDataString("Bến Thành")}"));
        var byDestination = await ReadJsonAsync(await client.GetAsync($"{RoutesUrl}?search={Uri.EscapeDataString("Chợ Lớn")}"));

        Assert.All(
            new[] { byCode, byName, byOrigin, byDestination },
            body => Assert.Equal(1, body.GetProperty("total").GetInt32()));
    }

    [Theory]
    [InlineData("Active", 1)]
    [InlineData("Inactive", 1)]
    [InlineData("active", 1)]
    [InlineData("KhongCo", 0)]
    public async Task Get_danh_sach_tuyen_loc_theo_trang_thai(string status, int expectedTotal)
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        await SeedRouteAsync(factory, code: "01", status: RouteStatus.Active);
        await SeedRouteAsync(factory, code: "02", status: RouteStatus.Inactive);

        var response = await client.GetAsync($"{RoutesUrl}?status={status}");
        var body = await ReadJsonAsync(response);

        // Mã trạng thái lạ KHÔNG phải lỗi 400 — hợp đồng chốt trả danh sách rỗng, cùng lối bộ lọc
        // role của /admin/users. Ca "KhongCo" khẳng định cả mã 200 lẫn total 0.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(expectedTotal, body.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Get_danh_sach_tuyen_total_la_tong_khop_loc_chu_khong_phai_so_dong_trong_trang()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var baseTime = new DateTime(2026, 9, 20, 8, 0, 0, DateTimeKind.Utc);
        await SeedRouteAsync(factory, code: "01", createdAt: baseTime);
        await SeedRouteAsync(factory, code: "02", createdAt: baseTime.AddMinutes(1));
        await SeedRouteAsync(factory, code: "03", createdAt: baseTime.AddMinutes(2));

        var firstPage = await ReadJsonAsync(await client.GetAsync($"{RoutesUrl}?page=1&pageSize=2"));
        var secondPage = await ReadJsonAsync(await client.GetAsync($"{RoutesUrl}?page=2&pageSize=2"));

        Assert.Equal(2, ItemsOf(firstPage).GetArrayLength());
        Assert.Equal(3, firstPage.GetProperty("total").GetInt32());
        Assert.Equal(["03", "02"], CodesOf(ItemsOf(firstPage)));

        // Trang 2 chỉ còn đúng một dòng, và total vẫn là 3 chứ không phải 1 — lỗi hay gặp là
        // đếm số dòng sau khi đã Skip/Take.
        Assert.Equal(["01"], CodesOf(ItemsOf(secondPage)));
        Assert.Equal(3, secondPage.GetProperty("total").GetInt32());
    }

    [Theory]
    [InlineData("page=0", "page", "Số trang phải từ 1 trở lên")]
    [InlineData("pageSize=0", "pageSize", "Số dòng mỗi trang phải từ 1 đến 100")]
    [InlineData("pageSize=101", "pageSize", "Số dòng mỗi trang phải từ 1 đến 100")]
    public async Task Get_danh_sach_tuyen_trang_ngoai_khoang_tra_400(string query, string field, string message)
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var response = await client.GetAsync($"{RoutesUrl}?{query}");
        var body = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Dữ liệu đầu vào không hợp lệ", body.GetProperty("message").GetString());
        Assert.Contains(field, ErrorFieldsOf(body));

        // Kiểm tra luôn câu chữ: gắn [Range] mà quên ErrorMessage thì client nhận câu tiếng Anh
        // của framework, trái quy ước D3.
        Assert.Equal(message, body.GetProperty("errors").GetProperty(field)[0].GetString());
    }

    [Fact]
    public async Task Get_chi_tiet_tuyen_tra_ve_du_truong_va_trang_thai_dang_chuoi()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var route = await SeedRouteAsync(
            factory,
            code: "01",
            name: "Bến Thành — Chợ Lớn",
            origin: "Bến Thành",
            destination: "Chợ Lớn",
            distanceKm: 12.5m);

        var body = await ReadJsonAsync(await client.GetAsync(RouteUrl(route.Id)));

        Assert.Equal(route.Id, body.GetProperty("id").GetGuid());
        Assert.Equal("01", body.GetProperty("code").GetString());
        Assert.Equal("Bến Thành — Chợ Lớn", body.GetProperty("name").GetString());
        Assert.Equal("Bến Thành", body.GetProperty("origin").GetString());
        Assert.Equal("Chợ Lớn", body.GetProperty("destination").GetString());
        Assert.Equal(12.5m, body.GetProperty("distanceKm").GetDecimal());

        // Chuỗi chứ không phải số: Program.cs không đăng ký JsonStringEnumConverter nên nếu
        // RouteResponse để kiểu enum thì client nhận 0 — trái tinh thần quy ước A3.
        Assert.Equal("Active", body.GetProperty("status").GetString());

        // Chưa sửa lần nào thì updatedAt phải là null, không phải chuỗi rỗng.
        Assert.Equal(JsonValueKind.Null, body.GetProperty("updatedAt").ValueKind);
    }

    [Fact]
    public async Task Get_chi_tiet_tuyen_khong_ton_tai_tra_404()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var response = await client.GetAsync(RouteUrl(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Không tìm thấy tuyến đường", await MessageAsync(response));
    }

    // ---------------------------------------------------------------------------------------
    // POST /api/routes
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Post_tao_tuyen_tra_201_kem_duong_dan_toi_tuyen_vua_tao()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var response = await client.PostAsJsonAsync(
            RoutesUrl,
            RouteBody(code: "01", distanceKm: 12.5m));
        var body = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        // 201 phải kèm Location trỏ tới tài nguyên vừa tạo, nếu không client phải đoán id.
        Assert.NotNull(response.Headers.Location);
        Assert.Equal(RouteUrl(body.GetProperty("id").GetGuid()), response.Headers.Location!.AbsolutePath);

        Assert.Equal("01", body.GetProperty("code").GetString());
        Assert.Equal(12.5m, body.GetProperty("distanceKm").GetDecimal());

        // Tuyến mới luôn Active — CreateRouteRequest cố ý không nhận trường status.
        Assert.Equal("Active", body.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Post_tao_tuyen_cat_khoang_trang_o_cac_truong_chuoi()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var body = await ReadJsonAsync(await client.PostAsJsonAsync(
            RoutesUrl,
            RouteBody(code: " 01 ", name: "  Bến Thành — Chợ Lớn  ")));

        Assert.Equal("01", body.GetProperty("code").GetString());
        Assert.Equal("Bến Thành — Chợ Lớn", body.GetProperty("name").GetString());
    }

    [Fact]
    public async Task Post_trung_ma_tuyen_tra_400_o_errors_code()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        await SeedRouteAsync(factory, code: "01");

        var response = await client.PostAsJsonAsync(RoutesUrl, RouteBody(code: "01"));
        var body = await ReadJsonAsync(response);

        // 400 chứ không phải 409: mã tuyến là khoá do quản lý gõ tay vào ô form, frontend gắn
        // thẳng lỗi vào ô input — cùng lý do trùng SĐT trả 400 errors.phoneNumber.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Mã tuyến đã tồn tại", body.GetProperty("message").GetString());
        Assert.Contains("code", ErrorFieldsOf(body));
    }

    [Fact]
    public async Task Post_trung_ma_tuyen_sau_khi_cat_khoang_trang_van_bi_chan()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        await SeedRouteAsync(factory, code: "01");

        // Mã " 01 " dài 4 ký tự nên lọt qua [StringLength], nhưng sau Trim lại trùng "01".
        // Kiểm tra unique phải chạy trên chuỗi đã Trim, nếu không sẽ có hai tuyến cùng mã.
        var response = await client.PostAsJsonAsync(RoutesUrl, RouteBody(code: " 01 "));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("code", ErrorFieldsOf(await ReadJsonAsync(response)));
    }

    [Theory]
    [InlineData(-1, "Chiều dài tuyến không được âm")]
    [InlineData("10000", "Chiều dài tuyến tối đa 9 999.99 km")]
    public async Task Post_chieu_dai_ngoai_bien_tra_400_o_errors_distanceKm(object distanceKm, string message)
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var response = await client.PostAsJsonAsync(
            RoutesUrl,
            RouteBody(distanceKm: Convert.ToDecimal(distanceKm)));
        var body = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("distanceKm", ErrorFieldsOf(body));
        Assert.Equal(message, body.GetProperty("errors").GetProperty("distanceKm")[0].GetString());
    }

    [Fact]
    public async Task Post_chieu_dai_dung_bang_tran_thi_van_tao_duoc()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        // Biên trên là trần cột numeric(6,2) — 9999.99 vẫn hợp lệ, chỉ 10000 mới bị chặn.
        // Ca này bắt lỗi dùng ">" thành ">=" ở ValidateDistanceKm.
        var response = await client.PostAsJsonAsync(RoutesUrl, RouteBody(distanceKm: 9999.99m));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Post_bo_trong_chieu_dai_thi_coi_nhu_0_km()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var body = await ReadJsonAsync(await client.PostAsJsonAsync(RoutesUrl, new
        {
            code = "01",
            name = "Bến Thành — Chợ Lớn",
            origin = "Bến Thành",
            destination = "Chợ Lớn",
        }));

        // Tuyến chưa đo chiều dài vẫn tạo được — hợp đồng ghi rõ "Bỏ trống = 0".
        Assert.Equal(0m, body.GetProperty("distanceKm").GetDecimal());
    }

    [Theory]
    [InlineData("code", "Mã tuyến không được để trống")]
    [InlineData("name", "Tên tuyến không được để trống")]
    [InlineData("origin", "Điểm đầu không được để trống")]
    [InlineData("destination", "Điểm cuối không được để trống")]
    public async Task Post_thieu_truong_bat_buoc_tra_400_kem_ten_truong(string field, string message)
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var body = await ReadJsonAsync(await client.PostAsJsonAsync(RoutesUrl, RouteBodyWithout(field)));

        Assert.Equal("Dữ liệu đầu vào không hợp lệ", body.GetProperty("message").GetString());
        Assert.Contains(field, ErrorFieldsOf(body));
        Assert.Equal(message, body.GetProperty("errors").GetProperty(field)[0].GetString());
    }

    [Fact]
    public async Task Post_ma_tuyen_chi_mot_ky_tu_tra_400_o_errors_code()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var response = await client.PostAsJsonAsync(RoutesUrl, RouteBody(code: "0"));
        var body = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("code", ErrorFieldsOf(body));
        Assert.Equal("Mã tuyến phải từ 2 đến 20 ký tự", body.GetProperty("errors").GetProperty("code")[0].GetString());
    }

    // ---------------------------------------------------------------------------------------
    // PUT /api/routes/{id}
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Put_sua_tuyen_cap_nhat_du_truong_va_dat_updatedAt()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var route = await SeedRouteAsync(factory, code: "01");
        Assert.Null(route.UpdatedAt);

        var body = await ReadJsonAsync(await client.PutAsJsonAsync(
            RouteUrl(route.Id),
            RouteUpdateBody(
                code: "01B",
                name: "Bến Thành — Bình Chánh",
                origin: "Bến Thành",
                destination: "Bình Chánh",
                distanceKm: 18.75m)));

        Assert.Equal("01B", body.GetProperty("code").GetString());
        Assert.Equal("Bến Thành — Bình Chánh", body.GetProperty("name").GetString());
        Assert.Equal("Bình Chánh", body.GetProperty("destination").GetString());
        Assert.Equal(18.75m, body.GetProperty("distanceKm").GetDecimal());

        // updatedAt phải được đặt sau lần sửa đầu tiên — màn hình quản trị dựa vào nó để biết
        // bản ghi nào vừa bị ai đó sửa.
        Assert.Equal(JsonValueKind.String, body.GetProperty("updatedAt").ValueKind);
    }

    [Fact]
    public async Task Put_bo_trong_status_thi_giu_nguyen_trang_thai_hien_tai()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var route = await SeedRouteAsync(factory, code: "01", status: RouteStatus.Active);

        var body = await ReadJsonAsync(await client.PutAsJsonAsync(
            RouteUrl(route.Id),
            RouteUpdateBody(code: "01", name: "Tuyến Một", status: null)));

        Assert.Equal("Active", body.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Put_status_Active_mo_lai_duoc_tuyen_da_ngung_khai_thac()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var route = await SeedRouteAsync(factory, code: "01", status: RouteStatus.Inactive);

        var body = await ReadJsonAsync(await client.PutAsJsonAsync(
            RouteUrl(route.Id),
            RouteUpdateBody(code: "01", name: "Tuyến Một", status: "Active")));

        Assert.Equal("Active", body.GetProperty("status").GetString());
    }

    [Theory]
    [InlineData("DangChay")]
    [InlineData("1")]
    public async Task Put_status_khong_hop_le_tra_400_o_errors_status(string status)
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var route = await SeedRouteAsync(factory, code: "01");

        var response = await client.PutAsJsonAsync(
            RouteUrl(route.Id),
            RouteUpdateBody(code: "01", name: "Tuyến Một", status: status));
        var body = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("status", ErrorFieldsOf(body));

        // Danh sách mã hợp lệ lấy từ chính enum nên câu chữ phải khớp từng chữ.
        Assert.Equal(
            "Trạng thái không hợp lệ. Chấp nhận: Active, Inactive",
            body.GetProperty("errors").GetProperty("status")[0].GetString());

        // "1" là ca đáng giá nhất: Enum.TryParse sẽ nhận nó thành Inactive, còn quy ước A3 bắt
        // trạng thái phải là chuỗi đọc được — nên RouteService so tên enum thay vì TryParse.
        var stored = await QueryAsync(factory, db => db.Routes.FirstAsync(r => r.Id == route.Id));
        Assert.Equal(RouteStatus.Active, stored.Status);
    }

    [Fact]
    public async Task Put_doi_ma_sang_ma_cua_tuyen_khac_tra_400_o_errors_code()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        await SeedRouteAsync(factory, code: "01");
        var other = await SeedRouteAsync(factory, code: "02");

        var response = await client.PutAsJsonAsync(
            RouteUrl(other.Id),
            RouteUpdateBody(code: "01", name: "Tuyến Hai"));
        var body = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Mã tuyến đã tồn tại", body.GetProperty("message").GetString());
        Assert.Contains("code", ErrorFieldsOf(body));
    }

    [Fact]
    public async Task Put_giu_nguyen_ma_cua_chinh_minh_thi_khong_bao_trung()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var route = await SeedRouteAsync(factory, code: "01");

        // Kiểm tra unique phải loại trừ chính bản ghi đang sửa, nếu không thì không sửa được
        // tuyến nào mà không đổi mã.
        var response = await client.PutAsJsonAsync(
            RouteUrl(route.Id),
            RouteUpdateBody(code: "01", name: "Tuyến Một (đổi tên)"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Put_chieu_dai_am_tra_400_o_errors_distanceKm()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var route = await SeedRouteAsync(factory, code: "01");

        var response = await client.PutAsJsonAsync(
            RouteUrl(route.Id),
            RouteUpdateBody(code: "01", name: "Tuyến Một", distanceKm: -0.5m));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("distanceKm", ErrorFieldsOf(await ReadJsonAsync(response)));
    }

    [Fact]
    public async Task Put_tuyen_khong_ton_tai_tra_404()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var response = await client.PutAsJsonAsync(
            RouteUrl(Guid.NewGuid()),
            RouteUpdateBody(code: "01", name: "Tuyến Một"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Không tìm thấy tuyến đường", await MessageAsync(response));
    }

    // ---------------------------------------------------------------------------------------
    // DELETE /api/routes/{id} — xoá mềm
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Delete_tuyen_tra_200_kem_tuyen_da_ngung_khai_thac()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var route = await SeedRouteAsync(factory, code: "01", status: RouteStatus.Active);

        var response = await client.DeleteAsync(RouteUrl(route.Id));
        var body = await ReadJsonAsync(response);

        // 200 kèm Route, KHÔNG phải 204: cùng lối DELETE /admin/users/{id} trả về tài khoản đã
        // khoá, để màn hình quản trị cập nhật lại dòng mà không phải gọi thêm GET.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(route.Id, body.GetProperty("id").GetGuid());
        Assert.Equal("Inactive", body.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Delete_tuyen_la_xoa_mem_ban_ghi_van_con_trong_csdl()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var route = await SeedRouteAsync(factory, code: "01");

        await client.DeleteAsync(RouteUrl(route.Id));

        // Còn chuyến và vé cũ tham chiếu tới tuyến, mà quy ước A4 cấm thêm cột IsDeleted —
        // nên bản ghi phải còn nguyên trong CSDL, chỉ đổi trạng thái.
        var stored = await QueryAsync(factory, db => db.Routes.FirstOrDefaultAsync(r => r.Id == route.Id));

        Assert.NotNull(stored);
        Assert.Equal(RouteStatus.Inactive, stored!.Status);
    }

    [Fact]
    public async Task Delete_tuyen_da_ngung_khai_thac_goi_lai_van_tra_200()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var route = await SeedRouteAsync(factory, code: "01", status: RouteStatus.Inactive);

        // Idempotent — cùng lối AdminUserService.SetStatusAsync. Gọi lần hai không được thành 404
        // hay 409, vì client retry sau khi mạng chập chờn là chuyện thường.
        var first = await client.DeleteAsync(RouteUrl(route.Id));
        var second = await client.DeleteAsync(RouteUrl(route.Id));

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
    }

    [Fact]
    public async Task Delete_tuyen_khong_ton_tai_tra_404()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var response = await client.DeleteAsync(RouteUrl(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_tuyen_khong_dung_toi_tram_va_gia_ve_cua_tuyen()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var route = await SeedRouteAsync(factory, code: "01");
        var stop = await SeedStopAsync(factory, "Trạm Cầu Giấy");
        await SeedRouteStopAsync(factory, route.Id, stop.Id, stopOrder: 1);
        await SeedFareAsync(factory, route.Id, PassengerType.Standard, 7000m);

        await client.DeleteAsync(RouteUrl(route.Id));

        // Xoá mềm tuyến KHÔNG được kéo theo dữ liệu con — tuyến ngừng khai thác vẫn phải giữ
        // trạm và bảng giá để mở lại được bằng PUT status = "Active".
        var stops = await ReadJsonAsync(await client.GetAsync($"/api/routes/{route.Id}/stops"));
        var fares = await ReadJsonAsync(await client.GetAsync(FaresUrl(route.Id)));

        Assert.Equal(1, stops.GetArrayLength());
        Assert.Equal(1, fares.GetArrayLength());
    }

    // ---------------------------------------------------------------------------------------
    // GET /api/stops
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Get_danh_sach_tram_xep_theo_ten()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        // Cố ý seed lệch thứ tự alphabet để bài test bắt được lỗi quên OrderBy.
        await SeedStopAsync(factory, "Trạm Cầu Giấy");
        await SeedStopAsync(factory, "Bến Thành");
        await SeedStopAsync(factory, "Chợ Lớn");

        var body = await ReadJsonAsync(await client.GetAsync(StopsUrl));

        Assert.Equal(["Bến Thành", "Chợ Lớn", "Trạm Cầu Giấy"], NamesOf(body));
    }

    [Fact]
    public async Task Get_danh_sach_tram_khi_chua_co_tram_nao_tra_mang_rong()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var response = await client.GetAsync(StopsUrl);
        var body = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, body.GetArrayLength());
    }

    [Fact]
    public async Task Get_chi_tiet_tram_tra_ve_dung_nam_truong_cua_hop_dong()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var stop = await SeedStopAsync(factory, "Trạm Cầu Giấy", latitude: 21.0307, longitude: 105.8034);

        var body = await ReadJsonAsync(await client.GetAsync(StopUrl(stop.Id)));

        Assert.Equal(stop.Id, body.GetProperty("id").GetGuid());
        Assert.Equal("Trạm Cầu Giấy", body.GetProperty("name").GetString());
        Assert.Equal("Địa chỉ Trạm Cầu Giấy", body.GetProperty("address").GetString());
        Assert.Equal(21.0307, body.GetProperty("latitude").GetDouble(), 4);
        Assert.Equal(105.8034, body.GetProperty("longitude").GetDouble(), 4);

        // Hợp đồng và kiểu Stop bên frontend chốt đúng 5 trường. Thêm createdAt/updatedAt vào
        // StopResponse là đổi hình dạng API — phải sửa api-contract.md trước.
        Assert.Equal(5, body.EnumerateObject().Count());
    }

    [Fact]
    public async Task Get_chi_tiet_tram_khong_ton_tai_tra_404()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var response = await client.GetAsync(StopUrl(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Không tìm thấy trạm dừng", await MessageAsync(response));
    }

    // ---------------------------------------------------------------------------------------
    // POST /api/stops
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Post_tao_tram_tra_201_kem_duong_dan_toi_tram_vua_tao()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var response = await client.PostAsJsonAsync(StopsUrl, StopBody("Trạm Cầu Giấy"));
        var body = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        Assert.Equal(StopUrl(body.GetProperty("id").GetGuid()), response.Headers.Location!.AbsolutePath);

        Assert.Equal("Trạm Cầu Giấy", body.GetProperty("name").GetString());
        Assert.Equal(21.03, body.GetProperty("latitude").GetDouble(), 4);
    }

    [Fact]
    public async Task Post_tao_duoc_hai_tram_trung_ten()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        // Khác mã tuyến, tên trạm KHÔNG phải khoá nghiệp vụ — hai trạm cùng tên ở hai quận là
        // chuyện bình thường. Ca này khoá lại quyết định đó, tránh việc thêm ràng buộc unique
        // vào cột Name mà không sửa hợp đồng.
        var first = await client.PostAsJsonAsync(StopsUrl, StopBody("Trạm Cầu Giấy"));
        var second = await client.PostAsJsonAsync(StopsUrl, StopBody("Trạm Cầu Giấy"));

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
    }

    [Theory]
    [InlineData("name", "Tên trạm không được để trống")]
    [InlineData("address", "Địa chỉ không được để trống")]
    public async Task Post_thieu_truong_bat_buoc_cua_tram_tra_400(string field, string message)
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var body = await ReadJsonAsync(await client.PostAsJsonAsync(StopsUrl, StopBodyWithout(field)));

        Assert.Contains(field, ErrorFieldsOf(body));
        Assert.Equal(message, body.GetProperty("errors").GetProperty(field)[0].GetString());
    }

    [Theory]
    [InlineData(91.0)]
    [InlineData(-91.0)]
    [InlineData(200.0)]
    public async Task Post_vi_do_ngoai_bien_tra_400_o_errors_latitude(double latitude)
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var response = await client.PostAsJsonAsync(StopsUrl, StopBody("Trạm Cầu Giấy", latitude: latitude));
        var body = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("latitude", ErrorFieldsOf(body));
        Assert.Equal("Vĩ độ phải từ -90 đến 90", body.GetProperty("errors").GetProperty("latitude")[0].GetString());
    }

    /// <summary>
    /// ⛔ LỖI ĐANG MỞ — cần Trần Trung Hiếu sửa <c>StopRequest</c> trước khi task này Done.
    ///
    /// <para>
    /// <c>[Range(-90, 90)]</c> ở <c>StopRequest.Latitude</c> gọi nhầm overload <c>(int, int)</c>
    /// của <see cref="System.ComponentModel.DataAnnotations.RangeAttribute"/>. Overload đó đặt
    /// <c>OperandType = typeof(int)</c>, nên giá trị <c>double</c> bị chuyển sang <c>int</c> TRƯỚC
    /// khi so sánh — 90.01 thành 90 và lọt qua. Cận trên thật của thuộc tính là ±90.5, không phải ±90.
    /// </para>
    ///
    /// <para>
    /// Hệ quả: trạm có vĩ độ 90.4 (bắc cực, không thuộc mạng lưới xe buýt nào) vẫn tạo được và
    /// nằm lại trong CSDL. Hợp đồng ở mục "Trạm dừng — /stops" ghi rõ vĩ độ từ -90 đến 90 và
    /// "vi phạm ràng buộc trên → 400 với errors.&lt;tên trường&gt;".
    /// </para>
    ///
    /// <para>
    /// Cách sửa đề xuất: dùng overload <c>(double, double)</c> — <c>[Range(-90.0, 90.0, …)]</c> —
    /// ở cả <c>Latitude</c> lẫn <c>Longitude</c> (<c>[Range(-180.0, 180.0, …)]</c>), giữ nguyên
    /// câu ErrorMessage.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(90.01)]
    [InlineData(90.4)]
    [InlineData(-90.4)]
    public async Task Post_vi_do_ngoai_bien_sat_nguong_tra_400_o_errors_latitude(double latitude)
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var response = await client.PostAsJsonAsync(StopsUrl, StopBody("Trạm Cầu Giấy", latitude: latitude));
        var body = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("latitude", ErrorFieldsOf(body));
    }

    /// <summary>Biên đúng của hợp đồng vẫn phải nhận — -90 và 90 là vĩ độ hợp lệ.</summary>
    [Theory]
    [InlineData(90.0)]
    [InlineData(-90.0)]
    public async Task Post_vi_do_dung_bang_bien_thi_van_tao_duoc_tram(double latitude)
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var response = await client.PostAsJsonAsync(StopsUrl, StopBody("Trạm Cầu Giấy", latitude: latitude));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Theory]
    [InlineData(181.0)]
    [InlineData(-181.0)]
    public async Task Post_kinh_do_ngoai_bien_tra_400_o_errors_longitude(double longitude)
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var response = await client.PostAsJsonAsync(StopsUrl, StopBody("Trạm Cầu Giấy", longitude: longitude));
        var body = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("longitude", ErrorFieldsOf(body));
    }

    /// <summary>
    /// ⛔ Cùng một lỗi với ca vĩ độ ở trên: <c>[Range(-180, 180)]</c> dùng overload
    /// <c>(int, int)</c> nên 180.4 bị làm tròn thành 180 và lọt qua. Kinh độ hợp lệ tối đa trên
    /// thực tế là 180, nên 180.4 là toạ độ không tồn tại.
    /// </summary>
    [Theory]
    [InlineData(180.01)]
    [InlineData(180.4)]
    [InlineData(-180.4)]
    public async Task Post_kinh_do_ngoai_bien_sat_nguong_tra_400_o_errors_longitude(double longitude)
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var response = await client.PostAsJsonAsync(StopsUrl, StopBody("Trạm Cầu Giấy", longitude: longitude));
        var body = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("longitude", ErrorFieldsOf(body));
    }

    [Fact]
    public async Task Post_ten_tram_chi_mot_ky_tu_tra_400_o_errors_name()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var response = await client.PostAsJsonAsync(StopsUrl, StopBody("A"));
        var body = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Tên trạm phải từ 2 đến 200 ký tự", body.GetProperty("errors").GetProperty("name")[0].GetString());
    }

    // ---------------------------------------------------------------------------------------
    // PUT /api/stops/{id}
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Put_sua_tram_cap_nhat_du_truong_va_dat_updatedAt()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var stop = await SeedStopAsync(factory, "Trạm Cầu Giấy");
        Assert.Null(stop.UpdatedAt);

        var body = await ReadJsonAsync(await client.PutAsJsonAsync(
            StopUrl(stop.Id),
            StopBody("Trạm Cầu Giấy (mới)", latitude: 21.05, longitude: 105.81)));

        Assert.Equal("Trạm Cầu Giấy (mới)", body.GetProperty("name").GetString());
        Assert.Equal("Địa chỉ Trạm Cầu Giấy (mới)", body.GetProperty("address").GetString());
        Assert.Equal(21.05, body.GetProperty("latitude").GetDouble(), 4);
        Assert.Equal(105.81, body.GetProperty("longitude").GetDouble(), 4);

        // StopResponse không có updatedAt (hợp đồng chỉ 5 trường) nên phải đọc thẳng từ CSDL.
        var stored = await QueryAsync(factory, db => db.Stops.FirstAsync(s => s.Id == stop.Id));
        Assert.NotNull(stored.UpdatedAt);
    }

    [Fact]
    public async Task Put_sua_tram_giu_nguyen_ban_ghi_chu_khong_tao_them()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var stop = await SeedStopAsync(factory, "Trạm Cầu Giấy");

        var body = await ReadJsonAsync(await client.PutAsJsonAsync(StopUrl(stop.Id), StopBody("Trạm Cầu Giấy")));

        Assert.Equal(stop.Id, body.GetProperty("id").GetGuid());

        var count = await QueryAsync(factory, db => db.Stops.CountAsync());
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task Put_tram_khong_ton_tai_tra_404()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var response = await client.PutAsJsonAsync(StopUrl(Guid.NewGuid()), StopBody("Trạm Cầu Giấy"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---------------------------------------------------------------------------------------
    // DELETE /api/stops/{id}
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Delete_tram_tra_204_va_xoa_han_khoi_csdl()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var stop = await SeedStopAsync(factory, "Trạm Cầu Giấy");

        var response = await client.DeleteAsync(StopUrl(stop.Id));

        // Stop KHÔNG có cột trạng thái nên không xoá mềm được như Routes (quy ước A4 chỉ cho
        // dùng cột trạng thái sẵn có) — xoá hẳn và trả 204, khác hẳn 200 kèm body của Routes.
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var stored = await QueryAsync(factory, db => db.Stops.FirstOrDefaultAsync(s => s.Id == stop.Id));
        Assert.Null(stored);
    }

    [Fact]
    public async Task Delete_tram_dang_nam_tren_tuyen_tra_409_kem_huong_dan_go_truoc()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var route = await SeedRouteAsync(factory, code: "01");
        var stop = await SeedStopAsync(factory, "Trạm Cầu Giấy");
        await SeedRouteStopAsync(factory, route.Id, stop.Id, stopOrder: 1);

        var response = await client.DeleteAsync(StopUrl(stop.Id));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(
            "Trạm đang nằm trên tuyến đường nên không thể xóa. Gỡ trạm khỏi tuyến trước.",
            await MessageAsync(response));

        // Bị chặn thì không được xoá gì — nếu không thì tuyến còn dòng RouteStops trỏ vào hư không.
        var stored = await QueryAsync(factory, db => db.Stops.FirstOrDefaultAsync(s => s.Id == stop.Id));
        Assert.NotNull(stored);
    }

    [Fact]
    public async Task Delete_duoc_tram_sau_khi_go_khoi_tuyen()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var route = await SeedRouteAsync(factory, code: "01");
        var stop = await SeedStopAsync(factory, "Trạm Cầu Giấy");
        var routeStop = await SeedRouteStopAsync(factory, route.Id, stop.Id, stopOrder: 1);

        await client.DeleteAsync($"/api/routes/{route.Id}/stops/{routeStop.Id}");
        var response = await client.DeleteAsync(StopUrl(stop.Id));

        // Chốt lại vòng đời đầy đủ: gỡ khỏi tuyến xong thì trạm xoá được — thông báo 409 ở ca
        // trên chỉ đúng nếu bước này thật sự mở được đường.
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Delete_tram_khong_ton_tai_tra_404()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var response = await client.DeleteAsync(StopUrl(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---------------------------------------------------------------------------------------
    // GET /api/routes/{routeId}/fares
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Get_bang_gia_tuyen_chua_cau_hinh_tra_mang_rong_chu_khong_phai_404()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var route = await SeedRouteAsync(factory, code: "01");

        var response = await client.GetAsync(FaresUrl(route.Id));
        var body = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, body.GetArrayLength());
    }

    [Fact]
    public async Task Get_bang_gia_tuyen_khong_ton_tai_tra_404()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var response = await client.GetAsync(FaresUrl(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Không tìm thấy tuyến đường", await MessageAsync(response));
    }

    [Fact]
    public async Task Get_bang_gia_xep_theo_thu_tu_khai_bao_cua_enum_chu_khong_theo_alphabet()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var route = await SeedRouteAsync(factory, code: "01");

        // Seed theo thứ tự ngược hẳn với thứ tự hiển thị. Nếu FareService sắp bằng ORDER BY dưới
        // CSDL thì PostgreSQL xếp theo alphabet — Child, Disabled, Senior, Standard, Student —
        // và bảng giá trên màn hình nhảy lung tung.
        await SeedFareAsync(factory, route.Id, PassengerType.Disabled, 3000m);
        await SeedFareAsync(factory, route.Id, PassengerType.Child, 2000m);
        await SeedFareAsync(factory, route.Id, PassengerType.Senior, 4000m);
        await SeedFareAsync(factory, route.Id, PassengerType.Student, 5000m);
        await SeedFareAsync(factory, route.Id, PassengerType.Standard, 7000m);

        var body = await ReadJsonAsync(await client.GetAsync(FaresUrl(route.Id)));

        Assert.Equal(
            ["Standard", "Student", "Senior", "Child", "Disabled"],
            PassengerTypesOf(body));
    }

    [Fact]
    public async Task Get_bang_gia_chi_tra_ve_dong_gia_cua_dung_tuyen_do()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var routeA = await SeedRouteAsync(factory, code: "01");
        var routeB = await SeedRouteAsync(factory, code: "02");

        await SeedFareAsync(factory, routeA.Id, PassengerType.Standard, 7000m);
        await SeedFareAsync(factory, routeB.Id, PassengerType.Standard, 9000m);
        await SeedFareAsync(factory, routeB.Id, PassengerType.Student, 5000m);

        var body = await ReadJsonAsync(await client.GetAsync(FaresUrl(routeA.Id)));

        Assert.Equal(1, body.GetArrayLength());
        Assert.Equal(7000m, body[0].GetProperty("price").GetDecimal());
        Assert.Equal(routeA.Id, body[0].GetProperty("routeId").GetGuid());
    }

    [Fact]
    public async Task Get_mot_dong_gia_tra_ve_du_truong()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var route = await SeedRouteAsync(factory, code: "01");
        var fare = await SeedFareAsync(factory, route.Id, PassengerType.Student, 5000m);

        var body = await ReadJsonAsync(await client.GetAsync(FareUrl(route.Id, fare.Id)));

        Assert.Equal(fare.Id, body.GetProperty("id").GetGuid());
        Assert.Equal(route.Id, body.GetProperty("routeId").GetGuid());
        Assert.Equal("Student", body.GetProperty("passengerType").GetString());
        Assert.Equal(5000m, body.GetProperty("price").GetDecimal());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("updatedAt").ValueKind);
    }

    [Fact]
    public async Task Get_dong_gia_cua_tuyen_khac_tra_404_chu_khong_phai_200()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var routeA = await SeedRouteAsync(factory, code: "01");
        var routeB = await SeedRouteAsync(factory, code: "02");
        var fareOfA = await SeedFareAsync(factory, routeA.Id, PassengerType.Standard, 7000m);

        // Id dòng giá là duy nhất toàn hệ thống, nên tra thẳng vẫn ra — nhưng hợp đồng bắt kiểm
        // tra nó có thuộc đúng tuyến trên đường dẫn không. Sót bước này là lỗi rò rỉ dữ liệu
        // giữa hai tuyến.
        var response = await client.GetAsync(FareUrl(routeB.Id, fareOfA.Id));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Không tìm thấy giá vé", await MessageAsync(response));
    }

    // ---------------------------------------------------------------------------------------
    // POST /api/routes/{routeId}/fares
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Post_them_gia_ve_tra_201_kem_duong_dan_toi_dong_vua_tao()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var route = await SeedRouteAsync(factory, code: "01");

        var response = await client.PostAsJsonAsync(
            FaresUrl(route.Id),
            new { passengerType = "Student", price = 5000m });
        var body = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        Assert.Equal(
            FareUrl(route.Id, body.GetProperty("id").GetGuid()),
            response.Headers.Location!.AbsolutePath);

        Assert.Equal("Student", body.GetProperty("passengerType").GetString());
        Assert.Equal(5000m, body.GetProperty("price").GetDecimal());
    }

    [Theory]
    [InlineData("student")]
    [InlineData("STUDENT")]
    public async Task Post_ma_doi_tuong_khong_phan_biet_hoa_thuong_nhung_luu_dang_chuan(string passengerType)
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var route = await SeedRouteAsync(factory, code: "01");

        var body = await ReadJsonAsync(await client.PostAsJsonAsync(
            FaresUrl(route.Id),
            new { passengerType, price = 5000m }));

        // Giá trị lưu xuống CSDL luôn là tên chuẩn của enum, nên trong bảng không bao giờ có
        // hai kiểu viết của cùng một đối tượng.
        Assert.Equal("Student", body.GetProperty("passengerType").GetString());
    }

    [Fact]
    public async Task Post_them_gia_cho_tuyen_khong_ton_tai_tra_404()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var response = await client.PostAsJsonAsync(
            FaresUrl(Guid.NewGuid()),
            new { passengerType = "Standard", price = 7000m });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Không tìm thấy tuyến đường", await MessageAsync(response));
    }

    [Fact]
    public async Task Post_ma_doi_tuong_khong_hop_le_tra_400_kem_danh_sach_ma_hop_le()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var route = await SeedRouteAsync(factory, code: "01");

        var response = await client.PostAsJsonAsync(
            FaresUrl(route.Id),
            new { passengerType = "Vip", price = 7000m });
        var body = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("passengerType", ErrorFieldsOf(body));
        Assert.Equal(
            "Đối tượng hành khách không hợp lệ. Chấp nhận: Standard, Student, Senior, Child, Disabled",
            body.GetProperty("errors").GetProperty("passengerType")[0].GetString());
    }

    [Theory]
    [InlineData(0, "Giá vé phải lớn hơn 0")]
    [InlineData(-1000, "Giá vé phải lớn hơn 0")]
    [InlineData("10000000000", "Giá vé tối đa 9 999 999 999.99")]
    public async Task Post_gia_ve_ngoai_bien_tra_400_o_errors_price(object price, string message)
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var route = await SeedRouteAsync(factory, code: "01");

        var response = await client.PostAsJsonAsync(
            FaresUrl(route.Id),
            new { passengerType = "Standard", price = Convert.ToDecimal(price) });
        var body = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("price", ErrorFieldsOf(body));
        Assert.Equal(message, body.GetProperty("errors").GetProperty("price")[0].GetString());
    }

    [Fact]
    public async Task Post_bo_trong_gia_ve_thi_coi_nhu_0_va_bi_chan()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var route = await SeedRouteAsync(factory, code: "01");

        var response = await client.PostAsJsonAsync(FaresUrl(route.Id), new { passengerType = "Standard" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("price", ErrorFieldsOf(await ReadJsonAsync(response)));
    }

    [Fact]
    public async Task Post_trung_doi_tuong_da_co_gia_tra_409()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var route = await SeedRouteAsync(factory, code: "01");
        await SeedFareAsync(factory, route.Id, PassengerType.Student, 5000m);

        var response = await client.PostAsJsonAsync(
            FaresUrl(route.Id),
            new { passengerType = "Student", price = 6000m });

        // 409 chứ không phải 400: đây không phải dữ liệu sai định dạng mà là xung đột với dữ
        // liệu đang có. Muốn đổi giá thì PUT vào dòng đã có.
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("Tuyến này đã có giá vé cho đối tượng đó", await MessageAsync(response));
    }

    [Fact]
    public async Task Post_cung_doi_tuong_o_tuyen_khac_thi_khong_bi_chan()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var routeA = await SeedRouteAsync(factory, code: "01");
        var routeB = await SeedRouteAsync(factory, code: "02");
        await SeedFareAsync(factory, routeA.Id, PassengerType.Student, 5000m);

        // Ràng buộc unique là (RouteId, PassengerType) chứ không phải PassengerType đơn lẻ.
        var response = await client.PostAsJsonAsync(
            FaresUrl(routeB.Id),
            new { passengerType = "Student", price = 6000m });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    // ---------------------------------------------------------------------------------------
    // PUT /api/routes/{routeId}/fares/{id}
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Put_sua_gia_ve_chi_doi_gia_va_dat_updatedAt()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var route = await SeedRouteAsync(factory, code: "01");
        var fare = await SeedFareAsync(factory, route.Id, PassengerType.Student, 5000m);

        var body = await ReadJsonAsync(await client.PutAsJsonAsync(
            FareUrl(route.Id, fare.Id),
            new { price = 6500m }));

        Assert.Equal(6500m, body.GetProperty("price").GetDecimal());

        // Đối tượng giữ nguyên — UpdateFareRequest cố ý không có trường passengerType.
        Assert.Equal("Student", body.GetProperty("passengerType").GetString());
        Assert.Equal(JsonValueKind.String, body.GetProperty("updatedAt").ValueKind);
    }

    [Theory]
    [InlineData(0, "Giá vé phải lớn hơn 0")]
    [InlineData("10000000000", "Giá vé tối đa 9 999 999 999.99")]
    public async Task Put_gia_ve_ngoai_bien_tra_400_o_errors_price(object price, string message)
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var route = await SeedRouteAsync(factory, code: "01");
        var fare = await SeedFareAsync(factory, route.Id, PassengerType.Standard, 7000m);

        var response = await client.PutAsJsonAsync(
            FareUrl(route.Id, fare.Id),
            new { price = Convert.ToDecimal(price) });
        var body = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(message, body.GetProperty("errors").GetProperty("price")[0].GetString());

        // Giá cũ phải còn nguyên — sửa hỏng không được để lại dấu vết.
        var stored = await QueryAsync(factory, db => db.Fares.FirstAsync(f => f.Id == fare.Id));
        Assert.Equal(7000m, stored.Price);
    }

    [Fact]
    public async Task Put_dong_gia_cua_tuyen_khac_tra_404()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var routeA = await SeedRouteAsync(factory, code: "01");
        var routeB = await SeedRouteAsync(factory, code: "02");
        var fareOfA = await SeedFareAsync(factory, routeA.Id, PassengerType.Standard, 7000m);

        var response = await client.PutAsJsonAsync(FareUrl(routeB.Id, fareOfA.Id), new { price = 8000m });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var stored = await QueryAsync(factory, db => db.Fares.FirstAsync(f => f.Id == fareOfA.Id));
        Assert.Equal(7000m, stored.Price);
    }

    [Fact]
    public async Task Put_dong_gia_khong_ton_tai_tra_404()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var route = await SeedRouteAsync(factory, code: "01");

        var response = await client.PutAsJsonAsync(FareUrl(route.Id, Guid.NewGuid()), new { price = 8000m });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---------------------------------------------------------------------------------------
    // DELETE /api/routes/{routeId}/fares/{id}
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Delete_gia_ve_tra_204_va_xoa_han_khoi_csdl()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var route = await SeedRouteAsync(factory, code: "01");
        var fare = await SeedFareAsync(factory, route.Id, PassengerType.Student, 5000m);

        var response = await client.DeleteAsync(FareUrl(route.Id, fare.Id));

        // Xoá cứng: Fare không có cột trạng thái và không bảng nào tham chiếu tới nó.
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var stored = await QueryAsync(factory, db => db.Fares.FirstOrDefaultAsync(f => f.Id == fare.Id));
        Assert.Null(stored);
    }

    [Fact]
    public async Task Delete_gia_ve_roi_thi_tao_lai_duoc_cung_doi_tuong()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var route = await SeedRouteAsync(factory, code: "01");
        var fare = await SeedFareAsync(factory, route.Id, PassengerType.Student, 5000m);

        await client.DeleteAsync(FareUrl(route.Id, fare.Id));

        // Đây là cách đổi đối tượng của một dòng giá: xoá rồi tạo lại. Ca này chốt rằng
        // ràng buộc unique đã thật sự nhả sau khi xoá.
        var response = await client.PostAsJsonAsync(
            FaresUrl(route.Id),
            new { passengerType = "Student", price = 6500m });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Delete_dong_gia_cua_tuyen_khac_tra_404()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var routeA = await SeedRouteAsync(factory, code: "01");
        var routeB = await SeedRouteAsync(factory, code: "02");
        var fareOfA = await SeedFareAsync(factory, routeA.Id, PassengerType.Standard, 7000m);

        var response = await client.DeleteAsync(FareUrl(routeB.Id, fareOfA.Id));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        // Xoá nhầm tuyến là mất dữ liệu không lấy lại được — phải chắc bản ghi còn nguyên.
        var stored = await QueryAsync(factory, db => db.Fares.FirstOrDefaultAsync(f => f.Id == fareOfA.Id));
        Assert.NotNull(stored);
    }

    [Fact]
    public async Task Delete_dong_gia_khong_ton_tai_tra_404()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var route = await SeedRouteAsync(factory, code: "01");

        var response = await client.DeleteAsync(FareUrl(route.Id, Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---------------------------------------------------------------------------------------
    // Helper — mỗi lớp test tự chép, không có lớp base chung (đúng lệ đang có của dự án)
    // ---------------------------------------------------------------------------------------

    private const string RoutesUrl = "/api/routes";

    private const string StopsUrl = "/api/stops";

    private static string RouteUrl(Guid id) => $"{RoutesUrl}/{id}";

    private static string StopUrl(Guid id) => $"{StopsUrl}/{id}";

    private static string FaresUrl(Guid routeId) => $"{RoutesUrl}/{routeId}/fares";

    private static string FareUrl(Guid routeId, Guid fareId) => $"{FaresUrl(routeId)}/{fareId}";

    /// <summary>
    /// Gọi đủ 15 endpoint của ba nhóm với một client bất kỳ. Dùng chung cho ca 401 và ca 403:
    /// hai ca chỉ khác nhau ở client, nên danh sách endpoint phải là một nguồn duy nhất —
    /// thêm endpoint mới mà quên một trong hai ca là chuyện rất dễ xảy ra.
    /// </summary>
    private static async Task<HttpResponseMessage[]> SendAllAsync(HttpClient client, Guid routeId, Guid id)
    {
        var calls = new (HttpMethod Method, string Url, object? Body)[]
        {
            (HttpMethod.Get, RoutesUrl, null),
            (HttpMethod.Get, RouteUrl(id), null),
            (HttpMethod.Post, RoutesUrl, RouteBody(code: "09")),
            (HttpMethod.Put, RouteUrl(id), RouteUpdateBody(code: "09", name: "Tuyến Chín")),
            (HttpMethod.Delete, RouteUrl(id), null),

            (HttpMethod.Get, StopsUrl, null),
            (HttpMethod.Get, StopUrl(id), null),
            (HttpMethod.Post, StopsUrl, StopBody("Trạm Cầu Giấy")),
            (HttpMethod.Put, StopUrl(id), StopBody("Trạm Cầu Giấy")),
            (HttpMethod.Delete, StopUrl(id), null),

            (HttpMethod.Get, FaresUrl(routeId), null),
            (HttpMethod.Get, FareUrl(routeId, id), null),
            (HttpMethod.Post, FaresUrl(routeId), new { passengerType = "Standard", price = 7000m }),
            (HttpMethod.Put, FareUrl(routeId, id), new { price = 7000m }),
            (HttpMethod.Delete, FareUrl(routeId, id), null),
        };

        var responses = new List<HttpResponseMessage>(calls.Length);

        foreach (var (method, url, body) in calls)
        {
            responses.Add(await SendAsync(client, method, url, body));
        }

        return [.. responses];
    }

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string url, object? body)
    {
        var request = new HttpRequestMessage(method, url);

        if (body is not null)
        {
            // JsonContent.Create lấy kiểu thật lúc chạy, nên nhận object vẫn serialize đúng
            // hình dạng của anonymous type truyền vào.
            request.Content = JsonContent.Create(body);
        }

        return client.SendAsync(request);
    }

    private static object RouteBody(
        string code = "01",
        string name = "Bến Thành — Chợ Lớn",
        string origin = "Bến Thành",
        string destination = "Chợ Lớn",
        decimal distanceKm = 12.5m)
        => new { code, name, origin, destination, distanceKm };

    private static object RouteUpdateBody(
        string code = "01",
        string name = "Bến Thành — Chợ Lớn",
        string origin = "Bến Thành",
        string destination = "Chợ Lớn",
        decimal distanceKm = 12.5m,
        string? status = null)
        => new { code, name, origin, destination, distanceKm, status };

    /// <summary>Body thiếu đúng một trường bắt buộc, để kiểm tra thông báo của từng ô nhập.</summary>
    private static object RouteBodyWithout(string field)
    {
        var values = new Dictionary<string, object>
        {
            ["code"] = "01",
            ["name"] = "Bến Thành — Chợ Lớn",
            ["origin"] = "Bến Thành",
            ["destination"] = "Chợ Lớn",
        };

        values.Remove(field);

        return values;
    }

    private static object StopBody(
        string name,
        double latitude = 21.03,
        double longitude = 105.80)
        => new { name, address = $"Địa chỉ {name}", latitude, longitude };

    private static object StopBodyWithout(string field)
    {
        var values = new Dictionary<string, object>
        {
            ["name"] = "Trạm Cầu Giấy",
            ["address"] = "Địa chỉ Trạm Cầu Giấy",
            ["latitude"] = 21.03,
            ["longitude"] = 105.80,
        };

        values.Remove(field);

        return values;
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
        => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static JsonElement ItemsOf(JsonElement listBody) => listBody.GetProperty("items");

    private static string[] CodesOf(JsonElement array)
        => array.EnumerateArray()
            .Select(row => row.GetProperty("code").GetString() ?? string.Empty)
            .ToArray();

    private static string[] NamesOf(JsonElement array)
        => array.EnumerateArray()
            .Select(row => row.GetProperty("name").GetString() ?? string.Empty)
            .ToArray();

    private static string[] PassengerTypesOf(JsonElement array)
        => array.EnumerateArray()
            .Select(row => row.GetProperty("passengerType").GetString() ?? string.Empty)
            .ToArray();

    private static async Task<string> MessageAsync(HttpResponseMessage response)
        => (await ReadJsonAsync(response)).GetProperty("message").GetString() ?? string.Empty;

    private static string[] ErrorFieldsOf(JsonElement body)
        => body.GetProperty("errors")
            .EnumerateObject()
            .Select(property => property.Name)
            .ToArray();

    private static async Task<T> QueryAsync<T>(TestAppFactory factory, Func<AppDbContext, Task<T>> query)
    {
        using var scope = factory.Services.CreateScope();

        return await query(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private static async Task<HttpClient> SignInAsync(TestAppFactory factory, Guid roleId, string roleCode)
    {
        await EnsureAllRolesAsync(factory);
        var user = await SeedUserAsync(factory, roleId, roleCode);

        return ClientWith(factory, factory.CreateTokenFor(user));
    }

    private static async Task<HttpClient> SignInAsManagerAsync(TestAppFactory factory)
        => await SignInAsync(factory, RoleIds.Manager, RoleCodes.Manager);

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

    /// <summary>
    /// Dựng đúng nền dữ liệu mà migration tạo sẵn ở production: 4 vai trò chuẩn.
    /// Provider InMemory KHÔNG chạy <c>HasData</c>, nên không seed thì tài khoản trỏ tới vai trò
    /// không tồn tại và mọi request đều 403 dù token hợp lệ.
    /// </summary>
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

    /// <summary>Bốn vai trò migration seed sẵn — khớp <c>AppDbContext.UserRoles.cs</c>.</summary>
    private static readonly (Guid Id, string Code)[] CanonicalRoles =
    [
        (RoleIds.Admin, RoleCodes.Admin),
        (RoleIds.Manager, RoleCodes.Manager),
        (RoleIds.Driver, RoleCodes.Driver),
        (RoleIds.Passenger, RoleCodes.Passenger),
    ];

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
            // Vai trò chính phải luôn có mặt ở bảng nối — đúng bất biến mà AuthService giữ.
            UserRoles = [new UserRole { RoleId = roleId }],
        };

        await factory.SeedAsync(db => db.Users.Add(user));

        return user;
    }

    private static async Task<RouteEntity> SeedRouteAsync(
        TestAppFactory factory,
        string code = "01",
        string name = "Tuyến Một",
        string origin = "Bến Thành",
        string destination = "Chợ Lớn",
        decimal distanceKm = 12.5m,
        RouteStatus status = RouteStatus.Active,
        DateTime? createdAt = null)
    {
        var route = new RouteEntity
        {
            Id = Guid.NewGuid(),
            Code = code,
            Name = name,
            Origin = origin,
            Destination = destination,
            DistanceKm = distanceKm,
            Status = status,
            CreatedAt = createdAt ?? DateTime.UtcNow,
        };

        await factory.SeedAsync(db => db.Routes.Add(route));

        return route;
    }

    private static async Task<Stop> SeedStopAsync(
        TestAppFactory factory,
        string name,
        double latitude = 21.03,
        double longitude = 105.80)
    {
        var stop = new Stop
        {
            Id = Guid.NewGuid(),
            Name = name,
            Address = $"Địa chỉ {name}",
            Latitude = latitude,
            Longitude = longitude,
        };

        await factory.SeedAsync(db => db.Stops.Add(stop));

        return stop;
    }

    private static async Task<RouteStop> SeedRouteStopAsync(
        TestAppFactory factory,
        Guid routeId,
        Guid stopId,
        int stopOrder,
        decimal distanceKm = 0m)
    {
        // Chỉ gán khoá ngoại, KHÔNG gán navigation: Route/Stop được seed ở scope khác, gán
        // navigation vào đây sẽ khiến EF tưởng chúng là bản ghi mới và chèn trùng khoá chính.
        var routeStop = new RouteStop
        {
            Id = Guid.NewGuid(),
            RouteId = routeId,
            StopId = stopId,
            StopOrder = stopOrder,
            DistanceKm = distanceKm,
        };

        await factory.SeedAsync(db => db.RouteStops.Add(routeStop));

        return routeStop;
    }

    private static async Task<Fare> SeedFareAsync(
        TestAppFactory factory,
        Guid routeId,
        PassengerType passengerType,
        decimal price)
    {
        var fare = new Fare
        {
            Id = Guid.NewGuid(),
            RouteId = routeId,
            PassengerType = passengerType,
            Price = price,
        };

        await factory.SeedAsync(db => db.Fares.Add(fare));

        return fare;
    }
}
