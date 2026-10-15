using System.ComponentModel.DataAnnotations;

namespace SmartBus.Api.Dtos.TicketValidations;

/// <summary>
/// Body của POST /api/tickets/validate — một LƯỢT SOÁT VÉ (US 15 "Soát vé QR", Sprint 4, task
/// *"API soát vé: nhận mã QR, xác thực chữ ký, trả kết quả hợp lệ/không"* — Trần Trung Hiếu, dòng
/// 15). Hợp đồng đầy đủ ở mục "Soát vé QR — POST /tickets/validate" của docs/api-contract.md.
///
/// Hai trường cuối (<see cref="ClientScanId"/> / <see cref="ScannedAt"/>) là mảnh của hàng đợi
/// ngoại tuyến (dòng 18, Phùng Duy Hoàng) — đặc tả ở docs/39-hang-doi-ngoai-tuyen-soat-ve.md §4.1:
/// vắng cả hai = lượt online kiểu cũ (server tự lấy giờ, không chống trùng); có
/// <see cref="ClientScanId"/> = lượt có thể được gửi lại, server chống trùng theo nó và trả về kết
/// quả ĐÃ GHI của lần đầu.
/// </summary>
public class ValidateTicketRequest
{
    /// <summary>
    /// NGUYÊN VĂN chuỗi mã QR máy quét đọc được — định dạng <c>SBT1:{ticketId}.{blob}</c> (106 ký
    /// tự, docs/30). Server tự xác thực chữ ký, máy quét không gửi gì thêm.
    ///
    /// <see cref="RequiredAttribute"/> bắt CẢ chuỗi rỗng/toàn khoảng trắng — thiếu hay rỗng đều là
    /// 400 với lỗi theo trường <c>code</c>. Cố ý KHÔNG khai <c>StringLength(200)</c>: chuỗi quá dài
    /// là lỗi của bản ghi nhật ký và do <see cref="Services.TicketValidationRecorder"/> chặn (lỗi
    /// theo trường <c>scannedCode</c>, docs/39 §3.5), để mọi phép kiểm độ dài nằm ở đúng một chỗ
    /// thay vì hai nơi trả hai câu khác nhau.
    /// </summary>
    [Required(ErrorMessage = "Mã QR không được để trống")]
    public string? Code { get; init; }

    /// <summary>
    /// Chuyến phụ xe ĐANG CHẠY lúc quét — nguồn duy nhất kết luận được <c>WrongTrip</c>. Tuỳ chọn
    /// theo hợp đồng (docs/29 §3); thiếu hẳn thì endpoint bỏ qua phép đối chiếu chuyến.
    ///
    /// Chuyến KHÔNG tồn tại là 404 — lỗi phía gọi, khác hẳn "vé không hợp lệ" (cùng lối
    /// POST /vouchers/validate).
    /// </summary>
    public Guid? TripId { get; init; }

    /// <summary>
    /// Mã lượt soát do CHÍNH MÁY QUÉT sinh NGAY LÚC QUÉT (UUID v4) — khoá chống trùng khi hàng đợi
    /// ngoại tuyến đồng bộ lại (docs/39 §3.2). Null = lượt online, gửi thẳng một lần.
    /// </summary>
    public Guid? ClientScanId { get; init; }

    /// <summary>
    /// Giờ MÁY QUÉT ghi nhận lượt soát (ISO-8601, kèm offset hoặc hậu tố Z). Null = server lấy giờ
    /// nhận request. Có giá trị thì recorder kiểm cửa sổ hợp lý (không quá 5 phút tương lai, không
    /// quá 72 giờ quá khứ — docs/34 §4.2) trước khi nhận.
    /// </summary>
    public DateTime? ScannedAt { get; init; }
}
