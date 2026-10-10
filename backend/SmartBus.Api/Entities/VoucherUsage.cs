namespace SmartBus.Api.Entities;

/// <summary>
/// Một LƯỢT TIÊU THỤ voucher — bảng <c>VoucherUsages</c>, mỗi giao dịch thành công có mã tối đa một
/// dòng. Task *"API kiểm tra và áp dụng voucher vào đơn hàng"* (dòng 52, Nguyễn Duy Kiên).
///
/// Bảng chỉ GHI THÊM: không có API sửa/xoá lượt tiêu thụ (sửa tại chỗ thì mất dấu vết tiền đã giảm),
/// nên không có cột <c>UpdatedAt</c> — đúng quy ước A4 "UpdatedAt nullable ở bảng có sửa". Cùng lối
/// <see cref="FeedbackReply"/> và <see cref="Payment"/>.
///
/// Vì sao tồn tại: số tiền giảm KHÔNG được ghi ở đâu khác. Entity <see cref="Payment"/> không có cột
/// voucher, nên bảng này vừa là dấu vết của khoản giảm, vừa là nguồn cho API thống kê hiệu quả
/// voucher (dòng 54) đếm lượt và cộng tiền giảm theo từng mã.
/// </summary>
public class VoucherUsage
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Voucher đã bị tiêu thụ một lượt.</summary>
    public Guid VoucherId { get; set; }

    public Voucher? Voucher { get; set; }

    /// <summary>
    /// Khách đã dùng lượt này. ⚠️ Hiện **chưa** dùng để chặn gì — luật "mỗi khách một lượt" chưa
    /// được chốt (xem mục "Câu hỏi mở" của hợp đồng). Ghi sẵn ở đây để khi nhóm chốt thì thêm luật
    /// là thêm một điều kiện truy vấn, KHÔNG phải migrate lại bảng.
    /// </summary>
    public Guid UserId { get; set; }

    public User? User { get; set; }

    /// <summary>
    /// Mã giao dịch đã tiêu thụ voucher này (<c>Payments.PaymentCode</c>) — DUY NHẤT, và chính là
    /// chốt chống tiêu thụ hai lần: callback cổng gửi lại hay job đối soát chạy đè đều đâm vào
    /// unique index này chứ không cộng thêm một lượt.
    ///
    /// ⚠️ Cố ý là CỘT TRẦN, không phải FK sang <c>Payments</c>: bảng đó cũng chưa migrate, đặt FK
    /// bây giờ là buộc migration <c>Vouchers</c> phải chạy sau migration <c>Payments</c> — một ràng
    /// buộc thứ tự giữa hai việc của hai người, đổi lấy đúng một cột. Repo đã có sẵn lối này:
    /// <see cref="Payment.TicketId"/> cũng là cột trần chờ bảng <c>Tickets</c>. Nối FK bằng một
    /// migration sau, khi <c>Payments</c> đã có bảng.
    /// </summary>
    public string PaymentCode { get; set; } = string.Empty;

    /// <summary>Tổng tiền đơn TRƯỚC giảm — ảnh chụp lúc áp, không phải con số tra ngược lại được.</summary>
    public decimal OrderAmount { get; set; }

    /// <summary>Số tiền đã giảm ở lượt này; <c>OrderAmount - DiscountAmount</c> là số khách đã trả.</summary>
    public decimal DiscountAmount { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
