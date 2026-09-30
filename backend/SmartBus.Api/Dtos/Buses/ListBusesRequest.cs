using System.ComponentModel.DataAnnotations;

namespace SmartBus.Api.Dtos.Buses;

/// <summary>
/// Tham số lọc + phân trang của GET /api/buses — bind từ query string.
/// Cùng khuôn với <see cref="Routes.ListRoutesRequest"/>.
/// </summary>
public class ListBusesRequest
{
    /// <summary>Tìm theo biển số hoặc loại xe — không phân biệt hoa thường.</summary>
    [StringLength(50, ErrorMessage = "Từ khóa tìm kiếm tối đa 50 ký tự")]
    public string? Search { get; set; }

    /// <summary>
    /// Lọc theo trạng thái: "Active" | "Maintenance" | "Inactive". Bỏ trống = lấy cả ba.
    /// Giá trị không khớp ba mã trên trả về danh sách rỗng — cùng lối
    /// <see cref="Routes.ListRoutesRequest.Status"/> không ép báo lỗi ở query string.
    /// </summary>
    [StringLength(20, ErrorMessage = "Trạng thái tối đa 20 ký tự")]
    public string? Status { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Số trang phải từ 1 trở lên")]
    public int? Page { get; set; }

    [Range(1, 100, ErrorMessage = "Số dòng mỗi trang phải từ 1 đến 100")]
    public int? PageSize { get; set; }
}
