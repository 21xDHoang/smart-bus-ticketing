using Microsoft.EntityFrameworkCore;
using SmartBus.Api.Data;
using SmartBus.Api.Dtos.Refunds;
using SmartBus.Api.Entities;

namespace SmartBus.Api.Services;

/// <summary>
/// Cài đặt <see cref="IRefundLookupService"/> — hợp đồng đầy đủ ở mục "Hoàn tiền — /refunds" của
/// docs/api-contract.md.
///
/// Chỉ đọc: không có thao tác ghi nào ở đây nên cũng không có <c>SaveChangesAsync</c> — cùng lối
/// <see cref="MyTicketsService"/> và <see cref="SeatHoldLookupService"/>.
///
/// Bốn quyết định đáng đọc trước khi sửa:
///
/// 1. <b>Phạm vi "của tôi" nằm TRONG TRUY VẤN (<c>WHERE UserId = @u</c>), không nằm ở controller.</b>
///    Không có tham số nào để dò khoản hoàn của người khác, và ca "của người khác" tự rơi vào 404
///    giống hệt ca "không tồn tại", không cần nhánh 403 riêng. Cùng lối <see cref="MyTicketsService"/>
///    và <see cref="FeedbackLookupService"/>.
/// 2. <b>Không 404 khi lọc theo giao dịch mà không ra dòng nào.</b> "Giao dịch này không phát sinh
///    khoản hoàn nào" là câu trả lời thường gặp nhất (mọi giao dịch thành công đều rơi vào ca đó),
///    không phải sự cố. Xem doc-comment <see cref="IRefundLookupService.ListMineAsync"/>.
/// 3. <b><c>retryCount</c> là phép ĐẾM trên bảng nhật ký, không phải cột của <c>Refunds</c></b> —
///    xem hợp đồng mục "<c>retryCount</c> — con số này đếm ở đâu ra". Đây là chỗ cài đặt đúng lời
///    hứa đã ghi ở docs/40-csdl-hoan-tien.md §2.4 khi bảng <c>Refunds</c> cố ý không có cột đếm.
/// 4. <b>Tên hiển thị nối sẵn ở tầng CSDL</b> (<c>Tickets → Trips → Routes</c>, <c>Seats</c>,
///    <c>Users</c>, <c>Payments</c>) để frontend vẽ một dòng khoản hoàn mà không phải gọi thêm
///    endpoint nào — cùng lối <see cref="MyTicketsService"/>. Bảy trường suy từ vé là <b>nullable</b>
///    vì khoản hoàn nhánh <c>PaymentFailure</c> thường không có vé nào.
///
/// <c>GetByRefundCodeAsync</c> KHÔNG dùng <c>FirstOrDefaultAsync</c> trên cả bảng rồi kiểm quyền sở
/// hữu ở tầng trên: hai điều kiện (<c>RefundCode</c> và <c>UserId</c>) nằm chung một <c>WHERE</c>,
/// nên không có nhánh nào đọc được khoản hoàn của người khác — kể cả để đếm hay để kiểm sự tồn tại.
/// </summary>
public class RefundLookupService : IRefundLookupService
{
    private const string RefundNotFoundMessage = "Không tìm thấy khoản hoàn tiền";

    private readonly AppDbContext _db;

    public RefundLookupService(AppDbContext db) => _db = db;

    public async Task<IReadOnlyList<RefundResponse>> ListMineAsync(
        Guid userId,
        ListMyRefundsRequest request,
        CancellationToken cancellationToken = default)
    {
        var query = _db.Refunds
            .AsNoTracking()
            .Where(r => r.UserId == userId);

        if (!string.IsNullOrWhiteSpace(request.PaymentCode))
        {
            // Lọc qua khoá ngoại chứ không qua một cột mã chép sẵn: Refunds không có cột
            // PaymentCode (mã giao dịch là chuyện của bảng Payments, chép sang đây là nguồn sự thật
            // thứ hai cho cùng một chuỗi). EF dịch thành một phép nối sang Payments và dùng chỉ mục
            // unique của Payments.PaymentCode, còn phía Refunds là IX_Refunds_PaymentId.
            var paymentCode = request.PaymentCode;

            query = query.Where(r => r.Payment!.PaymentCode == paymentCode);
        }

        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            if (!TryParseRefundStatus(request.Status, out var status))
            {
                // Mã trạng thái lạ: không báo lỗi mà trả mảng rỗng — cùng lối FeedbackLookupService
                // và TripLookupService: mã lạ có thể là trạng thái hợp lệ trong tương lai, 400 ở đây
                // làm màn hình cũ vỡ khi backend thêm giá trị mới.
                return [];
            }

            query = query.Where(r => r.Status == status);
        }

