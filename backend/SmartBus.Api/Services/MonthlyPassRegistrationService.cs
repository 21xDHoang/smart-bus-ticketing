using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using SmartBus.Api.Data;
using SmartBus.Api.Dtos.MonthlyPasses;
using SmartBus.Api.Entities;

namespace SmartBus.Api.Services;

/// <summary>
/// Cài đặt <see cref="IMonthlyPassRegistrationService"/> — hợp đồng đầy đủ ở mục
/// "Vé tháng — /monthly-passes" của docs/api-contract.md.
///
/// Bám khuôn <see cref="MonthlyPassRenewalService"/> (cùng bảng, cùng luật): cùng câu thông báo
/// cho tuyến/loại vé, cùng cách sinh mã vé, cùng luật chồng lấn khoảng [ValidFrom, ValidTo).
/// Khác ở hai chỗ cố ý: vé mới bắt đầu hiệu lực từ BÂY GIỜ (gia hạn còn nối đuôi vé cũ khi vé cũ
/// còn hạn), và phép so chồng lấn không có vé cũ để trừ ra — mọi vé của người gọi trên tuyến đều
/// là "vé khác".
///
/// Việc chặn trùng vé tháng đang hoạt động trên cùng tuyến (task "Validate trùng vé tháng đang
/// hoạt động trên cùng tuyến") nằm ở đây — đúng chỗ duy nhất nó có thể nằm, vì không có ràng
/// buộc CSDL nào diễn đạt được "đang hoạt động" (AppDbContext.MonthlyPass.cs nói rõ).
/// </summary>
public class MonthlyPassRegistrationService : IMonthlyPassRegistrationService
{
    private const string RouteNotFoundMessage = "Không tìm thấy tuyến đường";

    private const string PassTypeNotFoundMessage = "Không tìm thấy loại vé";

    private const string OverlapMessage = "Đã có vé tháng khác trên cùng tuyến trong khoảng thời gian này";

    private const string CodeCollisionMessage = "Không sinh được mã vé tháng, vui lòng thử lại";

    /// <summary>Bộ ký tự sinh 6 ký tự cuối mã vé — trùng bộ ký tự frontend đang dùng (monthlyPassApi.ts).</summary>
    private const string CodeAlphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";

    private const int CodeSuffixLength = 6;

    /// <summary>
    /// Số lần thử sinh mã chưa trùng — cùng con số và cùng lý do với MonthlyPassRenewalService.
    /// 36^6 ≈ 2,2 tỷ mã nên một lần thử gần như chắc chắn đã sạch; hết lượt vẫn trùng thì trả 409
    /// chứ không ghi liều — cột Code có ràng buộc unique ở CSDL.
    /// </summary>
    private const int CodeAttempts = 5;

    private readonly AppDbContext _db;

    public MonthlyPassRegistrationService(AppDbContext db) => _db = db;

