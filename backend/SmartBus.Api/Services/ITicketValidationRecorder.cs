using SmartBus.Api.Dtos.TicketValidations;
using SmartBus.Api.Entities;

namespace SmartBus.Api.Services;

/// <summary>
/// Ghi một LƯỢT SOÁT vào nhật ký <c>TicketValidations</c> — task dòng 18 *"Xử lý ngoại tuyến: lưu tạm
/// lượt soát khi mất mạng, đồng bộ lại sau"* (Phùng Duy Hoàng), US 15. Đây là mảnh SERVER của giao
/// thức ngoại tuyến; hàng đợi phía máy quét là việc của màn quét (dòng 20, Nguyễn Đình Băng) và được
/// đặc tả ở <c>docs/39-hang-doi-ngoai-tuyen-soat-ve.md</c>.
///
/// <para>
/// <b>Ai gọi:</b> endpoint soát vé <c>POST /tickets/validate</c> (dòng 15, Trần Trung Hiếu) sau khi đã
/// xác thực chữ ký QR và tính xong <see cref="TicketValidationResult"/>. Cả lượt ONLINE lẫn lượt
/// đồng bộ từ hàng đợi ngoại tuyến đều đi qua đúng một cửa này — nhờ vậy hai đường không thể ghi
/// nhật ký lệch nhau.
/// </para>
///
/// <para>
/// <b>Service này KHÔNG làm gì:</b> không xác thực chữ ký QR, không tra vé, không tính kết quả, không
/// đánh dấu <c>Ticket.Status = Used</c> — đó là việc của endpoint (dòng 15/16). Ở đây chỉ có: chống
/// trùng theo <c>ClientScanId</c>, kiểm khoảng hợp lý của <c>ScannedAt</c>, và ghi dòng nhật ký.
/// </para>
///
/// <para>
/// <b>Hợp đồng idempotent (docs/34 §6):</b> gửi lại cùng <c>ClientScanId</c> — dù là lần gửi thứ hai
/// do mất mạng, do app bị kill, hay do hai lượt thử chồng nhau — luôn là THÀNH CÔNG và trả về ĐÚNG
/// dòng đã ghi lần đầu; không bao giờ đẻ dòng thứ hai và không bao giờ nổ 500 vì lỗi unique. Bên gọi
/// vì vậy phải lấy kết quả từ <c>ServiceResult.Data.Result</c> (kết quả đã ghi đứng vững), đừng lấy
/// kết quả vừa tự tính lại — hai lần gọi cùng một lượt phải trả về cùng một câu trả lời.
/// </para>
/// </summary>
public interface ITicketValidationRecorder
{
    /// <summary>
    /// Ghi nhật ký một lượt soát. Xem hợp đồng trên interface.
    /// </summary>
    /// <param name="request">Dữ liệu lượt soát — kết quả do bên gọi tính sẵn.</param>
    /// <param name="validatedByUserId">Tài xế/phụ xe đang đăng nhập (claim NameIdentifier) — bảng
    /// kiểm toán nên cột này bắt buộc, không có đường ghi hộ.</param>
    /// <param name="ct">Token huỷ của request.</param>
    /// <returns>
    /// Thành công: dòng nhật ký — dòng VỪA ghi, hoặc dòng đã ghi từ lượt gửi trước cùng
    /// <c>ClientScanId</c>.
    /// <see cref="ServiceErrorKind.Invalid"/>: chuỗi QR rỗng/quá dài, cặp (TicketId, Result) lệch bất
    /// biến, hoặc ScannedAt ngoài khoảng hợp lý — lỗi theo trường ở <c>Errors</c> để màn quét biết
    /// lượt nào không nên thử lại (docs/39 §3).
    /// </returns>
    Task<ServiceResult<TicketValidation>> RecordAsync(
        RecordTicketValidationRequest request, Guid validatedByUserId, CancellationToken ct);
}
