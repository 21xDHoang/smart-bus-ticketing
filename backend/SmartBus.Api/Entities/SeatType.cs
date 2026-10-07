namespace SmartBus.Api.Entities;

/// <summary>
/// Loại ghế trên sơ đồ xe — cơ sở của cột <c>Seats.SeatType</c>.
/// Lưu dạng chuỗi trong CSDL (quy ước A3): "Vip" đọc là hiểu, còn 1 thì phải tra bảng mã.
/// </summary>
public enum SeatType
{
    /// <summary>Ghế phổ thông — mọi sơ đồ đều có.</summary>
    Standard,

    /// <summary>
    /// Ghế VIP — task "Cấu hình sơ đồ ghế theo loại xe (số tầng, số ghế, ghế VIP)".
    /// Là thuộc tính của TỪNG GHẾ chứ không phải một con số trên sơ đồ: biết "có 6 ghế VIP"
    /// mà không biết sáu ghế nào thì màn hình chọn ghế không tô màu được.
    /// </summary>
    Vip
}
