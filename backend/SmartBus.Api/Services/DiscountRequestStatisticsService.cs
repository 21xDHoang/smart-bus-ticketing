using Microsoft.EntityFrameworkCore;
using SmartBus.Api.Data;
using SmartBus.Api.Dtos.DiscountRequests;
using SmartBus.Api.Entities;

namespace SmartBus.Api.Services;

/// <summary>
/// Cài đặt <see cref="IDiscountRequestStatisticsService"/> — hợp đồng đầy đủ ở mục "Hồ sơ ưu đãi —
/// /discount-requests", phần "GET /discount-requests/statistics" của docs/api-contract.md.
///
/// Ba quyết định đáng đọc trước khi sửa:
///
/// 1. <b>Bốn con số trạng thái đến từ bốn truy vấn đếm ĐỘC LẬP, không cộng dồn trên một truy vấn
///    gộp.</b> Nếu suy con này từ con kia thì đẳng thức <c>total == pending + approved + rejected</c>
///    đúng theo định nghĩa — không test nào có thể đỏ, và đẳng thức chỉ còn là tài liệu. Đếm thẳng
///    bằng truy vấn riêng thì đẳng thức mới là phép kiểm thật. Đúng lối
///    <see cref="VoucherStatisticsService.GetAsync"/>.
/// 2. <b><c>approvedActive</c> là phần bù của <c>approvedExpired</c> trong <c>approved</c></b>
///    (<c>approved - approvedExpired</c>), nên đẳng thức thứ hai luôn đúng tuyệt đối. Chỉ
///    <c>approvedExpired</c> được đếm riêng — nó là con số có truy vấn riêng; <c>approvedActive</c>
///    không thể đếm sai độc lập với <c>approved</c> và <c>approvedExpired</c>.
/// 3. <b>Hồ sơ <c>Approved</c> có <c>ExpiresAt</c> null được tính là CÒN HIỆU LỰC.</b> Entity ghi rõ:
///    thẻ người cao tuổi không có ngày hết hạn, và job nhắc hết hạn chỉ áp dụng cho học sinh/sinh
///    viên. Đọc null thành "hết hạn" sẽ làm mọi ưu đãi người cao tuổi biến mất khỏi số đang hiệu
///    lực.
///
/// Bảng <c>DiscountRequests</c> đã có migration từ Sprint 2
/// (<c>20260930095511_Sprint2_TripDriver_DiscountRequests</c>), nên service này chạy được trên CSDL
/// thật. Chỉ đọc — không có migration kèm theo.
/// </summary>
public class DiscountRequestStatisticsService : IDiscountRequestStatisticsService
{
    private readonly AppDbContext _db;

    public DiscountRequestStatisticsService(AppDbContext db) => _db = db;

    public async Task<DiscountRequestStatisticsResponse> GetAsync(CancellationToken cancellationToken = default)
    {
        // Mốc "bây giờ" lấy MỘT lần cho cả request: ExpiresAt đúng bằng mốc này phải rơi về phía đã
        // hết hạn, và hai lượt đọc UtcNow khác nhau có thể cho hai câu trả lời khác nhau trong cùng
        // một response.
        var now = DateTime.UtcNow;

        // ---------------------------------------------------------------------------------------
        // Bốn con số trạng thái — mỗi con một truy vấn đếm riêng (xem quyết định 1 ở đầu lớp).
        // ---------------------------------------------------------------------------------------
        var total = await _db.DiscountRequests
            .AsNoTracking()
            .CountAsync(cancellationToken);

        var pending = await _db.DiscountRequests
            .AsNoTracking()
            .CountAsync(d => d.Status == DiscountRequestStatus.Pending, cancellationToken);

        var approved = await _db.DiscountRequests
            .AsNoTracking()
            .CountAsync(d => d.Status == DiscountRequestStatus.Approved, cancellationToken);

        var rejected = await _db.DiscountRequests
            .AsNoTracking()
            .CountAsync(d => d.Status == DiscountRequestStatus.Rejected, cancellationToken);

        // ---------------------------------------------------------------------------------------
        // Tập con hết hạn của nhóm đã duyệt. Điều kiện Status là BẮT BUỘC: hồ sơ Pending/Rejected
        // cũng có ExpiresAt (dữ liệu cũ hoặc nhập nhầm lúc tạo), nhưng chúng không phải "đã duyệt
        // và hết hạn" — đếm chúng ở đây sẽ làm approvedActive sai theo.
        // ---------------------------------------------------------------------------------------
        var approvedExpired = await _db.DiscountRequests
            .AsNoTracking()
            .CountAsync(
                d => d.Status == DiscountRequestStatus.Approved
                    && d.ExpiresAt != null
                    && d.ExpiresAt <= now,
                cancellationToken);

        return new DiscountRequestStatisticsResponse
        {
            Total = total,
            Pending = pending,
            Approved = approved,
            Rejected = rejected,
            ApprovedActive = approved - approvedExpired,
            ApprovedExpired = approvedExpired,
        };
    }
}
