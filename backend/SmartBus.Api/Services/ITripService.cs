using SmartBus.Api.Dtos.Trips;

namespace SmartBus.Api.Services;

/// <summary>
/// Nghiệp vụ chuyến xe (story 13) — Vàng Thị Dăm.
/// Hợp đồng đầy đủ ở mục "Chuyến xe — /trips" của docs/api-contract.md.
///
/// Phần việc này CHỈ ĐỌC: story 13 giao cho Dăm "API chi tiết chuyến". Việc tạo chuyến (CRUD lịch
/// trình theo tuyến) là của Hiếu, sinh chuyến tự động là của Kiên, danh sách chuyến theo ngày là
/// của Hoàng — nên interface này cố ý không có phương thức ghi nào.
///
/// Việc chặn người không phải Admin/Quản lý là của <see cref="RbacPolicies.ManagerOrAbove"/> gắn ở
/// Controller, không lặp lại ở đây — Service không biết ai đang gọi.
/// </summary>
public interface ITripService
{
    /// <summary>
    /// Chi tiết một chuyến: giờ chạy, tuyến, xe (loại xe + sức chứa) và danh sách trạm dừng.
    ///
    /// Chuyến không tồn tại → 404. Chuyến có thật nhưng tuyến hoặc xe của nó không còn trong CSDL
    /// cũng → 404 (dữ liệu mồ côi, không phải chuyến để hiển thị) — hai khoá ngoại đều Restrict nên
    /// đường đi thường không tới đây.
    ///
    /// Tuyến của chuyến chưa gán trạm nào trả về <c>stops</c> RỖNG, không phải 404.
    /// </summary>
    Task<ServiceResult<TripDetailResponse>> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);
}
