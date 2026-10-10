namespace SmartBus.Api.Dtos.Vouchers;

/// <summary>
/// Kết quả kiểm tra một mã voucher (US 18, task *"API kiểm tra và áp dụng voucher vào đơn hàng"* —
/// Nguyễn Duy Kiên). Hợp đồng đầy đủ ở mục "Voucher — /vouchers" của docs/api-contract.md.
///
/// 🔴 <see cref="Valid"/> = <c>false</c> vẫn trả HTTP **200**: đây là endpoint XEM TRƯỚC, màn thanh
/// toán gọi mỗi lần khách gõ mã, nên "mã không dùng được" là câu TRẢ LỜI chứ không phải request hỏng.
/// Chỉ body sai khuôn mới là 400, và chuyến không tồn tại là 404.
///
/// Hình dạng này KHÁC khuôn lỗi chung <c>{ message, errors }</c> của dự án — cố ý, và đã biện luận
/// trong hợp đồng: một mã bị từ chối không phải một lỗi cần FE gắn vào ô input, mà là một kết quả
/// cần hiển thị nguyên câu cho khách.
/// </summary>
public class VoucherValidationResponse
{
    /// <summary>Mã có dùng được cho đơn này không. <c>false</c> thì xem <see cref="ReasonCode"/>.</summary>
    public bool Valid { get; set; }

    /// <summary>
    /// Lý do không dùng được — một trong các hằng của <see cref="VoucherReasonCodes"/>. <c>null</c>
    /// khi <see cref="Valid"/> là <c>true</c>. FE DỊCH theo mã này, đừng so chuỗi <see cref="Message"/>.
    /// </summary>
    public string? ReasonCode { get; set; }

    /// <summary>
    /// Câu tiếng Việt hiển thị thẳng cho khách — có sẵn để FE không phải tự dịch, nhưng câu chữ sẽ
    /// còn đổi nên đừng dùng nó làm khoá phân nhánh.
    /// </summary>
    public string? Message { get; set; }

    /// <summary>Voucher tìm được — <c>null</c> khi <see cref="ReasonCode"/> là <c>NotFound</c>.</summary>
    public Guid? VoucherId { get; set; }

    /// <summary>Mã đã chuẩn hoá chữ HOA — FE hiển thị lại đúng mã đang áp dụng.</summary>
    public string? Code { get; set; }

    /// <summary>
    /// "Percent" | "FixedAmount" — tên chuỗi của <see cref="Entities.VoucherDiscountType"/>.
    ///
    /// Trả về CHUỖI chứ không phải enum: Program.cs không đăng ký <c>JsonStringEnumConverter</c>, nên
    /// kiểu enum sẽ serialize thành 0, 1… — đọc không hiểu, trái tinh thần quy ước A3. Cùng lý do
    /// <see cref="AuditLogs.AuditLogResponse.Action"/> để kiểu string.
    /// </summary>
    public string? DiscountType { get; set; }

    /// <summary>Số tiền được giảm, ĐÃ LÀM TRÒN VỀ ĐỒNG NGUYÊN. <c>0</c> khi <see cref="Valid"/> là <c>false</c>.</summary>
    public decimal DiscountAmount { get; set; }

    /// <summary>
    /// Số tiền khách phải trả sau giảm = <c>orderAmount - discountAmount</c>, không bao giờ âm.
    /// Khi mã không dùng được thì bằng đúng <c>orderAmount</c> — FE không phải phân nhánh.
    /// </summary>
    public decimal FinalAmount { get; set; }

    /// <summary>
    /// Trần giảm giá của voucher (chỉ có nghĩa với <c>Percent</c>) — để FE hiện được câu
    /// "giảm 20%, tối đa 30.000đ". <c>null</c> = không trần.
    /// </summary>
    public decimal? MaxDiscount { get; set; }
}
