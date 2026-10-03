using SmartBus.Api.Dtos.Feedbacks;

namespace SmartBus.Api.Services;

/// <summary>
/// Thống kê phản ánh theo loại và theo tuyến — task *"API thống kê phản ánh theo loại và theo tuyến"*
/// (US 24, Sprint 2, Nguyễn Duy Kiên). Hợp đồng đầy đủ ở mục "Phản ánh — /feedbacks", phần "Thống kê
/// phản ánh" của docs/api-contract.md.
///
/// Cố ý đứng riêng thay vì nối vào <see cref="IFeedbackAdminService"/> hay
/// <see cref="IFeedbackLookupService"/>: ba bên trả lời ba câu hỏi khác nhau (xử lý một phản ánh /
/// phản ánh của tôi / số liệu tổng hợp), cùng lối các cặp service đã tách từ Sprint 1. Đây cũng là
/// lý do nó KHÔNG nhận tham số lọc nào — xem chú thích ở <see cref="GetAsync"/>.
///
/// Chỉ đọc và không có tham số, nên không có nhánh lỗi nào: đúng quyền là luôn có kết quả, kể cả khi
/// hệ thống chưa có phản ánh nào (mọi con đếm bằng 0) — vì thế không dùng <c>ServiceResult</c>.
/// </summary>
public interface IFeedbackStatisticsService
{
    /// <summary>
    /// Hai bảng đếm của toàn bộ phản ánh: theo loại (luôn đủ ba dòng, thứ tự cố định
    /// Complaint → Compliment → Suggestion) và theo tuyến (chỉ tuyến đã có phản ánh, số lượng giảm
    /// dần, trùng số thì theo mã tuyến tăng dần).
    ///
    /// KHÔNG có tham số lọc theo thời gian/trạng thái và không phân trang: task chỉ hỏi "theo loại và
    /// theo tuyến", thêm tham số là đổi hình dạng API (⛔5 — sửa hợp đồng và báo nhóm trước).
    /// </summary>
    Task<FeedbackStatisticsResponse> GetAsync(CancellationToken cancellationToken = default);
}
