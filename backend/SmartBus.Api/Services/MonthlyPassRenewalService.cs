using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using SmartBus.Api.Data;
using SmartBus.Api.Dtos.MonthlyPasses;
using SmartBus.Api.Entities;

namespace SmartBus.Api.Services;

/// <summary>
/// Cài đặt <see cref="IMonthlyPassRenewalService"/> — hợp đồng đầy đủ ở mục
/// "Vé tháng — /monthly-passes" của docs/api-contract.md.
///
/// Ba quyết định đáng đọc cùng:
///   - Dòng cũ KHÔNG bị sửa (kể cả Status/UpdatedAt) — lịch sử và mốc tính kỳ kế tiếp nằm ở đó.
///   - Vé đã hết hạn thì hiệu lực từ BÂY GIỜ, không hồi tố: hồi tố là tặng không một khoảng thời
///     gian đã trôi qua mà khách không dùng được gì.
///   - Chồng lấn so theo KHOẢNG ValidFrom/ValidTo chứ không so Status — cột Status có độ trễ
///     (job quét của Kiên), so nó là so sai (AppDbContext.MonthlyPass.cs nói rõ).
/// </summary>
public class MonthlyPassRenewalService : IMonthlyPassRenewalService
{
    private const string PassNotFoundMessage = "Không tìm thấy vé tháng";
    private const string PassTypeNotFoundMessage = "Không tìm thấy loại vé";
    private const string RouteNotFoundMessage = "Không tìm thấy tuyến đường";
    private const string OverlapMessage = "Đã có vé tháng khác trên cùng tuyến trong khoảng thời gian này";
    private const string CodeCollisionMessage = "Không sinh được mã vé tháng, vui lòng thử lại";

    /// <summary>Bộ ký tự sinh 6 ký tự cuối mã vé — trùng bộ ký tự frontend đang dùng (monthlyPassApi.ts).</summary>
    private const string CodeAlphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";

    private const int CodeSuffixLength = 6;

    /// <summary>
    /// Số lần thử sinh mã chưa trùng. 36^6 ≈ 2,2 tỷ mã nên một lần thử gần như chắc chắn đã sạch;
    /// vài lần là để chắc chắn tuyệt đối. Hết lượt vẫn trùng thì trả 409 chứ không ghi liều —
    /// cột Code có ràng buộc unique ở CSDL.
    /// </summary>
    private const int CodeAttempts = 5;

    private readonly AppDbContext _db;

    public MonthlyPassRenewalService(AppDbContext db) => _db = db;

    public async Task<ServiceResult<MonthlyPassResponse>> RenewAsync(
        Guid id,
        Guid userId,
        RenewMonthlyPassRequest? request,
        CancellationToken cancellationToken = default)
    {
        // Điều kiện UserId nằm NGAY trong truy vấn: vé của người khác rơi vào cùng nhánh null với
        // vé không tồn tại, nên hai ca chắc chắn trả cùng một câu — không lộ vé của người khác.
        var pass = await _db.MonthlyPasses
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id && p.UserId == userId, cancellationToken);

        if (pass is null)
        {
            return ServiceResult<MonthlyPassResponse>.NotFound(PassNotFoundMessage);
        }

        // Loại vé: bỏ trống mã = giữ nguyên loại của vé cũ. Tra lại từ bảng chứ không dùng
        // navigation: cần đúng GIÁ HIỆN HÀNH để chụp (giá gói sửa được sau khi bán) và mã chuỗi
        // để trả về. Dòng mồ côi là chuyện chỉ có ở dữ liệu hỏng (FK Restrict chặn xoá thật) —
        // trả 404 thay vì để nổ NullReferenceException.
        var requestedCode = request?.PassTypeCode?.Trim();

        var passType = string.IsNullOrWhiteSpace(requestedCode)
            ? await _db.PassTypes.AsNoTracking().FirstOrDefaultAsync(
                pt => pt.Id == pass.PassTypeId, cancellationToken)
            : await _db.PassTypes.AsNoTracking().FirstOrDefaultAsync(
                pt => pt.Code == requestedCode, cancellationToken);

        if (passType is null)
        {
            return ServiceResult<MonthlyPassResponse>.NotFound(PassTypeNotFoundMessage);
        }

        // Mã tuyến để sinh mã vé (khuôn MP-{mã tuyến}-{6 ký tự}). Cùng lối trên: tuyến mồ côi
        // trả 404 thay vì để nổ.
        var routeCode = await _db.Routes
            .AsNoTracking()
            .Where(r => r.Id == pass.RouteId)
            .Select(r => r.Code)
            .FirstOrDefaultAsync(cancellationToken);

