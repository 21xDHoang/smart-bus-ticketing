using System.ComponentModel.DataAnnotations;

namespace SmartBus.Api.Dtos.MonthlyPasses.Admin;

/// <summary>
/// Tham số lọc + phân trang của GET /api/monthly-passes (màn quản trị) — bind từ query string.
/// Cùng khuôn với <see cref="Buses.ListBusesRequest"/>.
/// Hợp đồng đầy đủ ở mục "Vé tháng — /monthly-passes" của docs/api-contract.md.
/// </summary>
public class ListMonthlyPassesRequest
{
    /// <summary>
    /// Tìm không phân biệt hoa thường theo mã vé, họ tên hoặc SĐT hành khách.
    /// </summary>
    [StringLength(100, ErrorMessage = "Từ khóa tìm kiếm tối đa 100 ký tự")]
    public string? Search { get; set; }

    /// <summary>
    /// Lọc theo cột Status: "Active" | "Expired" | "Cancelled". Bỏ trống = lấy cả ba.
    /// Giá trị không khớp mã nào trả về danh sách rỗng — cùng lối
    /// <see cref="Buses.ListBusesRequest.Status"/>, không báo lỗi ở query string.
    ///
    /// Đây là bộ lọc TRA CỨU theo cột lưu, không phải "còn hiệu lực thật" — Active/Expired có độ
    /// trễ (job quét). Muốn đếm theo hiệu lực thật thì dùng GET /monthly-passes/statistics.
    /// </summary>
    [StringLength(20, ErrorMessage = "Trạng thái tối đa 20 ký tự")]
    public string? Status { get; set; }

    /// <summary>Chỉ lấy vé của một tuyến. Bỏ trống = mọi tuyến.</summary>
    public Guid? RouteId { get; set; }

    /// <summary>Chỉ lấy vé của một loại vé (mã trong bảng PassTypes). Bỏ trống = mọi loại.</summary>
    [StringLength(20, ErrorMessage = "Mã loại vé không được vượt quá 20 ký tự")]
    public string? PassTypeCode { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Số trang phải từ 1 trở lên")]
    public int? Page { get; set; }

    [Range(1, 100, ErrorMessage = "Số dòng mỗi trang phải từ 1 đến 100")]
    public int? PageSize { get; set; }
}
