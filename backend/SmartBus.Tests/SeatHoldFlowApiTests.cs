using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;
using SmartBus.Api.Data;
using SmartBus.Api.Entities;
using RouteEntity = SmartBus.Api.Entities.Route;
using UserEntity = SmartBus.Api.Entities.User;

namespace SmartBus.Tests;

/// <summary>
/// Test tích hợp CHÉO BỀ MẶT cho luồng chọn ghế (US 2, Sprint 3) — task *"Test API giữ ghế, nhả ghế
/// và chống trùng ghế (xUnit)"* (sheet Sprint 3, dòng 16).
///
/// <para>
/// <b>Vì sao có file này.</b> Ba file test anh em — <see cref="SeatHoldCreateApiTests"/> (Kiên),
/// <see cref="SeatHoldReleaseApiTests"/> (Hoàng), <see cref="TripSeatMapApiTests"/> (Hiếu) — do
/// chính ba tác giả API viết. Mỗi file chỉ chạm MỘT bề mặt, và cả ba đều SEED thẳng dòng
/// <c>SeatHolds</c> xuống CSDL thay vì tạo nó qua API. Hệ quả: chưa ca nào chạy hai bề mặt trong
/// cùng một luồng, nên chưa có gì khoá lại lời hứa thật của story — *ghế đang giữ thì sơ đồ hiện
/// <c>Held</c> và người khác bị chặn; nhả rồi thì ghế tự do trở lại và người khác giữ được*. Đó
/// đúng là khoảng trống file này lấp — cùng lối <c>FeedbackFlowApiTests</c> đã lấp cho US 24.
/// </para>
///
/// <para>
/// <b>Ba nhóm ca:</b>
/// </para>
/// <list type="number">
///   <item>
///     <b>Chốt chống trùng ghế ở tầng mô hình + migration.</b> Partial unique index
///     <c>IX_SeatHolds_TripId_SeatId</c> — <c>UNIQUE ("TripId", "SeatId") WHERE "Status" =
///     'Holding'</c> — là *"ràng buộc quan trọng nhất của Sprint 3"* (quy ước A6, docs/26 §3).
///     Không test nào trước đây khẳng định nó tồn tại: xoá nhầm <c>HasFilter(...)</c> ở
///     <c>AppDbContext.Seat.cs</c> thì không có gì đỏ.
///   </item>
///   <item>
///     <b>Luồng thật xuyên bề mặt</b> — giữ → sơ đồ → nhả → người khác giữ lại, tất cả qua HTTP
///     thật, dữ liệu dựng qua API chứ không seed thẳng <c>SeatHolds</c>.
///   </item>
///   <item>
///     <b>Góc cạnh vòng đời</b> — nhả lần hai idempotent, nhật ký và <c>UpdatedAt</c> sau khi nhả,
///     hình dạng phiên giống nhau ở ba bề mặt.
///   </item>
/// </list>
///
/// <para>
/// ⚠️ <b>Giới hạn của provider InMemory — đọc trước khi tin bộ test này.</b>
/// </para>
/// <list type="bullet">
///   <item>
///     <b>Cuộc đua thật ở tầng CSDL KHÔNG được phủ.</b> InMemory không dựng unique index (kể cả
///     partial), nên nhánh hai request ĐỒNG THỜI cùng giữ một ghế — request thua đâm vào index lúc
///     <c>SaveChanges</c> và nhận 409 câu chung — chỉ chạy được ở PostgreSQL thật. Bộ test này vì
///     vậy khẳng định chốt đó ở tầng <b>mô hình EF</b> và tầng <b>migration</b> (nhóm 1 — dựng một
///     context Npgsql CHỈ để soi metadata, xem <c>TaoDbContextQuanHe</c>): đủ để bắt hồi quy khi ai
///     đó gỡ index/bộ lọc, nhưng KHÔNG thay thế được một lượt chạy trên PostgreSQL.
///     CI hiện tại (`.github/workflows/ci.yml`) chạy <c>dotnet test</c> không kèm service Postgres.
///     Bằng chứng cuộc đua thuộc task *"Kiểm thử đồng thời: 50 khách cùng giữ 1 ghế"* (dòng 28).
///   </item>
///   <item>
///     <b>InMemory không cưỡng chế khoá ngoại</b> nên Route/Bus/Seat/SeatLayout được seed tối thiểu
///     vừa đủ cho truy vấn đọc đúng, không dựng cả cây dữ liệu thật.
///   </item>
///   <item>
///     <b>InMemory không chạy <c>HasData</c></b> — phải seed vai trò trước, không thì token hợp lệ
///     mà người dùng trỏ tới vai trò không tồn tại.
///   </item>
/// </list>
///
/// <para>
/// Dùng lại <see cref="TestAppFactory"/> của JwtAuthTests: app thật + middleware thật + JWT thật,
/// mỗi test một CSDL InMemory riêng.
/// </para>
/// </summary>
public class SeatHoldFlowApiTests
{
    private const string CreateUrl = "/api/seat-holds";

