using Microsoft.EntityFrameworkCore;
using SmartBus.Api.Data;
using SmartBus.Api.Dtos.TicketValidations;
using SmartBus.Api.Entities;

namespace SmartBus.Api.Services;

/// <summary>
/// Cài đặt <see cref="ITicketValidationService"/> — hợp đồng đầy đủ ở mục "Soát vé QR" của
/// docs/api-contract.md.
///
/// Bốn quyết định đáng đọc trước khi sửa:
///
/// 1. <b>Thứ tự năm phép kiểm khớp đúng thứ tự bảng lý do của docs/29 §3</b> — NotFound → Cancelled
///    → AlreadyUsed → WrongTrip → Expired → Valid. Đổi thứ tự là đổi hợp đồng với màn quét QR
///    (frontend đã viết sẵn năm lý do theo đúng tập này).
/// 2. <b><c>NotFound</c> gộp CẢ mã rác lẫn mã sai chữ ký lẫn mã ký cho vé không tồn tại</b> —
///    người soát chỉ cần một câu trả lời "không cho lên xe"; việc phân biệt là điều tra sau, dựa
///    vào <c>ScannedCode</c> đã lưu nguyên văn (docs/34 §3).
/// 3. <b>Kết quả trả về lấy từ dòng nhật ký ĐÃ GHI</b> (<c>recorded.Result</c>), không phải kết
///    quả vừa tính — lượt gửi lại cùng <c>clientScanId</c> phải trả về câu trả lời của lần đầu
///    (docs/39 §4.2). Trạng thái vé có thể đã đổi giữa hai lần gửi; bản ghi nhật ký đứng vững.
/// 4. <b>Tra thông tin hiển thị bằng truy vấn THEO KHOÁ NGOẠI, không Include/projection chạm
///    navigation</b> — provider InMemory của tầng test bỏ rơi cả bản ghi gốc khi Include (và cả
///    projection có điều kiện null) trỏ tới navigation KHÔNG có dòng nào trong store: vé seed mồ
///    côi thiếu Route (đúng lệ "seed dòng mồ côi" của bộ test) khiến truy vấn trả null thay vì vé
///    kèm cột hiển thị rỗng. PostgreSQL thì LEFT JOIN bình thường, nhưng hai nơi phải cư xử như
///    nhau. Truy vấn theo Id từng bảng thì cả hai nơi đều trả đúng.
/// </summary>
public class TicketValidationService : ITicketValidationService
{
    private readonly AppDbContext _db;

    private readonly ITicketQrService _qr;

    private readonly ITicketValidationRecorder _recorder;

    private readonly ITicketBoardingStopService _boardingStops;

    public TicketValidationService(
        AppDbContext db,
        ITicketQrService qr,
        ITicketValidationRecorder recorder,
        ITicketBoardingStopService boardingStops)
    {
        _db = db;
        _qr = qr;
        _recorder = recorder;
        _boardingStops = boardingStops;
    }

    public async Task<ServiceResult<TicketValidationResponse>> ValidateAsync(
        ValidateTicketRequest request,
        Guid validatedByUserId,
        CancellationToken cancellationToken = default)
    {
        // Bước 0 — chuyến phụ xe khai phải tồn tại: 404 chứ không phải một kết quả soát, vì đây là
        // lỗi phía gọi (cùng lối POST /vouchers/validate). Không kiểm trạng thái chuyến — endpoint
        // soát vé không có quyền phán chuyến đang chạy hay không.
        if (request.TripId is { } tripId && !await _db.Trips.AnyAsync(t => t.Id == tripId, cancellationToken))
        {
            return ServiceResult<TicketValidationResponse>.NotFound("Không tìm thấy chuyến");
        }

        // Bước 1 — xác thực chữ ký TRƯỚC, rồi mới tin ticketId nhúng trong mã (docs/30 §3.2).
        TicketScanRow? row = null;
        TicketValidationResult result = TicketValidationResult.NotFound;

        if (_qr.TryVerify(request.Code, out var verifiedTicketId))
        {
            var ticket = await _db.Tickets
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == verifiedTicketId, cancellationToken);

            // Vé không tồn tại dù chữ ký khớp: mã do hệ thống ký cho một vé chưa từng sinh, hoặc vé
            // đã bị xoá tay — vẫn NotFound, TicketId null cho khớp bất biến của bảng nhật ký.
            if (ticket is not null)
            {
                row = await LoadRowAsync(ticket, cancellationToken);
                result = DecideResult(row, request.TripId);
            }
        }

