namespace SmartBus.Api.Dtos.Vouchers;

/// <summary>
/// Lý do một voucher KHÔNG dùng được — giá trị của trường <c>reasonCode</c> trong
/// <see cref="VoucherValidationResponse"/>. Khai báo thành hằng (cùng lối
/// <see cref="Payments.PaymentProviderCodes"/> và <see cref="Entities.RoleCodes"/>): service,
/// endpoint, test và frontend dùng chung một nguồn — gõ sai mã là lỗi biên dịch.
///
/// 🔴 Vì sao cần mã chứ không chỉ có câu <c>message</c>: FE phải DỊCH lý do sang tiếng Việt hiển thị
/// cho khách, và câu chữ của <c>message</c> sẽ còn đổi (sửa cho rõ hơn, thêm số tiền vào câu…). So
/// chuỗi <c>message</c> là hỏng lặng lẽ mỗi lần ai đó sửa một chữ.
///
/// Thứ tự trong tệp này theo đúng THỨ TỰ KIỂM TRA của hợp đồng (mục "Voucher — /vouchers"): service
/// trả về điều kiện ĐẦU TIÊN không thoả, nên thứ tự quyết định câu khách nhìn thấy.
/// </summary>
public static class VoucherReasonCodes
{
    /// <summary>Không có voucher nào mang mã này (sau khi đã chuẩn hoá chữ HOA).</summary>
    public const string NotFound = "NotFound";

    /// <summary>Voucher đang bị người vận hành tắt (<c>Status = 'Inactive'</c>).</summary>
    public const string Inactive = "Inactive";

    /// <summary>Chưa tới <c>ValidFrom</c>.</summary>
    public const string NotStarted = "NotStarted";

    /// <summary>Đã quá <c>ValidUntil</c>.</summary>
    public const string Expired = "Expired";

    /// <summary>Đã dùng hết lượt (<c>UsedCount >= Quantity</c>).</summary>
    public const string OutOfStock = "OutOfStock";

    /// <summary>Voucher gắn một tuyến khác với tuyến của chuyến khách đang đặt (dòng 53).</summary>
    public const string WrongRoute = "WrongRoute";

    /// <summary>Giá trị đơn chưa đạt <c>MinOrderValue</c> (dòng 53).</summary>
    public const string BelowMinOrder = "BelowMinOrder";

    // KHÔNG có mã cho "chuyến không tồn tại": đó là lỗi phía GỌI (404), không phải voucher bị từ
    // chối — trộn hai thứ vào một danh sách là bắt FE đi tìm cách hiển thị một câu không dành cho
    // khách. Cùng lối, body sai khuôn là 400 của ModelState, không đi qua bảng mã này.
}
