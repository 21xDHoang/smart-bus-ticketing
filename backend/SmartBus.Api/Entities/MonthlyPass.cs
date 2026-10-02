namespace SmartBus.Api.Entities;

/// <summary>
/// Bảng MonthlyPasses — vé tháng của hành khách (US 16 "Đăng ký vé tháng").
/// Task migrate bảng này là của Vàng Thị Dăm (story 16).
///
/// Theo đúng A8.2: vé tháng là QUYỀN ĐI LẠI TRÊN MỘT TUYẾN trong một khoảng thời gian —
/// có Code (mã QR unique) + RouteId + ValidFrom/ValidTo + Status, và KHÔNG kèm ghế.
/// Khách vé tháng không đảm bảo có chỗ ngồi; đó là trade-off đã biết trước của A8.2, không
/// phải thiếu sót. Vì vậy bảng này KHÔNG có SeatId và KHÔNG sinh Ticket cho từng chuyến.
///
/// Gia hạn (task "API gia hạn vé tháng + tính ngày hiệu lực kế tiếp") = ghi thêm MỘT dòng mới
/// với ValidFrom = ValidTo của vé cũ, KHÔNG sửa dòng cũ. Dòng cũ ở lại làm lịch sử và làm mốc
/// tính ngày kế tiếp; sửa tại chỗ thì mất cả hai.
/// </summary>
public class MonthlyPass
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Mã vé tháng, cũng là nội dung mã QR để soát vé (A6: MonthlyPasses — Code, "mã QR soát vé
    /// tháng"). Unique ở tầng CSDL vì trùng mã nghĩa là soát vé ra hai người.
    /// Dài tối đa 40: frontend sinh theo khuôn "MP-{mã tuyến}-{6 ký tự}", mà mã tuyến tối đa 20
    /// ký tự (xem AppDbContext.Route.cs) → 3 + 20 + 1 + 6 = 30, chừa 10 ký tự dự phòng.
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// Hành khách sở hữu vé. KHÔNG unique: một người mua vé tháng cho nhiều tuyến cùng lúc, và
    /// mua lại cùng tuyến ở kỳ sau là chuyện bình thường.
    /// </summary>
    public Guid UserId { get; set; }

    public User? User { get; set; }

    /// <summary>Tuyến mà vé này cho phép đi lại. Vé tháng gắn với một tuyến, không phải toàn mạng.</summary>
    public Guid RouteId { get; set; }

    public Route? Route { get; set; }

    /// <summary>Loại vé đã mua — quyết định thời hạn và giá gói.</summary>
    public Guid PassTypeId { get; set; }

    public PassType? PassType { get; set; }

    /// <summary>
    /// Số tiền THỰC TRẢ tại thời điểm đăng ký (VND).
    ///
    /// ⚠️ Cố ý lặp lại <see cref="PassType.Price"/> — đây là ảnh chụp, không phải dữ liệu thừa.
    /// Báo cáo doanh thu (US 19, Sprint 5) phải cộng số ĐÃ THU, mà giá gói trong PassTypes thì
    /// sửa được: đổi giá hôm nay không được phép làm doanh thu các kỳ trước nhảy theo. Cùng lối
    /// với việc Tickets sẽ chụp lại giá vé thay vì tra Fares lúc xuất hoá đơn.
    /// </summary>
    public decimal Price { get; set; }

    /// <summary>Ngày bắt đầu hiệu lực.</summary>
    public DateTime ValidFrom { get; set; }

    /// <summary>
    /// Ngày hết hiệu lực. KHÔNG nullable: vé vừa đăng ký luôn biết ngày kết thúc (ValidFrom cộng
    /// DurationMonths của loại vé), khác với DiscountRequest.ExpiresAt — ở đó người duyệt mới biết
    /// ngày hết hạn của thẻ minh chứng, còn ở đây hệ thống tự tính được.
    /// </summary>
    public DateTime ValidTo { get; set; }

    /// <summary>
    /// Trạng thái lưu trữ — xem <see cref="MonthlyPassStatus"/>, đọc kỹ cảnh báo ở đó:
    /// <b>hiệu lực thật phải suy từ ValidFrom/ValidTo</b>, không phải từ cột này.
    /// </summary>
    public MonthlyPassStatus Status { get; set; } = MonthlyPassStatus.Active;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Vé tháng có sửa dữ liệu (job lật sang Expired) nên có UpdatedAt (A4).</summary>
    public DateTime? UpdatedAt { get; set; }
}
