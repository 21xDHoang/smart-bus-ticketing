namespace SmartBus.Api.Entities;

/// <summary>
/// Bảng Trips — một chuyến xe cụ thể chạy trên một tuyến vào một giờ nhất định.
/// Task migrate bảng này là của Vàng Thị Dăm (story 13).
///
/// KHÔNG có bảng Schedule đi kèm (quy ước A8.3): lịch trình định kỳ không tách thành bảng mẫu,
/// chỉ dùng Trips + API sinh chuyến hàng loạt. Quản lý chọn tuyến + giờ bắt đầu + giờ kết thúc
/// + tần suất (phút) → hệ thống sinh N dòng Trips. Thêm bảng mẫu là thêm câu hỏi không story
/// nào trả lời: "sửa mẫu thì chuyến đã sinh có đổi theo không?".
///
/// Thứ tự trạm của chuyến KHÔNG lưu ở đây mà lấy từ RouteStops của tuyến (đã migrate Sprint 1):
/// cùng một tuyến, mọi chuyến đều dừng đúng dãy trạm đó theo StopOrder.
/// </summary>
public class Trip
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid RouteId { get; set; }

    public Route? Route { get; set; }

    public Guid BusId { get; set; }

    public Bus? Bus { get; set; }

    // ── Tài xế phụ trách chuyến (US 14 "Phân công điều xe").
    //    Trỏ thẳng tới Users, KHÔNG có bảng Drivers riêng: quy ước A8.4 chốt đúng 4 vai trò
    //    (Admin/Manager/Driver/Passenger) và tài xế chỉ là một vai trò của Users — "cần thêm
    //    vai trò sau này = thêm 1 dòng bảng Roles, không phải sửa schema". A9 cũng không có
    //    bảng Drivers lẫn TripAssignments, mà luật 2.4 cấm tự thêm bảng ngoài A9.
    //    Cũng không có cột phụ xe: A8.4 ghi rõ "Không có Phụ xe", và US 15 nói "phụ xe/tài xế"
    //    vẫn thoả với một người làm.

    /// <summary>
    /// Tài xế được phân công. Nullable vì chuyến do BackgroundService sinh hàng loạt (US 13)
    /// ra đời TRƯỚC khi có ai điều xe — US 14 chính là nghiệp vụ gán tài xế vào chuyến sau đó.
    /// Bắt buộc NOT NULL sẽ chặn luôn luồng sinh chuyến tự động.
    /// </summary>
    public Guid? DriverId { get; set; }

    public User? Driver { get; set; }

    /// <summary>Giờ khởi hành thực tế của chuyến. timestamptz UTC — xem quy ước A3.</summary>
    public DateTime DepartureTime { get; set; }

    /// <summary>
    /// Giờ dự kiến tới bến cuối. Nullable vì lúc sinh chuyến có thể chưa chốt được
    /// (chưa có thời gian chạy chuẩn của tuyến).
    /// </summary>
    public DateTime? ArrivalTime { get; set; }

    /// <summary>Lưu dạng chuỗi trong CSDL — xem quy ước A3.</summary>
    public TripStatus Status { get; set; } = TripStatus.Scheduled;

    // ── Vị trí hiện tại của xe, đặt trên Trips chứ không trên Buses (quy ước A8.6):
    //    vị trí chỉ có nghĩa khi gắn với một chuyến đang chạy, mọi story về vị trí đều hỏi
    //    "xe của chuyến này đang ở đâu". Không có bảng lịch sử vị trí — không story nào cần.

    /// <summary>Trạm xe vừa đi qua gần nhất. Null khi chuyến chưa chạy.</summary>
    public Guid? CurrentStopId { get; set; }

    public Stop? CurrentStop { get; set; }

    /// <summary>Vĩ độ hiện tại của xe — double precision, KHÔNG dùng real/float4 (quy ước A3).</summary>
    public double? CurrentLat { get; set; }

    /// <summary>Kinh độ hiện tại của xe — double precision, cùng lý do trên.</summary>
    public double? CurrentLng { get; set; }

    /// <summary>Lần cuối vị trí được cập nhật. Null khi chưa từng báo vị trí.</summary>
    public DateTime? PositionUpdatedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Chuyến có sửa dữ liệu (đổi xe, huỷ chuyến) nên có UpdatedAt (A4).</summary>
    public DateTime? UpdatedAt { get; set; }
}
