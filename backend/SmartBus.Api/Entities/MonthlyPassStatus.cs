namespace SmartBus.Api.Entities;

/// <summary>
/// Trạng thái hiệu lực của một vé tháng (US 16). Lưu dạng chuỗi trong CSDL (quy ước A3).
///
/// ⚠️ Đọc kèm cảnh báo dưới đây trước khi dùng — nó KHÁC lối của <see cref="DiscountRequestStatus"/>.
///
/// <see cref="DiscountRequestStatus"/> cố ý KHÔNG có Expired vì "hết hạn suy ra được từ
/// ExpiresAt &lt; now, thêm Expired là đẻ ra hai nguồn sự thật". Lập luận đó đúng, nhưng ở đây
/// vẫn có <see cref="Expired"/> vì hai nguồn của nhóm yêu cầu tường minh:
///   - Sheet Sprint 2 giao Nguyễn Duy Kiên task "BackgroundService tự động chuyển vé tháng hết
///     hạn sang trạng thái Expired" — job đó chỉ có nghĩa nếu Expired là giá trị lưu trữ.
///   - frontend/src/api/monthlyPassApi.ts (đã merge vào main) khai
///     <c>MonthlyPassStatus = 'Active' | 'Expired'</c> và ghi rõ "khớp cột varchar Status của
///     bảng MonthlyPasses".
///
/// 🔴 HỆ QUẢ BẮT BUỘC NHỚ — vì Expired là trạng thái LƯU, nó LUÔN có thể lệch với thực tế:
/// giữa lúc ValidTo trôi qua và lúc job quét chạy (job chạy theo lịch, không chạy từng giây),
/// một vé đã hết hạn vẫn còn Status = Active. Cho nên:
///   - Mọi truy vấn hỏi "vé này còn dùng được không" (soát vé QR, US 15) PHẢI so cả ValidTo,
///     không được chỉ hỏi Status == Active.
///   - Truy vấn "vé tháng đang hoạt động của tôi" cũng phải so ValidFrom &lt;= now &lt;= ValidTo.
///   - Expired chỉ dùng để LỌC và THỐNG KÊ cho rẻ, không phải nguồn sự thật về hiệu lực.
///
/// KHÔNG có Cancelled: không story nào cho hành khách tự huỷ vé tháng, và A8.2 không nhắc tới
/// hoàn tiền vé tháng. Vé tháng hết hạn là hết hiệu lực, không có nghiệp vụ thu hồi.
/// </summary>
public enum MonthlyPassStatus
{
    /// <summary>Đang trong thời gian hiệu lực. Trạng thái mặc định của vé vừa đăng ký.</summary>
    Active,

    /// <summary>
    /// Đã quá <see cref="MonthlyPass.ValidTo"/>, do BackgroundService lật. Xem cảnh báo ở trên:
    /// giá trị này có độ trễ, không được dùng làm nguồn sự thật về hiệu lực.
    /// </summary>
    Expired
}
