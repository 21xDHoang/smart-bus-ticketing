namespace SmartBus.Api.Entities;

/// <summary>
/// Bảng Buses — xe buýt trong đội xe (quy ước A9 mục 10, Sprint 2).
/// Task migrate bảng này là của Vàng Thị Dăm (story 13).
/// </summary>
public class Bus
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Biển số xe — "29B-123.45". Là khoá nghiệp vụ: hai xe trùng biển số là dữ liệu sai,
    /// chặn ở tầng CSDL bằng chỉ mục unique (xem AppDbContext.Trip.cs). Chuỗi ngắn có giới
    /// hạn (quy ước A3), không dùng text.
    /// </summary>
    public string LicensePlate { get; set; } = string.Empty;

    /// <summary>Loại xe — "Xe buýt 45 chỗ", "Xe buýt điện". Chuỗi ngắn (quy ước A3).</summary>
    public string BusType { get; set; } = string.Empty;

    /// <summary>Sức chứa theo số ghế. Phải khớp với số dòng Seat của xe.</summary>
    public int Capacity { get; set; }

    /// <summary>Lưu dạng chuỗi trong CSDL — xem quy ước A3.</summary>
    public BusStatus Status { get; set; } = BusStatus.Active;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Xe có sửa dữ liệu (đổi trạng thái bảo dưỡng, đổi loại xe) nên có UpdatedAt (A4).</summary>
    public DateTime? UpdatedAt { get; set; }

    /// <summary>Sơ đồ ghế của xe. Ghế thuộc về xe — xoá xe thì ghế đi theo (A5).</summary>
    public ICollection<Seat> Seats { get; set; } = new List<Seat>();

    /// <summary>Các chuyến đã và đang chạy bằng xe này.</summary>
    public ICollection<Trip> Trips { get; set; } = new List<Trip>();
}
