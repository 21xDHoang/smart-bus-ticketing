using Microsoft.EntityFrameworkCore;
using SmartBus.Api.Data;
using SmartBus.Api.Dtos.Fares;
using SmartBus.Api.Entities;

namespace SmartBus.Api.Services;

/// <summary>
/// Cài đặt <see cref="IFareService"/>. Story 12 — Phùng Duy Hoàng.
/// </summary>
public class FareService : IFareService
{
    private const string RouteNotFoundMessage = "Không tìm thấy tuyến đường";
    private const string FareNotFoundMessage = "Không tìm thấy giá vé";
    private const string PassengerTypeInvalidMessage = "Đối tượng hành khách không hợp lệ";
    private const string PriceNotPositiveMessage = "Giá vé phải lớn hơn 0";
    private const string PriceTooLargeMessage = "Giá vé tối đa 9 999 999 999.99";
    private const string DuplicateFareMessage = "Tuyến này đã có giá vé cho đối tượng đó";

    /// <summary>Trần của cột numeric(12,2): 12 chữ số tổng, trong đó 2 chữ số thập phân.</summary>
    private static readonly decimal MaxPrice = 9999999999.99m;

    private readonly AppDbContext _db;

    public FareService(AppDbContext db) => _db = db;

    public async Task<ServiceResult<IReadOnlyList<FareResponse>>> ListByRouteAsync(
        Guid routeId,
        CancellationToken cancellationToken = default)
    {
        if (!await RouteExistsAsync(routeId, cancellationToken))
        {
            return ServiceResult<IReadOnlyList<FareResponse>>.NotFound(RouteNotFoundMessage);
        }

        var fares = await _db.Fares
            .AsNoTracking()
            .Where(f => f.RouteId == routeId)
            .ToListAsync(cancellationToken);

        // Sắp xếp ở phía C# chứ không OrderBy trong câu LINQ: cột PassengerType lưu dạng chuỗi
        // (quy ước A3) nên PostgreSQL sẽ xếp theo alphabet — Child, Disabled, Senior, Standard,
        // Student — chứ không theo thứ tự khai báo trong enum. Để nguyên thì bảng giá trên
        // giao diện nhảy lung tung theo bảng chữ cái. Mỗi tuyến chỉ có tối đa 5 dòng nên
        // sắp tại chỗ không tốn kém.
        var ordered = fares
            .OrderBy(f => f.PassengerType)
            .ThenBy(f => f.Id)
            .Select(ToResponse)
            .ToList();

        return ServiceResult<IReadOnlyList<FareResponse>>.Ok(ordered);
    }

    public async Task<ServiceResult<FareResponse>> GetByIdAsync(
        Guid routeId,
        Guid id,
        CancellationToken cancellationToken = default)
    {
        if (!await RouteExistsAsync(routeId, cancellationToken))
        {
            return ServiceResult<FareResponse>.NotFound(RouteNotFoundMessage);
        }

        var fare = await _db.Fares
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == id && f.RouteId == routeId, cancellationToken);

