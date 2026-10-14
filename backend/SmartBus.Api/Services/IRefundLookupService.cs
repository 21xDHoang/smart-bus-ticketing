using SmartBus.Api.Dtos.Refunds;

namespace SmartBus.Api.Services;

/// <summary>
/// Tra cứu trạng thái hoàn tiền của chính người gọi — GET /api/refunds và
/// GET /api/refunds/{refundCode} (US 8, task *"API tra cứu trạng thái hoàn tiền của giao dịch"*,
/// dòng 42 Sprint 4 — Vàng Thị Dăm). Hợp đồng đầy đủ ở mục "Hoàn tiền — /refunds" của
/// docs/api-contract.md.
///
/// Giao diện CỐ Ý không có tham số nào để hỏi khoản hoàn của người khác: cả hai hàm đều nhận
/// <c>userId</c> của người đang đăng nhập và dùng nó làm điều kiện truy vấn. Nhóm quản trị tra khoản
/// hoàn của người khác bằng GET /admin/refunds — bề mặt khác, chưa làm (chưa có chủ trong bảng phân
/// công).
/// </summary>
public interface IRefundLookupService
{
    /// <summary>
    /// Khoản hoàn của một người, mới nhất trước — mảng trần, không phân trang. Không có khoản nào
    /// thì mảng rỗng, không phải lỗi.
    ///
    /// <paramref name="request"/> lọc theo <c>paymentCode</c> (trạng thái hoàn tiền của MỘT giao
    /// dịch — câu chữ của task dòng 42) và theo <c>status</c>. Giao dịch không phát sinh khoản hoàn
    /// nào — hoặc không tồn tại, hoặc là giao dịch của người khác — đều trả mảng RỖNG: *"không có
    /// khoản hoàn nào"* là một câu trả lời, không phải lỗi. Hệ quả: endpoint này không dùng được để
    /// dò xem một mã giao dịch có tồn tại hay không, và đó là chủ ý.
    /// </summary>
    Task<IReadOnlyList<RefundResponse>> ListMineAsync(
        Guid userId,
        ListMyRefundsRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Một khoản hoàn theo mã hoàn tiền, nhưng chỉ khi khoản đó thuộc <paramref name="userId"/> —
    /// không tồn tại và của người khác đều là <c>NotFound</c> (404) với cùng một câu, không để lộ
    /// khoản hoàn nào đang tồn tại.
    /// </summary>
    Task<ServiceResult<RefundResponse>> GetByRefundCodeAsync(
        Guid userId,
        string refundCode,
        CancellationToken cancellationToken = default);
}
