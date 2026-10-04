using SmartBus.Api.Dtos.Stops;

namespace SmartBus.Api.Services;

/// <summary>
/// Gợi ý trạm dừng cho hành khách (GET /api/stops/search). Hợp đồng đầy đủ ở mục
/// "Tra cứu trạm dừng — /stops/search" của docs/api-contract.md.
///
/// Phần việc này CHỈ ĐỌC: không có thao tác ghi nào, nên không có <c>SaveChangesAsync</c> và không
/// có <c>try/catch DbUpdateException</c> như các service CRUD khác — không có gì để xung đột.
///
/// Cố ý đứng riêng thay vì nối vào <see cref="IStopService"/> (CRUD trạm của quản lý,
/// <c>[Authorize(ManagerOrAbove)]</c>): endpoint này là API CÔNG KHAI, trả một lát cắt gợi ý cho
/// người chưa đăng nhập, còn <see cref="IStopService"/> trả TOÀN BỘ trạm cho quản lý — hai bề mặt
/// nghiệp vụ, hai đối tượng gọi, hai mức quyền. Cùng lối tách
/// <see cref="IRouteSearchService"/> khỏi <see cref="IRouteService"/>.
/// </summary>
public interface IStopSearchService
{
    /// <summary>
    /// Trạm khớp từ khoá, tối đa <c>request.Limit</c> dòng (mặc định 10).
    ///
    /// Khớp <b>tên trạm hoặc địa chỉ</b>, <b>không phân biệt hoa thường và không phân biệt dấu</b>
    /// ("cau giay" ra "Trạm Cầu Giấy"). Xếp hạng: tên bắt đầu bằng từ khoá → tên chứa từ khoá →
    /// chỉ địa chỉ chứa từ khoá; trong cùng hạng xếp theo tên (chuẩn <c>vi</c>). Từ khoá bỏ trống
    /// (hoặc toàn khoảng trắng) = đầu danh sách theo tên.
    ///
    /// Không trạm nào khớp → mảng rỗng, không phải 404 — bộ lọc mềm, cùng lối
    /// <see cref="IRouteSearchService.SearchAsync"/>. <c>limit</c> ngoài 1..50 chặn ở Controller
    /// bằng <c>[Range]</c> → 400 <c>errors.limit</c>.
    /// </summary>
    Task<ServiceResult<IReadOnlyList<StopResponse>>> SearchAsync(
        StopSearchRequest request,
        CancellationToken cancellationToken = default);
}
