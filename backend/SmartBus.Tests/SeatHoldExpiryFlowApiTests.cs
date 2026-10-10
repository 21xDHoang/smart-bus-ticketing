using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SmartBus.Api.Data;
using SmartBus.Api.Entities;
using SmartBus.Api.Services;
using RouteEntity = SmartBus.Api.Entities.Route;
using UserEntity = SmartBus.Api.Entities.User;

namespace SmartBus.Tests;

/// <summary>
/// Test luồng HẾT HẠN của giữ chỗ (US 3 "Giữ chỗ tạm thời") — task *"Test API giữ chỗ / hết hạn /
/// nhả ghế tự động (xUnit)"* (sheet Sprint 3, dòng 27).
///
/// <para>
/// <b>Vì sao có file này.</b> Bề mặt này đã có bốn file test do chính người viết code dựng
/// (<see cref="SeatHoldExpiryServiceTests"/> — 13 ca cho phần ruột job;
/// <see cref="SeatHoldExpiryWiringTests"/> — 2 ca cho đăng ký DI;
/// <see cref="SeatHoldExtendApiTests"/> — 12 ca; <see cref="SeatHoldLookupApiTests"/> — 10 ca), cộng
/// một file khẳng định trong host test job hết hạn là BẢN NOOP
/// (<see cref="TestHostKhongChayJobNenTests"/>). Đọc kỹ cả năm thì thấy ba khoảng trống thật:
/// </para>
/// <list type="number">
///   <item>
///     <b>Lớp bọc job chưa từng được CHẠY.</b> Không file nào <c>new
///     SeatHoldExpiryBackgroundService(...)</c> hay chạm tới <c>ExecuteAsync</c>. Hai ca wiring chỉ
///     hỏi *"có mặt trong DI đúng một lần"* và *"resolve được ruột job từ một scope"* — đúng những
///     thứ đọc code là thấy. Còn hành vi chỉ lộ ra khi vòng lặp THẬT SỰ quay thì chưa ai phủ: lượt
///     quét đầu chạy NGAY lúc khởi động, và <c>SweepOnceAsync</c> NUỐT MỌI LỖI. Thiếu vế nào cũng
///     im lặng: ghế của khách bỏ ngang thanh toán cứ thế bị chặn mãi mà CI vẫn xanh — đúng kiểu lỗi
///     mà chính tác giả <see cref="SeatHoldExpiryWiringTests"/> đã ghi nhận là điểm mù.
///   </item>
///   <item>
///     <b>Không có ca end-to-end hết hạn nào.</b> Bốn file kia đều SEED thẳng dòng <c>SeatHolds</c>
///     xuống CSDL rồi khẳng định. Chuỗi thật của story — *giữ qua HTTP → quá hạn → job quét → sơ đồ
///     ghế trả <c>Available</c> → khách khác giữ lại được* — chưa tồn tại. Vòng NHẢ TAY
///     (<c>POST .../release</c>) đã được khoá ở <see cref="SeatHoldFlowApiTests"/>; vòng HẾT HẠN thì
///     còn hở, dù đó mới là nửa chữ *"nhả ghế tự động"* của tên task.
///   </item>
///   <item>
///     <b>Chốt CSDL đứng sau luật gia hạn/hết hạn không được khẳng định ở đâu.</b> Unique index
///     <c>IX_SeatHoldLogs_SeatHoldId_Action</c> là chốt cho CẢ HAI luật: *"gia hạn tối đa 1 lần"*
///     (US 3) và *"hai bản app cùng quét job thì nhật ký không đếm hai lần"* — điều kiện khiến lượt
///     quét chạy lặp lại được. Xoá nhầm <c>IsUnique()</c> ở <c>AppDbContext.Seat.cs</c> thì hôm nay
///     không có gì đỏ.
///   </item>
/// </list>
///
/// <para>
/// <b>Ba nhóm ca:</b> nhóm 1 chạy THẬT lớp bọc job trên một bệ đỡ tối thiểu (không dựng ASP.NET
/// host); nhóm 2 đi trọn vòng đời hết hạn xuyên bề mặt qua HTTP thật; nhóm 3 soi mô hình EF và
/// migration của chốt nhật ký. Không viết lại ca nào của bốn file đã có.
/// </para>
///
/// <para>
/// <b>Vì sao KHÔNG ghim nhịp quét 1 phút.</b> <see cref="MonthlyPassExpiryWiringTests"/> đã cố ý
/// không ghim chu kỳ của job vé tháng, và ở đây theo đúng tiền lệ đó: <c>SweepInterval</c> là hằng
/// <c>private static readonly</c>, ghim nó phải dùng phản chiếu và sẽ đỏ oan mỗi lần ai đó chỉnh
/// nhịp — mà chỉnh nhịp là quyết định thiết kế hợp lệ, không phải hồi quy. Thứ đáng khoá không phải
/// con số 1 phút mà là *"có lượt quét chạy trước khi hết nhịp đầu tiên"*, và nhóm 1 khoá đúng điều
/// đó bằng thời gian chờ ngắn hơn nhịp rất nhiều.
/// </para>
///
/// <para>
/// ⚠️ <b>Giới hạn — đọc trước khi tin bộ test này.</b>
/// </para>
/// <list type="bullet">
///   <item>
///     <b>Cuộc đua thật ở tầng CSDL KHÔNG được phủ.</b> Provider InMemory không dựng unique index
///     (kể cả index <c>(SeatHoldId, Action)</c>), nên nhánh hai bản app cùng quét một lượt giữ —
///     bản thua đâm vào index lúc <c>SaveChanges</c> và rollback cả lượt — chỉ chạy được ở
///     PostgreSQL thật. Nhóm 3 vì vậy khẳng định chốt đó ở tầng <b>mô hình EF</b> và tầng
///     <b>migration</b> (dựng context Npgsql CHỈ để soi metadata): đủ để bắt hồi quy khi ai đó gỡ
///     index, nhưng KHÔNG thay thế được một lượt chạy trên PostgreSQL. CI hiện tại
///     (<c>.github/workflows/ci.yml</c>) chạy <c>dotnet test</c> không kèm service Postgres. Bằng
///     chứng cuộc đua thuộc task *"Kiểm thử đồng thời: 50 khách cùng giữ 1 ghế"* (dòng 28).
///   </item>
///   <item>
///     <b>Không tua được đồng hồ thật.</b> Cửa sổ giữ chỗ 10 phút là hằng số của <c>SeatHoldCreateService</c>
///     và job nền truyền thẳng <c>DateTime.UtcNow</c>, nên "khe hở" giữa lúc hết hạn và lượt quét kế
///     tiếp không thể tạo bằng cách chờ. Nhóm 2 dựng nó bằng cách đẩy <c>ExpiresAt</c> về quá khứ
///     qua <c>SeedAsync</c> — đúng trạng thái mà một lượt quét chậm để lại trong CSDL.
///   </item>
///   <item>
///     <b>Trong mọi host test, job hết hạn chạy bản noop</b> (<see cref="TestHostKhongChayJobNenTests"/>):
///     <see cref="TestAppFactory"/> chỉ thay LỚP BỌC, còn <see cref="ISeatHoldExpiryService"/> vẫn là
///     bản thật đăng ký ở <c>Program.cs</c>. Nhờ vậy nhóm 2 tự quyết định thời điểm quét thay vì đua
///     với một vòng lặp nền.
///   </item>
///   <item>
///     <b>InMemory không cưỡng chế khoá ngoại</b> nên Route/Bus/SeatLayout/Seat/Trip được seed tối
///     thiểu vừa đủ cho truy vấn đọc đúng.
///   </item>
/// </list>
/// </summary>
public class SeatHoldExpiryFlowApiTests
{
    private const string CreateUrl = "/api/seat-holds";

