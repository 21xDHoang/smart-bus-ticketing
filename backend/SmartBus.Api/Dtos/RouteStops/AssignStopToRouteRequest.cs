using System.ComponentModel.DataAnnotations;

namespace SmartBus.Api.Dtos.RouteStops;

/// <summary>
/// Body của POST /api/routes/{routeId}/stops — gán một trạm vào tuyến.
///
/// Trạm mới luôn nối vào CUỐI tuyến. Muốn chèn vào giữa thì gán xong rồi gọi
/// PUT /routes/{routeId}/stops/order — nhờ vậy server chỉ có một đường duy nhất để
/// sinh ra thứ tự, không phải lo hai trạm cùng chèn vào một chỗ.
///
/// Hợp đồng: docs/api-contract.md mục "Trạm trên tuyến — /routes/{routeId}/stops".
/// </summary>
public class AssignStopToRouteRequest
{
    /// <summary>
    /// Trạm cần gán. Để <c>Guid?</c> chứ không phải <c>Guid</c>: kiểu không nullable thì
    /// body thiếu hẳn trường này sẽ nhận giá trị mặc định <c>Guid.Empty</c> và <c>[Required]</c>
    /// không bắt được (nó chỉ bắt null), lỗi sẽ trôi xuống tận tầng dữ liệu thành 404 khó hiểu.
    /// </summary>
    [Required(ErrorMessage = "Trạm không được để trống")]
    public Guid? StopId { get; set; }

    /// <summary>
    /// Khoảng cách từ trạm liền trước tới trạm này (km). Bỏ trống = 0.
    ///
    /// Cố ý KHÔNG gắn [Range]: trần của cột numeric(6,2) là 9999.99 mà [Range] chỉ nhận biên
    /// dạng chuỗi, dễ lệch với hằng số trong service — cùng lý do FareService không gắn [Range]
    /// cho giá tiền. Kiểm tra ở RouteStopService.
    /// </summary>
    public decimal DistanceKm { get; set; }
}
