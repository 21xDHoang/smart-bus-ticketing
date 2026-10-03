using Microsoft.EntityFrameworkCore;
using SmartBus.Api.Data;
using SmartBus.Api.Entities;
using SmartBus.Api.Services;

namespace SmartBus.Tests;

/// <summary>
/// Test phần ruột job sinh chuyến tự động — <see cref="TripGenerationService"/>
/// (story 13, task *"BackgroundService sinh chuyến tự động từ lịch trình theo ngày"* — Nguyễn Duy Kiên).
///
/// Gọi thẳng service với một mốc <c>now</c> ghim sẵn, KHÔNG dựng host: vòng lặp
/// <see cref="TripGenerationBackgroundService"/> chạy theo nhịp một giờ nên chờ nó là test chập chờn
/// và chậm. Phần "job có thật sự được đăng ký vào app không" nằm ở
/// <see cref="TripGenerationWiringTests"/>.
///
/// Dùng provider InMemory như phần còn lại của bộ test. Không seed Seats: InMemory không cưỡng chế
/// khoá ngoại, mà truy vấn của job chỉ đọc Routes / Buses / Trips.
///
/// <b>Mọi mốc thời gian trong file này dựng qua <see cref="GioVietNam"/>, không viết thẳng UTC.</b>
/// Đó là chủ ý: chỗ dễ sai nhất của job là nhầm "ngày" giờ Việt Nam với "ngày" UTC, nên ca test phải
/// đọc ra được giờ Việt Nam chứ không phải một chuỗi UTC phải nhẩm trong đầu.
/// </summary>
public class TripGenerationServiceTests
{
    /// <summary>
    /// Mốc "bây giờ": 19:00 giờ Việt Nam ngày 3/10/2026. Chọn buổi tối để lịch trình trong ngày
    /// (05:00–07:00) đã chạy hết — job lấy ngày hôm nay làm mẫu.
    /// </summary>
    private static readonly DateTime BayGio = GioVietNam(3, 19);

    /// <summary>
    /// Tên CSDL InMemory riêng cho từng ca test. xUnit dựng một instance lớp test mới cho mỗi ca nên
    /// mỗi ca có CSDL sạch — cùng tên là dùng chung dữ liệu.
    /// </summary>
    private readonly string _tenCsdl = $"sinh-chuyen-{Guid.NewGuid()}";

    // ---------------------------------------------------------------------------------------
    // Nhân bản lịch trình sang ngày mai
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Sinh_chuyen_cho_ngay_mai_tu_lich_trinh_hom_nay()
    {
        using var db = TaoDb();
        var tuyen = await SeedTuyenAsync(db);
        var xe = await SeedXeAsync(db);

        await SeedChuyenAsync(db, tuyen, xe, GioVietNam(3, 5));
        await SeedChuyenAsync(db, tuyen, xe, GioVietNam(3, 6));
        await SeedChuyenAsync(db, tuyen, xe, GioVietNam(3, 7));

        var ketQua = await new TripGenerationService(db).GenerateUpcomingAsync(BayGio);

        // 3 chuyến mẫu × 7 ngày trống (4/10 → 10/10).
        Assert.Equal(21, ketQua.TripsCreated);
        Assert.Equal(1, ketQua.RoutesFilled);
        Assert.Equal(0, ketQua.SkippedInactiveBusTrips);

        // Đọc lại bằng context khác để chắc chắn thay đổi đã xuống CSDL, không chỉ nằm trong change
        // tracker của context vừa dùng.
        using var dbDoc = TaoDb();
        var ngayMai = await ChuyenTrongNgayAsync(dbDoc, tuyen.Id, 4);

        Assert.Equal(
            new[] { GioVietNam(4, 5), GioVietNam(4, 6), GioVietNam(4, 7) },
            ngayMai.Select(t => t.DepartureTime));

        // Chuyến sinh tự động ra đời TRƯỚC khi điều xe: chưa có tài xế, đang chờ chạy — đúng luồng
        // US 13 → US 14, và đúng lý do Trip.DriverId phải nullable.
        Assert.All(ngayMai, t =>
        {
            Assert.Equal(TripStatus.Scheduled, t.Status);
            Assert.Null(t.DriverId);
            Assert.Equal(xe.Id, t.BusId);
        });
    }

