using System.ComponentModel.DataAnnotations;

namespace SmartBus.Api.Dtos.MonthlyPasses;

/// <summary>
/// Body của POST /api/monthly-passes/{id}/renew — mọi trường đều tùy chọn, gửi <c>{}</c> hoặc
/// không gửi body đều được. Hợp đồng đầy đủ ở mục "Vé tháng — /monthly-passes" của
/// docs/api-contract.md.
/// </summary>
public class RenewMonthlyPassRequest
{
    /// <summary>
    /// Mã loại vé mới (bảng PassTypes), ví dụ "OneMonth", "TwelveMonths". Bỏ trống / chuỗi rỗng =
    /// giữ nguyên loại vé của vé đang gia hạn. Không khớp loại vé nào → 404.
    /// </summary>
    [StringLength(20, ErrorMessage = "Mã loại vé không được vượt quá 20 ký tự")]
    public string? PassTypeCode { get; set; }
}
