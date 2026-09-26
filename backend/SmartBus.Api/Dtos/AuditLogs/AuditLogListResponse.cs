namespace SmartBus.Api.Dtos.AuditLogs;

/// <summary>
/// Kết quả phân trang của GET /api/audit-logs.
/// <see cref="Items"/> là trang hiện tại, <see cref="Total"/> là tổng số dòng khớp bộ lọc —
/// frontend dùng <see cref="Total"/> để vẽ phân trang của AntD Table.
/// Cùng khuôn <see cref="Routes.RouteListResponse"/> và <see cref="Admin.AdminUserListResponse"/>.
/// </summary>
public class AuditLogListResponse
{
    public IReadOnlyList<AuditLogResponse> Items { get; set; } = [];

    public int Total { get; set; }

    public int Page { get; set; }

    public int PageSize { get; set; }
}
