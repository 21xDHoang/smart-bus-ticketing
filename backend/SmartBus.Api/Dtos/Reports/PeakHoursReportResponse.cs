namespace SmartBus.Api.Dtos.Reports;

/// <summary>
/// Hình dạng trả về của GET /api/reports/peak-hours (US 20 "Tỷ lệ lấp đầy", Sprint 4 — Trần Trung
/// Hiếu). Hợp đồng: docs/api-contract.md mục "Khung giờ cao điểm — /reports/peak-hours".
///
/// Trục cố định 24 giờ (0–23) theo GIỜ VIỆT NAM — giờ không có chuyến vẫn hiện diện với số 0
/// (cùng lối "trục cố định, thiếu dữ liệu thì vẽ cột 0" của RevenueReportResponse). Giờ cao điểm
/// là giờ có chuyến khởi hành và mật độ (khách trung bình mỗi chuyến của giờ đó) ≥ mật độ trung
/// bình cả kỳ — so trên giá trị ĐÃ LÀM TRÒN 2 chữ số, đúng con số hiển thị trong phản hồi.
/// </summary>
public class PeakHoursReportResponse
{
    /// <summary>Ngày bắt đầu báo cáo (lịch Việt Nam) — lặp lại tham số sau khi áp dụng mặc định.</summary>
    public DateOnly From { get; set; }

    /// <summary>Ngày kết thúc báo cáo (lịch Việt Nam), TÍNH CẢ.</summary>
    public DateOnly To { get; set; }

    /// <summary>Tổng lượt khách cả kỳ — chỉ vé Paid/Used của các chuyến khởi hành trong kỳ.</summary>
    public int TotalPassengers { get; set; }

    /// <summary>Tổng chuyến khởi hành trong kỳ — mẫu số của mật độ.</summary>
    public int TotalTrips { get; set; }

    /// <summary>
    /// Mật độ trung bình cả kỳ = TotalPassengers / TotalTrips, làm tròn 2 chữ số — chính là
    /// ngưỡng đánh dấu cao điểm. 0 khi kỳ không có chuyến nào.
    /// </summary>
    public double AveragePassengersPerTrip { get; set; }

    /// <summary>Danh sách giờ (0–23, tăng dần) được đánh dấu cao điểm.</summary>
    public required IReadOnlyList<int> PeakHours { get; set; }

    /// <summary>Đủ 24 bucket liên tục từ 0 đến 23 giờ — trục biểu đồ cột theo khung giờ.</summary>
    public required IReadOnlyList<PeakHourBucketResponse> Hours { get; set; }
}

/// <summary>Một giờ trong ngày (giờ Việt Nam) — một cột của biểu đồ mật độ theo khung giờ.</summary>
public class PeakHourBucketResponse
{
    /// <summary>Giờ trong ngày, 0–23.</summary>
    public int Hour { get; set; }

    /// <summary>Tổng lượt khách của các chuyến khởi hành trong giờ này.</summary>
    public int Passengers { get; set; }

    /// <summary>Số chuyến KHÁC NHAU khởi hành trong giờ này.</summary>
    public int Trips { get; set; }

    /// <summary>Mật độ = Passengers / Trips, làm tròn 2 chữ số — 0 khi giờ không có chuyến.</summary>
    public double AveragePassengersPerTrip { get; set; }

    /// <summary>True khi giờ có chuyến, cả kỳ có khách, và mật độ ≥ mật độ trung bình cả kỳ.</summary>
    public bool IsPeak { get; set; }
}
