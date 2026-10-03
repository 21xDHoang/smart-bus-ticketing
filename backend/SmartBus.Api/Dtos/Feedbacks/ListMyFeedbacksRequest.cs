using System.ComponentModel.DataAnnotations;

namespace SmartBus.Api.Dtos.Feedbacks;

/// <summary>
/// Tham số query của GET /api/feedbacks/me — hợp đồng "Phản ánh — /feedbacks", mục "Phản ánh của
/// tôi" của docs/api-contract.md.
///
/// Chỉ có <c>status</c>, KHÔNG có <c>page</c>/<c>pageSize</c>: danh sách phản ánh của một hành
/// khách là MẢNG TRẦN không phân trang — cùng lối GET /monthly-passes/me (một người chỉ có vài
/// phản ánh, phân trang là nghi thức thừa). Cố ý không dùng chung <see cref="ListFeedbacksRequest"/>
/// với nhóm admin: lớp đó mang theo page/pageSize mà endpoint này không nhận, dùng lại là để lộ ra
/// hai tham số không có tác dụng.
///
/// <c>status</c> để DẠNG CHUỖI rồi so ở tầng nghiệp vụ, không để kiểu enum (mục D3 của
/// docs/03-quy-uoc.md): mã lạ KHÔNG phải lỗi — hàm ý "chưa có phản ánh nào ở trạng thái đó" nên trả
/// mảng rỗng, cùng lối <see cref="ListFeedbacksRequest.Status"/> của nhóm admin.
/// </summary>
public class ListMyFeedbacksRequest
{
    /// <summary>Một trong New / InProgress / Resolved. Bỏ trống = mọi trạng thái.</summary>
    [StringLength(20, ErrorMessage = "Mã trạng thái tối đa 20 ký tự")]
    public string? Status { get; set; }
}
