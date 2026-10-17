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
/// Hai giá trị <c>Refund…</c> là kết quả CUỐI của một khoản hoàn tiền thuộc US 8 — task Sprint 4
/// *"Thông báo kết quả hoàn tiền qua email và trên web"* (Phùng Duy Hoàng, dòng 34): báo cho CHỦ
/// khoản hoàn khi dòng <see cref="Refund"/> đã chốt kết quả cuối. Vế "trên web" được cài, vế email
/// không — A8.5 chốt thông báo in-app + bảng <c>Notifications</c>, và người đặt việc chốt lại ngày
/// 15/10. Chính sách đầy đủ (khi nào báo, khi nào im lặng) ở doc-comment hai giá trị dưới và
/// docs/41-thong-bao-ket-qua-hoan-tien.md.
///
/// ⚠️ Đây KHÔNG phải bản sao của <see cref="CancellationRequestStatus"/>: <c>Approved</c> của yêu cầu
/// huỷ và <c>Approved</c> của yêu cầu đổi là hai thông báo khác nhau (một bên là "vé đã huỷ", một
/// bên là "vé mới đã phát hành"), nên không thể gộp còn hai giá trị. Cũng không thể chỉ có hai giá
/// trị "theo loại yêu cầu" rồi để trạng thái nằm ở <see cref="Notification.RelatedEntityId"/>: màn
/// danh sách thông báo phải vẽ được nhãn và biểu tượng ngay từ dòng thông báo, không được bắt nó
/// truy vấn chéo sang bảng yêu cầu — bảng yêu cầu có thể đã bị xoá theo thời gian, còn thông báo là
/// thứ khách đọc lại được (A8.5).
///
/// Hai giá trị <c>Trip…</c> là thông báo trễ/đổi lịch trình chuyến thuộc US 10 — task Sprint 4
/// *"Service thông báo trễ chuyến / thay đổi lịch trình"* (Phùng Duy Hoàng, dòng 44): báo cho mọi
/// hành khách có vé <c>Paid</c> của chuyến khi quản lý sửa giờ chạy qua
/// <c>PUT /routes/{routeId}/trips/{id}</c>. Chính sách đầy đủ ở doc-comment hai giá trị dưới và
/// docs/44-thong-bao-tre-chuyen-doi-lich-trinh.md.
///
/// Giá trị thứ chín — <c>RefundReconciliationAlert</c> — là cảnh báo VẬN HÀNH của bước đối soát hoàn
/// tiền hằng ngày (dòng 32 Sprint 4, Nguyễn Duy Kiên): loại đầu tiên gửi cho Admin/Manager thay vì
/// hành khách. Lý do đầy đủ ở doc-comment giá trị đó và docs/48-doi-soat-hoan-tien-hang-ngay.md.
///
/// ⚠️ Tập giá trị này CÒN DÀI RA: Sprint 5 (US 10/US 11 — thông báo trạm, sự cố) sẽ thêm giá trị
/// cho các loại khác. Thêm giá trị enum KHÔNG tốn migration (cột là varchar, không có CHECK
/// constraint), nhưng là thay đổi hợp đồng với frontend — phải báo trước khi thêm, đúng luật 5.
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
    TicketChangeRejected,

    /// <summary>
    /// Lượt gọi cổng hoàn tiền THÀNH CÔNG — tiền đã thực sự rời cổng. Chỉ phát ở kết quả CUỐI: một
    /// khoản hoàn có thể phải gọi cổng nhiều lượt (thang thử lại), nhưng thành công là lượt cuối của
    /// khoản đó (dòng đã Succeeded không bao giờ được gọi lại — gọi nữa là hoàn hai lần), nên "mỗi
    /// lượt thành công" và "khoản đã Succeeded" là một.
    /// </summary>
    RefundSucceeded,

    /// <summary>
    /// Khoản hoàn đã HẾT LỊCH thử lại (<see cref="Refund.NextRetryAt"/> null) mà vẫn chưa chuyển
    /// được — hệ thống đã làm hết phần tự động, khoản chờ quản trị xử lý tay. Các lượt thất bại CÒN
    /// lịch thử lại KHÔNG phát loại này: một khoản hỏng tạm thời sẽ dội tới năm thông báo giống hệt
    /// nhau. Khoản còn <see cref="RefundStatus.Pending"/> (cổng chưa trả lời) cũng không phát — chưa
    /// có gì để nói, và đoán là chuyện hệ thống này không làm.
    /// </summary>
    RefundFailed,

    /// <summary>
    /// Chuyến bị <b>lùi giờ</b>: ít nhất một mốc (khởi hành hoặc tới dự kiến) đổi, <b>mọi</b> mốc đổi
    /// đều ra sau, và <see cref="Trip.ArrivalTime"/> không đổi giữa null và có giá trị. Phát cho mọi
    /// hành khách có vé <c>Paid</c> của chuyến, ngay sau khi lượt PUT đã lưu. Một chuyến bị dời hai
    /// lần là HAI sự kiện — không có chốt chống trùng cho loại này (xem docs/44 §3).
    /// </summary>
    TripDelayed,

    /// <summary>
    /// Lịch trình chuyến đổi theo hướng khác "lùi giờ": có mốc <b>sớm hơn</b>, hoặc
    /// <see cref="Trip.ArrivalTime"/> đổi giữa null và có giá trị (thêm/bỏ mốc tới). Đây là loại
    /// duy nhất còn lại khi giờ thực sự đổi, nên hai loại <c>Trip…</c> phủ kín mọi lần đổi giờ.
    /// Cùng đối tượng nhận và cùng lối phát với <see cref="TripDelayed"/>.
    /// </summary>
    TripScheduleChanged,

    /// <summary>
    /// Cảnh báo VẬN HÀNH: lượt đối soát hoàn tiền hằng ngày tìm thấy lệch số liệu (US 8 — task
    /// *"Đối soát hoàn tiền hằng ngày + cảnh báo khi lệch số liệu"*, dòng 32 Sprint 4, Nguyễn Duy
    /// Kiên).
    ///
    /// <para>
    /// ⚠️ Đây là loại thông báo ĐẦU TIÊN không nhắm vào hành khách: người nhận là mọi tài khoản
    /// <c>Admin</c>/<c>Manager</c> đang hoạt động. Nó trỏ vào NHIỀU khoản hoàn cùng lúc, nên
    /// <see cref="Notification.RelatedEntityType"/> và <see cref="Notification.RelatedEntityId"/> để
    /// null — gán một mã đại diện là nói dối về phạm vi của thông báo. Mã các khoản hoàn liên quan
    /// nằm trong <see cref="Notification.Body"/> để quản trị mở đúng dòng ở màn quản lý.
    /// </para>
    ///
    /// <para>
    /// Nội dung KHÔNG có số tiền — cùng luật với mọi loại khác (docs/api-contract.md, mục "Chín loại
    /// thông báo"): số tiền tra ở chính dòng <c>Refunds</c>, chép vào đây là dựng bản sao thứ hai có
    /// thể lệch. Chỉ báo khi CÓ lệch, và tối đa một lần mỗi ngày — xem
    /// <c>RefundReconciliationNotifier</c>.
    /// </para>
    ///
    /// <para>
    /// ⚠️ Frontend <c>notificationApi.ts</c> chưa khai loại thứ chín (file của Nguyễn Đình Băng).
    /// <c>NotificationBell</c> sẽ xếp nó vào nhóm đỏ mặc định — đúng hướng cho một cảnh báo — nhưng
    /// nhãn và biểu tượng riêng cần Băng bổ sung; đã ghi ở docs/api-contract.md và docs/48.
    /// </para>
    /// </summary>
    RefundReconciliationAlert,

    /// <summary>
    /// Báo cáo xuất ở nền đã DỰNG XONG — file sẵn sàng tải. Task *"Xử lý xuất file lớn bất đồng bộ +
    /// thông báo khi xong"* (Sprint 5 dòng 64, Vàng Thị Dăm).
    ///
    /// Người nhận là người ĐẶT job (không phải mọi quản lý): thông báo này chỉ có nghĩa với chủ
    /// file, và đường dẫn tải chỉ mở cho chính họ. <see cref="Notification.RelatedEntityType"/> là
    /// <see cref="NotificationTarget.ExportJob"/>, id là <c>ExportJobs.Id</c>.
    /// </summary>
    ExportCompleted,

    /// <summary>
    /// Báo cáo xuất ở nền THẤT BẠI — không có file để tải. Cùng người nhận và cùng con trỏ với
    /// <see cref="ExportCompleted"/>; lý do nằm ở <see cref="Notification.Body"/> để người dùng biết
    /// nên thu hẹp bộ lọc hay báo quản trị.
    /// </summary>
    ExportFailed
}