    /// <summary>Mã migration dựng bảng <c>SeatHolds</c> — nơi chốt chống trùng ghế phải có thật.</summary>
    private const string MigrationSeatHolds = "20261006122313_Sprint3_Seats_SeatLayouts_SeatHolds";

    /// <summary>Tên index chống trùng ghế, đúng như EF sinh ra ở migration Sprint 3.</summary>
    private const string IndexChongTrungGhe = "IX_SeatHolds_TripId_SeatId";

    /// <summary>Bộ lọc của partial unique index — nguyên văn, khớp <c>AppDbContext.Seat.cs</c>.</summary>
    private const string BoLocChiGiu = "\"Status\" = 'Holding'";

    // =======================================================================================
    // Nhóm 1 — Chốt chống trùng ghế ở tầng mô hình và migration
    // =======================================================================================

    /// <summary>
    /// Mô hình EF phải khai ĐÚNG MỘT index trên <c>(TripId, SeatId)</c>, unique, kèm bộ lọc
    /// <c>Status = 'Holding'</c>. Ba cách hỏng đều bị ca này bắt: (a) bỏ <c>IsUnique()</c> → mất
    /// chốt, hai khách cùng giữ một ghế lọt xuống CSDL; (b) bỏ <c>HasFilter(...)</c> → biến thành
    /// unique cứng, ghế đã NHẢ vẫn bị chặn vĩnh viễn; (c) đổi bộ lọc sang trạng thái khác → chốt
    /// canh sai cột.
    /// </summary>
    [Fact]
    public void Mo_hinh_khai_dung_mot_index_unique_co_dieu_kien_tren_TripId_SeatId()
    {
        using var db = TaoDbContextQuanHe();

        var entityType = db.Model.FindEntityType(typeof(SeatHold));
        Assert.NotNull(entityType);

        var indexesTrenCapGhe = entityType!.GetIndexes()
            .Where(index => index.Properties.Select(p => p.Name).SequenceEqual(["TripId", "SeatId"]))
            .ToList();

        var index = Assert.Single(indexesTrenCapGhe);

        Assert.True(index.IsUnique, "Index (TripId, SeatId) phải unique — thiếu IsUnique() là mất chốt chống trùng ghế.");
        Assert.Equal(BoLocChiGiu, index.GetFilter());
    }

    /// <summary>
    /// Không được có bất kỳ index UNIQUE nào khác trên <c>SeatHold</c> ngoài cặp <c>(TripId, SeatId)</c>
    /// đã có điều kiện — đặc biệt là một unique cứng trên cùng cặp cột (Ca này bắt hồi quy "thêm
    /// index mà quên bộ lọc" theo hướng khác với ca trên: hai index cùng cặp cột, một có lọc một không).
    /// </summary>
    [Fact]
    public void Mo_hinh_khong_co_unique_cung_tren_cap_TripId_SeatId()
    {
        using var db = TaoDbContextQuanHe();

        var entityType = db.Model.FindEntityType(typeof(SeatHold))!;

        // Unique cứng trên đúng cặp (TripId, SeatId) = chặn cả ghế đã Released/Expired → ghế không
        // bao giờ trở lại sơ đồ được. Chỉ được phép tồn tại unique CÓ điều kiện.
        var uniqueCung = entityType.GetIndexes()
            .Where(index => index.IsUnique)
            .Where(index => index.Properties.Select(p => p.Name).SequenceEqual(["TripId", "SeatId"]))
            .Where(index => index.GetFilter() is null)
            .ToList();

        Assert.Empty(uniqueCung);
    }

