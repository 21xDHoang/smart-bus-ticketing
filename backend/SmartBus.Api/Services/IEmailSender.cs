namespace SmartBus.Api.Services;

/// <summary>
/// Hợp đồng gửi MỘT email (task dòng 42 — *"Sinh file PDF vé có mã QR + gửi kèm qua email"*, US 4,
/// Sprint 3).
///
/// Tách khỏi <see cref="ITicketEmailService"/> có chủ đích: tầng "gửi thư" (SMTP) và tầng "soạn
/// thư vé" (nội dung + đính kèm PDF) đổi vì lý do khác nhau. Test soạn thư dùng đồ giả thay
/// <see cref="IEmailSender"/> nên không cần SMTP thật; đổi nhà cung cấp gửi thư sau này chỉ đụng
/// một cài đặt.
///
/// Ném exception khi gửi hỏng (SMTP từ chối, mạng đứt) — service KHÔNG tự nuốt lỗi. Bên gọi
/// (luồng phát hành vé) quyết định: vé đã phát hành thì gửi email hỏng KHÔNG được làm hỏng việc
/// phát hành — bọc try/catch ở đó, xem docs/31.
/// </summary>
public interface IEmailSender
{
    /// <summary>Gửi một email (kèm đính kèm nếu có). Ném exception nếu gửi thất bại.</summary>
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}
