using System.ComponentModel.DataAnnotations;

namespace SmartBus.Api.Dtos.SeatLayouts;

/// <summary>
/// Body của POST /api/seat-layouts — cấu hình sơ đồ ghế cho một LOẠI XE (story 2
/// "Cấu hình sơ đồ ghế theo loại xe (số tầng, số ghế, ghế VIP)").
///
/// Lưu sơ đồ là ghế có ngay: service tự sinh ghế cho mọi xe cùng loại chưa có ghế nào
/// (mục "Lưu sơ đồ là ghế có ngay (tự sinh)" của docs/api-contract.md).
///
/// Thư mục đặt tên số nhiều "SeatLayouts" có chủ ý: namespace SmartBus.Api.Dtos.SeatLayout
/// (số ít) sẽ trùng tên với entity SmartBus.Api.Entities.SeatLayout, và file nào import cả hai
/// thì compiler báo "SeatLayout is a namespace but is used like a type". Cùng lối Dtos/Fares.
/// </summary>
public class CreateSeatLayoutRequest
{
    /// <summary>
    /// Loại xe áp dụng — khớp <c>Buses.BusType</c> ("Xe buýt 45 chỗ"). Duy nhất toàn hệ thống:
    /// mỗi loại xe đúng MỘT sơ đồ (chỉ mục unique ở AppDbContext.Seat.cs).
    ///
    /// Trùng loại xe trả 400 gắn vào đúng ô nhập này (errors.busType) — cùng lối biển số xe ở
    /// /buses và mã tuyến ở /routes, vì đây là chuỗi quản lý gõ tay vào form.
    /// </summary>
    [Required(ErrorMessage = "Loại xe không được để trống")]
    [StringLength(50, MinimumLength = 2, ErrorMessage = "Loại xe phải từ 2 đến 50 ký tự")]
    public string BusType { get; set; } = string.Empty;

    /// <summary>
    /// Số tầng: 1 hoặc 2.
    ///
    /// Cố ý KHÔNG gắn [Range] ở đây: biên và câu thông báo nằm ở tầng nghiệp vụ
    /// (<c>SeatLayoutService</c>) để chỉ khai báo một lần và để lỗi đi qua đúng cấu trúc
    /// { message, errors } thống nhất của dự án — cùng lối <c>CreateFareRequest.Price</c>.
    /// </summary>
    public int NumberOfFloors { get; set; } = 1;

    /// <summary>
    /// Số hàng ghế trên MỖI tầng — 1 đến 26 (26 là số chữ cái A–Z dùng đánh mã ghế; màn hình
    /// giới hạn chặt hơn: 13).
    /// </summary>
    public int RowsPerFloor { get; set; }

    /// <summary>Số ghế tối đa trên một hàng — 1 đến 10 (màn hình giới hạn 6).</summary>
    public int ColumnsPerRow { get; set; }

    /// <summary>
    /// Toạ độ các ghế VIP, mỗi phần tử dạng <c>"tầng-hàng-cột"</c> đếm từ 1 — ví dụ
    /// <c>["1-1-1", "1-1-2"]</c>. Bỏ trống = sơ đồ không có ghế VIP.
    ///
    /// Để <c>List&lt;string&gt;</c> chứ không phải chuỗi đã nối ';': hình dạng API là mảng cho
    /// frontend dễ dùng, còn service nối lại thành chuỗi khi lưu xuống cột
    /// <c>SeatLayout.VipSeatPositions</c> — quy ước lưu trữ của entity.
    ///
    /// Toạ độ sai định dạng, nằm ngoài lưới, hoặc trùng nhau bị chặn ở tầng service,
    /// lỗi gắn vào errors.vipSeatPositions.
    /// </summary>
    public List<string> VipSeatPositions { get; set; } = new();
}
