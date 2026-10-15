namespace SmartBus.Api.Services;

/// <summary>
/// Chính sách xếp lịch thử lại khoản hoàn thất bại (US 8 — task *"Xử lý hoàn tiền thất bại + retry
/// theo lịch"*, Phùng Duy Hoàng). Hàm thuần, không đụng CSDL: <c>RefundAttemptService</c> gọi nó
/// mỗi khi một lượt gọi cổng trả về <c>Failed</c> để tính mốc <c>NextRetryAt</c> kế tiếp.
///
/// Bậc thang: 5 phút → 30 phút → 2 giờ → 12 giờ → 24 giờ (tổng cộng ~38,5 giờ kể từ lượt đầu).
/// Lý do chọn: khoảng cách tăng dần để lượt hỏng tạm thời (cổng nghẽn, timeout thoáng qua) được
/// cứu sớm, còn lỗi cấu hình thật (sai mã tài khoản nhận hoàn) không dội bom cổng 5 phút một lần
/// suốt ngày. Hết thang thì <c>NextRetryAt = null</c> — khoản nằm lại ở <c>Failed</c> và chờ quản
/// trị bấm tay, CỐ Ý không thêm giá trị enum mới (docs/40 §2.6 + §6).
///
/// ⚠️ Cố ý hard-code thay vì đọc từ cấu hình — cùng lối và cùng lý do đã ghi ở
/// <c>SeatHoldExpiryBackgroundService</c>: một khoá cấu hình gõ sai sẽ làm app chết ngay lúc khởi
/// động, còn con số đúng ở đây chỉ có một. Muốn đổi chính sách thì sửa mảng này — đây là nguồn
/// duy nhất.
/// </summary>
public static class RefundRetryPolicy
{
    /// <summary>
    /// Khoảng chờ trước từng lượt thử lại. Chỉ số trong mảng = số lượt THỬ LẠI đã thực hiện tính
    /// đến lượt vừa hỏng (lượt <c>Initial</c> không nằm trong lịch — nó do service tạo khoản gọi
    /// ngay, không chờ).
    /// </summary>
    public static readonly IReadOnlyList<TimeSpan> Delays = new[]
    {
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(30),
        TimeSpan.FromMinutes(120),
        TimeSpan.FromMinutes(720),
        TimeSpan.FromMinutes(1440),
    };

    /// <summary>
    /// Mốc chờ lượt kế tiếp sau một lượt thất bại, hoặc <c>null</c> khi đã hết lịch.
    /// </summary>
    /// <param name="failedAt">
    /// Thời điểm lượt gọi vừa thất bại (service truyền đúng mốc <c>now</c> của lượt đó — không tự
    /// gọi <c>DateTime.UtcNow</c> để test ghim được mốc).
    /// </param>
    /// <param name="retriesAttempted">
    /// Số lượt THỬ LẠI đã thực hiện tính đến VÀ BAO GỒM lượt vừa hỏng — lượt <c>Initial</c> không
    /// phải thử lại nên lần hỏng đầu tiên truyền 0 (→ chờ <c>Delays[0]</c> = 5 phút), lượt thử lại
    /// thứ nhất hỏng truyền 1, …, lượt thử lại thứ năm hỏng truyền 5 → <c>null</c> (hết lịch, chờ
    /// quản trị bấm tay).
    /// </param>
    public static DateTime? NextRetryAt(DateTime failedAt, int retriesAttempted)
    {
        return retriesAttempted < Delays.Count ? failedAt + Delays[retriesAttempted] : null;
    }
}
