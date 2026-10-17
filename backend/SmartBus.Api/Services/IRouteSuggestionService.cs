using SmartBus.Api.Dtos.Routes;

namespace SmartBus.Api.Services;

/// <summary>
/// Gợi ý tuyến cho màn tra cứu tuyến (GET /api/routes/suggestions). Hợp đồng đầy đủ ở mục
/// "Gợi ý tuyến cho ngày đã chọn — /routes/suggestions" của docs/api-contract.md.
///
/// Khác <see cref="IRouteSearchService"/> ở câu hỏi: endpoint kia lọc <b>theo ngày</b> nên ngày
/// không có chuyến trả mảng rỗng; endpoint này luôn trả tuyến nối được hai trạm và tách lịch
/// chạy ra thành các trường riêng (ngày gần nhất, chạy hằng ngày, lý do trống).
/// </summary>
public interface IRouteSuggestionService
{
    /// <summary>
    /// Tuyến <c>Active</c> nối đúng hai trạm (trạm đi trước trạm đến theo <c>stopOrder</c>),
    /// không phụ thuộc ngày, kèm lịch chạy trong cửa sổ <c>windowDays</c> ngày tính từ hôm nay
    /// (giờ Việt Nam).
    ///
    /// Hai đầu nhận id hoặc tên; id được ưu tiên. Thiếu cả hai ở một đầu → 400; hai đầu trùng
    /// nhau → 400; id/tên không giải được ra trạm → 404; <c>date</c> sai định dạng hoặc
    /// <c>windowDays</c> ngoài 1..14 → 400.
    /// </summary>
    Task<ServiceResult<RouteSuggestionsResponse>> SearchAsync(
        RouteSuggestionsRequest request,
        CancellationToken cancellationToken = default);
}