    /// <summary>Mã migration dựng bảng nhật ký — nơi chốt <c>(SeatHoldId, Action)</c> phải có thật.</summary>
    private const string MigrationSeatHoldLogs = "20261007160423_Sprint3_SeatHoldLogs";

    /// <summary>Tên index chốt nhật ký, đúng như EF sinh ra ở migration Sprint 3.</summary>
    private const string IndexChotNhatKy = "IX_SeatHoldLogs_SeatHoldId_Action";

    /// <summary>
    /// Hạn chờ tín hiệu từ job nền. Xa dưới nhịp quét 1 phút (nên thấy tín hiệu trong ngần này nghĩa
    /// là lượt quét đã chạy NGAY, không phải chờ hết nhịp) nhưng đủ rộng cho một máy CI đang tải.
    /// </summary>
    private static readonly TimeSpan HanChoTinHieu = TimeSpan.FromSeconds(10);

    // =======================================================================================
    // Nhóm 1 — "Nhả ghế tự động": chạy THẬT lớp bọc job
    // =======================================================================================

    /// <summary>
    /// Lượt quét đầu phải chạy NGAY lúc khởi động, không đợi hết nhịp — đó là điều duy nhất khiến
    /// ghế của khách bỏ ngang thanh toán được trả lại trong vòng một phút kể từ lúc app lên.
    ///
    /// <para>
    /// Ca này khẳng định cả ba thứ cùng lúc: lượt giữ quá hạn bị lật <c>Expired</c> kèm
    /// <c>UpdatedAt</c> và ĐÚNG MỘT dòng nhật ký; lượt giữ còn hạn KHÔNG bị đụng tới; và tín hiệu
    /// đến trong <see cref="HanChoTinHieu"/> chứ không phải sau một nhịp 1 phút. Chờ tín hiệu là
    /// chứng minh lượt quét đã xong trước khi hết nhịp — không dùng <c>Task.Delay</c> quá nhịp, theo
    /// lệ của <c>TripSearchResultCacheTests</c> (*"Cố ý KHÔNG có test chờ đồng hồ"*).
    /// </para>
    /// </summary>
    [Fact]
    public async Task Job_quet_ngay_luot_dau_luc_khoi_dong_va_chi_lat_luot_qua_han()
    {
        using var provider = DungNhaMay();

        var userId = Guid.NewGuid();
        var quaHan = await SeedHoldAsync(
            provider, DateTime.UtcNow.AddMinutes(-5), userId, sessionCode: "PHIEN-QUA-HAN");
        var conHan = await SeedHoldAsync(
            provider, DateTime.UtcNow.AddMinutes(30), Guid.NewGuid(), sessionCode: "PHIEN-CON-HAN");

        var logger = new LoggerGhiNhan();
        var job = DungJob(provider, logger);

        try
        {
            await job.StartAsync(CancellationToken.None);
            await ChoTinHieuAsync(logger.CoThongTin.Task, "lượt quét đầu chạy lúc khởi động");

            var sauKhiQuet = await DocHoldAsync(provider, quaHan.Id);

            Assert.Equal(SeatHoldStatus.Expired, sauKhiQuet.Status);

            // A4: lượt giữ có sửa dữ liệu nên có UpdatedAt. Job ghi chính mốc đã dùng để xét hạn.
            Assert.NotNull(sauKhiQuet.UpdatedAt);

            var nhatKy = await DocNhatKyAsync(provider, quaHan.Id);

            var dongLog = Assert.Single(nhatKy);
            Assert.Equal(SeatHoldLogAction.Expired, dongLog.Action);
            Assert.Equal(userId, dongLog.UserId);
            Assert.Equal("PHIEN-QUA-HAN", dongLog.SessionCode);

            // Lượt giữ còn hạn không phải việc của lượt quét này — nếu ai đó bỏ điều kiện ExpiresAt
            // thì nhánh này đỏ, và đó đúng là hồi quy tốn kém nhất: ghế khách còn đang trả tiền bị nhả.
            var conHanSauQuet = await DocHoldAsync(provider, conHan.Id);
            Assert.Equal(SeatHoldStatus.Holding, conHanSauQuet.Status);
            Assert.Null(conHanSauQuet.UpdatedAt);
            Assert.Empty(await DocNhatKyAsync(provider, conHan.Id));

            // "Chỉ ghi log khi có việc" — nội dung dòng log phải nói đúng số lượt vừa nhả.
            Assert.Contains("1", Assert.Single(logger.ThongTin));
        }
        finally
        {
            await job.StopAsync(CancellationToken.None);
        }
    }

