namespace SmartBus.Api.Dtos.Reports;

/// <summary>
/// Phản hồi của GET /api/reports/fill-rate — tỷ lệ lấp đầy theo chuyến / tuyến / khung giờ
/// (US 20 "Tỷ lệ lấp đầy", Sprint 4 — Nguyễn Duy Kiên).
///
/// Một lượt gọi trả ba cách nhìn trên CÙNG một tập chuyến: <see cref="Trips"/> (từng chuyến),
/// <see cref="Routes"/> (gộp tuyến), <see cref="TimeSlots"/> (gộp khung giờ khởi hành) — cộng
/// <see cref="Summary"/> cho dải thẻ số liệu. Đẳng thức kiểm chéo: cộng các dòng của
/// <see cref="Routes"/> hoặc <see cref="TimeSlots"/> đều ra đúng <see cref="Summary"/>.
/// Hợp đồng: docs/api-contract.md mục "Tỷ lệ lấp đầy — /reports/fill-rate".
/// </summary>
public class FillRateReportResponse
{
    /// <summary>Ngày bắt đầu báo cáo (lịch Việt Nam) — lặp lại tham số sau khi áp dụng mặc định.</summary>
    public DateOnly From { get; set; }

    /// <summary>Ngày kết thúc báo cáo (lịch Việt Nam), TÍNH CẢ.</summary>
    public DateOnly To { get; set; }

    /// <summary>Dải số liệu tóm tắt của cả kỳ sau khi lọc.</summary>
    public required FillRateSummaryResponse Summary { get; set; }

    /// <summary>Mọi chuyến trong kỳ sau lọc, sắp theo giờ khởi hành tăng dần.</summary>
    public required IReadOnlyList<TripFillRateResponse> Trips { get; set; }

    /// <summary>Gộp theo tuyến, sắp theo mã tuyến tăng dần (Ordinal).</summary>
    public required IReadOnlyList<FillRateRouteRowResponse> Routes { get; set; }

    /// <summary>Đủ năm khung giờ theo thứ tự cố định; khung rỗng vẫn hiện với số 0.</summary>
    public required IReadOnlyList<FillRateTimeSlotRowResponse> TimeSlots { get; set; }
}

/// <summary>
/// Dải số liệu tóm tắt cho các thẻ đầu màn hình. Trung bình là **có trọng số theo sức chứa**
/// (<c>totalPassengers / totalSeats</c>), không phải trung bình cộng tỷ lệ từng chuyến.
/// </summary>
public class FillRateSummaryResponse
{
    /// <summary>Số chuyến khởi hành trong kỳ sau lọc — kể cả chuyến 0 khách.</summary>
    public int TotalTrips { get; set; }

    /// <summary>Tổng vé <c>Paid</c> + <c>Used</c> của các chuyến đó (vé <c>Cancelled</c> bỏ).</summary>
    public int TotalPassengers { get; set; }

    /// <summary>Tổng sức chứa ghế của các xe chạy các chuyến đó — mẫu số của tỷ lệ.</summary>
    public int TotalSeats { get; set; }

    /// <summary>
    /// <c>totalPassengers / totalSeats × 100</c>, làm tròn 1 chữ số thập phân; 0 khi
    /// <see cref="TotalSeats"/> = 0.
    /// </summary>
    public decimal AverageFillRatePct { get; set; }

    /// <summary>Số chuyến "đầy" — tỷ lệ lấp đầy (đã làm tròn) ≥ 80%.</summary>
    public int FullTrips { get; set; }

    /// <summary>Số chuyến "trống" — tỷ lệ lấp đầy (đã làm tròn) &lt; 50%.</summary>
    public int EmptyTrips { get; set; }
}

/// <summary>Một chuyến trong bảng thống kê tỷ lệ lấp đầy.</summary>
public class TripFillRateResponse
{
    /// <summary>Khoá chuyến — dùng làm <c>rowKey</c> của bảng.</summary>
    public Guid TripId { get; set; }

    /// <summary>
    /// Mã hiển thị <c>C{mã tuyến}-{HHmm}</c>. CHỈ để hiển thị — <c>Trips</c> không có cột mã, nên
    /// mã này không duy nhất giữa các ngày; khoá thật vẫn là <see cref="TripId"/>.
    /// </summary>
    public string TripCode { get; set; } = string.Empty;

