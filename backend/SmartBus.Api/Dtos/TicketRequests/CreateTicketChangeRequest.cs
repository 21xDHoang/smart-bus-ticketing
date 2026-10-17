using System.ComponentModel.DataAnnotations;

namespace SmartBus.Api.Dtos.TicketRequests;

/// <summary>
/// Body của <c>POST /api/ticket-changes</c> — hành khách gửi yêu cầu đổi chuyến (US 5, task
/// *"API gửi yêu cầu đổi chuyến + tính chênh lệch giá"* — Nguyễn Duy Kiên, dòng 4 Sprint 4).
/// Hợp đồng đầy đủ: docs/api-contract.md, mục "Gửi yêu cầu đổi chuyến".
///
/// KHÔNG có trường giá nào trong body — kể cả <c>newPrice</c>. Giá chuyến mới do máy chủ tra từ
/// <c>Fares</c> và chụp lại (cùng nguồn với <c>GET /trips/search</c>); nhận giá từ client là để
/// khách tự quyết định mình trả bao nhiêu.
///
/// KHÔNG có <c>userId</c>: người gửi lấy từ token. Nhận từ body là mở đường cho người này ghi yêu
/// cầu dưới tên người khác — cùng lý do đã ghi ở <see cref="UpdateTicketRequestStatusRequest"/>.
/// </summary>
public class CreateTicketChangeRequest
{
    /// <summary>Vé gốc khách muốn đổi. Không tồn tại hoặc không thuộc người gọi → 404.</summary>
    [Required(ErrorMessage = "Vé không được để trống")]
    public Guid? TicketId { get; set; }

    /// <summary>Chuyến khách muốn đổi sang. Không tồn tại → 404; đã huỷ / đã khởi hành → 409.</summary>
    [Required(ErrorMessage = "Chuyến muốn đổi sang không được để trống")]
    public Guid? NewTripId { get; set; }

    /// <summary>
    /// Ghế khách muốn ngồi ở chuyến mới. Phải thuộc xe của <see cref="NewTripId"/> — ghế của xe
    /// khác không tồn tại trên sơ đồ của chuyến đó, nên trả 404 như một id sai.
    /// </summary>
    [Required(ErrorMessage = "Ghế muốn đổi sang không được để trống")]
    public Guid? NewSeatId { get; set; }

    /// <summary>
    /// Lý do khách nêu. Không bắt buộc — cùng lý do như <see cref="Entities.CancellationRequest.Reason"/>:
    /// bắt buộc thì hành khách gõ "abc" cho xong, dữ liệu nhận được chẳng tốt hơn. Chuỗi rỗng /
    /// toàn khoảng trắng được lưu thành <c>null</c>.
    /// </summary>
    [StringLength(2000, ErrorMessage = "Lý do đổi vé tối đa 2000 ký tự")]
    public string? Reason { get; set; }
}