    /// <summary>
    /// Job phải NUỐT lỗi của phần ruột: CSDL chập chờn không được kéo sập API (mặc định của .NET là
    /// <c>BackgroundServiceExceptionBehavior.StopHost</c>, để exception vọt ra một lần là cả app tắt).
    ///
    /// <para>
    /// Đây là ca mà nhịp quét nhanh 1 phút khiến thành nghiêm trọng: mỗi lượt hỏng mà không nuốt lỗi
    /// là một lần app chết, và nó xảy ra TRƯỚC khi khách kịp nhận ra điều gì. Ca khẳng định vòng lặp
    /// vẫn sống (<c>ExecuteTask</c> chưa faulted, chưa hoàn tất) SAU khi ruột job đã ném.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Job_nuot_loi_cua_ruot_va_vong_lap_van_song()
    {
        var ruotHong = new RuotJobGia(loi: new InvalidOperationException("CSDL chập chờn"));

        using var provider = DungNhaMay(
            themDangKy: services => services.AddScoped<ISeatHoldExpiryService>(_ => ruotHong));

        var logger = new LoggerGhiNhan();
        var job = DungJob(provider, logger);

        try
        {
            await job.StartAsync(CancellationToken.None);

            // Dòng log lỗi là CÂU LỆNH CUỐI của nhánh catch, nên thấy nó nghĩa là nhánh nuốt lỗi đã
            // chạy trọn. Bỏ nhánh catch đó thì không có dòng nào để chờ, và ca này đỏ ngay ở đây.
            await ChoTinHieuAsync(logger.CoLoi.Task, "dòng log lỗi của lượt quét hỏng");

            Assert.Contains("thất bại", Assert.Single(logger.Loi));

            var vongLap = job.ExecuteTask;
            Assert.NotNull(vongLap);
            Assert.False(vongLap.IsFaulted, "Lỗi của ruột job không được vọt ra ngoài — mặc định StopHost sẽ tắt cả API.");
            Assert.False(vongLap.IsCompleted, "Vòng lặp phải còn sống để thử lại ở lượt quét kế tiếp.");
        }
        finally
        {
            await job.StopAsync(CancellationToken.None);
        }
    }

    /// <summary>
    /// Lượt quét không có gì để làm KHÔNG được ghi dòng <c>Information</c> nào.
    ///
    /// <para>
    /// Nhịp 1 phút nên ghi log vô điều kiện là 1440 dòng rác mỗi ngày cho một bảng gần như luôn rỗng —
    /// chúng che mất đúng những dòng đáng đọc. Ca này khoá quyết định đó.
    /// </para>
    ///
    /// <para>
    /// Ruột job ở đây là bản giả trả về 0 và báo tín hiệu khi được gọi, nên test biết chắc một lượt
    /// quét ĐÃ chạy. Sau đó <c>StopAsync</c> làm mốc chốt: nó chờ tới khi vòng lặp thoát, mà vòng lặp
    /// chỉ thoát sau khi đã đi qua câu <c>if (soLuotNha > 0)</c> của lượt hiện tại — nên phép đếm log
    /// dưới đây là tất định, không phải một cuộc đua may rủi.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Job_khong_ghi_log_Information_khi_khong_co_gi_qua_han()
    {
        var ruotRong = new RuotJobGia(soLuotTra: 0);

        using var provider = DungNhaMay(
            themDangKy: services => services.AddScoped<ISeatHoldExpiryService>(_ => ruotRong));

        var logger = new LoggerGhiNhan();
        var job = DungJob(provider, logger);

        try
        {
            await job.StartAsync(CancellationToken.None);
            await ChoTinHieuAsync(ruotRong.CoGoi.Task, "lượt quét gọi vào ruột job");
        }
        finally
        {
            await job.StopAsync(CancellationToken.None);
        }

        Assert.Empty(logger.ThongTin);
        Assert.Empty(logger.Loi);
    }

    // =======================================================================================
    // Nhóm 2 — Vòng đời hết hạn xuyên bề mặt qua HTTP thật
    // =======================================================================================

    /// <summary>
    /// "Khe hở" dưới một nhịp quét, nhìn từ sơ đồ ghế và từ API tra phiên: phiên đã quá
    /// <c>ExpiresAt</c> nhưng job chưa quét thì <c>Status</c> vẫn <c>Holding</c>, nên ghế VẪN bị
    /// chặn trên sơ đồ và phiên vẫn được báo là đang giữ.
    ///
    /// <para>
    /// Đây là hành vi CỐ Ý chứ không phải lỗi — quyết định *"cố ý ngây thơ với ExpiresAt"* đã được
    /// chốt cho endpoint nhả (docs/api-contract.md, mục <c>POST .../release</c>) để ghế không bị giữ
    /// thêm một nhịp. Hai bề mặt còn lại — sơ đồ ghế và API tra phiên — chưa có ca nào phủ đúng nhánh
    /// này, nên một hồi quy kiểu *"sơ đồ ghế tự lọc ExpiresAt"* sẽ lặng lẽ đổi hành vi mà không ai biết.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Trong_khe_ho_chua_quet_so_do_van_Held_va_phien_van_Holding()
    {
        using var factory = new TestAppFactory();
        var (trip, _, seats) = await SeedChuyenDayDuAsync(factory);
        var ghe = seats[0];

        var a = await SignInAsPassengerAsync(factory);
        var phien = await GiuGheAsync(a, trip.Id, ghe.Id);

        await DayHanVeQuaKhuAsync(factory, phien);

        // Ghế VẪN bị chặn: chỉ dòng Status = Holding chặn ghế, mà job chưa kịp lật.
        Assert.Equal("Held", TrangThaiGhe(await DocSoDoGheAsync(factory, trip.Id), ghe.Id));

        var traBody = await ReadJsonAsync(await a.GetAsync($"/api/seat-holds/{phien}"));

        Assert.Equal("Holding", traBody.GetProperty("status").GetString());

        // Còn Holding và chưa có dòng Extended nào ⇒ vẫn còn lượt gia hạn.
        Assert.True(traBody.GetProperty("canExtend").GetBoolean());
    }

