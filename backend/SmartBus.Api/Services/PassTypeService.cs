using Microsoft.EntityFrameworkCore;
using SmartBus.Api.Data;
using SmartBus.Api.Dtos.PassTypes;
using SmartBus.Api.Entities;

namespace SmartBus.Api.Services;

/// <summary>
/// Danh mục loại vé tháng — GET/POST/PUT/DELETE /api/pass-types. Bổ sung ngoài story 16,
/// hợp đồng ở mục "Loại vé tháng — /pass-types" của docs/api-contract.md.
///
/// Bảng PassTypes trước đây chỉ được ĐỌC lúc đăng ký/gia hạn; ở đây là phần CRUD cho quản trị
/// viên. Quyền truy cập do controller gác bằng <see cref="RbacPolicies.ManagerOrAbove"/>.
///
/// Service không đăng ký DI: đăng ký service nằm trong Program.cs — file của Phùng Duy Hoàng,
/// ngoài phạm vi được sửa. Controller nhận AppDbContext từ DI rồi khởi tạo service trực tiếp.
/// </summary>
public class PassTypeService
{
    private const string NotFoundMessage = "Không tìm thấy loại vé";
    private const string DuplicateCodeMessage = "Mã loại vé đã tồn tại";
    private const string InUseMessage = "Loại vé đã được sử dụng, không thể xoá";

    private readonly AppDbContext _db;

    public PassTypeService(AppDbContext db) => _db = db;

    /// <summary>
    /// Toàn bộ danh mục, sắp theo thời hạn rồi mã. Trả mảng trần — bảng chỉ vài dòng nên không
    /// phân trang (cùng lối GET /monthly-passes/me).
    /// </summary>
    public async Task<IReadOnlyList<PassTypeResponse>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        var passTypes = await _db.PassTypes
            .AsNoTracking()
            .OrderBy(pt => pt.DurationMonths)
            .ThenBy(pt => pt.Code)
            .ToListAsync(cancellationToken);

        // Đếm số vé tham chiếu từng loại trong MỘT truy vấn, không N+1.
        var usageCounts = await _db.MonthlyPasses
            .AsNoTracking()
            .GroupBy(p => p.PassTypeId)
            .Select(group => new { PassTypeId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.PassTypeId, row => row.Count, cancellationToken);

        return passTypes
            .Select(passType => ToResponse(passType, usageCounts.GetValueOrDefault(passType.Id)))
            .ToList();
    }

    public async Task<ServiceResult<PassTypeResponse>> CreateAsync(
        CreatePassTypeRequest request,
        CancellationToken cancellationToken = default)
    {
        var code = request.Code!.Trim();
        var name = request.Name!.Trim();

        // Kiểm trùng không phân biệt hoa thường ở tầng service: chỉ mục unique của CSDL phân
        // biệt hoa thường, nên "OneMonth" và "onemonth" lọt được qua CSDL nhưng là hai mã gây
        // nhầm cho người dùng khi nhập passTypeCode.
        if (await CodeExistsAsync(code, cancellationToken))
        {
            return ServiceResult<PassTypeResponse>.Conflict(DuplicateCodeMessage);
        }

        var passType = new PassType
        {
            Code = code,
            Name = name,
            DurationMonths = request.DurationMonths!.Value,
            Price = request.Price!.Value,
        };

        _db.PassTypes.Add(passType);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Cửa sổ đua: hai request cùng tạo một mã giữa lúc kiểm tra và lúc ghi. Chỉ mục
            // unique trên Code là lớp chặn cuối cùng — cùng lối các service CRUD khác.
            return ServiceResult<PassTypeResponse>.Conflict(DuplicateCodeMessage);
        }

        return ServiceResult<PassTypeResponse>.Ok(ToResponse(passType, 0));
    }

    /// <summary>
    /// Sửa tên/thời hạn/giá. KHÔNG sửa mã: mã là khoá nghiệp vụ bất biến (vé tháng tra loại vé
    /// theo Id, nhưng khách và client trao đổi bằng mã này).
    ///
    /// Đổi giá không làm vé đã bán nhảy theo — MonthlyPass.Price là ảnh chụp lúc mua.
    /// </summary>
    public async Task<ServiceResult<PassTypeResponse>> UpdateAsync(
        Guid id,
        UpdatePassTypeRequest request,
        CancellationToken cancellationToken = default)
    {
        var passType = await _db.PassTypes
            .FirstOrDefaultAsync(pt => pt.Id == id, cancellationToken);

        if (passType is null)
        {
            return ServiceResult<PassTypeResponse>.NotFound(NotFoundMessage);
        }

        passType.Name = request.Name!.Trim();
        passType.DurationMonths = request.DurationMonths!.Value;
        passType.Price = request.Price!.Value;
        passType.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);

        var usageCount = await _db.MonthlyPasses
            .CountAsync(p => p.PassTypeId == id, cancellationToken);

        return ServiceResult<PassTypeResponse>.Ok(ToResponse(passType, usageCount));
    }

    /// <summary>
    /// Xoá một loại vé CHƯA từng được dùng. Loại vé đã có vé tháng tham chiếu phải giữ lại:
    /// khoá ngoại Restrict chặn ở CSDL, service kiểm tra trước để trả 409 kèm câu giải thích
    /// thay vì lỗi 500.
    /// </summary>
    public async Task<ServiceResult<PassTypeResponse>> DeleteAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var passType = await _db.PassTypes
            .FirstOrDefaultAsync(pt => pt.Id == id, cancellationToken);

        if (passType is null)
        {
            return ServiceResult<PassTypeResponse>.NotFound(NotFoundMessage);
        }

        var inUse = await _db.MonthlyPasses
            .AnyAsync(p => p.PassTypeId == id, cancellationToken);

        if (inUse)
        {
            return ServiceResult<PassTypeResponse>.Conflict(InUseMessage);
        }

        var response = ToResponse(passType, 0);

        _db.PassTypes.Remove(passType);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Cửa sổ đua: một vé tháng được tạo bằng loại vé này giữa lúc kiểm tra và lúc xoá.
            // Khoá ngoại Restrict là lớp chặn cuối cùng.
            return ServiceResult<PassTypeResponse>.Conflict(InUseMessage);
        }

        return ServiceResult<PassTypeResponse>.Ok(response);
    }

    private Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken)
    {
        var lower = code.ToLowerInvariant();

        // ToLower() thay vì ILike — provider InMemory của bộ test không có ILike, cùng lối
        // AdminUserService và BusService.
        return _db.PassTypes.AnyAsync(pt => pt.Code.ToLower() == lower, cancellationToken);
    }

    private static PassTypeResponse ToResponse(PassType passType, int monthlyPassCount) => new()
    {
        Id = passType.Id,
        Code = passType.Code,
        Name = passType.Name,
        DurationMonths = passType.DurationMonths,
        Price = passType.Price,
        MonthlyPassCount = monthlyPassCount,
        CreatedAt = passType.CreatedAt,
        UpdatedAt = passType.UpdatedAt,
    };
}
