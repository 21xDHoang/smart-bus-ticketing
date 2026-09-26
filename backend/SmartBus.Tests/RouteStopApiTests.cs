using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using SmartBus.Api.Entities;
using SmartBus.Api.Services;
using RouteEntity = SmartBus.Api.Entities.Route;
using UserEntity = SmartBus.Api.Entities.User;

namespace SmartBus.Tests;

/// <summary>
/// Test tích hợp cho API gán trạm vào tuyến và sắp xếp lại thứ tự trạm —
/// <c>/routes/{routeId}/stops</c> (task story 12 — Nguyễn Duy Kiên).
///
/// Dùng lại <see cref="TestAppFactory"/> của JwtAuthTests: chạy trên app thật, mỗi test một
/// CSDL InMemory riêng.
///
/// ⚠️ Provider InMemory KHÔNG dựng unique index <c>(RouteId, StopId)</c> lẫn khoá ngoại Restrict
/// của PostgreSQL. Nên các ca "gán trùng trạm" ở đây kiểm chứng lớp kiểm tra trong
/// <c>RouteStopService</c> — chính là lớp giữ cho thông báo lỗi đúng ở production; còn ràng buộc
/// dưới CSDL (lớp chặn cuối khi hai request chạy song song) thì chỉ chạy được với PostgreSQL thật.
/// </summary>
public class RouteStopApiTests
{
    // ---------------------------------------------------------------------------------------
    // Phân quyền
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Khong_gui_token_thi_tra_401()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        var route = await SeedRouteAsync(factory);

