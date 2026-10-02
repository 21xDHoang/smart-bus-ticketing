namespace SmartBus.Api.Dtos.Trips;

/// <summary>
/// Kết quả kiểm tra trùng lịch điều xe — tách riêng hai danh sách vì thông báo cho người dùng
/// khác nhau ("xe đang chạy chuyến khác" so với "tài xế đang chạy chuyến khác").
///
/// Một chuyến trùng CẢ hai vế (đúng xe đó và đúng tài xế đó) xuất hiện ở cả hai danh sách —
/// người gọi muốn gộp thì tự gộp, service không che mất vế nào.
/// </summary>
public class TripConflictCheckResponse
{
    /// <summary>Các chuyến trùng khung giờ với XE đang xét. Rỗng khi request không truyền <c>busId</c>.</summary>
    public List<ConflictingTripResponse> BusConflicts { get; set; } = [];

    /// <summary>Các chuyến trùng khung giờ với TÀI XẾ đang xét. Rỗng khi request không truyền <c>driverId</c>.</summary>
    public List<ConflictingTripResponse> DriverConflicts { get; set; } = [];
}
