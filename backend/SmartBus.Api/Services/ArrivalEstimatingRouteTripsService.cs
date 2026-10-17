using Microsoft.EntityFrameworkCore;
using SmartBus.Api.Data;
using SmartBus.Api.Dtos.Trips;
using SmartBus.Api.Entities;

namespace SmartBus.Api.Services;

/// <summary>
/// Lớp bọc điền GIỜ ĐẾN cho các chuyến vừa sinh hàng loạt (task giờ đến + điểm đến, Sprint 4 —
/// Phùng Duy Hoàng).
///
/// <para>
/// Vì sao bọc thay vì sửa <see cref="RouteTripsService"/>: file đó của Hiếu (luật E1). Lớp bọc này
/// nằm giữa <see cref="NotifyingRouteTripsService"/> và <see cref="FleetAwareRouteTripsService"/>,
/// chỉ can thiệp DUY NHẤT vào <see cref="GenerateAsync"/>; năm lượt còn lại chuyển tiếp nguyên vẹn
/// (lượt <c>CreateAsync</c> đi tiếp xuống lớp hiểu đội xe nhiều chiếc).
/// </para>
///
/// <para>
/// Vì sao cần giờ đến: vé bán theo CẢ CHUYẾN (quy ước A8.1) nên bến cuối là một phần của vé. Không
/// có giờ đến thì màn hình lập lịch không tính được ETA, không phát hiện được lịch chồng lấn, và job
/// nhân bản ngày sau cũng không có gì để chép sang. <see cref="Trip.ArrivalTime"/> vốn đã nullable
/// với ghi chú "lúc sinh chuyến có thể chưa chốt được" — lớp này chốt bằng ước lượng.
/// </para>
///
/// <para>
/// Công thức ước lượng giống hệt <c>SampleDataSeeder.ThoiGianChayUocLuong</c>: tốc độ bình quân
/// 25 km/h, làm tròn LÊN 5 phút, tối thiểu 15 phút. Chép công thức thay vì gọi seeder vì seeder là
/// công cụ dựng dữ liệu mẫu, không phải nghiệp vụ dùng ở request thật; giữ hai nơi khớp nhau bằng
/// test thay vì phụ thuộc chéo.
/// </para>
///
/// <para>
/// Hợp đồng lỗi: lượt sinh đã COMMIT ở lớp trong trước khi lớp này chạy. Vì vậy lỗi khi ước lượng
/// (thiếu tuyến, CSDL trục trặc) chỉ ghi log và trả nguyên kết quả thành công — không được biến một
/// lượt sinh đã thành công thành 500, vì màn hình sẽ nói "thất bại" trong khi chuyến đã nằm trong
/// CSDL.
/// </para>
/// </summary>
public class ArrivalEstimatingRouteTripsService : IRouteTripsService
{
    /// <summary>Tốc độ bình quân ước lượng — khớp seeder mẫu và ghi chú trên màn hình.</summary>
    private const decimal AverageSpeedKmPerHour = 25m;

    /// <summary>Làm tròn lên theo bước 5 phút cho giờ đến dễ đọc, khớp seeder mẫu.</summary>
    private const int RoundingMinutes = 5;

    /// <summary>Chuyến ngắn đến đâu cũng cần ít nhất 15 phút — tránh giờ đến trùng giờ đi.</summary>
    private const int MinimumTravelMinutes = 15;

    private readonly IRouteTripsService _inner;
    private readonly AppDbContext _db;
    private readonly ILogger<ArrivalEstimatingRouteTripsService> _logger;

    public ArrivalEstimatingRouteTripsService(
        IRouteTripsService inner,
        AppDbContext db,
        ILogger<ArrivalEstimatingRouteTripsService> logger)
    {
        _inner = inner;
        _db = db;
        _logger = logger;
    }

    public Task<ServiceResult<TripListResponse>> ListAsync(
        Guid routeId,
        ListTripsRequest request,
        CancellationToken cancellationToken = default)
        => _inner.ListAsync(routeId, request, cancellationToken);

    public Task<ServiceResult<TripResponse>> GetByIdAsync(
        Guid routeId,
        Guid id,
        CancellationToken cancellationToken = default)
        => _inner.GetByIdAsync(routeId, id, cancellationToken);

    public Task<ServiceResult<TripResponse>> CreateAsync(
        Guid routeId,
        CreateTripRequest request,
        CancellationToken cancellationToken = default)
        => _inner.CreateAsync(routeId, request, cancellationToken);

    public Task<ServiceResult<TripResponse>> UpdateAsync(
        Guid routeId,
        Guid id,
        UpdateTripRequest request,
        CancellationToken cancellationToken = default)
        => _inner.UpdateAsync(routeId, id, request, cancellationToken);

