using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SmartBus.Api.Data;
using SmartBus.Api.Dtos.Trips;
using SmartBus.Api.Entities;
using SmartBus.Api.Services;
using RouteEntity = SmartBus.Api.Entities.Route;

namespace SmartBus.Tests;

/// <summary>
/// Test <see cref="TripStopTimeService"/> — giờ đến TỪNG TRẠM của một chuyến.
///
/// <para>
/// Bốn nhóm việc phải ghim:
/// <list type="number">
///   <item>giờ trung gian SUY RA từ km, trạm đầu / bến cuối bám đúng hai mốc neo;</item>
///   <item>thiếu km thì KHÔNG bịa giờ — trả null chứ không lấy giờ chuyến chia đều;</item>
///   <item>ghi đè thay thế toàn phần: lưu, sửa, xoá mốc, và vẫn trả giờ dự kiến bên cạnh;</item>
///   <item>các cổng chặn: trạm ngoài tuyến, trạm lặp, mốc neo, sai thứ tự, chuyến đã khoá.</item>
/// </list>
/// </para>
///
/// <para>
/// Chạy trên InMemory: index duy nhất (TripId, StopId) do CSDL thật gác, không phải service —
/// bài ở đây kiểm luật nghiệp vụ, không kiểm ràng buộc của PostgreSQL.
/// </para>
/// </summary>
public class TripStopTimeServiceTests
{
    private static readonly DateTime GioKhoiHanh = new(2026, 10, 17, 5, 0, 0, DateTimeKind.Utc);

    // =======================================================================================
    // Đọc: suy ra giờ từ km
    // =======================================================================================

    /// <summary>
    /// Tuyến 4 trạm, chặng 10 + 10 + 10 km, chuyến chạy 90 phút (05:00 → 06:30).
    /// Giờ trung gian = 05:00 + round(cumulative/30 × 90): trạm 2 = 05:30, trạm 3 = 06:00.
    /// Trạm đầu = giờ đi, bến cuối = giờ đến đã lưu — không phải giá trị suy ra.
    /// </summary>
    [Fact]
    public async Task Doc_thi_suy_ra_gio_tung_tram_tu_km_va_giu_hai_moc_neo()
    {
        await using var db = TaoDb();
        var duLieu = await SeedTuyenBonTramAsync(db);
        var service = TaoService(db);

        var result = await service.GetAsync(duLieu.Route.Id, duLieu.Trip.Id);

        Assert.True(result.Success);
        var response = result.Data!;
        Assert.True(response.HasDistanceData);
        Assert.False(response.ArrivalTimeIsEstimated);
        Assert.Equal(30m, response.TotalDistanceKm);
        Assert.Equal(4, response.Stops.Count);

        Assert.Equal(GioKhoiHanh, response.Stops[0].ArrivalTime);
        Assert.True(response.Stops[0].IsFirst);
        Assert.Equal(0m, response.Stops[0].CumulativeDistanceKm);

        Assert.Equal(GioKhoiHanh.AddMinutes(30), response.Stops[1].ArrivalTime);
        Assert.Equal(10m, response.Stops[1].CumulativeDistanceKm);
        Assert.False(response.Stops[1].IsManual);

        Assert.Equal(GioKhoiHanh.AddMinutes(60), response.Stops[2].ArrivalTime);
        Assert.Equal(20m, response.Stops[2].CumulativeDistanceKm);

        Assert.Equal(GioKhoiHanh.AddMinutes(90), response.Stops[3].ArrivalTime);
        Assert.True(response.Stops[3].IsLast);
        Assert.False(response.Stops[3].IsManual);

        // Không có mốc ghi đè nào thì giờ dự kiến = giờ đang dùng.
        Assert.All(
            response.Stops,
            stop => Assert.Equal(stop.DerivedArrivalTime, stop.ArrivalTime));
    }

