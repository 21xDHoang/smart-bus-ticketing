namespace SmartBus.Api.Dtos.Reports;

/// <summary>
/// Phản hồi của GET /api/reports/revenue/by-bus — doanh thu theo xe (US 19 "Doanh thu", Sprint 4
/// — Nguyễn Duy Kiên).
///
/// "Theo xe" nghĩa là gộp theo xe đã chạy chuyến được trả tiền (<c>Trips.BusId</c>), không phải
/// theo xe chở khách lúc soát vé. Chỉ xe CÓ ít nhất một giao dịch <c>Success</c> trong kỳ mới
/// thành dòng. Thứ tự: doanh thu giảm dần, bằng nhau thì biển số tăng dần (Ordinal).
/// </summary>
public class RevenueByBusResponse : RevenueBreakdownResponse
{
    /// <summary>Doanh thu từng xe trong kỳ, đã sắp xếp cố định.</summary>
    public required IReadOnlyList<RevenueBusRowResponse> Buses { get; set; }
}

/// <summary>Một dòng doanh thu của một xe.</summary>
public class RevenueBusRowResponse
{
    /// <summary>Biển số xe — "29B-123.45".</summary>
    public string LicensePlate { get; set; } = string.Empty;

    /// <summary>Loại xe — "Xe buýt 45 chỗ", "Xe buýt điện".</summary>
    public string BusType { get; set; } = string.Empty;

    /// <summary>Khoá xe — dùng để nối với danh mục xe ở FE.</summary>
    public Guid BusId { get; set; }

    /// <summary>Doanh thu (VND) — chỉ giao dịch Success của các chuyến do xe này chạy.</summary>
    public decimal Revenue { get; set; }

    /// <summary>Số giao dịch Success của xe.</summary>
    public int Payments { get; set; }
}
