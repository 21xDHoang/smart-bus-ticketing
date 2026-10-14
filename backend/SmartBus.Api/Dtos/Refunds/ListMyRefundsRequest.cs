using System.ComponentModel.DataAnnotations;

namespace SmartBus.Api.Dtos.Refunds;

/// <summary>
/// Tham số query của GET /api/refunds — hợp đồng "Hoàn tiền — /refunds" của docs/api-contract.md.
///
/// Chỉ có <c>paymentCode</c> và <c>status</c>, KHÔNG có <c>page</c>/<c>pageSize</c>: danh sách khoản
/// hoàn của một hành khách là MẢNG TRẦN không phân trang — cùng lối GET /feedbacks/me và
/// GET /monthly-passes/me (một người chỉ có vài khoản hoàn, phân trang là nghi thức thừa). Danh sách
/// toàn hệ thống có phân trang là việc của GET /admin/refunds — endpoint khác, hình dạng khác.
///
/// <c>paymentCode</c> là thứ biến endpoint này thành *"trạng thái hoàn tiền CỦA MỘT GIAO DỊCH"* —
/// đúng câu chữ của task dòng 42. Bỏ trống thì endpoint trả mọi khoản hoàn của người gọi, tức là
/// phục vụ luôn màn "Theo dõi trạng thái hoàn tiền của hành khách" (dòng 44, Dương Thị Hạnh) mà
/// KHÔNG cần thêm đường dẫn <c>/refunds/mine</c> — lý do đầy đủ ở hợp đồng, mục "Vì sao không có
/// GET /refunds/mine".
///
/// <c>status</c> để DẠNG CHUỖI rồi so ở tầng nghiệp vụ, không để kiểu enum (mục D3 của
/// docs/03-quy-uoc.md): mã lạ KHÔNG phải lỗi — hàm ý "chưa có khoản hoàn nào ở trạng thái đó" nên trả
/// mảng rỗng, cùng lối <c>ListMyFeedbacksRequest.Status</c>.
/// </summary>
public class ListMyRefundsRequest
{
    /// <summary>
    /// Mã giao dịch gốc (<c>Payments.PaymentCode</c>), dạng "PM-xxxxxxxx". Bỏ trống = mọi khoản hoàn
    /// của người gọi.
    ///
    /// Độ dài chặn ở 64 cho khớp cột <c>Payments.PaymentCode</c> và <c>Refunds.RefundCode</c> — mã dài
    /// hơn cột là mã không thể tồn tại, nên 400 ở đây là câu trả lời đúng, không phải khe hở bị bịt.
    /// </summary>
    [StringLength(64, ErrorMessage = "Mã giao dịch tối đa 64 ký tự")]
    public string? PaymentCode { get; set; }

    /// <summary>Một trong Pending / Succeeded / Failed. Bỏ trống = mọi trạng thái.</summary>
    [StringLength(20, ErrorMessage = "Mã trạng thái tối đa 20 ký tự")]
    public string? Status { get; set; }
}
