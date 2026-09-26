using System.ComponentModel.DataAnnotations;

namespace SmartBus.Api.Dtos.RouteStops;

/// <summary>
/// Body của PUT /api/routes/{routeId}/stops/order — sắp xếp lại thứ tự trạm của tuyến.
///
/// Thay thế TOÀN PHẦN: mảng phải chứa đủ và đúng tập trạm hiện có của tuyến. Hợp đồng:
/// docs/api-contract.md mục "Trạm trên tuyến — /routes/{routeId}/stops".
/// </summary>
public class ReorderRouteStopsRequest
{
    /// <summary>
    /// Danh sách trạm theo thứ tự mới. Khởi tạo sẵn mảng rỗng để body <c>{}</c> không biến
    /// thành null rồi nổ NullReferenceException ở tầng service: Program.cs đã tắt filter
    /// validate tự động của ASP.NET Core (ServiceResult tự lo phần lỗi), nên không có gì
    /// chặn trước hộ.
    ///
    /// Client KHÔNG gửi stopOrder — server suy ra từ vị trí trong mảng (phần tử đầu = 1).
    /// Nhờ vậy không thể tồn tại hai trạm cùng thứ tự do client gửi lên.
    /// </summary>
    public List<ReorderRouteStopItem> Items { get; set; } = [];
}

/// <summary>Một trạm trong danh sách sắp xếp lại.</summary>
public class ReorderRouteStopItem
{
    /// <summary>Trạm cần xếp lại. Bắt buộc — xem chú thích ở AssignStopToRouteRequest.StopId.</summary>
    [Required(ErrorMessage = "Trạm không được để trống")]
    public Guid? StopId { get; set; }

    /// <summary>
    /// Khoảng cách từ trạm liền trước tới trạm này (km).
    /// Bỏ trống = GIỮ NGUYÊN giá trị đang có.
    ///
    /// Cố ý để nullable: thao tác này là "sắp xếp lại thứ tự", không phải "viết lại khoảng cách".
    /// Nếu bỏ trống mà hiểu là 0 thì một lần kéo-thả sẽ xoá sạch khoảng cách quản lý đã nhập —
    /// cùng lối UpdateRouteRequest.Status bỏ trống là giữ nguyên trạng thái. Muốn đặt lại về 0
    /// thì gửi thẳng <c>"distanceKm": 0</c>.
    /// </summary>
    public decimal? DistanceKm { get; set; }
}
