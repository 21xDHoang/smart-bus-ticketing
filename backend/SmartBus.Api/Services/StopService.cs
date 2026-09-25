using Microsoft.EntityFrameworkCore;
using SmartBus.Api.Data;
using SmartBus.Api.Dtos.Stops;
using SmartBus.Api.Entities;

namespace SmartBus.Api.Services;

/// <summary>
/// Cài đặt <see cref="IStopService"/>. Story 12 — Trần Trung Hiếu.
/// </summary>
public class StopService : IStopService
{
    private const string StopNotFoundMessage = "Không tìm thấy trạm dừng";

    private const string StopInUseMessage =
        "Trạm đang nằm trên tuyến đường nên không thể xóa. Gỡ trạm khỏi tuyến trước.";

    private readonly AppDbContext _db;

    public StopService(AppDbContext db) => _db = db;

    public async Task<ServiceResult<IReadOnlyList<StopResponse>>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        var stops = await _db.Stops
            .AsNoTracking()
            .OrderBy(s => s.Name)
            // Chốt thêm theo Id: nhiều trạm trùng tên thì thứ tự vẫn ổn định.
            .ThenBy(s => s.Id)
            .ToListAsync(cancellationToken);

        return ServiceResult<IReadOnlyList<StopResponse>>.Ok(
            stops.Select(ToResponse).ToList());
    }

    public async Task<ServiceResult<StopResponse>> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var stop = await _db.Stops
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

        return stop is null
            ? ServiceResult<StopResponse>.NotFound(StopNotFoundMessage)
            : ServiceResult<StopResponse>.Ok(ToResponse(stop));
    }

    public async Task<ServiceResult<StopResponse>> CreateAsync(
        StopRequest request,
        CancellationToken cancellationToken = default)
    {
        var stop = new Stop
        {
            Name = request.Name.Trim(),
            Address = request.Address.Trim(),
            Latitude = request.Latitude,
            Longitude = request.Longitude,
        };

        _db.Stops.Add(stop);
        await _db.SaveChangesAsync(cancellationToken);

        return ServiceResult<StopResponse>.Ok(ToResponse(stop));
    }

    public async Task<ServiceResult<StopResponse>> UpdateAsync(
        Guid id,
        StopRequest request,
        CancellationToken cancellationToken = default)
    {
        var stop = await _db.Stops.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (stop is null)
        {
            return ServiceResult<StopResponse>.NotFound(StopNotFoundMessage);
        }

        stop.Name = request.Name.Trim();
        stop.Address = request.Address.Trim();
        stop.Latitude = request.Latitude;
        stop.Longitude = request.Longitude;
        stop.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);

        return ServiceResult<StopResponse>.Ok(ToResponse(stop));
    }

    public async Task<ServiceResult<bool>> DeleteAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var stop = await _db.Stops.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (stop is null)
        {
            return ServiceResult<bool>.NotFound(StopNotFoundMessage);
        }

        // Trạm nằm trên tuyến thì FK của RouteStops chặn xoá (Restrict — quy ước A5).
        // Kiểm tra trước để người dùng nhận 409 kèm thông báo hiểu được, thay vì lỗi 500.
        if (await _db.RouteStops.AnyAsync(rs => rs.StopId == id, cancellationToken))
        {
            return ServiceResult<bool>.Conflict(StopInUseMessage);
        }

        _db.Stops.Remove(stop);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Hai request chạy song song: trạm vừa được gán vào tuyến sau bước kiểm tra ở trên.
            // Ràng buộc FK trong CSDL là lớp chặn cuối cùng — trả cùng lỗi 409.
            return ServiceResult<bool>.Conflict(StopInUseMessage);
        }

        return ServiceResult<bool>.Ok(true);
    }

    private static StopResponse ToResponse(Stop stop) => new()
    {
        Id = stop.Id,
        Name = stop.Name,
        Address = stop.Address,
        Latitude = stop.Latitude,
        Longitude = stop.Longitude,
    };
}