    public Task<ServiceResult<TripResponse>> DeleteAsync(
        Guid routeId,
        Guid id,
        CancellationToken cancellationToken = default)
        => _inner.DeleteAsync(routeId, id, cancellationToken);

    /// <summary>
    /// Chuyển tiếp lượt sinh rồi điền giờ đến cho đúng các chuyến vừa tạo. Chỉ đụng dòng
    /// <c>ArrivalTime</c> đang null, nên nếu sau này lớp trong tự chốt giờ đến thì lớp này không đè.
    /// </summary>
    public async Task<ServiceResult<GenerateTripsResponse>> GenerateAsync(
        Guid routeId,
        GenerateTripsRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await _inner.GenerateAsync(routeId, request, cancellationToken);

        if (!result.Success || result.Data is not { Total: > 0 } response)
        {
            return result;
        }

        try
        {
            await FillArrivalTimesAsync(routeId, response, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Huỷ request phải nổi lên trên — nuốt vào đây là biến một lượt huỷ thành 200 dở dang.
            throw;
        }
        catch (Exception exception)
        {
            // Chuyến đã lưu xong ở lớp trong; báo lỗi ở đây sẽ khiến màn hình nói "sinh thất bại"
            // trong khi dữ liệu đã vào CSDL. Giờ đến là thông tin phụ — log rồi trả kết quả gốc.
            _logger.LogWarning(
                exception,
                "Không ước lượng được giờ đến cho {Count} chuyến vừa sinh của tuyến {RouteId}",
                response.Total,
                routeId);
        }

        return result;
    }

    /// <summary>
    /// Đọc tuyến để lấy chiều dài, tính thời gian chạy, ghi <c>ArrivalTime</c> cho các chuyến vừa
    /// sinh rồi cập nhật luôn DTO trả về — màn hình thấy giờ đến ngay, không phải gọi lại API.
    /// </summary>
    private async Task FillArrivalTimesAsync(
        Guid routeId,
        GenerateTripsResponse response,
        CancellationToken cancellationToken)
    {
        var distanceKm = await _db.Routes.AsNoTracking()
            .Where(route => route.Id == routeId)
            .Select(route => (decimal?)route.DistanceKm)
            .FirstOrDefaultAsync(cancellationToken);

        if (distanceKm is null)
        {
            _logger.LogWarning(
                "Không tìm thấy tuyến {RouteId} để ước lượng giờ đến cho {Count} chuyến vừa sinh",
                routeId,
                response.Total);
            return;
        }

        var travelMinutes = EstimateTravelMinutes(distanceKm.Value);

        var ids = response.Items.Select(item => item.Id).ToList();
        if (ids.Count == 0)
        {
            return;
        }

        var trips = await _db.Trips
            .Where(trip => ids.Contains(trip.Id))
            .ToListAsync(cancellationToken);

        foreach (var trip in trips)
        {
            // ??=: tôn trọng giờ đến lớp trong đã chốt (nếu có); chỉ điền chỗ còn trống.
            trip.ArrivalTime ??= trip.DepartureTime.AddMinutes(travelMinutes);
        }

        await _db.SaveChangesAsync(cancellationToken);

        var arrivalById = trips
            .Where(trip => trip.ArrivalTime is not null)
            .ToDictionary(trip => trip.Id, trip => trip.ArrivalTime);

        foreach (var item in response.Items)
        {
            if (arrivalById.TryGetValue(item.Id, out var arrivalTime))
            {
                item.ArrivalTime = arrivalTime;
            }
        }
    }

    /// <summary>
    /// Thời gian chạy ước lượng (phút) từ chiều dài tuyến: 25 km/h, làm tròn lên bội số 5, tối
    /// thiểu 15. Tuyến dài 20 km → 48 phút → 50 phút; tuyến 1 km → 2,4 phút → 15 phút.
    ///
    /// <para>
    /// <c>public</c> (không phải <c>internal</c>) để test ghim thẳng công thức — dự án không bật
    /// <c>InternalsVisibleTo</c>. Không phải API công khai: lớp này chỉ được resolve qua
    /// <see cref="IRouteTripsService"/>.
    /// </para>
    /// </summary>
    public static int EstimateTravelMinutes(decimal distanceKm)
    {
        var rawMinutes = distanceKm / AverageSpeedKmPerHour * 60m;
        var roundedUp = Math.Ceiling(rawMinutes / RoundingMinutes) * RoundingMinutes;
        return (int)Math.Max(MinimumTravelMinutes, roundedUp);
    }
}