    /// <summary>Khoá tuyến — nối với danh mục tuyến ở FE.</summary>
    public Guid RouteId { get; set; }

    /// <summary>Mã tuyến hiển thị cho hành khách — "01", "B10"…</summary>
    public string RouteCode { get; set; } = string.Empty;

    /// <summary>Tên tuyến, ví dụ "Bến Thành — Chợ Lớn".</summary>
    public string RouteName { get; set; } = string.Empty;

    /// <summary>Khoá xe — nối với danh mục xe ở FE.</summary>
    public Guid BusId { get; set; }

    /// <summary>Biển số xe — "29B-123.45".</summary>
    public string BusPlate { get; set; } = string.Empty;

    /// <summary>Sức chứa ghế của xe — mẫu số của tỷ lệ lấp đầy.</summary>
    public int Capacity { get; set; }

    /// <summary>Số vé <c>Paid</c> + <c>Used</c> của chuyến (vé <c>Cancelled</c> bỏ).</summary>
    public int Passengers { get; set; }

    /// <summary>Giờ khởi hành dạng ISO 8601 kèm offset <c>+07:00</c>.</summary>
    public DateTimeOffset DepartureTime { get; set; }

    /// <summary>
    /// <c>passengers / capacity × 100</c>, làm tròn 1 chữ số thập phân; 0 khi
    /// <see cref="Capacity"/> ≤ 0. Không chặn ở 100 — dữ liệu bán vượt sức chứa phải lộ ra.
    /// </summary>
    public decimal FillRatePct { get; set; }
}

/// <summary>Một dòng gộp theo tuyến — cùng bộ số với <see cref="FillRateSummaryResponse"/>.</summary>
public class FillRateRouteRowResponse
{
    public Guid RouteId { get; set; }

    public string RouteCode { get; set; } = string.Empty;

    public string RouteName { get; set; } = string.Empty;

    /// <summary>Số chuyến của tuyến trong kỳ sau lọc.</summary>
    public int TotalTrips { get; set; }

    /// <summary>Tổng vé <c>Paid</c> + <c>Used</c> của tuyến.</summary>
    public int TotalPassengers { get; set; }

    /// <summary>Tổng sức chứa ghế của các chuyến thuộc tuyến.</summary>
    public int TotalSeats { get; set; }

    /// <summary><c>totalPassengers / totalSeats × 100</c>, làm tròn 1 chữ số; 0 khi không có ghế.</summary>
    public decimal AverageFillRatePct { get; set; }

    /// <summary>Số chuyến đầy (≥ 80%) của tuyến.</summary>
    public int FullTrips { get; set; }

    /// <summary>Số chuyến trống (&lt; 50%) của tuyến.</summary>
    public int EmptyTrips { get; set; }
}

/// <summary>Một dòng gộp theo khung giờ khởi hành.</summary>
public class FillRateTimeSlotRowResponse
{
    /// <summary>Mã khung — <c>early</c> / <c>morning</c> / <c>afternoon</c> / <c>evening</c> / <c>night</c>.</summary>
    public string TimeSlot { get; set; } = string.Empty;

    /// <summary>Giờ bắt đầu (tính) theo giờ Việt Nam.</summary>
    public int FromHour { get; set; }

    /// <summary>Giờ kết thúc (KHÔNG tính). Riêng <c>night</c> nhỏ hơn <see cref="FromHour"/> vì vắt qua nửa đêm.</summary>
    public int ToHour { get; set; }

    /// <summary>Số chuyến khởi hành trong khung.</summary>
    public int TotalTrips { get; set; }

    /// <summary>Tổng vé <c>Paid</c> + <c>Used</c> của các chuyến trong khung.</summary>
    public int TotalPassengers { get; set; }

    /// <summary>Tổng sức chứa ghế của các chuyến trong khung.</summary>
    public int TotalSeats { get; set; }

    /// <summary><c>totalPassengers / totalSeats × 100</c>, làm tròn 1 chữ số; 0 khi không có ghế.</summary>
    public decimal AverageFillRatePct { get; set; }

    /// <summary>Số chuyến đầy (≥ 80%) trong khung.</summary>
    public int FullTrips { get; set; }

    /// <summary>Số chuyến trống (&lt; 50%) trong khung.</summary>
    public int EmptyTrips { get; set; }
}
