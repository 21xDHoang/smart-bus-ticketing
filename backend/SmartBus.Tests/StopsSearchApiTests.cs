using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SmartBus.Api.Entities;
using SmartBus.Api.Services;
using UserEntity = SmartBus.Api.Entities.User;

namespace SmartBus.Tests;

/// <summary>
/// Test cho <c>GET /api/stops/search</c> — task *"API gợi ý trạm dừng theo từ khoá (autocomplete)"*
/// (Sprint 2 dòng 29, Hoàng Văn Thịnh). Hợp đồng đầy đủ ở mục "Tra cứu trạm dừng — /stops/search"
/// của docs/api-contract.md.
///
/// Ba thứ lớp test này khoá, theo thứ tự quan trọng:
///
/// 1. <b>Bỏ dấu</b> — hành khách gõ trên điện thoại gần như luôn không gõ dấu ("cau giay"), còn
///    trạm lưu tên CÓ dấu. Đây là lý do endpoint tồn tại; chỉ <c>ToLower()</c> là không đủ, và
///    riêng chữ <c>đ</c> còn không tách được bằng NFD nên phải thay tay.
/// 2. <b>Công khai</b> — hành khách gõ từ khoá TRƯỚC khi đăng nhập. Kèm một ca khẳng định bề mặt
///    CRUD <c>/api/stops</c> của quản lý VẪN còn khoá, để không ai "mở luôn cho tiện".
/// 3. <b>Thứ tự ổn định</b> — hai lần gọi cùng tham số phải ra cùng kết quả. <c>List.Sort</c>
///    không ổn định, nên nếu ai đó bỏ khoá chốt cuối theo <c>Id</c> thì các trạm cùng hạng/trùng
///    tên sẽ đổi chỗ giữa hai lượt gọi và ca này đỏ.
///
/// Mỗi lớp test tự chép helper của mình — đúng lệ đang có của dự án (không có lớp base chung).
/// </summary>
public class StopsSearchApiTests
{
    // =======================================================================================
    // 1. API công khai — không token vẫn gọi được
    // =======================================================================================

    /// <summary>
    /// Hợp đồng: endpoint <b>CÔNG KHAI</b>. Không gửi token phải là 200 — hành khách chọn trạm
    /// trước khi đăng nhập. Ca này cũng ghim luôn việc route không bị <c>{id:guid}</c> của
    /// <c>StopsController</c> nuốt: nuốt thì kết quả là 400 (bind GUID hỏng), không phải 200.
    /// </summary>
    [Fact]
    public async Task Khong_token_van_goi_duoc()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        await SeedStopAsync(factory, "Trạm Cầu Giấy", "Số 1 Cầu Giấy, Hà Nội");

        var response = await client.GetAsync("/api/stops/search");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// Vế còn lại của "công khai": <b>có</b> token (kể cả vai trò thấp nhất) cũng ra đúng kết quả
    /// đó — endpoint công khai thì không được đổi hành vi theo người gọi.
    /// </summary>
    [Fact]
    public async Task Co_token_hay_khong_thi_ket_qua_giong_het_nhau()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        await SeedStopAsync(factory, "Trạm Cầu Giấy", "Số 1 Cầu Giấy, Hà Nội");

        var khongToken = await ReadJsonAsync(await client.GetAsync(SearchUrl(keyword: "cau giay")));

        var khach = await SeedUserAsync(factory, "Passenger");
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", factory.CreateTokenFor(khach));

        var coToken = await ReadJsonAsync(await client.GetAsync(SearchUrl(keyword: "cau giay")));

