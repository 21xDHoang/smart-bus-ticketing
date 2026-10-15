using Microsoft.EntityFrameworkCore;
using SmartBus.Api.Data;
using SmartBus.Api.Dtos.TicketRequests;
using SmartBus.Api.Entities;

namespace SmartBus.Api.Services;

/// <summary>
/// Bản cài đặt của <see cref="ITicketRequestReviewService"/> — xem interface để có bức tranh nghiệp
/// vụ và ba việc bắt buộc. Ở đây chỉ nói những gì KHÔNG hiện ra từ chữ ký hàm.
///
/// <b>Vì sao dùng thẳng <see cref="ITicketRequestResultNotifier"/> chứ không bọc decorator</b> như
/// <c>NotifyingSeatHoldCreateService</c>: lối decorator ở đó tồn tại vì
/// <c>SeatHoldCreateService</c> là file của người khác, mà luật E1 cấm sửa file người khác — nên
/// người thêm thông báo phải bọc từ ngoài. Service này là file mới của chính người viết: không có
/// ai để né, và một lớp bọc chỉ làm chỗ gọi thông báo xa chỗ lật trạng thái.
///
/// <b>Vì sao hai luồng dùng chung một <c>SaveChangesAsync</c> mà không tách transaction riêng:</b>
/// huỷ vé + lật yêu cầu là MỘT sự thật nghiệp vụ — lưu nửa vời thì hoặc vé chết mà yêu cầu còn
/// <c>Pending</c> (quản lý bấm lại, lần hai ra 409 và không ai hiểu vì sao), hoặc yêu cầu
/// <c>Approved</c> mà vé vẫn sống (khách đi được bằng vé đã được hoàn tiền). Một lượt lưu là lượt
/// duy nhất cả hai cùng sống hoặc cùng chết.
/// </summary>
public sealed class TicketRequestReviewService : ITicketRequestReviewService
{
    private const string CancellationNotFoundMessage = "Không tìm thấy yêu cầu huỷ vé.";
    private const string TicketChangeNotFoundMessage = "Không tìm thấy yêu cầu đổi vé.";

    /// <summary>
    /// Dùng chung cho HAI ca khác nhau của cùng một chuyện: quản lý khác vừa bấm xong, hoặc chính
    /// quản lý này bấm hai lần (double-click, F5 rồi gửi lại). Cả hai đều là "yêu cầu không còn ở
    /// hàng chờ nữa" — gộp một câu vì màn hình chỉ cần biết *tải lại đi*, không cần biết ai.
    /// </summary>
    private const string AlreadyReviewedMessage =
        "Yêu cầu đã được người khác xử lý. Hãy tải lại để xem kết quả mới nhất.";

    private const string InvalidStatusMessage =
        "Trạng thái không hợp lệ. Chỉ nhận \"Approved\" hoặc \"Rejected\".";

    private const string TicketNotPaidMessage =
        "Vé của yêu cầu này không còn hiệu lực để huỷ hoặc đổi (đã bị huỷ, hoặc đã được soát vé ở cổng).";

    private const string NewTripNotReadyMessage =
        "Chuyến khách xin đổi sang đã bị huỷ hoặc đã khởi hành. Hãy từ chối yêu cầu và mời khách chọn chuyến khác.";

    private const string NewSeatTakenMessage =
        "Ghế khách xin đổi sang đã có người đặt trong lúc chờ duyệt. Hãy từ chối yêu cầu và mời khách chọn ghế khác.";

    /// <summary>
    /// Hai giá trị của <see cref="TicketRequestResponse.Type"/>. Đây là TÊN BẢNG chứ không phải dữ
    /// liệu — xem doc của trường đó — nên là hằng ở tầng service, không phải enum trong entity.
    /// </summary>
    private const string CancelType = "Cancel";

    private const string ChangeType = "Change";

    private readonly AppDbContext _db;
    private readonly ITicketQrService _ticketQrService;
    private readonly ITicketRequestResultNotifier _notifier;

    public TicketRequestReviewService(
        AppDbContext db,
        ITicketQrService ticketQrService,
        ITicketRequestResultNotifier notifier)
    {
        _db = db;
        _ticketQrService = ticketQrService;
        _notifier = notifier;
    }

