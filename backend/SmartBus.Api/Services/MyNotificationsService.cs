using Microsoft.EntityFrameworkCore;
using SmartBus.Api.Data;
using SmartBus.Api.Dtos.Notifications;
using SmartBus.Api.Entities;

namespace SmartBus.Api.Services;

/// <summary>
/// Cài đặt <see cref="IMyNotificationsService"/> — hợp đồng đầy đủ ở mục "Thông báo trong ứng dụng —
/// /notifications" của docs/api-contract.md.
///
/// Chỉ đọc: không có thao tác ghi nào ở đây nên cũng không có <c>SaveChangesAsync</c> — cùng lối
/// <see cref="MyTicketsService"/> và <see cref="RefundLookupService"/>.
///
/// Ba quyết định đáng đọc trước khi sửa:
///
/// 1. <b>Phạm vi "của tôi" nằm TRONG TRUY VẤN (<c>WHERE UserId = @u</c>), không nằm ở controller.</b>
///    Không có tham số nào để dò thông báo của người khác — cùng lối <see cref="MyTicketsService"/>
///    và <see cref="RefundLookupService"/>. Thông báo là dữ liệu riêng của một người (kết quả duyệt
///    yêu cầu huỷ vé CỦA HỌ), nên đây không phải chuyện lịch sự mà là chuyện quyền đọc.
/// 2. <b>Không có nhánh lỗi nào.</b> Chưa nhận thông báo nào là mảng rỗng; không có tham số lọc nào
///    để mà gõ sai. Vì vậy service trả thẳng danh sách chứ không bọc <see cref="ServiceResult{T}"/> —
///    cùng lối <c>MyTicketsService.ListAsync</c>.
/// 3. <b>Sắp theo <c>CreatedAt</c> giảm dần, rồi tới <c>Id</c>.</b> Màn danh sách đọc mới nhất trước.
///    Bảng chỉ-ghi-thêm nên không có cột nào khác để sắp; <c>ThenBy(Id)</c> chỉ để thứ tự TẤT ĐỊNH khi
///    hai thông báo sinh cùng một giây (ca có thật: một lượt duyệt phát thông báo rồi service hoàn
///    tiền phát tiếp) — thiếu nó thì hai lần gọi cùng dữ liệu có thể ra hai thứ tự khác nhau, và test
///    sẽ chập chờn. Cùng lối danh sách khoản hoàn và danh sách phản ánh.
///
/// Truy vấn dùng đúng chỉ mục <c>IX_Notifications_UserId_CreatedAt</c> đã dựng từ migration
/// <c>20261013143603_Sprint4_Notifications</c>: lọc theo <c>UserId</c> rồi sắp theo <c>CreatedAt</c>
/// là đúng thứ tự cột của chỉ mục đó, nên không cần chỉ mục mới và task này KHÔNG sinh migration nào.
/// </summary>
public class MyNotificationsService : IMyNotificationsService
{
    private readonly AppDbContext _db;

    public MyNotificationsService(AppDbContext db) => _db = db;

    public async Task<IReadOnlyList<NotificationResponse>> ListMineAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var rows = await _db.Notifications
            .AsNoTracking()
            .Where(n => n.UserId == userId)
            .OrderByDescending(n => n.CreatedAt)
            .ThenBy(n => n.Id)
            .Select(n => new NotificationRow
            {
                Id = n.Id,
                Type = n.Type,
                Title = n.Title,
                Body = n.Body,
                RelatedEntityType = n.RelatedEntityType,
                RelatedEntityId = n.RelatedEntityId,
                CreatedAt = n.CreatedAt,
            })
            .ToListAsync(cancellationToken);

        return rows.Select(ToResponse).ToList();
    }

    /// <summary>
    /// Hình dạng trung gian giữ nguyên kiểu enum của hai cột <c>Type</c> và <c>RelatedEntityType</c>.
    /// Có lớp này để phép chiếu chạy được dưới CSDL thật (Npgsql dịch <c>Select</c> thành SQL) mà
    /// việc đổi enum sang chuỗi lại nằm trong bộ nhớ — nhét <c>n.Type.ToString()</c> thẳng vào
    /// <c>Select</c> là để EF dịch một lời gọi phương thức không dịch được.
    /// </summary>
    private sealed class NotificationRow
    {
        public Guid Id { get; init; }

        public NotificationType Type { get; init; }

        public string Title { get; init; } = string.Empty;

        public string Body { get; init; } = string.Empty;

        public NotificationTarget? RelatedEntityType { get; init; }

        public Guid? RelatedEntityId { get; init; }

        public DateTime CreatedAt { get; init; }
    }

    private static NotificationResponse ToResponse(NotificationRow row) => new()
    {
        Id = row.Id,
        // Tên chuỗi của enum — đúng giá trị đang nằm trong cột (HasConversion<string>), và đúng bộ
        // giá trị interface AppNotification của frontend đang chờ.
        Type = row.Type.ToString(),
        Title = row.Title,
        Body = row.Body,
        // Cột nullable: thông báo không gắn bản ghi nào (thông báo hệ thống của Sprint 5) để null,
        // KHÔNG đổi thành chuỗi rỗng — frontend phân biệt "không có gì để mở" với "mở màn X".
        RelatedEntityType = row.RelatedEntityType?.ToString(),
        RelatedEntityId = row.RelatedEntityId,
        CreatedAt = row.CreatedAt,
    };
}