        Assert.Equal(khongToken.GetRawText(), coToken.GetRawText());
    }

    /// <summary>
    /// Canh gác đi kèm: mở endpoint gợi ý KHÔNG được làm hở bề mặt CRUD <c>/api/stops</c> của quản
    /// lý. Cách "sửa" hấp dẫn nhưng sai là gỡ <c>[Authorize]</c> ở mức lớp của <c>StopsController</c>
    /// — ca này đỏ ngay nếu ai đó làm thế.
    /// </summary>
    [Fact]
    public async Task Be_mat_CRUD_tram_cua_quan_ly_van_con_khoa()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/stops");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // =======================================================================================
    // 2. Bỏ dấu + bỏ hoa thường — lý do endpoint tồn tại
    // =======================================================================================

    /// <summary>
    /// Cùng một trạm, sáu cách gõ khác nhau — tất cả phải ra đúng nó. Bảng quét các dạng mà người
    /// dùng thật sự gõ: không dấu, có dấu, HOA, khoảng trắng thừa hai đầu và ở giữa.
    ///
    /// Khẳng định cả <c>name</c> trả về: kết quả phải là tên CÓ DẤU như trong CSDL (để hiển thị và
    /// để gửi tiếp cho <c>GET /routes/search</c>), chứ không phải chuỗi đã chuẩn hoá dùng nội bộ.
    /// </summary>
    [Theory]
    [InlineData("cau giay")]
    [InlineData("Cầu Giấy")]
    [InlineData("CẦU GIẤY")]
    [InlineData("  cau   giay  ")]
    [InlineData("CÂU GIẤY")]
    [InlineData("cau giay ")]
    public async Task Go_khong_dau_hay_co_dau_deu_ra_cung_tram(string keyword)
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        await SeedStopAsync(factory, "Trạm Cầu Giấy", "Số 1 Cầu Giấy, Hà Nội");
        await SeedStopAsync(factory, "Bến xe Mỹ Đình", "Số 20 Phạm Hùng, Nam Từ Liêm, Hà Nội");

        var body = await ReadJsonAsync(await client.GetAsync(SearchUrl(keyword: keyword)));

        var dong = Assert.Single(body.EnumerateArray());
        Assert.Equal("Trạm Cầu Giấy", dong.GetProperty("name").GetString());
    }

    /// <summary>
    /// Chữ <b>đ</b> là ca riêng, không nằm chung bảng trên: <c>đ</c> (U+0111) là một CHỮ CÁI độc
    /// lập chứ không phải "d" cộng dấu, nên NFD không tách được nó. Ai bỏ sót bước thay tay
    /// <c>đ → d</c> thì đúng ca này đỏ, còn cả bảng trên vẫn xanh — nên nó phải đứng riêng.
    /// </summary>
    [Theory]
    [InlineData("dong da")]
    [InlineData("Đống Đa")]
    [InlineData("DONG DA")]
    public async Task Chu_d_móc_khớp_voi_d_thuong(string keyword)
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        await SeedStopAsync(factory, "Trạm Đống Đa", "Số 1 Tôn Đức Thắng, Đống Đa, Hà Nội");

        var body = await ReadJsonAsync(await client.GetAsync(SearchUrl(keyword: keyword)));

        var dong = Assert.Single(body.EnumerateArray());
        Assert.Equal("Trạm Đống Đa", dong.GetProperty("name").GetString());
    }

    /// <summary>
    /// Khớp cả <b>địa chỉ</b>: người chỉ nhớ tên đường ("Hai Bà Trưng") vẫn chọn được đúng trạm.
    /// Trạm dưới đây có tên hoàn toàn không chứa từ khoá — chỉ địa chỉ chứa — nên nếu ai đó bỏ
    /// nhánh khớp địa chỉ thì kết quả rỗng.
    /// </summary>
    [Fact]
    public async Task Khop_ca_dia_chi_chu_khong_chi_ten_tram()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        await SeedStopAsync(factory, "Trạm Bách Khoa", "Số 1 Đại Cồ Việt, Hai Bà Trưng, Hà Nội");
        await SeedStopAsync(factory, "Trạm Kim Mã", "Số 1 Kim Mã, Ba Đình, Hà Nội");

        var body = await ReadJsonAsync(await client.GetAsync(SearchUrl(keyword: "hai ba trung")));

        var dong = Assert.Single(body.EnumerateArray());
        Assert.Equal("Trạm Bách Khoa", dong.GetProperty("name").GetString());
    }

    /// <summary>
    /// Không trạm nào khớp → <c>[]</c> kèm 200, <b>không</b> phải 404 — bộ lọc mềm, cùng lối
    /// <c>GET /routes/search</c>. Trả 404 ở đây sẽ khiến ô gợi ý hiện lỗi đỏ mỗi lần hành khách gõ
    /// dở một chữ.
    /// </summary>
    [Theory]
    [InlineData("zzz")]
    [InlineData("khong-co-tram-nao-ten-nhu-vay")]
    [InlineData("   ")] // chỉ khoảng trắng — là "rỗng", không phải "không khớp"
    public async Task Khong_khop_gi_tra_mang_rong_chu_khong_phai_404(string keyword)
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        await SeedStopAsync(factory, "Trạm Cầu Giấy", "Số 1 Cầu Giấy, Hà Nội");

        var response = await client.GetAsync(SearchUrl(keyword: keyword));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // "   " rơi vào nhánh duyệt danh sách nên KHÔNG rỗng — tách ra khỏi hai ca kia.
        var mongDoi = string.IsNullOrWhiteSpace(keyword) ? 1 : 0;
        Assert.Equal(mongDoi, (await ReadJsonAsync(response)).GetArrayLength());
    }

    // =======================================================================================
    // 3. Xếp hạng — tên-đầu > tên-chứa > địa-chỉ
    // =======================================================================================

    /// <summary>
    /// Hợp đồng: *"tên trạm bắt đầu bằng từ khoá trước, rồi tên chứa từ khoá, cuối cùng là trạm
    /// chỉ khớp địa chỉ; trong cùng một hạng xếp theo tên tăng dần (chuẩn vi)"*.
    ///
    /// Bộ dữ liệu dựng để mỗi hạng có mặt và hạng giữa có BA trạm (để phần "trong cùng hạng xếp
    /// theo tên" cũng được kiểm chứ không chỉ ba nhóm rời):
    ///
    ///   hạng 0 — "Giảng Võ"              (tên bắt đầu bằng "gia")
    ///   hạng 1 — "Bến xe Gia Lâm"        (tên chứa "gia")
    ///            "Trạm Cầu Giấy"
    ///            "Trạm Giảng Võ"
    ///   hạng 2 — "Trạm Đại Học"          (chỉ ĐỊA CHỈ chứa "gia": "Số 5 Giảng Võ")
    ///
    /// Trạm "Trạm Kim Mã" cố ý không dính từ khoá nào — đối chứng rằng phép lọc vẫn lọc.
    /// </summary>
    [Fact]
    public async Task Xep_hang_ten_dau_roi_ten_chua_roi_dia_chi()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        await SeedStopAsync(factory, "Trạm Giảng Võ", "Số 148 Giảng Võ, Ba Đình, Hà Nội");
        await SeedStopAsync(factory, "Trạm Cầu Giấy", "Số 1 Cầu Giấy, Hà Nội");
        await SeedStopAsync(factory, "Bến xe Gia Lâm", "Số 9 Ngô Gia Khảm, Long Biên, Hà Nội");
        await SeedStopAsync(factory, "Trạm Kim Mã", "Số 1 Kim Mã, Ba Đình, Hà Nội");
        await SeedStopAsync(factory, "Giảng Võ", "Số 148 Giảng Võ, Ba Đình, Hà Nội");
        await SeedStopAsync(factory, "Trạm Đại Học", "Số 5 Giảng Võ, Ba Đình, Hà Nội");

        var body = await ReadJsonAsync(await client.GetAsync(SearchUrl(keyword: "gia")));

        Assert.Equal(
            ["Giảng Võ", "Bến xe Gia Lâm", "Trạm Cầu Giấy", "Trạm Giảng Võ", "Trạm Đại Học"],
            body.EnumerateArray().Select(dong => dong.GetProperty("name").GetString()));
    }

    /// <summary>
    /// Từ khoá khớp <b>giữa</b> tên trạm vẫn ra — không chỉ khớp đầu tên. Đối chứng cho nhánh
    /// <c>StartsWith</c> ở trên: nếu ai đó chỉ để lại <c>StartsWith</c> thì ca này rỗng.
    /// </summary>
    [Fact]
    public async Task Khop_giua_ten_tram_van_ra()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        await SeedStopAsync(factory, "Trạm Cầu Giấy", "Số 1 Cầu Giấy, Hà Nội");

        var body = await ReadJsonAsync(await client.GetAsync(SearchUrl(keyword: "giay")));

        Assert.Single(body.EnumerateArray());
    }

    // =======================================================================================
    // 4. Từ khoá rỗng = duyệt danh sách (ô CHỌN trạm, không phải ô TÌM)
    // =======================================================================================

    /// <summary>
    /// Hợp đồng: *"Bỏ trống hoặc toàn khoảng trắng = lấy đầu danh sách"*. Hành khách vừa bấm vào ô
    /// phải thấy ngay vài trạm để chọn — khác <c>GET /routes/search</c>, nơi thiếu từ khoá là 400.
    /// </summary>
    [Theory]
    [InlineData(null)]  // không gửi tham số nào
    [InlineData("")]    // gửi keyword rỗng
    [InlineData("   ")] // toàn khoảng trắng
    public async Task Tu_khoa_rong_hoac_bo_trong_deu_tra_dau_danh_sach(string? keyword)
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        await SeedStopAsync(factory, "Trạm Cầu Giấy", "Số 1 Cầu Giấy, Hà Nội");
        await SeedStopAsync(factory, "Bến xe Mỹ Đình", "Số 20 Phạm Hùng, Hà Nội");
        await SeedStopAsync(factory, "Trạm Kim Mã", "Số 1 Kim Mã, Hà Nội");

        var body = await ReadJsonAsync(await client.GetAsync(SearchUrl(keyword: keyword)));

        // Xếp theo tên (chuẩn vi): "Bến xe…" < "Trạm Cầu…" < "Trạm Kim…".
        Assert.Equal(
            ["Bến xe Mỹ Đình", "Trạm Cầu Giấy", "Trạm Kim Mã"],
            body.EnumerateArray().Select(dong => dong.GetProperty("name").GetString()));
    }

    /// <summary>
    /// Trần mặc định là 10: gieo 12 trạm rồi hỏi mà không gửi <c>limit</c> — chỉ được trả 10 dòng
    /// ĐẦU theo tên. Dropdown gợi ý chỉ đọc được chừng đó dòng.
    /// </summary>
    [Fact]
    public async Task Mac_dinh_lay_toi_da_10_goi_y()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        for (var i = 1; i <= 12; i++)
        {
            await SeedStopAsync(factory, $"Trạm số {i:00}", $"Số {i} Đường Mẫu, Hà Nội");
        }

        var body = await ReadJsonAsync(await client.GetAsync(SearchUrl()));

        Assert.Equal(10, body.GetArrayLength());
    }

    // =======================================================================================
    // 5. limit — cắt đúng, và ngoài 1..50 là lỗi GỌI
    // =======================================================================================

    /// <summary>Hợp đồng: cắt đúng <c>limit</c> dòng, ở cả chế độ có từ khoá lẫn duyệt danh sách.</summary>
    [Theory]
    [InlineData(1, null)]
    [InlineData(3, null)]
    [InlineData(50, null)]
    [InlineData(2, "tram")]
    [InlineData(50, "tram")]
    public async Task Limit_cat_dung_so_dong(int limit, string? keyword)
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        for (var i = 1; i <= 5; i++)
        {
            await SeedStopAsync(factory, $"Trạm số {i:00}", $"Số {i} Đường Mẫu, Hà Nội");
        }

        var body = await ReadJsonAsync(await client.GetAsync(SearchUrl(keyword, limit.ToString())));

        Assert.Equal(Math.Min(limit, 5), body.GetArrayLength());
    }

    /// <summary>
    /// Hợp đồng: <c>limit</c> ngoài 1..50 → 400 kèm <c>errors.limit</c>. Bảng quét cả hai phía
    /// ngoài biên và các cách viết hỏng — mỗi kiểu đi qua một nhánh khác nhau (bind hỏng so với
    /// bind được nhưng ngoài khoảng).
    ///
    /// Vì sao phải ồn ào chứ không kẹp về biên: màn hình gửi <c>limit</c> là gửi ý định của mình;
    /// im lặng sửa thành 10 thì người sửa mãi không biết mình gửi sai.
    /// </summary>
    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("51")]
    [InlineData("1000")]
    [InlineData("abc")]
    [InlineData("3.5")]
    public async Task Limit_ngoai_khoang_1_50_tra_400_kem_loi_o_truong_limit(string limit)
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        await SeedStopAsync(factory, "Trạm Cầu Giấy", "Số 1 Cầu Giấy, Hà Nội");

        var response = await client.GetAsync(SearchUrl(limit: limit));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        // D3: khuôn lỗi { message, errors }, khoá theo tên trường camelCase — frontend đọc thẳng
        // khoá đó để tô đỏ ô nhập.
        var body = await ReadJsonAsync(response);

        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("message").GetString()));
        AssertCoLoiTheoTruong(body, "limit");
    }

    /// <summary>
    /// Đối chứng của bảng trên: <c>limit=</c> rỗng (client nối query string cẩu thả) KHÔNG phải
    /// "số sai" mà là "không gửi" — lấy mặc định 10 chứ không 400.
    /// </summary>
    [Fact]
    public async Task Limit_rong_duoc_coi_nhu_khong_gui()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        await SeedStopAsync(factory, "Trạm Cầu Giấy", "Số 1 Cầu Giấy, Hà Nội");

        var response = await client.GetAsync("/api/stops/search?limit=");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, (await ReadJsonAsync(response)).GetArrayLength());
    }

    // =======================================================================================
    // 6. Thứ tự ổn định — List.Sort không ổn định nên khoá chốt cuối phải là Id
    // =======================================================================================

    /// <summary>
    /// Hai trạm <b>trùng tên</b> (khác địa chỉ) — cùng hạng, cùng tên, nên chỉ còn <c>Id</c> phân
    /// biệt. Id đặt ngược thứ tự chèn để nếu ai đó bỏ khoá chốt cuối, thứ tự rơi về thứ tự chèn và
    /// ca này đỏ.
    /// </summary>
    [Fact]
    public async Task Trung_ten_thi_xep_theo_Id()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        var idLon = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");
        var idNho = Guid.Parse("00000000-0000-0000-0000-000000000001");

        await SeedStopAsync(factory, "Trạm Trùng Tên", "Số 2 Đường B, Hà Nội", id: idLon);
        await SeedStopAsync(factory, "Trạm Trùng Tên", "Số 1 Đường A, Hà Nội", id: idNho);

        var body = await ReadJsonAsync(await client.GetAsync(SearchUrl(keyword: "trung ten")));

        Assert.Equal(
            [idNho, idLon],
            body.EnumerateArray().Select(dong => dong.GetProperty("id").GetGuid()));
    }

    /// <summary>
    /// Hợp đồng: *"Hai lần gọi cùng tham số cho ra cùng một kết quả"*. Gieo nhiều trạm cùng hạng
    /// để có cơ hội lộ thứ tự không ổn định, rồi so hai lượt gọi.
    /// </summary>
    [Fact]
    public async Task Hai_lan_goi_cung_tham_so_ra_cung_ket_qua()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        for (var i = 0; i < 8; i++)
        {
            await SeedStopAsync(factory, "Trạm Trùng Tên", $"Số {i} Đường Mẫu, Hà Nội");
        }

        var lan1 = await ReadJsonAsync(await client.GetAsync(SearchUrl(keyword: "trung")));
        var lan2 = await ReadJsonAsync(await client.GetAsync(SearchUrl(keyword: "trung")));

        Assert.Equal(lan1.GetRawText(), lan2.GetRawText());
    }

    // =======================================================================================
    // 7. Hình dạng response — đúng 5 trường, không thêm không bớt
    // =======================================================================================

    /// <summary>
    /// Hợp đồng: *"Trả về đúng entity <c>Stop</c> của mục Trạm dừng — không thêm trường nào"*, và
    /// kiểu <c>Stop</c> bên frontend (frontend/src/api/stopApi.ts) đã chốt đúng 5 trường. Ca này
    /// ghim cả TÊN trường, để <c>createdAt</c>/<c>updatedAt</c> lọt ra là đỏ ngay.
    /// </summary>
    [Fact]
    public async Task Tra_ve_dung_5_truong_khong_kem_createdAt_updatedAt()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        await SeedStopAsync(factory, "Trạm Cầu Giấy", "Số 1 Cầu Giấy, Hà Nội", latitude: 21.0307, longitude: 105.8034);

        var dong = Assert.Single((await ReadJsonAsync(await client.GetAsync(SearchUrl(keyword: "cau giay")))).EnumerateArray());

        Assert.Equal(
            ["address", "id", "latitude", "longitude", "name"],
            dong.EnumerateObject().Select(t => t.Name).OrderBy(ten => ten, StringComparer.Ordinal));

        // Toạ độ là double precision (quy ước A3) — đọc ra phải đúng con số đã gieo.
        Assert.Equal(21.0307, dong.GetProperty("latitude").GetDouble());
        Assert.Equal(105.8034, dong.GetProperty("longitude").GetDouble());
    }

    // =======================================================================================
    // 8. Bảng trạm rỗng
    // =======================================================================================

    /// <summary>
    /// Chưa có trạm nào trong CSDL (hệ thống mới dựng) → <c>[]</c> kèm 200. Không được 500 hay 404:
    /// màn hình đang mở phải chịu được trạng thái dữ liệu rỗng.
    /// </summary>
    [Fact]
    public async Task Bang_tram_rong_tra_mang_rong()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync(SearchUrl(keyword: "cau giay"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, (await ReadJsonAsync(response)).GetArrayLength());
    }

    // =======================================================================================
    // Helper — mỗi lớp test tự chép, không có lớp base chung (đúng lệ đang có của dự án)
    // =======================================================================================

    /// <summary>
    /// Dựng URL gợi ý trạm. Tham số nhận <b>chuỗi thô</b> để gửi được cả dữ liệu hỏng ("abc",
    /// "3.5", chuỗi rỗng) — đó là một nửa không gian đầu vào mà lớp test này quét.
    /// <c>null</c> = không gửi tham số đó.
    /// </summary>
    private static string SearchUrl(string? keyword = null, string? limit = null)
    {
        var phan = new List<string>();

        if (keyword is not null) phan.Add($"keyword={Uri.EscapeDataString(keyword)}");
        if (limit is not null) phan.Add($"limit={Uri.EscapeDataString(limit)}");

        return phan.Count == 0 ? "/api/stops/search" : $"/api/stops/search?{string.Join("&", phan)}";
    }

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

    private static async Task<Stop> SeedStopAsync(
        TestAppFactory factory,
        string name,
        string address,
        double latitude = 21.0,
        double longitude = 105.0,
        Guid? id = null)
    {
        var stop = new Stop
        {
            Id = id ?? Guid.NewGuid(),
            Name = name,
            Address = address,
            Latitude = latitude,
            Longitude = longitude,
        };

        await factory.SeedAsync(db => db.Stops.Add(stop));

        return stop;
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
            FullName = "Khách chọn trạm",
            PasswordHash = PasswordService.HashPassword("matkhau123"),
            IsActive = true,
            RoleId = roleId,
            UserRoles = [new UserRole { RoleId = roleId }],
        };

        await factory.SeedAsync(db => db.Users.Add(user));

        return user;
    }
}