    /// <summary>
    /// <c>Trips.ArrivalTime</c> trống: bến cuối ước lượng 25 km/h / làm tròn lên bội 5 / sàn 15 —
    /// ở đây 30 km → 72 → 75 phút. Cờ <c>ArrivalTimeIsEstimated</c> phải bật để UI nói rõ là ước lượng.
    /// </summary>
    [Fact]
    public async Task Chuyen_thieu_gio_den_thi_uoc_luong_ben_cuoi_va_bao_co_estimated()
    {
        await using var db = TaoDb();
        var duLieu = await SeedTuyenBonTramAsync(db, coGioDen: false);
        var service = TaoService(db);

        var result = await service.GetAsync(duLieu.Route.Id, duLieu.Trip.Id);

        Assert.True(result.Success);
        var response = result.Data!;
        Assert.True(response.ArrivalTimeIsEstimated);
        Assert.Equal(GioKhoiHanh.AddMinutes(75), response.ArrivalTime);
        Assert.Equal(GioKhoiHanh.AddMinutes(75), response.Stops[3].ArrivalTime);

        // Trung gian suy ra theo 75 phút thực tế của chuyến, không phải theo 30 km/h.
        Assert.Equal(GioKhoiHanh.AddMinutes(25), response.Stops[1].ArrivalTime);
        Assert.Equal(GioKhoiHanh.AddMinutes(50), response.Stops[2].ArrivalTime);
    }

    /// <summary>
    /// Tuyến chưa nhập km chặng: KHÔNG bịa giờ trung gian. Hai mốc neo vẫn có, trung gian null,
    /// cờ <c>HasDistanceData</c> tắt để UI hiện cảnh báo thay vì hiện giờ sai.
    /// </summary>
    [Fact]
    public async Task Thieu_du_lieu_km_thi_tram_trung_gian_de_null_chu_khong_bia_gio()
    {
        await using var db = TaoDb();
        var duLieu = await SeedTuyenBonTramAsync(db, distanceKm: 0m);
        var service = TaoService(db);

        var result = await service.GetAsync(duLieu.Route.Id, duLieu.Trip.Id);

        Assert.True(result.Success);
        var response = result.Data!;
        Assert.False(response.HasDistanceData);
        Assert.Equal(0m, response.TotalDistanceKm);

        Assert.Equal(GioKhoiHanh, response.Stops[0].ArrivalTime);
        Assert.Null(response.Stops[1].ArrivalTime);
        Assert.Null(response.Stops[1].DerivedArrivalTime);
        Assert.Null(response.Stops[2].ArrivalTime);
        Assert.Equal(GioKhoiHanh.AddMinutes(90), response.Stops[3].ArrivalTime);
    }

    /// <summary>Chuyến không thuộc tuyến trong đường dẫn → 404, không lộ dữ liệu của tuyến khác.</summary>
    [Fact]
    public async Task Chuyen_khong_thuoc_tuyen_tra_404()
    {
        await using var db = TaoDb();
        var duLieu = await SeedTuyenBonTramAsync(db);
        var service = TaoService(db);

        var result = await service.GetAsync(Guid.NewGuid(), duLieu.Trip.Id);

        Assert.False(result.Success);
        Assert.Equal(ServiceErrorKind.NotFound, result.ErrorKind);
    }

    // =======================================================================================
    // Ghi đè: thay thế toàn phần
    // =======================================================================================

