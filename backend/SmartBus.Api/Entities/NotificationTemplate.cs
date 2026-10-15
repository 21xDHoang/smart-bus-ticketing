namespace SmartBus.Api.Entities;

/// <summary>
/// Bảng <c>NotificationTemplates</c> — MẪU NỘI DUNG cho một loại thông báo, để quản trị viên sửa được
/// câu chữ mà không phải deploy lại API.
///
/// Thuộc task *"Migrate bảng Notifications, NotificationTemplates, Subscriptions"* của bảng phân công
/// **Sprint 5, dòng 14** (Vàng Thị Dăm, ưu tiên Cao, 4 điểm), phục vụ US 10 *"thông báo khi xe sắp đến
/// trạm đón/trạm xuống"*. Chi tiết quyết định: <c>docs/37-csdl-mau-thong-bao-va-theo-doi.md</c>.
///
/// ⚠️ <b>Bảng này KHÔNG có trong A9.</b> A9 (#15) chỉ có <c>Notifications</c>. Cùng tình trạng với
/// <c>TicketChanges</c>/<c>CancellationRequests</c> (<c>docs/32</c> §1), <c>TicketValidations</c>
/// (<c>docs/34</c> §1) và <c>Refunds</c>/<c>RefundLogs</c> (<c>docs/40</c> §1): tên bảng có thật
/// trong bảng phân công, A9 thì không. Cách xử lý vẫn như cũ — <b>dựng theo phân công, rồi báo nhóm
/// bổ sung A9</b>; chủ CSDL không tự thêm bảng ngoài A9 mà im lặng.
///
/// ⚠️ <b>Vì sao có bảng này khi <c>docs/33</c> §6.3 đã cố ý dùng bốn hằng chuỗi trong code:</b> hai
/// việc khác nhau, không mâu thuẫn. Bốn hằng chuỗi ở <c>TicketRequestResultNotifier</c> là **câu mặc
/// định** — chúng vẫn còn nguyên giá trị và vẫn là thứ được dùng khi loại thông báo đó chưa có mẫu
/// nào (<see cref="IsActive"/> giải thích cùng ý này). Bảng này là **phần ghi đè** do quản trị viên
/// soạn, phục vụ màn *"Màn hình Admin quản lý mẫu thông báo"* (Sprint 5, dòng 24). Chính
/// <c>docs/33</c> §6.3 đã chốt trước rằng *"chuyển sang template sau là thay chỗ đọc chuỗi, không
/// phải sửa schema"* — nhánh này dựng đúng cái schema đó.
///
/// 🔴 <b>Vì sao KHÔNG phải "bảng mẫu thì phải có mẫu mới gửi được thông báo":</b> nếu bảng này là
/// nguồn DUY NHẤT thì một loại thông báo chưa có mẫu sẽ **im lặng không gửi** — khách không nhận được
/// gì và không có lỗi nào để lần, đúng kiểu hỏng mà <c>docs/33</c> §3.5 phải bắt. Quy ước của bảng
/// này vì vậy là <b>mẫu là phần GHI ĐÈ, không phải điều kiện</b>: không có mẫu (hoặc mẫu đang tắt) →
/// tầng service dùng câu mặc định trong code.
/// </summary>
public class NotificationTemplate
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Loại thông báo mà mẫu này dùng để soạn — xem <see cref="NotificationType"/>. Cột này là **khoá
    /// nghiệp vụ** của bảng: có chỉ mục duy nhất, mỗi loại tối đa MỘT mẫu.
    ///
    /// 🔴 Vì sao duy nhất: nếu một loại có hai mẫu thì câu hỏi *"lấy mẫu nào"* không có câu trả lời
    /// đúng — tầng service sẽ phải chọn theo <c>Id</c> nhỏ nhất hoặc theo <c>CreatedAt</c>, tức là
    /// gửi cho khách một câu **tuỳ theo thứ tự dòng trong CSDL**. Sai kiểu đó chỉ lộ ra khi có người
    /// tạo mẫu thứ hai, và lúc đó rất khó lần. Chặn ở tầng CSDL là chặn được ngay lúc tạo.
    ///
    /// ⚠️ Lưu dạng chuỗi trong CSDL (A3), <c>varchar(30)</c> — cùng độ rộng đã dùng cho
    /// <see cref="Notification.Type"/>, và <c>NotificationType</c> sẽ dài thêm ở Sprint 5 (thông báo
    /// trạm, trễ chuyến, sự cố). Thêm giá trị enum KHÔNG tốn migration vì cột là varchar không có
    /// CHECK constraint — nhưng đó là thay đổi hợp đồng với frontend, phải báo trước (luật 5).
    /// </summary>
    public NotificationType Type { get; set; }

    /// <summary>
    /// Dòng tiêu đề của mẫu, <c>varchar(200)</c> — **cùng độ rộng** với <see cref="Notification.Title"/>
    /// vì khi dùng mẫu thì giá trị ở đây được chép thẳng sang cột đó. Để hai cột khác độ rộng là tạo
    /// ra một cái bẫy: mẫu dài 250 ký tự lưu được (cột này rộng hơn) nhưng lúc ghi thông báo mới vỡ,
    /// tức là lỗi chỉ nổ ở lượt gửi chứ không nổ ở lượt soạn mẫu.
    ///
    /// Có thể chứa chỗ trống điền sau — xem <see cref="Body"/>.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Nội dung đầy đủ của mẫu, <c>text</c> — cùng kiểu với <see cref="Notification.Body"/> và cùng
    /// lý do ở trên. Cố ý không đặt trần độ dài: nội dung là văn bản do người soạn, không có độ dài
    /// trần hợp lý (cùng lối <c>CancellationRequest.Reason</c>).
    ///
    /// ⚠️ <b>Quy ước CHỖ TRỐNG</b> (do nhánh này đề xuất, nhóm chưa chốt — xem <c>docs/37</c> §6):
    /// phần cần điền lúc gửi viết trong cặp ngoặc nhọn, tên PascalCase — ví dụ
    /// <c>"Xe buýt tuyến {RouteCode} sắp đến trạm {StopName}."</c>. Tầng service thay thế trước khi ghi
    /// vào <c>Notifications.Body</c>.
    ///
    /// 🔴 Hệ quả bắt buộc phải xử lý ở tầng service, KHÔNG được bỏ qua: một chỗ trống không có dữ
    /// liệu để điền thì **không được** để nguyên <c>{StopName}</c> trong thông báo khách đọc — phải
    /// thay bằng một câu trung tính hoặc bỏ cả câu đó. Và một chỗ trống gõ sai tên sẽ không bao giờ
    /// khớp, nên <b>việc kiểm danh sách chỗ trống hợp lệ theo từng <see cref="Type"/> là việc của
    /// endpoint lưu mẫu</b> (Sprint 5, dòng 24 — Dương Thị Hạnh) chứ không phải của người đưa thư.
    /// Cố ý không làm sẵn ở đây: danh sách chỗ trống hợp lệ phụ thuộc vào dữ liệu mà từng service
    /// (dòng 15/18/29) có trong tay, và cả ba service đó chưa ai viết.
    /// </summary>
    public string Body { get; set; } = string.Empty;

    /// <summary>
    /// Mẫu có đang được dùng hay không.
    ///
    /// 🔴 <b>Tắt mẫu KHÔNG có nghĩa là tắt thông báo</b> — nghĩa là quay về câu mặc định trong code
    /// (xem phần đầu file). Đây là lý do cột này tồn tại thay vì để quản trị viên xoá dòng: người ta
    /// cần tắt một mẫu đang soạn dở mà không mất phần chữ đã viết, và cần một cách tắt **không** biến
    /// thành "khách thôi nhận thông báo" trong im lặng.
    ///
    /// Dùng <c>bool</c> theo đúng lối A4 đã chỉ: *"Dùng cột trạng thái sẵn có: Users.IsActive,
    /// Routes.Status…"* — A4 gọi tên <c>IsActive</c> như cột bật/tắt hợp lệ của dự án, không phải
    /// <c>IsDeleted</c> (thứ bị cấm tuyệt đối).
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Thời điểm tạo mẫu (UTC).</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Thời điểm sửa mẫu gần nhất (UTC) — A4 bắt buộc có ở bảng **có sửa**.
    ///
    /// Khác hẳn <see cref="Notification"/> (A4 gọi tên đúng bảng đó ở nhóm chỉ-ghi-thêm, nên bảng đó
    /// cố ý không có cột này): mẫu là thứ quản trị viên sửa đi sửa lại. Giữ cột này là cách duy nhất
    /// trả lời *"câu chữ này đổi lúc nào"* khi một thông báo đã gửi bị khách thắc mắc.
    ///
    /// Nullable vì dòng vừa tạo chưa sửa lần nào. ⚠️ Không có cột "ai sửa": người sửa nằm ở
    /// <c>AuditLogs</c> do <c>AuditLogMiddleware</c> tự ghi cho lượt PUT/PATCH của màn quản lý mẫu —
    /// chép lại vào đây là hai nguồn cho cùng một sự thật.
    /// </summary>
    public DateTime? UpdatedAt { get; set; }
}