        var rows = await query
            // Màn theo dõi đọc mới nhất trước; trùng thời điểm (tạo cùng giây) xếp tiếp theo Id để
            // thứ tự tất định — cùng lối danh sách phản ánh.
            .OrderByDescending(r => r.CreatedAt)
            .ThenBy(r => r.Id)
            .Select(r => new RefundRow
            {
                Id = r.Id,
                RefundCode = r.RefundCode,
                Source = r.Source,

                // Khoản hoàn nhánh PaymentFailure thường KHÔNG có vé (vé chỉ phát hành sau khi tiền
                // về thành công), nên phải kiểm null ở đây — "!" chỉ là gợi ý cho trình biên dịch,
                // lúc chạy vẫn nổ khi TicketId null. Ba tầng còn lại (Ticket → Trip → Route) là khoá
                // ngoại BẮT BUỘC nên dùng "!" được, cùng lối MyTicketsService.
                TicketCode = r.Ticket != null ? r.Ticket.Code : null,
                RouteCode = r.Ticket != null ? r.Ticket.Trip!.Route!.Code : null,
                RouteName = r.Ticket != null ? r.Ticket.Trip!.Route!.Name : null,
                Origin = r.Ticket != null ? r.Ticket.Trip!.Route!.Origin : null,
                Destination = r.Ticket != null ? r.Ticket.Trip!.Route!.Destination : null,
                DepartureTime = r.Ticket != null ? r.Ticket.Trip!.DepartureTime : null,
                SeatNumber = r.Ticket != null ? r.Ticket.Seat!.SeatNumber : null,

                // UserId và PaymentId đều là khoá ngoại BẮT BUỘC (khoản hoàn không gắn giao dịch nào
                // là khoản hoàn không đối soát được) nên hai tầng này không cần kiểm null.
                PassengerName = r.User!.FullName,
                Amount = r.Amount,
                MethodName = r.Payment!.MethodCode,
                PaymentCode = r.Payment!.PaymentCode,

                GatewayTransactionId = r.GatewayTransactionId,
                Status = r.Status,
                FailReason = r.FailReason,
                RetryCount = _db.RefundLogs.Count(
                    l => l.RefundId == r.Id && l.Trigger != RefundLogTrigger.Initial),
                CreatedAt = r.CreatedAt,
                ProcessedAt = r.ProcessedAt,
                UpdatedAt = r.UpdatedAt,
            })
            .ToListAsync(cancellationToken);