    /// <summary>
    /// Chốt phải có thật trong CSDL, không chỉ nằm trong mô hình EF: migration dựng bảng
    /// <c>SeatHolds</c> phải sinh ra <c>CreateIndexOperation</c> unique kèm đúng bộ lọc. Mô hình
    /// khai mà migration không mang — hoặc migration bị sinh lại thiếu <c>filter</c> — thì chốt
    /// không tồn tại ở PostgreSQL, nơi nó thực sự làm việc.
    /// </summary>
    [Fact]
    public void Migration_Sprint3_sinh_index_unique_co_dieu_kien_xuong_CSDL()
    {
        using var db = TaoDbContextQuanHe();

        // Migration phải được EF nhận diện — nếu ai đó xoá/đổi tên file migration thì dừng ngay ở đây
        // với thông báo rõ, thay vì ném KeyNotFoundException khó đọc ở dưới.
        Assert.Contains(MigrationSeatHolds, db.Database.GetMigrations());

        var operation = FindCreateIndexOperation(db, MigrationSeatHolds, IndexChongTrungGhe);

        Assert.True(operation.IsUnique, "Migration phải sinh index unique — thiếu là chốt chống trùng ghế không có thật trong CSDL.");
        Assert.Equal(BoLocChiGiu, operation.Filter);
    }

    /// <summary>
    /// Migration cũng không được sinh unique cứng trên <c>(TripId, SeatId)</c> — cùng lý do ca mô
    /// hình ở trên, nhưng ở tầng CSDL: unique cứng ở đó là ghế đã nhả vẫn bị chặn vĩnh viễn, và
    /// sửa lại phải sinh migration mới (việc của chủ CSDL).
    /// </summary>
    [Fact]
    public void Migration_khong_sinh_unique_cung_tren_cap_TripId_SeatId()
    {
        using var db = TaoDbContextQuanHe();

        var migrationsAssembly = db.GetService<IMigrationsAssembly>();

        var uniqueCung = MigrationOperations(migrationsAssembly, db, MigrationSeatHolds)
            .OfType<CreateIndexOperation>()
            .Where(operation => operation.Table == "SeatHolds" && operation.IsUnique && operation.Filter is null)
            .Where(operation => operation.Columns.SequenceEqual(["TripId", "SeatId"]))
            .ToList();

        Assert.Empty(uniqueCung);
    }

    // =======================================================================================
    // Nhóm 2 — Luồng thật xuyên bề mặt: giữ → sơ đồ → nhả → giữ lại
    // =======================================================================================

    /// <summary>Điểm khởi đầu của story: chưa ai giữ gì thì mọi ghế trên sơ đồ đều "Available".</summary>
    [Fact]
    public async Task So_do_ban_dau_moi_ghe_deu_Available()
    {
        using var factory = new TestAppFactory();
        var (trip, _, seats) = await SeedChuyenDayDuAsync(factory);

        var soDo = await DocSoDoGheAsync(factory, trip.Id);

        foreach (var seat in seats)
        {
            Assert.Equal("Available", TrangThaiGhe(soDo, seat.Id));
        }
    }

