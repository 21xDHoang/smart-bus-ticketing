using System.ComponentModel.DataAnnotations;

namespace SmartBus.Api.Dtos.PassTypes;

/// <summary>
/// Body của PUT /api/pass-types/{id} — sửa tên, thời hạn và giá của một loại vé.
/// KHÔNG có <c>code</c>: mã là khoá nghiệp vụ bất biến (khách và client trao đổi bằng mã này).
/// Hợp đồng đầy đủ ở mục "Loại vé tháng — /pass-types" của docs/api-contract.md.
/// </summary>
public class UpdatePassTypeRequest
{
    /// <summary>Tên hiển thị cho hành khách, tối đa 100 ký tự.</summary>
    [Required(ErrorMessage = "Tên loại vé không được để trống")]
    [StringLength(100, MinimumLength = 1, ErrorMessage = "Tên loại vé phải từ 1 đến 100 ký tự")]
    public string? Name { get; set; }

    /// <summary>Thời hạn tính bằng tháng lịch, 1..120.</summary>
    [Range(1, 120, ErrorMessage = "Thời hạn phải từ 1 đến 120 tháng")]
    public int? DurationMonths { get; set; }

    /// <summary>
    /// Giá gói mới (VND). Chỉ ảnh hưởng các vé mua SAU đó — vé đã bán giữ giá chụp trong
    /// MonthlyPass.Price.
    /// </summary>
    [Range(
        typeof(decimal),
        "0",
        "9999999999.99",
        ErrorMessage = "Giá gói phải từ 0 đến 9.999.999.999,99")]
    public decimal? Price { get; set; }
}
