using Microsoft.EntityFrameworkCore;
using SmartBus.Api.Data;
using SmartBus.Api.Dtos.Vouchers;
using SmartBus.Api.Entities;

namespace SmartBus.Api.Services;

/// <summary>
/// Cài đặt <see cref="IVoucherAdminService"/> — hợp đồng đầy đủ ở mục "Voucher — /vouchers", phần
/// "CRUD voucher" của docs/api-contract.md.
///
/// Bốn quyết định đáng đọc trước khi sửa:
///
/// 1. <b>Mã chuẩn hoá CHỮ HOA trên MỌI đường ghi.</b> Unique index của CSDL phân biệt hoa thường,
///    nên "duy nhất" giữ được bằng chuẩn hoá ở tầng service + index là chốt cuối (ghi thẳng chữ
///    thường vẫn lọt qua index) — xem ghi chú ở AppDbContext.Voucher.cs. Chuẩn hoá ở service cũng
///    khiến khách gõ "summer10" vẫn ra, đúng như <see cref="VoucherValidationService"/> đã hứa.
/// 2. <b>Luật phụ thuộc kiểu giảm giá nằm ở đây, không phải attribute.</b> Cùng một cột
///    <c>DiscountValue</c> nhưng Percent đọc là 1–100 còn FixedAmount đọc là số tiền — attribute
///    không diễn đạt được luật hai nhánh, nên DTO chỉ chặn khoảng chung còn service chặn theo kiểu
///    và trả lỗi kèm đúng tên trường camelCase (hợp đồng mục CRUD voucher). Kiểu giảm giá và trạng
///    thái nhận dạng CHUỖI tên enum rồi parse ở đây (từ chối cả chuỗi số) — cùng lối
///    FareService.TryParsePassengerType.
/// 3. <b><c>usedCount</c> không bao giờ được đổi qua CRUD.</b> Nó tăng duy nhất ở
///    <see cref="VoucherRedemptionService"/> khi tiền về; DTO request không có trường này nên mọi
///    giá trị FE gửi lên bị bỏ qua — đúng lời hứa trong bảng trường entity Voucher.
/// 4. <b>Đổi trạng thái là endpoint riêng (PATCH), không gộp vào PUT.</b> Bật/tắt là quyết định
///    người vận hành làm hàng ngày; tách ra thì màn quản lý có nút bật/tắt mà không phải gửi lại
///    cả form sửa, và nhật ký kiểm toán ghi rõ một thao tác riêng.
///
/// Bảng <c>Vouchers</c> đã có migration (PR #144, Vàng Thị Dăm) nên service này chạy được trên CSDL
/// thật sau bước <c>dotnet ef database update</c> (docs/27 §4).
/// </summary>
public class VoucherAdminService : IVoucherAdminService
{
    private const string VoucherNotFoundMessage = "Không tìm thấy voucher";

    private const string RouteNotFoundMessage = "Không tìm thấy tuyến đường";

    private const string CodeExistsMessage = "Mã voucher đã tồn tại";

    private const string DiscountTypeInvalidMessage = "Loại giảm giá không hợp lệ";

    private const string StatusInvalidMessage = "Trạng thái voucher không hợp lệ";

    private const string PercentOutOfRangeMessage = "Phần trăm giảm phải từ 1 đến 100";

    private const string FixedAmountOutOfRangeMessage = "Số tiền giảm phải lớn hơn 0";

    private const string MaxDiscountOutOfRangeMessage = "Trần giảm phải lớn hơn 0";

    private const string QuantityBelowUsedMessage = "Số lượng không được nhỏ hơn số lượt đã dùng";

    private const string DateRangeMessage = "Thời điểm kết thúc phải sau thời điểm bắt đầu";

    private const int DefaultPageSize = 10;

    private readonly AppDbContext _db;

    public VoucherAdminService(AppDbContext db) => _db = db;

    public async Task<ServiceResult<VoucherListResponse>> ListAsync(
        ListVouchersRequest request,
        CancellationToken cancellationToken = default)
    {
        var page = request.Page ?? 1;
        var pageSize = request.PageSize ?? DefaultPageSize;

        VoucherStatus? statusFilter = null;

        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            if (!TryParseVoucherStatus(request.Status, out var parsed))
            {
                return ServiceResult<VoucherListResponse>.Invalid(
                    "Dữ liệu đầu vào không hợp lệ",
                    new Dictionary<string, string[]> { ["status"] = [StatusInvalidMessage] });
            }

            statusFilter = parsed;
        }

