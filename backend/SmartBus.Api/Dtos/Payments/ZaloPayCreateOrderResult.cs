namespace SmartBus.Api.Dtos.Payments;

/// <summary>
/// Kết quả gọi tạo đơn ZaloPay (<see cref="Services.IZaloPayGatewayService.CreateOrderAsync"/>).
///
/// ⚠️ Bộ mã của ZaloPay KHÁC MoMo: <c>return_code = 1</c> là THÀNH CÔNG (không phải 0), <c>2</c> là
/// thất bại, <c>3</c> là đang xử lý. Đừng so với 0 theo thói quen đọc MoMo — so nhầm thì mọi lượt
/// tạo đơn hỏng đều bị đọc thành thành công. <see cref="Success"/> đã gói phép so đó lại.
///
/// Thành công ở bước này chỉ là cổng CHẤP NHẬN đơn — khách còn phải trả tiền; kết quả cuối cùng
/// đến qua callback.
/// </summary>
public class ZaloPayCreateOrderResult
{
    /// <summary>Cổng chấp nhận tạo đơn (<c>return_code = 1</c>) và có <see cref="OrderUrl"/> để chuyển khách.</summary>
    public bool Success { get; set; }

    /// <summary>Mã kết quả thô của cổng — giữ nguyên để ghi log và tra tài liệu khi hỏng.</summary>
    public int ReturnCode { get; set; }

    /// <summary>Thông báo của cổng (<c>return_message</c>) — trả nguyên văn cho người gọi khi hỏng.</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>Mã lỗi chi tiết (<c>sub_return_code</c>) — ví dụ <c>-68</c> là trùng app_trans_id.</summary>
    public int SubReturnCode { get; set; }

    /// <summary>Thông báo chi tiết (<c>sub_return_message</c>) — thường mới là câu nói rõ vì sao hỏng.</summary>
    public string SubReturnMessage { get; set; } = string.Empty;

    /// <summary>URL trang thanh toán của ZaloPay — chuyển khách sang đây (khác rỗng khi <see cref="Success"/>).</summary>
    public string OrderUrl { get; set; } = string.Empty;

    /// <summary>Ảnh QR của đơn — null khi cổng không trả.</summary>
    public string? QrCode { get; set; }
}
