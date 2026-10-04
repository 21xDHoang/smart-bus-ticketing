using System.ComponentModel.DataAnnotations;

namespace SmartBus.Api.Dtos.MonthlyPasses;

/// <summary>
/// Body của POST /api/monthly-passes — đăng ký vé tháng mới cho chính người gọi.
/// Hợp đồng đầy đủ ở mục "Vé tháng — /monthly-passes" của docs/api-contract.md.
///
/// Chỉ có hai trường: "thời hạn" mà task nói tới KHÔNG phải tham số riêng — nó do loại vé
/// (bảng PassTypes) quyết định, hành khách chọn loại vé là chọn luôn thời hạn và giá gói.
/// </summary>
public class RegisterMonthlyPassRequest
{
    /// <summary>
    /// Bắt buộc. GUID của tuyến muốn đăng ký. Không trỏ tới tuyến nào → 404 — tham chiếu cứng,
    /// cùng câu trả lời của GET /trips/search.
    /// </summary>
    [Required(ErrorMessage = "Tuyến đường không được để trống")]
    public Guid? RouteId { get; set; }

    /// <summary>
    /// Bắt buộc. Mã loại vé trong bảng PassTypes, ví dụ "OneMonth". Không khớp loại vé nào → 404.
    /// </summary>
    [Required(ErrorMessage = "Loại vé không được để trống")]
    [StringLength(20, ErrorMessage = "Mã loại vé không được vượt quá 20 ký tự")]
    public string? PassTypeCode { get; set; }
}