    /// <summary>
    /// Trong khe hở đó, gia hạn phải THÀNH CÔNG: phiên đã quá hạn trên đồng hồ mà chưa bị quét thì
    /// vẫn còn <c>Holding</c>, và <see cref="SeatHoldExtendService"/> chỉ từ chối khi KHÔNG còn dòng
    /// <c>Holding</c> nào.
    ///
    /// <para>
    /// Đây là hành vi đúng của màn hình: đồng hồ vừa về 0, khách bấm "Gia hạn" ngay, mà lượt quét
    /// chưa tới — khách phải nhận lại trọn một cửa sổ 10 phút chứ không phải câu "phiên đã kết thúc".
    /// Hạn mới tính từ LÚC GỌI chứ không phải từ hạn cũ (hợp đồng chốt vậy để gia hạn sớm không được
    /// thưởng thời gian), nên ca này cũng khoá luôn cách tính đó.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Trong_khe_ho_chua_quet_gia_han_van_thanh_cong_va_hoi_sinh_ghe()
    {
        using var factory = new TestAppFactory();
        var (trip, _, seats) = await SeedChuyenDayDuAsync(factory);
        var ghe = seats[0];

        var a = await SignInAsPassengerAsync(factory);
        var phien = await GiuGheAsync(a, trip.Id, ghe.Id);

        await DayHanVeQuaKhuAsync(factory, phien);

        var truocKhiGoi = DateTime.UtcNow;
        var giaHan = await a.PostAsync($"/api/seat-holds/{phien}/extend", content: null);

        Assert.Equal(HttpStatusCode.OK, giaHan.StatusCode);

        var body = await ReadJsonAsync(giaHan);

        Assert.Equal("Holding", body.GetProperty("status").GetString());

        // Vừa dùng hết lượt gia hạn duy nhất của phiên.
        Assert.False(body.GetProperty("canExtend").GetBoolean());

        var hanMoi = body.GetProperty("expiresAt").GetDateTime();
        Assert.True(
            hanMoi >= truocKhiGoi.AddMinutes(10),
            $"Hạn mới phải tính từ lúc gọi (>= {truocKhiGoi:O} + 10 phút), không phải từ hạn cũ — hạn nhận được: {hanMoi:O}.");
        Assert.True(hanMoi <= DateTime.UtcNow.AddMinutes(11), $"Hạn mới không được vượt quá lúc gọi + 10 phút — hạn nhận được: {hanMoi:O}.");

        // Xuống tới CSDL thì hạn mới phải có thật, không chỉ nằm trong response — và nhật ký phải có
        // đúng một dòng Extended cho lượt giữ này.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var hold = await db.SeatHolds.SingleAsync(h => h.SessionCode == phien);
        Assert.Equal(hold.ExpiresAt, hanMoi, TimeSpan.FromSeconds(1));
        Assert.Equal(SeatHoldStatus.Holding, hold.Status);

        var dongExtended = await db.SeatHoldLogs
            .Where(log => log.SeatHoldId == hold.Id && log.Action == SeatHoldLogAction.Extended)
            .CountAsync();
        Assert.Equal(1, dongExtended);

        // Ghế vẫn Held — hồi sinh thành công, không rơi khỏi sơ đồ.
        Assert.Equal("Held", TrangThaiGhe(await DocSoDoGheAsync(factory, trip.Id), ghe.Id));
    }

    /// <summary>
    /// MỆNH ĐỀ TRUNG TÂM của tên task — *"nhả ghế TỰ ĐỘNG"* — và là ca chưa file test nào phủ. Đi
    /// trọn vòng qua HTTP thật: khách giữ ghế → quá hạn → job quét → ghế về <c>Available</c> → khách
    /// KHÁC giữ lại được đúng ghế đó.
    ///
    /// <para>
    /// Lượt quét được gọi thẳng qua DI của host thay vì chờ vòng lặp nền: <see cref="TestAppFactory"/>
    /// thay lớp bọc bằng bản noop nên test tự quyết định thời điểm, còn
    /// <see cref="ISeatHoldExpiryService"/> vẫn là bản THẬT — đúng phần ruột mà job nền chạy mỗi phút.
    /// Sau khi quét, gia hạn phải đóng lại bằng 409: đây là vế còn lại của ca "khe hở" ở trên, và là
    /// ca lỗi mà mọi bề mặt khác đã có nhưng bề mặt này thì chưa.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Quet_that_lat_Expired_tra_ghe_ve_Available_va_nguoi_khac_giu_lai_duoc()
    {
        using var factory = new TestAppFactory();
        var (trip, _, seats) = await SeedChuyenDayDuAsync(factory);
        var ghe = seats[0];

        var a = await SignInAsPassengerAsync(factory);
        var phien = await GiuGheAsync(a, trip.Id, ghe.Id);

        await DayHanVeQuaKhuAsync(factory, phien);

        // Lượt quét THẬT: đúng phần ruột mà job nền chạy, gọi tất định thay vì chờ nhịp 1 phút.
        Assert.Equal(1, await QuetHoldHetHanAsync(factory));

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var hold = await db.SeatHolds.SingleAsync(h => h.SessionCode == phien);

            Assert.Equal(SeatHoldStatus.Expired, hold.Status);
            Assert.NotNull(hold.UpdatedAt);

            // Cả phiên có đúng HAI dòng nhật ký: một Held do API tạo phiên ghi lúc khách giữ ghế, và
            // một Expired do lượt quét vừa rồi. Lượt quét THÊM một dòng vào lịch sử, không viết lại nó.
            var nhatKy = await db.SeatHoldLogs
                .Where(log => log.SeatHoldId == hold.Id)
                .ToListAsync();

            Assert.Equal(2, nhatKy.Count);
            Assert.Contains(nhatKy, log => log.Action == SeatHoldLogAction.Held);

            var dongExpired = Assert.Single(nhatKy, log => log.Action == SeatHoldLogAction.Expired);
            Assert.Equal(SeatHoldLogAction.Expired, dongExpired.Action);
            Assert.Equal(hold.UserId, dongExpired.UserId);
            Assert.Equal(phien, dongExpired.SessionCode);
        }

