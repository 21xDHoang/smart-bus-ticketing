namespace SmartBus.Api.Dtos.Admin;

/// <summary>
/// Kết quả phân trang của GET /api/admin/users.
/// <see cref="Items"/> là trang hiện tại, <see cref="Total"/> là tổng số dòng khớp bộ lọc —
/// frontend dùng <see cref="Total"/> để vẽ phân trang của AntD Table.
/// </summary>
public class AdminUserListResponse
{
    public IReadOnlyList<AdminUserResponse> Items { get; set; } = [];

    public int Total { get; set; }

    public int Page { get; set; }

    public int PageSize { get; set; }
}
