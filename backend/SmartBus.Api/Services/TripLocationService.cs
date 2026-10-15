using Microsoft.EntityFrameworkCore;
using SmartBus.Api.Data;
using SmartBus.Api.Dtos.Trips;
using SmartBus.Api.Entities;

namespace SmartBus.Api.Services;

/// <summary>
/// Cài đặt <see cref="ITripLocationService"/> — ghi vị trí hiện tại của xe vào chuyến (US 10
/// "Thông báo trạm", Sprint 4, task *"API cập nhật vị trí chuyến (CurrentLat/CurrentLng/
/// CurrentStopId)"* — Trần Trung Hiếu).
///
/// Bốn chốt nghiệp vụ, đọc trước khi sửa:
///
/// 1. **Chỉ tài xế được phân công của chuyến mới ghi được.** Truy vấn lọc ngay theo DriverId
///    của người gọi: chuyến không tồn tại và chuyến của tài xế khác CÙNG trả 404 — không để lộ
///    chuyến nào đang chạy (cùng lối "vé của người khác trả 404" của MyTicketsService).
/// 2. **Chỉ chuyến Scheduled hoặc Running nhận vị trí.** Completed/Cancelled → 409: vị trí chỉ
///    có nghĩa khi xe đang trên đường (quy ước A8.6). Cố ý NHẬN cả Scheduled: chưa có endpoint
///    "bắt đầu chuyến" nào đổi trạng thái sang Running, chặn Scheduled thì thiết bị không báo
///    được vị trí trước giờ chạy.
/// 3. **Trạm phải thuộc tuyến của chuyến.** CurrentStopId là "trạm vừa đi qua" — trạm lạ không
///    có StopOrder trên tuyến sẽ làm hỏng phép tính "cách trạm kế tiếp bao xa" của service
///    thông báo (Kiên, cùng story). Không tồn tại → 404; tồn tại nhưng không nằm trong
///    RouteStops của tuyến → 400 theo trường currentStopId.
/// 4. **positionUpdatedAt luôn là giờ server nhận request** — không tin giờ thiết bị gửi lên.
/// </summary>
public sealed class TripLocationService : ITripLocationService
{
    private readonly AppDbContext _db;

    public TripLocationService(AppDbContext db) => _db = db;

    public async Task<ServiceResult<TripLocationResponse>> UpdateAsync(
        Guid tripId,
        Guid driverUserId,
        UpdateTripLocationRequest request,
        CancellationToken cancellationToken)
    {
        var trip = await _db.Trips
            .FirstOrDefaultAsync(t => t.Id == tripId && t.DriverId == driverUserId, cancellationToken);

        if (trip is null)
        {
            // Cùng một câu 404 cho "không tồn tại" và "không phải chuyến của tài xế này" — không
            // để lộ chuyến nào đang có tài xế khác điều khiển.
            return ServiceResult<TripLocationResponse>.NotFound("Không tìm thấy chuyến xe.");
        }

        if (trip.Status is TripStatus.Completed or TripStatus.Cancelled)
        {
            var message = trip.Status == TripStatus.Completed
                ? "Chuyến đã hoàn thành, không thể cập nhật vị trí."
                : "Chuyến đã hủy, không thể cập nhật vị trí.";

            return ServiceResult<TripLocationResponse>.Conflict(message);
        }

        // Bỏ trống currentStopId = chỉ cập nhật toạ độ, giữ nguyên trạm hiện tại — thiết bị
        // không phải lúc nào cũng xác định được trạm gần nhất.
        if (request.CurrentStopId is { } stopId)
        {
            var stopExists = await _db.Stops.AnyAsync(s => s.Id == stopId, cancellationToken);

            if (!stopExists)
            {
                return ServiceResult<TripLocationResponse>.NotFound("Không tìm thấy trạm dừng.");
            }

            var stopOnRoute = await _db.RouteStops.AnyAsync(
                rs => rs.RouteId == trip.RouteId && rs.StopId == stopId, cancellationToken);

            if (!stopOnRoute)
            {
                const string message = "Trạm không thuộc tuyến của chuyến.";

                return ServiceResult<TripLocationResponse>.Invalid(
                    message,
                    new Dictionary<string, string[]> { ["currentStopId"] = [message] });
            }

            trip.CurrentStopId = stopId;
        }

        var nowUtc = DateTime.UtcNow;

        trip.CurrentLat = request.CurrentLat!.Value;
        trip.CurrentLng = request.CurrentLng!.Value;
        trip.PositionUpdatedAt = nowUtc;
        trip.UpdatedAt = nowUtc;

        await _db.SaveChangesAsync(cancellationToken);

        return ServiceResult<TripLocationResponse>.Ok(new TripLocationResponse
        {
            TripId = trip.Id,
            CurrentStopId = trip.CurrentStopId,
            CurrentLat = trip.CurrentLat!.Value,
            CurrentLng = trip.CurrentLng!.Value,
            PositionUpdatedAt = trip.PositionUpdatedAt!.Value,
        });
    }
}