    [Fact]
    public async Task Chuyen_sang_ngay_giu_nguyen_gio_dia_phuong_chu_khong_phai_gio_utc()
    {
        using var db = TaoDb();
        var tuyen = await SeedTuyenAsync(db);
        var xe = await SeedXeAsync(db);

        // Chuyến sớm nhất trong ngày: 05:00 giờ Việt Nam = 22:00 UTC HÔM TRƯỚC. Đây chính là ca mà
        // cách gộp theo ngày UTC sẽ làm sai — chuyến thuộc ngày lịch 3/10 giờ Việt Nam nhưng nằm ở
        // ngày UTC 2/10, nên gộp theo UTC là cắt đôi một ngày khai thác.
        var chuyenSom = await SeedChuyenAsync(db, tuyen, xe, GioVietNam(3, 5));
        Assert.Equal(new DateTime(2026, 10, 2, 22, 0, 0, DateTimeKind.Utc), chuyenSom.DepartureTime);

        await new TripGenerationService(db).GenerateUpcomingAsync(BayGio);

        using var dbDoc = TaoDb();
        var banSao = await dbDoc.Trips
            .SingleAsync(t => t.RouteId == tuyen.Id && t.Id != chuyenSom.Id && t.DepartureTime < GioVietNam(4, 6));

        // 05:00 giờ Việt Nam ngày 4/10. Nếu job cộng nhầm theo ngày UTC thì chuyến này sẽ rơi vào
        // 22:00 UTC ngày 4/10, tức 05:00 giờ Việt Nam ngày 5/10 — lệch hẳn một ngày.
        Assert.Equal(GioVietNam(4, 5), banSao.DepartureTime);
    }

    [Fact]
    public async Task Moi_tuyen_nhan_ban_mau_cua_rieng_minh()
    {
        using var db = TaoDb();
        var tuyen1 = await SeedTuyenAsync(db);
        var tuyen2 = await SeedTuyenAsync(db);
        var xe = await SeedXeAsync(db);

        // Hai tuyến chạy giờ khác nhau — nhân bản chéo là hỏng lịch trình của cả hai.
        await SeedChuyenAsync(db, tuyen1, xe, GioVietNam(3, 5));
        await SeedChuyenAsync(db, tuyen1, xe, GioVietNam(3, 6));
        await SeedChuyenAsync(db, tuyen2, xe, GioVietNam(3, 7, 30));

        var ketQua = await new TripGenerationService(db).GenerateUpcomingAsync(BayGio);

        Assert.Equal(21, ketQua.TripsCreated); // (2 + 1) chuyến × 7 ngày
        Assert.Equal(2, ketQua.RoutesFilled);

        using var dbDoc = TaoDb();
        var cuaTuyen2 = await ChuyenTrongNgayAsync(dbDoc, tuyen2.Id, 4);
        Assert.Equal(GioVietNam(4, 7, 30), Assert.Single(cuaTuyen2).DepartureTime);
    }

    // ---------------------------------------------------------------------------------------
    // Tầm nhìn 7 ngày
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Chi_lap_trong_tam_bay_ngay_ke_tu_ngay_mai()
    {
        using var db = TaoDb();
        var tuyen = await SeedTuyenAsync(db);
        var xe = await SeedXeAsync(db);
        await SeedChuyenAsync(db, tuyen, xe, GioVietNam(3, 5));

        var ketQua = await new TripGenerationService(db).GenerateUpcomingAsync(BayGio);

        Assert.Equal(7, ketQua.TripsCreated);

        using var dbDoc = TaoDb();
        var moc = await dbDoc.Trips
            .Where(t => t.DepartureTime > BayGio)
            .Select(t => t.DepartureTime)
            .ToListAsync();

        // Đúng 4/10 → 10/10 giờ Việt Nam: không sót ngày mai, không vượt quá hạn 7 ngày.
        Assert.Equal(
            new[] { 4, 5, 6, 7, 8, 9, 10 }.Select(n => new DateTime(2026, 10, n)).ToList(),
            moc.Select(x => x.AddHours(7).Date).Distinct().OrderBy(x => x).ToList());
    }

