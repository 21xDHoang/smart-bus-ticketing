namespace SmartBus.Api.Dtos.SeatLayouts;

/// <summary>
/// Một sơ đồ ghế trong response của /seat-layouts — khớp mục "Sơ đồ ghế — /seat-layouts" của
/// docs/api-contract.md. Đổi tên trường ở đây là đổi hình dạng API: phải sửa api-contract.md
/// trước rồi báo người viết frontend (Băng, Hạnh, Thịnh), xem docs/03-quy-uoc.md mục 2.2 điều 5.
///
/// Cố ý KHÔNG có mảng <c>seats</c>: sơ đồ là hình học của một LOẠI XE, còn ghế thật thuộc TỪNG XE
/// (bảng Seats, đọc qua GET /trips/{tripId}/seats). Trả ghế ở đây sẽ là hai nguồn sự thật cho
/// cùng một câu hỏi.
/// </summary>
public class SeatLayoutResponse
{
    public Guid Id { get; set; }

    /// <summary>Loại xe — khoá nghiệp vụ, duy nhất toàn hệ thống.</summary>
    public string BusType { get; set; } = string.Empty;

    /// <summary>Số tầng: 1 hoặc 2.</summary>
    public int NumberOfFloors { get; set; }

    /// <summary>Số hàng ghế trên mỗi tầng.</summary>
    public int RowsPerFloor { get; set; }

    /// <summary>Số ghế trên mỗi hàng.</summary>
    public int ColumnsPerRow { get; set; }

    /// <summary>
    /// Tổng số ghế = <c>numberOfFloors × rowsPerFloor × columnsPerRow</c> — server tự tính,
    /// client không gửi lên (cùng lối <c>Buses.Capacity</c> phải khớp số ghế của xe).
    /// </summary>
    public int TotalSeats { get; set; }

    /// <summary>
    /// Toạ độ các ghế VIP, dạng <c>"tầng-hàng-cột"</c> đếm từ 1. Mảng rỗng = không có ghế VIP.
    /// Trả mảng chứ không trả chuỗi ';' như cột trong CSDL — quy ước lưu trữ là chuyện nội bộ.
    /// </summary>
    public string[] VipSeatPositions { get; set; } = [];

    /// <summary>
    /// CHỈ có nghĩa ở response của POST/PUT: số xe vừa được sinh (hoặc sinh lại) ghế trong lượt
    /// lưu này — để giao diện báo được "Đã lưu và sinh ghế cho 1 xe". GET luôn trả 0.
    /// </summary>
    public int BusesSeated { get; set; }

    public DateTime CreatedAt { get; set; }

    /// <summary>null khi sơ đồ chưa được sửa lần nào.</summary>
    public DateTime? UpdatedAt { get; set; }
}
