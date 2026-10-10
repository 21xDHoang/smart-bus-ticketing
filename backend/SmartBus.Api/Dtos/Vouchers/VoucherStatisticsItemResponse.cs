namespace SmartBus.Api.Dtos.Vouchers;

/// <summary>
/// Một dòng của bảng hiệu quả voucher trong GET /api/vouchers/statistics — hợp đồng "Voucher —
/// /vouchers", mục "GET /vouchers/statistics" của docs/api-contract.md.
///
/// Một dòng cho MỘT voucher đã phát hành. Voucher chưa ai dùng vẫn có dòng với <see cref="UsedCount"/>
/// bằng 0 — đó là câu trả lời đắt nhất của bảng này ("mã nào phát hành mà bỏ xó"), và một
/// <c>GroupBy</c> ngây thơ trên <c>VoucherUsages</c> sẽ làm rơi đúng những dòng đó.
///
/// Bốn trường cấu hình của voucher (<c>discountType</c>, <c>discountValue</c>, <c>minOrderValue</c>,
/// <c>maxDiscount</c>) CỐ Ý không có ở đây: chúng thuộc màn cấu hình <c>GET /vouchers</c> (dòng 51),
/// bảng hiệu quả chỉ trả lời "đã dùng bao nhiêu, giảm bao nhiêu tiền". Cần thì gọi endpoint kia.
/// </summary>
public class VoucherStatisticsItemResponse
{
    public Guid VoucherId { get; set; }

    /// <summary>Mã khách gõ — khoá để đối chiếu với màn quản lý voucher.</summary>
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Tên chuỗi của enum — Active / Inactive. Đây là trạng thái do người vận hành bật/tắt, KHÔNG
    /// phải trạng thái hiệu lực: một voucher <c>Active</c> vẫn có thể đang ngoài khoảng
    /// <see cref="ValidFrom"/>/<see cref="ValidUntil"/>, nên đừng suy "hết hạn" từ trường này.
    /// </summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>Điều kiện tuyến của voucher; <c>null</c> = áp dụng cho mọi tuyến.</summary>
    public Guid? RouteId { get; set; }

    /// <summary>
    /// Mã/tên tuyến GHÉP SẴN (left join) để màn hình khỏi gọi <c>GET /routes/{id}</c> cho từng dòng —
    /// cùng lý do bảng theo tuyến của thống kê phản ánh ghép sẵn hai trường này. <c>null</c> khi
    /// voucher không gắn tuyến, hoặc (dữ liệu bất thường) tuyến đã bị xoá.
    /// </summary>
    public string? RouteCode { get; set; }

    /// <inheritdoc cref="RouteCode"/>
    public string? RouteName { get; set; }

    /// <summary>Tổng số lượt phát hành — mẫu số của tỉ lệ tiêu thụ.</summary>
    public int Quantity { get; set; }

    /// <summary>
    /// Số lượt đã tiêu thụ thật, ĐẾM TỪ bảng <c>VoucherUsages</c> — không đọc cột denormalized
    /// <c>Vouchers.UsedCount</c>. Cột kia là bộ đếm do <c>VoucherRedemptionService</c> tăng để
    /// <c>VoucherValidationService</c> khỏi phải <c>COUNT(*)</c> mỗi lần khách gõ mã; nó KHÔNG phải
    /// bản ghi sự thật. Bảng thống kê đếm từ nguồn thật, nên nếu hai bên vênh thì bảng này là bên
    /// đúng và phơi ra chỗ vênh — đọc cột kia là nhân bản con số sai thêm một lần nữa.
    /// </summary>
    public int UsedCount { get; set; }

    /// <summary>
    /// Số KHÁCH KHÁC NHAU đã dùng voucher — không phải số lượt. Một khách dùng hai lần đóng góp 1.
    ///
    /// ⚠️ Luật "mỗi khách một lượt" CHƯA được chốt (xem "Câu hỏi mở" của hợp đồng), nên hiện tại
    /// trường này KHÔNG bằng <see cref="UsedCount"/> là chuyện bình thường, không phải lỗi dữ liệu.
    /// </summary>
    public int UniqueCustomers { get; set; }

    /// <summary>Tổng tiền TRƯỚC giảm của mọi lượt đã tiêu thụ của voucher này.</summary>
    public decimal TotalOrderAmount { get; set; }

    /// <summary>
    /// Tổng tiền đã giảm của voucher này — chi phí thật của mã.
    ///
    /// 🔹 Tỉ lệ "giảm bao nhiêu phần trăm đơn" là giá trị DẪN XUẤT, màn hình tự tính từ cặp
    /// <see cref="TotalDiscountAmount"/>/<see cref="TotalOrderAmount"/>. Server cố ý không trả sẵn:
    /// một trường tỉ lệ mở lại đúng câu hỏi đã chốt ở <c>FeedbackTypeCountResponse</c> (đơn vị nào?
    /// làm tròn mấy chữ số?) trong khi cặp số thô đã chính xác và không thể hiểu sai.
    /// </summary>
    public decimal TotalDiscountAmount { get; set; }

    /// <summary>
    /// Lượt tiêu thụ đầu tiên / gần nhất. <b>Cả hai đều <c>null</c> khi <see cref="UsedCount"/> bằng
    /// 0</b> — giữ đúng lối nullable của phép gộp trên tập rỗng, không bịa ra một mốc thời gian.
    /// </summary>
    public DateTime? FirstUsedAt { get; set; }

    /// <inheritdoc cref="FirstUsedAt"/>
    public DateTime? LastUsedAt { get; set; }

    /// <summary>
    /// Khoảng hiệu lực của voucher — chính là khoảng chiến dịch của nó: <c>VoucherValidationService</c>
    /// chặn <c>NotStarted</c> và <c>Expired</c>, nên một lượt tiêu thụ chỉ ghi được khi voucher đang
    /// trong hiệu lực. Vì vậy số liệu trọn đời ở trên CHÍNH LÀ số liệu của chiến dịch, và endpoint
    /// không cần tham số lọc theo ngày.
    /// </summary>
    public DateTime ValidFrom { get; set; }

    /// <inheritdoc cref="ValidFrom"/>
    public DateTime ValidUntil { get; set; }
}
