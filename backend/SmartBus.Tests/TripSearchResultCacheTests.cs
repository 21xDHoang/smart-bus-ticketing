using SmartBus.Api.Dtos.Trips;
using SmartBus.Api.Services;

namespace SmartBus.Tests;

/// <summary>
/// Test lớp đệm kết quả tìm chuyến — <see cref="TripSearchResultCache"/>
/// (task *"Cache kết quả tìm kiếm tuyến phổ biến để giảm tải DB"* — Nguyễn Duy Kiên).
///
/// Không đi qua HTTP: dựng thẳng lớp đệm nên **đặt được TTL** — <see cref="TestAppFactory"/> là
/// <c>internal sealed</c> và không có chỗ cấu hình tham số cho service, nên muốn thử nhánh hết hạn
/// thì phải dựng trực tiếp. Phần "đệm có thật sự đứng trước endpoint không" nằm ở
/// <see cref="TripSearchCacheApiTests"/> (đi qua HTTP).
///
/// Cố ý KHÔNG có test chờ đồng hồ (kiểu <c>Task.Delay</c> quá TTL): nhánh hết hạn thử bằng
/// <see cref="TimeSpan.Zero"/> — vừa tất định vừa không chập chờn khi máy chậm.
/// </summary>
public class TripSearchResultCacheTests
{
    // ---------------------------------------------------------------------------------------
    // Ngưỡng "tuyến phổ biến" — hỏi lặp lại mới vào đệm
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void Luot_hoi_dau_tien_khong_luu_ket_qua()
    {
        var cache = new TripSearchResultCache();
        var ketQua = KetQua(1);

        // Lượt 1: chỉ ghi nhận lượt hỏi, kết quả vừa đọc từ CSDL KHÔNG được lưu.
        Assert.Null(cache.Lookup("khoa-1"));
        cache.Store("khoa-1", ketQua);

        // Hai lượt sau vẫn phải hỏi CSDL ⇒ Store ở lượt 1 đã không lưu gì (nếu lưu thì lượt 2 đã
        // trúng đệm rồi).
        Assert.Null(cache.Lookup("khoa-1"));
        Assert.Null(cache.Lookup("khoa-1"));

        Assert.Equal(0, cache.Hits);
        Assert.Equal(3, cache.Misses);
    }

    [Fact]
    public void Luot_hoi_thu_hai_moi_bat_dau_luu_ket_qua()
    {
        var cache = new TripSearchResultCache();
        var ketQua = KetQua(2);
        const string key = "khoa-1";

        Assert.Null(cache.Lookup(key));
        cache.Store(key, ketQua);

        // Lượt 2: đã đủ ngưỡng phổ biến — lượt này còn phải hỏi CSDL, nhưng kết quả được lưu.
        Assert.Null(cache.Lookup(key));
        cache.Store(key, ketQua);

        // Lượt 3: trúng đệm, không chạm CSDL.
        var trung = cache.Lookup(key);

        Assert.NotNull(trung);
        Assert.Equal(ketQua.Select(t => t.Id), trung!.Select(t => t.Id));
        Assert.Equal(1, cache.Hits);
        Assert.Equal(2, cache.Misses);
    }

    [Fact]
    public void Khoa_khac_nhau_khong_dung_chung_ket_qua()
    {
        var cache = new TripSearchResultCache();
        var cuaA = KetQua(2);

        // Làm nóng khoá A tới lúc trúng đệm.
        cache.Lookup("A");
        cache.Store("A", cuaA);
        cache.Lookup("A");
        cache.Store("A", cuaA);
        Assert.NotNull(cache.Lookup("A"));

        // Khoá B là truy vấn khác (tuyến khác / khoảng thời gian khác) → đi lượt hỏi riêng của nó.
        Assert.Null(cache.Lookup("B"));
    }

