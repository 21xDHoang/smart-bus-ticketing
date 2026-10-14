namespace SmartBus.Api.Entities;

/// <summary>
/// Kết quả của MỘT lượt gọi cổng hoàn tiền — ghi vào <see cref="RefundLog"/> (US 8). Lưu dạng chuỗi
/// trong CSDL (quy ước A3).
///
/// Ba giá trị, đúng ba thứ cổng có thể trả lời: đã hoàn xong, từ chối/lỗi, hoặc <b>không trả lời</b>
/// (timeout, mất kết nối giữa chừng). Giá trị thứ ba không phải cho đủ bộ — nó là ca có thật của mọi
/// cổng thanh toán, và là ca duy nhất mà hệ thống KHÔNG được phép đoán.
///
/// 🔴 <b><see cref="Unresolved"/> cố ý là giá trị 0</b> — khác mọi enum còn lại của dự án, và khác
/// cả <see cref="TicketValidationResult"/> (ở đó giá trị 0 là <c>NotFound</c>, chọn kiểu hỏng ồn ào
/// thay vì im lặng). Lý do ở đây là <b>tiền</b>, không phải tính đọc được:
///
/// | Nếu giá trị 0 là… | Một dòng log ghi thiếu kết quả sẽ… | Hậu quả |
/// |---|---|---|
/// | <c>Succeeded</c> | tự nhận là đã hoàn tiền | <b>im lặng</b> — sổ sách ghi đã trả trong khi tiền chưa đi |
/// | <c>Failed</c> | tự nhận là thất bại | job thử lại theo lịch (dòng 41) gọi cổng lần nữa cho một khoản <b>có thể đã hoàn</b> → hoàn hai lần |
/// | <c>Unresolved</c> | tự nhận là <b>chưa rõ</b> | khoản nằm nguyên ở <see cref="RefundStatus.Pending"/>, không lượt gọi tự động nào chạm tới, và nó lộ ra ở bước đối soát (dòng 43) |
///
/// Chọn kiểu hỏng ồn ào — nhưng ở đây "ồn ào" phải hiểu là <b>không tự quyết định thay cổng</b>.
/// Đây cũng là lý do enum này KHÔNG dùng chung với <see cref="RefundStatus"/>: <c>RefundStatus</c>
/// có <c>Pending</c> nghĩa là "chưa gọi cổng xong", còn lượt gọi ở đây đã kết thúc — chỉ là kết thúc
/// mà không có câu trả lời. Gộp hai bộ giá trị làm mất đúng phân biệt đó.
/// </summary>
public enum RefundLogResult
{
    /// <summary>
    /// Cổng <b>không trả lời</b>: hết thời gian chờ, đứt kết nối, hoặc trả về mã không nhận dạng
    /// được. Hệ thống chưa biết tiền có rời cổng hay không.
    ///
    /// 🔴 Khoản hoàn của lượt này <b>giữ nguyên <see cref="RefundStatus.Pending"/></b> và KHÔNG được
    /// xếp vào lịch thử lại — gọi lại một lượt chưa rõ kết quả là đúng đường dẫn tới hoàn hai lần.
    /// Việc tra cho ra nhẽ thuộc bước đối soát (dòng 43): đối chiếu với sao kê cổng rồi mới chốt
    /// trạng thái. Lý do đầy đủ: <c>docs/35-csdl-hoan-tien.md</c> §4.3.
    /// </summary>
    Unresolved,

    /// <summary>
    /// Cổng trả lời <b>từ chối</b> hoặc báo lỗi — ví dụ sai mã định danh tài khoản nhận hoàn. Đây
    /// là câu trả lời rõ ràng, nên khoản hoàn lật sang <see cref="RefundStatus.Failed"/> và được
    /// phép xếp lịch thử lại.
    /// </summary>
    Failed,

    /// <summary>
    /// Cổng xác nhận <b>đã hoàn tiền</b>. Mã giao dịch phía cổng của lượt này ghi vào
    /// <see cref="RefundLog.GatewayTransactionId"/>, và khoản hoàn lật sang
    /// <see cref="RefundStatus.Succeeded"/>.
    /// </summary>
    Succeeded,
}
