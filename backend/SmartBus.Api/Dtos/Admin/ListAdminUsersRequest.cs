using System.ComponentModel.DataAnnotations;

namespace SmartBus.Api.Dtos.Admin;

/// <summary>
/// Tham số lọc + phân trang của GET /api/admin/users — bind từ query string.
/// Tên trường khớp tham số mà frontend đang gửi (frontend/src/api/adminUserApi.ts).
/// </summary>
public class ListAdminUsersRequest
{
    /// <summary>Tìm theo họ tên, SĐT hoặc email — không phân biệt hoa thường.</summary>
    [StringLength(200, ErrorMessage = "Từ khóa tìm kiếm tối đa 200 ký tự")]
    public string? Search { get; set; }

    /// <summary>
    /// Lọc theo mã VAI TRÒ CHÍNH (Admin | Manager | Driver | Passenger) — khớp đúng cột
    /// "Vai trò" đang hiển thị trên bảng, để bộ lọc và cột không nói hai chuyện khác nhau.
    /// Bỏ trống = lấy mọi vai trò.
    /// </summary>
    [StringLength(50, ErrorMessage = "Mã vai trò tối đa 50 ký tự")]
    public string? Role { get; set; }

    /// <summary>true = đang mở, false = đã khóa. Bỏ trống = lấy cả hai.</summary>
    public bool? IsActive { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Số trang phải từ 1 trở lên")]
    public int? Page { get; set; }

    [Range(1, 100, ErrorMessage = "Số dòng mỗi trang phải từ 1 đến 100")]
    public int? PageSize { get; set; }
}
