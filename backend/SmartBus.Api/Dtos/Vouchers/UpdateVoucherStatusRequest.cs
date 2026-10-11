using System.ComponentModel.DataAnnotations;
using SmartBus.Api.Entities;

namespace SmartBus.Api.Dtos.Vouchers;

/// <summary>
/// Body của PATCH /api/vouchers/{id}/status — bật/tắt áp dụng voucher (US 18, task *"API CRUD
/// voucher"* — Trần Trung Hiếu, dòng 51). Chỉ có đúng một trường: trạng thái người vận hành bật/tắt
/// (xem <see cref="VoucherStatus"/> — KHÔNG phải trạng thái hiệu lực; hết hạn/hết lượt là thứ suy ra
/// được, không lưu).
///
/// Nhận CHUỖI tên enum ("Active"/"Inactive") chứ không phải kiểu enum — cùng lý do
/// <see cref="CreateVoucherRequest.DiscountType"/>: parse ở tầng service mới trả được câu lỗi tiếng
/// Việt kèm <c>errors.status</c>, và không nhận chuỗi số.
/// </summary>
public class UpdateVoucherStatusRequest
{
    [Required(ErrorMessage = "Trạng thái không được để trống")]
    [StringLength(20, ErrorMessage = "Trạng thái voucher không hợp lệ")]
    public string? Status { get; set; }
}
