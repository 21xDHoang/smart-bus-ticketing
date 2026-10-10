using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartBus.Api.Services;

namespace SmartBus.Api.Controllers;

/// <summary>
/// Thống kê hiệu quả voucher đã phát hành — GET /api/vouchers/statistics. Task *"API thống kê hiệu quả
/// voucher"* (dòng 54, US 18 — Quản lý Voucher, Sprint 3, Nguyễn Duy Kiên). Hợp đồng đầy đủ ở mục
/// "Voucher — /vouchers", phần "GET /vouchers/statistics" của docs/api-contract.md.
///
/// [Authorize(Policy = ManagerOrAbove)]: đây là số liệu kinh doanh của nhà xe — doanh thu đã giảm giá
/// và mã nào đang bị bỏ xó — cùng quyền với <c>VoucherValidationController</c> và cùng lý do: cái
/// nhìn trên TOÀN BỘ voucher, không phải dữ liệu của một hành khách.
///
/// Đứng ở controller riêng trên bề mặt <c>api/vouchers</c>: luật nhóm không cho sửa file của người
/// khác, mà <c>VouchersController</c> là file của Trần Trung Hiếu (dòng 51) — mỗi bề mặt một
/// controller, cùng lối <c>FeedbackStatisticsController</c> đứng chung bề mặt với
/// <c>FeedbackAdminController</c>.
///
/// ⚠️ Bề mặt này nay có HAI đoạn literal (<c>validate</c>, <c>statistics</c>) bên cạnh route chi tiết.
/// Vì vậy <c>GET /api/vouchers/{id}</c> của dòng 51 PHẢI giữ ràng buộc <c>{id:guid}</c>: nới thành
/// <c>{id}</c> trần là <c>statistics</c> bị route chi tiết nuốt mất và endpoint này không bao giờ
/// chạy — cùng lối <c>export</c> của <c>GET /api/audit-logs/export</c>.
/// </summary>
[ApiController]
[Route("api/vouchers")]
[Authorize(Policy = RbacPolicies.ManagerOrAbove)]
public class VoucherStatisticsController : ControllerBase
{
    private readonly IVoucherStatisticsService _voucherStatisticsService;

    public VoucherStatisticsController(IVoucherStatisticsService voucherStatisticsService)
        => _voucherStatisticsService = voucherStatisticsService;

    /// <summary>
    /// Bảng hiệu quả của mọi voucher đã phát hành, kèm các con số tổng của cả chương trình khuyến mãi.
    ///
    /// Không có token → 401; không đủ quyền → 403. Không có nhánh lỗi nào khác: endpoint không nhận
    /// tham số, nên đúng quyền là luôn 200 — kể cả khi hệ thống chưa có voucher nào (mọi con đếm bằng
    /// 0 và <c>items</c> rỗng; "chưa có dữ liệu" là một câu trả lời, không phải lỗi).
    /// </summary>
    [HttpGet("statistics")]
    public async Task<IActionResult> GetStatistics(CancellationToken cancellationToken)
    {
        var thongKe = await _voucherStatisticsService.GetAsync(cancellationToken);

        return Ok(thongKe);
    }
}
