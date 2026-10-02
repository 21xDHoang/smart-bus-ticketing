using SmartBus.Api.Dtos.Trips;

namespace SmartBus.Api.Services;

/// <summary>
/// Đổi xe / đổi tài xế một chuyến khi có sự cố (US 14 "Phân công điều xe", task "API đổi xe/đổi
/// tài xế khi có sự cố + ghi log thay đổi" — Phùng Duy Hoàng).
///
/// Khác <c>PUT /routes/{routeId}/trips/{id}</c> (sửa toàn phần lịch trình) ở ba điểm:
/// <list type="bullet">
///   <item>Chỉ sửa phân công (xe/tài xế), không đụng tới giờ chạy — luồng sự cố không có lý do
///   gì để ghi lại giờ.</item>
///   <item>Gọi <see cref="ITripConflictService"/> kiểm tra trùng lịch điều xe nhưng chỉ trả về
///   cảnh báo trong kết quả, KHÔNG chặn: chuyến đang chạy cần thay xe/tài xế ngay, người điều
///   hành được phép cố ý chấp nhận trùng (khác luồng tạo lịch trình trả 409).</item>
///   <item>Chỉ áp dụng cho chuyến Scheduled/Running — chuyến đã hủy hoặc đã chạy xong không
///   đổi phân công được nữa.</item>
/// </list>
///
/// Không tự ghi nhật ký: mọi request thành công đã được <c>AuditLogMiddleware</c> ghi tự động
/// (hành động Update, đối tượng "Trips:{id}", kèm người thực hiện + IP — US 23).
/// </summary>
public interface ITripAssignmentService
{
    /// <summary>
    /// Đổi xe và/hoặc tài xế của chuyến <paramref name="tripId"/>.
    ///
    /// Kết quả: Ok kèm phân công mới + cảnh báo trùng lịch (chỉ soi tài nguyên được đổi);
    /// NotFound khi chuyến/xe/tài xế không tồn tại; Conflict khi chuyến đã hủy/hoàn thành, xe
    /// không khai thác, hoặc tài khoản tài xế bị khóa; Invalid khi request không truyền tài
    /// nguyên nào.
    /// </summary>
    Task<ServiceResult<TripAssignmentResponse>> ReassignAsync(
        Guid tripId,
        ReassignTripRequest request,
        CancellationToken cancellationToken = default);
}
