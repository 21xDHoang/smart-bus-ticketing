using SmartBus.Api.Dtos.Trips;

namespace SmartBus.Api.Services;

/// <summary>
/// Nghiệp vụ lịch trình chạy xe theo tuyến — story 13 "Lập lịch trình", Trần Trung Hiếu.
/// Hợp đồng đầy đủ ở mục "Lịch trình chạy xe — /routes/{routeId}/trips" của docs/api-contract.md.
///
/// Lịch trình định kỳ KHÔNG tách bảng Schedule (quy ước A8.3): mỗi chuyến là một dòng Trips,
/// còn "ngày áp dụng + giờ khởi hành + tần suất" của lịch trình được nắm trọn trong
/// <see cref="GenerateAsync"/> — tuyến + xe + mốc bắt đầu + mốc kết thúc + tần suất (phút)
/// → sinh N dòng Trips cách đều tần suất.
/// </summary>
public interface ITripService
{
    /// <summary>Danh sách chuyến của tuyến, lọc theo khoảng giờ khởi hành/trạng thái và phân trang.</summary>
    Task<ServiceResult<TripListResponse>> ListAsync(
        Guid routeId,
        ListTripsRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Chi tiết một chuyến. Chuyến của tuyến khác cũng là 404 — xem hợp đồng "routeId phải khớp".</summary>
    Task<ServiceResult<TripResponse>> GetByIdAsync(
        Guid routeId,
        Guid id,
        CancellationToken cancellationToken = default);

    /// <summary>Thêm một chuyến lẻ. Chuyến mới luôn ở trạng thái Scheduled.</summary>
    Task<ServiceResult<TripResponse>> CreateAsync(
        Guid routeId,
        CreateTripRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Sửa xe/giờ chạy. Trạng thái bỏ trống thì giữ nguyên.</summary>
    Task<ServiceResult<TripResponse>> UpdateAsync(
        Guid routeId,
        Guid id,
        UpdateTripRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Huỷ chuyến — chuyển về Cancelled (quy ước A4: cấm thêm cột IsDeleted, dùng cột trạng thái
    /// sẵn có). Chuyến đã Completed không huỷ được. Trả về chuyến đã huỷ để màn hình cập nhật
    /// luôn dòng đang hiển thị — cùng lối DELETE /routes/{id}.
    /// </summary>
    Task<ServiceResult<TripResponse>> DeleteAsync(Guid routeId, Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sinh chuyến hàng loạt (quy ước A8.3): chuyến đầu xuất phát đúng mốc bắt đầu, các chuyến
    /// sau cách đều tần suất (phút), chuyến cuối không vượt quá mốc kết thúc.
    /// Mỗi lần gọi tạo tối đa 500 chuyến — quá thì 400, thu hẹp khoảng hoặc tăng tần suất.
    /// </summary>
    Task<ServiceResult<GenerateTripsResponse>> GenerateAsync(
        Guid routeId,
        GenerateTripsRequest request,
        CancellationToken cancellationToken = default);
}
