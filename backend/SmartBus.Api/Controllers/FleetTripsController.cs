using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartBus.Api.Dtos.Trips;
using SmartBus.Api.Services;

namespace SmartBus.Api.Controllers;

/// <summary>
/// Sinh chuyến hàng loạt cho NHIỀU xe — <c>POST /api/routes/{routeId}/trips/generate-fleet</c>
/// (nợ của đợt mở rộng đội xe 17/10/2026, Phùng Duy Hoàng).
///
/// <para>
/// Controller riêng thay vì thêm action vào <see cref="RouteTripsController"/>: file đó của Trần Trung
/// Hiếu (luật E1). Cùng tiền tố route vì đây vẫn là thao tác trên lịch chạy của một tuyến; hình dạng
/// request/response khác hẳn endpoint một xe nên tách service + controller là ranh giới tự nhiên.
/// </para>
///
/// <para>
/// Phân quyền và cách map lỗi chép đúng <see cref="RouteTripsController"/> để mọi endpoint lịch trình
/// trả cùng một hình dạng: 404 <c>{message}</c>, 400 <c>{message, errors}</c>, 409 <c>{message}</c>.
/// </para>
/// </summary>
[ApiController]
[Route("api/routes/{routeId:guid}/trips")]
[Authorize(Policy = RbacPolicies.ManagerOrAbove)]
public class FleetTripsController : ControllerBase
{
    private readonly IFleetTripGenerationService _fleetTripService;

    public FleetTripsController(IFleetTripGenerationService fleetTripService)
        => _fleetTripService = fleetTripService;

    /// <summary>
    /// Sinh các chuyến cách đều tần suất và chia vòng tròn cho các xe trong <c>busIds</c>.
    /// Hợp đồng đầy đủ ở docs/api-contract.md mục "POST /routes/{routeId}/trips/generate-fleet".
    /// </summary>
    [HttpPost("generate-fleet")]
    public async Task<IActionResult> GenerateFleet(
        Guid routeId,
        [FromBody] GenerateFleetTripsRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationError();
        }

        var result = await _fleetTripService.GenerateAsync(routeId, request, cancellationToken);

        return result.Success ? Ok(result.Data) : Failure(result);
    }

    private IActionResult Failure<T>(ServiceResult<T> result) => result.ErrorKind switch
    {
        ServiceErrorKind.NotFound => NotFound(new { message = result.Error }),
        ServiceErrorKind.Invalid => BadRequest(new { message = result.Error, errors = result.Errors }),
        _ => Conflict(new { message = result.Error, errors = result.Errors }),
    };

    /// <summary>
    /// Chép từ <see cref="RouteTripsController"/> (private ở đó, không dùng chung được nếu không sửa
    /// file của Hiếu). Tên field đưa về camelCase đúng như JSON client gửi lên.
    /// </summary>
    private IActionResult ValidationError() => BadRequest(new
    {
        message = "Dữ liệu đầu vào không hợp lệ",
        errors = ModelState.Where(e => e.Value?.Errors.Count > 0)
            .ToDictionary(
                entry => JsonNamingPolicy.CamelCase.ConvertName(entry.Key),
                entry => entry.Value!.Errors.Select(e => e.ErrorMessage).ToArray()),
    });
}
