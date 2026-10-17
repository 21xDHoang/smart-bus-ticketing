using System.ComponentModel.DataAnnotations;

namespace SmartBus.Api.Dtos.PassTypes;

/// <summary>
/// Body của POST /api/pass-types — thêm một loại vé tháng vào danh mục.
/// Hợp đồng đầy đủ ở mục "Loại vé tháng — /pass-types" của docs/api-contract.md.
/// </summary>
public class CreatePassTypeRequest
{
    /// <summary>
    /// Mã nghiệp vụ, 1–20 ký tự chữ/số (ví dụ "OneMonth"). Trùng mã → 409.
    /// Kiểm trùng không phân biệt hoa thường, nhưng lưu nguyên chuỗi gửi lên.
    /// </summary>
    [Required(ErrorMessage = "Mã loại vé không được để trống")]
    [StringLength(20, MinimumLength = 1, ErrorMessage = "Mã loại vé phải từ 1 đến 20 ký tự")]
    [RegularExpression(
        "^[A-Za-z0-9]+$",
        ErrorMessage = "Mã loại vé chỉ gồm chữ và số, không dấu cách")]
    public string? Code { get; set; }

    /// <summary>Tên hiển thị cho hành khách, tối đa 100 ký tự.</summary>
    [Required(ErrorMessage = "Tên loại vé không được để trống")]
    [StringLength(100, MinimumLength = 1, ErrorMessage = "Tên loại vé phải từ 1 đến 100 ký tự")]
    public string? Name { get; set; }

    /// <summary>Thời hạn tính bằng tháng lịch, 1..120.</summary>
    [Range(1, 120, ErrorMessage = "Thời hạn phải từ 1 đến 120 tháng")]
    public int? DurationMonths { get; set; }

    /// <summary>
    /// Giá gói cho toàn bộ thời hạn (VND). Không âm; trần khớp numeric(12,2) của CSDL.
    /// </summary>
    [Range(
        typeof(decimal),
        "0",
        "9999999999.99",
        ErrorMessage = "Giá gói phải từ 0 đến 9.999.999.999,99")]
    public decimal? Price { get; set; }
}
