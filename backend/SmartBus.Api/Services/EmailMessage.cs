namespace SmartBus.Api.Services;

/// <summary>
/// Một email cần gửi — dữ liệu thuần, không dính MailKit (task dòng 42, US 4, Sprint 3).
/// Nhờ vậy tầng soạn thư (<see cref="TicketEmailService"/>) test được bằng đồ giả
/// <see cref="IEmailSender"/> mà không cần SMTP, còn tầng gửi (<see cref="SmtpEmailSender"/>) là
/// chỗ DUY NHẤT biết MailKit tồn tại.
/// </summary>
public sealed record EmailMessage
{
    /// <summary>Địa chỉ người nhận.</summary>
    public required string ToAddress { get; init; }

    /// <summary>Tên người nhận — có thì hiện kèm địa chỉ, không có thì chỉ hiện địa chỉ.</summary>
    public string? ToName { get; init; }

    /// <summary>Tiêu đề email — tiếng Việt có dấu thoải mái, MimeKit tự mã hoá UTF-8 đúng chuẩn.</summary>
    public required string Subject { get; init; }

    /// <summary>Thân email dạng HTML. Bên soạn thư phải tự HtmlEncode mọi giá trị người dùng nhập
    /// (tên hành khách, tên tuyến…) — nội dung này chèn thẳng vào HTML.</summary>
    public required string HtmlBody { get; init; }

    /// <summary>
    /// Thân dạng chữ thuần — bản thay thế cho trình đọc thư không hiện HTML. Không bắt buộc, nhưng
    /// email chỉ có HTML thuần bị bộ lọc thư rác đánh giá thấp; vé là thư giao dịch, càng ít cớ
    /// rơi vào hộp thư rác càng tốt.
    /// </summary>
    public string? PlainTextBody { get; init; }

    /// <summary>Tệp đính kèm (vé PDF). Mặc định rỗng — thư không đính kèm vẫn gửi được.</summary>
    public IReadOnlyList<EmailAttachment> Attachments { get; init; } = [];
}

/// <summary>Một tệp đính kèm trong <see cref="EmailMessage"/>.</summary>
public sealed record EmailAttachment
{
    /// <summary>
    /// Tên tệp hiện cho người nhận. Nên là ASCII không dấu (vé dùng "ve-dien-tu-xxxxxxxx.pdf"):
    /// một số ứng dụng thư cũ hiển thị sai tên có dấu, và tên này còn đi vào đường lưu tệp trên
    /// máy người nhận.
    /// </summary>
    public required string FileName { get; init; }

    /// <summary>Nội dung tệp — với vé là mảng byte PDF từ <see cref="ITicketPdfService"/>.</summary>
    public required byte[] Content { get; init; }

    /// <summary>MIME type — mặc định application/pdf (đúng cho vé; đổi khi có loại đính kèm khác).</summary>
    public string ContentType { get; init; } = "application/pdf";
}
