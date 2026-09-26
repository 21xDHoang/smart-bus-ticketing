using Microsoft.EntityFrameworkCore;
using SmartBus.Api.Data;
using SmartBus.Api.Dtos.RouteStops;
using SmartBus.Api.Entities;

namespace SmartBus.Api.Services;

/// <summary>
/// Cài đặt <see cref="IRouteStopService"/>. Story 12 — Nguyễn Duy Kiên.
/// </summary>
public class RouteStopService : IRouteStopService
{
    private const string RouteNotFoundMessage = "Không tìm thấy tuyến đường";

    private const string StopNotFoundMessage = "Không tìm thấy trạm dừng";

    private const string StopNotOnRouteMessage = "Trạm không nằm trên tuyến đường này";

    private const string DuplicateStopMessage = "Trạm đã nằm trên tuyến đường này";

    private const string EmptyItemsMessage = "Danh sách trạm không được để trống";

    private const string MissingStopIdMessage = "Danh sách trạm có trạm thiếu stopId";

    private const string DuplicateStopInItemsMessage = "Danh sách trạm có trạm bị lặp lại";

    private const string ItemsNotMatchMessage =
        "Danh sách trạm phải khớp đúng các trạm hiện có của tuyến";

    private const string DistanceNegativeMessage = "Khoảng cách không được âm";

    private const string DistanceTooLargeMessage = "Khoảng cách tối đa 9999.99 km";

    /// <summary>
    /// Trần của cột <c>numeric(6,2)</c>: 6 chữ số tổng, trong đó 2 chữ số thập phân.
    /// Phải chặn ở đây chứ không trông vào CSDL: PostgreSQL trả lỗi 22003 khi giá trị tràn cột,
    /// mà lỗi đó đi qua <c>DbUpdateException</c> nên sẽ bị nuốt thành 409 "trạm đã nằm trên
    /// tuyến" — một thông báo sai hẳn nguyên nhân. Provider InMemory của bộ test còn không
    /// dựng <c>HasPrecision</c>, nên chỉ có tầng service mới kiểm tra được cho cả hai đường.
    /// </summary>
    private const decimal MaxDistanceKm = 9999.99m;

    private readonly AppDbContext _db;

    public RouteStopService(AppDbContext db) => _db = db;

    public async Task<ServiceResult<IReadOnlyList<RouteStopResponse>>> ListByRouteAsync(
        Guid routeId,
        CancellationToken cancellationToken = default)
    {
        if (!await RouteExistsAsync(routeId, cancellationToken))
        {
            return ServiceResult<IReadOnlyList<RouteStopResponse>>.NotFound(RouteNotFoundMessage);
        }

        var routeStops = await OrderedByRouteQuery(routeId)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return ServiceResult<IReadOnlyList<RouteStopResponse>>.Ok(
            routeStops.Select(ToResponse).ToList());
    }

    public async Task<ServiceResult<RouteStopResponse>> AssignAsync(
        Guid routeId,
        AssignStopToRouteRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!await RouteExistsAsync(routeId, cancellationToken))
        {
            return ServiceResult<RouteStopResponse>.NotFound(RouteNotFoundMessage);
        }

        // [Required] trên Guid? đã chặn trường hợp thiếu stopId; đọc thẳng .Value vì tới đây
        // thì nó chắc chắn có giá trị.
        var stopId = request.StopId!.Value;

        // Nạp cả thực thể chứ không chỉ AnyAsync: cần tên/địa chỉ/toạ độ để trả về đủ như GET,
        // và gán vào navigation thì EF tự điền StopId, khỏi phải truy vấn thêm lần nữa.
        var stop = await _db.Stops.FirstOrDefaultAsync(s => s.Id == stopId, cancellationToken);
        if (stop is null)
        {
            return ServiceResult<RouteStopResponse>.NotFound(StopNotFoundMessage);
        }

        if (ValidateDistance(request.DistanceKm) is { } distanceError)
        {
            return ServiceResult<RouteStopResponse>.Invalid(
                distanceError, FieldErrors("distanceKm", distanceError));
        }

        // Kiểm tra trước để người dùng nhận 409 kèm thông báo hiểu được. Ràng buộc unique
        // (RouteId, StopId) dưới CSDL vẫn là lớp chặn cuối, bắt ở khối try bên dưới — nhưng
        // provider InMemory của bộ test KHÔNG dựng unique index, nên chính bước này mới là
        // thứ giữ cho các ca test chạy đúng.
        if (await _db.RouteStops.AnyAsync(
                rs => rs.RouteId == routeId && rs.StopId == stopId, cancellationToken))
        {
            return ServiceResult<RouteStopResponse>.Conflict(DuplicateStopMessage);
        }

        var routeStop = new RouteStop
        {
            RouteId = routeId,
            StopId = stopId,
            Stop = stop,
            StopOrder = await NextStopOrderAsync(routeId, cancellationToken),
            DistanceKm = request.DistanceKm,
        };

