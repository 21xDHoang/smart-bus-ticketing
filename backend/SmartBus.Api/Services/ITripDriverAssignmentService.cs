using SmartBus.Api.Dtos.Trips;

namespace SmartBus.Api.Services;

/// <summary>
/// Gán tài xế vào chuyến — phần "tài xế" của task "API gán xe + tài xế vào từng chuyến"
/// (US 14 "Phân công điều xe", Nguyễn Duy Kiên). Hợp đồng đầy đủ ở mục
/// "Lịch trình chạy xe — /routes/{routeId}/trips" của docs/api-contract.md.
///
/// Phần "xe" của tên task đã có sẵn từ trước: đổi xe một chuyến là
/// <c>PUT /routes/{routeId}/trips/{id}</c> với <c>busId</c> mới (<c>RouteTripsService</c>).
/// Không có "phụ xe": quy ước A8.4 chốt đúng 4 vai trò, bảng <c>Trips</c> không có cột phụ xe.
///
/// Gán theo LÔ vì màn hình phân công chọn nhiều chuyến rồi gán một tài xế trong một thao tác —
/// chữ ký của frontend là <c>bulkAssignDriver(routeId, tripIds, driverId)</c>.
/// </summary>
public interface ITripDriverAssignmentService
{
    /// <summary>
    /// Gán <c>driverId</c> cho toàn bộ chuyến trong <c>tripIds</c> — tất cả phải thuộc tuyến
    /// <paramref name="routeId"/> và đang ở trạng thái còn phân công được.
    ///
    /// Thao tác là NGUYÊN TỬ: một chuyến không hợp lệ thì không chuyến nào bị đổi.
    ///
    /// Kết quả: Ok kèm kết quả từng chuyến — trùng lịch tài xế chỉ là CẢNH BÁO nằm trong
    /// <c>conflicts</c> của từng chuyến chứ không chặn (luồng điều hành được phép cố ý chấp nhận
    /// trùng, cùng triết lý <c>PUT /routes/{routeId}/trips/{id}</c>). NotFound khi tuyến không
    /// tồn tại, chuyến không tồn tại/thuộc tuyến khác, hoặc <c>driverId</c> không phải tài khoản
    /// mang vai trò <c>Driver</c>. Conflict khi chuyến đã huỷ/đã chạy xong, hoặc tài khoản tài xế
    /// đang bị khoá.
    /// </summary>
    Task<ServiceResult<DriverAssignmentResponse>> AssignAsync(
        Guid routeId,
        AssignDriverToTripsRequest request,
        CancellationToken cancellationToken = default);
}
