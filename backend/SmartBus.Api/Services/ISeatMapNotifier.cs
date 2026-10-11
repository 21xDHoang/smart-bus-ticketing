namespace SmartBus.Api.Services;

/// <summary>
/// Cổng phát sự kiện "sơ đồ ghế vừa đổi" ra Hub SignalR — task *"Đồng bộ trạng thái ghế realtime
/// giữa các phiên (SignalR)"*, dòng 11 Sprint 3 (US 2).
///
/// Tồn tại để ba lớp bọc Notifying* không cầm thẳng <c>IHubContext</c>: tầng nghiệp vụ chỉ nói
/// "chuyến này vừa đổi", còn chuyện đẩy qua SignalR là chi tiết của một cài đặt duy nhất. Test của
/// lớp bọc chỉ cần giả lập cổng này (ghi lại tripId nhận được), không phải dựng cả Hub.
/// </summary>
public interface ISeatMapNotifier
{
    /// <summary>
    /// Phát <c>SeatMapChanged(tripId)</c> cho mọi client đang xem sơ đồ ghế của chuyến.
    ///
    /// Hợp đồng của cổng: KHÔNG ném lỗi ra ngoài khi kênh đẩy hỏng. Người gọi (các lớp bọc) đang ở
    /// cuối một thao tác nghiệp vụ ĐÃ thành công (giữ ghế, nhả ghế, quét hết hạn) — lời nhắc
    /// realtime là thứ phụ trợ, không được phép biến thao tác đó thành lỗi.
    /// </summary>
    Task SeatMapChangedAsync(Guid tripId);
}
