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
/// Test tích hợp cho API lịch trình chạy xe theo tuyến — <c>/api/routes/{routeId}/trips</c>
/// (story 13 "Lập lịch trình", task "Test API lịch trình và job sinh chuyến tự động (xUnit)" —
/// Giàng A Vàng).
///
/// <para>
/// Phủ trọn sáu endpoint của <c>RouteTripsController</c>: danh sách, chi tiết, thêm chuyến lẻ,
/// sửa, huỷ, và <c>POST .../trips/generate</c> — API sinh chuyến hàng loạt theo tần suất mà
/// quy ước A8.3 chốt thay cho bảng mẫu <c>Schedules</c>. Phần "job sinh chuyến tự động" còn lại
/// là <c>BackgroundService</c> của Nguyễn Duy Kiên; hợp đồng ghi rõ nó <b>chưa làm</b>
/// (docs/api-contract.md, mục "Chuyến xe — /trips"), nên lớp test này không phủ — xem ghi chú
/// ở vùng "Sinh chuyến hàng loạt".
/// </para>
///
/// <para>
/// Dùng lại <see cref="TestAppFactory"/> của JwtAuthTests: chạy trên app thật, mỗi test một
/// CSDL InMemory riêng. <c>RouteTripsController</c> gắn <c>[Authorize(Policy = ManagerOrAbove)]</c>
/// ở cả lớp nên mọi ca đều đi qua đúng tầng phân quyền thật.
/// </para>
///
/// <para>
/// ⚠️ Mọi mốc thời gian ở đây dùng offset <c>+00:00</c> cho dễ đọc tương quan với cột
/// <c>timestamptz</c> UTC — trừ đúng một ca kiểm chứng việc quy đổi từ <c>+07:00</c> (giờ Việt Nam)
/// sang UTC, vì đó là múi giờ thật của người dùng.
/// </para>
/// </summary>
public class RouteTripsApiTests
{
    /// <summary>Ngày gốc 00:00 UTC — mọi mốc trong lớp tính từ đây để ca nào cũng cùng một ngày (UTC).</summary>
    private static readonly DateTime Day = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>Mốc UTC trong ngày test. Trả <see cref="DateTime"/> vì cột <c>Trips.DepartureTime</c> là DateTime.</summary>
    private static DateTime At(int hour, int minute = 0) => Day.AddHours(hour).AddMinutes(minute);

    /// <summary>Cùng mốc trên nhưng dạng gửi lên body/query — kèm offset để qua được ràng buộc hợp đồng.</summary>
    private static DateTimeOffset AtOffset(int hour, int minute = 0) => new(At(hour, minute), TimeSpan.Zero);

    // =======================================================================================
    // Phân quyền
    // =======================================================================================

    /// <summary>
    /// Không gửi token thì middleware JWT chặn trước khi tới controller — 401, không phải 403.
    /// Phân biệt đúng hai mã này quan trọng với frontend: 401 thì đăng nhập lại, 403 thì báo
    /// "không có quyền".
    /// </summary>
    [Fact]
    public async Task Khong_gui_token_thi_moi_endpoint_deu_tra_401()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();
        var routeId = Guid.NewGuid();

