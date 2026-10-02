using SmartBus.Api.Dtos.MonthlyPasses;

namespace SmartBus.Api.Services;

/// <summary>
/// Gia hạn vé tháng (POST /api/monthly-passes/{id}/renew) — story 16 "Đăng ký vé tháng",
/// Phùng Duy Hoàng. Hợp đồng đầy đủ ở mục "Vé tháng — /monthly-passes" của docs/api-contract.md.
///
/// Quy tắc lõi (chốt trong entity <see cref="Entities.MonthlyPass"/> của Vàng Thị Dăm): gia hạn =
/// ghi thêm MỘT dòng mới với ValidFrom = ValidTo của vé cũ; dòng cũ KHÔNG bị sửa — ở lại làm lịch
/// sử và làm mốc tính kỳ kế tiếp.
///
/// Cố ý đứng riêng thay vì nối vào service đăng ký vé tháng (POST /monthly-passes — Trần Trung
/// Hiếu, chưa làm): hai thao tác khác nghiệp vụ (đăng ký tính mốc từ bây giờ, gia hạn nối từ vé
/// cũ) — cùng lối các cặp service đã tách từ Sprint 1 (IStopService / IRouteStopService).
/// </summary>
public interface IMonthlyPassRenewalService
{
    /// <summary>
    /// Gia hạn một vé tháng của chính người gọi, trả về DÒNG MỚI vừa ghi.
    ///
    /// <paramref name="userId"/> là người gọi lấy từ token; vé của người khác trả 404 cùng câu với
    /// vé không tồn tại — không phân biệt để không lộ vé của người khác có tồn tại.
    /// <paramref name="request"/> null hoặc PassTypeCode rỗng = giữ nguyên loại vé của vé cũ.
    /// Khoảng hiệu lực mới chồng lấn vé khác cùng người cùng tuyến → 409.
    /// </summary>
    Task<ServiceResult<MonthlyPassResponse>> RenewAsync(
        Guid id,
        Guid userId,
        RenewMonthlyPassRequest? request,
        CancellationToken cancellationToken = default);
}