    // ---------------------------------------------------------------------------------------
    // Hết hạn và trần số khoá
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void Het_han_thi_quen_ca_ket_qua_lan_so_luot_da_dem()
    {
        // TTL bằng 0 = hết hạn ngay: mọi lượt hỏi đều rơi vào nhánh hết hạn.
        var cache = new TripSearchResultCache(TimeSpan.Zero);
        var ketQua = KetQua(1);
        const string key = "khoa-het-han";

        for (var luot = 0; luot < 5; luot++)
        {
            Assert.Null(cache.Lookup(key));
            cache.Store(key, ketQua);
        }

        // Không lượt nào trúng, và số lượt đã đếm cũng bị quên theo — nếu giữ số lượt thì lượt 2 đã
        // đủ ngưỡng phổ biến và kết quả đã được lưu.
        Assert.Equal(0, cache.Hits);
        Assert.Equal(5, cache.Misses);
        Assert.Equal(1, cache.Count);
    }

    [Fact]
    public void Cham_tran_thi_khong_nhan_them_khoa_moi()
    {
        var cache = new TripSearchResultCache();
        var ketQua = KetQua(1);

        // Lấp đầy đệm: mỗi khoá phải hỏi 2 lượt mới được lưu (ngưỡng phổ biến).
        for (var i = 0; i < ITripSearchResultCache.MaxEntries; i++)
        {
            var key = $"khoa-{i}";
            cache.Lookup(key);
            cache.Store(key, ketQua);
            cache.Lookup(key);
            cache.Store(key, ketQua);
        }

        Assert.Equal(ITripSearchResultCache.MaxEntries, cache.Count);

        // Khoá thứ 201: đệm đầy và chưa khoá nào hết hạn → không nhận thêm, lượt này đi thẳng CSDL.
        Assert.Null(cache.Lookup("khoa-thu-201"));
        cache.Store("khoa-thu-201", ketQua);

        Assert.Equal(ITripSearchResultCache.MaxEntries, cache.Count);
    }

    [Fact]
    public void Nhieu_luong_cung_luc_khong_nem_loi_va_khong_vuot_tran()
    {
        var cache = new TripSearchResultCache();
        var ketQua = KetQua(1);

        // Đệm là singleton dùng chung mọi request — chạy song song chỉ để chắc rằng không có chỗ
        // đọc/ghi nào ném lỗi và trần số khoá vẫn là trần thật.
        var loi = Record.Exception(() => Parallel.For(0, 1000, i =>
        {
            var key = $"khoa-{i % 300}";
            cache.Lookup(key);
            cache.Store(key, ketQua);
            cache.Lookup(key);
        }));

        Assert.Null(loi);
        Assert.True(
            cache.Count <= ITripSearchResultCache.MaxEntries,
            $"Đệm giữ {cache.Count} khoá, vượt trần {ITripSearchResultCache.MaxEntries}.");
    }

    // ---------------------------------------------------------------------------------------
    // Helper — mỗi lớp test tự chép, không có lớp base chung (đúng lệ đang có của dự án)
    // ---------------------------------------------------------------------------------------

    /// <summary>Một danh sách kết quả tìm chuyến giả — nội dung không quan trọng, chỉ cần Id khác nhau.</summary>
    private static List<TripSearchResult> KetQua(int soChuyen)
    {
        var danhSach = new List<TripSearchResult>();

        for (var i = 0; i < soChuyen; i++)
        {
            danhSach.Add(new TripSearchResult
            {
                Id = Guid.NewGuid(),
                RouteId = Guid.NewGuid(),
                RouteCode = "01",
                RouteName = "Bến xe Mỹ Đình — Bến xe Gia Lâm",
                DepartureTime = new DateTime(2026, 10, 2, 8, 0, 0, DateTimeKind.Utc),
                Price = 7000m,
                SeatsRemaining = 45,
                Capacity = 45,
                BusType = "Xe buýt 45 chỗ",
            });
        }

        return danhSach;
    }
}
