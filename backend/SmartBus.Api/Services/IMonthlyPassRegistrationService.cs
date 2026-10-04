using SmartBus.Api.Dtos.MonthlyPasses;

namespace SmartBus.Api.Services;

/// <summary>
/// Đăng ký vé tháng cho chính người gọi (POST /api/monthly-passes) — story 16, Trần Trung Hiếu.
/// Hợp đồng đầy đủ ở mục "Vé tháng — /monthly-passes" của docs/api-contract.md.
///
/// Cố ý đứng riêng thay vì nối vào <see cref="IMonthlyPassRenewalService"/> (cùng bảng, khác
/// nghiệp vụ: đăng ký bắt đầu từ bây giờ, gia hạn nối đuôi vé cũ — và hai task thuộc hai người):
/// mỗi bề mặt một service, cùng khuôn cặp IStopService / IRouteStopService ở Sprint 1.
///
/// Yêu cầu đăng nhập nhưng KHÔNG yêu cầu vai trò cụ thể — cùng lối gia hạn: RBAC của dự án không
/// có policy Passenger, quyền sở hữu nằm ngay trong luồng (vé luôn gắn <c>userId</c> của người
/// gọi, Controller lấy từ claim NameIdentifier).
/// </summary>
public interface IMonthlyPassRegistrationService
{
    /// <summary>
    /// Tạo vé tháng mới của <paramref name="userId"/> trên tuyến <c>request.RouteId</c> với loại
    /// vé <c>request.PassTypeCode</c>: hiệu lực từ bây giờ, cộng <c>DurationMonths</c> của loại vé
    /// theo tháng lịch, giá chụp từ bảng PassTypes, mã vé server sinh.
    ///
    /// Thiếu <c>routeId</c>/<c>passTypeCode</c> → 400 (chặn ở Controller bằng <c>[Required]</c>);
    /// tuyến không tồn tại → 404; loại vé không tồn tại → 404; khoảng hiệu lực mới chồng lấn vé
    /// khác của cùng người trên cùng tuyến → 409 (so khoảng [ValidFrom, ValidTo), không so Status).
    /// </summary>
    Task<ServiceResult<MonthlyPassResponse>> RegisterAsync(
        RegisterMonthlyPassRequest request,
        Guid userId,
        CancellationToken cancellationToken = default);
}
