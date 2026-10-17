using SmartBus.Api.Dtos.TicketRequests;

namespace SmartBus.Api.Services;

/// <summary>
/// Gửi yêu cầu đổi chuyến — <c>POST /api/ticket-changes</c> (US 5, task *"API gửi yêu cầu đổi
/// chuyến + tính chênh lệch giá"* — Nguyễn Duy Kiên, dòng 4 Sprint 4).
///
/// Đây là NỬA ĐẦU của luồng đổi vé; nửa sau là <see cref="ITicketRequestReviewService"/> (Hoàng,
/// dòng 5) — quản lý duyệt/từ chối. Hai nửa cố ý nằm ở hai service: phần gửi chỉ GHI một dòng
/// <c>Pending</c>, phần duyệt mới đụng vào <c>Tickets</c>.
///
/// 🔴 Ba điều hợp đồng bắt buộc, chép từ docs/32-csdl-huy-doi-ve.md §3.3 và §6:
///   1. <b>Không đụng dòng vé.</b> Vé gốc vẫn <c>Paid</c> cho tới khi quản lý duyệt — khách còn
///      nguyên chuyến cũ.
///   2. <b>Không giữ ghế mới</b> (không tạo <c>SeatHolds</c>): giữ chỗ là nghiệp vụ của luồng mua
///      vé, có job hết hạn riêng. Vì vậy giữa lúc gửi và lúc duyệt người khác có thể mua mất ghế —
///      phép kiểm còn trống ở đây là phép kiểm MÔ TẢ cho khách, lưới thật nằm ở lượt duyệt.
///   3. <b>Chụp lại <c>NewPrice</c> từ <c>Fares</c></b>; chênh lệch <c>NewPrice - Ticket.Price</c>
///      chỉ được TÍNH trong response, KHÔNG ghi cột nào.
/// </summary>
public interface ITicketChangeSubmissionService
{
    /// <summary>
    /// Ghi một yêu cầu đổi chuyến ở trạng thái <c>Pending</c> cho vé của chính người gọi.
    ///
    /// Các nhánh lỗi (controller dịch sang mã HTTP, xem <see cref="ServiceErrorKind"/>):
    /// vé không tồn tại / không thuộc người gọi → 404; vé không còn <c>Paid</c>, vé đang có yêu cầu
    /// huỷ hoặc đổi chờ duyệt, chuyến mới đã huỷ/đã khởi hành, ghế mới đã có người đặt, tuyến chưa
    /// có giá → 409; chuyến/ghế mới không tồn tại → 404.
    ///
    /// Thành công trả đúng hình dạng dòng chung <see cref="TicketRequestResponse"/>, với
    /// <c>priceDifference</c> CÓ DẤU (dương = khách trả thêm, âm = nhà xe hoàn lại).
    /// </summary>
    /// <param name="request">Vé gốc, chuyến mới, ghế mới và lý do.</param>
    /// <param name="userId">
    /// Người gửi, LẤY TỪ TOKEN — không bao giờ lấy từ body. Cũng là điều kiện sở hữu vé: vé của
    /// người khác trả 404 chứ không phải 403 (không tiết lộ vé có tồn tại, docs/28 §6).
    /// </param>
    Task<ServiceResult<TicketRequestResponse>> SubmitAsync(
        CreateTicketChangeRequest request,
        Guid userId,
        CancellationToken cancellationToken);
}
