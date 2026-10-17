using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartBus.Api.Services;

namespace SmartBus.Api.Controllers;

/// <summary>
/// Thống kê số lượng hồ sơ ưu đãi theo trạng thái — GET /api/discount-requests/statistics. Task
/// *"API thống kê số lượng hồ sơ ưu đãi theo trạng thái"* (Sprint 4 dòng 55, US 17 — Vàng Thị Dăm).
/// Hợp đồng đầy đủ ở mục "Hồ sơ ưu đãi — /discount-requests" của docs/api-contract.md.
///
/// [Authorize(Policy = ManagerOrAbove)]: đây là số liệu vận hành của HR/Quản lý — có bao nhiêu hồ
/// sơ đang chờ duyệt, bao nhiêu ưu đãi còn hiệu lực — không phải dữ liệu của một hành khách. Cùng
/// quyền với hai endpoint duyệt hồ sơ (dòng 51/52) mà bảng này phục vụ.
///
/// Đứng ở controller riêng trên bề mặt <c>api/discount-requests</c>: luật nhóm không cho sửa file
/// của người khác, mà hai endpoint gửi/duyệt hồ sơ là của Trần Trung Hiếu (dòng 51) và Nguyễn Duy
/// Kiên (dòng 52) — mỗi bề mặt một controller, cùng lối <c>VoucherStatisticsController</c> đứng
/// chung bề mặt với <c>VouchersController</c>.
///
/// ⚠️ Bề mặt này nay có một đoạn literal (<c>statistics</c>) bên cạnh các route sẽ dựng sau. Vì vậy
/// route chi tiết <c>GET /api/discount-requests/{id}</c> — nếu có — PHẢI giữ ràng buộc
/// <c>{id:guid}</c>: nới thành <c>{id}</c> trần là <c>statistics</c> bị route chi tiết nuốt mất và
/// endpoint này không bao giờ chạy — cùng lối <c>export</c> của <c>GET /api/audit-logs/export</c>.
/// </summary>
[ApiController]
[Route("api/discount-requests")]
[Authorize(Policy = RbacPolicies.ManagerOrAbove)]
public class DiscountRequestStatisticsController : ControllerBase
{
    private readonly IDiscountRequestStatisticsService _discountRequestStatisticsService;

    public DiscountRequestStatisticsController(
        IDiscountRequestStatisticsService discountRequestStatisticsService)
        => _discountRequestStatisticsService = discountRequestStatisticsService;

    /// <summary>
    /// Số lượng hồ sơ ưu đãi theo từng trạng thái, kèm hai tập con còn hiệu lực / hết hạn của nhóm
    /// đã duyệt.
    ///
    /// Không có token → 401; không đủ quyền → 403. Không có nhánh lỗi nào khác: endpoint không nhận
    /// tham số, nên đúng quyền là luôn 200 — kể cả khi hệ thống chưa có hồ sơ nào (mọi con đếm bằng
    /// 0; "chưa có dữ liệu" là một câu trả lời, không phải lỗi).
    /// </summary>
    [HttpGet("statistics")]
    public async Task<IActionResult> GetStatistics(CancellationToken cancellationToken)
    {
        var thongKe = await _discountRequestStatisticsService.GetAsync(cancellationToken);

        return Ok(thongKe);
    }
}