    public async Task<ServiceResult<TicketRequestResponse>> ReviewCancellationAsync(
        Guid id,
        UpdateTicketRequestStatusRequest request,
        Guid reviewerUserId,
        CancellationToken cancellationToken)
    {
        if (!TryParseDecision(request.Status, out var decision))
        {
            return InvalidStatus();
        }

        var cancellation = await _db.CancellationRequests
            .Include(c => c.Ticket)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

        if (cancellation is null)
        {
            return ServiceResult<TicketRequestResponse>.NotFound(CancellationNotFoundMessage);
        }

        if (cancellation.Status != CancellationRequestStatus.Pending)
        {
            return ServiceResult<TicketRequestResponse>.Conflict(AlreadyReviewedMessage);
        }

        // FK TicketId là bắt buộc (Restrict) nên Include luôn ra vé — dấu ! chỉ để yên trình biên
        // dịch, không phải một nhánh có thật.
        var ticket = cancellation.Ticket!;
        var now = DateTime.UtcNow;

        if (decision == Decision.Rejected)
        {
            // Nhánh từ chối KHÔNG đụng dòng vé: vé giữ Paid, ghế vẫn của khách, không khoản tiền
            // nào di chuyển. RejectReason của yêu cầu đang Pending vốn đã null nên không cần xoá.
            cancellation.Status = CancellationRequestStatus.Rejected;
            cancellation.RejectReason = NormalizeRejectReason(request.RejectReason);
        }
        else
        {
            // Vé phải CÒN Paid ngay lúc này. Hai đường dẫn tới đây: (a) khách gửi một yêu cầu huỷ
            // VÀ một yêu cầu đổi trên cùng vé — hai bảng có chỉ mục riêng nên CSDL không chặn được
            // ca này, Dăm đã ghi rõ luật liên bảng là việc của service; duyệt cả hai thì lượt thứ
            // hai hoàn tiền cho một vé đã chết. (b) vé đã bị soát ở cổng trong lúc chờ duyệt —
            // hoàn tiền cho chuyến khách đã đi là mất tiền thật.
            if (ticket.Status != TicketStatus.Paid)
            {
                return ServiceResult<TicketRequestResponse>.Conflict(TicketNotPaidMessage);
            }

            cancellation.Status = CancellationRequestStatus.Approved;

            // Vé chỉ chết ở BƯỚC NÀY, không phải lúc khách gửi yêu cầu — gửi yêu cầu không làm mất
            // ghế của khách. A6 giải phóng ghế khi vé rời khỏi Paid (partial index chỉ giữ
            // Held/Paid), nên ghế về chợ ngay sau lượt lưu.
            ticket.Status = TicketStatus.Cancelled;
            ticket.UpdatedAt = now;

            // KHÔNG ghi dòng hoàn tiền nào: bảng Refunds thuộc US 8, chưa tồn tại. Số tiền hoàn là
            // trường suy ra ở response. Viết trước một bảng không có là bịa ra nguồn sự thật thứ hai.
        }

        cancellation.ReviewedByUserId = reviewerUserId;
        cancellation.ReviewedAt = now;
        cancellation.UpdatedAt = now;

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Cột xmin của dòng yêu cầu đã đổi giữa lúc đọc và lúc ghi — quản lý khác vừa duyệt
            // xong. Đây là 409 chứ không phải 500, và cũng không phải lượt duyệt thành công: câu
            // trả về phải nói cho người dùng biết tải lại (docs/32 §4.3).
            return ServiceResult<TicketRequestResponse>.Conflict(AlreadyReviewedMessage);
        }

        // Thông báo SAU khi đã lưu: gọi trước mà lượt lưu hỏng thì khách nhận tin "đã huỷ vé" cho
        // một vé còn nguyên. Notifier tự nuốt lỗi (bảng Notifications hỏng không được làm hỏng
        // nghiệp vụ đã commit) nên không cần bọc try ở đây.
        await _notifier.NotifyCancellationReviewedAsync(cancellation);