        // Ghế tự do trở lại trên sơ đồ — đây là thứ khách sau nhìn thấy.
        Assert.Equal("Available", TrangThaiGhe(await DocSoDoGheAsync(factory, trip.Id), ghe.Id));

        var traBody = await ReadJsonAsync(await a.GetAsync($"/api/seat-holds/{phien}"));
        Assert.Equal("Expired", traBody.GetProperty("status").GetString());
        Assert.False(traBody.GetProperty("canExtend").GetBoolean());

        // Phiên đã hết hạn thì không hồi sinh được nữa: khác hẳn ca "khe hở" ở trên, nơi gia hạn vẫn 200.
        var giaHanSauKhiQuet = await a.PostAsync($"/api/seat-holds/{phien}/extend", content: null);
        Assert.Equal(HttpStatusCode.Conflict, giaHanSauKhiQuet.StatusCode);
        Assert.Equal("Phiên giữ chỗ đã kết thúc, không thể gia hạn.", await MessageAsync(giaHanSauKhiQuet));

        // Đích đến của cả story: người sau giữ được đúng ghế vừa được nhả tự động.
        var b = await SignInAsPassengerAsync(factory);
        var giuB = await b.PostAsJsonAsync(CreateUrl, new { tripId = trip.Id, seatIds = new[] { ghe.Id } });

