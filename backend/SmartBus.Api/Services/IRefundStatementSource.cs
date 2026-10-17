namespace SmartBus.Api.Services;

/// <summary>
/// Nguồn SAO KÊ hoàn tiền của cổng thanh toán — chỗ nối cho bước đối soát hằng ngày (US 8, task
/// *"Đối soát hoàn tiền hằng ngày + cảnh báo khi lệch số liệu"*, dòng 32 Sprint 4, Nguyễn Duy Kiên).
///
/// <para>
/// 🔴 <b>Bản chạy hiện tại KHÔNG có nguồn sao kê thật.</b> Không cổng nào trong repo có API tra cứu
/// khoản hoàn: <c>MoMoGatewayService.QueryTransactionAsync</c> và
/// <c>ZaloPayGatewayService.QueryOrderAsync</c> tra GIAO DỊCH THU TIỀN, không tra lệnh hoàn; ZaloPay
/// có <c>query_refund</c> nhưng client hoàn tiền chưa dùng tới và VNPay sandbox giới hạn chức năng
/// hoàn. Vì vậy bản mặc định là <see cref="UnavailableRefundStatementSource"/> — nó trả <c>null</c>
/// (nguồn không khả dụng) chứ KHÔNG trả một sao kê rỗng.
/// </para>
///
/// <para>
/// 🔴 <b>Phân biệt "không có nguồn" với "sao kê rỗng" là bắt buộc.</b> Một sao kê rỗng nghĩa là
/// *"cổng xác nhận không có lệnh hoàn nào trong khoảng này"* — đem nó đi đối chiếu sẽ báo MỌI khoản
/// hoàn thành công trong ngày là "không có trên sao kê", tức là cảnh báo giả hàng loạt. <c>null</c>
/// nghĩa là *"không biết"*, và bước đối soát chỉ được bỏ phần đối chiếu sao kê, không được kết luận
/// gì từ chỗ không biết.
/// </para>
///
/// <para>
/// Khi có cổng thật (hoặc khi vận hành muốn nhập file sao kê do cổng gửi), chỉ cần thêm một lớp cài
/// đặt giao diện này và đổi đúng một dòng đăng ký trong <c>Program.cs</c> — service đối soát và
/// notifier không phải sửa. Đây là lý do seam tồn tại ngay từ bản chưa có nguồn: phần đối chiếu nội
/// bộ và phần đối chiếu sao kê là hai loại phép kiểm khác nhau, và loại thứ hai phải có chỗ đứng
/// trước khi có dữ liệu thật.
/// </para>
/// </summary>
public interface IRefundStatementSource
{
    /// <summary>
    /// Đọc sao kê hoàn tiền trong khoảng <paramref name="fromUtc"/>–<paramref name="toUtc"/> (bao gồm
    /// hai đầu).
    /// </summary>
    /// <returns>
    /// <c>null</c> khi KHÔNG đọc được sao kê (chưa có nguồn, cổng không hỗ trợ, đang lỗi). Trả về một
    /// <see cref="RefundStatement"/> rỗng khi đọc được và cổng xác nhận không có lệnh hoàn nào —
    /// hai câu trả lời này khác nhau, xem doc-comment giao diện.
    /// </returns>
    Task<RefundStatement?> GetStatementAsync(
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Sao kê hoàn tiền của một khoảng thời gian. Không có trường tổng hợp nào — mọi phép đối chiếu đều
/// làm trên <see cref="Entries"/>, và một phép "tổng số tiền khớp" không nói được khoản nào lệch.
/// </summary>
public sealed class RefundStatement
{
    public DateTime FromUtc { get; init; }

    public DateTime ToUtc { get; init; }

    public IReadOnlyList<RefundStatementEntry> Entries { get; init; } = [];
}

/// <summary>Một dòng trên sao kê cổng — một lệnh hoàn cổng đã ghi nhận.</summary>
public sealed class RefundStatementEntry
{
    /// <summary>
    /// Mã giao dịch hoàn phía cổng. Đây là khoá đối chiếu với <c>Refunds.GatewayTransactionId</c> và
    /// <c>RefundLogs.GatewayTransactionId</c> — so bằng <see cref="StringComparison.OrdinalIgnoreCase"/>
    /// vì các cổng không thống nhất chữ hoa/thường.
    /// </summary>
    public string GatewayTransactionId { get; init; } = string.Empty;

    /// <summary>Số tiền cổng đã ghi nhận cho lệnh hoàn này.</summary>
    public decimal Amount { get; init; }

    public RefundStatementEntryStatus Status { get; init; }

    /// <summary>Thông điệp cổng kèm theo (nếu có) — chỉ để đưa vào log khi lệch.</summary>
    public string? Message { get; init; }
}

/// <summary>
/// Trạng thái một lệnh hoàn trên sao kê. Cố ý chỉ HAI giá trị, khớp câu hỏi duy nhất mà bước đối soát
/// cần trả lời: cổng đã trả tiền hay chưa. Trạng thái trung gian kiểu "đang xử lý" không đối chiếu
/// được với <c>RefundStatus</c> của hệ thống (vốn cũng chỉ có Pending/Succeeded/Failed) và sẽ buộc
/// bước đối soát phải đoán — thứ dự án này không làm.
/// </summary>
public enum RefundStatementEntryStatus
{
    Succeeded,

    Failed,
}

/// <summary>
/// Bản mặc định của <see cref="IRefundStatementSource"/> — luôn trả <c>null</c> vì bản chạy hiện tại
/// chưa có cổng nào cung cấp sao kê hoàn tiền (lý do đầy đủ ở doc-comment giao diện).
///
/// <para>
/// ⚠️ Đây KHÔNG phải một lớp giả để test qua: nó là câu trả lời trung thực "không biết", và bước đối
/// soát ghi rõ <c>StatementAvailable = false</c> trong báo cáo. Không có cảnh báo giả nào được sinh
/// từ chỗ không có dữ liệu.
/// </para>
/// </summary>
public sealed class UnavailableRefundStatementSource : IRefundStatementSource
{
    public Task<RefundStatement?> GetStatementAsync(
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default)
        => Task.FromResult<RefundStatement?>(null);
}