        _db.RouteStops.Add(routeStop);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Hai request gán cùng một trạm chạy song song đều lọt qua bước kiểm tra ở trên.
            // Ràng buộc unique (RouteId, StopId) trong CSDL là lớp chặn cuối cùng — trả cùng
            // lỗi 409 thay vì để người dùng nhận lỗi 500.
            return ServiceResult<RouteStopResponse>.Conflict(DuplicateStopMessage);
        }

        return ServiceResult<RouteStopResponse>.Ok(ToResponse(routeStop));
    }

    public async Task<ServiceResult<IReadOnlyList<RouteStopResponse>>> ReorderAsync(
        Guid routeId,
        ReorderRouteStopsRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!await RouteExistsAsync(routeId, cancellationToken))
        {
            return ServiceResult<IReadOnlyList<RouteStopResponse>>.NotFound(RouteNotFoundMessage);
        }

        var items = request.Items;

        // Items khởi tạo sẵn bằng [] nên chỉ null khi client gửi thẳng "items": null.
        if (items is null || items.Count == 0)
        {
            return InvalidItems(EmptyItemsMessage);
        }

        var submitted = new List<Guid>(items.Count);

        foreach (var item in items)
        {
            if (item.StopId is not { } stopId)
            {
                return InvalidItems(MissingStopIdMessage);
            }

            submitted.Add(stopId);
        }

        // Kiểm tra trùng TRƯỚC khi so tập: mảng [A, A] cũng là "không khớp tập trạm hiện có",
        // nhưng thông báo "trạm bị lặp lại" mới chỉ đúng chỗ để người gọi sửa.
        if (submitted.Distinct().Count() != submitted.Count)
        {
            return InvalidItems(DuplicateStopInItemsMessage);
        }

        var existing = await OrderedByRouteQuery(routeId).ToListAsync(cancellationToken);

        // So bằng HashSet.SetEquals, KHÔNG dùng "đếm cho bằng nhau rồi Contains từng phần tử":
        // cách đó vẫn cho lọt mảng [A, A, B] khi tuyến có {A, B} — đếm thì bằng nhau, mọi phần
        // tử đều có trong danh sách cũ, mà thực ra B bị ghi hai lần còn A thì mất.
        if (!submitted.ToHashSet().SetEquals(existing.Select(rs => rs.StopId)))
        {
            return InvalidItems(ItemsNotMatchMessage);
        }

        // Kiểm tra hết rồi mới sửa: nếu vừa sửa vừa kiểm tra thì một mảng sai ở phần tử cuối
        // sẽ để lại nửa danh sách đã đổi trong change tracker — lần này chưa ghi xuống CSDL
        // (DbContext sống theo từng request) nhưng rất dễ vỡ nếu sau này có ai gọi lại.
        foreach (var item in items)
        {
            if (item.DistanceKm is { } distanceKm
                && ValidateDistance(distanceKm) is { } distanceError)
            {
                return ServiceResult<IReadOnlyList<RouteStopResponse>>.Invalid(
                    distanceError, FieldErrors("distanceKm", distanceError));
            }
        }

        var byStopId = existing.ToDictionary(rs => rs.StopId);

        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            var routeStop = byStopId[item.StopId!.Value];

            // stopOrder do server suy ra từ vị trí trong mảng, client không gửi.
            routeStop.StopOrder = index + 1;

            // Bỏ trống = giữ nguyên số đang có, không phải gán 0 — xem chú thích ở DTO.
            if (item.DistanceKm is { } distanceKm)
            {
                routeStop.DistanceKm = distanceKm;
            }
        }

        await _db.SaveChangesAsync(cancellationToken);

        var reordered = existing
            .OrderBy(rs => rs.StopOrder)
            .ThenBy(rs => rs.Id)
            .Select(ToResponse)
            .ToList();

        return ServiceResult<IReadOnlyList<RouteStopResponse>>.Ok(reordered);
    }

    public async Task<ServiceResult<bool>> RemoveAsync(
        Guid routeId,
        Guid id,
        CancellationToken cancellationToken = default)
    {
        if (!await RouteExistsAsync(routeId, cancellationToken))
        {
            return ServiceResult<bool>.NotFound(RouteNotFoundMessage);
        }

        // Tìm theo khoá của dòng bảng nối, không phải theo StopId — xem chú thích ở
        // IRouteStopService.RemoveAsync. Kèm RouteId để dòng của tuyến khác ra 404.
        var routeStop = await _db.RouteStops.FirstOrDefaultAsync(
            rs => rs.RouteId == routeId && rs.Id == id, cancellationToken);

        if (routeStop is null)
        {
            // Trạm có thể có thật nhưng không nằm trên tuyến này — vẫn là 404. Gỡ một thứ
            // không có sẵn không phải là thành công, nên không trả 204.
            return ServiceResult<bool>.NotFound(StopNotOnRouteMessage);
        }

        // Xoá cứng: RouteStops không có cột trạng thái và không có IsDeleted (quy ước A4),
        // gỡ trạm khỏi tuyến là mất dòng thật.
        _db.RouteStops.Remove(routeStop);

        // Dồn số các trạm còn lại thành 1..N liên tục để StopOrder không có lỗ hổng.
        // Xếp kèm khoá phụ theo Id: hai trạm cùng StopOrder (dữ liệu cũ, hoặc do hai request
        // gán song song) mà thiếu khoá phụ thì thứ tự dồn không xác định giữa các lần chạy.
        // Không Include(Stop) như OrderedByRouteQuery: ở đây chỉ cần đọc và ghi lại StopOrder,
        // kéo thêm thực thể trạm về là thừa.
        var remaining = await _db.RouteStops
            .Where(rs => rs.RouteId == routeId && rs.Id != routeStop.Id)
            .OrderBy(rs => rs.StopOrder)
            .ThenBy(rs => rs.Id)
            .ToListAsync(cancellationToken);

        for (var index = 0; index < remaining.Count; index++)
        {
            remaining[index].StopOrder = index + 1;
        }

        // Một SaveChangesAsync cho cả xoá lẫn dồn số: tách thành hai lần lưu thì lần thứ hai
        // lỗi sẽ để lại tuyến đã mất trạm nhưng thứ tự còn lỗ hổng — đúng cái bất biến mà
        // bước dồn số sinh ra để giữ.
        await _db.SaveChangesAsync(cancellationToken);

        return ServiceResult<bool>.Ok(true);
    }

    private Task<bool> RouteExistsAsync(Guid routeId, CancellationToken cancellationToken)
        => _db.Routes.AnyAsync(r => r.Id == routeId, cancellationToken);

    /// <summary>
    /// Truy vấn trạm của tuyến theo đúng thứ tự chạy, kèm thực thể trạm để trả về đủ
    /// tên/địa chỉ/toạ độ.
    ///
    /// Luôn xếp kèm khoá phụ theo Id: RouteStops KHÔNG có unique index trên (RouteId, StopOrder)
    /// — hai request POST chạy song song có thể cùng đọc max(StopOrder) rồi cùng ghi max+1 — nên
    /// hai trạm cùng thứ tự là chuyện có thật. Thiếu khoá phụ thì thứ tự hiển thị đổi giữa các
    /// lần gọi. Cùng chiêu RouteService đã dùng cho phân trang.
    /// </summary>
    private IQueryable<RouteStop> OrderedByRouteQuery(Guid routeId)
        => _db.RouteStops
            .Include(rs => rs.Stop)
            .Where(rs => rs.RouteId == routeId)
            .OrderBy(rs => rs.StopOrder)
            .ThenBy(rs => rs.Id);

    /// <summary>
    /// Thứ tự kế tiếp của tuyến: cuối danh sách hiện tại + 1, trạm đầu tiên là 1.
    ///
    /// Ép kiểu <c>(int?)</c> trước khi lấy Max là bắt buộc: MaxAsync trên dãy RỖNG ném
    /// InvalidOperationException ("Sequence contains no elements") — mà tuyến chưa gán trạm
    /// nào chính là ca đầu tiên ai cũng gặp.
    /// </summary>
    private async Task<int> NextStopOrderAsync(Guid routeId, CancellationToken cancellationToken)
        => (await _db.RouteStops
                .Where(rs => rs.RouteId == routeId)
                .MaxAsync(rs => (int?)rs.StopOrder, cancellationToken) ?? 0) + 1;

    /// <summary>Trả về thông báo lỗi, hoặc null nếu khoảng cách hợp lệ.</summary>
    private static string? ValidateDistance(decimal distanceKm) => distanceKm switch
    {
        < 0 => DistanceNegativeMessage,
        > MaxDistanceKm => DistanceTooLargeMessage,
        _ => null,
    };

    /// <summary>
    /// Lỗi gắn vào một trường của body — đúng cấu trúc { message, errors } của dự án
    /// (docs/01-kien-truc.md) để frontend gắn thẳng vào ô nhập tương ứng.
    /// </summary>
    private static IReadOnlyDictionary<string, string[]> FieldErrors(string field, string message)
        => new Dictionary<string, string[]> { [field] = [message] };

    /// <summary>Lỗi của cả mảng trạm trong PUT /stops/order — gắn vào trường "items".</summary>
    private static ServiceResult<IReadOnlyList<RouteStopResponse>> InvalidItems(string message)
        => ServiceResult<IReadOnlyList<RouteStopResponse>>.Invalid(
            message, FieldErrors("items", message));

    private static RouteStopResponse ToResponse(RouteStop routeStop) => new()
    {
        Id = routeStop.Id,
        RouteId = routeStop.RouteId,
        StopId = routeStop.StopId,

        // Trạm bị xoá cứng trong khi dòng nối còn lại là chuyện không xảy ra được (FK Restrict
        // chặn — quy ước A5), nhưng vẫn để nhánh dự phòng thay vì dùng ! : một dòng trắng còn
        // hơn một lỗi 500 khó lần ra.
        StopName = routeStop.Stop?.Name ?? string.Empty,
        StopAddress = routeStop.Stop?.Address ?? string.Empty,
        Latitude = routeStop.Stop?.Latitude ?? 0,
        Longitude = routeStop.Stop?.Longitude ?? 0,

        StopOrder = routeStop.StopOrder,
        DistanceKm = routeStop.DistanceKm,
    };
}
