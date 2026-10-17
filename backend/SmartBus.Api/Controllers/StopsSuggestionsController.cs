using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using SmartBus.Api.Dtos.Stops;
using SmartBus.Api.Services;

namespace SmartBus.Api.Controllers;

/// <summary>
/// Gợi ý trạm cho ô điểm đi/điểm đến của màn tra cứu tuyến (GET /api/stops/suggestions).
/// Hợp đồng đầy đủ ở mục "Gợi ý trạm cho tra cứu tuyến — /stops/suggestions" của
/// docs/api-contract.md.
///
/// ⚠️ KHÔNG gắn [Authorize]: đây là API CÔNG KHAI — hành khách chọn trạm trước khi đăng nhập,
/// cùng nhóm với /stops/search, /routes/search và /trips/search. Phân quyền của dự án là opt-in
/// (không có fallback policy), nên vắng [Authorize] nghĩa là ai cũng gọi được.
///
/// Đứng ở controller riêng cùng bề mặt api/stops: mỗi bề mặt một controller, cùng lối cặp
/// StopsController / StopsSearchController. Đoạn literal "suggestions" không thể khớp
/// {id:guid} của StopsController nên không tranh chấp route.
/// </summary>
[ApiController]
[Route("api/stops")]
public class StopsSuggestionsController : ControllerBase
{
    private readonly IStopSuggestionService _stopSuggestionService;

    public StopsSuggestionsController(IStopSuggestionService stopSuggestionService)
        => _stopSuggestionService = stopSuggestionService;

    /// <summary>
    /// Trạm thuộc tuyến Active, khớp từ khoá (tên hoặc địa chỉ, không phân biệt hoa thường và
    /// không phân biệt dấu), tối đa <c>limit</c> dòng — mặc định 10. Có <c>originStopId</c> thì
    /// chỉ gợi ý trạm sau điểm đi; có <c>destinationStopId</c> thì chỉ gợi ý trạm trước điểm đến.
    /// </summary>
    [HttpGet("suggestions")]
    public async Task<IActionResult> Suggestions(
        [FromQuery] StopSuggestionRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationError();
        }

        var result = await _stopSuggestionService.SearchAsync(request, cancellationToken);

        return result.Success ? Ok(result.Data) : Failure(result);
    }

    private IActionResult Failure<T>(ServiceResult<T> result) => result.ErrorKind switch
    {
        ServiceErrorKind.NotFound => NotFound(new { message = result.Error }),
        ServiceErrorKind.Invalid => BadRequest(new { message = result.Error, errors = result.Errors }),
        _ => Conflict(new { message = result.Error, errors = result.Errors }),
    };

    // Cố ý chép lại thay vì tách lớp dùng chung — cùng lý do đã ghi ở StopsSearchController:
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
