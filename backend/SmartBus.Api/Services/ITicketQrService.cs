namespace SmartBus.Api.Services;

/// <summary>
/// Sinh và kiểm mã QR của vé điện tử (US 4, Sprint 3, task *"Service sinh mã QR duy nhất + ký số
/// chống làm giả"* — Nguyễn Duy Kiên).
///
/// Hợp đồng hai chiều quanh MỘT chuỗi duy nhất — chính chuỗi in trên QR, cũng là giá trị của cột
/// <c>Tickets.Code</c>:
///   • BÊN PHÁT HÀNH (Hiếu — gọi sau khi tiền về, trước khi lưu vé): <see cref="GenerateCode"/>
///     ra mã rồi lưu vào vé.
///   • BÊN SOÁT VÉ (API dòng 43 / Sprint 4 dòng 15 — docs/29): <see cref="TryVerify"/> kiểm chữ ký
///     trước khi tra bảng theo mã.
///
/// Service KHÔNG chạm CSDL: nó không biết vé có tồn tại hay không, vé thuộc chuyến nào, đã soát
/// chưa — những câu đó thuộc người gọi tra bảng <c>Tickets</c> (docs/28 §2, docs/29 §2). Ở đây chỉ
/// có một câu hỏi: chuỗi này có phải do mình ký ra không, và ký cho vé nào.
/// </summary>
public interface ITicketQrService
{
    /// <summary>
    /// Sinh mã QR cho một vé. Mã DUY NHẤT toàn hệ thống — kể cả hai lượt gọi liên tiếp cho cùng một
    /// <paramref name="ticketId"/> cũng ra hai mã khác nhau (xem <see cref="TicketQrService"/> giải
    /// thích nonce), để bên phát hành có đường lùi khi lượt ghi đâm chỉ mục unique của cột
    /// <c>Code</c>: bắt lỗi rồi gọi lại hàm này là có mã mới (docs/28 §6).
    /// </summary>
    /// <param name="ticketId">Id vé (khoá chính bảng <c>Tickets</c>) — chữ ký gắn cứng vào id này.</param>
    /// <returns>Chuỗi đúng định dạng <c>SBT1:{ticketId}.{chuỗiBase64}</c> (106 ký tự, dưới hạn 200 của cột).</returns>
    string GenerateCode(Guid ticketId);

    /// <summary>
    /// Kiểm một chuỗi có phải mã QR hợp lệ do service này ký ra không (đúng tiền tố phiên bản, đúng
    /// chữ ký HMAC). Khoảng trắng thừa hai đầu được bỏ qua; hàm KHÔNG ném với mọi đầu vào.
    /// </summary>
    /// <param name="code">Nội dung quét từ QR — có thể null/rác.</param>
    /// <param name="ticketId">Id vé nhúng trong mã — chỉ có nghĩa khi hàm trả true, ngược lại là <see cref="Guid.Empty"/>.</param>
    /// <returns>True khi chữ ký khớp; false cho mọi ca còn lại (mã rác, mã bị sửa, ký bằng khoá khác).</returns>
    bool TryVerify(string? code, out Guid ticketId);
}
