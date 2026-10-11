using System.ComponentModel.DataAnnotations;
using SmartBus.Api.Entities;

namespace SmartBus.Api.Dtos.Vouchers;

/// <summary>
/// Body của PUT /api/vouchers/{id} — sửa voucher (US 18, task *"API CRUD voucher"* — Trần Trung
/// Hiếu, dòng 51). Cùng bảng trường và cùng bảng ràng buộc với <see cref="CreateVoucherRequest"/>;
/// khác đúng hai điểm (đều nằm ở tầng service, xem hợp đồng mục CRUD voucher):
///
/// 1. <c>quantity</c> phải ≥ <c>usedCount</c> — hạ tổng lượt xuống dưới số đã tiêu thụ là dữ liệu
///    tự mâu thuẫn → 400 kèm <c>errors.quantity</c>.
/// 2. Trùng mã thì bỏ qua chính voucher đang sửa — cùng mã với voucher KHÁC mới là 409.
///
/// <c>usedCount</c> và <c>status</c> cố ý KHÔNG có: usedCount do backend giữ (gửi lên bị bỏ qua),
/// đổi trạng thái qua PATCH /vouchers/{id}/status — hai đường ghi tách bạch theo hợp đồng.
/// </summary>
public class UpdateVoucherRequest
{
    /// <summary>Mã khách gõ — chuẩn hoá <c>Trim().ToUpperInvariant()</c> ở tầng service.</summary>
    [Required(ErrorMessage = "Mã voucher không được để trống")]
    [StringLength(20, MinimumLength = 2, ErrorMessage = "Mã voucher phải từ 2 đến 20 ký tự")]
    public string? Code { get; set; }

    [Required(ErrorMessage = "Tên voucher không được để trống")]
    [StringLength(200, ErrorMessage = "Tên voucher tối đa 200 ký tự")]
    public string? Name { get; set; }

    /// <summary>Kiểu giảm giá — chuỗi tên enum, parse ở tầng service (xem CreateVoucherRequest.DiscountType).</summary>
    [Required(ErrorMessage = "Loại giảm giá không được để trống")]
    [StringLength(20, ErrorMessage = "Loại giảm giá không hợp lệ")]
    public string? DiscountType { get; set; }

    [Required(ErrorMessage = "Giá trị giảm không được để trống")]
    [Range(typeof(decimal), "0", "9999999999.99", ErrorMessage = "Giá trị giảm không hợp lệ")]
    public decimal? DiscountValue { get; set; }

    [Range(typeof(decimal), "0", "9999999999.99", ErrorMessage = "Giá trị đơn tối thiểu không hợp lệ")]
    public decimal? MinOrderValue { get; set; }

    [Range(typeof(decimal), "0", "9999999999.99", ErrorMessage = "Trần giảm không hợp lệ")]
    public decimal? MaxDiscount { get; set; }

    /// <summary>Điều kiện tuyến — null = áp dụng cho mọi tuyến. Tuyến không tồn tại → 404 ở tầng service.</summary>
    public Guid? RouteId { get; set; }

    [Required(ErrorMessage = "Số lượng không được để trống")]
    [Range(1, int.MaxValue, ErrorMessage = "Số lượng phải từ 1 trở lên")]
    public int? Quantity { get; set; }

    [Required(ErrorMessage = "Thời điểm bắt đầu không được để trống")]
    public DateTime? ValidFrom { get; set; }

    [Required(ErrorMessage = "Thời điểm kết thúc không được để trống")]
    public DateTime? ValidUntil { get; set; }
}
