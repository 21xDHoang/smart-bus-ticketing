using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using SmartBus.Api.Data;
using SmartBus.Api.Dtos.SeatHolds;
using SmartBus.Api.Entities;
using SmartBus.Api.Services;

// Bí danh cho hai thực thể trùng tên với kiểu phổ biến của ASP.NET (Route của Routing, User của
// claim) — cùng lối bí danh UserEntity đang dùng trong các file test khác của bộ test.
using RouteEntity = SmartBus.Api.Entities.Route;
using UserEntity = SmartBus.Api.Entities.User;

namespace SmartBus.Tests;

/// <summary>
/// Kiểm thử ĐỒNG THỜI cho luồng giữ ghế — task *"Kiểm thử đồng thời: 50 khách cùng giữ 1 ghế"*
/// (Product_Backlog_Smart_Bus.xlsx, sheet Sprint 3 dòng 25 — story 3 "Giữ chỗ tạm thời").
///
/// <para>
/// <b>Vì sao phải có file này.</b> Chốt chống hai khách giữ cùng một ghế của dự án KHÔNG nằm ở tầng
/// service — nó là một partial unique index ở tầng PostgreSQL (<c>docs/26 §3</c>,
/// <c>AppDbContext.Seat.cs</c>):
/// </para>
/// <code>
/// CREATE UNIQUE INDEX "IX_SeatHolds_TripId_SeatId" ON "SeatHolds" ("TripId", "SeatId")
///     WHERE "Status" = 'Holding';
/// </code>
/// <para>
/// Phép kiểm "ghế còn trống" trong <see cref="SeatHoldCreateService"/> CỐ Ý không đủ: hai request
/// cùng đọc thấy ghế trống rồi cùng ghi là chuyện xảy ra được, và <c>docs/26 §3</c> nói thẳng
/// *"Index này mới là thứ bảo đảm"*. Nhánh thua đâm vào index và nhận <c>DbUpdateException</c>, được
/// service đổi thành 409.
/// </para>
///
/// <para>
/// <b>Bộ test cũ KHÔNG chạm được nhánh đó</b> — <c>SeatHoldCreateApiTests</c> ghi rõ trong docstring
/// của nó: *"KHÔNG dựng unique index … nhánh hai request cùng giữ một ghế (bắt DbUpdateException trả
/// 409) chỉ chạy ở PostgreSQL, không phủ được ở đây"*. Lý do là
/// <c>Microsoft.EntityFrameworkCore.InMemory</c> không cưỡng chế unique index nào cả, còn máy trạm
/// lẫn CI đều không có PostgreSQL (không docker/psql; <c>.github/</c> là file của Hoàng). Nghĩa là
/// bất biến quan trọng nhất của nghiệp vụ bán vé — *một ghế chỉ có một người giữ* — trước file này
/// chưa từng được chạy thử lần nào.
/// </para>
///
/// <para>
/// <b>Cách giải quyết: SQLite.</b> Đây là engine DUY NHẤT chạy trong tiến trình mà cưỡng chế được
/// partial unique index, và nó dựng ra index <b>đúng nguyên văn</b> luật PostgreSQL khai (xem
/// <see cref="SQLite_dung_dung_luat_index_ma_PostgreSQL_khai"/>). Nên bài test ở đây chạy trên
/// <see cref="NhaMaySqlite"/> — app THẬT, middleware THẬT, JWT THẬT, service THẬT, chỉ khác provider
/// CSDL — và đỏ/xanh theo đúng thứ sẽ xảy ra ở PostgreSQL.
/// </para>
///
/// <para>
/// ⚠️ <b>Giới hạn còn lại.</b> SQLite không phải PostgreSQL: nó chưa từng chạy qua Npgsql, nên file
/// này không thay được một lượt chạy thật trên môi trường online — việc đó thuộc task *"Kiểm thử
/// chéo luồng giữ chỗ trên môi trường online"* (dòng 26). Nhóm 3 ở đây là cầu nối: nó khoá lại
/// rằng luật SQLite cưỡng chế trong test CHÍNH LÀ luật mô hình EF và migration khai cho PostgreSQL.
/// </para>
/// </summary>
public class SeatHoldRaceFlowApiTests
{
    private const string DuongGiuGhe = "/api/seat-holds";

    /// <summary>Con số của chính tên task: 50 khách cùng lúc.</summary>
    private const int SoKhach = 50;

    /// <summary>Migration Sprint 3 dựng bảng <c>SeatHolds</c> kèm partial unique index.</summary>
    private const string MigrationSeatHolds = "20261006122313_Sprint3_Seats_SeatLayouts_SeatHolds";

    private const string IndexChotGhe = "IX_SeatHolds_TripId_SeatId";

    /// <summary>Mệnh đề lọc của index — phần biến unique index thành PARTIAL (xem nhóm 2).</summary>
    private const string LocChotGhe = "\"Status\" = 'Holding'";

    /// <summary>
    /// Câu 409 của nhánh đua ở CSDL (<c>SeatHoldCreateService.SeatTakenRaceMessage</c>) — lúc thua
    /// index thì không còn biết ghế nào vừa bị giành nên câu là câu chung.
    /// </summary>
    private const string CauNhanhDua =
        "Một hoặc nhiều ghế vừa được người khác giữ, vui lòng chọn ghế khác.";

    /// <summary>Câu 409 của phép kiểm trước trong service, khi một ghế cụ thể đang bị giữ.</summary>
    private const string CauKemSoGhe = "Ghế A1 đang được giữ cho chuyến này.";

    // =======================================================================================
    // Nhóm 1 — 50 khách đua nhau qua HTTP THẬT
    // =======================================================================================

