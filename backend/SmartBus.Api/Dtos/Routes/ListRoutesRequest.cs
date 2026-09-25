using System.ComponentModel.DataAnnotations;

namespace SmartBus.Api.Dtos.Routes;

/// <summary>
/// Tham số lọc + phân trang của GET /api/routes — bind từ query string.
/// Cùng khuôn với <see cref="Admin.ListAdminUsersRequest"/>.
/// </summary>
public class ListRoutesRequest
{
    /// <summary>Tìm theo mã, tên, điểm đầu hoặc điểm cuối — không phân biệt hoa thường.</summary>
    [StringLength(200, ErrorMessage = "Từ khóa tìm kiếm tối đa 200 ký tự")]
    public string? Search { get; set; }

    /// <summary>
    /// Lọc theo trạng thái: "Active" | "Inactive". Bỏ trống = lấy cả hai.
    /// Giá trị không khớp hai mã trên trả về danh sách rỗng — cùng lối
    /// <see cref="Admin.ListAdminUsersRequest.Role"/> không ép báo lỗi ở query string.
    /// </summary>
    [StringLength(20, ErrorMessage = "Trạng thái tối đa 20 ký tự")]
    public string? Status { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Số trang phải từ 1 trở lên")]
    public int? Page { get; set; }

    [Range(1, 100, ErrorMessage = "Số dòng mỗi trang phải từ 1 đến 100")]
    public int? PageSize { get; set; }
}
