using System.ComponentModel.DataAnnotations;

namespace SmartBus.Api.Dtos.TicketRequests;

/// <summary>
/// Body của <c>PATCH /api/cancellation-requests/{id}</c> và <c>PATCH /api/ticket-changes/{id}</c> —
/// quản lý duyệt hoặc từ chối một yêu cầu huỷ / đổi vé (US 5, task *"API duyệt / từ chối yêu cầu
/// huỷ - đổi vé"* — Phùng Duy Hoàng, dòng 5 Sprint 4).
///
/// MỘT lớp dùng chung cho cả hai endpoint vì hai bảng có cùng vòng đời trạng thái và cùng nghiệp vụ
/// duyệt (chốt ở docs/api-contract.md, mục "Huỷ &amp; Đổi vé"). Tách đôi ra thì hai bản sẽ trôi khỏi
/// nhau ở đúng chỗ dễ trôi nhất — luật `rejectReason` chỉ có nghĩa khi từ chối.
///
/// <c>status</c> để DẠNG CHUỖI rồi kiểm ở tầng nghiệp vụ, không để kiểu enum — cùng lối
/// <see cref="Feedbacks.UpdateFeedbackStatusRequest.Status"/> và
/// <see cref="Vouchers.UpdateVoucherStatusRequest.Status"/> (mục D3 của docs/03-quy-uoc.md): để kiểu
/// enum thì JSON gửi lên mã sai bị model binding từ chối TRƯỚC khi vào tới service, và thông báo lỗi
/// của framework không theo cấu trúc <c>{ message, errors }</c> thống nhất của dự án.
/// </summary>
public class UpdateTicketRequestStatusRequest
{
    /// <summary>
    /// Quyết định của quản lý: <c>"Approved"</c> hoặc <c>"Rejected"</c>.
    ///
    /// 🔴 Chỉ HAI giá trị này. <c>"Pending"</c> là trạng thái khách gửi, không phải một quyết định —
    /// nhận nó là mở đường cho một lượt gọi "duyệt" ghi ngược yêu cầu về hàng chờ. Giá trị khác
    /// (kể cả chuỗi số) → 400 kèm <c>errors.status</c>.
    /// </summary>
    [Required(ErrorMessage = "Trạng thái không được để trống")]
    [StringLength(20, ErrorMessage = "Mã trạng thái tối đa 20 ký tự")]
    public string? Status { get; set; }

    /// <summary>
    /// Lý do từ chối do quản lý nhập. Chỉ có nghĩa khi <c>status = "Rejected"</c>; bỏ trống vẫn hợp lệ
    /// — thông báo gửi khách ghi rõ *"Người duyệt không nêu lý do."* thay vì để cụt.
    ///
    /// Gửi kèm khi <c>"Approved"</c> KHÔNG phải lỗi 400: service bỏ qua và không lưu (cột
    /// <c>RejectReason</c> chỉ có nghĩa ở nhánh từ chối, lưu rồi để màn hình hiện "lý do từ chối"
    /// trên một yêu cầu đã duyệt là dữ liệu sai). Lý do đầy đủ: docs/api-contract.md.
    /// </summary>
    [StringLength(2000, ErrorMessage = "Lý do từ chối tối đa 2000 ký tự")]
    public string? RejectReason { get; set; }
}
