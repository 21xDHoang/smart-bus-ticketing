using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SmartBus.Api.Entities;
using SmartBus.Api.Services;
using RouteEntity = SmartBus.Api.Entities.Route;
using UserEntity = SmartBus.Api.Entities.User;

namespace SmartBus.Tests;

/// <summary>
/// Test theo bộ dữ liệu đầu vào cho <c>GET /api/trips/search</c> — task *"Test API tìm kiếm tuyến với
/// nhiều bộ dữ liệu đầu vào (xUnit)"* (Giàng A Vàng, story 1). Hợp đồng đầy đủ ở mục
/// "GET /trips/search" của docs/api-contract.md.
///
/// Vì sao có lớp test này bên cạnh <see cref="TripSearchCacheApiTests"/>: lớp kia khoá **bộ đệm**
/// (task của Kiên) và chỉ chạm endpoint ở vài ca đủ để chứng minh đệm không đổi hình dạng response.
/// Lớp này khoá **không gian đầu vào** của endpoint: ba tham số <c>routeId</c>/<c>from</c>/<c>to</c>
/// được quét theo bảng thay vì theo từng ca rời — đúng nghĩa "nhiều bộ dữ liệu đầu vào".
///
/// Endpoint là API CÔNG KHAI duy nhất của bề mặt <c>/trips</c> nên không ca nào ở đây gắn token;
/// một ca có gắn token để chứng minh điều ngược lại (token cũng không đổi kết quả).
///
/// ⚠️ Bộ đệm: mỗi ca dựng một <see cref="TestAppFactory"/> riêng nên không dính kết quả của ca
/// khác. Trong cùng một ca, chỉ lượt gọi ĐẦU TIÊN là chắc chắn đọc tươi từ CSDL — từ lượt thứ hai
/// trở đi kết quả có thể tới từ bộ đệm (xem <see cref="TripSearchResultCache"/>). Vì vậy mọi khẳng
/// định về dữ liệu đều đọc ở lượt gọi đầu.
/// </summary>
public class TripSearchInputDataApiTests
{
    /// <summary>Mốc 00:00 ngày 02/10/2026 UTC — mọi chuyến trong lớp này rơi quanh ngày này.</summary>
    private static readonly DateTime DauNgay = new(2026, 10, 2, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>Giờ khởi hành của một chuyến: <c>Gio(8)</c> = 08:00 ngày <see cref="DauNgay"/>.</summary>
    private static DateTime Gio(int gio) => DauNgay.AddHours(gio);

    // =======================================================================================
    // 1. routeId — tập giá trị SAI ĐỊNH DẠNG
    // =======================================================================================

    /// <summary>
    /// Hợp đồng: *"routeId thiếu hoặc sai định dạng GUID → 400"*. Bảng dưới quét các cách viết
    /// hỏng khác nhau — bỏ trống, chữ, số, GUID thiếu ký tự, GUID thừa ký tự — vì mỗi kiểu hỏng
    /// đi qua một nhánh bind khác nhau của ModelState.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("123")]
    [InlineData("khong-phai-guid")]
    [InlineData("3f2a1b0c-0000-0000-0000-00000000000")]   // thiếu 1 ký tự
    [InlineData("3f2a1b0c-0000-0000-0000-0000000000000")] // thừa 1 ký tự
    [InlineData("{3f2a1b0c-0000-0000-0000-00000000000}")] // ngoặc lệch
    public async Task RouteId_sai_dinh_dang_tra_400_kem_loi_theo_truong(string routeId)
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync(SearchUrl(routeId: routeId));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        // D3: mọi endpoint trả lỗi theo đúng một khuôn { message, errors }, và `errors` khoá theo
        // tên trường camelCase — frontend đọc thẳng khoá đó để tô đỏ ô nhập.
        var body = await ReadJsonAsync(response);

        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("message").GetString()));
        AssertCoLoiTheoTruong(body, "routeId");
    }

    /// <summary>
    /// Ca riêng: **không gửi tham số nào cả**. Không thể nhét vào bảng trên vì ở đây khoá
    /// <c>routeId</c> vắng hẳn khỏi query string, còn bảng trên gửi khoá với giá trị hỏng.
    /// </summary>
    [Fact]
    public async Task Thieu_hoan_toan_routeId_tra_400()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/trips/search");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AssertCoLoiTheoTruong(await ReadJsonAsync(response), "routeId");
    }

    /// <summary>
    /// Gửi <c>routeId</c> nhưng để rỗng — cùng kết cục 400 như thiếu hẳn, chỉ khác đường đi
    /// (rỗng → null → <c>[Required]</c>, chứ không phải lỗi bind).
    /// </summary>
    [Fact]
    public async Task RouteId_rong_tra_400_chu_khong_phai_404()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync(SearchUrl(routeId: string.Empty));

        // `Guid.Empty` KHÔNG được lọt qua đây: rỗng là "chưa nhập", phải là 400; còn
        // "00000000-0000-0000-0000-000000000000" là GUID hợp lệ trỏ tới tuyến không tồn tại → 404.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AssertCoLoiTheoTruong(await ReadJsonAsync(response), "routeId");
    }

    // =======================================================================================
    // 2. routeId — GUID HỢP LỆ nhưng không trỏ tới tuyến nào
    // =======================================================================================

    /// <summary>
    /// Hợp đồng: *"GUID không trỏ tới tuyến nào → 404"*, tham chiếu cứng — tuyến không tồn tại là
    /// lỗi GỌI, không phải "tuyến này chưa có chuyến nào". Bảng quét cả dạng GUID có gạch và dạng
    /// <c>N</c> (32 ký tự hex, không gạch) — dạng <c>N</c> vẫn là GUID hợp lệ nên phải đi tiếp tới
    /// bước tra tuyến chứ không được dừng ở 400.
    /// </summary>
    [Theory]
    [InlineData("00000000-0000-0000-0000-000000000000")] // Guid.Empty
    [InlineData("3f2a1b0c-0000-0000-0000-000000000001")]
    [InlineData("3f2a1b0c000000000000000000000001")]     // dạng N (32 hex, không gạch) — hợp lệ
    [InlineData("FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF")]
    public async Task RouteId_hop_le_nhung_khong_ton_tai_tra_404(string routeId)
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync(SearchUrl(routeId: routeId));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Không tìm thấy tuyến đường", await MessageAsync(response));
    }

    // =======================================================================================
    // 3. from / to — bảng tổ hợp quyết định TẬP CHUYẾN trả về
    // =======================================================================================

    /// <summary>
    /// Bốn chuyến trong ngày (06h, 08h, 10h, 12h) rồi quét các tổ hợp <c>from</c>/<c>to</c>:
    /// không mốc nào · chỉ <c>from</c> · chỉ <c>to</c> · cả hai · <c>from == to</c> · khoảng rỗng ·
    /// khoảng phủ cả ngày. Đây là phần lõi của "nhiều bộ dữ liệu đầu vào".
    ///
    /// Mốc hai đầu đều **tính luôn** (hợp đồng: *"tính luôn mốc"*) nên chuyến đúng 08:00 phải có
    /// mặt ở cả <c>from=08:00</c> lẫn <c>to=08:00</c> — sai chỗ này là hành khách mất chuyến ngay
    /// tại giờ mình chọn.
    /// </summary>
    public static TheoryData<int?, int?, int[]> CacBoKhoangThoiGian => new()
    {
        { null, null, [6, 8, 10, 12] },   // không lọc → cả ngày
        { 8, null, [8, 10, 12] },         // chỉ from → khoảng mở về sau
        { null, 8, [6, 8] },              // chỉ to → khoảng mở về trước, tính luôn mốc
        { 7, 9, [8] },                    // cả hai, khoảng ở giữa
        { 8, 8, [8] },                    // from == to → đúng khoảnh khắc 08:00, KHÔNG phải 400
        { 13, null, [] },                 // from sau chuyến cuối → rỗng
        { null, 5, [] },                  // to trước chuyến đầu → rỗng
        { 0, 23, [6, 8, 10, 12] },        // phủ cả ngày
        { 6, 12, [6, 8, 10, 12] },        // hai mốc trùng đúng chuyến đầu và cuối → tính luôn cả hai
    };

    [Theory]
    [MemberData(nameof(CacBoKhoangThoiGian))]
    public async Task Bo_loc_from_to_tra_dung_tap_chuyen(int? tuGio, int? denGio, int[] gioMongDoi)
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        var route = await SeedRouteAsync(factory);
        var bus = await SeedBusAsync(factory);

        foreach (var gio in new[] { 6, 8, 10, 12 })
        {
            await SeedTripAsync(factory, route.Id, bus.Id, Gio(gio));
        }

        var body = await ReadJsonAsync(await client.GetAsync(
            SearchUrl(route.Id, from: tuGio is { } tu ? IsoZ(Gio(tu)) : null,
                              to: denGio is { } den ? IsoZ(Gio(den)) : null)));

        Assert.Equal(
            gioMongDoi.Select(Gio),
            body.EnumerateArray().Select(dong => dong.GetProperty("departureTime").GetDateTime()));
    }

    /// <summary>
    /// Cùng một khoảng thời gian nhưng viết bằng hai múi giờ khác nhau phải ra **cùng kết quả**:
    /// giờ Việt Nam (+07:00) quy về UTC khi so sánh. Màn hình tra cứu gửi mốc ngày theo giờ địa
    /// phương của hành khách, còn <c>DepartureTime</c> lưu UTC — lệch phép quy đổi là lệch cả ngày.
    ///
    /// Chuyến 01:00Z chính là 08:00 giờ Việt Nam, nên mốc <c>08:00+07:00</c> rơi ĐÚNG vào nó và
    /// phải trả về chuyến đó (tính luôn mốc), không được trả rỗng.
    /// </summary>
    [Theory]
    [InlineData("2026-10-02T08:00:00+07:00")] // = 01:00Z — mốc from trùng đúng giờ chạy
    [InlineData("2026-10-02T01:00:00Z")]     // cùng khoảnh khắc, viết bằng UTC
    [InlineData("2026-10-02T01:00:00+00:00")] // cùng khoảnh khắc, offset 0 viết tường minh
    public async Task Moc_thoi_gian_theo_gio_viet_Nam_quy_ve_UTC_khi_so_sanh(string mocFrom)
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        var route = await SeedRouteAsync(factory);
        var bus = await SeedBusAsync(factory);
        await SeedTripAsync(factory, route.Id, bus.Id, Gio(1));

        var body = await ReadJsonAsync(await client.GetAsync(SearchUrl(route.Id, from: mocFrom)));

        var dong = Assert.Single(body.EnumerateArray());
        Assert.Equal(Gio(1), dong.GetProperty("departureTime").GetDateTime());
    }

    /// <summary>
    /// 08:00 giờ Việt Nam là 01:00Z — nếu ai đó so mốc thô (bỏ qua offset) thì chuyến 01:00Z sẽ
    /// bị coi là "trước mốc" và rơi mất. Ca này ghim đúng lỗi đó bằng một mốc lệch 7 tiếng.
    /// </summary>
    [Fact]
    public async Task Moc_gio_Viet_Nam_khong_bi_hieu_nham_thanh_UTC()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        var route = await SeedRouteAsync(factory);
        var bus = await SeedBusAsync(factory);

        // 01:00Z (= 08:00 giờ VN) và 10:00Z (= 17:00 giờ VN).
        await SeedTripAsync(factory, route.Id, bus.Id, Gio(1));
        await SeedTripAsync(factory, route.Id, bus.Id, Gio(10));

        // Lọc "từ 08:00 sáng giờ VN": phải còn CẢ HAI. So thô thành "từ 08:00Z" thì chuyến
        // 01:00Z mất oan.
        var body = await ReadJsonAsync(await client.GetAsync(
            SearchUrl(route.Id, from: "2026-10-02T08:00:00+07:00")));

        Assert.Equal(
            [Gio(1), Gio(10)],
            body.EnumerateArray().Select(dong => dong.GetProperty("departureTime").GetDateTime()));
    }

    // =======================================================================================
    // 4. from / to — khoảng SAI (to sớm hơn from)
    // =======================================================================================

    /// <summary>
    /// Hợp đồng: *"to sớm hơn from → 400 errors.to"*. Bảng quét các mức lệch khác nhau — lệch 1
    /// giây, lệch nửa ngày, lệch sang ngày hôm trước. Chỉ báo lỗi khi **cả hai** mốc cùng có mặt:
    /// thiếu một mốc là khoảng mở, không phải khoảng sai.
    /// </summary>
    [Theory]
    [InlineData("2026-10-02T08:00:00Z", "2026-10-02T07:59:59Z")] // lệch 1 giây
    [InlineData("2026-10-02T08:00:00Z", "2026-10-02T00:00:00Z")] // lệch trong ngày
    [InlineData("2026-10-02T08:00:00Z", "2026-10-01T08:00:00Z")] // lệch sang hôm trước
    [InlineData("2026-10-02T23:59:59+07:00", "2026-10-02T00:00:00Z")] // sai khác múi giờ
    public async Task To_som_hon_from_tra_400_kem_loi_o_truong_to(string from, string to)
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        var route = await SeedRouteAsync(factory);

        var response = await client.GetAsync(SearchUrl(route.Id, from: from, to: to));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await ReadJsonAsync(response);

        // Lỗi nghiệp vụ của service trả câu của service ở cả `message` lẫn `errors.to`.
        Assert.Equal("Thời điểm kết thúc phải sau thời điểm bắt đầu", body.GetProperty("message").GetString());
        Assert.Equal(
            "Thời điểm kết thúc phải sau thời điểm bắt đầu",
            body.GetProperty("errors").GetProperty("to")[0].GetString());
    }

    /// <summary>
    /// Khoảng sai phải bị chặn kể cả khi tuyến **không có chuyến nào** — nếu không, màn hình nhận
    /// mảng rỗng và tưởng "hôm nay không có chuyến" trong khi thật ra tham số gửi sai.
    /// </summary>
    [Fact]
    public async Task Khoang_sai_van_tra_400_khi_tuyen_khong_co_chuyen_nao()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        var route = await SeedRouteAsync(factory);

        var response = await client.GetAsync(
            SearchUrl(route.Id, from: IsoZ(Gio(8)), to: IsoZ(Gio(7))));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("to", (await ReadJsonAsync(response)).GetProperty("errors").EnumerateObject().Single().Name);
    }

    /// <summary>
    /// Đối chứng của bảng trên: <c>from == to</c> KHÔNG phải khoảng sai (service chỉ chặn
    /// <c>to &lt; from</c>) — đây là truy vấn "đúng một khoảnh khắc", hợp lệ và phải trả 200.
    /// </summary>
    [Fact]
    public async Task From_bang_to_la_khoang_hop_le_khong_phai_400()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        var route = await SeedRouteAsync(factory);
        var bus = await SeedBusAsync(factory);
        await SeedTripAsync(factory, route.Id, bus.Id, Gio(8));

        var response = await client.GetAsync(SearchUrl(route.Id, from: IsoZ(Gio(8)), to: IsoZ(Gio(8))));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, (await ReadJsonAsync(response)).GetArrayLength());
    }

    // =======================================================================================
    // 5. Chuyến không khớp bộ lọc → mảng rỗng, KHÔNG phải 404
    // =======================================================================================

    /// <summary>
    /// Hợp đồng: *"Không có chuyến nào khớp → [], không phải 404"*. Bảng quét hai đường dẫn tới
    /// mảng rỗng: tuyến chưa có chuyến nào, và tuyến có chuyến nhưng nằm ngoài khoảng hỏi.
    /// </summary>
    [Theory]
    [InlineData(true, null, null)]   // tuyến mới, không seed chuyến nào
    [InlineData(false, 20, null)]    // có chuyến trong ngày nhưng hỏi sau giờ cuối
    [InlineData(false, null, 5)]     // có chuyến nhưng hỏi trước giờ đầu
    public async Task Khong_co_chuyen_khop_tra_mang_rong_chu_khong_phai_404(
        bool tuyenRong, int? tuGio, int? denGio)
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        var route = await SeedRouteAsync(factory);

        if (!tuyenRong)
        {
            var bus = await SeedBusAsync(factory);
            await SeedTripAsync(factory, route.Id, bus.Id, Gio(6));
            await SeedTripAsync(factory, route.Id, bus.Id, Gio(8));
        }

        var response = await client.GetAsync(SearchUrl(
            route.Id,
            from: tuGio is { } tu ? IsoZ(Gio(tu)) : null,
            to: denGio is { } den ? IsoZ(Gio(den)) : null));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, (await ReadJsonAsync(response)).GetArrayLength());
    }

    // =======================================================================================
    // 6. Bộ lọc trạng thái — chỉ chuyến Scheduled
    // =======================================================================================

    /// <summary>
    /// Hợp đồng: *"Chỉ trả chuyến Scheduled"* và endpoint **không có tham số status**. Bảng quét
    /// bốn trạng thái: mỗi lượt seed một chuyến ở trạng thái đó (cùng giờ) rồi hỏi — chỉ
    /// <c>Scheduled</c> được lọt ra.
    /// </summary>
    [Theory]
    [InlineData(TripStatus.Scheduled, 1)]
    [InlineData(TripStatus.Running, 0)]
    [InlineData(TripStatus.Completed, 0)]
    [InlineData(TripStatus.Cancelled, 0)]
    public async Task Chi_tra_chuyen_Scheduled(TripStatus trangThai, int soChuyenMongDoi)
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        var route = await SeedRouteAsync(factory);
        var bus = await SeedBusAsync(factory);
        await SeedTripAsync(factory, route.Id, bus.Id, Gio(8), status: trangThai);

        var body = await ReadJsonAsync(await client.GetAsync(SearchUrl(route.Id)));

        Assert.Equal(soChuyenMongDoi, body.GetArrayLength());
    }

    /// <summary>
    /// Gửi kèm tham số <c>status</c> — thứ endpoint này KHÔNG có (khác <c>GET /trips</c> của màn
    /// hình điều hành). Tham số lạ phải bị bỏ qua, không được lọc bớt chuyến: hành khách không có
    /// cách nào tự loại chuyến khỏi kết quả tra cứu.
    /// </summary>
    [Fact]
    public async Task Tham_so_status_khong_duoc_ho_tro_va_bi_bo_qua()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        var route = await SeedRouteAsync(factory);
        var bus = await SeedBusAsync(factory);
        await SeedTripAsync(factory, route.Id, bus.Id, Gio(8));

        // status=Cancelled: nếu ai đó lỡ nối tham số này vào truy vấn thì kết quả rỗng.
        var body = await ReadJsonAsync(await client.GetAsync(SearchUrl(route.Id, extra: "status=Cancelled")));

        Assert.Equal(1, body.GetArrayLength());
    }

    // =======================================================================================
    // 7. Giá vé — chỉ giá phổ thông, thiếu giá là trạng thái bình thường
    // =======================================================================================

    /// <summary>
    /// Hợp đồng: <c>price</c> là **giá vé phổ thông** (<c>PassengerType.Standard</c>) của tuyến,
    /// <c>null</c> khi tuyến chưa cấu hình giá. Bảng quét các thế đặt bảng giá: chỉ có phổ thông ·
    /// đủ cả năm đối tượng · không có dòng nào · chỉ có đối tượng ưu đãi.
    ///
    /// Hai ca cuối quan trọng: tuyến có bảng giá nhưng **thiếu dòng Standard** vẫn phải trả
    /// <c>null</c>, không được lấy đại giá của sinh viên/người cao tuổi làm giá niêm yết.
    /// </summary>
    public static TheoryData<string, decimal?> CacTheBangGia => new()
    {
        { "pho-thong-7000", 7000m },
        { "du-nam-doi-tuong", 7000m },
        { "khong-co-dong-nao", null },
        { "chi-co-sinh-vien", null },
        { "chi-co-nguoi-cao-tuoi", null },
        { "pho-thong-0", 0m },
    };

    [Theory]
    [MemberData(nameof(CacTheBangGia))]
    public async Task Price_chi_lay_gia_pho_thong(string the, decimal? giaMongDoi)
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        var route = await SeedRouteAsync(factory);
        var bus = await SeedBusAsync(factory);
        await SeedTripAsync(factory, route.Id, bus.Id, Gio(8));

        switch (the)
        {
            case "pho-thong-7000":
                await SeedFareAsync(factory, route.Id, PassengerType.Standard, 7000m);
                break;
            case "du-nam-doi-tuong":
                await SeedFareAsync(factory, route.Id, PassengerType.Standard, 7000m);
                await SeedFareAsync(factory, route.Id, PassengerType.Student, 3000m);
                await SeedFareAsync(factory, route.Id, PassengerType.Senior, 0m);
                await SeedFareAsync(factory, route.Id, PassengerType.Child, 1000m);
                await SeedFareAsync(factory, route.Id, PassengerType.Disabled, 500m);
                break;
            case "chi-co-sinh-vien":
                await SeedFareAsync(factory, route.Id, PassengerType.Student, 3000m);
                break;
            case "chi-co-nguoi-cao-tuoi":
                await SeedFareAsync(factory, route.Id, PassengerType.Senior, 0m);
                break;
            case "pho-thong-0":
                await SeedFareAsync(factory, route.Id, PassengerType.Standard, 0m);
                break;
        }

        var dong = Assert.Single((await ReadJsonAsync(await client.GetAsync(SearchUrl(route.Id)))).EnumerateArray());

        var price = dong.GetProperty("price");

        if (giaMongDoi is null)
        {
            // Thiếu giá là trạng thái dữ liệu bình thường — màn hình hiện "Chưa có giá", không 404.
            Assert.Equal(JsonValueKind.Null, price.ValueKind);
        }
        else
        {
            Assert.Equal(giaMongDoi.Value, price.GetDecimal());
        }
    }

    /// <summary>
    /// Giá vé là **của tuyến**, không phải của từng chuyến: hai chuyến trong cùng tuyến trả cùng
    /// một con số, kể cả khi chạy bằng hai xe khác nhau.
    /// </summary>
    [Fact]
    public async Task Gia_ve_giong_nhau_giua_cac_chuyen_cua_cung_tuyen()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        var route = await SeedRouteAsync(factory);
        var xe45 = await SeedBusAsync(factory);
        var xe29 = await SeedBusAsync(factory, licensePlate: "29B-999.99", capacity: 29);

        await SeedTripAsync(factory, route.Id, xe45.Id, Gio(8));
        await SeedTripAsync(factory, route.Id, xe29.Id, Gio(10));
        await SeedFareAsync(factory, route.Id, PassengerType.Standard, 7000m);

        var body = await ReadJsonAsync(await client.GetAsync(SearchUrl(route.Id)));

        Assert.All(
            body.EnumerateArray(),
            dong => Assert.Equal(7000m, dong.GetProperty("price").GetDecimal()));
    }

    // =======================================================================================
    // 8. Xe chạy chuyến — sức chứa và loại xe đi kèm kết quả
    // =======================================================================================

    /// <summary>
    /// <c>capacity</c> / <c>busType</c> / <c>seatsRemaining</c> lấy từ **xe chạy chuyến**, không
    /// phải từ tuyến. Bảng quét hai loại xe khác nhau — nếu ai đó lấy nhầm hằng số hay lấy từ
    /// tuyến thì ca 29 chỗ sẽ lộ ngay.
    ///
    /// <c>seatsRemaining</c> hôm nay luôn bằng <c>capacity</c>: bảng vé/giữ chỗ chưa migrate
    /// (Sprint 3). Ghim luôn con số đó để lúc bảng vé vào, test này đỏ và người sửa biết phải
    /// cập nhật.
    /// </summary>
    [Theory]
    [InlineData(45, "Xe buýt 45 chỗ")]
    [InlineData(29, "Xe buýt 29 chỗ")]
    [InlineData(16, "Xe buýt 16 chỗ")]
    public async Task Suc_chua_va_loai_xe_lay_tu_xe_chay_chuyen(int sucChua, string loaiXe)
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        var route = await SeedRouteAsync(factory);
        var bus = await SeedBusAsync(factory, busType: loaiXe, capacity: sucChua);
        await SeedTripAsync(factory, route.Id, bus.Id, Gio(8));

        var dong = Assert.Single((await ReadJsonAsync(await client.GetAsync(SearchUrl(route.Id)))).EnumerateArray());

        Assert.Equal(sucChua, dong.GetProperty("capacity").GetInt32());
        Assert.Equal(sucChua, dong.GetProperty("seatsRemaining").GetInt32());
        Assert.Equal(loaiXe, dong.GetProperty("busType").GetString());
    }

    // =======================================================================================
    // 9. Chuyến mồ côi (xe đã biến mất) không được lọt ra kết quả
    // =======================================================================================

    /// <summary>
    /// Hợp đồng: *"Chuyến mồ côi (tuyến/xe đã biến mất) không bao giờ lọt ra kết quả"* — phép đọc
    /// ghép <c>Trips ⋈ Buses</c> tường minh. Hành khách nhìn thấy một chuyến mà không biết xe nào
    /// chạy thì không đặt vé được, nên chuyến đó phải biến mất khỏi kết quả thay vì hiện với
    /// <c>capacity = 0</c>.
    /// </summary>
    [Fact]
    public async Task Chuyen_mo_coi_khong_lot_ra_ket_qua()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        var route = await SeedRouteAsync(factory);
        var bus = await SeedBusAsync(factory);

        await SeedTripAsync(factory, route.Id, bus.Id, Gio(6));
        // Chuyến trỏ tới xe không tồn tại — chỉ gán BusId, không tạo dòng Bus nào.
        await SeedTripAsync(factory, route.Id, Guid.NewGuid(), Gio(8));

        var body = await ReadJsonAsync(await client.GetAsync(SearchUrl(route.Id)));

        var dong = Assert.Single(body.EnumerateArray());
        Assert.Equal(Gio(6), dong.GetProperty("departureTime").GetDateTime());
    }

    // =======================================================================================
    // 10. arrivalTime — chuyến chưa chốt giờ đến
    // =======================================================================================

    /// <summary>
    /// Hợp đồng: *"arrivalTime: chuyến chưa có giờ đến → null"* — chuyến sinh hàng loạt có thể
    /// chưa chốt giờ đến. Bảng quét cả hai thế: có giờ đến và chưa có.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ArrivalTime_null_khi_chuyen_chua_chot_gio_den(bool coGioDen)
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        var route = await SeedRouteAsync(factory);
        var bus = await SeedBusAsync(factory);
        // Chuyến 08:00 chạy 60 phút → giờ đến 09:00, để giá trị mong đợi khác hẳn mặc định của
        // helper (45 phút) — nếu ai đó trả nhầm hằng số mặc định thì ca này bắt được.
        await SeedTripAsync(factory, route.Id, bus.Id, Gio(8), chuaChotGioDen: !coGioDen, phutChay: 60);

        var dong = Assert.Single((await ReadJsonAsync(await client.GetAsync(SearchUrl(route.Id)))).EnumerateArray());

        var arrival = dong.GetProperty("arrivalTime");

        if (coGioDen)
        {
            Assert.Equal(Gio(9), arrival.GetDateTime());
        }
        else
        {
            Assert.Equal(JsonValueKind.Null, arrival.ValueKind);
        }
    }

    // =======================================================================================
    // 11. Thứ tự — trùng giờ khởi hành thì xếp theo Id
    // =======================================================================================

    /// <summary>
    /// Hợp đồng: *"Thứ tự: departureTime tăng dần, trùng giờ xếp tiếp theo id để hai lần gọi ra
    /// cùng một kết quả"*. Chuyến sinh hàng loạt cùng một giây là chuyện thường, nên nhánh trùng
    /// giờ là nhánh chạy thật chứ không phải ca hiếm.
    ///
    /// Khẳng định đọc ở **lượt gọi đầu** — từ lượt thứ hai kết quả có thể tới từ bộ đệm nên so hai
    /// lượt với nhau không chứng minh được gì (xem ghi chú đầu lớp).
    /// </summary>
    [Fact]
    public async Task Trung_gio_khoi_hanh_thi_xep_theo_Id()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        var route = await SeedRouteAsync(factory);
        var bus = await SeedBusAsync(factory);

        // Hai chuyến CÙNG giờ, id đặt ngược thứ tự chèn để nếu ai đó bỏ ThenBy(Id) thì thứ tự
        // rơi về thứ tự chèn và test đỏ.
        var idLon = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");
        var idNho = Guid.Parse("00000000-0000-0000-0000-000000000001");

        await SeedTripAsync(factory, route.Id, bus.Id, Gio(8), id: idLon);
        await SeedTripAsync(factory, route.Id, bus.Id, Gio(8), id: idNho);

        var body = await ReadJsonAsync(await client.GetAsync(SearchUrl(route.Id)));

        Assert.Equal(
            [idNho, idLon],
            body.EnumerateArray().Select(dong => dong.GetProperty("id").GetGuid()));
    }

    // =======================================================================================
    // 12. Không lẫn chuyến của tuyến khác
    // =======================================================================================

    /// <summary>
    /// <c>routeId</c> là bộ lọc DUY NHẤT của endpoint (ngoài khoảng thời gian và trạng thái), nên
    /// hỏi tuyến A không bao giờ được thấy chuyến của tuyến B — kể cả khi cả hai chạy cùng xe,
    /// cùng giờ. Kèm luôn <c>routeCode</c>/<c>routeName</c> phải là của tuyến được hỏi.
    /// </summary>
    [Fact]
    public async Task Khong_lan_chuyen_cua_tuyen_khac()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        var tuyenA = await SeedRouteAsync(factory, code: "01", name: "Bến xe Mỹ Đình — Bến xe Gia Lâm");
        var tuyenB = await SeedRouteAsync(factory, code: "02", name: "Bến xe Giáp Bát — Bến xe Yên Nghĩa");
        var bus = await SeedBusAsync(factory);

        await SeedTripAsync(factory, tuyenA.Id, bus.Id, Gio(8));
        await SeedTripAsync(factory, tuyenB.Id, bus.Id, Gio(8));
        await SeedTripAsync(factory, tuyenB.Id, bus.Id, Gio(10));

        var bodyA = await ReadJsonAsync(await client.GetAsync(SearchUrl(tuyenA.Id)));

        var dong = Assert.Single(bodyA.EnumerateArray());
        Assert.Equal(tuyenA.Id, dong.GetProperty("routeId").GetGuid());
        Assert.Equal("01", dong.GetProperty("routeCode").GetString());
        Assert.Equal("Bến xe Mỹ Đình — Bến xe Gia Lâm", dong.GetProperty("routeName").GetString());

        // Đối chứng: tuyến B vẫn trả đủ hai chuyến của nó.
        var bodyB = await ReadJsonAsync(await client.GetAsync(SearchUrl(tuyenB.Id)));
        Assert.Equal(2, bodyB.GetArrayLength());
    }

    // =======================================================================================
    // 13. Endpoint công khai — token không đổi kết quả
    // =======================================================================================

    /// <summary>
    /// <see cref="TripSearchCacheApiTests"/> đã ghim "gọi không token vẫn 200". Ca này ghim vế còn
    /// lại: **có** token (kể cả token của vai trò thấp nhất) cũng ra đúng kết quả đó — endpoint
    /// công khai thì không được đổi hành vi theo người gọi.
    /// </summary>
    [Fact]
    public async Task Co_token_hay_khong_thi_ket_qua_giong_het_nhau()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        var route = await SeedRouteAsync(factory);
        var bus = await SeedBusAsync(factory);
        await SeedTripAsync(factory, route.Id, bus.Id, Gio(8));

        var khach = await SeedUserAsync(factory, "Passenger");
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", factory.CreateTokenFor(khach));

        var body = await ReadJsonAsync(await client.GetAsync(SearchUrl(route.Id)));

        Assert.Equal(1, body.GetArrayLength());
    }

    // =======================================================================================
    // 14. from / to — tập giá trị SAI ĐỊNH DẠNG
    // =======================================================================================

    /// <summary>
    /// Hợp đồng khai <c>from</c>/<c>to</c> là *"ISO 8601 có múi giờ"*. Bảng quét các cách viết hỏng
    /// — chữ, giờ không tồn tại (99:99:99), tháng/ngày không tồn tại (13/45), chuỗi rỗng.
    ///
    /// Vì sao ca này đáng có: mốc thời gian hỏng mà **lọt** qua bind sẽ bị hiểu thành một khoảnh
    /// khắc nào đó (thường là null hoặc mặc định) và màn hình nhận mảng rỗng — hành khách tưởng
    /// "hôm đó không có chuyến" trong khi thật ra tham số gửi sai. Sai thì phải ồn ào.
    /// </summary>
    [Theory]
    [InlineData("from", "hom-qua", null)]
    [InlineData("from", "abc", null)]
    [InlineData("from", "2026-10-02T99:99:99Z", null)]
    [InlineData("from", "2026-13-45", null)]
    [InlineData("to", null, "khong-phai-moc-thoi-gian")]
    [InlineData("to", null, "2026-10-02T99:99:99Z")]
    [InlineData("to", null, "2026-13-45")]
    public async Task Moc_thoi_gian_sai_dinh_dang_tra_400_kem_loi_theo_truong(
        string truong, string? from, string? to)
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        var route = await SeedRouteAsync(factory);

        var response = await client.GetAsync(SearchUrl(route.Id, from: from, to: to));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AssertCoLoiTheoTruong(await ReadJsonAsync(response), truong);
    }

    /// <summary>
    /// Mốc **thiếu múi giờ** — hợp đồng nói mốc phải có múi giờ, nhưng endpoint vẫn nhận. Ca này
    /// ghim lại cách hiểu hiện tại thay vì phán nó đúng hay sai: giá trị thiếu múi giờ được hiểu
    /// theo **giờ của máy chủ**, không phải UTC.
    ///
    /// Vì sao viết được thành test chạy đúng ở mọi nơi (CI là UTC, máy này là UTC+7): kỳ vọng được
    /// tính từ <see cref="TimeZoneInfo.Local"/> của chính máy đang chạy, không phải hằng số. Nếu
    /// một ngày backend đổi sang hiểu là UTC, ca này đỏ ngay — đó chính là điều cần biết.
    ///
    /// ⚠️ Đây là ĐIỂM RỦI RO, không phải tính năng: cùng một chuỗi <c>2026-10-02T08:00:00</c> cho
    /// ra hai khoảnh khắc khác nhau tuỳ máy chủ đặt ở đâu. Màn hình phải luôn gửi kèm múi giờ.
    /// </summary>
    [Fact]
    public async Task Moc_thieu_mui_gio_duoc_hieu_theo_gio_may_chu()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        var route = await SeedRouteAsync(factory);
        var bus = await SeedBusAsync(factory);

        // Chuyến 01:00Z — tức 08:00 giờ Việt Nam, nhưng cũng là 08:00 giờ máy chủ UTC.
        await SeedTripAsync(factory, route.Id, bus.Id, Gio(1));

        // "08:00 không kèm múi giờ" → máy chủ hiểu theo giờ của nó.
        var cachHieuCuaMayChu = Gio(8) - TimeZoneInfo.Local.GetUtcOffset(Gio(8));

        var body = await ReadJsonAsync(await client.GetAsync(
            SearchUrl(route.Id, from: "2026-10-02T08:00:00")));

        // Hệ quả quan sát được: chuyến 01:00Z nằm TRƯỚC mốc nếu máy chủ ở Đông Nam Á (mốc tương
        // đương 01:00Z), và nằm ĐÚNG mốc nếu máy chủ ở UTC.
        var mongDoi = cachHieuCuaMayChu <= Gio(1) ? 1 : 0;

        Assert.Equal(mongDoi, body.GetArrayLength());
    }

    // =======================================================================================
    // 15. Khoảng thời gian bắc qua nửa đêm
    // =======================================================================================

    /// <summary>
    /// Hợp đồng không giới hạn khoảng hỏi trong một ngày: lọc "theo ngày" chỉ là cách màn hình gửi
    /// <c>from</c>/<c>to</c> của trọn ngày, còn endpoint chỉ so hai mốc. Chuyến đêm (22:00 hôm
    /// trước → 02:00 hôm sau) là ca mà một bản cài đặt lọc theo "ngày" sẽ làm rơi mất.
    /// </summary>
    [Fact]
    public async Task Khoang_bac_qua_nua_dem_van_loc_dung()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        var route = await SeedRouteAsync(factory);
        var bus = await SeedBusAsync(factory);

        var dem1 = Gio(22);
        var sangHomSau = DauNgay.AddDays(1).AddHours(2);   // 02:00 ngày hôm sau
        var truaHomSau = DauNgay.AddDays(1).AddHours(6);   // 06:00 ngày hôm sau

        await SeedTripAsync(factory, route.Id, bus.Id, dem1);
        await SeedTripAsync(factory, route.Id, bus.Id, sangHomSau);
        await SeedTripAsync(factory, route.Id, bus.Id, truaHomSau);

        var body = await ReadJsonAsync(await client.GetAsync(
            SearchUrl(route.Id, from: IsoZ(dem1), to: IsoZ(sangHomSau))));

        // Hai chuyến nằm trong khoảng (tính luôn cả hai mốc); chuyến 06:00 hôm sau bị loại.
        Assert.Equal(
            [dem1, sangHomSau],
            body.EnumerateArray().Select(dong => dong.GetProperty("departureTime").GetDateTime()));
    }

    // =======================================================================================
    // 16. Không phân trang — hỏi bao nhiêu trả bấy nhiêu
    // =======================================================================================

    /// <summary>
    /// Hợp đồng: *"Trả về mảng trần, không phân trang: một tuyến trong một khoảng ngày là vài chục
    /// chuyến — màn hình đọc hết để tự sắp xếp theo giờ/giá."*
    ///
    /// Ca này vượt qua mọi con số mặc định hay gặp của phân trang (10 / 20) để nếu ai đó lỡ gắn
    /// <c>pageSize</c> mặc định vào truy vấn thì 5 chuyến cuối biến mất — và màn hình kết quả sẽ
    /// im lặng bỏ sót chuyến mà hành khách không có cách nào biết.
    /// </summary>
    [Fact]
    public async Task Tra_het_chuyen_trong_khoang_khong_bi_cat_bot()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        var route = await SeedRouteAsync(factory);
        var bus = await SeedBusAsync(factory);

        const int soChuyen = 25;

        for (var i = 0; i < soChuyen; i++)
        {
            await SeedTripAsync(factory, route.Id, bus.Id, Gio(0).AddMinutes(i * 20));
        }

        var body = await ReadJsonAsync(await client.GetAsync(
            SearchUrl(route.Id, from: IsoZ(DauNgay), to: IsoZ(DauNgay.AddDays(1)))));

        Assert.Equal(soChuyen, body.GetArrayLength());
    }

    // =======================================================================================
    // Helper — mỗi lớp test tự chép, không có lớp base chung (đúng lệ đang có của dự án)
    // =======================================================================================

    /// <summary>
    /// Dựng URL tra cứu. Tham số nhận **chuỗi thô** chứ không phải <c>Guid</c>/<c>DateTimeOffset</c>
    /// để gửi được cả dữ liệu hỏng ("abc", "{...}", chuỗi rỗng) — đó là một nửa không gian đầu vào
    /// mà lớp test này sinh ra để quét. <c>null</c> = không gửi tham số đó.
    /// </summary>
    private static string SearchUrl(
        object? routeId = null,
        string? from = null,
        string? to = null,
        string? extra = null)
    {
        var phan = new List<string>();

        if (routeId is not null) phan.Add($"routeId={Uri.EscapeDataString(routeId.ToString()!)}");
        if (from is not null) phan.Add($"from={Uri.EscapeDataString(from)}");
        if (to is not null) phan.Add($"to={Uri.EscapeDataString(to)}");
        if (extra is not null) phan.Add(extra);

        return phan.Count == 0 ? "/api/trips/search" : $"/api/trips/search?{string.Join("&", phan)}";
    }

    /// <summary>Mốc ISO 8601 UTC có hậu tố Z — cách viết mà màn hình gửi lên sau khi quy đổi.</summary>
    private static string IsoZ(DateTime value)
        => value.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    /// <summary>
    /// Khẳng định khuôn lỗi D3: <c>errors</c> có đúng khoá của trường sai, và khoá đó mang ít nhất
    /// một câu lỗi. Sai tên khoá là frontend không tô đỏ được ô nhập tương ứng.
    /// </summary>
    private static void AssertCoLoiTheoTruong(JsonElement body, string truong)
    {
        Assert.True(body.TryGetProperty("errors", out var errors), "Lỗi phải có khoá 'errors' (quy ước D3).");

        Assert.True(
            errors.TryGetProperty(truong, out var danhSach),
            $"errors phải có khoá '{truong}'. Thực tế: {errors}");

        Assert.NotEmpty(danhSach.EnumerateArray());
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
        => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task<string> MessageAsync(HttpResponseMessage response)
        => (await ReadJsonAsync(response)).GetProperty("message").GetString() ?? string.Empty;

    private static async Task<RouteEntity> SeedRouteAsync(
        TestAppFactory factory,
        string code = "01",
        string name = "Bến xe Mỹ Đình — Bến xe Gia Lâm")
    {
        var route = new RouteEntity
        {
            Id = Guid.NewGuid(),
            Code = code,
            Name = name,
            Origin = "Bến xe Mỹ Đình",
            Destination = "Bến xe Gia Lâm",
        };

        await factory.SeedAsync(db => db.Routes.Add(route));

        return route;
    }

    private static async Task<Bus> SeedBusAsync(
        TestAppFactory factory,
        string licensePlate = "29B-123.45",
        string busType = "Xe buýt 45 chỗ",
        int capacity = 45)
    {
        var bus = new Bus
        {
            Id = Guid.NewGuid(),
            LicensePlate = licensePlate,
            BusType = busType,
            Capacity = capacity,
            Status = BusStatus.Active,
        };

        await factory.SeedAsync(db => db.Buses.Add(bus));

        return bus;
    }

    /// <summary>
    /// <paramref name="chuaChotGioDen"/> = true để cột <c>ArrivalTime</c> là <c>null</c> thật.
    /// Cố ý không dùng <c>DateTime? arrivalTime = null</c>: tham số mặc định null không phân biệt
    /// được "chưa chốt giờ đến" với "không truyền, cho giờ mặc định" — chính chỗ đó làm một ca của
    /// lớp test này đỏ oan ở lần chạy đầu.
    /// </summary>
    private static async Task<Trip> SeedTripAsync(
        TestAppFactory factory,
        Guid routeId,
        Guid busId,
        DateTime departureTime,
        bool chuaChotGioDen = false,
        int phutChay = 45,
        TripStatus status = TripStatus.Scheduled,
        Guid? id = null)
    {
        // Chỉ gán khoá ngoại, KHÔNG gán navigation: Route/Bus được seed ở scope khác, gán navigation
        // vào đây sẽ khiến EF tưởng chúng là bản ghi mới và chèn trùng khoá chính.
        var trip = new Trip
        {
            Id = id ?? Guid.NewGuid(),
            RouteId = routeId,
            BusId = busId,
            DepartureTime = departureTime,
            ArrivalTime = chuaChotGioDen ? null : departureTime.AddMinutes(phutChay),
            Status = status,
        };

        await factory.SeedAsync(db => db.Trips.Add(trip));

        return trip;
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

    /// <summary>
    /// Tài khoản mang một vai trò, đủ để <c>ITokenService</c> phát được access token — token chỉ
    /// cần đúng hình dạng, vì endpoint này không đọc claim nào.
    /// </summary>
    private static async Task<UserEntity> SeedUserAsync(TestAppFactory factory, string roleCode)
    {
        var roleId = Guid.NewGuid();

        await factory.SeedAsync(db =>
        {
            if (!db.Roles.Any(r => r.Code == roleCode))
            {
                db.Roles.Add(new Role { Id = roleId, Code = roleCode, Name = roleCode });
            }
        });

        var user = new UserEntity
        {
            Id = Guid.NewGuid(),
            PhoneNumber = "09" + Random.Shared.Next(10_000_000, 99_999_999),
            FullName = "Khách tra cứu",
            PasswordHash = PasswordService.HashPassword("matkhau123"),
            IsActive = true,
            RoleId = roleId,
            UserRoles = [new UserRole { RoleId = roleId }],
        };

        await factory.SeedAsync(db => db.Users.Add(user));

        return user;
    }
}
