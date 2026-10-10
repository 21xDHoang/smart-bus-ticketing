using Microsoft.EntityFrameworkCore;
using SmartBus.Api.Data;
using SmartBus.Api.Dtos.Vouchers;

namespace SmartBus.Api.Services;

/// <summary>
/// Cài đặt <see cref="IVoucherStatisticsService"/> — hợp đồng đầy đủ ở mục "Voucher — /vouchers",
/// phần "GET /vouchers/statistics" của docs/api-contract.md.
///
/// Bốn quyết định đáng đọc trước khi sửa:
///
/// 1. <b>SÁU con số tổng đến từ truy vấn gộp ĐỘC LẬP, không cộng lại từ <c>items</c>.</b> Cộng từ
///    <c>items</c> thì năm đẳng thức hợp đồng đúng theo ĐỊNH NGHĨA — không test nào có thể đỏ, và cả
///    mục "đẳng thức" chỉ còn là tài liệu. Đếm thẳng bằng truy vấn riêng thì đẳng thức mới là phép
///    kiểm thật. Đúng lối <see cref="FeedbackStatisticsService.GetAsync"/> đã làm cho thống kê phản
///    ánh, kèm comment nêu cùng lý do.
/// 2. <b>Nền của bảng là <c>Vouchers</c>, không phải <c>VoucherUsages</c>.</b> Gộp nhóm ngây thơ trên
///    bảng lượt tiêu thụ sẽ làm rơi đúng những dòng cần thấy nhất: mã đã phát hành mà chưa ai dùng là
///    "hiệu quả bằng 0" — câu trả lời đắt nhất của một bảng thống kê hiệu quả. Cùng lối lập luận với
///    <c>byType</c> của thống kê phản ánh: trục cố định, thiếu dữ liệu thì vẽ cột 0 chứ không bỏ cột.
/// 3. <b>Nền KHÔNG lọc <c>Status</c>.</b> Phải gồm cả voucher <c>Inactive</c>: lọc mất chúng là
///    <c>sum(items[].usedCount)</c> tụt xuống dưới <c>totalUsed</c> và đẳng thức đỏ ngay — hoặc tệ
///    hơn, sai lặng lẽ nếu tổng cũng suy từ <c>items</c>.
/// 4. <b>Không join sang <c>Payments</c>.</b> <c>VoucherUsage.PaymentCode</c> là cột trần không FK
///    (xem XML doc của entity), nên một phép join là inner join và sẽ làm rơi mọi lượt tiêu thụ chưa
///    có bản ghi thanh toán tương ứng. Thống kê đếm lượt đã tiêu thụ, không đối soát cổng thanh toán.
///
/// Bảng <c>Vouchers</c>/<c>VoucherUsages</c> đã có migration (PR #144, Vàng Thị Dăm) nên service này
/// chạy được trên CSDL thật.
/// </summary>
public class VoucherStatisticsService : IVoucherStatisticsService
{
    private readonly AppDbContext _db;

    public VoucherStatisticsService(AppDbContext db) => _db = db;

