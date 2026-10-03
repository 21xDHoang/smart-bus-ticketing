using SmartBus.Api.Dtos.Feedbacks;

namespace SmartBus.Api.Services;

/// <summary>
/// Tra cứu phản ánh của CHÍNH người gọi — task *"API danh sách phản ánh của hành khách + theo dõi
/// trạng thái xử lý"* (US 24, Sprint 2, Nguyễn Duy Kiên). Hợp đồng đầy đủ ở mục "Phản ánh —
/// /feedbacks", phần "Phản ánh của tôi" của docs/api-contract.md.
///
/// Cố ý đứng riêng thay vì nối vào <see cref="IFeedbackAdminService"/>: một bên là nghiệp vụ vận
/// hành (mọi quản lý xử lý mọi phản ánh, sau policy ManagerOrAbove), một bên là hành khách xem dữ
/// liệu của chính mình — cùng lối các cặp service đã tách từ Sprint 1 (IStopService /
/// IRouteStopService, IMonthlyPassRenewalService / IMonthlyPassLookupService).
///
/// KHÔNG có tham số nào để chọn hành khách: <c>userId</c> là tham số ĐẦU TIÊN của mọi hàm, controller
/// đọc nó từ JWT, và phép lọc theo nó nằm ngay trong truy vấn — phản ánh của người khác không có
/// đường lọt vào kết quả. Đó cũng là lý do không có nhánh 403 ở đây: ca "của người khác" rơi vào
/// 404 giống hệt ca "không tồn tại" (hợp đồng nói rõ vì sao).
/// </summary>
public interface IFeedbackLookupService
{
    /// <summary>
    /// Phản ánh của <paramref name="userId"/>, mới nhất trước (createdAt giảm dần, phụ id cho ổn
    /// định), KHÔNG phân trang — mảng trần, cùng lối <c>GET /monthly-passes/me</c>.
    ///
    /// <c>status</c> là mã: mã lạ trả mảng RỖNG chứ không phải lỗi (cùng lối
    /// <c>GET /routes?status=</c> — mã lạ có thể là giá trị hợp lệ trong tương lai). Người chưa gửi
    /// phản ánh nào cũng là mảng rỗng, nên không có nhánh ServiceResult.
    /// </summary>
    Task<IReadOnlyList<MyFeedbackListItemResponse>> ListMineAsync(
        Guid userId,
        ListMyFeedbacksRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Chi tiết một phản ánh của <paramref name="userId"/> kèm toàn bộ luồng phản hồi (cũ → mới).
    ///
    /// Phản ánh không tồn tại HOẶC thuộc hành khách khác → 404 với cùng một câu
    /// "Không tìm thấy phản ánh": phân biệt hai ca là xác nhận với người đang dò rằng id đó có thật
    /// trên hệ thống.
    /// </summary>
    Task<ServiceResult<MyFeedbackResponse>> GetMineAsync(
        Guid userId,
        Guid id,
        CancellationToken cancellationToken = default);
}