    [Fact]
    public async Task Chay_hai_luot_lien_tiep_luot_sau_khong_sinh_them()
    {
        using var db = TaoDb();
        var tuyen = await SeedTuyenAsync(db);
        var xe = await SeedXeAsync(db);
        await SeedChuyenAsync(db, tuyen, xe, GioVietNam(3, 5));

        var service = new TripGenerationService(db);

        Assert.Equal(7, (await service.GenerateUpcomingAsync(BayGio)).TripsCreated);
        // Lượt sau (app khởi động lại, hoặc bản app thứ hai chạy song song) thấy mọi ngày đã có
        // chuyến nên không được sinh thêm — nếu không, mỗi lượt chạy lại nhân đôi lịch trình.
        Assert.Equal(0, (await service.GenerateUpcomingAsync(BayGio)).TripsCreated);
    }

    // ---------------------------------------------------------------------------------------
    // Ngày đã có chủ thì không đụng vào
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Ngay_da_co_chuyen_thi_khong_sinh_them_cho_ngay_do()
    {
        using var db = TaoDb();
        var tuyen = await SeedTuyenAsync(db);
        var xe = await SeedXeAsync(db);
        await SeedChuyenAsync(db, tuyen, xe, GioVietNam(3, 5));

        // Quản lý đã tự xếp một chuyến cho ngày mai.
        var chuyenTuXep = await SeedChuyenAsync(db, tuyen, xe, GioVietNam(4, 9));

        var ketQua = await new TripGenerationService(db).GenerateUpcomingAsync(BayGio);

        // Ngày mai bị bỏ qua, chỉ còn 6 ngày trống (5/10 → 10/10).
        Assert.Equal(6, ketQua.TripsCreated);

        using var dbDoc = TaoDb();
        var ngayMai = await ChuyenTrongNgayAsync(dbDoc, tuyen.Id, 4);
        Assert.Equal(chuyenTuXep.Id, Assert.Single(ngayMai).Id);
    }

    [Fact]
    public async Task Ngay_bi_huy_het_chuyen_khong_bi_dung_lai()
    {
        using var db = TaoDb();
        var tuyen = await SeedTuyenAsync(db);
        var xe = await SeedXeAsync(db);
        await SeedChuyenAsync(db, tuyen, xe, GioVietNam(3, 5));

        // Quản lý cho nghỉ lễ: mọi chuyến ngày mai đã huỷ. Chuyến đã huỷ KHÔNG nằm trong mẫu,
        // nhưng ngày chứa nó vẫn là ngày "đã có chủ" — nếu tính ngày trống theo mẫu thì job sẽ
        // dựng lại đúng cái ngày mà quản lý vừa cho nghỉ.
        await SeedChuyenAsync(db, tuyen, xe, GioVietNam(4, 8), TripStatus.Cancelled);

        var ketQua = await new TripGenerationService(db).GenerateUpcomingAsync(BayGio);

        Assert.Equal(6, ketQua.TripsCreated);

        using var dbDoc = TaoDb();
        var ngayMai = await ChuyenTrongNgayAsync(dbDoc, tuyen.Id, 4);
        Assert.Equal(TripStatus.Cancelled, Assert.Single(ngayMai).Status);
    }

    [Fact]
    public async Task Khong_sao_chep_chuyen_da_huy_cua_ngay_mau()
    {
        using var db = TaoDb();
        var tuyen = await SeedTuyenAsync(db);
        var xe = await SeedXeAsync(db);

        await SeedChuyenAsync(db, tuyen, xe, GioVietNam(3, 5));
        await SeedChuyenAsync(db, tuyen, xe, GioVietNam(3, 6));
        await SeedChuyenAsync(db, tuyen, xe, GioVietNam(3, 7), TripStatus.Cancelled);

        var ketQua = await new TripGenerationService(db).GenerateUpcomingAsync(BayGio);

        // Chỉ 2 chuyến dùng được × 7 ngày — chuyến đã huỷ không được hồi sinh ở ngày tương lai.
        Assert.Equal(14, ketQua.TripsCreated);
    }