        var query = _db.Vouchers
            .AsNoTracking()
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            // So khớp không phân biệt hoa thường bằng ToLower() thay vì EF.Functions.ILike: câu
            // dịch sang PostgreSQL vẫn dùng được chỉ mục phù hợp, và provider InMemory của bộ test
            // không hỗ trợ ILike — cùng lối AdminUserService.ListAsync.
            var keyword = request.Search.Trim().ToLowerInvariant();
            query = query.Where(v =>
                v.Code.ToLower().Contains(keyword) ||
                v.Name.ToLower().Contains(keyword));
        }

        if (statusFilter is not null)
        {
            query = query.Where(v => v.Status == statusFilter);
        }

        var total = await query.CountAsync(cancellationToken);

        var rows = await query
            .OrderByDescending(v => v.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(v => new VoucherRow
            {
                Id = v.Id,
                Code = v.Code,
                Name = v.Name,
                DiscountType = v.DiscountType,
                DiscountValue = v.DiscountValue,
                MinOrderValue = v.MinOrderValue,
                MaxDiscount = v.MaxDiscount,
                RouteId = v.RouteId,
                RouteCode = v.Route == null ? null : v.Route.Code,
                RouteName = v.Route == null ? null : v.Route.Name,
                Quantity = v.Quantity,
                UsedCount = v.UsedCount,
                ValidFrom = v.ValidFrom,
                ValidUntil = v.ValidUntil,
                Status = v.Status,
                CreatedAt = v.CreatedAt,
                UpdatedAt = v.UpdatedAt,
            })
            .ToListAsync(cancellationToken);

        return ServiceResult<VoucherListResponse>.Ok(new VoucherListResponse
        {
            Items = rows.Select(ToResponse).ToList(),
            Total = total,
            Page = page,
            PageSize = pageSize,
        });
    }

    public async Task<ServiceResult<VoucherResponse>> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var row = await _db.Vouchers
            .AsNoTracking()
            .Where(v => v.Id == id)
            .Select(v => new VoucherRow
            {
                Id = v.Id,
                Code = v.Code,
                Name = v.Name,
                DiscountType = v.DiscountType,
                DiscountValue = v.DiscountValue,
                MinOrderValue = v.MinOrderValue,
                MaxDiscount = v.MaxDiscount,
                RouteId = v.RouteId,
                RouteCode = v.Route == null ? null : v.Route.Code,
                RouteName = v.Route == null ? null : v.Route.Name,
                Quantity = v.Quantity,
                UsedCount = v.UsedCount,
                ValidFrom = v.ValidFrom,
                ValidUntil = v.ValidUntil,
                Status = v.Status,
                CreatedAt = v.CreatedAt,
                UpdatedAt = v.UpdatedAt,
            })
            .FirstOrDefaultAsync(cancellationToken);

        return row is null
            ? ServiceResult<VoucherResponse>.NotFound(VoucherNotFoundMessage)
            : ServiceResult<VoucherResponse>.Ok(ToResponse(row));
    }

    public async Task<ServiceResult<VoucherResponse>> CreateAsync(
        CreateVoucherRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!TryParseDiscountType(request.DiscountType, out var discountType))
        {
            return InvalidField("discountType", DiscountTypeInvalidMessage);
        }

        var normalizedCode = NormalizeCode(request.Code!);

        if (await CodeExistsAsync(normalizedCode, excludeId: null, cancellationToken))
        {
            return ServiceResult<VoucherResponse>.Conflict(CodeExistsMessage);
        }

        // Luật phụ thuộc kiểu giảm giá + thứ tự hiệu lực + tuyến tham chiếu. Tách khỏi kiểm trùng
        // mã ở trên: trùng mã là xung đột trạng thái (409), còn đây là dữ liệu gửi lên sai (400/404).
        var validation = await ValidateBusinessRulesAsync(
            discountType,
            request.DiscountValue,
            request.MaxDiscount,
            request.ValidFrom,
            request.ValidUntil,
            request.RouteId,
            quantity: request.Quantity,
            usedCount: 0,
            cancellationToken);

        if (validation is not null)
        {
            return validation;
        }

        var now = DateTime.UtcNow;
        var voucher = new Voucher
        {
            Code = normalizedCode,
            Name = request.Name!.Trim(),
            DiscountType = discountType,
            DiscountValue = request.DiscountValue!.Value,
            MinOrderValue = request.MinOrderValue ?? 0m,
            // Trần chỉ có nghĩa cho Percent — FixedAmount thì ép null (trần với số tiền cố định là
            // khái niệm vô nghĩa, để 0 thay null sẽ thành "trần 0đ" và vô hiệu hoá voucher).
            MaxDiscount = discountType == VoucherDiscountType.Percent ? request.MaxDiscount : null,
            RouteId = request.RouteId,
            Quantity = request.Quantity!.Value,
            UsedCount = 0,
            ValidFrom = AsUtc(request.ValidFrom!.Value),
            ValidUntil = AsUtc(request.ValidUntil!.Value),
            // Voucher mới luôn Active — đổi trạng thái là việc của PATCH, không nhận qua POST.
            Status = VoucherStatus.Active,
            CreatedAt = now,
        };

        _db.Vouchers.Add(voucher);
        await _db.SaveChangesAsync(cancellationToken);

        return ServiceResult<VoucherResponse>.Ok(await ToResponseAsync(voucher.Id, cancellationToken));
    }

    public async Task<ServiceResult<VoucherResponse>> UpdateAsync(
        Guid id,
        UpdateVoucherRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!TryParseDiscountType(request.DiscountType, out var discountType))
        {
            return InvalidField("discountType", DiscountTypeInvalidMessage);
        }

        var voucher = await _db.Vouchers.FirstOrDefaultAsync(v => v.Id == id, cancellationToken);

        if (voucher is null)
        {
            return ServiceResult<VoucherResponse>.NotFound(VoucherNotFoundMessage);
        }

        var normalizedCode = NormalizeCode(request.Code!);

        if (await CodeExistsAsync(normalizedCode, excludeId: id, cancellationToken))
        {
            return ServiceResult<VoucherResponse>.Conflict(CodeExistsMessage);
        }

        var validation = await ValidateBusinessRulesAsync(
            discountType,
            request.DiscountValue,
            request.MaxDiscount,
            request.ValidFrom,
            request.ValidUntil,
            request.RouteId,
            quantity: request.Quantity,
            usedCount: voucher.UsedCount,
            cancellationToken);

        if (validation is not null)
        {
            return validation;
        }

        voucher.Code = normalizedCode;
        voucher.Name = request.Name!.Trim();
        voucher.DiscountType = discountType;
        voucher.DiscountValue = request.DiscountValue!.Value;
        voucher.MinOrderValue = request.MinOrderValue ?? 0m;
        voucher.MaxDiscount = discountType == VoucherDiscountType.Percent ? request.MaxDiscount : null;
        voucher.RouteId = request.RouteId;
        voucher.Quantity = request.Quantity!.Value;
        voucher.ValidFrom = AsUtc(request.ValidFrom!.Value);
        voucher.ValidUntil = AsUtc(request.ValidUntil!.Value);
        voucher.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);

        return ServiceResult<VoucherResponse>.Ok(await ToResponseAsync(voucher.Id, cancellationToken));
    }

    public async Task<ServiceResult<VoucherResponse>> UpdateStatusAsync(
        Guid id,
        UpdateVoucherStatusRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!TryParseVoucherStatus(request.Status, out var status))
        {
            return InvalidField("status", StatusInvalidMessage);
        }

        var voucher = await _db.Vouchers.FirstOrDefaultAsync(v => v.Id == id, cancellationToken);

        if (voucher is null)
        {
            return ServiceResult<VoucherResponse>.NotFound(VoucherNotFoundMessage);
        }

        voucher.Status = status;
        voucher.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);

        return ServiceResult<VoucherResponse>.Ok(await ToResponseAsync(voucher.Id, cancellationToken));
    }

    // ---------------------------------------------------------------------------------------
    // Kiểm tra nghiệp vụ chung cho tạo và sửa — trả null khi mọi luật thoả, ngược lại là kết quả
    // lỗi đã đóng gói sẵn. Tách riêng để tạo và sửa không chép nhau một bảng luật (hai bản sẽ lệch,
    // và bản lệch là bản quyết định tiền) — cùng lý do VoucherValidationService.ReasonFor dùng chung
    // cho kiểm tra và tiêu thụ.
    // ---------------------------------------------------------------------------------------

    private async Task<ServiceResult<VoucherResponse>?> ValidateBusinessRulesAsync(
        VoucherDiscountType discountType,
        decimal? discountValue,
        decimal? maxDiscount,
        DateTime? validFrom,
        DateTime? validUntil,
        Guid? routeId,
        int? quantity,
        int usedCount,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();

        // Giá trị giảm đọc theo kiểu — cùng một cột nhưng hai nghĩa (xem VoucherDiscountType).
        if (discountType == VoucherDiscountType.Percent
            && discountValue is < 1 or > 100)
        {
            errors["discountValue"] = [PercentOutOfRangeMessage];
        }

        if (discountType == VoucherDiscountType.FixedAmount
            && discountValue is <= 0)
        {
            errors["discountValue"] = [FixedAmountOutOfRangeMessage];
        }

        // Trần 0đ với Percent vô hiệu hoá luôn voucher (Min(rounded, 0) = 0) — từ chối ngay từ cửa.
        if (discountType == VoucherDiscountType.Percent
            && maxDiscount is <= 0)
        {
            errors["maxDiscount"] = [MaxDiscountOutOfRangeMessage];
        }

        // Cạn lượt là UsedCount chạm Quantity; hạ trần xuống dưới số đã tiêu thụ là dữ liệu tự mâu
        // thuẫn. Lúc tạo usedCount = 0 nên luật này chỉ thật sự chặn ở đường sửa.
        if (quantity is not null && quantity < usedCount)
        {
            errors["quantity"] = [QuantityBelowUsedMessage];
        }

        // Khoảng hiệu lực rỗng (bằng nhau) cũng bị từ chối — cùng câu DriverService.DateRangeMessage.
        if (validFrom is not null && validUntil is not null && validUntil <= validFrom)
        {
            errors["validUntil"] = [DateRangeMessage];
        }

        if (errors.Count > 0)
        {
            return ServiceResult<VoucherResponse>.Invalid("Dữ liệu đầu vào không hợp lệ", errors);
        }

        // Tuyến là tham chiếu cứng: GUID trỏ tới tuyến không tồn tại là lỗi phía gọi (404), không
        // phải lỗi khuôn dữ liệu — cùng lối tripId của POST /vouchers/validate và routeId của
        // MonthlyPassRegistrationService.
        if (routeId is not null
            && !await _db.Routes.AsNoTracking().AnyAsync(r => r.Id == routeId, cancellationToken))
        {
            return ServiceResult<VoucherResponse>.NotFound(RouteNotFoundMessage);
        }

        return null;
    }

    private async Task<bool> CodeExistsAsync(
        string normalizedCode,
        Guid? excludeId,
        CancellationToken cancellationToken)
        => await _db.Vouchers
            .AsNoTracking()
            .AnyAsync(v => v.Code == normalizedCode && (excludeId == null || v.Id != excludeId), cancellationToken);

    /// <summary>Đúng một chuẩn hoá cho mọi đường đọc/ghi — chép lại lối VoucherValidationService.</summary>
    private static string NormalizeCode(string code) => code.Trim().ToUpperInvariant();

    /// <summary>
    /// Mọi thời điểm lưu dạng UTC (quy ước A3 — timestamptz). FE gửi ISO có hậu tố Z nên giá trị
    /// bind lên thường đã là Kind=Utc; SpecifyKind giữ nhất quán cho ca FE gửi chuỗi không offset —
    /// cùng lối TripAssignmentService đã làm cho thời điểm từ request.
    /// </summary>
    private static DateTime AsUtc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);

    /// <summary>
    /// Parse chuỗi tên enum — so khớp với DANH SÁCH TÊN của enum thay vì dùng Enum.TryParse: bản
    /// TryParse chấp nhận cả chuỗi số ("3" sẽ thành một enum hợp lệ) nên client gửi số vẫn lọt qua,
    /// còn bỏ qua hoa/thường để "percent" hay "PERCENT" đều nhận. Giá trị lưu xuống CSDL luôn là
    /// tên chuẩn của enum — cùng lối FareService.TryParsePassengerType.
    /// </summary>
    private static bool TryParseDiscountType(string? value, out VoucherDiscountType discountType)
    {
        var trimmed = value?.Trim();

        foreach (var name in Enum.GetNames<VoucherDiscountType>())
        {
            if (string.Equals(name, trimmed, StringComparison.OrdinalIgnoreCase))
            {
                discountType = Enum.Parse<VoucherDiscountType>(name);
                return true;
            }
        }

        discountType = default;
        return false;
    }

    /// <summary>Parse chuỗi tên enum trạng thái — cùng lối <see cref="TryParseDiscountType"/>.</summary>
    private static bool TryParseVoucherStatus(string? value, out VoucherStatus status)
    {
        var trimmed = value?.Trim();

        foreach (var name in Enum.GetNames<VoucherStatus>())
        {
            if (string.Equals(name, trimmed, StringComparison.OrdinalIgnoreCase))
            {
                status = Enum.Parse<VoucherStatus>(name);
                return true;
            }
        }

        status = default;
        return false;
    }

    private static ServiceResult<VoucherResponse> InvalidField(string field, string message)
        => ServiceResult<VoucherResponse>.Invalid(
            "Dữ liệu đầu vào không hợp lệ",
            new Dictionary<string, string[]> { [field] = [message] });

    // ---------------------------------------------------------------------------------------
    // Phép chiếu + đổi dạng — dùng chung cho mọi đường trả VoucherResponse.
    // ---------------------------------------------------------------------------------------

    private async Task<VoucherResponse> ToResponseAsync(Guid id, CancellationToken cancellationToken)
    {
        var row = await _db.Vouchers
            .AsNoTracking()
            .Where(v => v.Id == id)
            .Select(v => new VoucherRow
            {
                Id = v.Id,
                Code = v.Code,
                Name = v.Name,
                DiscountType = v.DiscountType,
                DiscountValue = v.DiscountValue,
                MinOrderValue = v.MinOrderValue,
                MaxDiscount = v.MaxDiscount,
                RouteId = v.RouteId,
                RouteCode = v.Route == null ? null : v.Route.Code,
                RouteName = v.Route == null ? null : v.Route.Name,
                Quantity = v.Quantity,
                UsedCount = v.UsedCount,
                ValidFrom = v.ValidFrom,
                ValidUntil = v.ValidUntil,
                Status = v.Status,
                CreatedAt = v.CreatedAt,
                UpdatedAt = v.UpdatedAt,
            })
            .SingleAsync(cancellationToken);

        return ToResponse(row);
    }

    /// <summary>
    /// Hàng trung gian của phép chiếu — <see cref="VoucherRow.DiscountType"/> và
    /// <see cref="VoucherRow.Status"/> giữ enum để EF dịch được, rồi mới <c>ToString()</c> TRONG BỘ
    /// NHỚ khi đưa vào DTO: gọi <c>ToString()</c> ngay trong projection là thứ không dịch được sang
    /// SQL (cùng bẫy đã gặp ở FeedbackStatisticsService).
    /// </summary>
    private sealed class VoucherRow
    {
        public Guid Id { get; init; }

        public string Code { get; init; } = string.Empty;

        public string Name { get; init; } = string.Empty;

        public VoucherDiscountType DiscountType { get; init; }

        public decimal DiscountValue { get; init; }

        public decimal MinOrderValue { get; init; }

        public decimal? MaxDiscount { get; init; }

        public Guid? RouteId { get; init; }

        public string? RouteCode { get; init; }

        public string? RouteName { get; init; }

        public int Quantity { get; init; }

        public int UsedCount { get; init; }

        public DateTime ValidFrom { get; init; }

        public DateTime ValidUntil { get; init; }

        public VoucherStatus Status { get; init; }

        public DateTime CreatedAt { get; init; }

        public DateTime? UpdatedAt { get; init; }
    }

    private static VoucherResponse ToResponse(VoucherRow row) => new()
    {
        Id = row.Id,
        Code = row.Code,
        Name = row.Name,
        DiscountType = row.DiscountType.ToString(),
        DiscountValue = row.DiscountValue,
        MinOrderValue = row.MinOrderValue,
        MaxDiscount = row.MaxDiscount,
        RouteId = row.RouteId,
        RouteCode = row.RouteCode,
        RouteName = row.RouteName,
        Quantity = row.Quantity,
        UsedCount = row.UsedCount,
        ValidFrom = row.ValidFrom,
        ValidUntil = row.ValidUntil,
        Status = row.Status.ToString(),
        CreatedAt = row.CreatedAt,
        UpdatedAt = row.UpdatedAt,
    };
}