        // Bước 2 — trạm khách THỰC SỰ lên xe (dòng 17, Nguyễn Duy Kiên). Chỉ lượt Valid mới là một
        // lượt lên xe, và trạm phải thuộc tuyến của chuyến TRÊN VÉ — không so với
        // Tickets.BoardingStopId (trạm khách chọn lúc mua; docs/34 §2.1).
        //
        // Lượt gửi lại cùng clientScanId BỎ QUA bước này: recorder đọc trước và trả dòng gốc
        // (docs/39 §2.4 mục 2), nên kiểm lại trạm ở đây có thể biến một lượt đã ghi thành công
        // thành 400 chỉ vì lần gửi sau mang giá trị khác. Dòng đã ghi giữ nguyên trạm lần đầu.
        Guid? boardingStopId = null;
        var laLuotGuiLai = request.ClientScanId is { } clientScanId
            && await _db.TicketValidations
                .AsNoTracking()
                .AnyAsync(v => v.ClientScanId == clientScanId, cancellationToken);

        if (!laLuotGuiLai)
        {
            var boardingStop = await _boardingStops.ResolveAsync(
                request.BoardingStopId,
                row?.TripId,
                result,
                cancellationToken);

            if (!boardingStop.Success)
            {
                return ServiceResult<TicketValidationResponse>.Invalid(
                    boardingStop.Error!, boardingStop.Errors!);
            }

            boardingStopId = boardingStop.Data;
        }

        // Bước 3 — ghi nhật ký. MỌI lượt quét đều ghi, kể cả lượt bị từ chối (docs/34 §2.2).
        var recorded = await _recorder.RecordAsync(
            new RecordTicketValidationRequest
            {
                ScannedCode = request.Code ?? string.Empty,
                TicketId = row?.Id,
                TripId = request.TripId,
                Result = result,
                ClientScanId = request.ClientScanId,
                ScannedAt = request.ScannedAt,
                BoardingStopId = boardingStopId,
            },
            validatedByUserId,
            cancellationToken);

        if (!recorded.Success)
        {
            return ServiceResult<TicketValidationResponse>.Invalid(recorded.Error!, recorded.Errors!);
        }

        // Bước 4 — câu trả lời từ dòng ĐÃ GHI (docs/39 §4.2). Vé kèm theo chỉ để hiển thị: dựng từ
        // vé vừa tra khi nó khớp đúng dòng đã ghi; lệch (lượt gửi lại mà vé đã đổi trạng thái) thì
        // vẫn trả kết quả gốc, chỉ bỏ qua phần hiển thị.
        var saved = recorded.Data!;