        return fare is null
            ? ServiceResult<FareResponse>.NotFound(FareNotFoundMessage)
            : ServiceResult<FareResponse>.Ok(ToResponse(fare));
    }

    public async Task<ServiceResult<FareResponse>> CreateAsync(
        Guid routeId,
        CreateFareRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!await RouteExistsAsync(routeId, cancellationToken))
        {
            return ServiceResult<FareResponse>.NotFound(RouteNotFoundMessage);
        }

        if (!TryParsePassengerType(request.PassengerType, out var passengerType))
        {
            return InvalidField("passengerType", PassengerTypeMessage());
        }

        if (ValidatePrice(request.Price) is { } priceError)
        {
            return InvalidField("price", priceError);
        }

        if (await _db.Fares.AnyAsync(
                f => f.RouteId == routeId && f.PassengerType == passengerType,
                cancellationToken))
        {
            return ServiceResult<FareResponse>.Conflict(DuplicateFareMessage);
        }

        var fare = new Fare
        {
            RouteId = routeId,
            PassengerType = passengerType,
            Price = request.Price,
        };

        _db.Fares.Add(fare);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Hai request cùng đối tượng chạy song song đều lọt qua bước kiểm tra ở trên.
            // Ràng buộc unique (RouteId, PassengerType) trong CSDL là lớp chặn cuối cùng —
            // bắt ở đây để người dùng nhận 409 kèm thông báo hiểu được, thay vì lỗi 500.
            return ServiceResult<FareResponse>.Conflict(DuplicateFareMessage);
        }

        return ServiceResult<FareResponse>.Ok(ToResponse(fare));
    }

    public async Task<ServiceResult<FareResponse>> UpdateAsync(
        Guid routeId,
        Guid id,
        UpdateFareRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!await RouteExistsAsync(routeId, cancellationToken))
        {
            return ServiceResult<FareResponse>.NotFound(RouteNotFoundMessage);
        }

        var fare = await _db.Fares
            .FirstOrDefaultAsync(f => f.Id == id && f.RouteId == routeId, cancellationToken);

        if (fare is null)
        {
            return ServiceResult<FareResponse>.NotFound(FareNotFoundMessage);
        }

        if (ValidatePrice(request.Price) is { } priceError)
        {
            return InvalidField("price", priceError);
        }

        fare.Price = request.Price;
        fare.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);

        return ServiceResult<FareResponse>.Ok(ToResponse(fare));
    }

    public async Task<ServiceResult<bool>> DeleteAsync(
        Guid routeId,
        Guid id,
        CancellationToken cancellationToken = default)
    {
        if (!await RouteExistsAsync(routeId, cancellationToken))
        {
            return ServiceResult<bool>.NotFound(RouteNotFoundMessage);
        }

        var fare = await _db.Fares
            .FirstOrDefaultAsync(f => f.Id == id && f.RouteId == routeId, cancellationToken);

        if (fare is null)
        {
            return ServiceResult<bool>.NotFound(FareNotFoundMessage);
        }

        _db.Fares.Remove(fare);
        await _db.SaveChangesAsync(cancellationToken);

        return ServiceResult<bool>.Ok(true);
    }

    /// <summary>
    /// Tuyến có thật hay không. Kiểm tra bằng truy vấn riêng thay vì dựa vào khoá ngoại:
    /// khoá ngoại chỉ chặn được lúc ghi, còn GET trên tuyến không tồn tại vẫn phải là 404.
    /// </summary>
    private Task<bool> RouteExistsAsync(Guid routeId, CancellationToken cancellationToken)
        => _db.Routes.AnyAsync(r => r.Id == routeId, cancellationToken);

    /// <summary>
    /// So khớp với danh sách tên của enum thay vì dùng <c>Enum.TryParse</c>.
    ///
    /// TryParse chấp nhận cả chuỗi số — "3" sẽ ra Child — nên client gửi số vẫn lọt qua,
    /// trong khi quy ước A3 nói rõ trạng thái phải là chuỗi đọc được. So tên trực tiếp vừa
    /// chặn được lỗi đó vừa không cần <c>Enum.IsDefined</c> kiểm tra lại.
    /// Bỏ qua hoa/thường để "student" hay "STUDENT" đều nhận, nhưng giá trị lưu xuống CSDL
    /// luôn là tên chuẩn của enum, nên trong bảng không bao giờ có hai kiểu viết của cùng một đối tượng.
    /// </summary>
    private static bool TryParsePassengerType(string? value, out PassengerType passengerType)
    {
        var trimmed = value?.Trim();

        foreach (var name in Enum.GetNames<PassengerType>())
        {
            if (string.Equals(name, trimmed, StringComparison.OrdinalIgnoreCase))
            {
                passengerType = Enum.Parse<PassengerType>(name);
                return true;
            }
        }

        passengerType = default;
        return false;
    }

    /// <summary>
    /// Kiểm tra biên giá ở tầng nghiệp vụ chứ không gắn [Range] ở DTO: biên trên của tiền phải
    /// là decimal chính xác, mà [Range] chỉ nhận biên dưới dạng chuỗi. Khai báo một lần bằng
    /// decimal ở đây, và thông báo lỗi cũng đi qua đúng cấu trúc { message, errors } của dự án.
    /// Trả về null nếu giá hợp lệ, ngược lại là câu thông báo lỗi.
    /// </summary>
    private static string? ValidatePrice(decimal price)
        => price <= 0m ? PriceNotPositiveMessage
        : price > MaxPrice ? PriceTooLargeMessage
        : null;

    private static ServiceResult<FareResponse> InvalidField(string field, string message)
        => ServiceResult<FareResponse>.Invalid(
            message,
            new Dictionary<string, string[]> { [field] = [message] });

    /// <summary>
    /// Danh sách mã hợp lệ lấy từ chính enum, không chép tay vào câu chữ — thêm hay bớt một
    /// đối tượng ưu đãi thì thông báo lỗi tự đúng theo, không có chỗ nào để quên sửa.
    /// </summary>
    private static string PassengerTypeMessage()
        => $"{PassengerTypeInvalidMessage}. Chấp nhận: {string.Join(", ", Enum.GetNames<PassengerType>())}";

    private static FareResponse ToResponse(Fare fare) => new()
    {
        Id = fare.Id,
        RouteId = fare.RouteId,
        // Tên chuỗi của enum — đúng giá trị đang nằm trong cột PassengerType
        // (HasConversion<string> ở AppDbContext.Route.cs).
        PassengerType = fare.PassengerType.ToString(),
        Price = fare.Price,
        CreatedAt = fare.CreatedAt,
        UpdatedAt = fare.UpdatedAt,
    };
}
