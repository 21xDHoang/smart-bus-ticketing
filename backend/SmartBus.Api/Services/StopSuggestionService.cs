using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using SmartBus.Api.Data;
using SmartBus.Api.Dtos.Stops;
using SmartBus.Api.Entities;
using Route = SmartBus.Api.Entities.Route;

namespace SmartBus.Api.Services;

/// <summary>
/// Cài đặt <see cref="IStopSuggestionService"/> — hợp đồng đầy đủ ở mục
/// "Gợi ý trạm cho tra cứu tuyến — /stops/suggestions" của docs/api-contract.md.
///
/// <para>
/// <b>Vì sao phải tách khỏi <see cref="StopSearchService"/>.</b> Endpoint kia trả MỌI trạm trong
/// CSDL — đúng yêu cầu của ô chọn trạm dùng chung (vé tháng, quản lý tuyến). Ô tra cứu tuyến thì
/// chỉ được gợi ý trạm có thật trên một tuyến <c>Active</c>: trạm còn lại trong bảng là di tích
/// của tuyến đã xoá, chọn vào chắc chắn không ra tuyến nào. Lọc cứng trong endpoint cũ sẽ làm
/// hỏng các màn khác, nên tách bề mặt.
/// </para>
///
/// <para>
/// <b>Vì sao lọc trong bộ nhớ.</b> Cùng lý do đã ghi ở <see cref="StopSearchService"/>: khớp
/// không dấu cần <c>unaccent</c>, mà extension + cột chuẩn hoá là việc migration của chủ CSDL
/// (quy ước A7), và hàm riêng của Npgsql sẽ làm đổ bộ test chạy trên InMemory. Bảng trạm và bảng
/// nối của một thành phố chỉ vài trăm dòng, endpoint gọi theo từng nhịp gõ — đọc trọn rồi lọc
/// trong bộ nhớ là cái giá chấp nhận được, đổi lại có MỘT định nghĩa "bỏ dấu" chạy giống nhau ở
/// Postgres lẫn test.
/// </para>
/// </summary>
public class StopSuggestionService : IStopSuggestionService
{
    // Hạng khớp — cùng thang với StopSearchService để hai ô gợi ý xếp thứ tự giống nhau.
    private const int RankNamePrefix = 0;
    private const int RankNameContains = 1;
    private const int RankAddressContains = 2;

    /// <summary>
    /// So tên theo chuẩn <c>vi</c> — cùng lý do và cùng cách dựng với <see cref="StopSearchService"/>.
    /// </summary>
    private static readonly StringComparer NameComparer =
        StringComparer.Create(CultureInfo.GetCultureInfo("vi"), ignoreCase: true);

    private readonly AppDbContext _db;

    public StopSuggestionService(AppDbContext db) => _db = db;

