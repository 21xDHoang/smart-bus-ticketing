namespace SmartBus.Api.Dtos.Buses;

/// <summary>
/// Một tuyến mà xe đang chạy hoặc sẽ chạy — phần tử của <see cref="BusResponse.AssignedRoutes"/>.
/// Khớp mục "Xe buýt — /buses" của docs/api-contract.md.
///
/// Hệ thống KHÔNG có bảng gán xe ↔ tuyến (quy ước A9 đóng băng danh sách bảng): quan hệ này suy
/// ra từ <c>Trips</c> (<c>BusId</c> + <c>RouteId</c> + <c>DepartureTime</c>), nên con số
/// <see cref="TripCount"/> là "số chuyến trong cửa sổ 7 ngày", không phải một phân công cố định.
/// </summary>
public class BusRouteAssignmentResponse
{
    public Guid RouteId { get; set; }

    /// <summary>Mã tuyến hiển thị cho hành khách — "01", "B10".</summary>
    public string RouteCode { get; set; } = string.Empty;

    /// <summary>Tên tuyến, ví dụ "Bến Thành — Chợ Lớn".</summary>
    public string RouteName { get; set; } = string.Empty;

    /// <summary>
    /// Số chuyến của xe trên tuyến trong cửa sổ 7 ngày — tính cả chuyến đang chạy.
    /// </summary>
    public int TripCount { get; set; }

    /// <summary>
    /// Giờ khởi hành gần nhất còn ở trạng thái <c>Scheduled</c> của tuyến. Null khi tuyến chỉ còn
    /// chuyến đang chạy — dùng để sắp xếp tuyến theo thứ tự sắp phục vụ.
    /// </summary>
    public DateTime? NextDepartureTime { get; set; }
}
