using System.Globalization;
using SmartBus.Api.Dtos.Trips;

namespace SmartBus.Api.Services;

/// <summary>
/// Lớp bọc đệm cho <see cref="ITripSearchService"/> của GET /api/trips/search — task *"Cache kết quả
/// tìm kiếm tuyến phổ biến để giảm tải DB"* (Nguyễn Duy Kiên).
///
/// Vì sao là lớp bọc chứ không sửa thẳng <see cref="TripSearchService"/>: service, controller và DTO
/// của endpoint này là file của Phùng Duy Hoàng (quy ước E1 — nhóm không sửa file của người khác).
/// Lớp bọc đứng trước nên service thật không phải biết gì về đệm, và hình dạng response không đổi.
///
/// Đường đi: trúng đệm → trả luôn, KHÔNG chạm CSDL (thay vì ba truy vấn tuyến + giá + chuyến ⋈ xe);
/// trượt → hỏi service thật rồi lưu lại nếu thành công. Mọi nhánh lỗi (400 khoảng thời gian sai, 404
/// tuyến không tồn tại) đi thẳng từ service thật lên Controller — không bị đệm, không bị đổi một chữ.
/// </summary>
public class CachedTripSearchService : ITripSearchService
{
    private readonly TripSearchService _inner;

    private readonly ITripSearchResultCache _cache;

    public CachedTripSearchService(TripSearchService inner, ITripSearchResultCache cache)
    {
        _inner = inner;
        _cache = cache;
    }

    public async Task<ServiceResult<IReadOnlyList<TripSearchResult>>> SearchAsync(
        SearchTripsRequest request,
        CancellationToken cancellationToken = default)
    {
        // [Required] ở SearchTripsRequest đã chặn thiếu routeId ở Controller trước khi vào đây;
        // nhánh này chỉ để lớp bọc không dựng khoá từ dữ liệu thiếu — hành vi giữ nguyên như khi
        // chưa có đệm.
        if (request?.RouteId is null)
        {
            return await _inner.SearchAsync(request!, cancellationToken);
        }

        var key = BuildKey(request);

        if (_cache.Lookup(key) is { } cached)
        {
            return ServiceResult<IReadOnlyList<TripSearchResult>>.Ok(cached);
        }

        var result = await _inner.SearchAsync(request, cancellationToken);

        // Chỉ đệm kết quả thành công: tuyến vừa được tạo hay khoảng thời gian vừa sửa phải tra được
        // ngay ở lượt kế tiếp. Danh sách RỖNG vẫn là kết quả thành công → có đệm (một tuyến phổ biến
        // chưa có chuyến nào là truy vấn thật, hỏi lặp lại nhiều lần).
        if (result.Success && result.Data is { } data)
        {
            _cache.Store(key, data);
        }

        return result;
    }

    /// <summary>
    /// Khoá đệm của một truy vấn: tuyến + khoảng thời gian, quy hết về UTC — đúng cặp giá trị mà
    /// <see cref="TripSearchService"/> dùng để truy vấn (<c>request.From?.UtcDateTime</c>), nên hai
    /// lời gọi cùng một khoảnh khắc nhưng viết bằng hai múi giờ khác nhau vẫn trúng cùng một khoá.
    /// Invariant culture vì khoá chỉ để so bằng nhau, không phải để đọc. Không còn tham số nào khác
    /// trong <see cref="SearchTripsRequest"/>.
    /// </summary>
    private static string BuildKey(SearchTripsRequest request)
        => string.Create(
            CultureInfo.InvariantCulture,
            $"{request.RouteId!.Value:N}|{request.From?.UtcDateTime.Ticks}|{request.To?.UtcDateTime.Ticks}");
}
