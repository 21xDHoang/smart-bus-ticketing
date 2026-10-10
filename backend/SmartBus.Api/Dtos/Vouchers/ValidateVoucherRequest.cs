using System.ComponentModel.DataAnnotations;

namespace SmartBus.Api.Dtos.Vouchers;

/// <summary>
/// Body của POST /api/vouchers/validate — kiểm tra một mã voucher và tính số tiền được giảm
/// (US 18, task *"API kiểm tra và áp dụng voucher vào đơn hàng"* — Nguyễn Duy Kiên, dòng 52 + 53).
/// Hợp đồng đầy đủ ở mục "Voucher — /vouchers" của docs/api-contract.md.
///
/// Cố ý KHÔNG có <c>userId</c>: mã voucher là tài nguyên dùng chung, không thuộc về ai — cùng lối
/// <see cref="SeatHolds.CreateSeatHoldRequest"/> không có userId. Người gọi vẫn phải đăng nhập
/// (<c>[Authorize]</c>) vì đây là bước trong luồng thanh toán.
///
/// ⚠️ Cố ý KHÔNG có <c>discountAmount</c>: số tiền giảm do SERVER tính. Nhận con số từ client là mở
/// đường cho người ngoài tự khai số tiền được giảm.
/// </summary>
public class ValidateVoucherRequest
{
    /// <summary>
    /// Mã khách gõ. Ràng buộc 2–20 ký tự khớp đúng ô nhập của màn quản lý voucher
    /// (<c>VoucherFormModal.tsx</c>: <c>{ min: 2, max: 20 }</c>).
    ///
    /// Cố ý KHÔNG chặn bộ ký tự (không <c>[RegularExpression]</c>): bản nháp FE chỉ chuẩn hoá chữ HOA
    /// rồi nhận mọi ký tự, nên siết thêm ở đây là từ chối oan những mã màn quản lý cho phép tạo.
    /// Việc chuẩn hoá <c>Trim().ToUpperInvariant()</c> nằm ở tầng service — nhờ vậy khách gõ
    /// "summer10" hay " SUMMER10 " vẫn ra đúng voucher.
    ///
    /// Khai <c>string?</c> chứ không phải <c>string</c> cùng lý do <see cref="TripId"/>: để
    /// <c>= string.Empty</c> thì khi client BỎ HẲN trường này, model binding giữ nguyên chuỗi rỗng của
    /// initializer và cả hai attribute cùng báo lỗi — frontend nhận hai câu cho một lần thiếu, trong
    /// đó câu "phải từ 2 đến 20 ký tự" nói về một con số khách chưa hề gửi.
    /// </summary>
    [Required(ErrorMessage = "Mã voucher không được để trống")]
    [StringLength(20, MinimumLength = 2, ErrorMessage = "Mã voucher phải từ 2 đến 20 ký tự")]
    public string? Code { get; set; }

    /// <summary>
    /// Chuyến khách đang đặt — dùng để tra <c>Trip.RouteId</c> cho điều kiện tuyến (dòng 53).
    ///
    /// Để <c>Guid?</c> chứ không phải <c>Guid</c>: thiếu hẳn trường này sẽ nhận <c>Guid.Empty</c> và
    /// <c>[Required]</c> không bắt được — cùng lý do
    /// <see cref="SeatHolds.CreateSeatHoldRequest.TripId"/>.
    ///
    /// ⚠️ Chuyến KHÔNG tồn tại là **404**, không phải một <c>reasonCode</c>: đó là lỗi phía gọi
    /// (màn thanh toán gửi lên một chuyến không có thật), khác hẳn "voucher không dùng được".
    /// </summary>
    [Required(ErrorMessage = "Chuyến xe không được để trống")]
    public Guid? TripId { get; set; }

    /// <summary>
    /// Tổng tiền đơn TRƯỚC giảm giá — so với <c>MinOrderValue</c> và là gốc để tính phần trăm.
    ///
    /// Để <c>decimal?</c> cùng lý do <see cref="TripId"/>: thiếu trường thì nhận 0, mà 0 là giá trị
    /// HỢP LỆ (đơn rỗng) nên <c>[Required]</c> trên <c>decimal</c> không phân biệt được "không gửi"
    /// với "gửi số 0" — và hai ca đó phải khác nhau.
    ///
    /// Trần 9.999.999.999,99 khớp <c>numeric(12,2)</c> của cột tiền: nhận số lớn hơn là để nó tràn
    /// ở tầng CSDL với một lỗi khó lần theo.
    /// </summary>
    [Required(ErrorMessage = "Giá trị đơn hàng không được để trống")]
    [Range(typeof(decimal), "0", "9999999999.99", ErrorMessage = "Giá trị đơn hàng không hợp lệ")]
    public decimal? OrderAmount { get; set; }
}
