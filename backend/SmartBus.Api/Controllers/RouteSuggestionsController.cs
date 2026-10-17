using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using SmartBus.Api.Dtos.Routes;
using SmartBus.Api.Services;

namespace SmartBus.Api.Controllers;

/// <summary>
/// Gợi ý tuyến cho màn tra cứu tuyến (GET /api/routes/suggestions). Hợp đồng đầy đủ ở mục
/// "Gợi ý tuyến cho ngày đã chọn — /routes/suggestions" của docs/api-contract.md.
///
/// ⚠️ KHÔNG gắn [Authorize]: đây là API CÔNG KHAI — tra cứu tuyến là việc trước khi đăng nhập,
/// cùng nhóm với /routes/search. Phân quyền của dự án là opt-in (không có fallback policy), nên
/// vắng [Authorize] nghĩa là ai cũng gọi được.
///
/// Khác <see cref="RouteSearchController"/> ở hình dạng response và ở chỗ ngày KHÔNG lọc tuyến:
/// tuyến nối được hai trạm luôn được trả về kèm lịch chạy và ngày gần nhất. Đứng ở controller
/// riêng cùng bề mặt api/routes — đoạn literal "suggestions" không thể khớp {id:guid} của
/// RoutesController nên không tranh chấp route.
/// </summary>
[ApiController]
[Route("api/routes")]
public class RouteSuggestionsController : ControllerBase
{
    private readonly IRouteSuggestionService _routeSuggestionService;

    public RouteSuggestionsController(IRouteSuggestionService routeSuggestionService)
        => _routeSuggestionService = routeSuggestionService;

    /// <summary>
    /// Tuyến Active nối điểm đi với điểm đến, kèm lịch chạy trong cửa sổ 7 ngày và mã lý do khi
    /// ngày đã chọn không có chuyến. Hai đầu nhận id hoặc tên; id được ưu tiên.
    /// </summary>
    [HttpGet("suggestions")]
    public async Task<IActionResult> Suggestions(
        [FromQuery] RouteSuggestionsRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationError();
        }

        var result = await _routeSuggestionService.SearchAsync(request, cancellationToken);

        return result.Success ? Ok(result.Data) : Failure(result);
    }

    private IActionResult Failure<T>(ServiceResult<T> result) => result.ErrorKind switch
    {
        ServiceErrorKind.NotFound => NotFound(new { message = result.Error }),
        ServiceErrorKind.Invalid => BadRequest(new { message = result.Error, errors = result.Errors }),
        _ => Conflict(new { message = result.Error, errors = result.Errors }),
    };

    // Cố ý chép lại thay vì tách lớp dùng chung — cùng lý do đã ghi ở RouteSearchController:
    // tách ra thì phải sửa controller của người khác.
    private IActionResult ValidationError() => BadRequest(new
    {
        message = "Dữ liệu đầu vào không hợp lệ",
        errors = ModelState.Where(e => e.Value?.Errors.Count > 0)
            .ToDictionary(
                entry => JsonNamingPolicy.CamelCase.ConvertName(entry.Key),
                entry => entry.Value!.Errors.Select(e => e.ErrorMessage).ToArray()),
    });
}
