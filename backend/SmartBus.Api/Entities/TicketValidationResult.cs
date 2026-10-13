namespace SmartBus.Api.Entities;

/// <summary>
/// Kết quả một LƯỢT SOÁT VÉ (US 15 — "Soát vé QR"). Lưu dạng chuỗi trong CSDL (quy ước A3).
///
/// Sáu giá trị = <c>true</c> cộng ĐÚNG năm lý do từ chối đã chốt ở hợp đồng endpoint
/// (<c>docs/29-ban-giao-api-kiem-tra-hieu-luc-qr.md</c> §3) và đã được frontend viết sẵn ở
/// <c>frontend/src/api/ticketValidationApi.ts</c> (nhánh <c>feature/15-quet-qr-camera</c>, Nguyễn Đình
/// Băng, dòng 20). Thêm một giá trị ở đây là đổi hợp đồng với màn quét QR — đừng thêm.
///
/// ⚠️ <b>Vì sao GỘP "hợp lệ" và "lý do từ chối" vào MỘT cột</b> thay vì hai cột <c>IsValid</c> +
/// <c>Reason</c>: hai cột cho phép sinh ra dòng tự mâu thuẫn (<c>IsValid = true</c> đi kèm
/// <c>Reason = 'Cancelled'</c>), và không có ràng buộc CSDL nào chặn được tổ hợp đó. Một cột thì
/// tổ hợp đó không tồn tại. Ánh xạ sang hợp đồng là 1–1: <see cref="Valid"/> → <c>valid = true,
/// reason = null</c>; năm giá trị còn lại → <c>valid = false, reason = &lt;tên giá trị&gt;</c>.
///
/// ⚠️ <b>Vì sao <see cref="Valid"/> CỐ Ý KHÔNG phải giá trị 0.</b> <c>default(TicketValidationResult)</c>
/// là <see cref="NotFound"/>, không phải <see cref="Valid"/>. Một lượt ghi quên gán
/// <see cref="TicketValidation.Result"/> vì vậy hiện ra như một lượt BỊ TỪ CHỐI — ồn ào, và lộ ra ngay
/// khi đối chiếu thống kê lượt soát theo chuyến (dòng 24). Nếu <see cref="Valid"/> đứng đầu thì cùng
/// lỗi đó lại thành một lượt HỢP LỆ: âm thầm, và làm sai đúng con số mà màn thống kê sinh ra để đếm.
/// Đây là bảng nhật ký kiểm toán — chọn kiểu hỏng ồn ào thay vì kiểu hỏng im lặng.
/// </summary>
public enum TicketValidationResult
{
    /// <summary>
    /// Không tra ra vé nào mang mã này — <c>reason = "NotFound"</c> ở hợp đồng. Là giá trị 0 (xem ⚠️
    /// trên lớp).
    ///
    /// Cố ý BAO GỒM cả "mã rác" lẫn "mã có chữ ký sai": docs/30 §3.2 chốt là hai ca đó phải trả về
    /// CÙNG một câu trả lời — người soát chỉ cần biết cho lên xe hay không, còn việc phân biệt "khách
    /// dán nhầm chuỗi" với "kẻ làm vé giả" là việc điều tra sau, dựa vào <c>ScannedCode</c> đã lưu
    /// nguyên văn. Tách thêm một giá trị <c>Forged</c> ở đây là nói cho người soát biết nhiều hơn mức
    /// cần thiết.
    /// </summary>
    NotFound,

    /// <summary>Vé có thật nhưng đã bị huỷ (<see cref="TicketStatus.Cancelled"/>) — <c>reason = "Cancelled"</c>.</summary>
    Cancelled,

    /// <summary>
    /// Vé đã được soát trước đó (<see cref="TicketStatus.Used"/>) — <c>reason = "AlreadyUsed"</c>.
    /// Đây là dòng mà task dòng 16 *"API chống soát trùng"* sinh ra khi chặn lượt quét thứ hai, và là
    /// bằng chứng duy nhất còn lại sau khi lượt quét trùng bị từ chối.
    /// </summary>
    AlreadyUsed,

    /// <summary>
    /// Vé hợp lệ nhưng thuộc chuyến KHÁC chuyến phụ xe đang chạy — <c>reason = "WrongTrip"</c>. Chỉ
    /// kết luận được khi lượt soát có <see cref="TicketValidation.TripId"/>; không có thì không bao
    /// giờ trả về giá trị này.
    /// </summary>
    WrongTrip,

    /// <summary>Chuyến đã khởi hành xong mà vé chưa được soát — <c>reason = "Expired"</c>.</summary>
    Expired,

    /// <summary>
    /// Vé hợp lệ, được lên xe — <c>valid = true, reason = null</c>. Đứng CUỐI vì lý do ở ⚠️ trên lớp,
    /// không phải vì thứ tự nghiệp vụ.
    /// </summary>
    Valid
}
