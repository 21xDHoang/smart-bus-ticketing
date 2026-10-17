using Microsoft.EntityFrameworkCore;
using SmartBus.Api.Data;
using SmartBus.Api.Dtos.Trips;

namespace SmartBus.Api.Services;

/// <summary>
/// Lớp bọc NGOÀI CÙNG của chuỗi <c>IRouteTripsService</c>: chạy đúng lượt sửa chuyến như cũ, rồi báo
/// cho hành khách của chuyến nếu giờ chạy thực sự đổi — task Sprint 4 *"Service thông báo trễ chuyến /
/// thay đổi lịch trình"* (dòng 44, Phùng Duy Hoàng). Cùng lối <see cref="NotifyingRefundAttemptService"/>
/// và ba lớp <c>NotifyingSeatHold*</c>: <c>IRouteTripsService</c> trong DI trỏ vào lớp bọc này
/// (Program.cs), nên MỌI lượt sửa chuyến qua <c>PUT /routes/{routeId}/trips/{id}</c> đi qua đây mà
/// <see cref="RouteTripsService.UpdateAsync"/> không phải biết gì về SignalR.
///
/// <para>
/// <b>Chuỗi lớp bọc trong DI:</b> Notifying (lớp này) → <see cref="ArrivalEstimatingRouteTripsService"/>
/// → <see cref="RouteTripsService"/>. Lớp này nhận <c>IRouteTripsService</c> chứ không nhận lớp trần để
/// có thể nằm ngoài lớp điền giờ đến; nhờ vậy <c>GenerateAsync</c> vẫn đi qua lớp ước lượng, còn
/// <c>UpdateAsync</c> vẫn chạy trước khi chụp/so giờ như cũ. Không đổi thứ tự: Notifying phải ngoài
/// cùng để mọi lượt sửa chuyến — kể cả lượt đi qua lớp ước lượng — đều được báo tin.
/// </para>
///
/// <para>
/// <b>Vì sao bọc ở tầng interface chứ không sửa thẳng service:</b> đó là file của Trần Trung Hiếu
/// (luật E1 — không sửa file của người khác), và tách ra còn giữ đúng một việc phụ trợ ngoài luồng
/// nghiệp vụ. <see cref="RouteTripsService"/> vẫn là nguồn duy nhất của các kiểm tra trùng giờ, trần
/// chuyến/ngày và validate request; lớp bọc chỉ đọc thêm ảnh chụp trước khi chuyển tiếp.
/// </para>
///
/// <para>
/// <b>Ảnh chụp giờ cũ được lấy TRƯỚC khi chuyển lượt PUT:</b> <see cref="RouteTripsService.UpdateAsync"/>
/// nạp chính dòng <c>Trips</c> đó vào change tracker rồi sửa nó, nên đọc sau lượt gọi là đọc giờ mới.
/// Truy vấn chụp dùng <c>AsNoTracking</c> + chiếu hai cột, nên không đụng dòng mà lớp trong sắp sửa
/// nạp và không tốn tải entity kèm quan hệ.
/// </para>
///
/// <para>
/// <b>Chỉ báo khi lượt sửa THÀNH CÔNG</b> (cùng lối <see cref="NotifyingSeatHoldCreateService"/> lọc
/// bằng <c>result.Success</c>): 404/400 thì giờ chưa đổi, không có gì để báo. Còn "giờ có thực sự đổi
/// không" thì notifier tự quyết định bằng cách so ảnh chụp với dữ liệu đã lưu — lớp bọc cố ý mỏng,
/// không lọc hộ.
/// </para>
///
/// <para>
/// ⚠️ <c>cancellationToken</c> của mọi lượt gọi được chuyển tiếp nguyên vẹn, kể cả lượt chụp giờ cũ:
/// token của request sửa chuyến không được nuốt. <see cref="ITripScheduleChangeNotifier"/> cố ý không
/// nhận token — xem doc-comment interface đó.
/// </para>
/// </summary>
public class NotifyingRouteTripsService : IRouteTripsService
{
    private readonly IRouteTripsService _inner;

    private readonly ITripScheduleChangeNotifier _notifier;

    private readonly AppDbContext _db;

    private readonly ILogger<NotifyingRouteTripsService> _logger;

    public NotifyingRouteTripsService(
        IRouteTripsService inner,
        ITripScheduleChangeNotifier notifier,
        AppDbContext db,
        ILogger<NotifyingRouteTripsService> logger)
    {
        _inner = inner;
        _notifier = notifier;
        _db = db;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<ServiceResult<TripListResponse>> ListAsync(
        Guid routeId,
        ListTripsRequest request,
        CancellationToken cancellationToken = default)
    {
        return _inner.ListAsync(routeId, request, cancellationToken);
    }

    /// <inheritdoc />
    public Task<ServiceResult<TripResponse>> GetByIdAsync(
        Guid routeId,
        Guid id,
        CancellationToken cancellationToken = default)
    {
        return _inner.GetByIdAsync(routeId, id, cancellationToken);
    }

    /// <inheritdoc />
    public Task<ServiceResult<TripResponse>> CreateAsync(
        Guid routeId,
        CreateTripRequest request,
        CancellationToken cancellationToken = default)
    {
        return _inner.CreateAsync(routeId, request, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ServiceResult<TripResponse>> UpdateAsync(
        Guid routeId,
        Guid id,
        UpdateTripRequest request,
        CancellationToken cancellationToken = default)
    {
        // Chụp giờ cũ TRƯỚC lượt gọi — lớp trong sắp sửa chính dòng này (xem doc-comment lớp).
        var previous = await SnapshotAsync(routeId, id, cancellationToken);

        var result = await _inner.UpdateAsync(routeId, id, request, cancellationToken);

        if (result.Success)
        {
            // NotifyAsync không bao giờ ném lỗi và không nhận token — dòng thông báo phải được ghi kể
            // cả khi client đóng kết nối ngay sau khi nhận 200. Xem ITripScheduleChangeNotifier.
            await _notifier.NotifyAsync(id, previous);
        }

        return result;
    }

    /// <inheritdoc />
    public Task<ServiceResult<TripResponse>> DeleteAsync(
        Guid routeId,
        Guid id,
        CancellationToken cancellationToken = default)
    {
        return _inner.DeleteAsync(routeId, id, cancellationToken);
    }

    /// <inheritdoc />
    public Task<ServiceResult<GenerateTripsResponse>> GenerateAsync(
        Guid routeId,
        GenerateTripsRequest request,
        CancellationToken cancellationToken = default)
    {
        return _inner.GenerateAsync(routeId, request, cancellationToken);
    }

    /// <summary>
    /// Đọc giờ chạy trước lượt sửa. Lỗi đọc KHÔNG được làm hỏng lượt sửa: trả null để notifier im
    /// lặng (nó không phân loại được khi thiếu mốc cũ). Cancellation vẫn nổi lên như mọi lượt gọi khác.
    /// </summary>
    private async Task<TripScheduleSnapshot?> SnapshotAsync(
        Guid routeId,
        Guid id,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _db.Trips
                .AsNoTracking()
                .Where(t => t.Id == id && t.RouteId == routeId)
                .Select(t => new TripScheduleSnapshot(t.DepartureTime, t.ArrivalTime))
                .FirstOrDefaultAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex,
                "Không chụp được giờ chạy cũ của chuyến {TripId} trước lượt sửa — lượt sửa vẫn chạy,"
                + " nhưng sẽ không có thông báo đổi lịch trình.",
                id);
            return null;
        }
    }
}