    /// <summary>
    /// Nửa "chặn" của chốt chống trùng ghế, nhìn từ phía hành khách: A giữ ghế → sơ đồ hiện ghế đó
    /// <c>Held</c>, các ghế còn lại vẫn <c>Available</c>, và B giữ cùng ghế thì bị 409 kèm đúng số
    /// ghế vướng. Đây là ca thường gặp nhất của màn hình chọn ghế.
    /// </summary>
    [Fact]
    public async Task Giu_ghe_thi_so_do_hien_Held_va_nguoi_khac_bi_chan()
    {
        using var factory = new TestAppFactory();
        var (trip, _, seats) = await SeedChuyenDayDuAsync(factory);
        var (gheBiGiu, gheConLai) = (seats[0], seats[1]);

        var a = await SignInAsPassengerAsync(factory);
        var giuResponse = await a.PostAsJsonAsync(
            CreateUrl, new { tripId = trip.Id, seatIds = new[] { gheBiGiu.Id } });

        Assert.Equal(HttpStatusCode.Created, giuResponse.StatusCode);

        var soDo = await DocSoDoGheAsync(factory, trip.Id);
        Assert.Equal("Held", TrangThaiGhe(soDo, gheBiGiu.Id));
        Assert.Equal("Available", TrangThaiGhe(soDo, gheConLai.Id));

        var b = await SignInAsPassengerAsync(factory);
        var biChanResponse = await b.PostAsJsonAsync(
            CreateUrl, new { tripId = trip.Id, seatIds = new[] { gheBiGiu.Id } });

        Assert.Equal(HttpStatusCode.Conflict, biChanResponse.StatusCode);
        Assert.Equal(
            $"Ghế {gheBiGiu.SeatNumber} đang được giữ cho chuyến này.",
            await MessageAsync(biChanResponse));
    }

    /// <summary>
    /// MỆNH ĐỀ TRUNG TÂM của story, và là ca chưa file test nào phủ: nhả ghế phải THẬT SỰ trả ghế
    /// về cho người sau. Đi trọn vòng qua HTTP thật — A giữ, A nhả, sơ đồ về <c>Available</c>, rồi
    /// B giữ đúng ghế đó và phải thành công. Nếu ai đó làm hỏng nhánh nhả (ví dụ chỉ đổi trạng thái
    /// trong bộ nhớ mà không lưu, hoặc bộ lọc sơ đồ nhìn nhầm cột), ca này đỏ.
    /// </summary>
    [Fact]
    public async Task Nha_ghe_thi_so_do_ve_Available_va_nguoi_khac_giu_lai_duoc()
    {
        using var factory = new TestAppFactory();
        var (trip, _, seats) = await SeedChuyenDayDuAsync(factory);
        var ghe = seats[0];

        var a = await SignInAsPassengerAsync(factory);
        var giuA = await a.PostAsJsonAsync(CreateUrl, new { tripId = trip.Id, seatIds = new[] { ghe.Id } });
        Assert.Equal(HttpStatusCode.Created, giuA.StatusCode);
        var phienA = (await ReadJsonAsync(giuA)).GetProperty("sessionCode").GetString()!;

        var nhaA = await a.PostAsync($"/api/seat-holds/{phienA}/release", content: null);
        Assert.Equal(HttpStatusCode.OK, nhaA.StatusCode);
        Assert.Equal("Released", (await ReadJsonAsync(nhaA)).GetProperty("status").GetString());

        var soDo = await DocSoDoGheAsync(factory, trip.Id);
        Assert.Equal("Available", TrangThaiGhe(soDo, ghe.Id));

        var b = await SignInAsPassengerAsync(factory);
        var giuB = await b.PostAsJsonAsync(CreateUrl, new { tripId = trip.Id, seatIds = new[] { ghe.Id } });

        Assert.Equal(HttpStatusCode.Created, giuB.StatusCode);
        Assert.Equal("Holding", (await ReadJsonAsync(giuB)).GetProperty("status").GetString());
    }

