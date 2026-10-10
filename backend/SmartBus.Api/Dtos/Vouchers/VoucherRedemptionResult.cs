namespace SmartBus.Api.Dtos.Vouchers;

/// <summary>
/// Kết quả tiêu thụ một voucher (US 18, phần "áp dụng" của dòng 52 — Nguyễn Duy Kiên).
/// Người gọi dùng số tiền ở đây để ghi hoá đơn/đối soát, không cần đọc lại CSDL.
/// </summary>
public class VoucherRedemptionResult
{
    /// <summary>Voucher đã bị tiêu thụ một lượt.</summary>
    public Guid VoucherId { get; set; }

    /// <summary>
    /// Mã voucher đã chuẩn hoá chữ HOA của voucher vừa tiêu thụ. Đọc qua quan hệ
    /// <c>VoucherUsage → Voucher</c>: <c>VoucherUsage</c> chỉ giữ <c>VoucherId</c>, hợp đồng không có
    /// cột <c>voucherCode</c> (xem bảng "Entity VoucherUsage"). Rỗng khi giao dịch không dùng mã.
    /// </summary>
    public string VoucherCode { get; set; } = string.Empty;

    /// <summary>Tổng tiền đơn trước giảm, ảnh chụp tại lượt này.</summary>
    public decimal OrderAmount { get; set; }

    /// <summary>Số tiền đã giảm — BẰNG số đã trừ lúc tạo giao dịch, vì service kiểm lại từ đầu.</summary>
    public decimal DiscountAmount { get; set; }

    /// <summary>
    /// <c>true</c> khi lượt gọi này là BẢN GỬI LẠI — <c>VoucherUsage</c> đã tồn tại từ trước và
    /// <c>UsedCount</c> KHÔNG bị cộng thêm. Cờ này chỉ để ghi log và để test ghim được chốt
    /// idempotency; người gọi không cần rẽ nhánh theo nó (cả hai ca đều là thành công).
    /// </summary>
    public bool AlreadyRedeemed { get; set; }
}