        var response = await client.GetAsync(StopsUrl(route.Id));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(RoleCodes.Driver)]
    [InlineData(RoleCodes.Passenger)]
    public async Task Vai_tro_khac_quan_ly_goi_ca_bon_endpoint_thi_tra_403(string roleCode)
    {
        using var factory = new TestAppFactory();

        var client = await SignInAsync(factory, RoleIdsFor(roleCode), roleCode);
        var route = await SeedRouteAsync(factory);
        var stop = await SeedStopAsync(factory, "Trạm Cầu Giấy");

        // Cả 4 endpoint đều gác bằng ManagerOrAbove, nên phải kiểm tra từng cái — sót một
        // attribute ở một action là một lỗ hổng phân quyền, không phải một thiếu sót nhỏ.
        var responses = new[]
        {
            await client.GetAsync(StopsUrl(route.Id)),
            await client.PostAsJsonAsync(StopsUrl(route.Id), new { stopId = stop.Id }),
            await client.PutAsJsonAsync(OrderUrl(route.Id), ReorderBody([stop.Id])),
            await client.DeleteAsync(UrlOf(route.Id, Guid.NewGuid())),
        };

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode));
    }

    // ---------------------------------------------------------------------------------------
    // GET /routes/{routeId}/stops
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Get_danh_sach_tram_tra_ve_theo_dung_thu_tu()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var route = await SeedRouteAsync(factory);
        var a = await SeedStopAsync(factory, "Trạm A");
        var b = await SeedStopAsync(factory, "Trạm B");
        var c = await SeedStopAsync(factory, "Trạm C");

        // Cố ý seed lệch cả thứ tự chạy lẫn thứ tự tạo, để bài test bắt được lỗi quên OrderBy.
        await SeedRouteStopAsync(factory, route.Id, c.Id, stopOrder: 1);
        await SeedRouteStopAsync(factory, route.Id, a.Id, stopOrder: 2);
        await SeedRouteStopAsync(factory, route.Id, b.Id, stopOrder: 3);

        var response = await client.GetAsync(StopsUrl(route.Id));
        var body = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(["Trạm C", "Trạm A", "Trạm B"], NamesOf(body));
        Assert.Equal([1, 2, 3], OrdersOf(body));
    }

    [Fact]
    public async Task Get_tra_ve_du_thong_tin_tram_de_man_hinh_keo_tha_hien_thi()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var route = await SeedRouteAsync(factory);
        var stop = await SeedStopAsync(factory, "Trạm Cầu Giấy", latitude: 21.0307, longitude: 105.8034);
        await SeedRouteStopAsync(factory, route.Id, stop.Id, stopOrder: 1);

        var body = await ReadJsonAsync(await client.GetAsync(StopsUrl(route.Id)));

        var row = body[0];
        Assert.Equal(stop.Id, row.GetProperty("stopId").GetGuid());
        Assert.Equal("Trạm Cầu Giấy", row.GetProperty("stopName").GetString());
        Assert.Equal("Địa chỉ Trạm Cầu Giấy", row.GetProperty("stopAddress").GetString());
        Assert.Equal(21.0307, row.GetProperty("latitude").GetDouble(), 4);
        Assert.Equal(105.8034, row.GetProperty("longitude").GetDouble(), 4);
    }

    [Fact]
    public async Task Get_tuyen_chua_gan_tram_nao_tra_mang_rong_chu_khong_phai_404()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var route = await SeedRouteAsync(factory);

        var response = await client.GetAsync(StopsUrl(route.Id));
        var body = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, body.GetArrayLength());
    }

    [Fact]
    public async Task Get_tuyen_khong_ton_tai_tra_404()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var response = await client.GetAsync(StopsUrl(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---------------------------------------------------------------------------------------
    // POST /routes/{routeId}/stops — gán trạm vào cuối tuyến
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Gan_tram_dau_tien_thi_stopOrder_bang_1()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var route = await SeedRouteAsync(factory);
        var stop = await SeedStopAsync(factory, "Trạm Cầu Giấy");

        var response = await client.PostAsJsonAsync(StopsUrl(route.Id), new { stopId = stop.Id });
        var body = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        // id là khoá của DÒNG bảng nối, khác stopId — cả hai đều phải có mặt và khác nhau.
        Assert.NotEqual(Guid.Empty, body.GetProperty("id").GetGuid());
        Assert.NotEqual(body.GetProperty("id").GetGuid(), body.GetProperty("stopId").GetGuid());

        Assert.Equal(route.Id, body.GetProperty("routeId").GetGuid());
        Assert.Equal(stop.Id, body.GetProperty("stopId").GetGuid());
        Assert.Equal("Trạm Cầu Giấy", body.GetProperty("stopName").GetString());
        Assert.Equal(1, body.GetProperty("stopOrder").GetInt32());
    }

    [Fact]
    public async Task Gan_tram_tiep_theo_thi_noi_vao_cuoi_tuyen()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var route = await SeedRouteAsync(factory);
        var a = await SeedStopAsync(factory, "Trạm A");
        var b = await SeedStopAsync(factory, "Trạm B");

        await client.PostAsJsonAsync(StopsUrl(route.Id), new { stopId = a.Id });
        var second = await client.PostAsJsonAsync(StopsUrl(route.Id), new { stopId = b.Id });

        Assert.Equal(2, (await ReadJsonAsync(second)).GetProperty("stopOrder").GetInt32());

        var body = await ReadJsonAsync(await client.GetAsync(StopsUrl(route.Id)));
        Assert.Equal(["Trạm A", "Trạm B"], NamesOf(body));
        Assert.Equal([1, 2], OrdersOf(body));
    }

    [Fact]
    public async Task Gan_tram_da_nam_tren_tuyen_tra_409()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var route = await SeedRouteAsync(factory);
        var stop = await SeedStopAsync(factory, "Trạm Cầu Giấy");

        await client.PostAsJsonAsync(StopsUrl(route.Id), new { stopId = stop.Id });
        var response = await client.PostAsJsonAsync(StopsUrl(route.Id), new { stopId = stop.Id });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(
            "Trạm đã nằm trên tuyến đường này",
            (await ReadJsonAsync(response)).GetProperty("message").GetString());
    }

    [Fact]
    public async Task Gan_cung_mot_tram_len_tuyen_khac_thi_duoc()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var firstRoute = await SeedRouteAsync(factory, code: "01");
        var secondRoute = await SeedRouteAsync(factory, code: "02");
        var stop = await SeedStopAsync(factory, "Trạm Cầu Giấy");

        await client.PostAsJsonAsync(StopsUrl(firstRoute.Id), new { stopId = stop.Id });
        var response = await client.PostAsJsonAsync(StopsUrl(secondRoute.Id), new { stopId = stop.Id });

        // Ràng buộc unique là theo CẶP (RouteId, StopId), không phải theo trạm — một trạm nằm
        // trên nhiều tuyến là chuyện bình thường.
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Gan_tram_vao_tuyen_khong_ton_tai_tra_404()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var stop = await SeedStopAsync(factory, "Trạm Cầu Giấy");

        var response = await client.PostAsJsonAsync(StopsUrl(Guid.NewGuid()), new { stopId = stop.Id });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Gan_tram_khong_ton_tai_tra_404()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var route = await SeedRouteAsync(factory);

        var response = await client.PostAsJsonAsync(StopsUrl(route.Id), new { stopId = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Gan_tram_khong_gui_stopId_tra_400_kem_loi_theo_truong()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var route = await SeedRouteAsync(factory);

        var response = await client.PostAsJsonAsync(StopsUrl(route.Id), new { distanceKm = 1.5m });
        var body = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(body.GetProperty("errors").TryGetProperty("stopId", out _));
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(-5)]
    public async Task Gan_tram_distance_am_tra_400(double distanceKm)
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var route = await SeedRouteAsync(factory);
        var stop = await SeedStopAsync(factory, "Trạm Cầu Giấy");

        var response = await client.PostAsJsonAsync(
            StopsUrl(route.Id), new { stopId = stop.Id, distanceKm });
        var body = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(body.GetProperty("errors").TryGetProperty("distanceKm", out _));
    }

    [Fact]
    public async Task Gan_tram_distance_vuot_tran_cot_tra_400()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var route = await SeedRouteAsync(factory);
        var stop = await SeedStopAsync(factory, "Trạm Cầu Giấy");

        // Trần cột numeric(6,2) là 9999.99. Chặn ở service vì PostgreSQL trả lỗi 22003 khi tràn
        // cột, mà lỗi đó bị DbUpdateException nuốt thành 409 "trạm đã nằm trên tuyến" — sai hẳn
        // nguyên nhân. InMemory còn không dựng HasPrecision nên chỉ service kiểm tra được.
        var response = await client.PostAsJsonAsync(
            StopsUrl(route.Id), new { stopId = stop.Id, distanceKm = 10000m });
        var body = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(body.GetProperty("errors").TryGetProperty("distanceKm", out _));
    }

    // ---------------------------------------------------------------------------------------
    // PUT /routes/{routeId}/stops/order — sắp xếp lại toàn bộ
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Sap_xep_lai_thi_stopOrder_bang_vi_tri_trong_mang()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var (route, stops) = await SeedRouteWithStopsAsync(factory, "Trạm A", "Trạm B", "Trạm C");
        var (a, b, c) = (stops[0], stops[1], stops[2]);

        var response = await client.PutAsJsonAsync(OrderUrl(route.Id), ReorderBody([c.Id, a.Id, b.Id]));
        var body = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(["Trạm C", "Trạm A", "Trạm B"], NamesOf(body));
        Assert.Equal([1, 2, 3], OrdersOf(body));

        // GET lại để chắc chắn thứ tự mới đã ghi xuống CSDL, không chỉ đúng trong response.
        Assert.Equal(["Trạm C", "Trạm A", "Trạm B"], NamesOf(await ReadJsonAsync(await client.GetAsync(StopsUrl(route.Id)))));
    }

    [Fact]
    public async Task Sap_xep_lai_bo_trong_distance_thi_giu_nguyen_gia_tri_cu()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var route = await SeedRouteAsync(factory);
        var a = await SeedStopAsync(factory, "Trạm A");
        var b = await SeedStopAsync(factory, "Trạm B");
        await SeedRouteStopAsync(factory, route.Id, a.Id, stopOrder: 1, distanceKm: 1.5m);
        await SeedRouteStopAsync(factory, route.Id, b.Id, stopOrder: 2, distanceKm: 2.5m);

        // Không gửi distanceKm: thao tác này là "sắp xếp lại thứ tự", không phải "viết lại
        // khoảng cách" — bỏ trống mà hiểu là 0 thì một lần kéo-thả sẽ xoá sạch số quản lý đã nhập.
        var body = await ReadJsonAsync(
            await client.PutAsJsonAsync(OrderUrl(route.Id), ReorderBody([b.Id, a.Id])));

        Assert.Equal(2.5m, body[0].GetProperty("distanceKm").GetDecimal());
        Assert.Equal(1.5m, body[1].GetProperty("distanceKm").GetDecimal());
    }

    [Fact]
    public async Task Sap_xep_lai_co_gui_distance_thi_cap_nhat()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var route = await SeedRouteAsync(factory);
        var a = await SeedStopAsync(factory, "Trạm A");
        var b = await SeedStopAsync(factory, "Trạm B");
        await SeedRouteStopAsync(factory, route.Id, a.Id, stopOrder: 1, distanceKm: 1.5m);
        await SeedRouteStopAsync(factory, route.Id, b.Id, stopOrder: 2, distanceKm: 2.5m);

        // Muốn đặt lại về 0 thì gửi thẳng 0 — có gửi thì luôn được ghi đè.
        var body = await ReadJsonAsync(await client.PutAsJsonAsync(
            OrderUrl(route.Id),
            new
            {
                items = new[]
                {
                    new { stopId = b.Id, distanceKm = 0m },
                    new { stopId = a.Id, distanceKm = 3.25m },
                },
            }));

        Assert.Equal(0m, body[0].GetProperty("distanceKm").GetDecimal());
        Assert.Equal(3.25m, body[1].GetProperty("distanceKm").GetDecimal());
    }

    [Fact]
    public async Task Sap_xep_lai_thieu_tram_tra_400()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var (route, stops) = await SeedRouteWithStopsAsync(factory, "Trạm A", "Trạm B");

        var response = await client.PutAsJsonAsync(OrderUrl(route.Id), ReorderBody([stops[0].Id]));
        var body = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            "Danh sách trạm phải khớp đúng các trạm hiện có của tuyến",
            body.GetProperty("message").GetString());
        Assert.True(body.GetProperty("errors").TryGetProperty("items", out _));
    }

    [Fact]
    public async Task Sap_xep_lai_thua_tram_tra_400()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var (route, stops) = await SeedRouteWithStopsAsync(factory, "Trạm A", "Trạm B");
        var outsider = await SeedStopAsync(factory, "Trạm Không Thuộc Tuyến");

        var response = await client.PutAsJsonAsync(
            OrderUrl(route.Id), ReorderBody([stops[0].Id, stops[1].Id, outsider.Id]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Sap_xep_lai_trung_tram_tra_400()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var (route, stops) = await SeedRouteWithStopsAsync(factory, "Trạm A", "Trạm B");

        // [A, A] đếm thì bằng số trạm hiện có và mọi phần tử đều thuộc tuyến — chỉ có phép so
        // tập hợp mới bắt được. Thiếu ca này thì lỗi "B bị ghi hai lần, A thì mất" lọt xuống CSDL.
        var response = await client.PutAsJsonAsync(
            OrderUrl(route.Id), ReorderBody([stops[0].Id, stops[0].Id]));
        var body = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        // Đòi đúng thông báo "bị lặp lại", không chỉ đòi 400: mảng [A, A] cũng rơi vào nhánh
        // "không khớp tập trạm" nên nếu chỉ kiểm tra mã trạng thái thì bài test vẫn xanh kể cả
        // khi nhánh kiểm tra trùng bị xoá — mà nhánh đó mới là thứ nói đúng chỗ người gọi cần sửa.
        Assert.Equal("Danh sách trạm có trạm bị lặp lại", body.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Sap_xep_lai_mang_rong_tra_400()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var (route, stops) = await SeedRouteWithStopsAsync(factory, "Trạm A");
        _ = stops;

        var response = await client.PutAsJsonAsync(OrderUrl(route.Id), ReorderBody([]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Sap_xep_lai_items_null_tra_400_chu_khong_phai_500()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var (route, stops) = await SeedRouteWithStopsAsync(factory, "Trạm A");
        _ = stops;

        // Body gửi thẳng "items": null — không có nhánh chặn thì NullReferenceException ở tầng
        // service, tức là lỗi 500.
        var content = new StringContent("{\"items\": null}", Encoding.UTF8, "application/json");
        var response = await client.PutAsync(OrderUrl(route.Id), content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Sap_xep_lai_tuyen_khong_ton_tai_tra_404()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var response = await client.PutAsJsonAsync(OrderUrl(Guid.NewGuid()), ReorderBody([Guid.NewGuid()]));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Sap_xep_lai_distance_am_tra_400()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var (route, stops) = await SeedRouteWithStopsAsync(factory, "Trạm A", "Trạm B");

        var response = await client.PutAsJsonAsync(
            OrderUrl(route.Id),
            new
            {
                items = new[]
                {
                    new { stopId = stops[1].Id, distanceKm = 2.5m },
                    new { stopId = stops[0].Id, distanceKm = -1m },
                },
            });
        var body = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(body.GetProperty("errors").TryGetProperty("distanceKm", out _));
    }

    [Fact]
    public async Task Sap_xep_lai_that_bai_thi_khong_doi_du_lieu()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var (route, stops) = await SeedRouteWithStopsAsync(factory, "Trạm A", "Trạm B");

        // Mảng sai ở phần tử CUỐI. Nếu service vừa kiểm tra vừa sửa thì phần tử đầu đã kịp đổi
        // thứ tự trước khi phát hiện lỗi — dữ liệu phải nguyên vẹn sau một request 400.
        var response = await client.PutAsJsonAsync(
            OrderUrl(route.Id),
            new
            {
                items = new[]
                {
                    new { stopId = stops[1].Id, distanceKm = 2.5m },
                    new { stopId = stops[0].Id, distanceKm = -1m },
                },
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await ReadJsonAsync(await client.GetAsync(StopsUrl(route.Id)));
        Assert.Equal(["Trạm A", "Trạm B"], NamesOf(body));
        Assert.Equal([1, 2], OrdersOf(body));
    }

    // ---------------------------------------------------------------------------------------
    // DELETE /routes/{routeId}/stops/{id}
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Go_tram_tra_204_va_don_so_cac_tram_con_lai()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var (route, stops) = await SeedRouteWithStopsAsync(factory, "Trạm A", "Trạm B", "Trạm C");
        var middleRowId = RowIdOfStop(await ReadJsonAsync(await client.GetAsync(StopsUrl(route.Id))), stops[1].Id);

        var response = await client.DeleteAsync(UrlOf(route.Id, middleRowId));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var body = await ReadJsonAsync(await client.GetAsync(StopsUrl(route.Id)));

        // "Trạm C" phải dồn từ 3 xuống 2 — đây là điều kiện để stopOrder không bao giờ có lỗ hổng.
        Assert.Equal(["Trạm A", "Trạm C"], NamesOf(body));
        Assert.Equal([1, 2], OrdersOf(body));
    }

    [Fact]
    public async Task Go_tram_bang_stopId_thay_vi_id_dong_thi_tra_404()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var (route, stops) = await SeedRouteWithStopsAsync(factory, "Trạm A");

        // Đường dẫn nhận id của DÒNG RouteStop, không phải stopId của trạm. Ca này ghi lại
        // đúng cái bẫy đó để không ai "sửa" endpoint thành nhận stopId mà không đọc hợp đồng.
        var response = await client.DeleteAsync(UrlOf(route.Id, stops[0].Id));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Go_tram_khong_nam_tren_tuyen_tra_404()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var route = await SeedRouteAsync(factory, code: "01");
        var otherRoute = await SeedRouteAsync(factory, code: "02");
        var stop = await SeedStopAsync(factory, "Trạm Cầu Giấy");

        var otherRow = await SeedRouteStopAsync(factory, otherRoute.Id, stop.Id, stopOrder: 1);

        // Dòng có thật, nhưng thuộc tuyến khác — kèm routeId vào điều kiện tìm là để ra 404
        // chứ không phải gỡ nhầm trạm của tuyến khác.
        var response = await client.DeleteAsync(UrlOf(route.Id, otherRow.Id));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Go_tram_tuyen_khong_ton_tai_tra_404()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var response = await client.DeleteAsync(UrlOf(Guid.NewGuid(), Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---------------------------------------------------------------------------------------
    // Helper — mỗi lớp test tự chép, không có lớp base chung (đúng lệ đang có của dự án)
    // ---------------------------------------------------------------------------------------

    private static string StopsUrl(Guid routeId) => $"/api/routes/{routeId}/stops";

    private static string OrderUrl(Guid routeId) => $"{StopsUrl(routeId)}/order";

    private static string UrlOf(Guid routeId, Guid routeStopId) => $"{StopsUrl(routeId)}/{routeStopId}";

    private static object ReorderBody(Guid[] stopIds)
        => new { items = stopIds.Select(stopId => new { stopId }).ToArray() };

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
        => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static string[] NamesOf(JsonElement array)
        => array.EnumerateArray()
            .Select(row => row.GetProperty("stopName").GetString() ?? string.Empty)
            .ToArray();

    private static int[] OrdersOf(JsonElement array)
        => array.EnumerateArray()
            .Select(row => row.GetProperty("stopOrder").GetInt32())
            .ToArray();

    private static Guid RowIdOfStop(JsonElement array, Guid stopId)
        => array.EnumerateArray()
            .First(row => row.GetProperty("stopId").GetGuid() == stopId)
            .GetProperty("id")
            .GetGuid();

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
            Name = $"Tuyến {code}",
            Origin = "Bến Thành",
            Destination = "Chợ Lớn",
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

    /// <summary>Seed một tuyến đã gán sẵn các trạm theo đúng thứ tự truyền vào (bắt đầu từ 1).</summary>
    private static async Task<(RouteEntity Route, List<Stop> Stops)> SeedRouteWithStopsAsync(
        TestAppFactory factory,
        params string[] stopNames)
    {
        var route = await SeedRouteAsync(factory);
        var stops = new List<Stop>();

        for (var index = 0; index < stopNames.Length; index++)
        {
            var stop = await SeedStopAsync(factory, stopNames[index]);
            stops.Add(stop);

            await SeedRouteStopAsync(factory, route.Id, stop.Id, stopOrder: index + 1);
        }

        return (route, stops);
    }
}
