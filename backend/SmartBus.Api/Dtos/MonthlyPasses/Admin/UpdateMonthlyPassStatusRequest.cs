using System.ComponentModel.DataAnnotations;

namespace SmartBus.Api.Dtos.MonthlyPasses.Admin;

/// <summary>
/// Body của PATCH /api/monthly-passes/{id}/status — huỷ hoặc mở lại một vé.
/// Hợp đồng đầy đủ ở mục "Vé tháng — /monthly-passes" của docs/api-contract.md.
///
/// Chỉ nhận hai giá trị. "Expired" KHÔNG nhận vì đó là trạng thái do job hết hạn quản lý; muốn
/// đánh dấu vé hết hạn thì chờ job, hoặc huỷ vé bằng "Cancelled".
/// </summary>
public class UpdateMonthlyPassStatusRequest
{
    /// <summary>"Cancelled" (huỷ) hoặc "Active" (mở lại vé đã huỷ). Giá trị khác → 400.</summary>
    [Required(ErrorMessage = "Trạng thái không được để trống")]
    [RegularExpression(
        "^(Active|Cancelled)$",
        ErrorMessage = "Trạng thái chỉ nhận giá trị Active hoặc Cancelled")]
    public string? Status { get; set; }
}
