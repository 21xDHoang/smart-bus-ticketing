using SmartBus.Api.Dtos.Stops;

namespace SmartBus.Api.Services;

/// <summary>
/// Gợi ý trạm cho ô điểm đi/điểm đến của màn tra cứu tuyến (GET /api/stops/suggestions).
/// Hợp đồng đầy đủ ở mục "Gợi ý trạm cho tra cứu tuyến — /stops/suggestions" của
/// docs/api-contract.md.
///
/// Đứng riêng thay vì nối vào <see cref="IStopSearchService"/>: endpoint kia là ô chọn trạm dùng
/// chung và trả MỌI trạm trong CSDL; endpoint này chỉ trả trạm thuộc tuyến đang khai thác và lọc
/// được theo thứ tự trạm. Gộp hai bề mặt vào một endpoint thì hình dạng response phải đổi theo
/// ngữ cảnh gọi — trái lối "mỗi bề mặt một controller" của dự án.
/// </summary>
public interface IStopSuggestionService
{
    /// <summary>
    /// Trạm thuộc ít nhất một tuyến <c>Active</c>, khớp từ khoá (tên hoặc địa chỉ, không dấu,
    /// không hoa thường), tối đa <c>request.Limit</c> dòng.
    ///
    /// Có <c>originStopId</c> thì chỉ trả trạm đứng sau điểm đi trên cùng tuyến; có
    /// <c>destinationStopId</c> thì chỉ trả trạm đứng trước điểm đến; có cả hai thì phải thoả cả
    /// hai. Không có trạm nào khớp → mảng rỗng, không phải 404 — bộ lọc mềm.
    /// </summary>
    Task<ServiceResult<IReadOnlyList<StopSuggestionResponse>>> SearchAsync(
        StopSuggestionRequest request,
        CancellationToken cancellationToken = default);
}
