using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace SmartBus.Tests;

/// <summary>
/// Endpoint chỉ tồn tại trong project test, phục vụ việc kiểm chứng middleware ghi nhật ký
/// (task story 23 — Vàng Thị Dăm). Được nạp vào app thật qua <c>AddApplicationPart</c> trong
/// <see cref="TestAppFactory"/>, cùng lối với <see cref="TestProtectedController"/> — nhờ vậy
/// API thật không phải mọc thêm endpoint chỉ để phục vụ test.
///
/// Cố ý KHÔNG gắn <c>[Authorize]</c>: middleware ghi nhật ký phải chạy được cho cả request chưa
/// xác thực (UserId để NULL), mà điều đó chỉ kiểm chứng được nếu endpoint không đòi đăng nhập.
///
/// Hai route trên cùng controller để kiểm chứng việc suy ra TÊN BẢNG từ mẫu route:
/// <c>/api/_test/audit/routes/{routeId}/widgets/{id}</c> tác động lên bảng Widgets chứ không phải Routes.
/// </summary>
[ApiController]
[Route("api/_test/audit/widgets")]
[Route("api/_test/audit/routes/{routeId:guid}/widgets")]
public class TestAuditController : ControllerBase
{
    /// <summary>
    /// Tạo mới — 201 kèm body có trường <c>id</c>, đúng như mọi endpoint POST của dự án.
    /// </summary>
    [HttpPost]
    public IActionResult Create()
    {
        // Cố ý không dùng CreatedAtAction: controller gắn hai route nên việc sinh URL theo tên
        // action sẽ mơ hồ. Middleware ghi nhật ký chỉ đọc status code và body, không dùng Location.
        return StatusCode(StatusCodes.Status201Created, new { id = Guid.NewGuid(), name = "Widget vừa tạo" });
    }

    /// <summary>
    /// Tạo mới nhưng trả về body KHÔNG có trường <c>id</c> — ca dự phòng của cột Target.
    /// Route riêng (<c>~/</c> ghi đè tiền tố của controller) vì tên bảng suy ra từ đoạn tĩnh cuối
    /// cùng của đường dẫn, nên đường dẫn phải đọc ra được tên bảng.
    /// </summary>
    [HttpPost("~/api/_test/audit/widgets-no-id")]
    public IActionResult CreateWithoutId()
        => StatusCode(StatusCodes.Status201Created, new { name = "Widget không có id trong body" });

    /// <summary>Tạo mới nhưng thất bại — dữ liệu chưa đổi nên không được ghi nhật ký.</summary>
    [HttpPost("that-bai")]
    public IActionResult CreateFailing()
        => BadRequest(new { message = "Dữ liệu đầu vào không hợp lệ" });

    /// <summary>
    /// Sửa cả nhóm con: đường dẫn lồng có tham số cha nhưng KHÔNG có <c>{id}</c>, đoạn cuối là tên
    /// hành động — đúng hình dạng của <c>PUT /api/routes/{routeId}/stops/order</c> thật. Tài nguyên
    /// bị tác động là Widgets; "order" chỉ là hành động, không phải tên bảng.
    /// </summary>
    [HttpPut("~/api/_test/audit/routes/{routeId:guid}/widgets/order")]
    public IActionResult ReorderGroup(Guid routeId) => Ok(new { routeId });

    [HttpPut("{id:guid}")]
    public IActionResult Update(Guid id) => Ok(new { id, name = "Widget đã sửa" });

    [HttpPatch("{id:guid}")]
    public IActionResult Patch(Guid id) => Ok(new { id, name = "Widget đã sửa một phần" });

    [HttpDelete("{id:guid}")]
    public IActionResult Delete(Guid id) => NoContent();

    [HttpGet("{id:guid}")]
    public IActionResult GetById(Guid id) => Ok(new { id, name = "Widget" });
}