    public async Task<VoucherStatisticsResponse> GetAsync(CancellationToken cancellationToken = default)
    {
        // ---------------------------------------------------------------------------------------
        // (1) Sáu con số tổng — mỗi con một truy vấn gộp riêng, KHÔNG con nào suy từ items.
        //
        // Ép nullable ((int?)/(decimal?)) rồi ?? 0 là lối đã dùng ở RouteStopService cho phép gộp
        // trên tập rỗng: bảng chưa có dòng nào thì Sum phải cho 0 chứ không được ném.
        // ---------------------------------------------------------------------------------------
        var totalVouchers = await _db.Vouchers
            .AsNoTracking()
            .CountAsync(cancellationToken);

        var totalIssued = await _db.Vouchers
            .AsNoTracking()
            .SumAsync(v => (int?)v.Quantity, cancellationToken) ?? 0;

        // Đếm qua navigation (dịch thành NOT EXISTS, dùng index VoucherUsages (VoucherId, CreatedAt))
        // chứ KHÔNG đếm bằng cột denormalized Vouchers.UsedCount — phải cùng nguồn với usedCount của
        // từng dòng, nếu không một bộ đếm vênh sẽ cho ra hai con số mâu thuẫn trong cùng một response.
        var neverUsedVouchers = await _db.Vouchers
            .AsNoTracking()
            .CountAsync(v => !v.Usages.Any(), cancellationToken);

        // Nguồn sự thật của "đã tiêu thụ bao nhiêu" là bảng VoucherUsages, không phải cột UsedCount.
        var totalUsed = await _db.VoucherUsages
            .AsNoTracking()
            .CountAsync(cancellationToken);

        var totalOrderAmount = await _db.VoucherUsages
            .AsNoTracking()
            .SumAsync(u => (decimal?)u.OrderAmount, cancellationToken) ?? 0m;

        var totalDiscountAmount = await _db.VoucherUsages
            .AsNoTracking()
            .SumAsync(u => (decimal?)u.DiscountAmount, cancellationToken) ?? 0m;

        // ---------------------------------------------------------------------------------------
        // (2) Nền: MỌI voucher đã phát hành + tuyến của nó (left join).
        //
        // Status chiếu ra dưới dạng enum rồi mới ToString() TRONG BỘ NHỚ: gọi .ToString() ngay trong
        // projection là thứ không dịch được sang SQL (cùng bẫy đã gặp ở FeedbackStatisticsService).
        // ---------------------------------------------------------------------------------------
        var vouchers = await _db.Vouchers
            .AsNoTracking()
            .Select(v => new
            {
                v.Id,
                v.Code,
                v.Name,
                v.Status,
                v.Quantity,
                v.ValidFrom,
                v.ValidUntil,
                v.RouteId,
                // Tuyến có thể null (voucher dùng cho mọi tuyến) nên phải là điều kiện ba ngôi, không
                // phải "!" — "!" chỉ là gợi ý cho trình biên dịch, lúc chạy vẫn nổ khi Route null.
                RouteCode = v.Route == null ? null : v.Route.Code,
                RouteName = v.Route == null ? null : v.Route.Name,
            })
            .ToListAsync(cancellationToken);

        // ---------------------------------------------------------------------------------------
        // (3) Số liệu gộp theo voucher — gộp Ở TẦNG CSDL, không kéo từng dòng lượt tiêu thụ về đếm.
        // ---------------------------------------------------------------------------------------
        var usages = await _db.VoucherUsages
            .AsNoTracking()
            .GroupBy(u => u.VoucherId)
            .Select(g => new
            {
                VoucherId = g.Key,
                Count = g.Count(),
                OrderAmount = g.Sum(u => u.OrderAmount),
                DiscountAmount = g.Sum(u => u.DiscountAmount),
                FirstUsedAt = g.Min(u => u.CreatedAt),
                LastUsedAt = g.Max(u => u.CreatedAt),
            })
            .ToListAsync(cancellationToken);

        // ---------------------------------------------------------------------------------------
        // (4) Số khách khác nhau — Distinct ở tầng CSDL rồi đếm theo nhóm trong bộ nhớ.
        //
        // CỐ Ý không viết g.Select(u => u.UserId).Distinct().Count() lồng trong GroupBy: dạng đó dịch
        // được xuống Npgsql nhưng EFCore.InMemory KHÔNG dịch được (đã ghi lại đúng cái bẫy này ở
        // SeatHoldAbuseService), mà bộ test của dự án chạy trên InMemory. Distinct trên projection hai
        // cột là lối dịch được ở cả hai provider.
        //
        // Đổi lại: truy vấn này kéo về tối đa một dòng mỗi cặp (voucherId, userId). Chấp nhận được cho
        // một endpoint quản trị KHÔNG phải đường nóng; nếu bảng lớn lên thì chuyển sang COUNT(DISTINCT)
        // bằng SQL thô.
        // ---------------------------------------------------------------------------------------
        var customers = await _db.VoucherUsages
            .AsNoTracking()
            .Select(u => new { u.VoucherId, u.UserId })
            .Distinct()
            .ToListAsync(cancellationToken);

        // ---------------------------------------------------------------------------------------
        // (5) Ghép trong bộ nhớ. Voucher không có mặt ở (3) ⇒ usedCount = 0, tiền = 0, hai mốc = null.
        // ---------------------------------------------------------------------------------------
        var usageByVoucher = usages.ToDictionary(row => row.VoucherId);
        var customerCounts = customers
            .GroupBy(row => row.VoucherId)
            .ToDictionary(group => group.Key, group => group.Count());

        var items = vouchers
            .Select(v =>
            {
                var coUsage = usageByVoucher.TryGetValue(v.Id, out var usage);

                return new VoucherStatisticsItemResponse
                {
                    VoucherId = v.Id,
                    Code = v.Code,
                    Name = v.Name,
                    Status = v.Status.ToString(),
                    RouteId = v.RouteId,
                    RouteCode = v.RouteCode,
                    RouteName = v.RouteName,
                    Quantity = v.Quantity,
                    UsedCount = coUsage ? usage!.Count : 0,
                    UniqueCustomers = customerCounts.TryGetValue(v.Id, out var soKhach) ? soKhach : 0,
                    TotalOrderAmount = coUsage ? usage!.OrderAmount : 0m,
                    TotalDiscountAmount = coUsage ? usage!.DiscountAmount : 0m,
                    FirstUsedAt = coUsage ? usage!.FirstUsedAt : null,
                    LastUsedAt = coUsage ? usage!.LastUsedAt : null,
                    ValidFrom = v.ValidFrom,
                    ValidUntil = v.ValidUntil,
                };
            })
            // Mã hiệu quả nhất lên đầu — đó chính là câu bảng này để trả lời. Trùng số lượt thì theo mã
            // tăng dần: thứ tự phải TẤT ĐỊNH, nếu không thì hai lần gọi cho hai thứ tự khác nhau và
            // không ai so sánh được gì. Hệ quả đã biết và đã ghi vào hợp đồng: voucher chưa ai dùng nằm
            // CUỐI bảng — neverUsedVouchers cho màn hình biết có bao nhiêu để tự lọc.
            .OrderByDescending(item => item.UsedCount)
            .ThenBy(item => item.Code, StringComparer.Ordinal)
            .ToList();

        return new VoucherStatisticsResponse
        {
            TotalVouchers = totalVouchers,
            TotalIssued = totalIssued,
            TotalUsed = totalUsed,
            TotalOrderAmount = totalOrderAmount,
            TotalDiscountAmount = totalDiscountAmount,
            NeverUsedVouchers = neverUsedVouchers,
            Items = items,
        };
    }
}
