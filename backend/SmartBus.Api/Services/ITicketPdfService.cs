namespace SmartBus.Api.Services;

/// <summary>
/// Hợp đồng sinh file PDF vé có mã QR (task dòng 42 — *"Sinh file PDF vé có mã QR + gửi kèm qua
/// email"*, US 4, Sprint 3).
///
/// Một phép THUẦN: model vào, mảng byte PDF ra. Không CSDL, không mạng, không đọc file ngoài
/// assembly (font Roboto nhúng trong csproj), không phụ thuộc thời gian chạy — nên test không cần
/// hạ tầng gì và kết quả in ra giống nhau giữa máy dev với CI.
///
/// QR vẽ từ <see cref="TicketPdfModel.Code"/> — mã **đã lưu** trong <c>Tickets.Code</c>, không
/// sinh lại (lý do: docs/30 §3.1 — mỗi lượt sinh ra một mã khác).
/// </summary>
public interface ITicketPdfService
{
    /// <summary>Sinh PDF vé (khổ A5) từ dữ liệu đã gom sẵn. Ném <see cref="ArgumentException"/>
    /// nếu thiếu mã QR — vé không có mã để soát là vé vô dụng, hỏng phải lộ ra ngay chứ không in
    /// ra tấm vé trắng.</summary>
    byte[] GeneratePdf(TicketPdfModel model);
}