    [Fact]
    public async Task Ghi_de_gio_mot_tram_thi_luu_va_van_tra_gio_du_kien()
    {
        await using var db = TaoDb();
        var duLieu = await SeedTuyenBonTramAsync(db);
        var service = TaoService(db);
        var gioSua = GioKhoiHanh.AddMinutes(40);

        var result = await service.UpdateAsync(
            duLieu.Route.Id,
            duLieu.Trip.Id,
            Body((duLieu.Stop2.Id, gioSua)));

        Assert.True(result.Success);
        var tram2 = result.Data!.Stops[1];
        Assert.Equal(gioSua, tram2.ArrivalTime);
        Assert.Equal(GioKhoiHanh.AddMinutes(30), tram2.DerivedArrivalTime);
        Assert.True(tram2.IsManual);

        // Đọc lại từ CSDL — mốc phải thật sự nằm ở bảng, không chỉ nằm trong DTO trả về.
        var docLai = await service.GetAsync(duLieu.Route.Id, duLieu.Trip.Id);
        Assert.True(docLai.Success);
        Assert.Equal(gioSua, docLai.Data!.Stops[1].ArrivalTime);
        Assert.True(docLai.Data.Stops[1].IsManual);
        Assert.Single(await db.TripStopTimeOverrides.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Gui_mang_rong_thi_xoa_het_moc_ghi_de_va_quay_ve_gio_du_kien()
    {
        await using var db = TaoDb();
        var duLieu = await SeedTuyenBonTramAsync(db);
        var service = TaoService(db);

        var daLuu = await service.UpdateAsync(
            duLieu.Route.Id,
            duLieu.Trip.Id,
            Body((duLieu.Stop2.Id, GioKhoiHanh.AddMinutes(40))));
        Assert.True(daLuu.Success);

        var xoaHet = await service.UpdateAsync(
            duLieu.Route.Id,
            duLieu.Trip.Id,
            new UpdateTripStopTimesRequest { Items = [] });

        Assert.True(xoaHet.Success);
        Assert.False(xoaHet.Data!.Stops[1].IsManual);
        Assert.Equal(GioKhoiHanh.AddMinutes(30), xoaHet.Data.Stops[1].ArrivalTime);
        Assert.Empty(await db.TripStopTimeOverrides.AsNoTracking().ToListAsync());
    }

    /// <summary>
    /// Thay thế toàn phần: lần lưu sau chỉ gửi trạm 3 thì mốc cũ của trạm 2 phải biến mất —
    /// nếu chỉ "thêm/sửa" mà quên xoá thì màn hình sẽ kẹt mốc cũ mãi.
    /// </summary>
    [Fact]
    public async Task Lan_luu_sau_thay_the_toan_phan_moc_cu_khong_con()
    {
        await using var db = TaoDb();
        var duLieu = await SeedTuyenBonTramAsync(db);
        var service = TaoService(db);

        await service.UpdateAsync(
            duLieu.Route.Id,
            duLieu.Trip.Id,
            Body((duLieu.Stop2.Id, GioKhoiHanh.AddMinutes(40))));

        var lanHai = await service.UpdateAsync(
            duLieu.Route.Id,
            duLieu.Trip.Id,
            Body((duLieu.Stop3.Id, GioKhoiHanh.AddMinutes(70))));

        Assert.True(lanHai.Success);
        Assert.False(lanHai.Data!.Stops[1].IsManual);
        Assert.True(lanHai.Data.Stops[2].IsManual);

        var conLai = await db.TripStopTimeOverrides.AsNoTracking().ToListAsync();
        Assert.Single(conLai);
        Assert.Equal(duLieu.Stop3.Id, conLai[0].StopId);
    }

    [Fact]
    public async Task Sua_lai_cung_mot_tram_giu_nguyen_dong_va_cap_nhat_UpdatedAt()
    {
        await using var db = TaoDb();
        var duLieu = await SeedTuyenBonTramAsync(db);
        var service = TaoService(db);

        await service.UpdateAsync(
            duLieu.Route.Id,
            duLieu.Trip.Id,
            Body((duLieu.Stop2.Id, GioKhoiHanh.AddMinutes(40))));

        var banDau = await db.TripStopTimeOverrides.AsNoTracking().SingleAsync();

        var lanHai = await service.UpdateAsync(
            duLieu.Route.Id,
            duLieu.Trip.Id,
            Body((duLieu.Stop2.Id, GioKhoiHanh.AddMinutes(45))));

        Assert.True(lanHai.Success);
        var sauKhiSua = await db.TripStopTimeOverrides.AsNoTracking().SingleAsync();
        Assert.Equal(banDau.Id, sauKhiSua.Id);
        Assert.Equal(banDau.CreatedAt, sauKhiSua.CreatedAt);
        Assert.NotNull(sauKhiSua.UpdatedAt);
        Assert.Equal(GioKhoiHanh.AddMinutes(45), sauKhiSua.ArrivalTime);
    }

    /// <summary>Thiếu km vẫn sửa được tay — người điều hành biết giờ thật dù tuyến chưa nhập km.</summary>
    [Fact]
    public async Task Thieu_km_van_ghi_de_duoc_gio_tay()
    {
        await using var db = TaoDb();
        var duLieu = await SeedTuyenBonTramAsync(db, distanceKm: 0m);
        var service = TaoService(db);

        var result = await service.UpdateAsync(
            duLieu.Route.Id,
            duLieu.Trip.Id,
            Body((duLieu.Stop2.Id, GioKhoiHanh.AddMinutes(25))));

        Assert.True(result.Success);
        Assert.Equal(GioKhoiHanh.AddMinutes(25), result.Data!.Stops[1].ArrivalTime);
        Assert.True(result.Data.Stops[1].IsManual);
        Assert.Null(result.Data.Stops[1].DerivedArrivalTime);
    }

    // =======================================================================================
    // Cổng chặn
    // =======================================================================================

    [Fact]
    public async Task Khong_cho_ghi_de_tram_dau_hoac_ben_cuoi()
    {
        await using var db = TaoDb();
        var duLieu = await SeedTuyenBonTramAsync(db);
        var service = TaoService(db);

        var tramDau = await service.UpdateAsync(
            duLieu.Route.Id,
            duLieu.Trip.Id,
            Body((duLieu.Stop1.Id, GioKhoiHanh)));

        Assert.False(tramDau.Success);
        Assert.Equal(ServiceErrorKind.Invalid, tramDau.ErrorKind);
        Assert.True(tramDau.Errors!.ContainsKey("items"));

        var benCuoi = await service.UpdateAsync(
            duLieu.Route.Id,
            duLieu.Trip.Id,
            Body((duLieu.Stop4.Id, GioKhoiHanh.AddMinutes(100))));

        Assert.False(benCuoi.Success);
        Assert.Equal(ServiceErrorKind.Invalid, benCuoi.ErrorKind);
    }

    [Fact]
    public async Task Chan_tram_ngoai_tuyen_va_tram_lap()
    {
        await using var db = TaoDb();
        var duLieu = await SeedTuyenBonTramAsync(db);
        var service = TaoService(db);

        var ngoaiTuyen = await service.UpdateAsync(
            duLieu.Route.Id,
            duLieu.Trip.Id,
            Body((Guid.NewGuid(), GioKhoiHanh.AddMinutes(40))));

        Assert.False(ngoaiTuyen.Success);
        Assert.Equal(ServiceErrorKind.Invalid, ngoaiTuyen.ErrorKind);

        var lap = await service.UpdateAsync(
            duLieu.Route.Id,
            duLieu.Trip.Id,
            new UpdateTripStopTimesRequest
            {
                Items =
                [
                    Item(duLieu.Stop2.Id, GioKhoiHanh.AddMinutes(40)),
                    Item(duLieu.Stop2.Id, GioKhoiHanh.AddMinutes(45)),
                ],
            });

        Assert.False(lap.Success);
        Assert.Equal(ServiceErrorKind.Invalid, lap.ErrorKind);
        Assert.Empty(await db.TripStopTimeOverrides.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Chan_gio_dao_thu_tu_voi_moc_lien_ke()
    {
        await using var db = TaoDb();
        var duLieu = await SeedTuyenBonTramAsync(db);
        var service = TaoService(db);

        // Trước giờ khởi hành.
        var truocGioDi = await service.UpdateAsync(
            duLieu.Route.Id,
            duLieu.Trip.Id,
            Body((duLieu.Stop2.Id, GioKhoiHanh.AddMinutes(-5))));
        Assert.False(truocGioDi.Success);

        // Sau giờ đến bến cuối.
        var sauBenCuoi = await service.UpdateAsync(
            duLieu.Route.Id,
            duLieu.Trip.Id,
            Body((duLieu.Stop3.Id, GioKhoiHanh.AddMinutes(95))));
        Assert.False(sauBenCuoi.Success);

        // Trạm 3 đặt trước trạm 2; trạm 2 là mốc ghi đè nên phép so là bắt buộc.
        await service.UpdateAsync(
            duLieu.Route.Id,
            duLieu.Trip.Id,
            Body((duLieu.Stop3.Id, GioKhoiHanh.AddMinutes(60))));

        var nguocThuTu = await service.UpdateAsync(
            duLieu.Route.Id,
            duLieu.Trip.Id,
            new UpdateTripStopTimesRequest
            {
                Items =
                [
                    Item(duLieu.Stop2.Id, GioKhoiHanh.AddMinutes(60)),
                    Item(duLieu.Stop3.Id, GioKhoiHanh.AddMinutes(60)),
                ],
            });

        Assert.False(nguocThuTu.Success);
        Assert.Equal(ServiceErrorKind.Invalid, nguocThuTu.ErrorKind);
    }

    [Theory]
    [InlineData(TripStatus.Cancelled)]
    [InlineData(TripStatus.Completed)]
    public async Task Chuyen_da_khoa_thi_khong_sua_duoc_nhung_van_doc_duoc(TripStatus status)
    {
        await using var db = TaoDb();
        var duLieu = await SeedTuyenBonTramAsync(db);
        duLieu.Trip.Status = status;
        await db.SaveChangesAsync();
        var service = TaoService(db);

        var sua = await service.UpdateAsync(
            duLieu.Route.Id,
            duLieu.Trip.Id,
            Body((duLieu.Stop2.Id, GioKhoiHanh.AddMinutes(40))));

        Assert.False(sua.Success);
        Assert.Equal(ServiceErrorKind.Conflict, sua.ErrorKind);

        var doc = await service.GetAsync(duLieu.Route.Id, duLieu.Trip.Id);
        Assert.True(doc.Success);
    }

    [Fact]
    public async Task Body_thieu_items_tra_400_chu_khong_hieu_la_xoa_het()
    {
        await using var db = TaoDb();
        var duLieu = await SeedTuyenBonTramAsync(db);
        var service = TaoService(db);

        var result = await service.UpdateAsync(
            duLieu.Route.Id,
            duLieu.Trip.Id,
            new UpdateTripStopTimesRequest { Items = null });

        Assert.False(result.Success);
        Assert.Equal(ServiceErrorKind.Invalid, result.ErrorKind);
    }

    // ── Hạ tầng dùng chung của lớp test ────────────────────────────────────────────────────────

    private static AppDbContext TaoDb()
        => new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"gio-den-tung-tram-{Guid.NewGuid()}")
            .Options);

    private static TripStopTimeService TaoService(AppDbContext db)
        => new(db, NullLogger<TripStopTimeService>.Instance);

    private static UpdateTripStopTimesRequest Body(params (Guid StopId, DateTime Time)[] items)
        => new()
        {
            Items = items.Select(item => Item(item.StopId, item.Time)).ToList(),
        };

    private static UpdateTripStopTimeItem Item(Guid stopId, DateTime time)
        => new()
        {
            StopId = stopId,
            ArrivalTime = new DateTimeOffset(
                DateTime.SpecifyKind(time, DateTimeKind.Utc),
                TimeSpan.Zero),
        };

    /// <summary>
    /// Tuyến 4 trạm, mỗi chặng <paramref name="distanceKm"/> km; chuyến 05:00 → 06:30.
    /// Bốn trạm đủ để có hai trạm trung gian — chỗ luật "trạm đầu / bến cuối không ghi đè" cần.
    /// </summary>
    private static async Task<DuLieuTest> SeedTuyenBonTramAsync(
        AppDbContext db,
        decimal distanceKm = 10m,
        bool coGioDen = true)
    {
        var route = new RouteEntity
        {
            Id = Guid.NewGuid(),
            Code = "01TL-PY",
            Name = "Tân Long — Phổ Yên",
            Origin = "Tân Long",
            Destination = "Phổ Yên",
            DistanceKm = distanceKm * 3,
        };

        var stop1 = TaoTram("Phường Tân Long", "TP. Thái Nguyên");
        var stop2 = TaoTram("Dương Tự Minh", "TP. Thái Nguyên");
        var stop3 = TaoTram("Chùa Hang", "TP. Thái Nguyên");
        var stop4 = TaoTram("Bến xe Phổ Yên", "TP. Phổ Yên");

        db.Routes.Add(route);
        db.Stops.AddRange(stop1, stop2, stop3, stop4);
        db.RouteStops.AddRange(
            new RouteStop { RouteId = route.Id, StopId = stop1.Id, StopOrder = 1, DistanceKm = 0m },
            new RouteStop { RouteId = route.Id, StopId = stop2.Id, StopOrder = 2, DistanceKm = distanceKm },
            new RouteStop { RouteId = route.Id, StopId = stop3.Id, StopOrder = 3, DistanceKm = distanceKm },
            new RouteStop { RouteId = route.Id, StopId = stop4.Id, StopOrder = 4, DistanceKm = distanceKm });

        var trip = new Trip
        {
            Id = Guid.NewGuid(),
            RouteId = route.Id,
            Route = route,
            BusId = Guid.NewGuid(),
            DepartureTime = GioKhoiHanh,
            ArrivalTime = coGioDen ? GioKhoiHanh.AddMinutes(90) : null,
            Status = TripStatus.Scheduled,
        };
        db.Trips.Add(trip);

        await db.SaveChangesAsync();

        return new DuLieuTest(route, trip, stop1, stop2, stop3, stop4);
    }

    private static Stop TaoTram(string name, string address) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        Address = address,
        Latitude = 21.5,
        Longitude = 105.8,
    };

    private sealed record DuLieuTest(
        RouteEntity Route,
        Trip Trip,
        Stop Stop1,
        Stop Stop2,
        Stop Stop3,
        Stop Stop4);
}