        return ServiceResult<TicketValidationResponse>.Ok(new TicketValidationResponse
        {
            Valid = saved.Result == TicketValidationResult.Valid,
            Reason = saved.Result == TicketValidationResult.Valid ? null : saved.Result.ToString(),
            Ticket = row is not null && row.Id == saved.TicketId
                ? ToResponse(row)
                : null,
        });
    }

    /// <summary>
    /// Xét kết quả một lượt soát cho vé đã tra được. Thứ tự khớp đúng bảng lý do của docs/29 §3 —
    /// đổi thứ tự là đổi hợp đồng.
    /// </summary>
    private static TicketValidationResult DecideResult(TicketScanRow row, Guid? scanTripId)
    {
        if (row.Status == TicketStatus.Cancelled)
        {
            return TicketValidationResult.Cancelled;
        }

        if (row.Status == TicketStatus.Used)
        {
            return TicketValidationResult.AlreadyUsed;
        }

        // Chỉ kết luận được WrongTrip khi lượt soát có tripId (docs/34 §2.5).
        if (scanTripId is not null && row.TripId != scanTripId)
        {
            return TicketValidationResult.WrongTrip;
        }

        if (IsExpired(row))
        {
            return TicketValidationResult.Expired;
        }

        return TicketValidationResult.Valid;
    }

    /// <summary>
    /// "Chuyến đã khởi hành xong mà vé chưa được soát" (docs/29 §3): quá giờ khởi hành, hoặc chuyến
    /// đã chốt trạng thái Completed / Cancelled. Chuyến Cancelled mà vé còn Paid (chưa hoàn tiền
    /// xong) thì vé không còn hiệu lực — lý do gần nhất trong tập năm lý do là Expired. Chuyến không
    /// tra được (vé mồ côi trong test) thì không kết luận được hết hạn — chỉ so giờ nếu có giờ.
    /// </summary>
    private static bool IsExpired(TicketScanRow row)
        => row.TripStatus == TripStatus.Completed
            || row.TripStatus == TripStatus.Cancelled
            || (row.TripDepartureTime is { } departure && departure <= DateTime.UtcNow);

    /// <summary>
    /// Góp thông tin hiển thị cho một vé bằng các truy vấn nhỏ THEO KHOÁ NGOẠI — không Include,
    /// không projection chạm navigation (lý do ở quyết định 4 của lớp). Trên CSDL thật các khoá
    /// ngoại này không thể mồ côi (Restrict) nên mọi truy vấn đều trúng một dòng; trên InMemory của
    /// tầng test thì mồ côi trả null và cột hiển thị rỗng — cư xử như nhau ở cả hai nơi.
    /// </summary>
    private async Task<TicketScanRow> LoadRowAsync(Ticket ticket, CancellationToken cancellationToken)
    {
        var trip = await _db.Trips
            .AsNoTracking()
            .FirstOrDefaultAsync(tr => tr.Id == ticket.TripId, cancellationToken);

        var route = trip is null
            ? null
            : await _db.Routes
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == trip.RouteId, cancellationToken);

        var seat = await _db.Seats
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == ticket.SeatId, cancellationToken);

        var boardingStop = ticket.BoardingStopId is { } boardingId
            ? await _db.Stops
                .AsNoTracking()
                .FirstOrDefaultAsync(st => st.Id == boardingId, cancellationToken)
            : null;

        var alightingStop = ticket.AlightingStopId is { } alightingId
            ? await _db.Stops
                .AsNoTracking()
                .FirstOrDefaultAsync(st => st.Id == alightingId, cancellationToken)
            : null;

        var user = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == ticket.UserId, cancellationToken);

        return new TicketScanRow
        {
            Id = ticket.Id,
            Code = ticket.Code,
            TripId = ticket.TripId,
            Status = ticket.Status,
            UsedAt = ticket.UsedAt,
            TripStatus = trip?.Status,
            TripDepartureTime = trip?.DepartureTime,
            RouteCode = route?.Code,
            RouteName = route?.Name,
            SeatNumber = seat?.SeatNumber,
            BoardingStopName = boardingStop?.Name,
            AlightingStopName = alightingStop?.Name,
            PassengerName = user?.FullName,
        };
    }

    private static ValidatedTicketResponse ToResponse(TicketScanRow row) => new()
    {
        Id = row.Id,
        Code = row.Code,
        RouteCode = row.RouteCode ?? string.Empty,
        RouteName = row.RouteName ?? string.Empty,
        DepartureTime = row.TripDepartureTime ?? default,
        SeatNumber = row.SeatNumber ?? string.Empty,
        BoardingStopName = row.BoardingStopName,
        AlightingStopName = row.AlightingStopName,
        PassengerName = row.PassengerName,
        Status = row.Status.ToString(),
        UsedAt = row.UsedAt,
    };

    /// <summary>
    /// Hàng trung gian chở thông tin soát + hiển thị của một vé — <see cref="Status"/> giữ enum để
    /// <c>ToString()</c> trong bộ nhớ khi đưa vào DTO (cùng bẫy đã gặp ở FeedbackStatisticsService).
    /// Mọi cột tên hiển thị đều nullable vì vé seed mồ côi của bộ test thiếu dòng Route/Seat/User.
    /// </summary>
    private sealed class TicketScanRow
    {
        public Guid Id { get; init; }

        public string Code { get; init; } = string.Empty;

        public Guid TripId { get; init; }

        public TicketStatus Status { get; init; }

        public DateTime? UsedAt { get; init; }

        public TripStatus? TripStatus { get; init; }

        public DateTime? TripDepartureTime { get; init; }

        public string? RouteCode { get; init; }

        public string? RouteName { get; init; }

        public string? SeatNumber { get; init; }

        public string? BoardingStopName { get; init; }

        public string? AlightingStopName { get; init; }

        public string? PassengerName { get; init; }
    }
}
