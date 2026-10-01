using System.ComponentModel.DataAnnotations;

namespace SmartBus.Api.Dtos.Drivers;

/// <summary>
/// Tham số lọc + phân trang của GET /api/drivers — bind từ query string.
/// Cùng khuôn với <see cref="Admin.ListAdminUsersRequest"/> bỏ đi bộ lọc vai trò:
/// danh sách này chỉ chứa tài xế nên không có gì để lọc theo vai trò.
/// </summary>
public class ListDriversRequest
{
    /// <summary>Tìm theo họ tên, SĐT hoặc email — không phân biệt hoa thường.</summary>
    [StringLength(200, ErrorMessage = "Từ khóa tìm kiếm tối đa 200 ký tự")]
    public string? Search { get; set; }

    /// <summary>true = còn hoạt động, false = đã khóa. Bỏ trống = lấy cả hai.</summary>
    public bool? IsActive { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Số trang phải từ 1 trở lên")]
    public int? Page { get; set; }

    [Range(1, 100, ErrorMessage = "Số dòng mỗi trang phải từ 1 đến 100")]
    public int? PageSize { get; set; }
}