    /// <summary>
    /// Nhả cả PHIÊN nhiều ghế phải trả tự do cho cả nhóm, không chỉ ghế đầu — hợp đồng chốt "tạo /
    /// gia hạn / nhả / hết hạn đều đổi theo nhóm" (docs/26 §1). Người sau giữ lại được trọn nhóm.
    /// </summary>
    [Fact]
    public async Task Nha_ca_phien_nhieu_ghe_thi_nguoi_khac_giu_lai_duoc_ca_nhom()
    {
        using var factory = new TestAppFactory();
        var (trip, _, seats) = await SeedChuyenDayDuAsync(factory);
        var nhomGhe = new[] { seats[0].Id, seats[1].Id };

        var a = await SignInAsPassengerAsync(factory);
        var giuA = await a.PostAsJsonAsync(CreateUrl, new { tripId = trip.Id, seatIds = nhomGhe });
        Assert.Equal(HttpStatusCode.Created, giuA.StatusCode);
        var phienA = (await ReadJsonAsync(giuA)).GetProperty("sessionCode").GetString()!;

        var soDoTruocKhiNha = await DocSoDoGheAsync(factory, trip.Id);
        Assert.Equal("Held", TrangThaiGhe(soDoTruocKhiNha, seats[0].Id));
        Assert.Equal("Held", TrangThaiGhe(soDoTruocKhiNha, seats[1].Id));

        var nhaA = await a.PostAsync($"/api/seat-holds/{phienA}/release", content: null);
        Assert.Equal(HttpStatusCode.OK, nhaA.StatusCode);

        var soDoSauKhiNha = await DocSoDoGheAsync(factory, trip.Id);
        Assert.Equal("Available", TrangThaiGhe(soDoSauKhiNha, seats[0].Id));
        Assert.Equal("Available", TrangThaiGhe(soDoSauKhiNha, seats[1].Id));

        var b = await SignInAsPassengerAsync(factory);
        var giuB = await b.PostAsJsonAsync(CreateUrl, new { tripId = trip.Id, seatIds = nhomGhe });

        Assert.Equal(HttpStatusCode.Created, giuB.StatusCode);
        Assert.Equal(
            new[] { seats[0].SeatNumber, seats[1].SeatNumber },
            SeatNumbersOf(await ReadJsonAsync(giuB)));
    }

    /// <summary>
    /// Chốt chống trùng ghế là của CẶP (chuyến, ghế), không phải của ghế: cùng một ghế ở chuyến khác
    /// phải giữ được, và sơ đồ hai chuyến độc lập nhau. Nếu index bị khai thiếu <c>TripId</c>, ghế
    /// bị khoá vĩnh viễn trên mọi chuyến — ca này bắt được.
    /// </summary>
    [Fact]
    public async Task Cung_mot_ghe_o_chuyen_khac_thi_khong_bi_chan()
    {
        using var factory = new TestAppFactory();
        var route = await SeedRouteAsync(factory);
        var bus = await SeedBusAsync(factory);
        var layout = await SeedLayoutAsync(factory, bus.BusType);
        var seats = await SeedSeatsAsync(factory, bus.Id, layout.Id, soGhe: 2);

        // Hai chuyến CÙNG xe nên dùng chung dàn ghế — đúng ca "cùng một ghế, khác chuyến".
        var chuyen1 = await SeedTripAsync(factory, route.Id, bus.Id);
        var chuyen2 = await SeedTripAsync(factory, route.Id, bus.Id);
        var ghe = seats[0];

        var a = await SignInAsPassengerAsync(factory);
        var giuChuyen1 = await a.PostAsJsonAsync(
            CreateUrl, new { tripId = chuyen1.Id, seatIds = new[] { ghe.Id } });
        Assert.Equal(HttpStatusCode.Created, giuChuyen1.StatusCode);

        var b = await SignInAsPassengerAsync(factory);
        var giuChuyen2 = await b.PostAsJsonAsync(
            CreateUrl, new { tripId = chuyen2.Id, seatIds = new[] { ghe.Id } });
        Assert.Equal(HttpStatusCode.Created, giuChuyen2.StatusCode);

        // Sơ đồ đọc theo TỪNG chuyến: cả hai chuyến đều Held ở ghế đó, không chuyến nào lấn sang chuyến kia.
        Assert.Equal("Held", TrangThaiGhe(await DocSoDoGheAsync(factory, chuyen1.Id), ghe.Id));
        Assert.Equal("Held", TrangThaiGhe(await DocSoDoGheAsync(factory, chuyen2.Id), ghe.Id));
    }

