using SmartBus.Api.Dtos.TicketRequests;

namespace SmartBus.Api.Services;

/// <summary>
/// Duyệt / từ chối yêu cầu huỷ vé và yêu cầu đổi vé (US 5, Sprint 4, task *"API duyệt / từ chối yêu
/// cầu huỷ - đổi vé"* — Phùng Duy Hoàng, dòng 5).
///
/// Đây là NỬA SAU của luồng: nửa đầu là khách GỬI yêu cầu (<c>POST /cancellation-requests</c> —
/// Hiếu dòng 3; <c>POST /ticket-changes</c> — Kiên dòng 4), lúc đó chỉ ghi một dòng <c>Pending</c>
/// và KHÔNG đụng vào vé. Vé chỉ bị huỷ ở đây, khi quản lý bấm duyệt.
///
/// Vì sao hai việc này gộp một service: hai bảng có cùng vòng đời trạng thái, cùng luật duyệt, và
/// cùng hệ quả lên bảng <c>Tickets</c> — tách đôi thì hai bản trôi khỏi nhau ngay ở chỗ dễ trôi
/// nhất (luật vé phải còn <c>Paid</c> mới duyệt được). Hình dạng response cũng là một
/// (<see cref="TicketRequestResponse"/>).
///
/// 🔴 Ba việc mà bản cài đặt PHẢI làm, chép từ docs/32-csdl-huy-doi-ve.md §6:
///   1. <b>Đổi vé: kiểm lại ghế mới còn trống NGAY LÚC DUYỆT</b> (§3.3). Lúc khách gửi, ghế mới
///      KHÔNG được giữ chỗ (chủ ý của thiết kế — xem <c>TicketChangeStatus.Pending</c>), nên tin
///      vào phép kiểm lúc gửi là bán trùng ghế.
///   2. <b>Đổi vé đã duyệt thì HUỶ vé cũ và PHÁT HÀNH một vé MỚI</b> — không sửa vé cũ (§3.1).
///   3. <b>Bắt <c>DbUpdateConcurrencyException</c> của §4.3</b> và trả về *"yêu cầu đã được người
///      khác xử lý"* — đừng để nó thành 500.
/// </summary>
public interface ITicketRequestReviewService
{
    /// <summary>
    /// Quản lý duyệt / từ chối một yêu cầu huỷ vé (<c>PATCH /api/cancellation-requests/{id}</c>).
    ///
    /// Duyệt: lật yêu cầu sang <c>Approved</c> + huỷ vé (<c>Tickets.Status = Cancelled</c>). KHÔNG
    /// ghi dòng hoàn tiền nào — bảng <c>Refunds</c> thuộc US 8 và chưa tồn tại; số tiền hoàn là
    /// trường SUY RA ở response (xem <see cref="TicketRequestResponse.RefundAmount"/>).
    ///
    /// Từ chối: lật yêu cầu sang <c>Rejected</c> + lưu lý do; <b>không đụng dòng vé</b> — vé giữ
    /// <c>Paid</c>, ghế vẫn của khách, không khoản tiền nào di chuyển.
    /// </summary>
    /// <param name="id">Id yêu cầu (<c>CancellationRequests.Id</c>).</param>
    /// <param name="request">Quyết định của quản lý — chỉ <c>Approved</c> / <c>Rejected</c>.</param>
    /// <param name="reviewerUserId">
    /// Người bấm duyệt, LẤY TỪ TOKEN (claim <c>nameidentifier</c>) — không bao giờ lấy từ body:
    /// nhận từ body là mở đường cho người này ghi tên người khác vào cột <c>ReviewedByUserId</c>.
    /// </param>
    Task<ServiceResult<TicketRequestResponse>> ReviewCancellationAsync(
        Guid id,
        UpdateTicketRequestStatusRequest request,
        Guid reviewerUserId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Quản lý duyệt / từ chối một yêu cầu đổi vé (<c>PATCH /api/ticket-changes/{id}</c>).
    ///
    /// Duyệt: kiểm lại ghế mới (🔴 việc 1), huỷ vé cũ, PHÁT HÀNH VÉ MỚI (🔴 việc 2) với
    /// <c>Price = NewPrice</c> đã chụp lúc khách gửi — <b>không tra lại bảng <c>Fares</c></b> — rồi
    /// ghi <c>NewTicketId</c> và lật yêu cầu sang <c>Approved</c>.
    ///
    /// Từ chối: lật yêu cầu sang <c>Rejected</c> + lưu lý do; <c>NewTicketId</c> giữ null, không
    /// kiểm ghế (kiểm làm gì khi không cấp ghế cho ai).
    /// </summary>
    /// <param name="id">Id yêu cầu (<c>TicketChanges.Id</c>).</param>
    /// <param name="request">Quyết định của quản lý — chỉ <c>Approved</c> / <c>Rejected</c>.</param>
    /// <param name="reviewerUserId">Người bấm duyệt, lấy từ token — xem hàm trên.</param>
    Task<ServiceResult<TicketRequestResponse>> ReviewTicketChangeAsync(
        Guid id,
        UpdateTicketRequestStatusRequest request,
        Guid reviewerUserId,
        CancellationToken cancellationToken);
}
