using SmartBus.Api.Dtos.Trips;

namespace SmartBus.Api.Services;

/// <summary>
/// Kiểm tra trùng lịch điều xe — xe trùng chuyến và tài xế trùng chuyến (US 14 "Phân công điều xe",
/// task "Service kiểm tra trùng lịch tài xế và trùng xe giữa các chuyến" — Phùng Duy Hoàng).
///
/// Luật trùng khớp ĐÚNG phép kiểm tra "trùng khung giờ" khi tạo lịch trình ở
/// <c>RouteTripsService</c> (xem api-contract.md mục "Hai kiểm tra khi tạo lịch trình") nhưng mở
/// rộng thêm vế tài xế:
/// <list type="bullet">
///   <item>Khung giờ một chuyến là khoảng [giờ khởi hành, giờ đến]; chuyến chưa có giờ đến thì coi
///   là một mốc — chỉ chặn chuyến trùng đúng giờ khởi hành hoặc nằm lọt trong khung giờ kia.</item>
///   <item>Chuyến nối đuôi (chuyến này đến đúng giờ chuyến kia khởi hành) KHÔNG tính là trùng.</item>
///   <item>Chỉ chuyến đang chiếm chỗ trên thời gian biểu (Scheduled/Running) mới tính — Cancelled
///   và Completed không chặn chỗ nữa.</item>
/// </list>
///
/// Khác phép kiểm tra khi tạo lịch trình ở chỗ chỉ soi theo tài nguyên điều xe (xe/tài xế), không
/// soi theo tuyến: trùng TUYẾN là luật xếp lịch trình (giới hạn số chuyến/ngày của tuyến — đã có
/// ở <c>RouteTripsService</c>), còn trùng XE/TÀI XẾ mới là luật điều xe — một xe, một tài xế không
/// thể chạy hai chuyến cùng lúc, bất kể hai chuyến thuộc tuyến nào.
///
/// Service thuần, chưa gắn endpoint: cùng một phép kiểm tra dùng cho API gán xe + tài xế vào chuyến
/// (task 113 — Nguyễn Duy Kiên) và API đổi xe/đổi tài xế khi có sự cố (task kế tiếp của Hoàng) —
/// "cùng một hàm kiểm tra, chỉ khác điểm gọi" như ghi chú cuối mục "Hai kiểm tra khi tạo lịch
/// trình" của api-contract.md.
///
/// <para>
/// ⚠️ <c>PUT /routes/{routeId}/trips/{id}</c> (đổi xe của màn hình lập lịch trình) cố ý KHÔNG gọi
/// service này — luật đã chốt trong api-contract.md ("Sửa giờ một chuyến đã có vẫn lách được kiểm
/// tra trùng"). Muốn chặn cả luồng đó thì bổ sung ở task sau, gọi đúng service này.
/// </para>
/// </summary>
public interface ITripConflictService
{
    /// <summary>
    /// Tìm các chuyến đang hoạt động trùng khung giờ <c>[DepartureTime, ArrivalTime]</c> với xe
    /// và/hoặc tài xế trong <paramref name="request"/>.
    ///
    /// Kết quả: Ok kèm hai danh sách chuyến trùng (vế xe / vế tài xế), xếp theo giờ khởi hành;
    /// Invalid khi giờ đến trước giờ khởi hành hoặc thiếu giờ khởi hành. Tài nguyên nào không
    /// truyền thì vế đó bỏ qua — không truyền cả hai thì kết quả rỗng (không có gì để trùng),
    /// không phải lỗi: đây là phép tra cứu, người gọi tự quyết định chặn hay chỉ cảnh báo.
    /// </summary>
    Task<ServiceResult<TripConflictCheckResponse>> CheckAsync(
        TripConflictCheckRequest request,
        CancellationToken cancellationToken = default);
}
