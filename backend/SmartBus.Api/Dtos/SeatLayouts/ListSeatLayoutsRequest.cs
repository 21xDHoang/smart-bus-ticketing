using System.ComponentModel.DataAnnotations;

namespace SmartBus.Api.Dtos.SeatLayouts;

/// <summary>
/// Tham số lọc của GET /api/seat-layouts — bind từ query string. Cùng khuôn
/// <see cref="Buses.ListBusesRequest"/>, chỉ khác là KHÔNG phân trang: số sơ đồ bằng số loại xe
/// trong đội, chỉ vài dòng nên trả hết.
/// </summary>
public class ListSeatLayoutsRequest
{
    /// <summary>Tìm theo loại xe — không phân biệt hoa thường.</summary>
    [StringLength(50, ErrorMessage = "Từ khóa tìm kiếm tối đa 50 ký tự")]
    public string? Search { get; set; }
}
