using System.Globalization;
using SmartBus.Api.Dtos.Reports;

namespace SmartBus.Api.Services;

/// <summary>
/// Lớp bọc đệm cho <see cref="IRevenueReportService"/> của GET /api/reports/revenue — task
/// *"Cache kết quả báo cáo theo kỳ để tăng tốc"* (US 19 "Doanh thu", Sprint 5 dòng 43 — Vàng Thị
/// Dăm).
///
/// Vì sao là lớp bọc chứ không sửa thẳng <see cref="RevenueReportService"/>: service, controller
/// và DTO của endpoint này là file của Trần Trung Hiếu (quy ước E1 — không sửa file của người
/// khác). Lớp bọc đứng trước nên service thật không phải biết gì về đệm, và hình dạng response
/// không đổi. <c>RevenueComparisonService</c> cũng gọi qua <see cref="IRevenueReportService"/>,
/// nên phần so sánh giữa các kỳ được hưởng đệm mà không phải sửa một dòng.
///
/// Đường đi: trúng đệm → trả luôn, KHÔNG chạm CSDL (thay vì hai truy vấn gộp + một truy vấn dòng
/// thô); trượt → hỏi service thật rồi lưu lại nếu thành công. Mọi nhánh lỗi (400 granularity /
/// khoảng ngày sai, 404 tuyến không tồn tại) đi thẳng từ service thật lên Controller — không bị
/// đệm, không bị đổi một chữ.
/// </summary>
public sealed class CachedRevenueReportService : IRevenueReportService
{
    /// <summary>
    /// Giờ Việt Nam cố định, dùng khi dựng khoá cho cửa sổ mặc định. Cùng con số với
    /// <see cref="RevenueReportService"/> (UTC+7, không DST từ 1975); không rút thành hằng dùng
    /// chung được vì hằng đó là private trong file của Hiếu.
    /// </summary>
    private const int VietnamUtcOffsetHours = 7;

    private readonly RevenueReportService _inner;

    private readonly IRevenueReportCache _cache;

    public CachedRevenueReportService(RevenueReportService inner, IRevenueReportCache cache)
    {
        _inner = inner;
        _cache = cache;
    }

    public async Task<ServiceResult<RevenueReportResponse>> GetAsync(
        RevenueReportRequest request,
        CancellationToken cancellationToken)
    {
        // Controller luôn bind ra request khác null; nhánh này chỉ để lớp bọc không dựng khoá từ
        // dữ liệu thiếu — hành vi giữ nguyên như khi chưa có đệm (service thật ném).
        if (request is null)
        {
            return await _inner.GetAsync(request!, cancellationToken);
        }

        // Granularity lạ là lỗi 400: không đệm, để service thật trả đúng cấu trúc errors, đồng
        // thời không chiếm chỗ bằng một khoá không bao giờ thành công.
        if (!RevenueGranularityCodes.TryParse(request.Granularity, out _))
        {
            return await _inner.GetAsync(request, cancellationToken);
        }

        var key = BuildKey(request);

        if (_cache.Lookup(key) is { } cached)
        {
            return ServiceResult<RevenueReportResponse>.Ok(cached);
        }

        var result = await _inner.GetAsync(request, cancellationToken);

        // Chỉ đệm kết quả THÀNH CÔNG. Báo cáo rỗng vẫn là kết quả thành công → có đệm (một kỳ
        // chưa phát sinh giao dịch là truy vấn thật, dashboard hỏi lặp lại nhiều lần).
        if (result.Success && result.Data is { } data)
        {
            _cache.Store(key, data);
        }

        return result;
    }

    /// <summary>
    /// Khoá đệm của một báo cáo: độ mịn + khoảng ngày + tuyến. Chuẩn hoá đúng những gì service
    /// thật coi là cùng một truy vấn:
    /// <list type="bullet">
    /// <item>granularity bỏ trống/whitespace = <c>day</c>, hoa/thường không phân biệt;</item>
    /// <item>from/to là ngày lịch Việt Nam, giữ nguyên dạng <c>yyyy-MM-dd</c>;</item>
    /// <item>routeId bỏ trống = toàn công ty, dạng <c>N</c> để không phụ thuộc dấu gạch.</item>
    /// </list>
    /// Khi from hoặc to bỏ trống, service thật lấy mốc "hôm nay (giờ Việt Nam)" — đưa ngày hôm nay
    /// vào khoá để qua nửa đêm không trả lại báo cáo của cửa sổ cũ trong lúc entry còn hạn.
    /// </summary>
    private static string BuildKey(RevenueReportRequest request)
    {
        var granularity = request.Granularity?.Trim().ToLowerInvariant();

        if (string.IsNullOrEmpty(granularity))
        {
            granularity = "day";
        }

        var from = request.From?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "-";
        var to = request.To?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "-";
        var route = request.RouteId?.ToString("N") ?? "-";

        var today = request.From is null || request.To is null
            ? DateOnly.FromDateTime(DateTime.UtcNow.AddHours(VietnamUtcOffsetHours))
                .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : "-";

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{granularity}|{from}|{to}|{route}|{today}");
    }
}
