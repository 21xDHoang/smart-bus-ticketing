namespace SmartBus.Api.Entities;

/// <summary>
/// Loại thông báo trong bảng <c>Notifications</c> (A9 #15). Lưu dạng chuỗi trong CSDL (quy ước A3):
/// "CancellationApproved" đọc là hiểu, còn 1 thì phải tra bảng mã.
///
/// Bốn giá trị đầu tiên là KẾT QUẢ XỬ LÝ của hai loại yêu cầu thuộc US 5 — task Sprint 4
/// *"Ghi log và thông báo kết quả xử lý cho hành khách"* (Vàng Thị Dăm):
/// <c>Pending → Approved | Rejected</c> ở <see cref="CancellationRequest"/> và
/// <see cref="TicketChange"/>, nhân với hai loại yêu cầu.
///
/// ⚠️ Đây KHÔNG phải bản sao của <see cref="CancellationRequestStatus"/>: <c>Approved</c> của yêu cầu
/// huỷ và <c>Approved</c> của yêu cầu đổi là hai thông báo khác nhau (một bên là "vé đã huỷ", một
/// bên là "vé mới đã phát hành"), nên không thể gộp còn hai giá trị. Cũng không thể chỉ có hai giá
/// trị "theo loại yêu cầu" rồi để trạng thái nằm ở <see cref="Notification.RelatedEntityId"/>: màn
/// danh sách thông báo phải vẽ được nhãn và biểu tượng ngay từ dòng thông báo, không được bắt nó
/// truy vấn chéo sang bảng yêu cầu — bảng yêu cầu có thể đã bị xoá theo thời gian, còn thông báo là
/// thứ khách đọc lại được (A8.5).
///
/// ⚠️ Tập giá trị này CÒN DÀI RA: Sprint 5 (US 10/US 11 — thông báo trạm, trễ chuyến, sự cố) sẽ
/// thêm giá trị cho các loại khác. Thêm giá trị enum KHÔNG tốn migration (cột là varchar, không có
/// CHECK constraint), nhưng là thay đổi hợp đồng với frontend — phải báo trước khi thêm, đúng luật 5.
/// Cố ý chưa khai sẵn các giá trị đó ở đây: khai một giá trị chưa ai ghi vào là mở một nhánh không
/// có dữ liệu, trong khi thêm sau chỉ là một dòng.
/// </summary>
public enum NotificationType
{
    /// <summary>Quản lý đã DUYỆT yêu cầu huỷ vé — vé đã bị huỷ, ghế trả về chợ.</summary>
    CancellationApproved,

    /// <summary>Quản lý đã TỪ CHỐI yêu cầu huỷ vé — vé giữ nguyên, lý do ở <see cref="Notification.Body"/>.</summary>
    CancellationRejected,

    /// <summary>Quản lý đã DUYỆT yêu cầu đổi chuyến — vé cũ bị huỷ và vé MỚI đã được phát hành.</summary>
    TicketChangeApproved,

    /// <summary>Quản lý đã TỪ CHỐI yêu cầu đổi chuyến — vé giữ nguyên, lý do ở <see cref="Notification.Body"/>.</summary>
    TicketChangeRejected
}