        if (routeCode is null)
        {
            return ServiceResult<MonthlyPassResponse>.NotFound(RouteNotFoundMessage);
        }

        var now = DateTime.UtcNow;

        // "Tính ngày hiệu lực kế tiếp": vé còn hạn thì nối đuôi liền mạch (ValidFrom mới =
        // ValidTo cũ), hết hạn thì bắt đầu từ bây giờ — không hồi tố. Cộng bằng AddMonths (tháng
        // lịch, ngày cuối tháng tự kẹp: 31/01 + 1 tháng = 28/02) chứ không cộng số ngày.
        var validFrom = pass.ValidTo > now ? pass.ValidTo : now;
        var validTo = validFrom.AddMonths(passType.DurationMonths);

        if (await OverlapsAnotherPassAsync(pass, validFrom, validTo, cancellationToken))
        {
            return ServiceResult<MonthlyPassResponse>.Conflict(OverlapMessage);
        }

        var code = await GenerateCodeAsync(routeCode, cancellationToken);

        if (code is null)
        {
            return ServiceResult<MonthlyPassResponse>.Conflict(CodeCollisionMessage);
        }

        var newPass = new MonthlyPass
        {
            UserId = pass.UserId,
            RouteId = pass.RouteId,
            PassTypeId = passType.Id,
            // Ảnh chụp giá hiện hành — cùng lý do cột Price của entity: đổi giá gói sau này không
            // được làm doanh thu kỳ này nhảy theo.
            Price = passType.Price,
            ValidFrom = validFrom,
            ValidTo = validTo,
            Code = code,
            // Status nhận mặc định Active của entity. Không suy từ ngày: vé kỳ sau nằm ở tương
            // lai vẫn Active trong cột lưu trữ — hiệu lực hỏi bằng cặp ValidFrom/ValidTo.
        };

        _db.MonthlyPasses.Add(newPass);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Cửa sổ đua: hai request cùng sinh trúng một mã trong khoảng giữa kiểm tra và ghi.
            // Ràng buộc unique trên cột Code là lớp chặn cuối cùng — cùng lối các service CRUD.
            return ServiceResult<MonthlyPassResponse>.Conflict(CodeCollisionMessage);
        }

        return ServiceResult<MonthlyPassResponse>.Ok(ToResponse(newPass, passType.Code));
    }

    /// <summary>
    /// Vé khác của cùng người trên cùng tuyến có khoảng hiệu lực chồng lấn khoảng mới không.
    ///
    /// So nửa khoảng [ValidFrom, ValidTo): vé nối đuôi (ValidFrom mới = ValidTo vé kia) KHÔNG
    /// tính chồng — đó chính là ca gia hạn bình thường. Trừ chính vé đang gia hạn ra: nó là mốc
    /// nối, không phải đối thủ.
    /// </summary>
    private async Task<bool> OverlapsAnotherPassAsync(
        MonthlyPass pass,
        DateTime validFrom,
        DateTime validTo,
        CancellationToken cancellationToken)
    {
        var others = await _db.MonthlyPasses
            .AsNoTracking()
            .Where(p => p.UserId == pass.UserId && p.RouteId == pass.RouteId && p.Id != pass.Id)
            .Select(p => new { p.ValidFrom, p.ValidTo })
            .ToListAsync(cancellationToken);

        return others.Any(p => p.ValidFrom < validTo && validFrom < p.ValidTo);
    }

    /// <summary>
    /// Mã vé chưa trùng, hoặc null khi hết lượt thử. Kiểm tra trước khi ghi để ca thường không bao
    /// giờ chạm tới ràng buộc unique; trùng ở khe đua thì SaveChanges ném DbUpdateException.
    /// </summary>
    private async Task<string?> GenerateCodeAsync(string routeCode, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < CodeAttempts; attempt++)
        {
            var candidate = $"MP-{routeCode}-{RandomSuffix()}";

            if (!await _db.MonthlyPasses.AnyAsync(p => p.Code == candidate, cancellationToken))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>
    /// 6 ký tự ngẫu nhiên. Dùng RandomNumberGenerator chứ không Random.Shared: mã vé là nội dung
    /// QR soát vé — đoán được mã nghĩa là qua được cửa soát vé của người khác, nên dùng nguồn ngẫu
    /// nhiên mật mã, cùng lối TokenService sinh refresh token.
    /// </summary>
    private static string RandomSuffix()
    {
        var suffix = new char[CodeSuffixLength];

        for (var i = 0; i < suffix.Length; i++)
        {
            suffix[i] = CodeAlphabet[RandomNumberGenerator.GetInt32(CodeAlphabet.Length)];
        }

        return new string(suffix);
    }

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