        Assert.Equal(HttpStatusCode.Created, giuB.StatusCode);
        Assert.Equal("Holding", (await ReadJsonAsync(giuB)).GetProperty("status").GetString());
        Assert.Equal("Held", TrangThaiGhe(await DocSoDoGheAsync(factory, trip.Id), ghe.Id));
    }

    /// <summary>
    /// Lượt quét chạy LẶP LẠI được: lượt sau không lật lại lượt đã <c>Expired</c>, không ghi thêm
    /// nhật ký, và không đè <c>UpdatedAt</c> của lần nhả đầu.
    ///
    /// <para>
    /// Đây là điều kiện khiến nhịp 1 phút chịu được: mỗi ngày có 1440 lượt quét mà phần lớn phải là
    /// lượt rỗng. Ở bảng chỉ-ghi-thêm <c>SeatHoldLogs</c>, ghi lặp còn tệ hơn vô ích — câu hỏi *"tài
    /// khoản này để hết hạn bao nhiêu lần"* sẽ trả lời sai gấp đôi. Chốt Status trong điều kiện truy
    /// vấn của <see cref="SeatHoldExpiryService"/> là thứ giữ điều đó, và ca này khoá nó lại ở tầng
    /// hành vi chứ không chỉ ở tầng đơn vị (đã có ở <see cref="SeatHoldExpiryServiceTests"/>).
    /// </para>
    /// </summary>
    [Fact]
    public async Task Quet_lan_hai_khong_lat_lai_va_khong_ghi_them_nhat_ky()
    {
        using var factory = new TestAppFactory();
        var (trip, _, seats) = await SeedChuyenDayDuAsync(factory);
        var ghe = seats[0];

        var a = await SignInAsPassengerAsync(factory);
        var phien = await GiuGheAsync(a, trip.Id, ghe.Id);

        await DayHanVeQuaKhuAsync(factory, phien);

        Assert.Equal(1, await QuetHoldHetHanAsync(factory));

        Guid holdId;
        DateTime? mocNhaLanDau;

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var hold = await db.SeatHolds.AsNoTracking().SingleAsync(h => h.SessionCode == phien);

            (holdId, mocNhaLanDau) = (hold.Id, hold.UpdatedAt);
        }

        Assert.Equal(0, await QuetHoldHetHanAsync(factory));

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var hold = await db.SeatHolds.AsNoTracking().SingleAsync(h => h.Id == holdId);

            Assert.Equal(SeatHoldStatus.Expired, hold.Status);
            Assert.Equal(mocNhaLanDau, hold.UpdatedAt);

            // Vẫn ĐÚNG HAI dòng của lần đầu — Held lúc khách giữ ghế, Expired ở lượt quét đầu. Lượt
            // quét thứ hai không thêm gì, tức là cùng một sự kiện không bị đếm hai lần.
            Assert.Equal(2, await db.SeatHoldLogs.CountAsync(log => log.SeatHoldId == holdId));
            Assert.Equal(
                1,
                await db.SeatHoldLogs.CountAsync(
                    log => log.SeatHoldId == holdId && log.Action == SeatHoldLogAction.Expired));
        }

        Assert.Equal("Available", TrangThaiGhe(await DocSoDoGheAsync(factory, trip.Id), ghe.Id));
    }

    // =======================================================================================
    // Nhóm 3 — Chốt CSDL đứng sau luật gia hạn và hết hạn
    // =======================================================================================

    /// <summary>
    /// Mô hình EF phải khai ĐÚNG MỘT index trên <c>(SeatHoldId, Action)</c> và index đó phải UNIQUE.
    ///
    /// <para>
    /// Không phải ràng buộc cho đẹp: nó là chốt cho *"gia hạn tối đa 1 lần"* (dòng <c>Extended</c> thứ
    /// hai không lọt được xuống bảng) VÀ cho *"hai bản app cùng quét job thì nhật ký không đếm hai
    /// lần"* (bản thua đâm vào index, <c>SaveChanges</c> ném <c>DbUpdateException</c>, cả lượt quét
    /// rollback). Xoá <c>IsUnique()</c> thì cả hai luật mất chốt cuối cùng mà không có gì đỏ.
    /// </para>
    /// </summary>
    [Fact]
    public void Mo_hinh_khai_dung_mot_index_unique_tren_SeatHoldId_Action()
    {
        using var db = TaoDbContextQuanHe();

        var entityType = db.Model.FindEntityType(typeof(SeatHoldLog));
        Assert.NotNull(entityType);

        var indexTrenCapNhatKy = entityType!.GetIndexes()
            .Where(index => index.Properties.Select(p => p.Name).SequenceEqual(["SeatHoldId", "Action"]))
            .ToList();

        var index = Assert.Single(indexTrenCapNhatKy);

        Assert.True(
            index.IsUnique,
            "Index (SeatHoldId, Action) phải unique — thiếu IsUnique() là mất chốt 'gia hạn tối đa 1 lần' và chốt chống ghi trùng nhật ký.");
    }

    /// <summary>
    /// Chốt phải có thật trong CSDL, không chỉ nằm trong mô hình EF: migration dựng bảng
    /// <c>SeatHoldLogs</c> phải sinh ra <c>CreateIndexOperation</c> unique đúng tên, đúng bảng, đúng
    /// hai cột. Mô hình khai mà migration không mang thì ở PostgreSQL chốt không tồn tại — nơi duy
    /// nhất nó thực sự làm việc.
    /// </summary>
    [Fact]
    public void Migration_Sprint3_SeatHoldLogs_sinh_index_unique_xuong_CSDL()
    {
        using var db = TaoDbContextQuanHe();

        // Migration phải được EF nhận diện — nếu ai đó xoá/đổi tên file migration thì dừng ngay ở đây
        // với thông báo rõ, thay vì ném KeyNotFoundException khó đọc ở dưới.
        Assert.Contains(MigrationSeatHoldLogs, db.Database.GetMigrations());

        var operation = FindCreateIndexOperation(db, MigrationSeatHoldLogs, IndexChotNhatKy);

        Assert.Equal("SeatHoldLogs", operation.Table);
        Assert.True(
            operation.IsUnique,
            "Migration phải sinh index unique — thiếu là chốt nhật ký không có thật trong CSDL.");
        Assert.Equal(new[] { "SeatHoldId", "Action" }, operation.Columns);
    }

    // =======================================================================================
    // Helper nhóm 1 — bệ đỡ tối thiểu để chạy THẬT lớp bọc job, không dựng ASP.NET host
    // =======================================================================================

    /// <summary>
    /// Nhà máy dịch vụ tối thiểu, đăng ký ĐÚNG như app: <c>AddDbContext</c> cho CSDL InMemory riêng
    /// và <c>AddScoped&lt;ISeatHoldExpiryService, SeatHoldExpiryService&gt;</c> — chép lại hai dòng
    /// của <c>Program.cs</c> mà <see cref="SeatHoldExpiryWiringTests"/> chỉ khẳng định sự tồn tại.
    ///
    /// <para>
    /// <paramref name="themDangKy"/> chạy SAU, nên nó ghi đè được đăng ký ruột job (đăng ký sau thắng
    /// khi resolve) — dùng cho hai ca cần ruột giả.
    /// </para>
    ///
    /// <para>
    /// Không dựng ASP.NET host vì ca này không cần HTTP: cả thứ đang được kiểm nằm trong vòng lặp của
    /// job, và host chỉ làm nó chậm thêm.
    /// </para>
    /// </summary>
    private static ServiceProvider DungNhaMay(Action<IServiceCollection>? themDangKy = null)
    {
        // Tên CSDL phải chốt MỘT LẦN ngoài lambda: action cấu hình options chạy lại mỗi lần dựng
        // DbContext, nên sinh Guid trong đó là mỗi context trỏ một CSDL khác — seed một nơi, job đọc
        // nơi khác, và lượt quét "không tìm thấy gì" một cách hoàn toàn im lặng.
        var tenCsdl = $"job-het-han-{Guid.NewGuid()}";

        var services = new ServiceCollection();

        services.AddLogging();
        services.AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase(tenCsdl));

        // Đăng ký ruột job y hệt Program.cs: hosted service là singleton còn AppDbContext là scoped
        // nên job phải mở scope riêng cho mỗi lượt quét mới resolve được.
        services.AddScoped<ISeatHoldExpiryService, SeatHoldExpiryService>();

        themDangKy?.Invoke(services);

        return services.BuildServiceProvider();
    }

    private static SeatHoldExpiryBackgroundService DungJob(ServiceProvider provider, LoggerGhiNhan logger)
        => new(provider.GetRequiredService<IServiceScopeFactory>(), logger);

    /// <summary>
    /// Chờ một tín hiệu có hạn — thay cho việc chờ đồng hồ. Hết hạn thì <c>Assert</c> đỏ kèm mô tả,
    /// chứ không treo bộ test.
    /// </summary>
    private static async Task ChoTinHieuAsync(Task tinHieu, string moTa)
    {
        var xongTruocHan = await Task.WhenAny(tinHieu, Task.Delay(HanChoTinHieu));

        Assert.True(
            xongTruocHan == tinHieu,
            $"Không thấy {moTa} trong {HanChoTinHieu.TotalSeconds} giây — job nền có thể đã không chạy, hoặc lượt quét đầu bị đẩy ra sau nhịp.");
    }

    /// <summary>
    /// Một lượt giữ ghế trong CSDL của bệ đỡ. Không seed Trips/Seats/Users: InMemory không cưỡng chế
    /// khoá ngoại, mà truy vấn của job chỉ đọc đúng bảng <c>SeatHolds</c>.
    /// </summary>
    private static async Task<SeatHold> SeedHoldAsync(
        ServiceProvider provider,
        DateTime expiresAt,
        Guid userId,
        string sessionCode)
    {
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var luotGiu = new SeatHold
        {
            TripId = Guid.NewGuid(),
            SeatId = Guid.NewGuid(),
            UserId = userId,
            SessionCode = sessionCode,
            Status = SeatHoldStatus.Holding,
            ExpiresAt = expiresAt,
            CreatedAt = expiresAt.AddMinutes(-10),
        };

        db.SeatHolds.Add(luotGiu);
        await db.SaveChangesAsync();

        return luotGiu;
    }

    /// <summary>Đọc lại một lượt giữ bằng scope MỚI — dữ liệu phải đã xuống CSDL, không nằm trong change tracker.</summary>
    private static async Task<SeatHold> DocHoldAsync(ServiceProvider provider, Guid holdId)
    {
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await db.SeatHolds.AsNoTracking().SingleAsync(h => h.Id == holdId);
    }

    private static async Task<List<SeatHoldLog>> DocNhatKyAsync(ServiceProvider provider, Guid holdId)
    {
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await db.SeatHoldLogs.AsNoTracking().Where(log => log.SeatHoldId == holdId).ToListAsync();
    }

    /// <summary>
    /// Ruột job giả cho hai ca không cần CSDL: một ca cần nó NÉM, một ca cần nó trả về 0. Báo tín
    /// hiệu <see cref="CoGoi"/> ngay khi được gọi để test biết chắc một lượt quét đã chạy.
    /// </summary>
    private sealed class RuotJobGia : ISeatHoldExpiryService
    {
        private readonly int _soLuotTra;

        private readonly Exception? _loi;

        public RuotJobGia(int soLuotTra = 0, Exception? loi = null)
        {
            _soLuotTra = soLuotTra;
            _loi = loi;
        }

        public TaskCompletionSource CoGoi { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<int> ExpireDueHoldsAsync(DateTime now, CancellationToken cancellationToken = default)
        {
            CoGoi.TrySetResult();

            return _loi is null
                ? Task.FromResult(_soLuotTra)
                : Task.FromException<int>(_loi);
        }
    }

    /// <summary>
    /// Logger ghi nhận cho hai nhánh log của <see cref="SeatHoldExpiryBackgroundService"/>.
    ///
    /// <para>
    /// Mỗi dòng log được ghi ở CUỐI một nhánh xử lý (Information khi lượt quét có việc, Error trong
    /// nhánh <c>catch</c>), nên một <c>TaskCompletionSource</c> hoàn tất đồng nghĩa nhánh đó đã chạy
    /// trọn — kể cả <c>SaveChanges</c> đã commit. Nhờ đó test bắt được tín hiệu tất định, không phải
    /// chờ đồng hồ hay polling.
    /// </para>
    /// </summary>
    private sealed class LoggerGhiNhan : ILogger<SeatHoldExpiryBackgroundService>
    {
        private readonly ConcurrentQueue<string> _thongTin = new();

        private readonly ConcurrentQueue<string> _loi = new();

        public TaskCompletionSource CoThongTin { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource CoLoi { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IReadOnlyCollection<string> ThongTin => _thongTin.ToArray();

        public IReadOnlyCollection<string> Loi => _loi.ToArray();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Information)
            {
                _thongTin.Enqueue(formatter(state, exception));
                CoThongTin.TrySetResult();
            }
            else if (logLevel == LogLevel.Error)
            {
                _loi.Enqueue(formatter(state, exception));
                CoLoi.TrySetResult();
            }
        }
    }

    // =======================================================================================
    // Helper nhóm 2 — HTTP thật qua TestAppFactory
    // =======================================================================================

    /// <summary>Giữ một ghế qua API và trả về mã phiên.</summary>
    private static async Task<string> GiuGheAsync(HttpClient client, Guid tripId, Guid seatId)
    {
        var response = await client.PostAsJsonAsync(CreateUrl, new { tripId, seatIds = new[] { seatId } });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return (await ReadJsonAsync(response)).GetProperty("sessionCode").GetString()!;
    }

    /// <summary>
    /// Đẩy hạn của cả phiên về quá khứ — dựng lại "khe hở" mà một lượt quét chậm để lại trong CSDL.
    ///
    /// <para>
    /// Không thể tạo khe hở bằng cách chờ: cửa sổ giữ chỗ 10 phút là hằng số của
    /// <c>SeatHoldCreateService</c> và job nền truyền thẳng <c>DateTime.UtcNow</c> — không có đồng hồ
    /// nào để tua. Sửa thẳng <c>ExpiresAt</c> cho ra ĐÚNG trạng thái đó: <c>Status</c> còn
    /// <c>Holding</c> mà hạn đã qua.
    /// </para>
    /// </summary>
    private static async Task DayHanVeQuaKhuAsync(TestAppFactory factory, string sessionCode)
        => await factory.SeedAsync(db =>
        {
            foreach (var hold in db.SeatHolds.Where(h => h.SessionCode == sessionCode))
            {
                hold.ExpiresAt = DateTime.UtcNow.AddMinutes(-1);
            }
        });

    /// <summary>
    /// Chạy ĐÚNG phần ruột mà job nền chạy mỗi phút, gọi tất định qua DI của host. Trong host test
    /// chỉ LỚP BỌC bị thay bằng bản noop, <see cref="ISeatHoldExpiryService"/> vẫn là bản thật.
    /// </summary>
    private static async Task<int> QuetHoldHetHanAsync(TestAppFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var expiry = scope.ServiceProvider.GetRequiredService<ISeatHoldExpiryService>();

        return await expiry.ExpireDueHoldsAsync(DateTime.UtcNow);
    }

    /// <summary>Sơ đồ ghế theo chuyến — endpoint CÔNG KHAI nên không cần token.</summary>
    private static async Task<JsonElement> DocSoDoGheAsync(TestAppFactory factory, Guid tripId)
        => await ReadJsonAsync(await factory.CreateClient().GetAsync($"/api/trips/{tripId}/seats"));

    /// <summary>Trạng thái của một ghế trên sơ đồ, tra theo <c>id</c> chứ không theo vị trí mảng.</summary>
    private static string TrangThaiGhe(JsonElement soDo, Guid seatId)
        => soDo.GetProperty("seats").EnumerateArray()
            .Single(seat => seat.GetProperty("id").GetGuid() == seatId)
            .GetProperty("status")
            .GetString()!;

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
        => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task<string> MessageAsync(HttpResponseMessage response)
    {
        var body = await ReadJsonAsync(response);

        return body.GetProperty("message").GetString() ?? string.Empty;
    }

    private static async Task<HttpClient> SignInAsPassengerAsync(TestAppFactory factory)
    {
        var user = await SeedUserAsync(factory);

        return ClientWith(factory, factory.CreateTokenFor(user));
    }

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
    private static async Task<UserEntity> SeedUserAsync(TestAppFactory factory)
    {
        await factory.SeedAsync(db =>
        {
            if (!db.Roles.Any(r => r.Id == RoleIds.Passenger || r.Code == RoleCodes.Passenger))
            {
                db.Roles.Add(new Role { Id = RoleIds.Passenger, Code = RoleCodes.Passenger, Name = RoleCodes.Passenger });
            }
        });

        var user = new UserEntity
        {
            Id = Guid.NewGuid(),
            PhoneNumber = "09" + Random.Shared.Next(10_000_000, 99_999_999),
            FullName = "Hành Khách Test",
            PasswordHash = "hash-khong-dung-toi-trong-test",
            IsActive = true,
            RoleId = RoleIds.Passenger,
            UserRoles = [new UserRole { RoleId = RoleIds.Passenger }],
        };

        await factory.SeedAsync(db => db.Users.Add(user));

        return user;
    }

    /// <summary>
    /// Nền dữ liệu đầy đủ cho luồng chọn ghế: tuyến + xe + sơ đồ + chuyến + dàn ghế.
    /// <c>Bus</c> phải là dòng THẬT vì <c>TripSeatMapService</c> join chuyến với xe; <c>SeatLayout</c>
    /// phải khớp <c>BusType</c> vì số tầng tra theo khoá đó.
    /// </summary>
    private static async Task<(Trip Trip, Bus Bus, List<Seat> Seats)> SeedChuyenDayDuAsync(
        TestAppFactory factory,
        int soGhe = 3)
    {
        var route = await SeedRouteAsync(factory);
        var bus = await SeedBusAsync(factory);
        var layout = await SeedLayoutAsync(factory, bus.BusType);
        var seats = await SeedSeatsAsync(factory, bus.Id, layout.Id, soGhe);
        var trip = await SeedTripAsync(factory, route.Id, bus.Id);

        return (trip, bus, seats);
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

    private static async Task<Bus> SeedBusAsync(TestAppFactory factory, string busType = "Xe buýt 45 chỗ")
    {
        var bus = new Bus
        {
            Id = Guid.NewGuid(),
            LicensePlate = "29B-123.45",
            BusType = busType,
            Capacity = 45,
        };

        await factory.SeedAsync(db => db.Buses.Add(bus));

        return bus;
    }

    private static async Task<SeatLayout> SeedLayoutAsync(TestAppFactory factory, string busType)
    {
        var layout = new SeatLayout
        {
            Id = Guid.NewGuid(),
            BusType = busType,
            NumberOfFloors = 1,
            RowsPerFloor = 9,
            ColumnsPerRow = 5,
            TotalSeats = 45,
            VipSeatPositions = "1-1-1;1-1-2",
        };

        await factory.SeedAsync(db => db.SeatLayouts.Add(layout));

        return layout;
    }

    /// <summary>
    /// Dàn ghế của một xe, đánh số A1, A2… theo toạ độ lưới. Thứ tự trả về đúng thứ tự sơ đồ vẽ
    /// (hàng → cột).
    /// </summary>
    private static async Task<List<Seat>> SeedSeatsAsync(
        TestAppFactory factory,
        Guid busId,
        Guid seatLayoutId,
        int soGhe)
    {
        var seats = new List<Seat>();

        for (var i = 0; i < soGhe; i++)
        {
            seats.Add(new Seat
            {
                Id = Guid.NewGuid(),
                BusId = busId,
                SeatLayoutId = seatLayoutId,
                Floor = 1,
                RowIndex = 1,
                ColumnIndex = i + 1,
                SeatNumber = $"A{i + 1}",
                SeatType = SeatType.Standard,
            });
        }

        await factory.SeedAsync(db => db.Seats.AddRange(seats));

        return seats;
    }

    private static async Task<Trip> SeedTripAsync(TestAppFactory factory, Guid routeId, Guid busId)
    {
        var trip = new Trip
        {
            Id = Guid.NewGuid(),
            RouteId = routeId,
            BusId = busId,
            DepartureTime = new DateTime(2026, 10, 1, 1, 0, 0, DateTimeKind.Utc),
        };

        await factory.SeedAsync(db => db.Trips.Add(trip));

        return trip;
    }

    // =======================================================================================
    // Helper nhóm 3 — soi mô hình EF và migration (xem giải thích ở SeatHoldFlowApiTests)
    // =======================================================================================

    /// <summary>
    /// Context dựng trên provider QUAN HỆ (Npgsql) — điều kiện để soi được mô hình và migration.
    ///
    /// <para>
    /// Vì sao không dùng <see cref="AppDbContext"/> mà <see cref="TestAppFactory"/> cấp: factory cấu
    /// hình provider InMemory, mà InMemory KHÔNG đăng ký dịch vụ quan hệ — gọi
    /// <c>Database.GetMigrations()</c> ở đó ném "Relational-specific methods can only be used when
    /// the context is using a relational database provider", còn <c>IMigrationsAssembly</c> thì
    /// không phân giải được. Nhóm 3 vì vậy dựng context riêng.
    /// </para>
    ///
    /// <para>
    /// KHÔNG mở kết nối nào: soi mô hình là việc dựng metadata trong bộ nhớ, còn migration thì được
    /// nạp bằng phản chiếu từ assembly. Chuỗi kết nối dưới đây cố tình là địa chỉ không tồn tại —
    /// nó chỉ để provider biết mình là ai.
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
    /// Nạp migration theo mã và trả danh sách thao tác <c>Up</c>. Đọc <c>UpOperations</c> của đối
    /// tượng migration (đã được EF nhận diện) thay vì đọc file <c>.cs</c> dạng văn bản — bền hơn với
    /// format mã nguồn và không phụ thuộc đường dẫn tương đối của project test.
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
