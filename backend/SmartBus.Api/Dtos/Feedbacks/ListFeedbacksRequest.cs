using System.ComponentModel.DataAnnotations;

namespace SmartBus.Api.Dtos.Feedbacks;

/// <summary>
/// Tham số query của GET /api/admin/feedbacks — hợp đồng "Phản ánh — /feedbacks" của
/// docs/api-contract.md.
///
/// <c>status</c>/<c>type</c> để DẠNG CHUỖI rồi so ở tầng nghiệp vụ, không để kiểu enum (mục D3 của
/// docs/03-quy-uoc.md): mã lạ KHÔNG phải lỗi — hàm ý "chưa có phản ánh nào ở trạng thái đó" nên
/// trả danh sách rỗng, cùng lối <see cref="Routes.ListRoutesRequest.Status"/>.
/// </summary>
public class ListFeedbacksRequest
{
    /// <summary>Một trong New / InProgress / Resolved. Bỏ trống = mọi trạng thái.</summary>
    [StringLength(20, ErrorMessage = "Mã trạng thái tối đa 20 ký tự")]
    public string? Status { get; set; }

    /// <summary>Một trong Complaint / Compliment / Suggestion. Bỏ trống = mọi loại.</summary>
    [StringLength(20, ErrorMessage = "Mã loại phản ánh tối đa 20 ký tự")]
    public string? Type { get; set; }

    /// <summary>Trang hiện tại, bắt đầu từ 1. Bỏ trống = 1.</summary>
    [Range(1, int.MaxValue, ErrorMessage = "Số trang phải từ 1 trở lên")]
    public int? Page { get; set; }

    /// <summary>Số dòng mỗi trang, 1..100. Bỏ trống = 10.</summary>
    [Range(1, 100, ErrorMessage = "Số dòng mỗi trang phải từ 1 đến 100")]
    public int? PageSize { get; set; }
}