    /// <summary>
    /// Nhả rồi thì chính người cũ giữ lại được, và nhận MÃ PHIÊN MỚI — phiên cũ không còn chặn ghế.
    /// Ca này bắt hồi quy "nhả mà quên đóng phiên cũ" theo hướng nhìn từ phía người giữ.
    /// </summary>
    [Fact]
    public async Task Nguoi_giu_tu_nha_roi_tu_giu_lai_duoc_voi_phien_moi()
    {
        using var factory = new TestAppFactory();
        var (trip, _, seats) = await SeedChuyenDayDuAsync(factory);
        var ghe = seats[0];

        var a = await SignInAsPassengerAsync(factory);
        var giuLan1 = await a.PostAsJsonAsync(CreateUrl, new { tripId = trip.Id, seatIds = new[] { ghe.Id } });
        Assert.Equal(HttpStatusCode.Created, giuLan1.StatusCode);
        var phienLan1 = (await ReadJsonAsync(giuLan1)).GetProperty("sessionCode").GetString()!;

        var nha = await a.PostAsync($"/api/seat-holds/{phienLan1}/release", content: null);
        Assert.Equal(HttpStatusCode.OK, nha.StatusCode);

        var giuLan2 = await a.PostAsJsonAsync(CreateUrl, new { tripId = trip.Id, seatIds = new[] { ghe.Id } });
        Assert.Equal(HttpStatusCode.Created, giuLan2.StatusCode);

        var phienLan2 = (await ReadJsonAsync(giuLan2)).GetProperty("sessionCode").GetString();
        Assert.NotEqual(phienLan1, phienLan2);
    }

