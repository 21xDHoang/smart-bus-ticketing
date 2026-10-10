namespace SmartBus.Api.Dtos.Vouchers;

/// <summary>
/// Thống kê hiệu quả voucher đã phát hành — GET /api/vouchers/statistics. Hợp đồng "Voucher —
/// /vouchers", mục "GET /vouchers/statistics" của docs/api-contract.md.
///
/// Một dòng cho mỗi voucher ĐÃ PHÁT HÀNH (kể cả voucher chưa ai dùng) cộng sáu con số tổng. Không
/// lọc theo thời gian, không lọc theo trạng thái, không phân trang — task chỉ hỏi "hiệu quả voucher",
/// và khoảng <c>ValidFrom..ValidUntil</c> của chính voucher đã là khoảng chiến dịch của nó.
///
/// 🔴 <b>Sáu con số tổng đến từ truy vấn gộp ĐỘC LẬP, không cộng lại từ <see cref="Items"/>.</b> Nếu
/// cộng từ <see cref="Items"/> thì năm đẳng thức dưới đây đúng theo ĐỊNH NGHĨA — không test nào có
/// thể đỏ, và cả mục "đẳng thức" chỉ còn là tài liệu. Đếm thẳng bằng truy vấn riêng thì đẳng thức mới
/// là phép kiểm thật: <see cref="TotalVouchers"/> bắt lỗi truy vấn nền lọc rơi voucher (nhất là lọc
/// mất voucher <c>Inactive</c>), <see cref="TotalUsed"/> bắt lỗi gộp rơi dòng usage, hai tổng tiền bắt
/// lỗi cộng tiền, <see cref="NeverUsedVouchers"/> bắt lỗi đếm bằng nguồn sai. Đúng lối
/// <see cref="FeedbackStatisticsService"/> đã làm cho thống kê phản ánh.
///
/// Năm đẳng thức hợp đồng khoá lại, màn hình được phép dựa vào:
///
/// <code>
/// sum(Items[].UsedCount)                       == TotalUsed
/// sum(Items[].TotalOrderAmount)                == TotalOrderAmount
/// sum(Items[].TotalDiscountAmount)             == TotalDiscountAmount
/// count(Items)                                 == TotalVouchers
/// count(Items where UsedCount == 0)            == NeverUsedVouchers
/// </code>
///
/// Khác <c>WithoutTrip</c> của thống kê phản ánh, ở đây <b>không có phần dư</b>:
/// <c>VoucherUsage.VoucherId</c> là FK không nullable nên mọi lượt tiêu thụ đều thuộc về một voucher.
/// Vì vậy các đẳng thức đúng tuyệt đối, không cần số hạng bù.
/// </summary>
public class VoucherStatisticsResponse
{
    /// <summary>
    /// Tổng số voucher đã phát hành — MỌI trạng thái, kể cả <c>Inactive</c>. Lọc mất
    /// <c>Inactive</c> là <c>sum(Items[].UsedCount)</c> tụt xuống dưới <see cref="TotalUsed"/> và
    /// đẳng thức đỏ ngay.
    /// </summary>
    public int TotalVouchers { get; set; }

    /// <summary>Tổng <c>quantity</c> — tổng số lượt đã phát hành, mẫu số của tỉ lệ tiêu thụ.</summary>
    public int TotalIssued { get; set; }

    /// <summary>
    /// Tổng lượt đã tiêu thụ. Nguồn: bảng <c>VoucherUsages</c>, KHÔNG phải cột denormalized
    /// <c>Vouchers.UsedCount</c> — xem ghi chú ở <see cref="VoucherStatisticsItemResponse.UsedCount"/>.
    /// </summary>
    public int TotalUsed { get; set; }

    /// <summary>Tổng tiền TRƯỚC giảm của mọi lượt đã tiêu thụ.</summary>
    public decimal TotalOrderAmount { get; set; }

    /// <summary>Tổng tiền đã giảm — chi phí thật của cả chương trình khuyến mãi.</summary>
    public decimal TotalDiscountAmount { get; set; }

    /// <summary>
    /// Số voucher đã phát hành mà CHƯA AI DÙNG (<c>usedCount = 0</c>).
    ///
    /// ⚠️ Đây là <b>tập con đã có trong <see cref="Items"/></b>, KHÔNG phải một rổ riêng như
    /// <c>WithoutTrip</c> của thống kê phản ánh — mọi voucher đếm ở đây đều đã có mặt trong
    /// <see cref="Items"/> với <c>usedCount: 0</c>. Nó chỉ là trường tiện dụng để màn hình khỏi phải
    /// tự lọc, đừng đọc nó thành "số voucher không xuất hiện trong bảng".
    ///
    /// Đếm qua navigation <c>v.Usages.Any()</c> (dịch thành <c>NOT EXISTS</c>) chứ KHÔNG đếm bằng cột
    /// <c>Vouchers.UsedCount</c> — phải cùng nguồn với <c>usedCount</c> của từng dòng, nếu không một
    /// bộ đếm vênh sẽ cho ra hai con số mâu thuẫn nhau trong cùng một response.
    /// </summary>
    public int NeverUsedVouchers { get; set; }

    /// <summary>
    /// Một dòng mỗi voucher đã phát hành, kể cả voucher chưa ai dùng. Sắp <c>usedCount</c> giảm dần,
    /// trùng số thì theo <c>code</c> tăng dần — xem ghi chú ở service.
    /// </summary>
    public IReadOnlyList<VoucherStatisticsItemResponse> Items { get; set; } = [];
}
