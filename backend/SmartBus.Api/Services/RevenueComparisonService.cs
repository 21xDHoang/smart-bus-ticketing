using SmartBus.Api.Dtos.Reports;

namespace SmartBus.Api.Services;

/// <summary>
/// Cài đặt <see cref="IRevenueComparisonService"/> — so sánh doanh thu kỳ hiện tại với kỳ liền
/// trước cùng độ dài (US 19 "Doanh thu", Sprint 4, task *"API so sánh doanh thu giữa các kỳ
/// (tăng/giảm %)"* — Phùng Duy Hoàng). Hợp đồng đầy đủ ở mục "Báo cáo doanh thu — /reports/revenue"
/// của api-contract.md.
///
/// Ba chốt nghiệp vụ, đọc trước khi sửa:
///
/// 1. **Một nguồn số duy nhất.** Service gọi <see cref="IRevenueReportService.GetAsync"/> hai lần
///    thay vì tự truy vấn Payments. Nhờ vậy mọi quy tắc của báo cáo gốc (chỉ Success, PaidAt ??
///    CreatedAt, ngày theo giờ VN UTC+7, mặc định 30 ngày, trần 366 ngày, kiểm tra tuyến) áp dụng
///    y nguyên cho cả hai kỳ — không có định nghĩa "tuần/tháng" thứ hai để lệch.
/// 2. **Kỳ trước là khoảng CÙNG SỐ NGÀY kết thúc ngay trước from.** Ví dụ kỳ hiện tại 11/10 → 17/10
///    (7 ngày) thì kỳ trước là 04/10 → 10/10 (7 ngày). Cố ý không lấy "tháng trước theo lịch" vì
///    tháng 28/30/31 ngày sẽ làm hai kỳ khác độ dài — hợp đồng đã chốt "cùng độ dài".
/// 3. **% thay đổi là null khi kỳ trước bằng 0.** Không có nền để chia; trả 0% sẽ khiến màn hình
///    nói "không đổi" trong khi thực tế là từ không có doanh thu → có doanh thu. Số tuyệt đối
///    (<see cref="RevenueComparisonResponse.RevenueChange"/>) vẫn luôn có. Làm tròn 2 chữ số thập
///    phân, AwayFromZero.
/// </summary>
public sealed class RevenueComparisonService : IRevenueComparisonService
{
    private readonly IRevenueReportService _revenueReportService;

    public RevenueComparisonService(IRevenueReportService revenueReportService)
        => _revenueReportService = revenueReportService;

    public async Task<ServiceResult<RevenueComparisonResponse>> GetAsync(
        RevenueComparisonRequest request,
        CancellationToken cancellationToken)
    {
        // Kỳ hiện tại: giao nguyên tham số cho báo cáo gốc. Nó tự áp mặc định (30 ngày), kiểm tra
        // from/to + trần 366 ngày + tuyến tồn tại, rồi trả lại from/to ĐÃ CHỐT — nhờ đó kỳ trước
        // tính trên đúng khoảng người dùng thực sự nhận được, không phải khoảng đoán lại.
        var current = await _revenueReportService.GetAsync(
            new RevenueReportRequest
            {
                From = request.From,
                To = request.To,
                RouteId = request.RouteId,
            },
            cancellationToken);

        if (!current.Success)
        {
            return Propagate(current);
        }

        var currentData = current.Data!;
        var periodDays = currentData.To.DayNumber - currentData.From.DayNumber + 1;

        // Kỳ trước: cùng số ngày, kết thúc ngay trước from. Chặn tràn DateOnly.MinValue (chỉ xảy ra
        // khi from rơi vào đầu năm 1) — API phải trả 400, không được để 500 vì tràn số học lịch.
        var previousStartDay = currentData.From.DayNumber - periodDays;

        if (previousStartDay < 0)
        {
            const string message = "Không thể xác định kỳ liền trước cho khoảng ngày này.";

            return ServiceResult<RevenueComparisonResponse>.Invalid(
                message,
                new Dictionary<string, string[]> { ["from"] = [message], ["to"] = [message] });
        }

        var previousFrom = DateOnly.FromDayNumber(previousStartDay);
        var previousTo = currentData.From.AddDays(-1);

        var previous = await _revenueReportService.GetAsync(
            new RevenueReportRequest
            {
                From = previousFrom,
                To = previousTo,
                RouteId = request.RouteId,
            },
            cancellationToken);

        if (!previous.Success)
        {
            return Propagate(previous);
        }

        var previousData = previous.Data!;
        var revenueChange = currentData.TotalRevenue - previousData.TotalRevenue;
        var paymentsChange = currentData.TotalPayments - previousData.TotalPayments;

        return ServiceResult<RevenueComparisonResponse>.Ok(new RevenueComparisonResponse
        {
            From = currentData.From,
            To = currentData.To,
            PreviousFrom = previousData.From,
            PreviousTo = previousData.To,
            CurrentRevenue = currentData.TotalRevenue,
            PreviousRevenue = previousData.TotalRevenue,
            RevenueChange = revenueChange,
            RevenueChangePercent = PercentChange(revenueChange, previousData.TotalRevenue),
            CurrentPayments = currentData.TotalPayments,
            PreviousPayments = previousData.TotalPayments,
            PaymentsChange = paymentsChange,
            PaymentsChangePercent = PercentChange(paymentsChange, previousData.TotalPayments),
        });
    }

    /// <summary>
    /// % thay đổi so với nền kỳ trước, làm tròn 2 chữ số thập phân. Nền bằng 0 → null (chốt 3).
    /// </summary>
    private static decimal? PercentChange(decimal change, decimal baseline)
        => baseline == 0m
            ? null
            : Math.Round(change / baseline * 100m, 2, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Chuyển lỗi của báo cáo gốc sang kiểu kết quả của service này — controller chỉ thấy một
    /// hình dạng lỗi, không cần biết lỗi đến từ lần gọi nào. Báo cáo gốc chỉ trả Invalid/NotFound.
    /// </summary>
    private static ServiceResult<RevenueComparisonResponse> Propagate(
        ServiceResult<RevenueReportResponse> source) => source.ErrorKind switch
        {
            ServiceErrorKind.NotFound => ServiceResult<RevenueComparisonResponse>.NotFound(source.Error!),
            ServiceErrorKind.Conflict => ServiceResult<RevenueComparisonResponse>.Conflict(source.Error!),
            _ => ServiceResult<RevenueComparisonResponse>.Invalid(
                source.Error ?? "Dữ liệu đầu vào không hợp lệ",
                source.Errors ?? new Dictionary<string, string[]>()),
        };
}
