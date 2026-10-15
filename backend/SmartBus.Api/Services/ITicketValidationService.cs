using SmartBus.Api.Dtos.TicketValidations;

namespace SmartBus.Api.Services;

/// <summary>
/// Soát một mã QR vé — POST /api/tickets/validate (US 15 "Soát vé QR", Sprint 4, task *"API soát
/// vé: nhận mã QR, xác thực chữ ký, trả kết quả hợp lệ/không"* — Trần Trung Hiếu, dòng 15, cộng
/// dòng 19 *"Phân quyền: chỉ tài xế/phụ xe được soát vé"*). Hợp đồng đầy đủ ở mục "Soát vé QR" của
/// docs/api-contract.md; luồng bắt buộc ghi ở docs/34 §6 và docs/39 §4.
///
/// <para>
/// <b>Luồng ba bước, thứ tự cố định:</b>
/// 1. <see cref="ITicketQrService.TryVerify"/> xác thực chữ ký TRƯỚC — đừng tin <c>ticketId</c>
///    nhúng trong mã trước khi chữ ký khớp (docs/30 §3.2). Chữ ký sai và mã rác trả về CÙNG một
///    <c>NotFound</c>, không phân biệt ra ngoài (docs/34 §3).
/// 2. Tra <c>Tickets</c> theo Id đã ký, rồi xét trạng thái vé / đối chiếu chuyến / hết hạn để tính
///    <see cref="Entities.TicketValidationResult"/>.
/// 3. Gọi <see cref="ITicketValidationRecorder.RecordAsync"/> ghi nhật ký — MỌI lượt quét đều được
///    ghi, kể cả lượt bị từ chối (docs/34 §2.2).
/// </para>
///
/// <para>
/// <b>Vì sao endpoint KHÔNG lật <c>Ticket.Status = Used</c>:</b> việc đó thuộc dòng 16 *"API chống
/// soát trùng: đánh dấu vé đã sử dụng theo chuyến"* (Nguyễn Duy Kiên) — recorder và endpoint đều
/// không đụng trạng thái vé (docs/39 §5). Cho tới khi dòng 16 có, hai lượt soát cùng một vé còn
/// <c>Paid</c> đều trả <c>Valid</c> và mỗi lượt ghi một dòng nhật ký — ranh giới đã biết.
/// </para>
///
/// <para>
/// <b>Kết quả trả về là kết quả ĐÃ GHI, không phải kết quả vừa tính</b> (docs/39 §4.2): lượt gửi
/// lại cùng <c>clientScanId</c> phải trả về đúng câu trả lời của lần đầu, kể cả khi trạng thái vé
/// đã đổi từ lần đó.
/// </para>
/// </summary>
public interface ITicketValidationService
{
    /// <summary>
    /// Soát một lượt quét QR.
    /// </summary>
    /// <param name="request">Chuỗi mã QR + chuyến đang chạy + hai trường ngoại tuyến tuỳ chọn.</param>
    /// <param name="validatedByUserId">Tài xế/phụ xe đang đăng nhập — ghi vào
    /// <c>TicketValidations.ValidatedByUserId</c>, bảng kiểm toán nên bắt buộc.</param>
    /// <param name="cancellationToken">Token huỷ của request.</param>
    /// <returns>
    /// Thành công (mọi kết quả soát, kể cả <c>NotFound</c>): dòng nhật ký đã ghi và câu trả lời
    /// lấy từ <see cref="ServiceResult{T}.Data"/>.
    /// <see cref="ServiceErrorKind.NotFound"/>: <c>tripId</c> trỏ chuyến không tồn tại — lỗi phía
    /// gọi, không phải kết quả soát.
    /// <see cref="ServiceErrorKind.Invalid"/>: recorder từ chối lượt ghi (mã rỗng/quá dài,
    /// <c>scannedAt</c> ngoài cửa sổ) — 400 kèm lỗi theo trường, docs/39 §3.5.
    /// </returns>
    Task<ServiceResult<TicketValidationResponse>> ValidateAsync(
        ValidateTicketRequest request,
        Guid validatedByUserId,
        CancellationToken cancellationToken = default);
}
