using SmartBus.Api.Dtos.SeatLayouts;

namespace SmartBus.Api.Services;

/// <summary>
/// Nghiệp vụ sơ đồ ghế theo loại xe — story 2 "Cấu hình sơ đồ ghế theo loại xe
/// (số tầng, số ghế, ghế VIP)", Phùng Duy Hoàng.
///
/// Việc chặn người không phải Admin/Quản lý là của <see cref="RbacPolicies.ManagerOrAbove"/>
/// gắn ở Controller, không lặp lại ở đây: Service không biết ai đang gọi.
///
/// Điểm khác biệt so với các service CRUD khác: lưu sơ đồ KHÔNG chỉ ghi một dòng — nó sinh
/// luôn ghế thật cho mọi xe cùng loại đang chưa có ghế (mục "Lưu sơ đồ là ghế có ngay (tự sinh)"
/// của docs/api-contract.md). Vì vậy POST/PUT trả thêm <c>BusesSeated</c>, còn sửa hình học của
/// sơ đồ đang có ghế bị giữ chỗ/bán thì từ chối bằng 409.
/// </summary>
public interface ISeatLayoutService
{
    /// <summary>
    /// Danh sách sơ đồ, lọc theo loại xe (không phân biệt hoa thường), sắp theo <c>busType</c>
    /// tăng dần. Không phân trang — số sơ đồ bằng số loại xe trong đội, chỉ vài dòng.
    /// </summary>
    Task<ServiceResult<IReadOnlyList<SeatLayoutResponse>>> ListAsync(
        ListSeatLayoutsRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Một sơ đồ theo Id. Không tồn tại là 404.</summary>
    Task<ServiceResult<SeatLayoutResponse>> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Tạo sơ đồ cho một loại xe rồi sinh ghế cho mọi xe cùng loại CHƯA có ghế nào — xe đã có
    /// ghế không bị đụng tới. Trùng loại xe là 400 gắn vào ô nhập (errors.busType).
    /// </summary>
    Task<ServiceResult<SeatLayoutResponse>> CreateAsync(
        CreateSeatLayoutRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sửa hình học/VIP của sơ đồ rồi đồng bộ ghế: hình học không đổi thì chỉ sinh bù cho xe
    /// còn thiếu ghế; có đổi và chưa ghế nào bị giữ chỗ/bán thì xoá ghế cũ của các xe đó rồi
    /// sinh lại; có đổi mà ghế đã bị dùng thì 409.
    /// </summary>
    Task<ServiceResult<SeatLayoutResponse>> UpdateAsync(
        Guid id,
        UpdateSeatLayoutRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Xoá hẳn sơ đồ. Chỉ xoá được khi không còn ghế nào trỏ về nó — ghế là dữ liệu nghiệp vụ,
    /// cùng tinh thần Restrict của quy ước A5; còn ghế thì 409 kèm số lượng.
    /// </summary>
    Task<ServiceResult<bool>> DeleteAsync(
        Guid id,
        CancellationToken cancellationToken = default);
}
