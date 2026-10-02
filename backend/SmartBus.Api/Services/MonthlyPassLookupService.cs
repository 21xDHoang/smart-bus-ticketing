using Microsoft.EntityFrameworkCore;
using SmartBus.Api.Data;
using SmartBus.Api.Dtos.MonthlyPasses;
using SmartBus.Api.Entities;

namespace SmartBus.Api.Services;

/// <summary>
/// Cài đặt <see cref="IMonthlyPassLookupService"/> — hợp đồng đầy đủ ở mục
/// "Vé tháng — /monthly-passes" của docs/api-contract.md.
///
/// "Đang hoạt động" suy từ cặp mốc ValidFrom/ValidTo (tính cả hai mốc), KHÔNG đọc cột Status:
/// giữa lúc ValidTo trôi qua và lúc job quét của Kiên chạy, vé hết hạn vẫn còn Status = "Active"
/// (AppDbContext.MonthlyPass.cs nói rõ) — lọc theo Status sẽ trả về vé đã hết hạn.
///
/// Chỉ đọc, không nhánh lỗi nào ngoài 401 ở controller, nên không dùng ServiceResult:
/// mảng rỗng là câu trả lời hợp lệ (người chưa có vé đang hoạt động).
/// </summary>
public class MonthlyPassLookupService : IMonthlyPassLookupService
{
    private readonly AppDbContext _db;

    public MonthlyPassLookupService(AppDbContext db) => _db = db;

    public async Task<IReadOnlyList<MonthlyPassResponse>> GetActiveAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        // Điều kiện UserId nằm NGAY trong truy vấn — vé của người khác không có đường lọt vào
        // kết quả, không cần kiểm quyền sở hữu ở tầng trên.
        var rows = await _db.MonthlyPasses
            .AsNoTracking()
            .Where(p => p.UserId == userId && p.ValidFrom <= now && p.ValidTo >= now)
            .OrderBy(p => p.ValidTo)
            .ThenBy(p => p.Code)
            .Select(p => new { Pass = p, PassTypeCode = p.PassType!.Code })
            .ToListAsync(cancellationToken);

        return rows.Select(row => ToResponse(row.Pass, row.PassTypeCode)).ToList();
    }

    /// <summary>
    /// Chép từ MonthlyPassRenewalService.ToResponse — cố ý, cùng lối các controller chép
    /// ValidationError thay vì tách lớp dùng chung: tách ra thì phải sửa file của người khác.
    /// </summary>
    private static MonthlyPassResponse ToResponse(MonthlyPass pass, string passTypeCode) => new()
    {
        Id = pass.Id,
        Code = pass.Code,
        RouteId = pass.RouteId,
        PassTypeCode = passTypeCode,
        Price = pass.Price,
        ValidFrom = pass.ValidFrom,
        ValidTo = pass.ValidTo,
        // Tên chuỗi của enum — đúng giá trị đang nằm trong cột Status (HasConversion<string>).
        Status = pass.Status.ToString(),
        CreatedAt = pass.CreatedAt,
    };
}
