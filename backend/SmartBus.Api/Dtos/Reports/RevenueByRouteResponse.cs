namespace SmartBus.Api.Dtos.Reports;

/// <summary>
/// Phản hồi của GET /api/reports/revenue/by-route — doanh thu theo tuyến (US 19 "Doanh thu",
/// Sprint 4 — Nguyễn Duy Kiên).
///
/// <see cref="Routes"/> chỉ chứa tuyến CÓ ít nhất một giao dịch <c>Success</c> trong kỳ; tuyến
/// không bán được vé không hiện thành dòng 0. Thứ tự: doanh thu giảm dần, bằng nhau thì mã tuyến
/// tăng dần (Ordinal) — FE không phải tự sắp.
/// </summary>
public class RevenueByRouteResponse : RevenueBreakdownResponse
{
    /// <summary>Doanh thu từng tuyến trong kỳ, đã sắp xếp cố định.</summary>
    public required IReadOnlyList<RevenueRouteRowResponse> Routes { get; set; }
}

/// <summary>Một dòng doanh thu của một tuyến.</summary>
public class RevenueRouteRowResponse
{
    /// <summary>Mã tuyến hiển thị cho hành khách — "01", "B10"…</summary>
    public string RouteCode { get; set; } = string.Empty;

    /// <summary>Tên tuyến, ví dụ "Bến Thành — Chợ Lớn".</summary>
    public string RouteName { get; set; } = string.Empty;

    /// <summary>Khoá tuyến — dùng để nối với danh mục tuyến ở FE.</summary>
    public Guid RouteId { get; set; }

    /// <summary>Doanh thu (VND) — chỉ giao dịch Success của các chuyến thuộc tuyến này.</summary>
    public decimal Revenue { get; set; }

    /// <summary>Số giao dịch Success của tuyến.</summary>
    public int Payments { get; set; }
}
