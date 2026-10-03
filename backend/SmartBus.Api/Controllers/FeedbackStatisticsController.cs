using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartBus.Api.Services;

namespace SmartBus.Api.Controllers;

/// <summary>
/// Thống kê phản ánh theo loại và theo tuyến — GET /api/admin/feedbacks/statistics. Task *"API thống
/// kê phản ánh theo loại và theo tuyến"* (US 24, Sprint 2, Nguyễn Duy Kiên). Hợp đồng đầy đủ ở mục
/// "Phản ánh — /feedbacks", phần "Thống kê phản ánh" của docs/api-contract.md.
///
/// [Authorize(Policy = ManagerOrAbove)]: thống kê là số liệu vận hành của nhà xe — cùng quyền với
/// FeedbackAdminController, và cùng lý do: đây là cái nhìn trên TOÀN BỘ phản ánh, không phải dữ liệu
/// của một hành khách (khác hẳn FeedbackLookupController).
///
/// Đứng ở controller riêng cùng bề mặt api/admin/feedbacks với FeedbackAdminController: controller
/// kia là file của Hoàng, mà luật nhóm không cho sửa file của người khác (E1) — mỗi bề mặt một
/// controller, cùng lối TripSearchController đứng chung bề mặt api/trips. Đoạn literal "statistics"
/// không tranh chấp với route {id:guid} của controller kia: GUID sai định dạng không khớp template
/// nên "statistics" thắng, cùng lối "export" của GET /api/audit-logs/export.
/// </summary>
[ApiController]
[Route("api/admin/feedbacks")]
[Authorize(Policy = RbacPolicies.ManagerOrAbove)]
public class FeedbackStatisticsController : ControllerBase
{
    private readonly IFeedbackStatisticsService _feedbackStatisticsService;

    public FeedbackStatisticsController(IFeedbackStatisticsService feedbackStatisticsService)
        => _feedbackStatisticsService = feedbackStatisticsService;

    /// <summary>
    /// Đếm phản ánh theo loại và theo tuyến trên toàn hệ thống.
    /// Không có token → 401; không đủ quyền → 403. Không có nhánh lỗi nào khác: endpoint không nhận
    /// tham số, nên đúng quyền là luôn 200 — kể cả khi chưa có phản ánh nào (mọi con đếm bằng 0 và
    /// byType vẫn đủ ba dòng).
    /// </summary>
    [HttpGet("statistics")]
    public async Task<IActionResult> GetStatistics(CancellationToken cancellationToken)
    {
        var thongKe = await _feedbackStatisticsService.GetAsync(cancellationToken);

        return Ok(thongKe);
    }
}