    public async Task<ServiceResult<IReadOnlyList<StopSuggestionResponse>>> SearchAsync(
        StopSuggestionRequest request,
        CancellationToken cancellationToken = default)
    {
        // [Range] ở StopSuggestionRequest đã chặn limit ngoài 1..50; null = "không gửi".
        var limit = request.Limit ?? StopSuggestionRequest.DefaultLimit;
        var keyword = Normalize(request.Keyword);

        var activeRoutes = await _db.Routes
            .AsNoTracking()
            .Where(r => r.Status == RouteStatus.Active)
            .ToListAsync(cancellationToken);

        // Không có tuyến đang khai thác thì không có trạm nào hợp lệ để gợi ý — trả rỗng, không
        // phải lỗi: hệ thống vẫn chạy, chỉ là chưa có tuyến nào mở bán.
        if (activeRoutes.Count == 0)
        {
            return ServiceResult<IReadOnlyList<StopSuggestionResponse>>.Ok([]);
        }

        var activeRouteIds = activeRoutes.Select(r => r.Id).ToList();

        var routeStops = await _db.RouteStops
            .AsNoTracking()
            .Where(rs => activeRouteIds.Contains(rs.RouteId))
            .ToListAsync(cancellationToken);

        var routeStopsByRoute = routeStops
            .GroupBy(rs => rs.RouteId)
            .ToDictionary(g => g.Key, g => g.ToList());
        var routeById = activeRoutes.ToDictionary(r => r.Id);

        // stopId → các tuyến được phép hiện badge. Chỉ chứa trạm thoả ngữ cảnh lọc, nên vừa là
        // tập ứng viên vừa là dữ liệu badge — không phải lọc hai lần.
        var routeIdsByStop = new Dictionary<Guid, HashSet<Guid>>();

        void Allow(Guid stopId, Guid routeId)
        {
            if (!routeIdsByStop.TryGetValue(stopId, out var routeIds))
            {
                routeIds = [];
                routeIdsByStop[stopId] = routeIds;
            }

            routeIds.Add(routeId);
        }

        var originStopId = request.OriginStopId;
        var destinationStopId = request.DestinationStopId;

        foreach (var route in activeRoutes)
        {
            if (!routeStopsByRoute.TryGetValue(route.Id, out var rows))
            {
                continue;
            }

            var originRow = originStopId is { } oid
                ? rows.FirstOrDefault(rs => rs.StopId == oid)
                : null;

            var destinationRow = destinationStopId is { } did
                ? rows.FirstOrDefault(rs => rs.StopId == did)
                : null;

            // Trạm đã chọn không nằm trên tuyến này → tuyến không đóng góp gì.
            if (originStopId is not null && originRow is null)
            {
                continue;
            }

            if (destinationStopId is not null && destinationRow is null)
            {
                continue;
            }

            // Hai đầu trùng nhau hoặc ngược chiều: không có trạm nào "ở giữa" hợp lệ.
            if (originRow is not null
                && destinationRow is not null
                && originRow.StopOrder >= destinationRow.StopOrder)
            {
                continue;
            }

            var lowerBound = originRow?.StopOrder;
            var upperBound = destinationRow?.StopOrder;

            foreach (var row in rows)
            {
                if (lowerBound is { } lower && row.StopOrder <= lower)
                {
                    continue;
                }

                if (upperBound is { } upper && row.StopOrder >= upper)
                {
                    continue;
                }

                Allow(row.StopId, route.Id);
            }
        }

        if (routeIdsByStop.Count == 0)
        {
            return ServiceResult<IReadOnlyList<StopSuggestionResponse>>.Ok([]);
        }

        var candidateStopIds = routeIdsByStop.Keys.ToList();

        var stops = await _db.Stops
            .AsNoTracking()
            .Where(s => candidateStopIds.Contains(s.Id))
            .ToListAsync(cancellationToken);

        // Từ khoá rỗng = chế độ duyệt danh sách, cùng lối /stops/search.
        if (keyword.Length == 0)
        {
            return ServiceResult<IReadOnlyList<StopSuggestionResponse>>.Ok(
                stops
                    .OrderBy(s => s.Name, NameComparer)
                    .ThenBy(s => s.Id)
                    .Take(limit)
                    .Select(stop => ToResponse(stop, routeIdsByStop[stop.Id], routeById))
                    .ToList());
        }

        var scored = new List<(Stop Stop, int Rank)>(stops.Count);

        foreach (var stop in stops)
        {
            var rank = RankOf(stop, keyword);

            if (rank is not null)
            {
                scored.Add((stop, rank.Value));
            }
        }

        // List.Sort không ổn định — chốt khoá cuối bằng Id để hai lần gọi ra cùng thứ tự.
        scored.Sort((a, b) =>
        {
            var byRank = a.Rank.CompareTo(b.Rank);
            if (byRank != 0)
            {
                return byRank;
            }

            var byName = NameComparer.Compare(a.Stop.Name, b.Stop.Name);

            return byName != 0 ? byName : a.Stop.Id.CompareTo(b.Stop.Id);
        });

        return ServiceResult<IReadOnlyList<StopSuggestionResponse>>.Ok(
            scored
                .Take(limit)
                .Select(entry => ToResponse(
                    entry.Stop,
                    routeIdsByStop[entry.Stop.Id],
                    routeById))
                .ToList());
    }

    private static StopSuggestionResponse ToResponse(
        Stop stop,
        IReadOnlySet<Guid> routeIds,
        IReadOnlyDictionary<Guid, Route> routeById) => new()
    {
        Id = stop.Id,
        Name = stop.Name,
        Address = stop.Address,
        Latitude = stop.Latitude,
        Longitude = stop.Longitude,

        // routeIds chỉ chứa tuyến đang active và đã thoả ngữ cảnh lọc, nên không cần lọc lại.
        // Sắp theo mã để badge hiện ổn định; trùng mã xếp theo Id.
        Routes = routeIds
            .Select(routeId => routeById[routeId])
            .OrderBy(route => route.Code)
            .ThenBy(route => route.Id)
            .Select(route => new StopSuggestionRoute
            {
                RouteId = route.Id,
                RouteCode = route.Code,
                RouteName = route.Name,
            })
            .ToList(),
    };

    /// <summary>
    /// Hạng khớp của một trạm với từ khoá <b>đã chuẩn hoá</b>, hoặc <c>null</c> nếu không khớp.
    /// Cùng thang với <see cref="StopSearchService"/>: tên bắt đầu bằng → tên chứa → chỉ địa chỉ.
    /// </summary>
    private static int? RankOf(Stop stop, string needle)
    {
        var name = Normalize(stop.Name);

        if (name.StartsWith(needle, StringComparison.Ordinal))
        {
            return RankNamePrefix;
        }

        if (name.Contains(needle, StringComparison.Ordinal))
        {
            return RankNameContains;
        }

        return Normalize(stop.Address).Contains(needle, StringComparison.Ordinal)
            ? RankAddressContains
            : null;
    }

    /// <summary>
    /// Chuẩn hoá để so khớp: viết thường, bỏ dấu, gộp khoảng trắng, cắt hai đầu.
    ///
    /// Bản sao có chủ đích của <c>StopSearchService.Normalize</c> (private — và luật nhóm không
    /// cho sửa file của người khác để mở ra). Bản này phải cho ra đúng kết quả của
    /// <c>normalizeForSearch</c> phía frontend; sửa một bên thì sửa cả ba.
    ///
    /// <c>đ</c> (U+0111) là một CHỮ CÁI riêng, NFD không tách được — phải thay tay, nếu không
    /// "dong da" không ra "Trạm Đống Đa".
    /// </summary>
    private static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var decomposed = text.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);

        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            builder.Append(character == 'đ' ? 'd' : character);
        }

        return string.Join(
            ' ',
            builder.ToString().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
}