    public async Task<ServiceResult<MonthlyPassResponse>> RegisterAsync(
        RegisterMonthlyPassRequest request,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        // [Required] ở RegisterMonthlyPassRequest đã chặn thiếu routeId trước khi vào đây.
        var routeId = request.RouteId!.Value;

        // routeId là tham chiếu cứng: GUID trỏ tới tuyến không tồn tại là lỗi gọi, không phải
        // "không có gì để đăng ký" — cùng câu trả lời của GET /trips/search. Đọc cả mã tuyến ngay
        // trong lượt này: cần nó để sinh mã vé khuôn MP-{mã tuyến}-{6 ký tự}.
        var routeCode = await _db.Routes
            .AsNoTracking()
            .Where(r => r.Id == routeId)
            .Select(r => r.Code)
            .FirstOrDefaultAsync(cancellationToken);

        if (routeCode is null)
        {
            return ServiceResult<MonthlyPassResponse>.NotFound(RouteNotFoundMessage);
        }

        // Loại vé tra theo mã chuỗi — cùng lối gia hạn. Cần đúng GIÁ HIỆN HÀNH để chụp vào cột
        // Price (giá gói sửa được sau khi bán) và DurationMonths để tính thời hạn.
        var requestedCode = request.PassTypeCode!.Trim();

        var passType = await _db.PassTypes
            .AsNoTracking()
            .FirstOrDefaultAsync(pt => pt.Code == requestedCode, cancellationToken);

        if (passType is null)
        {
            return ServiceResult<MonthlyPassResponse>.NotFound(PassTypeNotFoundMessage);
        }

        // Hiệu lực từ thời điểm đăng ký — vé mới mua dùng được ngay, không hồi tố. Nối đuôi vé cũ
        // là chuyện của gia hạn, không phải đăng ký. Cộng bằng AddMonths (tháng lịch, ngày cuối
        // tháng tự kẹp: 31/01 + 1 tháng = 28/02) chứ không cộng số ngày — cùng lối gia hạn.
        var now = DateTime.UtcNow;
        var validFrom = now;
        var validTo = now.AddMonths(passType.DurationMonths);

        // Chặn trùng vé tháng đang hoạt động trên cùng tuyến: so khoảng [ValidFrom, ValidTo)
        // chồng lấn chứ không so cột Status — cột Status có độ trễ (job quét của Kiên), so nó là
        // so sai (AppDbContext.MonthlyPass.cs nói rõ). Khác gia hạn ở chỗ không có vé cũ để trừ
        // ra: mọi vé của người gọi trên tuyến này đều là "vé khác".
        if (await OverlapsAnyPassAsync(userId, routeId, validFrom, validTo, cancellationToken))
        {
            return ServiceResult<MonthlyPassResponse>.Conflict(OverlapMessage);
        }

        var code = await GenerateCodeAsync(routeCode, cancellationToken);

        if (code is null)
        {
            return ServiceResult<MonthlyPassResponse>.Conflict(CodeCollisionMessage);
        }

        var pass = new MonthlyPass
        {
            UserId = userId,
            RouteId = routeId,
            PassTypeId = passType.Id,
            // Ảnh chụp giá hiện hành — cùng lý do cột Price của entity: đổi giá gói sau này không
            // được làm doanh thu kỳ này nhảy theo.
            Price = passType.Price,
            ValidFrom = validFrom,
            ValidTo = validTo,
            Code = code,
            // Status nhận mặc định Active của entity. Vé mới đăng ký luôn Active trong cột lưu
            // trữ — hiệu lực thật vẫn phải hỏi bằng cặp ValidFrom/ValidTo.
        };

        _db.MonthlyPasses.Add(pass);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Cửa sổ đua: hai request cùng sinh trúng một mã trong khoảng giữa kiểm tra và ghi.
            // Ràng buộc unique trên cột Code là lớp chặn cuối cùng — cùng lối gia hạn.
            return ServiceResult<MonthlyPassResponse>.Conflict(CodeCollisionMessage);
        }

        return ServiceResult<MonthlyPassResponse>.Ok(ToResponse(pass, passType.Code));
    }

    /// <summary>
    /// Vé của cùng người trên cùng tuyến có khoảng hiệu lực chồng lấn khoảng mới không.
    ///
    /// So nửa khoảng [ValidFrom, ValidTo): vé nối đuôi (mốc này ValidFrom = mốc kia ValidTo)
    /// KHÔNG tính chồng — cùng luật gia hạn. Không trừ vé nào ra: đăng ký không có "vé cũ" để nối,
    /// mọi vé đều là đối thủ.
    /// </summary>
    private async Task<bool> OverlapsAnyPassAsync(
        Guid userId,
        Guid routeId,
        DateTime validFrom,
        DateTime validTo,
        CancellationToken cancellationToken)
    {
        var existing = await _db.MonthlyPasses
            .AsNoTracking()
            .Where(p => p.UserId == userId && p.RouteId == routeId)
            .Select(p => new { p.ValidFrom, p.ValidTo })
            .ToListAsync(cancellationToken);

        return existing.Any(p => p.ValidFrom < validTo && validFrom < p.ValidTo);
    }

    /// <summary>
    /// Mã vé chưa trùng, hoặc null khi hết lượt thử. Chép từ MonthlyPassRenewalService — cố ý
    /// không tách lớp dùng chung: tách ra thì phải sửa file của người khác, mà luật nhóm không
    /// cho sửa file của người khác. Các bản giống nhau là cái giá rẻ hơn.
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
    /// nhiên mật mã — cùng lối MonthlyPassRenewalService và TokenService.
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