        var responses = new[]
        {
            await client.GetAsync(TripsUrl(routeId)),
            await client.GetAsync(TripUrl(routeId, Guid.NewGuid())),
            await client.PostAsJsonAsync(TripsUrl(routeId), new { }),
            await client.PutAsJsonAsync(TripUrl(routeId, Guid.NewGuid()), new { }),
            await client.DeleteAsync(TripUrl(routeId, Guid.NewGuid())),
            await client.PostAsJsonAsync(GenerateUrl(routeId), new { }),
        };

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode));
    }

    /// <summary>
    /// Story 13 là "Là quản lý, tôi muốn thiết lập thời gian biểu…" nên chỉ Admin và Quản lý dùng
    /// được. Tài xế và Hành khách nhận 403 dù tuyến có thật — chặn ở policy, không tới service.
    /// </summary>
    [Theory]
    [InlineData(RoleCodes.Driver)]
    [InlineData(RoleCodes.Passenger)]
    public async Task Vai_tro_duoi_quan_ly_thi_tra_403(string roleCode)
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsync(factory, RoleIdsFor(roleCode), roleCode);
        var route = await SeedRouteAsync(factory);

        var response = await client.GetAsync(TripsUrl(route.Id));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>Admin cũng nằm trong nhóm quản lý — đọc được lịch trình như Quản lý.</summary>
    [Fact]
    public async Task Admin_doc_duoc_lich_trinh()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsync(factory, RoleIds.Admin, RoleCodes.Admin);
        var route = await SeedRouteAsync(factory);

        var response = await client.GetAsync(TripsUrl(route.Id));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // =======================================================================================
    // GET /routes/{routeId}/trips — danh sách chuyến của tuyến
    // =======================================================================================

    /// <summary>
    /// Lịch trình đọc như một cuốn thời gian biểu: xếp theo giờ khởi hành TĂNG DẦN, khác mọi màn
    /// hình danh sách quản trị xếp theo <c>createdAt</c> giảm dần. Seed cố ý ngược thứ tự chèn để
    /// nếu service quên OrderBy thì test đỏ ngay.
    /// </summary>
    [Fact]
    public async Task Danh_sach_xep_theo_gio_khoi_hanh_tang_dan_du_luu_nguoc()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);
        var (route, bus) = await SeedRouteAndBusAsync(factory);

        await SeedTripAsync(factory, route.Id, bus.Id, At(15));
        await SeedTripAsync(factory, route.Id, bus.Id, At(5));
        await SeedTripAsync(factory, route.Id, bus.Id, At(10));

        var body = await ReadJsonAsync(await client.GetAsync(TripsUrl(route.Id)));

        Assert.Equal([At(5), At(10), At(15)], DepartureTimesOf(body.GetProperty("items")));
    }

    /// <summary>Lọc <c>from</c>/<c>to</c> tính luôn mốc — chuyến đúng bằng mốc vẫn nằm trong kết quả.</summary>
    [Fact]
    public async Task Loc_theo_khoang_gio_tinh_luon_hai_moc()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);
        var (route, bus) = await SeedRouteAndBusAsync(factory);

        await SeedTripAsync(factory, route.Id, bus.Id, At(4));
        await SeedTripAsync(factory, route.Id, bus.Id, At(5));
        await SeedTripAsync(factory, route.Id, bus.Id, At(6));
        await SeedTripAsync(factory, route.Id, bus.Id, At(7));

        var url = $"{TripsUrl(route.Id)}?from={Enc(AtOffset(5))}&to={Enc(AtOffset(6))}";
        var body = await ReadJsonAsync(await client.GetAsync(url));

        Assert.Equal([At(5), At(6)], DepartureTimesOf(body.GetProperty("items")));
        Assert.Equal(2, body.GetProperty("total").GetInt32());
    }

    /// <summary><c>to</c> sớm hơn <c>from</c> là tham số sai, không phải tập rỗng — 400 kèm <c>errors.to</c>.</summary>
    [Fact]
    public async Task To_som_hon_from_thi_tra_400_errors_to()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);
        var route = await SeedRouteAsync(factory);

        var url = $"{TripsUrl(route.Id)}?from={Enc(AtOffset(10))}&to={Enc(AtOffset(5))}";
        var response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await ReadJsonAsync(response)).GetProperty("errors").TryGetProperty("to", out _));
    }

    /// <summary>Lọc theo trạng thái chỉ trả đúng trạng thái đó.</summary>
    [Fact]
    public async Task Loc_theo_trang_thai()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);
        var (route, bus) = await SeedRouteAndBusAsync(factory);

        await SeedTripAsync(factory, route.Id, bus.Id, At(5), status: TripStatus.Scheduled);
        await SeedTripAsync(factory, route.Id, bus.Id, At(6), status: TripStatus.Cancelled);
        await SeedTripAsync(factory, route.Id, bus.Id, At(7), status: TripStatus.Completed);

        var body = await ReadJsonAsync(await client.GetAsync($"{TripsUrl(route.Id)}?status=Cancelled"));

        Assert.Equal([At(6)], DepartureTimesOf(body.GetProperty("items")));
        Assert.Equal("Cancelled", body.GetProperty("items")[0].GetProperty("status").GetString());
    }

    /// <summary>
    /// Mã trạng thái lạ trả danh sách RỖNG chứ không báo lỗi — cùng lối bộ lọc <c>status</c> của
    /// <c>/routes</c>: mã lạ có thể là trạng thái hợp lệ trong tương lai, không đáng chặn cả request.
    /// </summary>
    [Fact]
    public async Task Trang_thai_khong_khop_ma_nao_thi_tra_danh_sach_rong()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);
        var (route, bus) = await SeedRouteAndBusAsync(factory);
        await SeedTripAsync(factory, route.Id, bus.Id, At(5));

        var response = await client.GetAsync($"{TripsUrl(route.Id)}?status=KhongCoThat");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Empty(body.GetProperty("items").EnumerateArray());
        Assert.Equal(0, body.GetProperty("total").GetInt32());
    }

    /// <summary>
    /// <c>total</c> là tổng số dòng KHỚP BỘ LỌC, không phải số dòng của trang — AntD Table dùng nó
    /// để vẽ phân trang. Ba chuyến, mỗi trang hai dòng: trang 1 có 2 dòng nhưng total phải là 3.
    /// </summary>
    [Fact]
    public async Task Phan_trang_tra_total_la_tong_khop_loc_khong_phai_so_dong_trang()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);
        var (route, bus) = await SeedRouteAndBusAsync(factory);

        await SeedTripAsync(factory, route.Id, bus.Id, At(5));
        await SeedTripAsync(factory, route.Id, bus.Id, At(6));
        await SeedTripAsync(factory, route.Id, bus.Id, At(7));

        var body = await ReadJsonAsync(await client.GetAsync($"{TripsUrl(route.Id)}?page=1&pageSize=2"));

        Assert.Equal(2, body.GetProperty("items").GetArrayLength());
        Assert.Equal(3, body.GetProperty("total").GetInt32());
        Assert.Equal(1, body.GetProperty("page").GetInt32());
        Assert.Equal(2, body.GetProperty("pageSize").GetInt32());
    }

    [Theory]
    [InlineData("page=0")]
    [InlineData("pageSize=0")]
    [InlineData("pageSize=101")]
    public async Task Phan_trang_ngoai_khoang_thi_tra_400(string query)
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);
        var route = await SeedRouteAsync(factory);

        var response = await client.GetAsync($"{TripsUrl(route.Id)}?{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>Tuyến chưa có chuyến nào là mảng rỗng, KHÔNG phải 404 — tuyến vẫn tồn tại.</summary>
    [Fact]
    public async Task Tuyen_chua_co_chuyen_thi_tra_mang_rong_khong_404()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);
        var route = await SeedRouteAsync(factory);

        var response = await client.GetAsync(TripsUrl(route.Id));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Empty(body.GetProperty("items").EnumerateArray());
        Assert.Equal(0, body.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Tuyen_khong_ton_tai_thi_danh_sach_tra_404()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var response = await client.GetAsync(TripsUrl(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// Danh sách trả kèm biển số xe và tên tài xế để màn hình lập lịch trình / phân công không phải
    /// gọi thêm API cho từng dòng. Chuyến chưa phân công trả null ở CẢ HAI trường tài xế — đó là
    /// trạng thái hợp lệ của chuyến vừa sinh hàng loạt, không phải lỗi.
    /// </summary>
    [Fact]
    public async Task Danh_sach_kem_bien_so_va_tai_xe_chua_phan_cong_tra_null()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);
        var route = await SeedRouteAsync(factory);
        var bus = await SeedBusAsync(factory, licensePlate: "29B-123.45");
        await SeedTripAsync(factory, route.Id, bus.Id, At(5));

        var body = await ReadJsonAsync(await client.GetAsync(TripsUrl(route.Id)));
        var trip = body.GetProperty("items")[0];

        Assert.Equal("29B-123.45", trip.GetProperty("busLicensePlate").GetString());
        Assert.Equal(JsonValueKind.Null, trip.GetProperty("driverId").ValueKind);
        Assert.Equal(JsonValueKind.Null, trip.GetProperty("driverName").ValueKind);
    }

    // =======================================================================================
    // GET /routes/{routeId}/trips/{id} — chi tiết một chuyến
    // =======================================================================================

    [Fact]
    public async Task Chi_tiet_chuyen_tra_du_truong()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);
        var (route, bus) = await SeedRouteAndBusAsync(factory);
        var trip = await SeedTripAsync(factory, route.Id, bus.Id, At(5), arrivalTime: At(6, 30));

        var body = await ReadJsonAsync(await client.GetAsync(TripUrl(route.Id, trip.Id)));

        Assert.Equal(trip.Id, body.GetProperty("id").GetGuid());
        Assert.Equal(route.Id, body.GetProperty("routeId").GetGuid());
        Assert.Equal(bus.Id, body.GetProperty("busId").GetGuid());
        Assert.Equal(At(5), body.GetProperty("departureTime").GetDateTime());
        Assert.Equal(At(6, 30), body.GetProperty("arrivalTime").GetDateTime());
        Assert.Equal("Scheduled", body.GetProperty("status").GetString());
    }

    /// <summary>
    /// <c>routeId</c> phải khớp: chuyến của tuyến khác là 404 chứ không phải 200. Nếu trả 200 thì
    /// màn hình của tuyến A hiển thị được chuyến của tuyến B — rò dữ liệu giữa hai tuyến.
    /// </summary>
    [Fact]
    public async Task Chuyen_cua_tuyen_khac_thi_tra_404()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);
        var (routeA, bus) = await SeedRouteAndBusAsync(factory);
        var routeB = await SeedRouteAsync(factory, code: "02");
        var trip = await SeedTripAsync(factory, routeA.Id, bus.Id, At(5));

        var response = await client.GetAsync(TripUrl(routeB.Id, trip.Id));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Id_khong_phai_guid_thi_tra_404()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        // Ràng buộc {id:guid} trên route: chuỗi không phải GUID không khớp endpoint nào cả.
        var response = await client.GetAsync($"/api/routes/{Guid.NewGuid()}/trips/khong-phai-guid");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// Khoá hình dạng API của <c>Trip</c> theo đúng mục "Lịch trình chạy xe" của api-contract.md.
    /// Thêm/bớt một trường là ĐỔI HÌNH DẠNG API: theo ⛔5 phải sửa hợp đồng trước rồi báo người viết
    /// frontend. Ca này làm đổ test ngay lúc đó, để việc đó là quyết định có ý thức.
    /// </summary>
    [Fact]
    public async Task Response_chi_tra_dung_cac_truong_trong_hop_dong()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);
        var (route, bus) = await SeedRouteAndBusAsync(factory);
        var trip = await SeedTripAsync(factory, route.Id, bus.Id, At(5));

        var body = await ReadJsonAsync(await client.GetAsync(TripUrl(route.Id, trip.Id)));

        Assert.Equal(
            [
                "arrivalTime", "busId", "busLicensePlate", "createdAt", "currentLat", "currentLng",
                "currentStopId", "departureTime", "driverId", "driverName", "id",
                "positionUpdatedAt", "routeId", "status", "updatedAt",
            ],
            PropertyNamesOf(body));
    }

    // =======================================================================================
    // POST /routes/{routeId}/trips — thêm một chuyến lẻ
    // =======================================================================================

    [Fact]
    public async Task Them_chuyen_le_tra_201_va_luon_o_trang_thai_Scheduled()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);
        var (route, bus) = await SeedRouteAndBusAsync(factory);

        var response = await client.PostAsJsonAsync(TripsUrl(route.Id), new
        {
            busId = bus.Id,
            departureTime = AtOffset(5),
            arrivalTime = AtOffset(6),
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Equal("Scheduled", body.GetProperty("status").GetString());
        Assert.Equal(At(5), body.GetProperty("departureTime").GetDateTime());
        Assert.NotNull(response.Headers.Location);
    }

    /// <summary>
    /// Mọi mốc gửi lên kèm múi giờ và được quy về UTC khi lưu (quy ước A3: <c>timestamptz</c>).
    /// Gửi 05:00 giờ Việt Nam (+07:00) phải nằm lại là 22:00 UTC ngày hôm trước.
    /// </summary>
    [Fact]
    public async Task Gio_gui_kem_mui_gio_duoc_quy_ve_utc()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);
        var (route, bus) = await SeedRouteAndBusAsync(factory);

        var vietnamFiveAm = new DateTimeOffset(2026, 10, 1, 5, 0, 0, TimeSpan.FromHours(7));
        var response = await client.PostAsJsonAsync(TripsUrl(route.Id), new
        {
            busId = bus.Id,
            departureTime = vietnamFiveAm,
        });

        var body = await ReadJsonAsync(response);
        Assert.Equal(vietnamFiveAm.UtcDateTime, body.GetProperty("departureTime").GetDateTime());
    }

    [Fact]
    public async Task Gio_den_khong_sau_gio_khoi_hanh_thi_tra_400_errors_arrivalTime()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);
        var (route, bus) = await SeedRouteAndBusAsync(factory);

        var response = await client.PostAsJsonAsync(TripsUrl(route.Id), new
        {
            busId = bus.Id,
            departureTime = AtOffset(6),
            arrivalTime = AtOffset(5),
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await ReadJsonAsync(response)).GetProperty("errors").TryGetProperty("arrivalTime", out _));
    }

    [Fact]
    public async Task Thieu_xe_hoac_gio_khoi_hanh_thi_tra_400()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);
        var route = await SeedRouteAsync(factory);

        var response = await client.PostAsJsonAsync(TripsUrl(route.Id), new { departureTime = AtOffset(5) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await ReadJsonAsync(response)).GetProperty("errors").TryGetProperty("busId", out _));
    }

    [Fact]
    public async Task Tuyen_khong_ton_tai_thi_tao_chuyen_tra_404()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);
        var bus = await SeedBusAsync(factory);

        var response = await client.PostAsJsonAsync(TripsUrl(Guid.NewGuid()), new
        {
            busId = bus.Id,
            departureTime = AtOffset(5),
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Xe_khong_ton_tai_thi_tra_404()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);
        var route = await SeedRouteAsync(factory);

        var response = await client.PostAsJsonAsync(TripsUrl(route.Id), new
        {
            busId = Guid.NewGuid(),
            departureTime = AtOffset(5),
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>Xe đang bảo dưỡng không được gán vào chuyến — 409, khác 404 của xe không tồn tại.</summary>
    [Fact]
    public async Task Xe_khong_o_trang_thai_khai_thac_thi_tra_409()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);
        var route = await SeedRouteAsync(factory);
        var bus = await SeedBusAsync(factory, status: BusStatus.Maintenance);

        var response = await client.PostAsJsonAsync(TripsUrl(route.Id), new
        {
            busId = bus.Id,
            departureTime = AtOffset(5),
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    // ── Kiểm tra "trùng khung giờ" khi tạo lịch trình ────────────────────────────────────────

    /// <summary>Trùng đúng giờ khởi hành với chuyến cùng tuyến → 409.</summary>
    [Fact]
    public async Task Trung_gio_khoi_hanh_cung_tuyen_thi_tra_409()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);
        var (route, bus) = await SeedRouteAndBusAsync(factory);
        await SeedTripAsync(factory, route.Id, bus.Id, At(5), arrivalTime: At(6));

        var response = await client.PostAsJsonAsync(TripsUrl(route.Id), new
        {
            busId = bus.Id,
            departureTime = AtOffset(5, 30),
            arrivalTime = AtOffset(6, 30),
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    /// <summary>
    /// Một xe không thể chạy hai chuyến cùng lúc — kể cả hai chuyến thuộc HAI TUYẾN KHÁC NHAU.
    /// Đây là nửa dễ quên của kiểm tra: chỉ so theo tuyến là lọt.
    /// </summary>
    [Fact]
    public async Task Trung_khung_gio_cung_xe_khac_tuyen_thi_tra_409()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);
        var routeA = await SeedRouteAsync(factory, code: "01");
        var routeB = await SeedRouteAsync(factory, code: "02");
        var bus = await SeedBusAsync(factory);
        await SeedTripAsync(factory, routeA.Id, bus.Id, At(5), arrivalTime: At(10));

        var response = await client.PostAsJsonAsync(TripsUrl(routeB.Id), new
        {
            busId = bus.Id,
            departureTime = AtOffset(6),
            arrivalTime = AtOffset(7),
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    /// <summary>
    /// Chuyến nối đuôi — chuyến mới khởi hành ĐÚNG lúc chuyến cũ tới bến — KHÔNG tính là trùng.
    /// Nếu tính là trùng thì không lịch trình nào nối chuyến được, và mọi lịch trình dày đặc đều vỡ.
    /// </summary>
    [Fact]
    public async Task Chuyen_noi_duoi_khong_tinh_la_trung()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);
        var (route, bus) = await SeedRouteAndBusAsync(factory);
        await SeedTripAsync(factory, route.Id, bus.Id, At(5), arrivalTime: At(6));

        var response = await client.PostAsJsonAsync(TripsUrl(route.Id), new
        {
            busId = bus.Id,
            departureTime = AtOffset(6),
            arrivalTime = AtOffset(7),
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    /// <summary>Chuyến đã huỷ / đã chạy xong không còn chiếm chỗ trên thời gian biểu nên không chặn.</summary>
    [Theory]
    [InlineData(TripStatus.Cancelled)]
    [InlineData(TripStatus.Completed)]
    public async Task Chuyen_da_huy_hoac_da_chay_xong_khong_chan(TripStatus status)
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);
        var (route, bus) = await SeedRouteAndBusAsync(factory);
        await SeedTripAsync(factory, route.Id, bus.Id, At(5), arrivalTime: At(6), status: status);

        var response = await client.PostAsJsonAsync(TripsUrl(route.Id), new
        {
            busId = bus.Id,
            departureTime = AtOffset(5),
            arrivalTime = AtOffset(6),
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    /// <summary>
    /// Trần sức chứa tuyến: 200 chuyến đang hoạt động mỗi ngày (UTC). Chuyến thứ 201 bị chặn với
    /// 400 <c>errors.departureTime</c> — đây là giới hạn tham số, khác 409 của trùng khung giờ.
    /// </summary>
    [Fact]
    public async Task Vuot_tran_200_chuyen_mot_ngay_thi_tra_400_errors_departureTime()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);
        var (route, bus) = await SeedRouteAndBusAsync(factory);
        await SeedManyTripsAsync(factory, route.Id, bus.Id, count: 200, stepMinutes: 1);

        var response = await client.PostAsJsonAsync(TripsUrl(route.Id), new
        {
            busId = bus.Id,
            departureTime = AtOffset(10),
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await ReadJsonAsync(response)).GetProperty("errors").TryGetProperty("departureTime", out _));
    }

    // =======================================================================================
    // PUT /routes/{routeId}/trips/{id} — sửa chuyến
    // =======================================================================================

    [Fact]
    public async Task Sua_gio_chay_thanh_cong_va_cap_nhat_updatedAt()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);
        var (route, bus) = await SeedRouteAndBusAsync(factory);
        var trip = await SeedTripAsync(factory, route.Id, bus.Id, At(5));

        var response = await client.PutAsJsonAsync(TripUrl(route.Id, trip.Id), new
        {
            busId = bus.Id,
            departureTime = AtOffset(6),
            arrivalTime = AtOffset(7),
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Equal(At(6), body.GetProperty("departureTime").GetDateTime());
        Assert.Equal(At(7), body.GetProperty("arrivalTime").GetDateTime());
        Assert.NotEqual(JsonValueKind.Null, body.GetProperty("updatedAt").ValueKind);
    }

    /// <summary>PUT là sửa toàn phần: bỏ trống <c>arrivalTime</c> là BỎ HẲN (gán null), không giữ nguyên.</summary>
    [Fact]
    public async Task Bo_trong_gio_den_thi_gan_null_chu_khong_giu_nguyen()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);
        var (route, bus) = await SeedRouteAndBusAsync(factory);
        var trip = await SeedTripAsync(factory, route.Id, bus.Id, At(5), arrivalTime: At(6));

        var response = await client.PutAsJsonAsync(TripUrl(route.Id, trip.Id), new
        {
            busId = bus.Id,
            departureTime = AtOffset(5),
        });

        var body = await ReadJsonAsync(response);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("arrivalTime").ValueKind);
    }

    [Fact]
    public async Task Trang_thai_ngoai_bon_ma_thi_tra_400_errors_status()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);
        var (route, bus) = await SeedRouteAndBusAsync(factory);
        var trip = await SeedTripAsync(factory, route.Id, bus.Id, At(5));

        var response = await client.PutAsJsonAsync(TripUrl(route.Id, trip.Id), new
        {
            busId = bus.Id,
            departureTime = AtOffset(5),
            status = "DangChay",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await ReadJsonAsync(response)).GetProperty("errors").TryGetProperty("status", out _));
    }

    /// <summary>Bỏ trống <c>status</c> thì GIỮ NGUYÊN trạng thái hiện tại — chỉ đổi giờ.</summary>
    [Fact]
    public async Task Bo_trong_trang_thai_thi_giu_nguyen()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);
        var (route, bus) = await SeedRouteAndBusAsync(factory);
        var trip = await SeedTripAsync(factory, route.Id, bus.Id, At(5), status: TripStatus.Running);

        var response = await client.PutAsJsonAsync(TripUrl(route.Id, trip.Id), new
        {
            busId = bus.Id,
            departureTime = AtOffset(6),
        });

        var body = await ReadJsonAsync(response);
        Assert.Equal("Running", body.GetProperty("status").GetString());
    }

    /// <summary>Mở lại chuyến đã huỷ: PUT với <c>status: "Scheduled"</c>.</summary>
    [Fact]
    public async Task Mo_lai_chuyen_da_huy_bang_status_Scheduled()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);
        var (route, bus) = await SeedRouteAndBusAsync(factory);
        var trip = await SeedTripAsync(factory, route.Id, bus.Id, At(5), status: TripStatus.Cancelled);

        var response = await client.PutAsJsonAsync(TripUrl(route.Id, trip.Id), new
        {
            busId = bus.Id,
            departureTime = AtOffset(5),
            status = "Scheduled",
        });

        var body = await ReadJsonAsync(response);
        Assert.Equal("Scheduled", body.GetProperty("status").GetString());
    }

    /// <summary>Đổi sang xe đang bảo dưỡng → 409.</summary>
    [Fact]
    public async Task Doi_sang_xe_khong_khai_thac_thi_tra_409()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);
        var (route, bus) = await SeedRouteAndBusAsync(factory);
        var broken = await SeedBusAsync(factory, licensePlate: "29B-999.99", status: BusStatus.Maintenance);
        var trip = await SeedTripAsync(factory, route.Id, bus.Id, At(5));

        var response = await client.PutAsJsonAsync(TripUrl(route.Id, trip.Id), new
        {
            busId = broken.Id,
            departureTime = AtOffset(5),
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    /// <summary>
    /// GIỮ NGUYÊN xe thì không kiểm tra lại trạng thái xe: xe chuyển sang bảo dưỡng SAU khi sinh
    /// chuyến không được chặn việc sửa giờ của chuyến đã tồn tại — nếu chặn thì một xe hỏng làm
    /// đóng băng cả lịch trình đã lập.
    /// </summary>
    [Fact]
    public async Task Giu_nguyen_xe_dang_bao_duong_van_sua_duoc_gio()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);
        var (route, bus) = await SeedRouteAndBusAsync(factory);
        var trip = await SeedTripAsync(factory, route.Id, bus.Id, At(5));

        await SetBusStatusAsync(factory, bus.Id, BusStatus.Maintenance);

        var response = await client.PutAsJsonAsync(TripUrl(route.Id, trip.Id), new
        {
            busId = bus.Id,
            departureTime = AtOffset(6),
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Sua_chuyen_cua_tuyen_khac_thi_tra_404()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);
        var (routeA, bus) = await SeedRouteAndBusAsync(factory);
        var routeB = await SeedRouteAsync(factory, code: "02");
        var trip = await SeedTripAsync(factory, routeA.Id, bus.Id, At(5));

        var response = await client.PutAsJsonAsync(TripUrl(routeB.Id, trip.Id), new
        {
            busId = bus.Id,
            departureTime = AtOffset(5),
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// PUT CỐ Ý không kiểm tra trùng khung giờ lẫn trần ngày (hợp đồng ghi rõ: task chỉ phủ "khi
    /// tạo lịch trình"). Ca này chốt hành vi đó để nếu sau này nhóm bổ sung kiểm tra thì test đỏ
    /// và việc đổi hành vi là một quyết định có ý thức, không phải một dòng code lỡ tay.
    /// </summary>
    [Fact]
    public async Task Sua_gio_de_trung_khung_gio_van_thanh_cong()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);
        var (route, bus) = await SeedRouteAndBusAsync(factory);
        await SeedTripAsync(factory, route.Id, bus.Id, At(5), arrivalTime: At(6));
        var second = await SeedTripAsync(factory, route.Id, bus.Id, At(20), arrivalTime: At(21));

        var response = await client.PutAsJsonAsync(TripUrl(route.Id, second.Id), new
        {
            busId = bus.Id,
            departureTime = AtOffset(5, 30),
            arrivalTime = AtOffset(6, 30),
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // =======================================================================================
    // DELETE /routes/{routeId}/trips/{id} — huỷ chuyến (xoá mềm)
    // =======================================================================================

    /// <summary>
    /// Huỷ chuyến = chuyển về <c>Cancelled</c>, KHÔNG xoá dòng khỏi CSDL — vé đã bán vẫn tham chiếu
    /// tới, và quy ước A4 cấm thêm cột <c>IsDeleted</c>.
    /// </summary>
    [Fact]
    public async Task Huy_chuyen_chuyen_ve_Cancelled_va_giu_ban_ghi()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);
        var (route, bus) = await SeedRouteAndBusAsync(factory);
        var trip = await SeedTripAsync(factory, route.Id, bus.Id, At(5));

        var response = await client.DeleteAsync(TripUrl(route.Id, trip.Id));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Cancelled", (await ReadJsonAsync(response)).GetProperty("status").GetString());
        Assert.Equal(1, await CountTripsAsync(factory, route.Id));
    }

    /// <summary>Huỷ lại chuyến đã huỷ là idempotent — 200, không báo lỗi.</summary>
    [Fact]
    public async Task Huy_lai_chuyen_da_huy_tra_200()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);
        var (route, bus) = await SeedRouteAndBusAsync(factory);
        var trip = await SeedTripAsync(factory, route.Id, bus.Id, At(5));

        await client.DeleteAsync(TripUrl(route.Id, trip.Id));
        var second = await client.DeleteAsync(TripUrl(route.Id, trip.Id));

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
    }

    /// <summary>Huỷ chuyến đã chạy xong là viết lại lịch sử — chặn hẳn bằng 409.</summary>
    [Fact]
    public async Task Huy_chuyen_da_Completed_thi_tra_409()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);
        var (route, bus) = await SeedRouteAndBusAsync(factory);
        var trip = await SeedTripAsync(factory, route.Id, bus.Id, At(5), status: TripStatus.Completed);

        var response = await client.DeleteAsync(TripUrl(route.Id, trip.Id));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Huy_chuyen_cua_tuyen_khac_thi_tra_404()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);
        var (routeA, bus) = await SeedRouteAndBusAsync(factory);
        var routeB = await SeedRouteAsync(factory, code: "02");
        var trip = await SeedTripAsync(factory, routeA.Id, bus.Id, At(5));

        var response = await client.DeleteAsync(TripUrl(routeB.Id, trip.Id));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // =======================================================================================
    // POST /routes/{routeId}/trips/generate — sinh chuyến hàng loạt (quy ước A8.3)
    //
    // ⚠️ Đây là API "lập lịch trình" mà quy ước A8.3 chốt thay cho bảng mẫu Schedules: quản lý
    //    chọn tuyến + xe + mốc bắt đầu + mốc kết thúc + tần suất → hệ thống sinh N dòng Trips.
    //    Task 112 của story 13 còn nửa "job sinh chuyến tự động" là BackgroundService của Nguyễn
    //    Duy Kiên — hợp đồng ghi rõ CHƯA LÀM và trong repo không có lớp nào như vậy, nên chưa có
    //    gì để test ở đây. Khi Kiên merge xong, nửa đó cần một lớp test riêng.
    // =======================================================================================

    /// <summary>
    /// Ví dụ trong hợp đồng: 05:00 → 22:00, tần suất 15 phút sinh đúng 69 chuyến
    /// (05:00, 05:15, …, 22:00) — chuyến đầu đúng mốc bắt đầu, chuyến cuối không vượt mốc kết thúc.
    /// </summary>
    [Fact]
    public async Task Generate_sinh_dung_so_chuyen_cach_deu_tan_suat()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);
        var (route, bus) = await SeedRouteAndBusAsync(factory);

        var response = await client.PostAsJsonAsync(GenerateUrl(route.Id), new
        {
            busId = bus.Id,
            startTime = AtOffset(5),
            endTime = AtOffset(22),
            frequencyMinutes = 15,
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Equal(69, body.GetProperty("total").GetInt32());
        Assert.Equal(69, body.GetProperty("items").GetArrayLength());

        var departures = DepartureTimesOf(body.GetProperty("items"));
        Assert.Equal(At(5), departures[0]);
        Assert.Equal(At(5, 15), departures[1]);
        Assert.Equal(At(22), departures[^1]);
        // Cách đều: mọi bước đúng bằng tần suất.
        Assert.All(
            departures.Zip(departures.Skip(1)),
            pair => Assert.Equal(15d, (pair.Second - pair.First).TotalMinutes));
    }

    /// <summary>
    /// Tần suất không chia hết khoảng thời gian: chuyến cuối là chuyến xa nhất CÒN nằm trong mốc
    /// kết thúc, không phải chuyến vượt mốc. 05:00 → 06:10 bước 30 phút: 05:00, 05:30, 06:00.
    /// </summary>
    [Fact]
    public async Task Generate_tan_suat_khong_chia_het_thi_chuyen_cuoi_khong_vuot_moc_ket_thuc()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);
        var (route, bus) = await SeedRouteAndBusAsync(factory);

        var response = await client.PostAsJsonAsync(GenerateUrl(route.Id), new
        {
            busId = bus.Id,
            startTime = AtOffset(5),
            endTime = AtOffset(6, 10),
            frequencyMinutes = 30,
        });

        var body = await ReadJsonAsync(response);
        Assert.Equal([At(5), At(5, 30), At(6)], DepartureTimesOf(body.GetProperty("items")));
    }

    /// <summary>Chuyến sinh hàng loạt ra đời ở trạng thái Scheduled, chưa phân công tài xế.</summary>
    [Fact]
    public async Task Chuyen_sinh_hang_loat_o_trang_thai_Scheduled_va_chua_co_tai_xe()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);
        var (route, bus) = await SeedRouteAndBusAsync(factory, licensePlate: "29B-123.45");

        var response = await client.PostAsJsonAsync(GenerateUrl(route.Id), new
        {
            busId = bus.Id,
            startTime = AtOffset(5),
            endTime = AtOffset(6),
            frequencyMinutes = 30,
        });

        var body = await ReadJsonAsync(response);
        var first = body.GetProperty("items")[0];

        Assert.Equal("Scheduled", first.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, first.GetProperty("driverId").ValueKind);
        Assert.Equal("29B-123.45", first.GetProperty("busLicensePlate").GetString());
    }

    /// <summary>Mốc bắt đầu kèm múi giờ Việt Nam cũng được quy về UTC như mọi trường thời gian khác.</summary>
    [Fact]
    public async Task Generate_quy_doi_moc_bat_dau_ve_utc()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);
        var (route, bus) = await SeedRouteAndBusAsync(factory);

        var start = new DateTimeOffset(2026, 10, 1, 5, 0, 0, TimeSpan.FromHours(7));
        var response = await client.PostAsJsonAsync(GenerateUrl(route.Id), new
        {
            busId = bus.Id,
            startTime = start,
            endTime = start.AddHours(1),
            frequencyMinutes = 30,
        });

        var body = await ReadJsonAsync(response);
        Assert.Equal(start.UtcDateTime, DepartureTimesOf(body.GetProperty("items"))[0]);
    }

    [Fact]
    public async Task Generate_moc_ket_thuc_khong_sau_moc_bat_dau_thi_tra_400_errors_endTime()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);
        var (route, bus) = await SeedRouteAndBusAsync(factory);

        var response = await client.PostAsJsonAsync(GenerateUrl(route.Id), new
        {
            busId = bus.Id,
            startTime = AtOffset(10),
            endTime = AtOffset(9),
            frequencyMinutes = 15,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await ReadJsonAsync(response)).GetProperty("errors").TryGetProperty("endTime", out _));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1441)]
    public async Task Generate_tan_suat_ngoai_khoang_thi_tra_400_errors_frequencyMinutes(int frequency)
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);
        var (route, bus) = await SeedRouteAndBusAsync(factory);

        var response = await client.PostAsJsonAsync(GenerateUrl(route.Id), new
        {
            busId = bus.Id,
            startTime = AtOffset(5),
            endTime = AtOffset(6),
            frequencyMinutes = frequency,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await ReadJsonAsync(response)).GetProperty("errors").TryGetProperty("frequencyMinutes", out _));
    }

    /// <summary>Thiếu hẳn trường bắt buộc — validate của DTO chặn trước khi vào service.</summary>
    [Fact]
    public async Task Generate_thieu_moc_bat_dau_thi_tra_400_errors_startTime()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);
        var (route, bus) = await SeedRouteAndBusAsync(factory);

        var response = await client.PostAsJsonAsync(GenerateUrl(route.Id), new
        {
            busId = bus.Id,
            endTime = AtOffset(6),
            frequencyMinutes = 15,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await ReadJsonAsync(response)).GetProperty("errors").TryGetProperty("startTime", out _));
    }

    /// <summary>
    /// Trần 500 chuyến mỗi lần gọi: tần suất 1 phút cho cả ngày là 1440 chuyến → 400 kèm gợi ý
    /// thu hẹp khoảng hoặc tăng tần suất. Trần này chặn một khoảng gõ nhầm sinh hàng vạn dòng Trips.
    /// </summary>
    [Fact]
    public async Task Generate_vuot_500_chuyen_mot_lan_thi_tra_400_errors_endTime()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);
        var (route, bus) = await SeedRouteAndBusAsync(factory);

        var response = await client.PostAsJsonAsync(GenerateUrl(route.Id), new
        {
            busId = bus.Id,
            startTime = AtOffset(0),
            endTime = AtOffset(23, 59),
            frequencyMinutes = 1,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await ReadJsonAsync(response)).GetProperty("errors").TryGetProperty("endTime", out _));
    }

    /// <summary>
    /// Sinh chuyến là NGUYÊN TỬ: một chuyến trong dải bị trùng khung giờ thì không chuyến nào được
    /// tạo — không bao giờ để lại một lịch trình dở dang.
    /// </summary>
    [Fact]
    public async Task Generate_trung_khung_gio_thi_tra_409_va_khong_tao_chuyen_nao()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);
        var (route, bus) = await SeedRouteAndBusAsync(factory);
        // Chuyến cũ chiếm 05:30 → 06:00, nằm giữa dải sắp sinh.
        await SeedTripAsync(factory, route.Id, bus.Id, At(5, 30), arrivalTime: At(6));

        var response = await client.PostAsJsonAsync(GenerateUrl(route.Id), new
        {
            busId = bus.Id,
            startTime = AtOffset(5),
            endTime = AtOffset(6),
            frequencyMinutes = 15,
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        // Nguyên tử: CSDL vẫn đúng một chuyến — chuyến cũ, không thêm dòng nào.
        Assert.Equal(1, await CountTripsAsync(factory, route.Id));
    }

    /// <summary>
    /// Trần sức chứa tuyến áp cho TỪNG ngày mà lịch trình chạm tới: 190 chuyến đã có + 20 sắp sinh
    /// = 210 > 200 nên cả lần gọi bị chặn trước khi ghi bất kỳ dòng nào.
    /// </summary>
    [Fact]
    public async Task Generate_vuot_tran_ngay_thi_tra_400_errors_endTime()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);
        var (route, bus) = await SeedRouteAndBusAsync(factory);
        await SeedManyTripsAsync(factory, route.Id, bus.Id, count: 190, stepMinutes: 5);

        var response = await client.PostAsJsonAsync(GenerateUrl(route.Id), new
        {
            busId = bus.Id,
            startTime = AtOffset(5),
            endTime = AtOffset(9, 45),
            frequencyMinutes = 15,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await ReadJsonAsync(response)).GetProperty("errors").TryGetProperty("endTime", out _));
        Assert.Equal(190, await CountTripsAsync(factory, route.Id));
    }

    [Fact]
    public async Task Generate_tuyen_khong_ton_tai_thi_tra_404()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);
        var bus = await SeedBusAsync(factory);

        var response = await client.PostAsJsonAsync(GenerateUrl(Guid.NewGuid()), new
        {
            busId = bus.Id,
            startTime = AtOffset(5),
            endTime = AtOffset(6),
            frequencyMinutes = 15,
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Generate_xe_khong_khai_thac_thi_tra_409()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);
        var route = await SeedRouteAsync(factory);
        var bus = await SeedBusAsync(factory, status: BusStatus.Maintenance);

        var response = await client.PostAsJsonAsync(GenerateUrl(route.Id), new
        {
            busId = bus.Id,
            startTime = AtOffset(5),
            endTime = AtOffset(6),
            frequencyMinutes = 15,
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    // =======================================================================================
    // Helper — mỗi lớp test tự chép, không có lớp base chung (đúng lệ đang có của dự án)
    // =======================================================================================

    private static string TripsUrl(Guid routeId) => $"/api/routes/{routeId}/trips";

    private static string TripUrl(Guid routeId, Guid tripId) => $"/api/routes/{routeId}/trips/{tripId}";

    private static string GenerateUrl(Guid routeId) => $"/api/routes/{routeId}/trips/generate";

    /// <summary>Mã hoá giá trị cho query string — dấu <c>+</c> của offset phải thành <c>%2B</c>.</summary>
    private static string Enc(DateTimeOffset value) => Uri.EscapeDataString(value.ToString("O"));

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
        => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static string[] PropertyNamesOf(JsonElement element)
        => element.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray();

    private static DateTime[] DepartureTimesOf(JsonElement listElement)
        => listElement.EnumerateArray().Select(trip => trip.GetProperty("departureTime").GetDateTime()).ToArray();

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

    private static async Task<RouteEntity> SeedRouteAsync(TestAppFactory factory, string code = "01")
    {
        var route = new RouteEntity
        {
            Id = Guid.NewGuid(),
            Code = code,
            Name = "Bến xe Mỹ Đình — Bến xe Gia Lâm",
            Origin = "Bến xe Mỹ Đình",
            Destination = "Bến xe Gia Lâm",
        };

        await factory.SeedAsync(db => db.Routes.Add(route));

        return route;
    }

    private static async Task<Bus> SeedBusAsync(
        TestAppFactory factory,
        string licensePlate = "29B-123.45",
        BusStatus status = BusStatus.Active)
    {
        var bus = new Bus
        {
            Id = Guid.NewGuid(),
            LicensePlate = licensePlate,
            BusType = "Hyundai County 29 chỗ",
            Capacity = 29,
            Status = status,
        };

        await factory.SeedAsync(db => db.Buses.Add(bus));

        return bus;
    }

    /// <summary>Cặp tối thiểu để một chuyến xem được: tuyến có thật và xe có thật.</summary>
    private static async Task<(RouteEntity Route, Bus Bus)> SeedRouteAndBusAsync(
        TestAppFactory factory,
        string licensePlate = "29B-123.45")
    {
        var route = await SeedRouteAsync(factory);
        var bus = await SeedBusAsync(factory, licensePlate);

        return (route, bus);
    }

    private static async Task<Trip> SeedTripAsync(
        TestAppFactory factory,
        Guid routeId,
        Guid busId,
        DateTime departureTime,
        DateTime? arrivalTime = null,
        TripStatus status = TripStatus.Scheduled)
    {
        // Chỉ gán khoá ngoại, KHÔNG gán navigation: Route/Bus được seed ở scope khác, gán navigation
        // vào đây sẽ khiến EF tưởng chúng là bản ghi mới và chèn trùng khoá chính.
        var trip = new Trip
        {
            Id = Guid.NewGuid(),
            RouteId = routeId,
            BusId = busId,
            DepartureTime = departureTime,
            ArrivalTime = arrivalTime,
            Status = status,
        };

        await factory.SeedAsync(db => db.Trips.Add(trip));

        return trip;
    }

    /// <summary>Seed <paramref name="count"/> chuyến cách nhau <paramref name="stepMinutes"/> phút, bắt đầu từ 00:00 UTC.</summary>
    private static async Task SeedManyTripsAsync(
        TestAppFactory factory,
        Guid routeId,
        Guid busId,
        int count,
        int stepMinutes)
    {
        await factory.SeedAsync(db => db.Trips.AddRange(Enumerable.Range(0, count).Select(index => new Trip
        {
            Id = Guid.NewGuid(),
            RouteId = routeId,
            BusId = busId,
            DepartureTime = Day.AddMinutes(index * stepMinutes),
            Status = TripStatus.Scheduled,
        })));
    }

    /// <summary>Đổi trạng thái xe sau khi đã seed — dựng ca "xe chuyển sang bảo dưỡng sau khi sinh chuyến".</summary>
    private static async Task SetBusStatusAsync(TestAppFactory factory, Guid busId, BusStatus status)
        => await factory.SeedAsync(db => db.Buses.First(bus => bus.Id == busId).Status = status);

    private static async Task<int> CountTripsAsync(TestAppFactory factory, Guid routeId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await db.Trips.CountAsync(trip => trip.RouteId == routeId);
    }
}