    /// <summary>
    /// Chính người đang giữ mà giữ lại cùng ghế thì vẫn 409 — ghế đang bị chặn cho MỌI người, kể cả
    /// chủ phiên. Kiểm chéo: sau lượt 409 đó, sơ đồ vẫn Held và phiên cũ vẫn nguyên (không sinh phiên mới).
    /// </summary>
    [Fact]
    public async Task Chinh_nguoi_dang_giu_giu_lai_cung_ghe_thi_tra_409()
    {
        using var factory = new TestAppFactory();
        var (trip, _, seats) = await SeedChuyenDayDuAsync(factory);
        var ghe = seats[0];

        var a = await SignInAsPassengerAsync(factory);
        var giuLan1 = await a.PostAsJsonAsync(CreateUrl, new { tripId = trip.Id, seatIds = new[] { ghe.Id } });
        Assert.Equal(HttpStatusCode.Created, giuLan1.StatusCode);
        var phienLan1 = (await ReadJsonAsync(giuLan1)).GetProperty("sessionCode").GetString()!;

        var giuLan2 = await a.PostAsJsonAsync(CreateUrl, new { tripId = trip.Id, seatIds = new[] { ghe.Id } });

        Assert.Equal(HttpStatusCode.Conflict, giuLan2.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Không sinh phiên thứ hai: vẫn đúng một dòng cho ghế đó.
        Assert.Equal(1, await db.SeatHolds.CountAsync());
        Assert.Equal(phienLan1, (await db.SeatHolds.SingleAsync()).SessionCode);

        Assert.Equal("Held", TrangThaiGhe(await DocSoDoGheAsync(factory, trip.Id), ghe.Id));
    }

    // =======================================================================================
    // Nhóm 3 — Góc cạnh vòng đời
    // =======================================================================================

    /// <summary>
    /// Nhả là thao tác KẾT THÚC nên gọi lại phải 200 (hợp đồng: "gọi lại bao nhiêu lần kết quả vẫn
    /// là 200"), KHÔNG ghi thêm nhật ký, và sơ đồ vẫn phải <c>Available</c>. Đây là ca dùng thật:
    /// đồng hồ về 0 và modal tự gọi release trong khi job nền đã quét trước đó vài giây.
    /// </summary>
    [Fact]
    public async Task Nha_lan_hai_tra_200_khong_ghi_them_log_va_so_do_van_Available()
    {
        using var factory = new TestAppFactory();
        var (trip, _, seats) = await SeedChuyenDayDuAsync(factory);
        var ghe = seats[0];

        var a = await SignInAsPassengerAsync(factory);
        var giu = await a.PostAsJsonAsync(CreateUrl, new { tripId = trip.Id, seatIds = new[] { ghe.Id } });
        var phien = (await ReadJsonAsync(giu)).GetProperty("sessionCode").GetString()!;

        var nhaLan1 = await a.PostAsync($"/api/seat-holds/{phien}/release", content: null);
        Assert.Equal(HttpStatusCode.OK, nhaLan1.StatusCode);

        var nhaLan2 = await a.PostAsync($"/api/seat-holds/{phien}/release", content: null);

        Assert.Equal(HttpStatusCode.OK, nhaLan2.StatusCode);
        Assert.Equal("Released", (await ReadJsonAsync(nhaLan2)).GetProperty("status").GetString());

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Một dòng Held lúc giữ + một dòng Released lúc nhả = 2. Lượt nhả thứ hai không thêm gì.
        Assert.Equal(2, await db.SeatHoldLogs.CountAsync());

        Assert.Equal("Available", TrangThaiGhe(await DocSoDoGheAsync(factory, trip.Id), ghe.Id));
    }

    /// <summary>
    /// Nhả phải để lại vết đúng như hợp đồng: mỗi lượt giữ một dòng log <c>Released</c> trỏ đúng
    /// <c>SeatHoldId</c>, dòng giữ được ghi <c>UpdatedAt</c> (A4), và <c>ExpiresAt</c> GIỮ NGUYÊN hạn
    /// cũ — nhả chỉ đổi trạng thái, không dời hạn (docs/26 §1). Nhóm hai ghế để bắt ca chỉ ghi log
    /// cho ghế đầu.
    /// </summary>
    [Fact]
    public async Task Nha_ghi_log_Released_cho_moi_ghe_va_dat_UpdatedAt_giu_nguyen_han()
    {
        using var factory = new TestAppFactory();
        var (trip, _, seats) = await SeedChuyenDayDuAsync(factory);
        var nhomGhe = new[] { seats[0].Id, seats[1].Id };

        var a = await SignInAsPassengerAsync(factory);
        var giu = await a.PostAsJsonAsync(CreateUrl, new { tripId = trip.Id, seatIds = nhomGhe });
        var giuBody = await ReadJsonAsync(giu);
        var phien = giuBody.GetProperty("sessionCode").GetString()!;
        var hanCu = giuBody.GetProperty("expiresAt").GetDateTime();

        var nha = await a.PostAsync($"/api/seat-holds/{phien}/release", content: null);
        Assert.Equal(HttpStatusCode.OK, nha.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var holds = await db.SeatHolds.Where(h => h.SessionCode == phien).ToListAsync();
        Assert.Equal(2, holds.Count);
        Assert.All(holds, hold => Assert.Equal(SeatHoldStatus.Released, hold.Status));
        Assert.All(holds, hold => Assert.NotNull(hold.UpdatedAt));

        // Hạn giữ nguyên hạn cũ, không dời theo lúc nhả.
        Assert.All(holds, hold => Assert.Equal(hanCu, hold.ExpiresAt, TimeSpan.FromSeconds(1)));

        var logReleased = await db.SeatHoldLogs
            .Where(log => log.SessionCode == phien && log.Action == SeatHoldLogAction.Released)
            .ToListAsync();

        Assert.Equal(2, logReleased.Count);
        Assert.Equal(
            holds.Select(h => h.Id).OrderBy(id => id),
            logReleased.Select(log => log.SeatHoldId).OrderBy(id => id));
    }

    /// <summary>
    /// Bất biến xuyên bề mặt: cùng MỘT phiên thật, ba endpoint đọc/ghi khác nhau phải trả về CÙNG
    /// một hình dạng <c>SeatHoldSession</c> — cùng bộ khoá, cùng <c>sessionCode</c>, cùng
    /// <c>tripId</c>, cùng <c>seatNumbers</c>. Màn hình đếm ngược đọc cả ba nên lệch hình dạng ở một
    /// endpoint là vỡ giao diện. Chỉ <c>status</c> / <c>canExtend</c> / <c>expiresAt</c> được khác,
    /// theo đúng chuyển trạng thái của vòng đời.
    /// </summary>
    [Fact]
    public async Task Ba_be_mat_tra_ve_cung_hinh_dang_phien()
    {
        using var factory = new TestAppFactory();
        var (trip, _, seats) = await SeedChuyenDayDuAsync(factory);
        var nhomGhe = new[] { seats[0].Id, seats[1].Id };

        var a = await SignInAsPassengerAsync(factory);

        var giuBody = await ReadJsonAsync(await a.PostAsJsonAsync(
            CreateUrl, new { tripId = trip.Id, seatIds = nhomGhe }));
        var phien = giuBody.GetProperty("sessionCode").GetString()!;

        var traBody = await ReadJsonAsync(await a.GetAsync($"/api/seat-holds/{phien}"));
        var nhaBody = await ReadJsonAsync(
            await a.PostAsync($"/api/seat-holds/{phien}/release", content: null));

        // Cùng bộ khoá ở cả ba.
        var boKhoa = new[] { "canExtend", "expiresAt", "seatNumbers", "sessionCode", "status", "tripId" };
        Assert.Equal(boKhoa, PropertyNamesOf(giuBody));
        Assert.Equal(boKhoa, PropertyNamesOf(traBody));
        Assert.Equal(boKhoa, PropertyNamesOf(nhaBody));

        // Cùng danh tính phiên ở cả ba.
        foreach (var body in new[] { giuBody, traBody, nhaBody })
        {
            Assert.Equal(phien, body.GetProperty("sessionCode").GetString());
            Assert.Equal(trip.Id, body.GetProperty("tripId").GetGuid());
            Assert.Equal(
                new[] { seats[0].SeatNumber, seats[1].SeatNumber },
                SeatNumbersOf(body));
        }

        // Chỉ ba trường vòng đời được đổi, và đổi đúng chiều.
        Assert.Equal("Holding", giuBody.GetProperty("status").GetString());
        Assert.True(giuBody.GetProperty("canExtend").GetBoolean());

        Assert.Equal("Holding", traBody.GetProperty("status").GetString());

        Assert.Equal("Released", nhaBody.GetProperty("status").GetString());
        Assert.False(nhaBody.GetProperty("canExtend").GetBoolean());
    }

    // =======================================================================================
    // Helper — mỗi lớp test tự chép, không có lớp base chung (đúng lệ đang có của dự án)
    // =======================================================================================

    /// <summary>
    /// Context dựng trên provider QUAN HỆ (Npgsql) — điều kiện để soi được mô hình và migration.
    ///
    /// <para>
    /// Vì sao không dùng <see cref="AppDbContext"/> mà <see cref="TestAppFactory"/> cấp: factory cấu
    /// hình provider InMemory, mà InMemory KHÔNG đăng ký dịch vụ quan hệ — gọi
    /// <c>Database.GetMigrations()</c> ở đó ném "Relational-specific methods can only be used when
    /// the context is using a relational database provider", còn <c>IMigrationsAssembly</c> thì
    /// không phân giải được. Nhóm 1 vì vậy dựng context riêng.
    /// </para>
    ///
    /// <para>
    /// KHÔNG mở kết nối nào: soi mô hình là việc dựng metadata trong bộ nhớ, còn migration thì được
    /// nạp bằng phản chiếu từ assembly. Chuỗi kết nối dưới đây cố tình là địa chỉ không tồn tại —
    /// nó chỉ để provider biết mình là ai. Ca test này không chạm tới CSDL thật.
    /// </para>
    ///
    /// <para>
    /// Gói <c>Npgsql.EntityFrameworkCore.PostgreSQL</c> không được khai ở
    /// <c>SmartBus.Tests.csproj</c> mà tới đây theo đường bắc cầu từ <c>SmartBus.Api</c> — đúng
    /// provider mà app dùng thật, nên migration soi được cũng chính là migration chạy ở sản xuất.
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

    private static string[] PropertyNamesOf(JsonElement element)
        => element.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray();

    private static string[] SeatNumbersOf(JsonElement body)
        => body.GetProperty("seatNumbers").EnumerateArray()
            .Select(number => number.GetString() ?? string.Empty)
            .ToArray();

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
    /// (hàng → cột) nên test so được <c>seatNumbers</c> mà không phải tự sắp lại.
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
}