    // ---------------------------------------------------------------------------------------
    // Tuyến / xe không còn khai thác
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Tuyen_ngung_khai_thac_khong_bi_dung_lai()
    {
        using var db = TaoDb();
        var tuyen = await SeedTuyenAsync(db, RouteStatus.Inactive);
        var xe = await SeedXeAsync(db);
        await SeedChuyenAsync(db, tuyen, xe, GioVietNam(3, 5));

        var ketQua = await new TripGenerationService(db).GenerateUpcomingAsync(BayGio);

        // Đặt Routes.Status = Inactive là công tắc duy nhất để ngừng một tuyến; không tôn trọng nó
        // thì job sẽ mãi dựng lại một tuyến đã bỏ, và không có cách nào tắt.
        Assert.Equal(0, ketQua.TripsCreated);

        using var dbDoc = TaoDb();
        Assert.Equal(1, await dbDoc.Trips.CountAsync());
    }

    [Fact]
    public async Task Chuyen_mau_cua_xe_da_rut_khoi_doi_bi_bo_qua()
    {
        using var db = TaoDb();
        var tuyen = await SeedTuyenAsync(db);
        var xeDangChay = await SeedXeAsync(db);
        var xeBaoDuong = await SeedXeAsync(db, BusStatus.Maintenance);

        await SeedChuyenAsync(db, tuyen, xeDangChay, GioVietNam(3, 5));
        await SeedChuyenAsync(db, tuyen, xeBaoDuong, GioVietNam(3, 6));

        var ketQua = await new TripGenerationService(db).GenerateUpcomingAsync(BayGio);

        // Cùng luật với API sinh chuyến (RouteTripsService từ chối xe không Active): job không được
        // tạo ra những chuyến mà chính API của dự án sẽ từ chối.
        Assert.Equal(7, ketQua.TripsCreated);
        Assert.Equal(1, ketQua.SkippedInactiveBusTrips);

        using var dbDoc = TaoDb();
        // Xe bảo dưỡng chỉ còn đúng chuyến mẫu hôm nay, không có bản sao nào ở ngày tương lai.
        Assert.Single(await dbDoc.Trips.Where(t => t.BusId == xeBaoDuong.Id).ToListAsync());
    }

    [Fact]
    public async Task Tuyen_chi_co_chuyen_cua_xe_da_rut_khoi_doi_thi_dung_im_va_bao_len()
    {
        using var db = TaoDb();
        var tuyen = await SeedTuyenAsync(db);
        var xeBaoDuong = await SeedXeAsync(db, BusStatus.Maintenance);
        await SeedChuyenAsync(db, tuyen, xeBaoDuong, GioVietNam(3, 5));

        var ketQua = await new TripGenerationService(db).GenerateUpcomingAsync(BayGio);

        // Không nhân bản được gì, nhưng đây là tuyến ĐỨNG IM chứ không phải tuyến không có việc —
        // phải báo lên để người điều xe biết mà thay xe, không được im lặng.
        Assert.Equal(0, ketQua.TripsCreated);
        Assert.Equal(1, ketQua.SkippedInactiveBusTrips);
    }

    // ---------------------------------------------------------------------------------------
    // Ca rỗng
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Tuyen_chua_tung_co_chuyen_thi_dung_im()
    {
        using var db = TaoDb();
        await SeedTuyenAsync(db);
        await SeedXeAsync(db);

        var ketQua = await new TripGenerationService(db).GenerateUpcomingAsync(BayGio);

        // Giới hạn đã biết của thiết kế "không có bảng mẫu" (A8.3): chưa có chuyến nào thì không có
        // gì để nhân bản. Quản lý phải gọi API sinh chuyến một lần để gieo lịch trình đầu tiên.
        Assert.Equal(0, ketQua.TripsCreated);

        using var dbDoc = TaoDb();
        Assert.Equal(0, await dbDoc.Trips.CountAsync());
    }

