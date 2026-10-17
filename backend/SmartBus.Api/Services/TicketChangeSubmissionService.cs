using Microsoft.EntityFrameworkCore;
using SmartBus.Api.Data;
using SmartBus.Api.Dtos.TicketRequests;
using SmartBus.Api.Entities;

namespace SmartBus.Api.Services;

/// <summary>
/// Bản cài đặt của <see cref="ITicketChangeSubmissionService"/> — xem interface để có bức tranh
/// nghiệp vụ và ba điều hợp đồng bắt buộc. Ở đây chỉ nói những gì KHÔNG hiện ra từ chữ ký hàm.
///
/// <b>Vì sao cả bốn câu 409 đều là 409 chứ không phải 400:</b> yêu cầu khách gửi lên luôn hợp lệ về
/// hình thức — chỉ có trạng thái thế giới đã đổi kể từ lúc khách mở màn hình (vé bị soát ở cổng,
/// người khác mua mất ghế, quản lý đã duyệt một yêu cầu khác). Đây đúng loại lỗi mà
/// <see cref="ServiceErrorKind.Conflict"/> sinh ra để nói: gửi lại y nguyên sẽ không bao giờ thành
/// công, khách phải làm một việc khác.
/// </summary>
public sealed class TicketChangeSubmissionService : ITicketChangeSubmissionService
{
    private const string TicketNotFoundMessage = "Không tìm thấy vé.";

    private const string TicketNotPaidMessage =
        "Vé này không còn hiệu lực để đổi (đã bị huỷ, hoặc đã được soát vé ở cổng).";

    /// <summary>
    /// Dùng chung cho cả hai đường dẫn tới cùng một trạng thái: (a) khách đã có yêu cầu huỷ hoặc đổi
    /// đang chờ trên vé này, hoặc (b) hai tab cùng bấm gửi và lượt thua đâm chỉ mục duy nhất điều
    /// kiện. Màn hình chỉ cần biết *vé đang có yêu cầu chờ*, không cần biết ai gửi trước.
    /// </summary>
    private const string PendingRequestMessage =
        "Vé này đang có yêu cầu huỷ hoặc đổi chờ duyệt. Vui lòng chờ quản lý xử lý trước khi gửi yêu cầu mới.";

    private const string TripNotFoundMessage = "Không tìm thấy chuyến xe.";

    /// <summary>
    /// Gộp "đã huỷ / đã chạy xong" (cột <c>Status</c>) với "giờ khởi hành đã qua" (cột
    /// <c>DepartureTime</c>): trạng thái chuyến được job nền cập nhật, nên một chuyến vừa qua giờ
    /// chạy vài giây vẫn có thể còn ghi <c>Scheduled</c>. Kiểm cả hai vế là cách duy nhất không phát
    /// hành vé cho một chuyến không còn chỗ để đổi sang.
    /// </summary>
    private const string TripNotReadyMessage =
        "Chuyến khách muốn đổi sang đã khởi hành hoặc không còn nhận đặt vé. Hãy chọn chuyến khác.";

    private const string SeatNotFoundMessage = "Không tìm thấy ghế trong chuyến đã chọn.";

    private const string SeatTakenMessage = "Ghế đã có người đặt. Hãy chọn ghế khác.";

    private const string FareNotFoundMessage =
        "Tuyến của chuyến khách muốn đổi sang chưa có giá vé. Hãy chọn chuyến khác.";

    /// <summary>
    /// Giá trị của <see cref="TicketRequestResponse.Type"/> — cùng hằng và cùng nghĩa với bản sao
    /// trong <see cref="TicketRequestReviewService"/>. Đây là TÊN BẢNG chứ không phải dữ liệu nên
    /// không nằm trong entity; hai service tự giữ hằng riêng thay vì kéo service duyệt vào phụ thuộc
    /// (file của Hoàng — luật E1 không cho sửa để tách một lớp dùng chung).
    /// </summary>
    private const string ChangeType = "Change";

    private readonly AppDbContext _db;

    public TicketChangeSubmissionService(AppDbContext db) => _db = db;

    public async Task<ServiceResult<TicketRequestResponse>> SubmitAsync(
        CreateTicketChangeRequest request,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var ticketId = request.TicketId.GetValueOrDefault();
        var newTripId = request.NewTripId.GetValueOrDefault();
        var newSeatId = request.NewSeatId.GetValueOrDefault();

        // Sở hữu vé nằm TRONG truy vấn, không kiểm ở controller — cùng lối MyTicketsService. Vé của
        // người khác trả 404, không phải 403: 403 nói "vé này có thật nhưng không phải của bạn",
        // tức là tiết lộ sự tồn tại của vé (docs/28 §6).
        var ticket = await _db.Tickets
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == ticketId && t.UserId == userId, cancellationToken);

