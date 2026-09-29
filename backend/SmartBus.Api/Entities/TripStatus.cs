namespace SmartBus.Api.Entities;

/// <summary>
/// Trạng thái một chuyến xe. Lưu dạng chuỗi trong CSDL (quy ước A3).
/// </summary>
public enum TripStatus
{
    /// <summary>Đã sinh từ lịch trình, chưa tới giờ chạy.</summary>
    Scheduled,

    /// <summary>Đang chạy — đây là lúc các cột vị trí (CurrentLat/Lng, CurrentStopId) có nghĩa (A8.6).</summary>
    Running,

    /// <summary>Đã chạy xong.</summary>
    Completed,

    /// <summary>Huỷ chuyến. Giữ bản ghi vì vé đã bán vẫn tham chiếu tới (A4 cấm cột IsDeleted).</summary>
    Cancelled
}