    [Fact]
    public async Task Khong_co_tuyen_nao_thi_tra_ve_rong_va_khong_nem_loi()
    {
        using var db = TaoDb();

        var ketQua = await new TripGenerationService(db).GenerateUpcomingAsync(BayGio);

        // Lượt chạy thường gặp nhất trong đời thật là lượt không có gì để làm — nó phải sạch.
        Assert.Equal(0, ketQua.TripsCreated);
        Assert.Equal(0, ketQua.RoutesFilled);
    }

    // ---------------------------------------------------------------------------------------
    // Helper — mỗi lớp test tự chép, không có lớp base chung (đúng lệ đang có của dự án)
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Mốc UTC của một giờ Việt Nam trong tháng 10/2026. Viết test bằng hàm này để ca kiểm đọc ra
    /// "05:00 ngày 3/10 giờ Việt Nam" chứ không phải một mốc UTC phải nhẩm trong đầu.
    /// </summary>
    private static DateTime GioVietNam(int ngay, int gio, int phut = 0)
        => new DateTime(2026, 10, ngay, gio, phut, 0, DateTimeKind.Utc).AddHours(-7);

    /// <summary>
    /// Context mới trên cùng một CSDL InMemory. Gọi nhiều lần được để đọc lại bằng context sạch —
    /// cùng tên CSDL nên dữ liệu dùng chung.
    /// </summary>
    private AppDbContext TaoDb() => new(
        new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(_tenCsdl)
            .Options);

    /// <summary>Chuyến của một tuyến trong một NGÀY GIỜ VIỆT NAM, xếp theo giờ khởi hành.</summary>
    private static async Task<List<Trip>> ChuyenTrongNgayAsync(AppDbContext db, Guid routeId, int ngay)
        => await db.Trips
            .Where(t => t.RouteId == routeId
                && t.DepartureTime >= GioVietNam(ngay, 0)
                && t.DepartureTime < GioVietNam(ngay + 1, 0))
            .OrderBy(t => t.DepartureTime)
            .ToListAsync();

    private static async Task<Route> SeedTuyenAsync(
        AppDbContext db,
        RouteStatus trangThai = RouteStatus.Active)
    {
        var tuyen = new Route
        {
            Code = $"T{Guid.NewGuid().ToString("N")[..4].ToUpperInvariant()}",
            Name = "Bến Thành — Chợ Lớn",
            Origin = "Bến Thành",
            Destination = "Chợ Lớn",
            Status = trangThai,
        };

        db.Routes.Add(tuyen);
        await db.SaveChangesAsync();

        return tuyen;
    }

    private static async Task<Bus> SeedXeAsync(
        AppDbContext db,
        BusStatus trangThai = BusStatus.Active)
    {
        var xe = new Bus
        {
            LicensePlate = $"29B-{Guid.NewGuid().ToString("N")[..5]}",
            BusType = "Xe buýt 45 chỗ",
            Capacity = 45,
            Status = trangThai,
        };

        db.Buses.Add(xe);
        await db.SaveChangesAsync();

        return xe;
    }

    private static async Task<Trip> SeedChuyenAsync(
        AppDbContext db,
        Route tuyen,
        Bus xe,
        DateTime departureUtc,
        TripStatus trangThai = TripStatus.Scheduled)
    {
        var chuyen = new Trip
        {
            RouteId = tuyen.Id,
            BusId = xe.Id,
            DepartureTime = departureUtc,
            // Giờ đến hơn giờ đi 45 phút — đủ để khung giờ của các chuyến trong file không chồng nhau.
            ArrivalTime = departureUtc.AddMinutes(45),
            Status = trangThai,
        };

        db.Trips.Add(chuyen);
        await db.SaveChangesAsync();

        return chuyen;
    }
}
