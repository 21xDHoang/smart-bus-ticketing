using Microsoft.Extensions.DependencyInjection;
using SmartBus.Api.Services;

namespace SmartBus.Tests;

/// <summary>
/// Ghim chỗ nối THÔNG BÁO TRỄ / ĐỔI LỊCH TRÌNH vào app — ba dòng <c>Program.cs</c> của task dòng 44
/// Sprint 4 (Phùng Duy Hoàng), phần mà <see cref="TripScheduleChangeNotifierTests"/> và
/// <see cref="NotifyingRouteTripsServiceTests"/> không nhìn tới vì chúng dựng service tay.
///
/// <para>
/// 🔴 Cả ba dòng thiếu đều là lỗi IM LẶNG ở mức độ khác nhau:
/// </para>
/// <list type="bullet">
///   <item>Thiếu <c>AddScoped&lt;IRouteTripsService&gt;</c> trỏ lớp bọc — mọi endpoint sửa chuyến vẫn
///   200, giờ vẫn đổi, tất cả test cũ vẫn xanh; chỉ mỗi khách là không bao giờ nhận thông báo. Đây là
///   ca dễ xảy ra nhất vì ai đó "dọn gọn" dòng đăng ký cũ
///   <c>AddScoped&lt;IRouteTripsService, RouteTripsService&gt;()</c> sẽ thay thế lớp bọc mà không có
///   triệu chứng nào.</item>
///   <item>Thiếu <c>AddScoped&lt;ITripScheduleChangeNotifier, TripScheduleChangeNotifier&gt;</c> — factory
///   của lớp bọc không resolve được tham số, nên MỌI lượt PUT sửa chuyến ném
///   <c>InvalidOperationException</c> (500), tức là đỏ ở giữa luồng nghiệp vụ chứ không ở chỗ khai
///   thiếu.</item>
///   <item>Thiếu <c>AddScoped&lt;RouteTripsService&gt;</c> (lớp trần) — cùng triệu chứng 500 như trên,
///   vì lớp bọc nhận lớp trần qua constructor chứ không new tay.</item>
/// </list>
///
/// <para>
/// Cùng lối <see cref="RefundRetryWiringTests"/> (lớp bọc chuỗi hoàn tiền) và
/// <see cref="TicketRequestResultWiringTests"/> (cổng thông báo kết quả huỷ/đổi vé): resolve QUA SCOPE
/// chứ không từ <c>factory.Services</c>, đúng hình dạng một request thật.
/// </para>
/// </summary>
public class TripScheduleChangeWiringTests
{
    [Fact]
    public void IRouteTripsService_tro_vao_LOP_BOC_chu_khong_phai_lop_tran()
    {
        using var factory = new TestAppFactory();
        using var scope = factory.Services.CreateScope();

        // IsType chứ NotNull: đăng ký lùi về lớp trần vẫn resolve được, vẫn biên dịch, mọi endpoint vẫn
        // chạy — chỉ tính năng thông báo biến mất. Phép so lớp là thứ duy nhất bắt được ca đó.
        Assert.IsType<NotifyingRouteTripsService>(
            scope.ServiceProvider.GetRequiredService<IRouteTripsService>());
    }

    [Fact]
    public void Notifier_duoc_dang_ky_bang_dung_lop_that()
    {
        using var factory = new TestAppFactory();
        using var scope = factory.Services.CreateScope();

        Assert.IsType<TripScheduleChangeNotifier>(
            scope.ServiceProvider.GetRequiredService<ITripScheduleChangeNotifier>());
    }

    [Fact]
    public void Lop_tran_van_resolve_duoc_de_lop_boc_boc_no()
    {
        using var factory = new TestAppFactory();
        using var scope = factory.Services.CreateScope();

        // Lớp bọc nhận RouteTripsService cụ thể qua constructor (không new tay), nên dòng AddScoped
        // lớp trần là một mắt của chuỗi. Bài này ghim mắt đó độc lập với lượt resolve interface —
        // thiếu nó thì bài đầu cũng đỏ, nhưng thông báo lỗi ở đây chỉ thẳng vào dòng khai thiếu.
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<RouteTripsService>());
    }
}