    /// <summary>
    /// Bất biến trung tâm của nghiệp vụ: 50 khách cùng giữ MỘT ghế thì đúng MỘT người thắng.
    ///
    /// <para>
    /// Ca này KHÔNG khẳng định request nào đi đường nào — nó khẳng định KẾT QUẢ, thứ phải đúng dù
    /// 50 request có bị xếp hàng hay không. Cổng đồng bộ chỉ để làm đua dễ xảy ra (tăng khả năng
    /// nhiều request cùng vượt qua phép kiểm trước rồi cùng ghi), không phải để dàn dựng kết quả.
    /// </para>
    /// <para>
    /// Ca này CỐ Ý không khẳng định phiên bản nào của câu 409 phải xuất hiện. 50 request xuất phát
    /// cùng lúc nhưng đường đi của chúng không do bài test quyết định: đo trên máy này, phần lớn lượt
    /// chạy có request thua đâm vào index (câu chung), nhưng có lượt cả 49 người thua đều đọc thấy
    /// ghế đã bị giữ nên phép kiểm trước bắt hết (câu kèm số ghế) — khoảng 1/14 lượt. Khẳng định
    /// "phải có câu chung" là khẳng định một chuyện đúng 93% số lần, tức là một bài test đỏ ngẫu
    /// nhiên, đúng thứ làm CI mất giá trị. Nhánh thua ở tầng CSDL được khẳng định TẤT ĐỊNH ở
    /// <see cref="Nhanh_thua_o_CSDL_tra_409_chung_va_khong_de_lai_dong_nao"/>.
    /// </para>
    /// <para>
    /// Phép khẳng định "không có 5xx" thì không phải cho đẹp: nếu ai đó gỡ nhánh <c>catch
    /// (DbUpdateException)</c> trong <see cref="SeatHoldCreateService"/>, người thua trong lượt chạy
    /// CÓ đua sẽ nhận 500 — lỗi thô của index lọt thẳng ra cho khách.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Nam_muoi_khach_cung_giu_mot_ghe_thi_dung_mot_nguoi_thang()
    {
        using var factory = new NhaMaySqlite();
        await factory.TaoCsdlAsync();

        var (trip, _, seats) = await SeedChuyenDayDuAsync(factory, soGhe: 1);
        var khach = await SeedKhachAsync(factory, SoKhach);

        var phanHoi = await DuaAsync(factory, khach, trip.Id, [seats[0].Id]);

        // Đúng một người thắng. Đây là cả nội dung của task, và là thứ mà InMemory không thể khẳng
        // định được: không có index thì cả 50 cùng ghi xong và cùng nhận 201.
        Assert.Equal(1, phanHoi.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(SoKhach - 1, phanHoi.Count(r => r.StatusCode == HttpStatusCode.Conflict));

        Assert.DoesNotContain(phanHoi, r => (int)r.StatusCode >= 500);

        var cauThua = new List<string>();
        foreach (var thua in phanHoi.Where(r => r.StatusCode == HttpStatusCode.Conflict))
        {
            var body = await ReadJsonAsync(thua);

            // Một khuôn lỗi DUY NHẤT cho mọi endpoint của dự án (docs/03-quy-uoc.md §5): người thua
            // phải nhận { message, errors }, không phải lỗi unique thô của Npgsql.
            Assert.Equal(["errors", "message"], PropertyNamesOf(body));

            cauThua.Add(body.GetProperty("message").GetString() ?? string.Empty);
        }

        // Cả hai câu 409 đều đã chốt: câu chung khi thua ở index, câu kèm số ghế khi phép kiểm trước
        // bắt được. Ca này không bắt buộc phải có mặt câu nào — nhưng KHÔNG được có câu thứ ba: một
        // câu lạ ở đây nghĩa là có đường trả lỗi mới mà không ai kiểm.
        Assert.All(cauThua, cau => Assert.Contains(cau, new[] { CauNhanhDua, CauKemSoGhe }));

        // Dấu vết dưới CSDL phải khớp: đúng một lượt giữ, đúng một dòng nhật ký. Nhật ký là chỗ dễ
        // sai nhất — 49 người thua mà mỗi người ghi một dòng thì câu hỏi "tài khoản này đã giữ chỗ
        // bao nhiêu lần" đếm sai.
        Assert.Equal(1, await DemLuotGiuAsync(factory, trip.Id, seats[0].Id));
        Assert.Equal(1, await DemNhatKyHeldAsync(factory));
    }

    /// <summary>
    /// Cùng một phiên giữ HAI ghế: người thắng được cả hai, người thua không để lại mảnh nào.
    ///
    /// <para>
    /// Ca này khoá lời hứa *"cả phiên ghi trong MỘT <c>SaveChangesAsync</c>"*
    /// (<see cref="SeatHoldCreateService"/>): nếu ai đó tách việc ghi thành nhiều lượt, một người
    /// thua có thể kịp giữ được ghế thứ nhất rồi đâm index ở ghế thứ hai — khách nhìn màn hình thấy
    /// mình đang giữ nửa phiên.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Nam_muoi_khach_cung_giu_mot_cap_hai_ghe_thi_khong_co_phien_do()
    {
        using var factory = new NhaMaySqlite();
        await factory.TaoCsdlAsync();

        var (trip, _, seats) = await SeedChuyenDayDuAsync(factory, soGhe: 2);
        var khach = await SeedKhachAsync(factory, SoKhach);

        var phanHoi = await DuaAsync(factory, khach, trip.Id, [seats[0].Id, seats[1].Id]);

        Assert.Equal(1, phanHoi.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(SoKhach - 1, phanHoi.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        Assert.DoesNotContain(phanHoi, r => (int)r.StatusCode >= 500);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var dangGiu = await db.SeatHolds
            .Where(h => h.TripId == trip.Id && h.Status == SeatHoldStatus.Holding)
            .ToListAsync();

        // Đúng hai dòng, và cả hai thuộc CÙNG một phiên — không có "1/2 phiên" nào sót lại.
        Assert.Equal(2, dangGiu.Count);
        Assert.Equal(2, dangGiu.Select(h => h.SeatId).Distinct().Count());
        Assert.Single(dangGiu.Select(h => h.SessionCode).Distinct());

        Assert.Equal(2, await DemNhatKyHeldAsync(factory));
    }

    // =======================================================================================
    // Nhóm 1b — nhánh thua ở index, dựng TẤT ĐỊNH (không phụ thuộc may rủi của 50 request)
    // =======================================================================================

    /// <summary>
    /// Nhánh thua ở tầng CSDL: <c>catch (DbUpdateException)</c> đổi lỗi unique thô của index thành
    /// 409 theo khuôn lỗi chung, và không để lại dòng nào của phiên thua.
    ///
    /// <para>
    /// <see cref="Nam_muoi_khach_cung_giu_mot_ghe_thi_dung_mot_nguoi_thang"/> chỉ chạm được khe thời
    /// gian này một cách may rủi, nên có lượt nó xanh mà nhánh <c>catch</c> chưa hề chạy — lúc đó nó
    /// chỉ đang chứng minh phép kiểm trước, đúng cái mà docstring của <c>SeatHoldCreateApiTests</c> đã
    /// nói là KHÔNG đủ. Ca này dựng lại khe đó bằng tay: một lượt giữ của người khác được ghi vào
    /// <b>giữa</b> phép kiểm "ghế còn trống" và <c>SaveChangesAsync</c> của người thua.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Nhanh_thua_o_CSDL_tra_409_chung_va_khong_de_lai_dong_nao()
    {
        using var factory = new NhaMaySqlite(chenGiua: true);
        await factory.TaoCsdlAsync();

        var (trip, _, seats) = await SeedChuyenDayDuAsync(factory, soGhe: 1);

        // batDau: 100 — tách hẳn dải số điện thoại khỏi các ca khác cho dễ đọc khi soi dữ liệu.
        var khach = await SeedKhachAsync(factory, 2, batDau: 100);

        // Người thua hỏi giữ ghế, đọc thấy ghế trống. Đối thủ bước vào ngay trước lượt ghi của người
        // thua — đúng khe mà ở môi trường thật hai request đồng thời tự tìm thấy nhau.
        factory.ChenGiua!.Nap(trip.Id, seats[0].Id, khach[1].Id);

        using var scope = factory.Services.CreateScope();
        var ketQua = await scope.ServiceProvider
            .GetRequiredService<ISeatHoldCreateService>()
            .CreateAsync(
                khach[0].Id,
                new CreateSeatHoldRequest { TripId = trip.Id, SeatIds = [seats[0].Id] });

        Assert.False(ketQua.Success);
        Assert.Equal(ServiceErrorKind.Conflict, ketQua.ErrorKind);

        // Câu CHUNG, không kèm số ghế: tới lúc này service không còn biết ghế nào vừa bị giành — nó
        // chỉ biết lượt ghi của mình đụng index. Và errors là null, đúng khuôn { message, errors }
        // mà mọi 409 của endpoint này trả về.
        Assert.Equal(CauNhanhDua, ketQua.Error);
        Assert.Null(ketQua.Errors);

        // Ghế chỉ còn MỘT lượt giữ — của đối thủ. Lượt của người thua đã rollback cùng lượt ghi hỏng.
        Assert.Equal(1, await DemLuotGiuAsync(factory, trip.Id, seats[0].Id));

        // Và nhật ký không có dòng Held nào: một phiên không thành hình thì không được để lại dấu vết
        // trong bảng mà job cảnh báo "giữ chỗ quá nhiều lần" đếm.
        Assert.Equal(0, await DemNhatKyHeldAsync(factory));
    }

    // =======================================================================================
    // Nhóm 2 — đối chứng ở tầng service: chốt không được chặn quá rộng
    // =======================================================================================

    /// <summary>
    /// 50 khách giữ 50 ghế KHÁC nhau thì cả 50 phải thắng.
    ///
    /// <para>
    /// Ca đối chứng cho ca đầu tiên, và không phải để cho vui: một index đặt sai cột — ví dụ unique
    /// trên <c>TripId</c> một mình, hay unique cứng quên mệnh đề <c>WHERE</c> — vẫn làm ca "50 người
    /// giữ 1 ghế" xanh, nhưng biến chuyến xe thành chỉ-bán-được-một-ghế. Chỉ ca này bắt được.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Nam_muoi_khach_giu_nam_muoi_ghe_khac_nhau_thi_ca_nam_muoi_deu_thang()
    {
        using var factory = new NhaMaySqlite();
        await factory.TaoCsdlAsync();

        var (trip, _, seats) = await SeedChuyenDayDuAsync(factory, soGhe: SoKhach);
        var khach = await SeedKhachAsync(factory, SoKhach);

        var ketQua = await DuaTangServiceAsync(factory, khach, trip.Id, seats);

        Assert.All(ketQua, r => Assert.True(r.Success, $"{SoKhach} khách giữ {SoKhach} ghế khác nhau mà có lượt bị chặn: {r.Error}"));

        Assert.Equal(SoKhach, await DemLuotGiuCuaChuyenAsync(factory, trip.Id));
        Assert.Equal(SoKhach, await DemNhatKyHeldAsync(factory));
    }

    /// <summary>
    /// Ghế đang có một lượt giữ CŨ đã <c>Released</c> thì không chặn ai — 50 khách đua nhau và vẫn
    /// đúng một người thắng.
    ///
    /// <para>
    /// Đây là ca phân biệt PARTIAL với unique cứng. Bỏ mệnh đề <c>WHERE "Status" = 'Holding'</c> đi
    /// thì dòng <c>Released</c> cũ đã chiếm sẵn cặp <c>(TripId, SeatId)</c> và CẢ 50 người đều đâm
    /// index — không ai thắng, tức là ghế vĩnh viễn không bán được cho ai sau một lần khách đổi ý.
    /// Index là partial không phải để cho gọn: nó là điều kiện để nhả ghế có tác dụng.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Ghe_co_luot_cu_da_Released_thi_khong_chan_nguoi_moi()
    {
        using var factory = new NhaMaySqlite();
        await factory.TaoCsdlAsync();

        var (trip, _, seats) = await SeedChuyenDayDuAsync(factory, soGhe: 1);
        var khachCu = await SeedKhachAsync(factory, 1);

        await SeedLuotGiuCuAsync(factory, khachCu[0].Id, trip.Id, seats[0].Id, SeatHoldStatus.Released);

        // batDau: 1 — khách cũ đã chiếm số điện thoại đầu tiên.
        var khach = await SeedKhachAsync(factory, SoKhach, batDau: 1);
        var phanHoi = await DuaAsync(factory, khach, trip.Id, [seats[0].Id]);

        Assert.Equal(1, phanHoi.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(SoKhach - 1, phanHoi.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        Assert.Equal(1, await DemLuotGiuAsync(factory, trip.Id, seats[0].Id));
    }

    /// <summary>
    /// Cùng ca trên với trạng thái <c>Expired</c> — lượt giữ do job quét hết hạn để lại. Cùng lý do
    /// như <see cref="Ghe_co_luot_cu_da_Released_thi_khong_chan_nguoi_moi"/>: nhả ghế tự động chỉ có
    /// nghĩa nếu dòng <c>Expired</c> thôi chặn ghế.
    /// </summary>
    [Fact]
    public async Task Ghe_co_luot_cu_da_Expired_thi_khong_chan_nguoi_moi()
    {
        using var factory = new NhaMaySqlite();
        await factory.TaoCsdlAsync();

        var (trip, _, seats) = await SeedChuyenDayDuAsync(factory, soGhe: 1);
        var khachCu = await SeedKhachAsync(factory, 1);

        await SeedLuotGiuCuAsync(factory, khachCu[0].Id, trip.Id, seats[0].Id, SeatHoldStatus.Expired);

        // batDau: 1 — khách cũ đã chiếm số điện thoại đầu tiên.
        var khach = await SeedKhachAsync(factory, SoKhach, batDau: 1);
        var phanHoi = await DuaAsync(factory, khach, trip.Id, [seats[0].Id]);

        Assert.Equal(1, phanHoi.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(1, await DemLuotGiuAsync(factory, trip.Id, seats[0].Id));
    }

    // =======================================================================================
    // Nhóm 3 — cầu nối: luật SQLite cưỡng chế CHÍNH LÀ luật PostgreSQL khai
    // =======================================================================================

    /// <summary>
    /// Mô hình EF của <see cref="SeatHold"/> khai đúng một index trên <c>(TripId, SeatId)</c>, nó là
    /// unique, VÀ có mệnh đề lọc theo <c>Status = 'Holding'</c>.
    /// </summary>
    [Fact]
    public void Mo_hinh_khai_index_partial_unique_dung_cot_va_dieu_kien()
    {
        using var db = TaoDbContextQuanHe();

        var index = Assert.Single(
            db.Model.FindEntityType(typeof(SeatHold))!.GetIndexes(), i => i.Properties.Select(p => p.Name).SequenceEqual(["TripId", "SeatId"]));

        Assert.True(index.IsUnique, "Index (TripId, SeatId) phải là unique — đây là chốt chống hai khách giữ cùng một ghế.");
        Assert.Equal(LocChotGhe, index.GetFilter());
    }

    /// <summary>
    /// Migration Sprint 3 mang mô hình đó xuống PostgreSQL: <c>CreateIndexOperation</c> đúng tên,
    /// <c>IsUnique</c>, và đúng mệnh đề lọc.
    ///
    /// <para>
    /// Ca này không thừa so với ca trên: mô hình khai mà migration không mang thì chốt không có thật
    /// ở CSDL — PostgreSQL chỉ biết những gì migration đã tạo.
    /// </para>
    /// </summary>
    [Fact]
    public void Migration_Sprint3_sinh_index_partial_unique()
    {
        using var db = TaoDbContextQuanHe();

        var operation = FindCreateIndexOperation(db, MigrationSeatHolds, IndexChotGhe);

        Assert.Equal("SeatHolds", operation.Table);
        Assert.Equal(["TripId", "SeatId"], operation.Columns);
        Assert.True(operation.IsUnique);
        Assert.Equal(LocChotGhe, operation.Filter);
    }

    /// <summary>
    /// SQLite thật sự dựng ra đúng luật mà mô hình và migration khai.
    ///
    /// <para>
    /// Không có ca này thì nhóm 1–2 chỉ nói về SQLite còn nhóm 3 chỉ nói về PostgreSQL, và <b>không
    /// ai nối hai bên lại</b>: một mệnh đề lọc viết khác đi, một index quên unique, đều có thể khiến
    /// bài test đua chạy trên một luật khác hẳn luật của môi trường thật mà vẫn xanh.
    /// </para>
    /// </summary>
    [Fact]
    public async Task SQLite_dung_dung_luat_index_ma_PostgreSQL_khai()
    {
        using var factory = new NhaMaySqlite();
        await factory.TaoCsdlAsync();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var connection = db.Database.GetDbConnection();
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT sql FROM sqlite_master WHERE type = 'index' AND name = $ten";
        command.Parameters.Add(new SqliteParameter("$ten", IndexChotGhe));

        var sql = (string?)await command.ExecuteScalarAsync();

        Assert.NotNull(sql);
        Assert.Contains("UNIQUE", sql);
        Assert.Contains("\"TripId\", \"SeatId\"", sql);
        Assert.Contains($"WHERE {LocChotGhe}", sql);
    }

    // =======================================================================================
    // Helper dựng dữ liệu — mỗi lớp test tự chép, không có lớp base chung (lệ đang có của dự án)
    // =======================================================================================

    /// <summary>
    /// Nền dữ liệu đầy đủ cho luồng chọn ghế: tuyến + xe + sơ đồ + chuyến + dàn ghế.
    ///
    /// <para>
    /// Khác <c>SeatHoldCreateApiTests</c> (seed chuyến với <c>RouteId</c> mồ côi vì InMemory không
    /// cưỡng chế khoá ngoại): SQLite CƯỠNG CHẾ khoá ngoại, nên mọi thứ phải là dòng thật và đúng thứ
    /// tự. Đó cũng là lý do <c>SeatLayout</c> phải khớp <c>BusType</c> của xe.
    /// </para>
    /// </summary>
    private static async Task<(Trip Trip, Bus Bus, List<Seat> Seats)> SeedChuyenDayDuAsync(
        NhaMaySqlite factory,
        int soGhe)
    {
        var route = new RouteEntity
        {
            Id = Guid.NewGuid(),
            Code = "01",
            Name = "Bến xe Mỹ Đình — Bến xe Gia Lâm",
            Origin = "Bến xe Mỹ Đình",
            Destination = "Bến xe Gia Lâm",
        };

        var bus = new Bus
        {
            Id = Guid.NewGuid(),
            LicensePlate = "29B-123.45",
            BusType = "Xe buýt 45 chỗ",
            Capacity = 45,
        };

        var layout = new SeatLayout
        {
            Id = Guid.NewGuid(),
            BusType = bus.BusType,
            NumberOfFloors = 1,
            RowsPerFloor = 9,
            ColumnsPerRow = 5,
            TotalSeats = 45,
            VipSeatPositions = string.Empty,
        };

        var seats = new List<Seat>();
        for (var i = 0; i < soGhe; i++)
        {
            seats.Add(new Seat
            {
                Id = Guid.NewGuid(),
                BusId = bus.Id,
                SeatLayoutId = layout.Id,
                Floor = 1,
                RowIndex = 1,
                ColumnIndex = i + 1,
                SeatNumber = $"A{i + 1}",
                SeatType = SeatType.Standard,
            });
        }

        var trip = new Trip
        {
            Id = Guid.NewGuid(),
            RouteId = route.Id,
            BusId = bus.Id,
            // Chuyến còn ở tương lai: SeatHoldCreateService chỉ chặn chuyến Cancelled/Completed,
            // không so giờ với hiện tại — nhưng một chuyến đã chạy xong mà vẫn bán được ghế là dữ
            // liệu test nhìn sai, dễ khiến người đọc sau tưởng service thiếu phép kiểm.
            DepartureTime = DateTime.UtcNow.AddDays(1),
        };

        await factory.SeedAsync(db =>
        {
            db.Routes.Add(route);
            db.Buses.Add(bus);
            db.SeatLayouts.Add(layout);
            db.Seats.AddRange(seats);
            db.Trips.Add(trip);
        });

        return (trip, bus, seats);
    }

    /// <summary>
    /// <paramref name="soLuong"/> tài khoản hành khách, đánh số từ <paramref name="batDau"/>.
    ///
    /// <para>
    /// <paramref name="batDau"/> phải tăng giữa các lượt gọi trong CÙNG một bài test: các ca "ghế có
    /// lượt cũ" seed một khách cũ rồi seed thêm 50 khách — <c>Users.PhoneNumber</c> là unique và
    /// SQLite cưỡng chế nó, nên đánh số lại từ đầu là đâm khoá ngoài ý muốn. Số điện thoại đánh tuần
    /// tự chứ không random (khác file anh em dùng <c>Random.Shared</c>): 50 số random vẫn có thể
    /// trùng nhau, và một bài test về khoá ghế đỏ vì số điện thoại trùng là bài test không nói được
    /// gì về thứ nó định nói.
    /// </para>
    /// <para>
    /// Vai trò được seed TRƯỚC, đúng thứ tự khoá ngoại <c>Users.RoleId</c>.
    /// </para>
    /// </summary>
    private static async Task<List<UserEntity>> SeedKhachAsync(
        NhaMaySqlite factory,
        int soLuong,
        int batDau = 0)
    {
        await factory.SeedAsync(db =>
        {
            if (!db.Roles.Any(r => r.Id == RoleIds.Passenger))
            {
                db.Roles.Add(new Role { Id = RoleIds.Passenger, Code = RoleCodes.Passenger, Name = RoleCodes.Passenger });
            }
        });

        var khach = new List<UserEntity>();
        for (var i = 0; i < soLuong; i++)
        {
            khach.Add(new UserEntity
            {
                Id = Guid.NewGuid(),
                PhoneNumber = $"09{batDau + i:D8}",
                FullName = $"Hành Khách {i}",
                PasswordHash = "hash-khong-dung-toi-trong-test",
                IsActive = true,
                RoleId = RoleIds.Passenger,
                UserRoles = [new UserRole { RoleId = RoleIds.Passenger }],
            });
        }

        await factory.SeedAsync(db => db.Users.AddRange(khach));

        // Nạp kèm VaiTrò trước khi phát token: TokenService lấy claim vai trò từ user.Role, nên một
        // tài khoản rời rời khỏi DbContext sẽ cho ra token không có vai trò — vẫn qua được [Authorize]
        // của endpoint này, nhưng không còn giống token app thật phát cho hành khách.
        using var scope = factory.Services.CreateScope();
        var dbDoc = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ids = khach.Select(k => k.Id).ToList();

        return await dbDoc.Users.Include(u => u.Role).Where(u => ids.Contains(u.Id)).ToListAsync();
    }

    /// <summary>Lượt giữ chỗ cũ của một khách khác — bàn đạp cho hai ca "index phải là partial".</summary>
    private static async Task SeedLuotGiuCuAsync(
        NhaMaySqlite factory,
        Guid userId,
        Guid tripId,
        Guid seatId,
        SeatHoldStatus status)
    {
        await factory.SeedAsync(db => db.SeatHolds.Add(new SeatHold
        {
            Id = Guid.NewGuid(),
            TripId = tripId,
            SeatId = seatId,
            UserId = userId,
            SessionCode = "PHIEN-KHACH-CU",
            Status = status,
            ExpiresAt = DateTime.UtcNow.AddMinutes(-1),
        }));
    }

    // =======================================================================================
    // Helper bắn 50 request
    // =======================================================================================

    /// <summary>
    /// Nâng sàn luồng của thread pool TRƯỚC khi mở cổng.
    ///
    /// <para>
    /// 50 continuation cùng thức dậy một lúc, mà sàn mặc định của thread pool chỉ bằng số nhân CPU.
    /// Vượt sàn thì pool tiếp thêm luồng rất chậm (khoảng một luồng mỗi 500ms) — đủ để 50 request
    /// chạy gần như tuần tự, và khi đó người thắng đã ghi xong TRƯỚC khi người thua kịp đọc, nên bài
    /// test đo nhầm phép kiểm trước thay vì đo index. Spike phải nâng sàn mới thấy được nhánh đua.
    /// Đây là điều kiện để 50 request ĐỒNG THỜI thật, không phải để ép kết quả.
    /// </para>
    /// </summary>
    private static void NangSanLuong()
    {
        ThreadPool.GetMinThreads(out var luong, out var luongIo);
        ThreadPool.SetMinThreads(
            Math.Max(luong, SoKhach + 16),
            Math.Max(luongIo, SoKhach + 16));
    }

    /// <summary>
    /// 50 request <c>POST /api/seat-holds</c> xuất phát CÙNG LÚC qua HTTP thật (TestServer thật,
    /// middleware thật, JWT thật).
    ///
    /// <para>
    /// Cổng <see cref="TaskCompletionSource"/> là thứ duy nhất dàn dựng ở đây, và nó chỉ làm việc đua
    /// dễ xảy ra hơn — không ép kết quả. Token được phát TRƯỚC khi mở cổng để 50 request không phải
    /// xếp hàng chờ dựng JWT.
    /// </para>
    /// </summary>
    private static async Task<HttpResponseMessage[]> DuaAsync(
        NhaMaySqlite factory,
        IReadOnlyList<UserEntity> khach,
        Guid tripId,
        Guid[] seatIds)
    {
        NangSanLuong();

        var client = factory.CreateClient();
        var ve = khach.Select(khachItem => factory.TaoToken(khachItem)).ToList();

        // Khởi động nguội: một request đi trọn đường ống (xác thực JWT → controller → service →
        // truy vấn CSDL) trên một chuyến KHÔNG tồn tại, nên nó trả 404 và không ghi dòng nào. Thiếu
        // bước này, request thắng thường là request được JIT trước — bài test khi đó đang đo thứ tự
        // biên dịch chứ không đo cơ chế khoá.
        using (var requestKhoiDong = new HttpRequestMessage(HttpMethod.Post, DuongGiuGhe)
        {
            Content = JsonContent.Create(new { tripId = Guid.NewGuid(), seatIds = new[] { Guid.NewGuid() } }),
        })
        {
            requestKhoiDong.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ve[0]);
            using var phanHoiKhoiDong = await client.SendAsync(requestKhoiDong);
        }

        var cong = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var viec = ve.Select(veItem => Task.Run(async () =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, DuongGiuGhe)
            {
                Content = JsonContent.Create(new { tripId, seatIds }),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", veItem);

            await cong.Task;

            return await client.SendAsync(request);
        })).ToList();

        cong.SetResult();

        return await Task.WhenAll(viec);
    }

    /// <summary>
    /// Cùng phép đua nhưng gọi thẳng <see cref="ISeatHoldCreateService"/> — dùng cho các ca mà thứ
    /// cần khẳng định là kết quả nghiệp vụ chứ không phải mã HTTP, và cho ca cần nhiều ghế hơn.
    /// </summary>
    private static async Task<List<ServiceResult<SeatHoldSessionResponse>>> DuaTangServiceAsync(
        NhaMaySqlite factory,
        IReadOnlyList<UserEntity> khach,
        Guid tripId,
        IReadOnlyList<Seat> seats)
    {
        NangSanLuong();

        // Cùng lý do khởi động nguội như DuaAsync, ở tầng service: một lượt gọi trên chuyến không
        // tồn tại, không ghi dòng nào.
        using (var scopeKhoiDong = factory.Services.CreateScope())
        {
            await scopeKhoiDong.ServiceProvider.GetRequiredService<ISeatHoldCreateService>()
                .CreateAsync(
                    khach[0].Id,
                    new CreateSeatHoldRequest { TripId = Guid.NewGuid(), SeatIds = [Guid.NewGuid()] });
        }

        var cong = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var viec = khach.Zip(seats).Select(cap => Task.Run(async () =>
        {
            using var scope = factory.Services.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<ISeatHoldCreateService>();

            await cong.Task;

            return await service.CreateAsync(
                cap.First.Id,
                new CreateSeatHoldRequest { TripId = tripId, SeatIds = [cap.Second.Id] });
        })).ToList();

        cong.SetResult();

        return [.. await Task.WhenAll(viec)];
    }

    // =======================================================================================
    // Helper đọc dấu vết dưới CSDL
    // =======================================================================================

    private static async Task<int> DemLuotGiuAsync(NhaMaySqlite factory, Guid tripId, Guid seatId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await db.SeatHolds.CountAsync(h =>
            h.TripId == tripId && h.SeatId == seatId && h.Status == SeatHoldStatus.Holding);
    }

    private static async Task<int> DemLuotGiuCuaChuyenAsync(NhaMaySqlite factory, Guid tripId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await db.SeatHolds.CountAsync(h => h.TripId == tripId && h.Status == SeatHoldStatus.Holding);
    }

    private static async Task<int> DemNhatKyHeldAsync(NhaMaySqlite factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await db.SeatHoldLogs.CountAsync(l => l.Action == SeatHoldLogAction.Held);
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
        => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static string[] PropertyNamesOf(JsonElement element)
        => element.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray();

    // =======================================================================================
    // Helper soi mô hình EF và migration (Npgsql chỉ để lấy metadata, không mở kết nối)
    // =======================================================================================

    /// <summary>
    /// Context dựng trên provider QUAN HỆ (Npgsql) — điều kiện để soi được mô hình và migration.
    ///
    /// <para>
    /// Không dùng context mà <see cref="NhaMaySqlite"/> cấp: SQLite cũng là provider quan hệ nên về
    /// nguyên tắc soi được, nhưng chuỗi kết nối ở đây cố ý là địa chỉ KHÔNG tồn tại — soi mô hình là
    /// việc dựng metadata trong bộ nhớ, migration thì được nạp bằng phản chiếu từ assembly, không có
    /// lượt nào chạm mạng.
    /// </para>
    /// </summary>
    private static AppDbContext TaoDbContextQuanHe()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=khong-ket-noi;Database=chi-soi-metadata;Username=khong;Password=khong")
            .Options;

        return new AppDbContext(options);
    }

    /// <summary>
    /// Nạp migration theo mã và trả danh sách thao tác <c>Up</c>. Đọc <c>UpOperations</c> của đối tượng
    /// migration (đã được EF nhận diện) thay vì đọc file <c>.cs</c> dạng văn bản — bền hơn với format
    /// mã nguồn và không phụ thuộc đường dẫn tương đối của project test.
    /// </summary>
    private static IReadOnlyList<MigrationOperation> MigrationOperations(
        IMigrationsAssembly migrationsAssembly,
        AppDbContext db,
        string migrationId)
    {
        var migrationType = migrationsAssembly.Migrations[migrationId];
        var migration = (Migration)migrationsAssembly.CreateMigration(migrationType, db.Database.ProviderName!);

        return migration.UpOperations;
    }

    private static CreateIndexOperation FindCreateIndexOperation(
        AppDbContext db,
        string migrationId,
        string indexName)
        => MigrationOperations(db.GetService<IMigrationsAssembly>(), db, migrationId)
            .OfType<CreateIndexOperation>()
            .Single(operation => operation.Name == indexName);
}

/// <summary>
/// App THẬT trên CSDL SQLite (tệp tạm) — bản sao có chủ đích của <c>TestAppFactory</c> ở
/// <c>JwtAuthTests.cs</c>, khác đúng một điểm: provider.
///
/// <para>
/// <b>Vì sao cần lớp riêng thay vì dùng <c>TestAppFactory</c>.</b> <c>TestAppFactory</c> là
/// <c>sealed</c> và trỏ <c>InMemory</c> — provider không cưỡng chế unique index, tức là không thể
/// chạy bài test đua này. Đổi provider là cả nội dung của task, nên phải có lớp riêng.
/// </para>
///
/// <para>
/// <b>Vì sao SQLite chứ không PostgreSQL.</b> Máy trạm không có docker/psql và CI không kèm service
/// PostgreSQL (mà <c>.github/</c> là file của Hoàng). SQLite là engine duy nhất chạy trong tiến trình
/// mà dựng được partial unique index — và nó dựng ĐÚNG luật, điều được khoá lại bằng
/// <see cref="SeatHoldRaceFlowApiTests.SQLite_dung_dung_luat_index_ma_PostgreSQL_khai"/>.
/// </para>
///
/// <para>
/// <b>Vì sao TỆP chứ không phải <c>mode=memory&amp;cache=shared</c>.</b> SQLite in-memory dùng chung
/// khoá ở mức bảng và trả <c>SQLITE_LOCKED</c> NGAY thay vì xếp hàng chờ — bài test 50 request sẽ đo
/// nhầm cơ chế khoá thay vì đo cái cần đo. Tệp cho khoá thật, kèm <c>Default Timeout</c> để lượt ghi
/// sau xếp hàng đúng như ở PostgreSQL.
/// </para>
/// </summary>
internal sealed class NhaMaySqlite : WebApplicationFactory<Program>
{
    private const string Issuer = "SmartBus";

    private const string Audience = "SmartBusClient";

    private const string SigningKey = "khoa-ky-danh-cho-test-dai-hon-32-ky-tu-abc";

    /// <summary>
    /// Móc chèn lượt giữ đối thủ vào giữa hai bước của <c>SeatHoldCreateService</c>. Null ở mọi ca
    /// không cần dựng lại khe đua (tức là gần hết file này) — ghim interceptor vào mọi DbContext chỉ
    /// để một ca dùng là mở thêm một đường mà các ca khác không ngờ tới.
    /// </summary>
    public ChenLuotGiuDoiThuInterceptor? ChenGiua { get; }

    /// <summary>
    /// Bảy job nền của app. Phải gỡ khỏi host test: <c>TripGenerationBackgroundService</c> chạy ngay
    /// một lượt lúc khởi động rồi ĐUA với lúc test seed dữ liệu — đúng loại lỗi làm đỏ CI ngẫu nhiên
    /// ở PR #81 (xem <c>ThayJobNenBangBanNoop</c>). File này không có test wiring nào nên gỡ hẳn được,
    /// không cần chép bảy lớp noop của <c>TestAppFactory</c>.
    /// </summary>
    private static readonly Type[] JobNen =
    [
        typeof(TripGenerationBackgroundService),
        typeof(MonthlyPassExpiryBackgroundService),
        typeof(SeatHoldExpiryBackgroundService),
        typeof(SeatHoldAbuseBackgroundService),
        typeof(RefundRetryBackgroundService),
        typeof(RefundReconciliationBackgroundService),
        typeof(ExportJobBackgroundService),
    ];

    private readonly string _tepCsdl =
        Path.Combine(Path.GetTempPath(), $"smartbus-dua-{Guid.NewGuid():N}.db");

    /// <param name="chenGiua">
    /// true để ghim <see cref="ChenLuotGiuDoiThuInterceptor"/> vào DbContext của host — chỉ ca
    /// <see cref="SeatHoldRaceFlowApiTests.Nhanh_thua_o_CSDL_tra_409_chung_va_khong_de_lai_dong_nao"/>
    /// cần.
    /// </param>
    public NhaMaySqlite(bool chenGiua = false)
    {
        if (chenGiua)
        {
            ChenGiua = new ChenLuotGiuDoiThuInterceptor($"Data Source={_tepCsdl};Default Timeout=30");
        }
    }

    private string ChuoiKetNoi => $"Data Source={_tepCsdl};Default Timeout=30";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        // Cùng lối TestAppFactory: JWT phải có mặt ngay lúc Program.cs dựng service, nên dùng
        // UseSetting chứ không phải ConfigureAppConfiguration.
        builder.UseSetting("Jwt:Issuer", Issuer);
        builder.UseSetting("Jwt:Audience", Audience);
        builder.UseSetting("Jwt:Key", SigningKey);
        builder.UseSetting("Jwt:AccessTokenMinutes", "30");
        builder.UseSetting("Jwt:RefreshTokenDays", "7");

        builder.ConfigureTestServices(services =>
        {
            // Phải gỡ CẢ IDbContextOptionsConfiguration: EF Core 9+ giữ action cấu hình ở đó, để sót
            // thì lambda Npgsql của Program.cs còn sống và ChuoiKetNoiCsdl.KiemTra ném ngay lúc dựng
            // DbContext (chuỗi kết nối thật không nằm trong repo — repo public).
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
            services.AddDbContext<AppDbContext>(options =>
            {
                options.UseSqlite(ChuoiKetNoi);

                if (ChenGiua is not null)
                {
                    options.AddInterceptors(ChenGiua);
                }
            });

            GoJobNen(services);
        });
    }

    /// <summary>Gỡ đúng sáu job nền theo kiểu đăng ký, không gỡ cả <see cref="IHostedService"/>.</summary>
    private static void GoJobNen(IServiceCollection services)
    {
        var canGo = services
            .Where(d => d.ServiceType == typeof(IHostedService)
                && d.ImplementationType is not null
                && JobNen.Contains(d.ImplementationType))
            .ToList();

        foreach (var descriptor in canGo)
        {
            services.Remove(descriptor);
        }
    }

    /// <summary>Dựng schema một lần trước khi seed — SQLite không tự tạo bảng như InMemory.</summary>
    public async Task TaoCsdlAsync()
    {
        using var scope = Services.CreateScope();

        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreatedAsync();
    }

    /// <summary>Ghi dữ liệu vào CSDL test. Luôn lưu lại vì request đọc ở scope khác.</summary>
    public async Task SeedAsync(Action<AppDbContext> seed)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        seed(db);

        await db.SaveChangesAsync();
    }

    /// <summary>Access token sinh bằng chính <see cref="ITokenService"/> của app — test không lệch khỏi code thật.</summary>
    public string TaoToken(UserEntity user)
    {
        using var scope = Services.CreateScope();

        return scope.ServiceProvider.GetRequiredService<ITokenService>().CreateAccessToken(user).Token;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (!disposing)
        {
            return;
        }

        // Pool giữ handle trên tệp, không xoá hết thì File.Delete ném IOException vì tệp "đang được
        // dùng bởi tiến trình khác" — dù "tiến trình khác" chính là mình.
        SqliteConnection.ClearAllPools();

        try
        {
            File.Delete(_tepCsdl);
        }
        catch (IOException)
        {
            // Tệp tạm trong thư mục temp: không xoá được cũng không ảnh hưởng kết quả test, và làm
            // đỏ một bài test đã chạy xong chỉ vì dọn dẹp là sai chỗ để báo lỗi.
        }
    }
}

/// <summary>
/// Chèn một lượt giữ của NGƯỜI KHÁC vào đúng khe giữa hai bước của
/// <c>SeatHoldCreateService</c>: sau khi phép kiểm "ghế còn trống" đã đọc xong, trước khi
/// <c>SaveChangesAsync</c> ghi.
///
/// <para>
/// <b>Vì sao cần một móc riêng.</b> Ở môi trường thật, khe này do hai request đồng thời tự tìm thấy
/// nhau. Trong test thì không dựng lại được bằng cách nào khác: <c>AsNoTracking()</c> luôn hỏi thẳng
/// CSDL, nên không có cách nào cho phép kiểm trước "nhìn hụt" một dòng đã có sẵn — phải chèn dòng đó
/// SAU phép kiểm, tức là trong lúc service đang ghi. <c>SaveChangesInterceptor.SavingChangesAsync</c>
/// là điểm móc duy nhất mà tầng test chen được vào giữa hai bước của service mà KHÔNG phải sửa code
/// nghiệp vụ (ranh giới 2.4: người kiểm thử không sửa code để test xanh).
/// </para>
///
/// <para>
/// <b>Vì sao ghi bằng kết nối KHÁC.</b> Chèn ngay trên DbContext đang ghi thì dòng đối thủ nằm cùng
/// một lượt ghi với dòng của người thua, và hai dòng ấy đụng nhau trong CÙNG một câu lệnh — hỏng theo
/// kiểu khác hẳn. Một kết nối riêng, commit xong rồi mới trả về, mô phỏng đúng chuyện người thắng đã
/// ghi xong trước: lượt ghi sau đó của service đâm vào partial unique index và nhận
/// <c>DbUpdateException</c> — đúng nhánh cần kiểm.
/// </para>
/// </summary>
internal sealed class ChenLuotGiuDoiThuInterceptor : SaveChangesInterceptor
{
    private readonly string _chuoiKetNoi;

    private Guid _tripId;

    private Guid _seatId;

    private Guid _doiThuId;

    private bool _daNap;

    public ChenLuotGiuDoiThuInterceptor(string chuoiKetNoi) => _chuoiKetNoi = chuoiKetNoi;

    /// <summary>
    /// Nạp đạn cho ĐÚNG MỘT lượt ghi kế tiếp — lượt ghi của service, không phải lượt seed nào. Tự tắt
    /// sau khi dùng để dòng đối thủ không bị chèn thêm ở những lượt ghi về sau.
    /// </summary>
    public void Nap(Guid tripId, Guid seatId, Guid doiThuId)
    {
        _tripId = tripId;
        _seatId = seatId;
        _doiThuId = doiThuId;
        _daNap = true;
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (_daNap)
        {
            _daNap = false;

            // Context KHÔNG ghim interceptor này (dựng thẳng từ chuỗi kết nối), nên lượt ghi của nó
            // không quay lại gọi chính nó.
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(_chuoiKetNoi)
                .Options;

            await using var ketNoiKhac = new AppDbContext(options);

            ketNoiKhac.SeatHolds.Add(new SeatHold
            {
                Id = Guid.NewGuid(),
                TripId = _tripId,
                SeatId = _seatId,
                UserId = _doiThuId,
                SessionCode = "PHIEN-DOI-THU",
                Status = SeatHoldStatus.Holding,
                ExpiresAt = DateTime.UtcNow.AddMinutes(10),
            });

            await ketNoiKhac.SaveChangesAsync(cancellationToken);
        }

        return await base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}
