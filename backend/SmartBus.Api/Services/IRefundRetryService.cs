namespace SmartBus.Api.Services;

/// <summary>
/// Ruột MỘT lượt quét hàng đợi hoàn tiền tới hạn của job nền (US 8 — task *"Xử lý hoàn tiền thất
/// bại + retry theo lịch"*, Phùng Duy Hoàng).
///
/// Người gọi duy nhất: <c>RefundRetryBackgroundService</c> — vòng lặp chỉ canh nhịp, còn ruột nằm
/// ở đây (cùng lối với <see cref="ISeatHoldExpiryService"/>).
///
/// Một lượt quét: đọc danh sách ID khoản <c>Failed</c> tới hạn (một truy vấn chỉ lấy ID, đi thẳng
/// vào <c>IX_Refunds_Status_NextRetryAt</c>), rồi thực hiện từng lượt gọi cổng qua
/// <see cref="IRefundAttemptService"/> trong scope riêng của từng khoản.
///
/// Vì sao <c>now</c> là tham số: test ghim được mốc tới hạn, và cả lượt quét dùng đúng MỘT mốc —
/// các khoản của cùng một đợt mang cùng dấu thời gian, dễ đối chiếu khi tra log.
/// </summary>
public interface IRefundRetryService
{
    /// <summary>
    /// Quét và thực hiện các lượt thử lại tới hạn. Trả về SỐ LƯỢT GỌI CỔNG đã thực hiện — không
    /// phải số khoản tới hạn, vì khoản thua cuộc đua hoặc vừa bị lượt khác đổi trạng thái thì không
    /// tính. Lỗi CSDL ngoài dự kiến thì ném ra; <c>OperationCanceledException</c> khi app đang tắt
    /// (job nền bắt cả hai — xem <c>RefundRetryBackgroundService</c>).
    /// </summary>
    Task<int> SweepAsync(DateTime now, CancellationToken cancellationToken = default);
}
