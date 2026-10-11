namespace SmartBus.Api.Dtos.Vouchers;

/// <summary>
/// Kết quả phân trang của GET /api/vouchers. <see cref="Items"/> là trang hiện tại,
/// <see cref="Total"/> là tổng số dòng KHỚP BỘ LỌC (không phải số dòng trong <see cref="Items"/>) —
/// màn quản lý dùng <see cref="Total"/> để vẽ phân trang của AntD Table. Cùng khuôn
/// <see cref="Admin.AdminUserListResponse"/> và khớp <c>VoucherListResult</c> của bản nháp FE.
/// </summary>
public class VoucherListResponse
{
    public IReadOnlyList<VoucherResponse> Items { get; set; } = [];

    public int Total { get; set; }

    public int Page { get; set; }

    public int PageSize { get; set; }
}
