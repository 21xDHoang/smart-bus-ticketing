using Microsoft.EntityFrameworkCore;
using SmartBus.Api.Data;
using SmartBus.Api.Dtos.SeatLayouts;
using SmartBus.Api.Entities;
using SmartBus.Api.Services;

namespace SmartBus.Tests;

/// <summary>
/// Test nghiệp vụ sơ đồ ghế — <see cref="SeatLayoutService"/> (story 2 "Cấu hình sơ đồ ghế theo
/// loại xe", Phùng Duy Hoàng). Phần đáng ghim nhất không phải CRUD mà là luật SINH GHẾ:
///   - lưu sơ đồ là ghế có ngay cho xe cùng loại đang trống ghế, xe đã có ghế không bị đụng;
///   - mã ghế hai tầng có tiền tố T{tầng}- (không thì (BusId, SeatNumber) trùng);
///   - ghế đang bị giữ chỗ / đã bán vé thì CẤM đổi hình học (409), chưa ai đụng thì xoá rồi sinh lại;
///   - sức chứa mọi xe cùng loại phải khớp lưới, lệch thì 400 kèm phép tính.
///
/// Chạy trên InMemory nên KHÔNG thực thi khoá ngoại lẫn chỉ mục unique (đã ghi ở header các test
/// khác) — vì vậy các ca dưới khẳng định chính các pre-check của service, còn thứ tự lệnh
/// xoá-trước-chèn của một SaveChanges (dựa vào chỉ mục unique (BusId, SeatNumber)) chỉ kiểm được
/// thật trên PostgreSQL ở bước kiểm chứng end-to-end.
/// </summary>
public class SeatLayoutServiceTests
{
    private static AppDbContext MoDb() => new(
        new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"smartbus-seatlayout-{Guid.NewGuid()}")
            .Options);

    private static Bus TaoXe(string bienSo, string loaiXe, int sucChua) =>
        new() { LicensePlate = bienSo, BusType = loaiXe, Capacity = sucChua };

    // ---------------------------------------------------------------------------------------
    // Sinh ghế khi lưu sơ đồ
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Luu_so_do_sinh_du_ghe_cho_xe_cung_loai_dang_trong_ghe()
    {
        await using var db = MoDb();
        var xe = TaoXe("20B123789", "Xe buýt 45 chỗ", 45);
        db.Buses.Add(xe);
        await db.SaveChangesAsync();

        var ketQua = await new SeatLayoutService(db).CreateAsync(new CreateSeatLayoutRequest
        {
            BusType = "Xe buýt 45 chỗ",
            NumberOfFloors = 1,
            RowsPerFloor = 9,
            ColumnsPerRow = 5,
            VipSeatPositions = ["1-1-1"],
        });

        Assert.True(ketQua.Success);
        var soDo = ketQua.Data!;
        Assert.Equal(45, soDo.TotalSeats);
        Assert.Equal(1, soDo.BusesSeated);

        var ghe = await db.Seats.Where(s => s.BusId == xe.Id).ToListAsync();

        // Đủ lưới 9×5, tất cả trỏ về sơ đồ vừa tạo (ghế không thuộc sơ đồ nào là ghế không vẽ được).
        Assert.Equal(45, ghe.Count);
        Assert.All(ghe, s => Assert.Equal(soDo.Id, s.SeatLayoutId));
        Assert.Contains(ghe, s => s.SeatNumber == "I5"); // hàng 9 = chữ I

        // Toạ độ đếm từ 1 và VIP đúng một ô — khoá "1-1-1" của sơ đồ là ghế A1.
        var a1 = ghe.Single(s => s.SeatNumber == "A1");
        Assert.Equal(1, a1.Floor);
        Assert.Equal(1, a1.RowIndex);
        Assert.Equal(1, a1.ColumnIndex);
        Assert.Equal(SeatType.Vip, a1.SeatType);
        Assert.Single(ghe, s => s.SeatType == SeatType.Vip);
    }

    [Fact]
    public async Task Luu_so_do_khong_dung_toi_xe_da_co_ghe()
    {
        await using var db = MoDb();
        var xeDaCoGhe = TaoXe("20B123789", "Xe buýt 45 chỗ", 45);
        var xeTrongGhe = TaoXe("20B999999", "Xe buýt 45 chỗ", 45);
        db.Buses.AddRange(xeDaCoGhe, xeTrongGhe);

        // Ghế cũ của xe thứ nhất — để nguyên mã Z9, không sinh thêm gì cho xe này.
        db.Seats.Add(new Seat
        {
            BusId = xeDaCoGhe.Id,
            SeatLayoutId = Guid.NewGuid(),
            SeatNumber = "Z9",
            RowIndex = 1,
            ColumnIndex = 1,
        });
        await db.SaveChangesAsync();

        var ketQua = await new SeatLayoutService(db).CreateAsync(new CreateSeatLayoutRequest
        {
            BusType = "Xe buýt 45 chỗ",
            NumberOfFloors = 1,
            RowsPerFloor = 9,
            ColumnsPerRow = 5,
        });

        Assert.True(ketQua.Success);
        Assert.Equal(1, ketQua.Data!.BusesSeated); // chỉ xe trống ghế được sinh
        Assert.Equal(46, await db.Seats.CountAsync()); // 1 ghế cũ + 45 ghế mới

        var gheXeCu = await db.Seats.Where(s => s.BusId == xeDaCoGhe.Id).ToListAsync();
        Assert.Single(gheXeCu);
        Assert.Equal("Z9", gheXeCu[0].SeatNumber);
    }

    [Fact]
    public async Task Xe_hai_tang_dat_tien_to_T_theo_tang()
    {
        await using var db = MoDb();
        var xe = TaoXe("29B-22222", "Xe giường nằm 2 tầng", 8);
        db.Buses.Add(xe);
        await db.SaveChangesAsync();

        var ketQua = await new SeatLayoutService(db).CreateAsync(new CreateSeatLayoutRequest
        {
            BusType = "Xe giường nằm 2 tầng",
            NumberOfFloors = 2,
            RowsPerFloor = 2,
            ColumnsPerRow = 2,
        });

        Assert.True(ketQua.Success);

        var ghe = await db.Seats.Where(s => s.BusId == xe.Id).ToListAsync();

        Assert.Equal(8, ghe.Count);
        // Cùng toạ độ hàng 1 cột 1 tồn tại ở CẢ HAI tầng, nên mã phải khác nhau — không có
        // tiền tố là trùng (BusId, SeatNumber) và ghế tầng 2 không phân biệt được trên vé.
        Assert.Contains(ghe, s => s.SeatNumber == "T1-A1");
        Assert.Contains(ghe, s => s.SeatNumber == "T2-A1");
        Assert.DoesNotContain(ghe, s => s.SeatNumber == "A1");

        var tangHai = ghe.Single(s => s.SeatNumber == "T2-A1");
        Assert.Equal(2, tangHai.Floor);
        Assert.Equal(1, tangHai.RowIndex);
        Assert.Equal(1, tangHai.ColumnIndex);
    }

    // ---------------------------------------------------------------------------------------
    // Các cửa chặn của lượt lưu
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Trung_loai_xe_tra_400_gan_o_busType()
    {
        await using var db = MoDb();
        var service = new SeatLayoutService(db);
        var yeuCau = new CreateSeatLayoutRequest
        {
            BusType = "Xe buýt 45 chỗ",
            NumberOfFloors = 1,
            RowsPerFloor = 9,
            ColumnsPerRow = 5,
        };

        Assert.True((await service.CreateAsync(yeuCau)).Success);

        var ketQua = await service.CreateAsync(yeuCau);

        Assert.False(ketQua.Success);
        Assert.Equal(ServiceErrorKind.Invalid, ketQua.ErrorKind);
        Assert.True(ketQua.Errors!.ContainsKey("busType")); // gắn đúng ô nhập trên form
        Assert.Equal(1, await db.SeatLayouts.CountAsync());
    }

    [Fact]
    public async Task Lech_suc_chua_tra_400_kem_phep_tinh()
    {
        await using var db = MoDb();
        var xe = TaoXe("20B123888", "Xe buýt 24 chỗ", 24);
        db.Buses.Add(xe);
        await db.SaveChangesAsync();

        var ketQua = await new SeatLayoutService(db).CreateAsync(new CreateSeatLayoutRequest
        {
            BusType = "Xe buýt 24 chỗ",
            NumberOfFloors = 1,
            RowsPerFloor = 9,
            ColumnsPerRow = 5, // 45 ghế, xe chỉ 24 chỗ
        });

        Assert.False(ketQua.Success);
        Assert.Equal(ServiceErrorKind.Invalid, ketQua.ErrorKind);

        // Không phải lỗi một ô nhập nào — cả lưới sai so với xe, errors rỗng, câu lỗi nêu phép tính.
        Assert.Empty(ketQua.Errors!);
        Assert.Contains("20B123888", ketQua.Error);
        Assert.Contains("24", ketQua.Error);
        Assert.Contains("45", ketQua.Error);
        Assert.Empty(db.SeatLayouts);
    }

    [Fact]
    public async Task Vi_tri_vip_ngoai_luoi_hoac_lap_bi_tu_choi()
    {
        await using var db = MoDb();
        var service = new SeatLayoutService(db);

        var ngoaiLuoi = await service.CreateAsync(new CreateSeatLayoutRequest
        {
            BusType = "Xe test VIP",
            NumberOfFloors = 1,
            RowsPerFloor = 2,
            ColumnsPerRow = 2,
            VipSeatPositions = ["1-1-1", "1-9-9"], // hàng 9 cột 9 không có trong lưới 2×2
        });

        Assert.False(ngoaiLuoi.Success);
        Assert.Equal(ServiceErrorKind.Invalid, ngoaiLuoi.ErrorKind);
        Assert.True(ngoaiLuoi.Errors!.ContainsKey("vipSeatPositions"));

        var lap = await service.CreateAsync(new CreateSeatLayoutRequest
        {
            BusType = "Xe test VIP",
            NumberOfFloors = 1,
            RowsPerFloor = 2,
            ColumnsPerRow = 2,
            VipSeatPositions = ["1-1-1", "01-1-1"], // cùng một ô, viết khác kiểu
        });

        Assert.False(lap.Success);
        Assert.Equal(ServiceErrorKind.Invalid, lap.ErrorKind);
        Assert.True(lap.Errors!.ContainsKey("vipSeatPositions"));

        Assert.Empty(db.SeatLayouts);
    }

    [Fact]
    public async Task Luoi_vuot_200_ghe_tra_400_khong_gan_o()
    {
        await using var db = MoDb();

        // 2 × 26 × 10 = 520 — vượt trần sức chứa xe của dự án.
        var ketQua = await new SeatLayoutService(db).CreateAsync(new CreateSeatLayoutRequest
        {
            BusType = "Xe khổng lồ",
            NumberOfFloors = 2,
            RowsPerFloor = 26,
            ColumnsPerRow = 10,
        });

        Assert.False(ketQua.Success);
        Assert.Equal(ServiceErrorKind.Invalid, ketQua.ErrorKind);
        Assert.Empty(ketQua.Errors!);
        Assert.Empty(db.SeatLayouts);
    }

    // ---------------------------------------------------------------------------------------
    // Sửa sơ đồ — sinh bù, sinh lại, hoặc từ chối
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Sua_hinh_hoc_khi_ghe_dang_bi_giu_cho_tra_409()
    {
        await using var db = MoDb();
        var xe = TaoXe("20B123456", "Xe buýt 4 chỗ", 4);
        db.Buses.Add(xe);
        await db.SaveChangesAsync();

        var service = new SeatLayoutService(db);
        var tao = await service.CreateAsync(new CreateSeatLayoutRequest
        {
            BusType = "Xe buýt 4 chỗ",
            NumberOfFloors = 1,
            RowsPerFloor = 2,
            ColumnsPerRow = 2,
        });
        Assert.True(tao.Success);

        var gheBiGiu = await db.Seats.FirstAsync(s => s.BusId == xe.Id);
        db.SeatHolds.Add(new SeatHold
        {
            SeatId = gheBiGiu.Id,
            TripId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            SessionCode = "PHIEN-GIU-TEST",
            ExpiresAt = DateTime.UtcNow.AddMinutes(10),
        });
        await db.SaveChangesAsync();

        // 1×2×2 → 1×1×4: tổng vẫn 4 ghế (khớp sức chứa) nhưng lưới đổi hẳn.
        var ketQua = await service.UpdateAsync(tao.Data!.Id, new UpdateSeatLayoutRequest
        {
            NumberOfFloors = 1,
            RowsPerFloor = 1,
            ColumnsPerRow = 4,
        });

        Assert.False(ketQua.Success);
        Assert.Equal(ServiceErrorKind.Conflict, ketQua.ErrorKind);
        Assert.Equal(
            "Sơ đồ đang được dùng để giữ chỗ hoặc bán vé — không sửa hình học được nữa.",
            ketQua.Error);

        // Sơ đồ và dàn ghế giữ nguyên — không có mảnh nào bị xoá nửa vời.
        var gheConLai = await db.Seats.Where(s => s.BusId == xe.Id).ToListAsync();
        Assert.Equal(4, gheConLai.Count);
        Assert.Contains(gheConLai, s => s.Id == gheBiGiu.Id);
    }

    [Fact]
    public async Task Sua_hinh_hoc_khi_ve_da_ban_tra_409()
    {
        await using var db = MoDb();
        var xe = TaoXe("20B123457", "Xe buýt 6 chỗ", 6);
        db.Buses.Add(xe);
        await db.SaveChangesAsync();

        var service = new SeatLayoutService(db);
        var tao = await service.CreateAsync(new CreateSeatLayoutRequest
        {
            BusType = "Xe buýt 6 chỗ",
            NumberOfFloors = 1,
            RowsPerFloor = 2,
            ColumnsPerRow = 3,
        });
        Assert.True(tao.Success);

        var gheDaBan = await db.Seats.FirstAsync(s => s.BusId == xe.Id);
        db.Tickets.Add(new Ticket
        {
            SeatId = gheDaBan.Id,
            TripId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            Code = "SBT1-TEST-VE-DA-BAN",
            Price = 100000m,
        });
        await db.SaveChangesAsync();

        // 1×2×3 → 1×1×6, tổng vẫn 6.
        var ketQua = await service.UpdateAsync(tao.Data!.Id, new UpdateSeatLayoutRequest
        {
            NumberOfFloors = 1,
            RowsPerFloor = 1,
            ColumnsPerRow = 6,
        });

        Assert.False(ketQua.Success);
        Assert.Equal(ServiceErrorKind.Conflict, ketQua.ErrorKind);
    }

    [Fact]
    public async Task Sua_hinh_hoc_khi_chua_ai_dung_thi_xoa_ghe_cu_sinh_lai()
    {
        await using var db = MoDb();
        var xe = TaoXe("20B123456", "Xe buýt 4 chỗ", 4);
        db.Buses.Add(xe);
        await db.SaveChangesAsync();

        var service = new SeatLayoutService(db);
        var tao = await service.CreateAsync(new CreateSeatLayoutRequest
        {
            BusType = "Xe buýt 4 chỗ",
            NumberOfFloors = 1,
            RowsPerFloor = 2,
            ColumnsPerRow = 2,
        });
        Assert.True(tao.Success);
        var idGheCu = (await db.Seats.Where(s => s.BusId == xe.Id).Select(s => s.Id).ToListAsync()).ToHashSet();

        var ketQua = await service.UpdateAsync(tao.Data!.Id, new UpdateSeatLayoutRequest
        {
            NumberOfFloors = 1,
            RowsPerFloor = 1,
            ColumnsPerRow = 4,
            VipSeatPositions = ["1-1-1"],
        });

        Assert.True(ketQua.Success);
        var soDoSau = ketQua.Data!;
        Assert.Equal(1, soDoSau.BusesSeated);
        Assert.NotNull(soDoSau.UpdatedAt);

        var gheMoi = await db.Seats.Where(s => s.BusId == xe.Id).ToListAsync();

        // Dàn ghế là dàn MỚI hoàn toàn theo lưới mới: 4 ghế hàng A, không dòng nào còn là ghế cũ.
        Assert.Equal(4, gheMoi.Count);
        Assert.All(gheMoi, s => Assert.DoesNotContain(s.Id, idGheCu));
        Assert.All(gheMoi, s => Assert.Equal(soDoSau.Id, s.SeatLayoutId));
        Assert.Contains(gheMoi, s => s.SeatNumber == "A4");
        Assert.Equal(SeatType.Vip, gheMoi.Single(s => s.SeatNumber == "A1").SeatType);
    }

    [Fact]
    public async Task Sua_khong_doi_gi_thi_chi_sinh_bu_cho_xe_moi()
    {
        await using var db = MoDb();
        var xeCu = TaoXe("20B111111", "Xe buýt 4 chỗ", 4);
        db.Buses.Add(xeCu);
        await db.SaveChangesAsync();

        var service = new SeatLayoutService(db);
        var tao = await service.CreateAsync(new CreateSeatLayoutRequest
        {
            BusType = "Xe buýt 4 chỗ",
            NumberOfFloors = 1,
            RowsPerFloor = 2,
            ColumnsPerRow = 2,
        });
        Assert.True(tao.Success);

        // Xe thứ hai cùng loại được thêm SAU khi sơ đồ đã có — nó đang trống ghế.
        var xeMoi = TaoXe("20B222222", "Xe buýt 4 chỗ", 4);
        db.Buses.Add(xeMoi);
        await db.SaveChangesAsync();

        var idGheXeCu = (await db.Seats.Where(s => s.BusId == xeCu.Id).Select(s => s.Id).ToListAsync()).ToHashSet();

        var ketQua = await service.UpdateAsync(tao.Data!.Id, new UpdateSeatLayoutRequest
        {
            NumberOfFloors = 1,
            RowsPerFloor = 2,
            ColumnsPerRow = 2, // y hệt sơ đồ cũ
        });

        Assert.True(ketQua.Success);
        Assert.Equal(1, ketQua.Data!.BusesSeated); // chỉ xe mới được sinh bù
        Assert.Equal(4, await db.Seats.CountAsync(s => s.BusId == xeMoi.Id));

        // Ghế xe cũ KHÔNG bị xoá-sinh lại (id giữ nguyên) — ghế cũ có thể đã in trên vé.
        var idGheXeCuSau = (await db.Seats.Where(s => s.BusId == xeCu.Id).Select(s => s.Id).ToListAsync()).ToHashSet();
        Assert.True(idGheXeCu.SetEquals(idGheXeCuSau));
    }

    [Fact]
    public async Task Sua_va_xoa_so_do_khong_ton_tai_tra_404()
    {
        await using var db = MoDb();
        var service = new SeatLayoutService(db);
        var idLa = Guid.NewGuid();

        var sua = await service.UpdateAsync(idLa, new UpdateSeatLayoutRequest
        {
            NumberOfFloors = 1,
            RowsPerFloor = 2,
            ColumnsPerRow = 2,
        });
        Assert.Equal(ServiceErrorKind.NotFound, sua.ErrorKind);

        var xoa = await service.DeleteAsync(idLa);
        Assert.Equal(ServiceErrorKind.NotFound, xoa.ErrorKind);
    }

    // ---------------------------------------------------------------------------------------
    // Xoá sơ đồ
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Xoa_so_do_con_ghe_tra_409_kem_so_luong()
    {
        await using var db = MoDb();
        var xe = TaoXe("20B123456", "Xe buýt 4 chỗ", 4);
        db.Buses.Add(xe);
        await db.SaveChangesAsync();

        var service = new SeatLayoutService(db);
        var tao = await service.CreateAsync(new CreateSeatLayoutRequest
        {
            BusType = "Xe buýt 4 chỗ",
            NumberOfFloors = 1,
            RowsPerFloor = 2,
            ColumnsPerRow = 2,
        });
        Assert.True(tao.Success);

        var ketQua = await service.DeleteAsync(tao.Data!.Id);

        Assert.False(ketQua.Success);
        Assert.Equal(ServiceErrorKind.Conflict, ketQua.ErrorKind);
        Assert.Contains("4", ketQua.Error);
        Assert.Single(db.SeatLayouts);
        Assert.Equal(4, await db.Seats.CountAsync());
    }

    [Fact]
    public async Task Xoa_so_do_khong_con_ghe_thi_xoa_duoc()
    {
        await using var db = MoDb();
        var service = new SeatLayoutService(db);

        // Loại xe chưa có xe nào trong đội — không sinh ghế, xoá được ngay.
        var tao = await service.CreateAsync(new CreateSeatLayoutRequest
        {
            BusType = "Xe chưa mua",
            NumberOfFloors = 1,
            RowsPerFloor = 2,
            ColumnsPerRow = 2,
        });
        var soDo = tao.Data!;
        Assert.Equal(0, soDo.BusesSeated);

        var ketQua = await service.DeleteAsync(soDo.Id);

        Assert.True(ketQua.Success);
        Assert.True(ketQua.Data);
        Assert.Empty(db.SeatLayouts);
    }

    // ---------------------------------------------------------------------------------------
    // Danh sách
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Danh_sach_loc_theo_loai_xe_khong_phan_biet_hoa_thuong()
    {
        await using var db = MoDb();
        var service = new SeatLayoutService(db);

        await service.CreateAsync(new CreateSeatLayoutRequest
        {
            BusType = "Xe buýt 45 chỗ",
            NumberOfFloors = 1,
            RowsPerFloor = 9,
            ColumnsPerRow = 5,
        });
        await service.CreateAsync(new CreateSeatLayoutRequest
        {
            BusType = "Xe giường nằm 2 tầng",
            NumberOfFloors = 2,
            RowsPerFloor = 2,
            ColumnsPerRow = 2,
        });

        var tatCa = await service.ListAsync(new ListSeatLayoutsRequest());
        var tatCaList = tatCa.Data!;
        Assert.Equal(2, tatCaList.Count);
        Assert.Equal("Xe buýt 45 chỗ", tatCaList[0].BusType); // sắp theo busType

        var loc = await service.ListAsync(new ListSeatLayoutsRequest { Search = "GIƯỜNG" });
        var locList = loc.Data!;
        Assert.Single(locList);
        Assert.Equal("Xe giường nằm 2 tầng", locList[0].BusType);
    }
}
