using SmartBus.Api.Dtos.Drivers;
using SmartBus.Api.Dtos.Trips;

namespace SmartBus.Api.Services;

/// <summary>
/// Nghiệp vụ hồ sơ tài xế — CRUD /api/drivers + ca làm việc /api/drivers/{id}/trips,
/// story 14, Trần Trung Hiếu.
/// Hợp đồng đầy đủ ở mục "Hồ sơ tài xế — /drivers" của docs/api-contract.md.
///
/// Tài xế là User mang vai trò Driver (quy ước A8.4, không có bảng Drivers riêng) nên mọi
/// thao tác ở đây đều chạy trên bảng Users với điều kiện lọc vai trò Driver.
/// </summary>
public interface IDriverService
{
    /// <summary>Danh sách tài xế, lọc theo từ khóa/trạng thái hoạt động và phân trang.</summary>
    Task<ServiceResult<DriverListResponse>> ListAsync(
        ListDriversRequest request,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<DriverResponse>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Tạo tài khoản mới mang vai trò Driver — hồ sơ tài xế luôn đi kèm tài khoản đăng nhập.</summary>
    Task<ServiceResult<DriverResponse>> CreateAsync(
        CreateDriverRequest request,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<DriverResponse>> UpdateAsync(
        Guid id,
        UpdateDriverRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Xoá mềm — khóa tài khoản (quy ước A4), không xoá dữ liệu: chuyến đã chạy vẫn tham chiếu
    /// tới tài xế qua Trips.DriverId. Không tự khóa được chính mình.
    /// Trả về tài xế đã khóa để màn hình cập nhật luôn dòng đang hiển thị.
    /// </summary>
    Task<ServiceResult<DriverResponse>> DeleteAsync(
        Guid id,
        Guid currentUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Ca làm việc của tài xế — danh sách chuyến có Trips.DriverId trỏ vào tài xế,
    /// lọc theo khoảng giờ khởi hành + trạng thái và phân trang.
    /// </summary>
    Task<ServiceResult<DriverTripListResponse>> ListTripsAsync(
        Guid driverId,
        ListTripsRequest request,
        CancellationToken cancellationToken = default);
}
