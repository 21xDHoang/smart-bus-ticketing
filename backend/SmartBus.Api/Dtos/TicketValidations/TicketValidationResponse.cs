namespace SmartBus.Api.Dtos.TicketValidations;

/// <summary>
/// Kết quả một lượt soát vé — response của POST /api/tickets/validate.
///
/// "Vé không hợp lệ" cũng là câu TRẢ LỜI thành công, không phải lỗi HTTP: mọi kết quả soát (kể cả
/// <c>NotFound</c>) trả 200 — cùng lối POST /vouchers/validate trả <c>valid: false</c> kèm lý do
/// (docs/29 §3). 400 chỉ dành cho request sai khuôn hoặc bị service ghi nhật ký từ chối (mã rỗng,
/// <c>scannedAt</c> ngoài cửa sổ — docs/39 §3.5).
///
/// <see cref="Reason"/> ánh xạ 1–1 từ <see cref="Entities.TicketValidationResult"/> (docs/34 §3):
/// <c>Valid</c> → <c>valid = true, reason = null</c>; năm giá trị còn lại → <c>valid = false,
/// reason = &lt;tên giá trị&gt;</c>. Năm lý do đã chốt với màn quét QR ở
/// <c>frontend/src/api/ticketValidationApi.ts</c> — thêm giá trị là đổi hợp đồng với frontend.
/// </summary>
public class TicketValidationResponse
{
    /// <summary>Vé được lên xe hay không.</summary>
    public bool Valid { get; init; }

    /// <summary>
    /// Lý do từ chối khi <see cref="Valid"/> là false —
    /// <c>NotFound</c> / <c>Cancelled</c> / <c>AlreadyUsed</c> / <c>WrongTrip</c> / <c>Expired</c>.
    /// Null khi hợp lệ.
    /// </summary>
    public string? Reason { get; init; }

    /// <summary>Vé soát được — null khi <c>reason = "NotFound"</c> (không tra ra vé).</summary>
    public ValidatedTicketResponse? Ticket { get; init; }
}
