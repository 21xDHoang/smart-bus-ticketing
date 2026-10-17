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
/// Có <see cref="Cancelled"/> từ đợt bổ sung màn "Quản lý vé tháng" cho Admin/Quản lý
/// (17/10/2026): hành khách vẫn KHÔNG tự huỷ vé, nhưng quản trị viên cần huỷ/mở lại một vé
/// (khách báo mất mã QR, thu hồi vé bán nhầm…). Trạng thái này KHÔNG suy ra được từ ngày tháng,
/// nên nó là nguồn sự thật duy nhất cho câu "vé đã bị huỷ chưa" — khác Active/Expired.
/// Huỷ vé không hoàn tiền: chưa có nghiệp vụ hoàn tiền vé tháng (A8.2).
/// </summary>
public enum MonthlyPassStatus
{
    /// <summary>Đang trong thời gian hiệu lực. Trạng thái mặc định của vé vừa đăng ký.</summary>
    Active,

    /// <summary>
    /// Đã quá <see cref="MonthlyPass.ValidTo"/>, do BackgroundService lật. Xem cảnh báo ở trên:
    /// giá trị này có độ trễ, không được dùng làm nguồn sự thật về hiệu lực.
    /// </summary>
    Expired,

    /// <summary>
    /// Quản trị viên huỷ vé trước hạn (hoặc vé bị thu hồi). KHÁC hai giá trị trên ở một điểm
    /// quyết định: đây là **chặn cứng** — vé đã huỷ không còn hiệu lực kể cả khi
    /// <c>ValidFrom &lt;= bây giờ &lt;= ValidTo</c>. Mọi truy vấn hỏi "vé này còn dùng được
    /// không" phải loại trạng thái này ra.
    ///
    /// Do quản trị viên đặt qua <c>PATCH /monthly-passes/{id}/status</c>, KHÔNG do job hết hạn
    /// (job chỉ lật Active → Expired). Có thể mở lại thành Active khi vé chưa quá ValidTo.
    /// </summary>
    Cancelled
}
