using Microsoft.EntityFrameworkCore;
using SmartBus.Api.Data;
using SmartBus.Api.Dtos.Feedbacks;
using SmartBus.Api.Entities;

namespace SmartBus.Api.Services;

/// <summary>
/// Cài đặt <see cref="IFeedbackStatisticsService"/> — hợp đồng đầy đủ ở mục "Phản ánh — /feedbacks",
/// phần "Thống kê phản ánh" của docs/api-contract.md.
///
/// Ba quyết định đáng đọc trước khi sửa:
///
/// 1. <b>Gộp nhóm ở tầng CSDL, không kéo dữ liệu về rồi đếm trong bộ nhớ.</b> Đây là endpoint thống
///    kê — kéo mọi dòng phản ánh về để đếm là đúng thứ nó tồn tại để tránh: chi phí tăng theo số phản
///    ánh, trong khi kết quả chỉ có vài dòng.
/// 2. <b>Bảng theo loại luôn đủ ba dòng</b>, kể cả loại chưa có phản ánh nào — dựng từ chính khai báo
///    enum rồi ghép con đếm vào, thay vì trả về đúng những loại có trong dữ liệu. Hợp đồng nói rõ lý
///    do: biểu đồ theo loại có trục cố định ba giá trị.
/// 3. <b>Phản ánh không gắn chuyến đếm riêng, không nhập vào bảng theo tuyến.</b> Chúng không quy được
///    về tuyến nào (A9 #20), nên nếu bỏ đi thì hai bảng đếm cộng lại không bằng tổng, còn nếu cho
///    thành một dòng <c>routeId: null</c> thì màn hình phải tự đoán cách vẽ.
///
/// Bốn truy vấn, mỗi cái một phép gộp đơn giản và đều có chỉ mục để tựa vào
/// (<c>Feedbacks (TripId)</c>, và phép đếm toàn bộ thì không lọc gì). Cố ý không gộp lại thành một
/// truy vấn cho "gọn": đây không phải đường nóng, mà gộp lại thì phải đọc thêm một đống chi tiết mới
/// biết con số nào là con số nào.
///
/// Bảng <c>Feedbacks</c> đã có migration (Vàng Thị Dăm) nên service này chạy được trên CSDL thật.
/// </summary>
public class FeedbackStatisticsService : IFeedbackStatisticsService
{
    private readonly AppDbContext _db;

    public FeedbackStatisticsService(AppDbContext db) => _db = db;

    public async Task<FeedbackStatisticsResponse> GetAsync(CancellationToken cancellationToken = default)
    {
        var total = await _db.Feedbacks
            .AsNoTracking()
            .CountAsync(cancellationToken);

        var byType = await CountByTypeAsync(cancellationToken);
        var byRoute = await CountByRouteAsync(cancellationToken);

        // Đếm thẳng theo tripId null thay vì lấy total trừ đi phần đã quy được về tuyến: phép trừ cho
        // ra "mọi thứ không quy được về tuyến", một tập RỘNG hơn — dữ liệu có chuyến trỏ hụt (FK
        // Restrict nên không xảy ra, nhưng nếu xảy ra) sẽ lặng lẽ chui vào đây và không ai biết. Đếm
        // thẳng thì đẳng thức sum(byRoute) + withoutTrip == total trở thành một phép kiểm thật.
        var withoutTrip = await _db.Feedbacks
            .AsNoTracking()
            .CountAsync(f => f.TripId == null, cancellationToken);

        return new FeedbackStatisticsResponse
        {
            Total = total,
            ByType = byType,
            ByRoute = byRoute,
            WithoutTrip = withoutTrip,
        };
    }

    /// <summary>
    /// Đếm theo loại, luôn đủ ba dòng theo thứ tự khai báo của <see cref="FeedbackType"/>
    /// (Complaint → Compliment → Suggestion, đúng thứ tự hợp đồng chốt).
    ///
    /// Thứ tự lấy từ <c>Enum.GetValues</c> chứ không sắp xếp lại: hợp đồng chốt thứ tự này, và nó
    /// trùng thứ tự khai báo — đổi thứ tự khai báo enum là đổi thứ tự response, nên đừng đổi.
    /// </summary>
    private async Task<IReadOnlyList<FeedbackTypeCountResponse>> CountByTypeAsync(
        CancellationToken cancellationToken)
    {
        var demTheoLoai = await _db.Feedbacks
            .AsNoTracking()
            .GroupBy(f => f.Type)
            .Select(g => new { Type = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        return Enum.GetValues<FeedbackType>()
            .Select(type => new FeedbackTypeCountResponse
            {
                // Tên chuỗi của enum — đúng giá trị đang nằm trong cột (HasConversion<string>).
                Type = type.ToString(),
                // Loại chưa có phản ánh nào không xuất hiện trong kết quả gộp nhóm ⇒ 0.
                Count = demTheoLoai.FirstOrDefault(row => row.Type == type)?.Count ?? 0,
            })
            .ToList();
    }

    /// <summary>
    /// Đếm theo tuyến của chuyến bị phản ánh. Chiếu sang (RouteId, Code, Name) rồi mới gộp nhóm — gộp
    /// thẳng theo navigation trong khoá nhóm khó dịch sang SQL hơn và chẳng được gì.
    ///
    /// Chỉ phản ánh CÓ chuyến mới vào đây (<c>f.Trip != null</c>); phần không gắn chuyến do
    /// <see cref="GetAsync"/> đếm riêng.
    /// </summary>
    private async Task<IReadOnlyList<FeedbackRouteCountResponse>> CountByRouteAsync(
        CancellationToken cancellationToken)
    {
        var demTheoChuyen = await _db.Feedbacks
            .AsNoTracking()
            .Where(f => f.Trip != null)
            .Select(f => new
            {
                RouteId = f.Trip!.RouteId,
                Code = f.Trip!.Route!.Code,
                Name = f.Trip!.Route!.Name,
            })
            .GroupBy(row => new { row.RouteId, row.Code, row.Name })
            .Select(g => new
            {
                g.Key.RouteId,
                g.Key.Code,
                g.Key.Name,
                Count = g.Count(),
            })
            .ToListAsync(cancellationToken);

        return demTheoChuyen
            // Tuyến bị phản ánh nhiều nhất lên đầu — đó chính là điều bảng này để trả lời. Trùng số
            // thì theo mã tuyến tăng dần: thứ tự phải tất định, nếu không thì hai lần gọi cho hai thứ
            // tự khác nhau và không ai so sánh được gì.
            .OrderByDescending(row => row.Count)
            .ThenBy(row => row.Code, StringComparer.Ordinal)
            .Select(row => new FeedbackRouteCountResponse
            {
                RouteId = row.RouteId,
                RouteCode = row.Code,
                RouteName = row.Name,
                Count = row.Count,
            })
            .ToList();
    }
}
