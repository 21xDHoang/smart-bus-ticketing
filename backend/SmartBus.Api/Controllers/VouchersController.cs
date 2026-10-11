using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartBus.Api.Dtos.Vouchers;
using SmartBus.Api.Services;

namespace SmartBus.Api.Controllers;

/// <summary>
/// CRUD voucher cho màn quản trị — GET/POST /api/vouchers, GET/PUT /api/vouchers/{id} và
/// PATCH /api/vouchers/{id}/status. Task *"API CRUD voucher: mã, loại giảm giá, điều kiện áp dụng,
/// số lượng"* (dòng 51, US 18, Sprint 3, Trần Trung Hiếu). Hợp đồng đầy đủ ở mục "Voucher — /vouchers",
/// phần "CRUD voucher" của docs/api-contract.md.
///
/// [Authorize(Policy = ManagerOrAbove)]: màn quản lý voucher là của nhân viên Marketing/quản lý —
/// cùng quyền VoucherStatisticsController, KHÁC hẳn VoucherValidationController ([Authorize] trần
/// cho hành khách tự kiểm mã của đơn mình).
///
/// Đứng ở controller riêng trên bề mặt <c>api/vouchers</c>: <c>validate</c> và <c>statistics</c> là
/// hai đoạn literal ở controller của Kiên, <c>{id}</c> là tham số nên không tranh chấp — mỗi task
/// một controller, cùng lối api/seat-holds và api/trips. 🔴 <c>{id:guid}</c> bắt buộc phải giữ: nới
/// thành <c>{id}</c> trần là <c>statistics</c> bị route chi tiết nuốt mất.
/// </summary>
[ApiController]
[Route("api/vouchers")]
[Authorize(Policy = RbacPolicies.ManagerOrAbove)]
public class VouchersController : ControllerBase
{
    private readonly IVoucherAdminService _voucherAdminService;

    public VouchersController(IVoucherAdminService voucherAdminService)
        => _voucherAdminService = voucherAdminService;

    /// <summary>
    /// Danh sách voucher — tìm theo mã/tên, lọc trạng thái, phân trang. Sắp createdAt giảm dần;
    /// <c>total</c> là tổng số dòng KHỚP BỘ LỌC để màn quản lý vẽ phân trang.
    ///
    /// Không có token → 401; thiếu quyền → 403; tham số lọc sai khuôn (trạng thái ngoài enum,
    /// page/pageSize ngoài khoảng) → 400 kèm <c>errors.&lt;field&gt;</c>.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(VoucherListResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> List(
        [FromQuery] ListVouchersRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationError();
        }

        var result = await _voucherAdminService.ListAsync(request, cancellationToken);

        return result.Success ? Ok(result.Data) : Failure(result);
    }

    /// <summary>
    /// Chi tiết một voucher. 🔴 Ràng buộc <c>{id:guid}</c> giữ cho <c>statistics</c> không bị nuốt
    /// (xem doc-comment của controller).
    ///
    /// Không tồn tại → 404; id không phải GUID → 404 do ràng buộc route.
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(VoucherResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var result = await _voucherAdminService.GetByIdAsync(id, cancellationToken);

        return result.Success ? Ok(result.Data) : Failure(result);
    }

    /// <summary>
    /// Tạo voucher mới. Mã chuẩn hoá CHỮ HOA; voucher mới luôn <c>Active</c>, <c>usedCount = 0</c>.
    ///
    /// Body sai khuôn → 400 kèm <c>errors.&lt;field&gt;</c>; trùng mã (sau chuẩn hoá) → 409; tuyến
    /// không tồn tại → 404 (lỗi phía gọi). Thành công → 201 kèm Location.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(VoucherResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(
        [FromBody] CreateVoucherRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationError();
        }

        var result = await _voucherAdminService.CreateAsync(request, cancellationToken);

        // 201 kèm Location trỏ tới GET chi tiết — cùng lối AdminUserController.Create.
        return result.Success
            ? CreatedAtAction(nameof(GetById), new { id = result.Data!.Id }, result.Data)
            : Failure(result);
    }

    /// <summary>
    /// Sửa voucher — cùng bảng luật của POST, thêm: trùng mã bỏ qua chính voucher đang sửa, và
    /// quantity phải ≥ usedCount. <c>usedCount</c>/<c>status</c> gửi lên bị bỏ qua.
    ///
    /// Không tồn tại → 404; trùng mã → 409; luật nghiệp vụ sai → 400 kèm <c>errors.&lt;field&gt;</c>.
    /// </summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(VoucherResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] UpdateVoucherRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationError();
        }

        var result = await _voucherAdminService.UpdateAsync(id, request, cancellationToken);

        return result.Success ? Ok(result.Data) : Failure(result);
    }

    /// <summary>
    /// Bật/tắt áp dụng — chỉ đổi cột Status. Đây là quyết định của người vận hành, KHÔNG phải trạng
    /// thái hiệu lực: voucher Active vẫn có thể chưa tới validFrom / đã quá validUntil / hết lượt,
    /// những thứ đó POST /vouchers/validate tự suy (xem VoucherStatus).
    ///
    /// Không tồn tại → 404; status ngoài enum → 400 kèm <c>errors.status</c>.
    /// </summary>
    [HttpPatch("{id:guid}/status")]
    [ProducesResponseType(typeof(VoucherResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateStatus(
        Guid id,
        [FromBody] UpdateVoucherStatusRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationError();
        }

        var result = await _voucherAdminService.UpdateStatusAsync(id, request, cancellationToken);

        return result.Success ? Ok(result.Data) : Failure(result);
    }

    /// <summary>
    /// Body sai khuôn — Program.cs đã tắt filter validate tự động của ASP.NET Core
    /// (SuppressModelStateInvalidFilter) để mọi lỗi đi qua đúng một khuôn { message, errors } của
    /// dự án, nên controller phải tự kiểm và tự dựng. Khoá lỗi đổi sang camelCase bằng
    /// JsonNamingPolicy vì tên trường trong ModelState là tên C# ("DiscountType"), còn frontend gắn
    /// lỗi theo tên trường trong JSON ("discountType") — cùng lối VoucherValidationController.
    /// </summary>
    private IActionResult ValidationError() => BadRequest(new
    {
        message = "Dữ liệu đầu vào không hợp lệ",
        errors = ModelState.Where(e => e.Value?.Errors.Count > 0)
            .ToDictionary(
                entry => JsonNamingPolicy.CamelCase.ConvertName(entry.Key),
                entry => entry.Value!.Errors.Select(e => e.ErrorMessage).ToArray()),
    });

    // Cùng một hàm ở VoucherValidationController, SeatHoldLookupController, RoutesController,
    // FaresController và TripSearchController — chép lại cố ý: tách lớp dùng chung thì phải sửa cả
    // controller của người khác, mà luật nhóm không cho sửa file của người khác.
    private IActionResult Failure<T>(ServiceResult<T> result) => result.ErrorKind switch
    {
        ServiceErrorKind.NotFound => NotFound(new { message = result.Error }),
        ServiceErrorKind.Invalid => BadRequest(new { message = result.Error, errors = result.Errors }),
        _ => Conflict(new { message = result.Error, errors = result.Errors }),
    };
}
