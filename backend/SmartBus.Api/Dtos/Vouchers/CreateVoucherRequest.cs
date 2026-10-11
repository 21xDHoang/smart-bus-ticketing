using System.ComponentModel.DataAnnotations;
using SmartBus.Api.Entities;

namespace SmartBus.Api.Dtos.Vouchers;

/// <summary>
/// Body của POST /api/vouchers — tạo voucher cho màn quản trị (US 18, task *"API CRUD voucher"* —
/// Trần Trung Hiếu, dòng 51). Hợp đồng đầy đủ ở mục "Voucher — /vouchers", phần "CRUD voucher" của
/// docs/api-contract.md.
///
/// Attribute chỉ chặn được lỗi KHÔNG phụ thuộc trường khác (thiếu, sai khoảng, sai độ dài). Các luật
/// phụ thuộc <see cref="DiscountType"/> (Percent: 1–100, FixedAmount: &gt; 0; trần chỉ có nghĩa cho
/// Percent) và thứ tự <see cref="ValidFrom"/>/<see cref="ValidUntil"/> nằm ở tầng service — cùng lối
/// <see cref="SeatHolds.CreateSeatHoldRequest"/> để lỗi trả về kèm đúng tên trường camelCase.
///
/// Cố ý KHÔNG có <c>status</c> (voucher mới luôn Active) và KHÔNG có <c>usedCount</c> (backend giữ,
/// FE gửi lên bị bỏ qua) — hợp đồng mục CRUD voucher.
/// </summary>
public class CreateVoucherRequest
{
    /// <summary>
    /// Mã khách gõ — chuẩn hoá <c>Trim().ToUpperInvariant()</c> ở tầng service, gõ "summer10" vẫn ra
    /// SUMMER10. Ràng buộc 2–20 khớp đúng ô nhập của màn quản lý (VoucherFormModal.tsx).
    /// Cố ý không chặn bộ ký tự — cùng lý do <see cref="ValidateVoucherRequest.Code"/>.
    /// </summary>
    [Required(ErrorMessage = "Mã voucher không được để trống")]
    [StringLength(20, MinimumLength = 2, ErrorMessage = "Mã voucher phải từ 2 đến 20 ký tự")]
    public string? Code { get; set; }

    [Required(ErrorMessage = "Tên voucher không được để trống")]
    [StringLength(200, ErrorMessage = "Tên voucher tối đa 200 ký tự")]
    public string? Name { get; set; }

    /// <summary>
    /// Kiểu giảm giá — quyết định nghĩa của <see cref="DiscountValue"/>. Nhận CHUỖI tên enum
    /// ("Percent"/"FixedAmount") chứ không phải kiểu enum: System.Text.Json mặc định không đọc enum
    /// từ chuỗi (cần JsonStringEnumConverter mà dự án không bật), và parse tay ở tầng service mới
    /// trả được câu lỗi tiếng Việt kèm <c>errors.discountType</c> — cùng lối
    /// FareService.TryParsePassengerType (cũng từ chối chuỗi số "3").
    /// </summary>
    [Required(ErrorMessage = "Loại giảm giá không được để trống")]
    [StringLength(20, ErrorMessage = "Loại giảm giá không hợp lệ")]
    public string? DiscountType { get; set; }

    /// <summary>
    /// Giá trị giảm. Trần 9.999.999.999,99 khớp numeric(12,2) của cột tiền — nhận số lớn hơn là để
    /// nó tràn ở tầng CSDL với một lỗi khó lần theo. Cận dưới phụ thuộc kiểu (Percent: 1–100,
    /// FixedAmount: &gt; 0) nên nằm ở tầng service.
    /// </summary>
    [Required(ErrorMessage = "Giá trị giảm không được để trống")]
    [Range(typeof(decimal), "0", "9999999999.99", ErrorMessage = "Giá trị giảm không hợp lệ")]
    public decimal? DiscountValue { get; set; }

    /// <summary>Giá trị đơn tối thiểu — bỏ trống coi như 0 (không yêu cầu).</summary>
    [Range(typeof(decimal), "0", "9999999999.99", ErrorMessage = "Giá trị đơn tối thiểu không hợp lệ")]
    public decimal? MinOrderValue { get; set; }

    /// <summary>
    /// Trần số tiền giảm — chỉ có nghĩa khi <see cref="DiscountType"/> là Percent; FixedAmount thì
    /// service ép null (trần với số tiền cố định là khái niệm vô nghĩa — xem Voucher.MaxDiscount).
    /// Percent mà gửi 0/âm → 400 kèm <c>errors.maxDiscount</c> ở tầng service.
    /// </summary>
    [Range(typeof(decimal), "0", "9999999999.99", ErrorMessage = "Trần giảm không hợp lệ")]
    public decimal? MaxDiscount { get; set; }

    /// <summary>Điều kiện tuyến — null = áp dụng cho mọi tuyến. Tuyến không tồn tại → 404 ở tầng service.</summary>
    public Guid? RouteId { get; set; }

    /// <summary>Tổng số lượt phát hành — ít nhất 1, mã 0 lượt không dùng được cho ai.</summary>
    [Required(ErrorMessage = "Số lượng không được để trống")]
    [Range(1, int.MaxValue, ErrorMessage = "Số lượng phải từ 1 trở lên")]
    public int? Quantity { get; set; }

    /// <summary>Bắt đầu hiệu lực — tính CẢ đầu mút này (now &gt;= validFrom). Lưu dạng UTC.</summary>
    [Required(ErrorMessage = "Thời điểm bắt đầu không được để trống")]
    public DateTime? ValidFrom { get; set; }

    /// <summary>Kết thúc hiệu lực — tính CẢ đầu mút này (now &lt;= validUntil). Phải sau ValidFrom (service kiểm).</summary>
    [Required(ErrorMessage = "Thời điểm kết thúc không được để trống")]
    public DateTime? ValidUntil { get; set; }
}
