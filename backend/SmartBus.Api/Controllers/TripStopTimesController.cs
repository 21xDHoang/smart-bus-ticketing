using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartBus.Api.Dtos.Trips;
using SmartBus.Api.Services;

namespace SmartBus.Api.Controllers;

/// <summary>
/// Giờ đến TỪNG TRẠM của một chuyến — hợp đồng ở mục
/// "Giờ đến từng trạm — /routes/{routeId}/trips/{id}/stop-times" của docs/api-contract.md.
///
/// <para>
/// Tách khỏi <c>RouteTripsController</c> (file của Trần Trung Hiếu) để không sửa file của người
/// khác; cùng một tầng đường dẫn lồng dưới tuyến nên bề mặt API vẫn liền mạch:
/// <c>/api/routes/{routeId}/trips/{id}/stop-times</c>.
/// </para>
///
/// <para>
/// GET trả dòng thời gian đầy đủ của chuyến (giờ dự kiến suy ra từ km + mốc đã ghi đè).
/// PUT ghi đè giờ các trạm TRUNG GIAN, thay thế toàn phần — gửi mảng rỗng để xoá hết mốc ghi đè.
/// Trạm đầu và bến cuối là mốc neo của chuyến, sửa ở giờ chạy chứ không sửa ở đây.
/// </para>
/// </summary>
[ApiController]
[Route("api/routes/{routeId:guid}/trips/{id:guid}/stop-times")]
[Authorize(Policy = RbacPolicies.ManagerOrAbove)]
public class TripStopTimesController : ControllerBase
{
    private readonly ITripStopTimeService _stopTimeService;

    public TripStopTimesController(ITripStopTimeService stopTimeService)
        => _stopTimeService = stopTimeService;

    /// <summary>Dòng thời gian đầy đủ của chuyến — mọi trạm, giờ đến đang có hiệu lực.</summary>
    [HttpGet]
    public async Task<IActionResult> Get(Guid routeId, Guid id, CancellationToken cancellationToken)
    {
        var result = await _stopTimeService.GetAsync(routeId, id, cancellationToken);

        return result.Success ? Ok(result.Data) : Failure(result);
    }

    /// <summary>
    /// Ghi đè giờ các trạm trung gian của chuyến. Body <c>{ "items": [] }</c> xoá hết mốc ghi đè.
    /// Chuyến đã huỷ / đã chạy xong → 409.
    /// </summary>
    [HttpPut]
    public async Task<IActionResult> Update(
        Guid routeId,
        Guid id,
        [FromBody] UpdateTripStopTimesRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationError();
        }

        var result = await _stopTimeService.UpdateAsync(routeId, id, request, cancellationToken);

        return result.Success ? Ok(result.Data) : Failure(result);
    }

    private IActionResult Failure<T>(ServiceResult<T> result) => result.ErrorKind switch
    {
        ServiceErrorKind.NotFound => NotFound(new { message = result.Error }),
        ServiceErrorKind.Invalid => BadRequest(new { message = result.Error, errors = result.Errors }),
        _ => Conflict(new { message = result.Error, errors = result.Errors }),
    };

    // Cố ý chép lại từ RouteTripsController thay vì tách lớp dùng chung — cùng lý do đã ghi ở
    // đó: tách ra thì phải sửa controller của người khác.
    private IActionResult ValidationError() => BadRequest(new
    {
        message = "Dữ liệu đầu vào không hợp lệ",
        errors = ModelState.Where(e => e.Value?.Errors.Count > 0)
            .ToDictionary(
                entry => JsonNamingPolicy.CamelCase.ConvertName(entry.Key),
                entry => entry.Value!.Errors.Select(e => e.ErrorMessage).ToArray()),
    });
}
