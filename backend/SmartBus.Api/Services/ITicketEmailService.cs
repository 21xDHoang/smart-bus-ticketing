namespace SmartBus.Api.Services;

/// <summary>
/// Hợp đồng "gửi vé điện tử qua email": sinh PDF vé rồi gửi kèm thư (task dòng 42 — *"Sinh file PDF
/// vé có mã QR + gửi kèm qua email"*, US 4, Sprint 3).
///
/// Ghép hai việc rời thành MỘT lời gọi cho luồng phát hành vé (Hiếu — docs/31 §5):
///
///     await ticketEmailService.SendTicketAsync(model, user.Email!, user.FullName);
///
/// ⚠️ Bên gọi PHẢI bọc try/catch: vé đã phát hành xong rồi (đã ghi CSDL, đã có mã QR) thì gửi
/// email hỏng — hộp thư chết, mạng đứt — KHÔNG được làm hỏng việc phát hành. Nuốt lỗi ở đây là
/// quyết định của bên gọi (kèm log), không phải của service này: docs/31.
/// </summary>
public interface ITicketEmailService
{
    /// <summary>
    /// Sinh PDF vé từ <paramref name="ticket"/> (đúng vé đó, đúng mã QR đã lưu) và gửi tới
    /// <paramref name="toAddress"/> kèm thư mời dạng HTML tiếng Việt.
    /// Ném exception nếu sinh PDF hoặc gửi thư thất bại.
    /// </summary>
    Task SendTicketAsync(
        TicketPdfModel ticket,
        string toAddress,
        string? toName = null,
        CancellationToken cancellationToken = default);
}