        if (ticket is null)
        {
            return ServiceResult<TicketRequestResponse>.NotFound(TicketNotFoundMessage);
        }

        if (ticket.Status != TicketStatus.Paid)
        {
            return ServiceResult<TicketRequestResponse>.Conflict(TicketNotPaidMessage);
        }

        // 🔴 docs/32 §4.4 việc 1 — ràng buộc LIÊN BẢNG, chỉ mục duy nhất điều kiện không với tới
        // bảng kia. Phải hỏi CẢ HAI: một vé vừa có yêu cầu huỷ vừa có yêu cầu đổi đang chờ thì
        // duyệt cả hai sẽ huỷ vé lần thứ hai và phát hành vé mới từ một vé đã chết.
        var hasPendingCancellation = await _db.CancellationRequests.AnyAsync(
            c => c.TicketId == ticket.Id && c.Status == CancellationRequestStatus.Pending,
            cancellationToken);

        var hasPendingChange = await _db.TicketChanges.AnyAsync(
            c => c.TicketId == ticket.Id && c.Status == TicketChangeStatus.Pending,
            cancellationToken);

        if (hasPendingCancellation || hasPendingChange)
        {
            return ServiceResult<TicketRequestResponse>.Conflict(PendingRequestMessage);
        }

        var newTrip = await _db.Trips
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == newTripId, cancellationToken);

        if (newTrip is null)
        {
            return ServiceResult<TicketRequestResponse>.NotFound(TripNotFoundMessage);
        }

        // Vé mới in QR được và là vé thật — nhận đổi sang một chuyến đã huỷ hoặc đã qua giờ chạy là
        // hứa bán một chỗ không tồn tại. Cùng phép kiểm lượt duyệt phải làm lại (docs/32 §3.3).
        if (newTrip.Status != TripStatus.Scheduled || newTrip.DepartureTime <= DateTime.UtcNow)
        {
            return ServiceResult<TicketRequestResponse>.Conflict(TripNotReadyMessage);
        }

        // Ghế phải thuộc XE CỦA CHUYẾN MỚI: cùng một id ghế ở xe khác là ghế không tồn tại trên sơ
        // đồ của chuyến này. Trả 404 như một id sai thay vì 400 — không có tham số hợp lệ nào để
        // "sửa" một id ghế thuộc xe khác.
        var newSeat = await _db.Seats
            .AsNoTracking()
            .FirstOrDefaultAsync(
                s => s.Id == newSeatId && s.BusId == newTrip.BusId,
                cancellationToken);

        if (newSeat is null)
        {
            return ServiceResult<TicketRequestResponse>.NotFound(SeatNotFoundMessage);
        }

        // Phép kiểm MÔ TẢ cho khách — lưới thật là phép kiểm lại ở lượt duyệt, vì ghế mới KHÔNG
        // được giữ ở đây (docs/32 §3.3). Vẫn phải làm: để khách gửi một ghế đã có người là hẹn một
        // lượt từ chối chắc chắn.
        if (await IsSeatTakenAsync(newTrip.Id, newSeat.Id, cancellationToken))
        {
            return ServiceResult<TicketRequestResponse>.Conflict(SeatTakenMessage);
        }

        // Giá phổ thông của tuyến — CÙNG nguồn và CÙNG bộ lọc với TripSearchService và
        // TripSeatMapService (Standard, một dòng cho mỗi cặp tuyến/đối tượng). Chụp lại ngay đây:
        // bảng giá sửa được, nên tra lại lúc duyệt sẽ ra con số khác con số khách đã đồng ý.
        var newPrice = await _db.Fares
            .AsNoTracking()
            .Where(f => f.RouteId == newTrip.RouteId && f.PassengerType == PassengerType.Standard)
            .Select(f => (decimal?)f.Price)
            .FirstOrDefaultAsync(cancellationToken);

        if (newPrice is null)
        {
            // Không có giá nghĩa là không có con số chênh lệch nào để khách đồng ý trước khi gửi.
            // Ghi yêu cầu với NewPrice = 0 sẽ là bịa ra một mức giá không tồn tại.
            return ServiceResult<TicketRequestResponse>.Conflict(FareNotFoundMessage);
        }

        var now = DateTime.UtcNow;
        var change = new TicketChange
        {
            // Id do entity tự sinh (Guid.NewGuid) — cùng khuôn mọi entity của dự án.
            TicketId = ticket.Id,
            UserId = userId,
            NewTripId = newTrip.Id,
            NewSeatId = newSeat.Id,
            NewPrice = newPrice.Value,
            Reason = NormalizeReason(request.Reason),
            // Dòng mới luôn Pending — không có trạng thái trung gian nào khác ở nửa gửi.
            Status = TicketChangeStatus.Pending,
            CreatedAt = now,
        };

        // KHÔNG đụng dòng vé và KHÔNG tạo SeatHold — xem 🔴 điều 1 và 2 ở interface.
        _db.TicketChanges.Add(change);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Lưới thật cho nhánh "hai tab cùng gửi": chỉ mục duy nhất điều kiện
            // IX_TicketChanges_TicketId_Pending chỉ cho một dòng Pending trên mỗi vé. Hai lượt cùng
            // đọc "chưa có yêu cầu nào" rồi cùng ghi là chuyện xảy ra được — lượt thua đâm chỉ mục
            // và phải ra 409 (tải lại đi), không phải 500. Cùng lối TicketRequestReviewService.
            return ServiceResult<TicketRequestResponse>.Conflict(PendingRequestMessage);
        }

        return ServiceResult<TicketRequestResponse>.Ok(ToResponse(change, ticket));
    }

    /// <summary>
    /// Ghế của chuyến mới đã có người chưa — hỏi CẢ HAI nguồn đang chiếm ghế: <c>SeatHolds</c>
    /// (chỉ dòng <c>Holding</c> mới chặn, đúng điều kiện partial index) và <c>Tickets</c> (chỉ vé
    /// <c>Paid</c>; vé <c>Used</c>/<c>Cancelled</c> đã thả ghế theo A6).
    ///
    /// Chép nguyên logic của bản song sinh trong <see cref="TicketRequestReviewService"/> thay vì
    /// gọi sang: helper đó là private và file đó của Hoàng (luật E1). Hai bản phải giữ CÙNG bộ lọc —
    /// lệch một chữ là phép kiểm lúc gửi và phép kiểm lúc duyệt nói hai chuyện khác nhau.
    /// </summary>
    private async Task<bool> IsSeatTakenAsync(Guid tripId, Guid seatId, CancellationToken cancellationToken)
    {
        var heldBySomeone = await _db.SeatHolds.AnyAsync(
            h => h.TripId == tripId && h.SeatId == seatId && h.Status == SeatHoldStatus.Holding,
            cancellationToken);

        if (heldBySomeone)
        {
            return true;
        }

        return await _db.Tickets.AnyAsync(
            t => t.TripId == tripId && t.SeatId == seatId && t.Status == TicketStatus.Paid,
            cancellationToken);
    }

    /// <summary>
    /// Cắt khoảng trắng thừa; chuỗi rỗng / toàn khoảng trắng là KHÔNG NÊU LÝ DO (<c>null</c>). Để
    /// nguyên <c>"   "</c> thì cột có giá trị mà màn hình hiện một ô trống — cùng lối
    /// <see cref="TicketRequestReviewService"/>.
    /// </summary>
    private static string? NormalizeReason(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    /// <summary>
    /// Dòng vừa tạo — đúng hình dạng chung của bốn endpoint US 5. Trường của loại "Cancel" có mặt
    /// và <c>null</c>, KHÔNG lược khỏi JSON (hợp đồng chốt một khuôn để màn theo dõi của khách đọc
    /// được cả hai loại). Các trường duyệt để <c>null</c> vì dòng còn <c>Pending</c>.
    /// </summary>
    private static TicketRequestResponse ToResponse(TicketChange change, Ticket ticket)
        => new()
        {
            Id = change.Id,
            Type = ChangeType,
            TicketId = ticket.Id,
            TicketCode = ticket.Code,
            Status = change.Status.ToString(),
            // Giá vé GỐC chụp lúc phát hành — không tra lại Fares.
            Price = ticket.Price,
            Reason = change.Reason,
            CancellationFee = null,
            RefundAmount = null,
            NewTripId = change.NewTripId,
            NewSeatId = change.NewSeatId,
            NewPrice = change.NewPrice,
            // Chênh lệch SUY RA, CÓ DẤU: dương = khách trả thêm, âm = nhà xe hoàn lại, 0 = ngang
            // giá. Hai vế bất biến nên không cần (và không được) thêm cột.
            PriceDifference = change.NewPrice - ticket.Price,
            NewTicketId = null,
            ReviewedByUserId = null,
            ReviewedAt = null,
            RejectReason = null,
            CreatedAt = change.CreatedAt,
            UpdatedAt = null,
        };
}
