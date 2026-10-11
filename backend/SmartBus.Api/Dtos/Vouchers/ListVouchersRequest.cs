using System.ComponentModel.DataAnnotations;

namespace SmartBus.Api.Dtos.Vouchers;

/// <summary>
/// Tham số lọc + phân trang của GET /api/vouchers — bind từ query string. Tên trường khớp tham số
/// mà bản nháp FE đang gửi (frontend/src/api/voucherApi.ts — <c>VoucherListParams</c>).
/// </summary>
public class ListVouchersRequest
{
    /// <summary>Tìm theo mã hoặc tên chương trình — không phân biệt hoa thường. Bỏ trống = tất cả.</summary>
    [StringLength(200, ErrorMessage = "Từ khóa tìm kiếm tối đa 200 ký tự")]
    public string? Search { get; set; }

    /// <summary>
    /// Lọc theo trạng thái NGƯỜI VẬN HÀNH bật/tắt ("Active"/"Inactive"). Bỏ trống = cả hai.
    /// Chuỗi chứ không phải enum để service parse và trả câu lỗi tiếng Việt kèm <c>errors.status</c>
    /// (và từ chối chuỗi số) — cùng lối FareService.TryParsePassengerType.
    /// </summary>
    [StringLength(20, ErrorMessage = "Trạng thái voucher không hợp lệ")]
    public string? Status { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Số trang phải từ 1 trở lên")]
    public int? Page { get; set; }

    [Range(1, 100, ErrorMessage = "Số dòng mỗi trang phải từ 1 đến 100")]
    public int? PageSize { get; set; }
}
