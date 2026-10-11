using Microsoft.EntityFrameworkCore;
using SmartBus.Api.Data;
using SmartBus.Api.Dtos.Tickets;

namespace SmartBus.Api.Services;

/// <summary>
/// Cài đặt <see cref="IMyTicketsService"/> — hợp đồng đầy đủ ở mục "Vé điện tử" của
/// docs/api-contract.md.
///
/// Ba quyết định đáng đọc trước khi sửa:
///
/// 1. <b>Phạm vi "của tôi" nằm TRONG TRUY VẤN (<c>WHERE UserId = @u</c>), không nằm ở controller.</b>
///    Không có tham số nào để dò vé người khác, và vé của người khác trả đúng 404 — cùng lối
///    <see cref="SeatHoldLookupService"/> và <see cref="FeedbackLookupService"/>.
/// 2. <b>Tên hiển thị nối sẵn ở tầng CSDL</b> (Trips → Routes, Seats, Stops) để FE vẽ một dòng vé
///    mà không phải gọi thêm endpoint nào. <c>BoardingStop</c>/<c>AlightingStop</c> nullable nên
///    phải là điều kiện ba ngôi, không phải "!" — "!" chỉ là gợi ý cho trình biên dịch, lúc chạy
///    vẫn nổ khi trạm null (cùng lối VoucherStatisticsService).
/// 3. <b>Không hứa thứ tự.</b> Hợp đồng ghi rõ màn "Vé của tôi" tự sắp lại; ở đây chỉ sắp
///    <c>CreatedAt</c> giảm dần để kết quả ổn định giữa các lượt gọi chứ không phải lời hứa cho FE.
///
/// Bảng <c>Tickets</c> đã có migration (<c>Sprint3_Payments_Vouchers_Tickets</c>, PR #144) nên
/// service này chạy được trên CSDL thật sau bước <c>dotnet ef database update</c> (docs/28 §8).
/// </summary>
public class MyTicketsService : IMyTicketsService
{
    private const string TicketNotFoundMessage = "Không tìm thấy vé";

    private readonly AppDbContext _db;

    public MyTicketsService(AppDbContext db) => _db = db;

    public async Task<IReadOnlyList<TicketResponse>> ListAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var rows = await _db.Tickets
            .AsNoTracking()
            .Where(t => t.UserId == userId)
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => new TicketRow
            {
                Id = t.Id,
                Code = t.Code,
                // Trip/Route/Seat là khoá ngoại bắt buộc (không nullable ở tầng dữ liệu) nên dùng "!"
                // để chặn CS8602 — cùng lối FeedbackStatisticsService ("!" là gợi ý cho trình biên
                // dịch, không ảnh hưởng câu SQL dịch ra).
                RouteCode = t.Trip!.Route!.Code,
                RouteName = t.Trip.Route.Name,
                Origin = t.Trip.Route.Origin,
                Destination = t.Trip.Route.Destination,
                DepartureTime = t.Trip.DepartureTime,
                SeatNumber = t.Seat!.SeatNumber,
                BoardingStopName = t.BoardingStop == null ? null : t.BoardingStop.Name,
                AlightingStopName = t.AlightingStop == null ? null : t.AlightingStop.Name,
                Price = t.Price,
                Status = t.Status,
                CreatedAt = t.CreatedAt,
                UsedAt = t.UsedAt,
            })
            .ToListAsync(cancellationToken);

        return rows.Select(ToResponse).ToList();
    }

    public async Task<ServiceResult<TicketResponse>> GetByIdAsync(
        Guid userId,
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var row = await _db.Tickets
            .AsNoTracking()
            .Where(t => t.Id == id && t.UserId == userId)
            .Select(t => new TicketRow
            {
                Id = t.Id,
                Code = t.Code,
                RouteCode = t.Trip!.Route!.Code,
                RouteName = t.Trip.Route.Name,
                Origin = t.Trip.Route.Origin,
                Destination = t.Trip.Route.Destination,
                DepartureTime = t.Trip.DepartureTime,
                SeatNumber = t.Seat!.SeatNumber,
                BoardingStopName = t.BoardingStop == null ? null : t.BoardingStop.Name,
                AlightingStopName = t.AlightingStop == null ? null : t.AlightingStop.Name,
                Price = t.Price,
                Status = t.Status,
                CreatedAt = t.CreatedAt,
                UsedAt = t.UsedAt,
            })
            .FirstOrDefaultAsync(cancellationToken);

        return row is null
            ? ServiceResult<TicketResponse>.NotFound(TicketNotFoundMessage)
            : ServiceResult<TicketResponse>.Ok(ToResponse(row));
    }

    /// <summary>
    /// Hàng trung gian của phép chiếu — <see cref="TicketRow.Status"/> giữ enum để EF dịch được,
    /// rồi mới <c>ToString()</c> TRONG BỘ NHỚ khi đưa vào DTO: gọi <c>ToString()</c> ngay trong
    /// projection là thứ không dịch được sang SQL (cùng bẫy đã gặp ở FeedbackStatisticsService).
    /// </summary>
    private sealed class TicketRow
    {
        public Guid Id { get; init; }

        public string Code { get; init; } = string.Empty;

        public string RouteCode { get; init; } = string.Empty;

        public string RouteName { get; init; } = string.Empty;

        public string Origin { get; init; } = string.Empty;

        public string Destination { get; init; } = string.Empty;

        public DateTime DepartureTime { get; init; }

        public string SeatNumber { get; init; } = string.Empty;

        public string? BoardingStopName { get; init; }

        public string? AlightingStopName { get; init; }

        public decimal Price { get; init; }

        public Entities.TicketStatus Status { get; init; }

        public DateTime CreatedAt { get; init; }

        public DateTime? UsedAt { get; init; }
    }

    private static TicketResponse ToResponse(TicketRow row) => new()
    {
        Id = row.Id,
        Code = row.Code,
        RouteCode = row.RouteCode,
        RouteName = row.RouteName,
        Origin = row.Origin,
        Destination = row.Destination,
        DepartureTime = row.DepartureTime,
        SeatNumber = row.SeatNumber,
        BoardingStopName = row.BoardingStopName,
        AlightingStopName = row.AlightingStopName,
        Price = row.Price,
        Status = row.Status.ToString(),
        CreatedAt = row.CreatedAt,
        UsedAt = row.UsedAt,
    };
}