        return ServiceResult<TicketRequestResponse>.Ok(ToCancellationResponse(cancellation, ticket));
    }

    public async Task<ServiceResult<TicketRequestResponse>> ReviewTicketChangeAsync(
        Guid id,
        UpdateTicketRequestStatusRequest request,
        Guid reviewerUserId,
        CancellationToken cancellationToken)
    {
        if (!TryParseDecision(request.Status, out var decision))
        {
            return InvalidStatus();
        }

        var change = await _db.TicketChanges
            .Include(c => c.Ticket)
            .Include(c => c.NewTrip)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

        if (change is null)
        {
            return ServiceResult<TicketRequestResponse>.NotFound(TicketChangeNotFoundMessage);
        }

        if (change.Status != TicketChangeStatus.Pending)
        {
            return ServiceResult<TicketRequestResponse>.Conflict(AlreadyReviewedMessage);
        }

        var oldTicket = change.Ticket!;
        var now = DateTime.UtcNow;

        if (decision == Decision.Rejected)
        {
            // Không đụng vé, KHÔNG kiểm ghế (không cấp ghế cho ai thì kiểm làm gì — và kiểm ở đây
            // sẽ đẻ ra một lượt từ chối thất bại vì ghế đã có người, trong khi từ chối thì luôn
            // phải thành công được), NewTicketId giữ null.
            change.Status = TicketChangeStatus.Rejected;
            change.RejectReason = NormalizeRejectReason(request.RejectReason);
        }
        else
        {
            if (oldTicket.Status != TicketStatus.Paid)
            {
                return ServiceResult<TicketRequestResponse>.Conflict(TicketNotPaidMessage);
            }

            // Chuyến mới phải còn chạy được. Ghế của một chuyến đã huỷ thì trống thật, nhưng phát
            // hành vé cho nó là bán một chuyến không bao giờ đi — cùng loại lỗi "tin vào phép kiểm
            // lúc khách gửi" mà §3.3 cảnh báo, chỉ khác là ở cột Status của chuyến.
            if (change.NewTrip is { } newTrip
                && newTrip.Status is TripStatus.Cancelled or TripStatus.Completed)
            {
                return ServiceResult<TicketRequestResponse>.Conflict(NewTripNotReadyMessage);
            }

            // 🔴 Việc 1 của docs/32 §6 — ghế mới KHÔNG được giữ lúc khách gửi, nên phải hỏi lại
            // ngay bây giờ. Đây là phép kiểm MÔ TẢ (cho quản lý một câu giải thích được); lưới
            // thật là chỉ mục A6 bên dưới, bắt ở catch DbUpdateException.
            if (await IsSeatTakenAsync(change.NewTripId, change.NewSeatId, cancellationToken))
            {
                return ServiceResult<TicketRequestResponse>.Conflict(NewSeatTakenMessage);
            }

            oldTicket.Status = TicketStatus.Cancelled;
            oldTicket.UpdatedAt = now;

            // 🔴 Việc 2 — đổi vé là HUỶ vé cũ rồi PHÁT HÀNH vé mới, không sửa vé cũ: vé cũ đã in
            // QR và có thể đã gửi email cho khách, sửa nó là để tồn tại hai bản mô tả khác nhau
            // của cùng một con vé.
            var newTicketId = Guid.NewGuid();
            var newTicket = new Ticket
            {
                Id = newTicketId,
                // Chữ ký gắn cứng vào id vé nên phải có id trước khi ký.
                Code = _ticketQrService.GenerateCode(newTicketId),
                TripId = change.NewTripId,
                SeatId = change.NewSeatId,
                // Vé mới thuộc CHÍNH khách cũ — người đi không đổi, chỉ chuyến và ghế đổi.
                UserId = change.UserId,
                // Giá đã chụp lúc khách gửi yêu cầu, KHÔNG tra lại bảng Fares: giá có thể đã đổi
                // giữa lúc gửi và lúc duyệt, và giá khách đã nhìn thấy trên màn hình mới là giá
                // khách chấp nhận trả (docs/api-contract.md).
                Price = change.NewPrice,
                Status = TicketStatus.Paid,
                CreatedAt = now,
                // BoardingStopId / AlightingStopId để trống: chúng thuộc tuyến của chuyến CŨ, gán
                // sang chuyến mới là trỏ vào điểm dừng của tuyến khác. Hai cột này nullable và chỉ
                // để hiển thị nên trống là hợp lệ.
            };

            _db.Tickets.Add(newTicket);
            change.NewTicketId = newTicketId;
            change.Status = TicketChangeStatus.Approved;
        }

        change.ReviewedByUserId = reviewerUserId;
        change.ReviewedAt = now;
        change.UpdatedAt = now;

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // ⚠️ Phải đứng TRƯỚC catch DbUpdateException bên dưới:
            // DbUpdateConcurrencyException là lớp con của DbUpdateException, đảo thứ tự là lượt
            // duyệt trùng bị báo thành "ghế đã có người đặt".
            return ServiceResult<TicketRequestResponse>.Conflict(AlreadyReviewedMessage);
        }
        catch (DbUpdateException)
        {
            // Lưới thật của phép kiểm ghế: chỉ mục A6 IX_Tickets_TripId_SeatId_Active chỉ cho một
            // vé Held/Paid trên mỗi (chuyến, ghế). Hai quản lý duyệt hai yêu cầu khác nhau vào
            // cùng một ghế trong cùng một khoảnh khắc thì phép kiểm mô tả ở trên không thấy nhau —
            // chỉ mục mới là thứ chặn, và ở đây nó phải ra 409 chứ không phải 500.
            //
            // Chỉ mục unique còn lại của bảng Tickets là Code; đâm vào nó gần như không xảy ra
            // (HMAC + nonce ngẫu nhiên) và docs/28 §6 có đường lùi "bắt lỗi rồi ký lại mã" cho
            // bên phát hành — nhưng phân biệt được hai chỉ mục thì phải đọc tên ràng buộc của
            // Postgres, mà bộ test InMemory không sinh ra. Đổi một phép kiểm gần như không bao giờ
            // sai lấy một nhánh phụ thuộc nhà cung cấp là không đáng.
            return ServiceResult<TicketRequestResponse>.Conflict(NewSeatTakenMessage);
        }

        await _notifier.NotifyTicketChangeReviewedAsync(change);

        return ServiceResult<TicketRequestResponse>.Ok(ToTicketChangeResponse(change, oldTicket));
    }

    /// <summary>
    /// Ghế của chuyến mới đã có người chưa — hỏi CẢ HAI bảng đang giữ ghế.
    ///
    /// Hỏi cả hai là bắt buộc: <c>TripSeatMapService</c> dựng sơ đồ ghế CHỈ từ <c>SeatHolds</c>, nên
    /// một ghế đã bán mà lượt giữ chỗ đã <c>Confirmed</c>/hết hạn vẫn hiện "trống" trên sơ đồ (đó là
    /// khoảng trống đã biết của phép kiểm bên đó, không phải file này sửa). Tin vào sơ đồ ở đây là
    /// phát hành vé thứ hai cho cùng một ghế.
    ///
    /// Bộ lọc chép ĐÚNG điều kiện của hai ràng buộc thật, không thêm không bớt: <c>Holding</c> là
    /// điều kiện của partial index trên <c>SeatHolds</c>, <c>Paid</c> là điều kiện của chỉ mục A6
    /// trên <c>Tickets</c>. Lệch một chữ là phép kiểm mô tả và lưới thật nói hai chuyện khác nhau.
    /// Vé <c>Used</c> KHÔNG tính là chiếm ghế — A6 thả ghế khi vé rời <c>Paid</c>, và chuyến đã chạy
    /// xong thì khách cũ không còn ngồi đó nữa.
    /// </summary>
    private async Task<bool> IsSeatTakenAsync(Guid tripId, Guid seatId, CancellationToken cancellationToken)
    {
        var heldBySomeoneElse = await _db.SeatHolds.AnyAsync(
            h => h.TripId == tripId && h.SeatId == seatId && h.Status == SeatHoldStatus.Holding,
            cancellationToken);

        if (heldBySomeoneElse)
        {
            return true;
        }

        return await _db.Tickets.AnyAsync(
            t => t.TripId == tripId && t.SeatId == seatId && t.Status == TicketStatus.Paid,
            cancellationToken);
    }

    /// <summary>
    /// Quyết định của quản lý — chỉ HAI giá trị, và <c>Pending</c> KHÔNG nằm trong đó.
    ///
    /// So khớp với <b>danh sách tên</b> của enum thay vì dùng <c>Enum.TryParse</c>: bản TryParse
    /// nhận cả chuỗi số ("1" thành <c>Rejected</c>) nên một client gửi số vẫn lọt qua, và chuỗi
    /// <c>"pending"</c> cũng lọt — đúng thứ doc của <c>Status</c> nói phải chặn. Cùng lối
    /// <c>VoucherAdminService.TryParseVoucherStatus</c>.
    /// </summary>
    private static bool TryParseDecision(string? value, out Decision decision)
    {
        var trimmed = value?.Trim();

        foreach (var name in Enum.GetNames<Decision>())
        {
            if (string.Equals(name, trimmed, StringComparison.OrdinalIgnoreCase))
            {
                decision = Enum.Parse<Decision>(name);
                return true;
            }
        }

        decision = default;
        return false;
    }

    /// <summary>
    /// Cắt khoảng trắng thừa và coi chuỗi rỗng / toàn khoảng trắng là KHÔNG NÊU LÝ DO (<c>null</c>).
    /// Để nguyên <c>"   "</c> thì cột có giá trị mà màn hình hiện một ô trống, và thông báo gửi
    /// khách mất câu *"Người duyệt không nêu lý do."* — thay bằng một khoảng trắng.
    /// </summary>
    private static string? NormalizeRejectReason(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private static ServiceResult<TicketRequestResponse> InvalidStatus()
        => ServiceResult<TicketRequestResponse>.Invalid(
            InvalidStatusMessage,
            new Dictionary<string, string[]> { ["status"] = [InvalidStatusMessage] });

    private static TicketRequestResponse ToCancellationResponse(CancellationRequest cancellation, Ticket ticket)
        => new()
        {
            Id = cancellation.Id,
            Type = CancelType,
            TicketId = ticket.Id,
            TicketCode = ticket.Code,
            Status = cancellation.Status.ToString(),
            Price = ticket.Price,
            Reason = cancellation.Reason,
            CancellationFee = cancellation.CancellationFee,
            // Trường suy ra, và chỉ có nghĩa khi ĐÃ DUYỆT: từ chối thì không khoản nào di chuyển,
            // còn Pending thì chưa ai quyết. Cả hai ca trả null chứ không trả price - fee.
            RefundAmount = cancellation.Status == CancellationRequestStatus.Approved
                ? ticket.Price - cancellation.CancellationFee
                : null,
            // Năm trường của loại "Change" — có mặt và null, KHÔNG lược khỏi JSON (hợp đồng chốt
            // một khuôn chung cho cả hai loại để màn duyệt đọc được cả hai). Gán tường minh thay
            // vì để mặc định: người đọc thấy ngay đây là chỗ cố ý, không phải chỗ quên.
            NewTripId = null,
            NewSeatId = null,
            NewPrice = null,
            PriceDifference = null,
            NewTicketId = null,
            ReviewedByUserId = cancellation.ReviewedByUserId,
            ReviewedAt = cancellation.ReviewedAt,
            RejectReason = cancellation.RejectReason,
            CreatedAt = cancellation.CreatedAt,
            UpdatedAt = cancellation.UpdatedAt,
        };

    private static TicketRequestResponse ToTicketChangeResponse(TicketChange change, Ticket oldTicket)
        => new()
        {
            Id = change.Id,
            Type = ChangeType,
            TicketId = oldTicket.Id,
            TicketCode = oldTicket.Code,
            Status = change.Status.ToString(),
            // Giá vé GỐC: vé cũ vẫn còn nguyên dòng trong bảng (chỉ đổi Status) nên đọc thẳng ra.
            Price = oldTicket.Price,
            Reason = change.Reason,
            // Hai trường của loại "Cancel" — có mặt và null, cùng lý do như hàm trên.
            CancellationFee = null,
            RefundAmount = null,
            NewTripId = change.NewTripId,
            NewSeatId = change.NewSeatId,
            NewPrice = change.NewPrice,
            // Có dấu: dương = khách trả thêm, âm = nhà xe hoàn lại. Hai vế đều bất biến (chụp lúc
            // khách gửi) nên phép trừ không bao giờ lệch — không cần cột.
            PriceDifference = change.NewPrice - oldTicket.Price,
            NewTicketId = change.NewTicketId,
            ReviewedByUserId = change.ReviewedByUserId,
            ReviewedAt = change.ReviewedAt,
            RejectReason = change.RejectReason,
            CreatedAt = change.CreatedAt,
            UpdatedAt = change.UpdatedAt,
        };

    /// <summary>
    /// Quyết định của quản lý. Cố ý KHÔNG dùng lại <c>CancellationRequestStatus</c> /
    /// <c>TicketChangeStatus</c>: hai enum đó có thêm <c>Pending</c>, mà <c>Pending</c> không phải
    /// một quyết định — dùng lại là để <c>Enum.GetNames</c> mời đúng giá trị cần chặn vào cửa.
    /// </summary>
    private enum Decision
    {
        Approved,
        Rejected,
    }
}