        return rows.Select(ToResponse).ToList();
    }

    public async Task<ServiceResult<RefundResponse>> GetByRefundCodeAsync(
        Guid userId,
        string refundCode,
        CancellationToken cancellationToken = default)
    {
        var row = await _db.Refunds
            .AsNoTracking()
            .Where(r => r.RefundCode == refundCode && r.UserId == userId)
            .Select(r => new RefundRow
            {
                Id = r.Id,
                RefundCode = r.RefundCode,
                Source = r.Source,
                TicketCode = r.Ticket != null ? r.Ticket.Code : null,
                RouteCode = r.Ticket != null ? r.Ticket.Trip!.Route!.Code : null,
                RouteName = r.Ticket != null ? r.Ticket.Trip!.Route!.Name : null,
                Origin = r.Ticket != null ? r.Ticket.Trip!.Route!.Origin : null,
                Destination = r.Ticket != null ? r.Ticket.Trip!.Route!.Destination : null,
                DepartureTime = r.Ticket != null ? r.Ticket.Trip!.DepartureTime : null,
                SeatNumber = r.Ticket != null ? r.Ticket.Seat!.SeatNumber : null,
                PassengerName = r.User!.FullName,
                Amount = r.Amount,
                MethodName = r.Payment!.MethodCode,
                PaymentCode = r.Payment!.PaymentCode,
                GatewayTransactionId = r.GatewayTransactionId,
                Status = r.Status,
                FailReason = r.FailReason,
                RetryCount = _db.RefundLogs.Count(
                    l => l.RefundId == r.Id && l.Trigger != RefundLogTrigger.Initial),
                CreatedAt = r.CreatedAt,
                ProcessedAt = r.ProcessedAt,
                UpdatedAt = r.UpdatedAt,
            })
            .FirstOrDefaultAsync(cancellationToken);

        return row is null
            ? ServiceResult<RefundResponse>.NotFound(RefundNotFoundMessage)
            : ServiceResult<RefundResponse>.Ok(ToResponse(row));
    }

    /// <summary>
    /// Hàng trung gian của phép chiếu — hai cột <see cref="Source"/> và <see cref="Status"/> giữ enum
    /// để EF dịch được, rồi mới <c>ToString()</c> TRONG BỘ NHỚ khi đưa vào DTO: gọi <c>ToString()</c>
    /// ngay trong projection là thứ không dịch được sang SQL (cùng bẫy đã gặp ở
    /// <c>MyTicketsService.TicketRow</c> và <c>FeedbackStatisticsService</c>).
    ///
    /// Cố ý chép nguyên hình dạng của <c>RefundRow</c> ở <see cref="MyTicketsService"/> thay vì tách
    /// thành lớp dùng chung: cùng lý do các controller chép <c>ValidationError</c> — tách ra là phải
    /// sửa file của người khác, mà luật nhóm không cho.
    /// </summary>
    private sealed class RefundRow
    {
        public Guid Id { get; init; }

        public string RefundCode { get; init; } = string.Empty;

        public RefundSource Source { get; init; }

        public string? TicketCode { get; init; }

        public string? RouteCode { get; init; }

        public string? RouteName { get; init; }

        public string? Origin { get; init; }

        public string? Destination { get; init; }

        public DateTime? DepartureTime { get; init; }

        public string? SeatNumber { get; init; }

        public string PassengerName { get; init; } = string.Empty;

        public decimal Amount { get; init; }

        public string MethodName { get; init; } = string.Empty;

        public string PaymentCode { get; init; } = string.Empty;

        public string? GatewayTransactionId { get; init; }

        public RefundStatus Status { get; init; }

        public string? FailReason { get; init; }

        public int RetryCount { get; init; }

        public DateTime CreatedAt { get; init; }

        public DateTime? ProcessedAt { get; init; }

        public DateTime? UpdatedAt { get; init; }
    }

    private static RefundResponse ToResponse(RefundRow row) => new()
    {
        Id = row.Id,
        RefundCode = row.RefundCode,
        // Tên chuỗi của enum — đúng giá trị đang nằm trong cột (HasConversion<string>).
        Source = row.Source.ToString(),
        TicketCode = row.TicketCode,
        RouteCode = row.RouteCode,
        RouteName = row.RouteName,
        Origin = row.Origin,
        Destination = row.Destination,
        DepartureTime = row.DepartureTime,
        SeatNumber = row.SeatNumber,
        PassengerName = row.PassengerName,
        Amount = row.Amount,
        MethodName = row.MethodName,
        PaymentCode = row.PaymentCode,
        GatewayTransactionId = row.GatewayTransactionId,
        Status = row.Status.ToString(),
        FailReason = row.FailReason,
        RetryCount = row.RetryCount,
        CreatedAt = row.CreatedAt,
        ProcessedAt = row.ProcessedAt,
        UpdatedAt = row.UpdatedAt,
    };

    /// <summary>
    /// So khớp với danh sách tên của enum thay vì dùng <c>Enum.TryParse</c> — cùng lý do
    /// <c>FeedbackLookupService</c>/<c>RouteService</c>/<c>TripLookupService</c>: TryParse chấp nhận
    /// cả chuỗi số (<c>Enum.Parse("0")</c> ra giá trị đầu tiên), trái quy ước A3. Bỏ qua hoa/thường
    /// khi đọc, nhưng giá trị lưu xuống CSDL luôn là tên chuẩn của enum.
    ///
    /// Chép lại thay vì tách lớp dùng chung: tách ra là phải sửa file của người khác, mà luật nhóm
    /// không cho sửa file của người khác — các bản giống nhau là cái giá rẻ hơn (cùng lối các
    /// controller chép <c>ValidationError</c>).
    /// </summary>
    private static bool TryParseRefundStatus(string? value, out RefundStatus status)
    {
        var trimmed = value?.Trim();

        foreach (var name in Enum.GetNames<RefundStatus>())
        {
            if (string.Equals(name, trimmed, StringComparison.OrdinalIgnoreCase))
            {
                status = Enum.Parse<RefundStatus>(name);
                return true;
            }
        }

        status = default;
        return false;
    }
}
