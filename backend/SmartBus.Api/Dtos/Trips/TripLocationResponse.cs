namespace SmartBus.Api.Dtos.Trips;

/// <summary>
/// Hình dạng trả về của PUT /api/trips/{id}/location — ảnh chụp vị trí VỪA LƯU của chuyến
/// (US 10 "Thông báo trạm", Sprint 4 — Trần Trung Hiếu).
///
/// Chỉ trả 5 trường vị trí, KHÔNG trả cả chuyến: bên gọi là thiết bị của tài xế báo vị trí
/// định kỳ, chỉ cần biết hệ thống đã nhận đúng con số nào. Nửa "đọc" cho hành khách (xem xe
/// đang ở đâu — US 9) sẽ có endpoint riêng ở Sprint sau.
/// </summary>
public class TripLocationResponse
{
    /// <summary>Chuyến vừa được cập nhật — lặp lại id trên đường dẫn để phản hồi tự đủ nghĩa.</summary>
    public Guid TripId { get; set; }

    /// <summary>Trạm gần nhất vừa lưu — null khi thiết bị không gửi trạm.</summary>
    public Guid? CurrentStopId { get; set; }

    /// <summary>Vĩ độ vừa lưu.</summary>
    public double CurrentLat { get; set; }

    /// <summary>Kinh độ vừa lưu.</summary>
    public double CurrentLng { get; set; }

    /// <summary>Thời điểm hệ thống ghi nhận vị trí này (UTC) — luôn là giờ server nhận request,
    /// không tin giờ thiết bị gửi lên.</summary>
    public DateTime PositionUpdatedAt { get; set; }
}
