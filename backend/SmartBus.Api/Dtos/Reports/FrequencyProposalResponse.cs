namespace SmartBus.Api.Dtos.Reports;

/// <summary>
/// Hình dạng trả về của GET /api/reports/frequency-proposals — đề xuất tăng/giảm tần suất chạy xe
/// theo dữ liệu (US 20 "Tỷ lệ lấp đầy", Sprint 4 — Phùng Duy Hoàng). Hợp đồng đầy đủ ở mục "Đề
/// xuất tần suất chạy xe — /reports/frequency-proposals" của docs/api-contract.md.
///
/// Đây là lớp ĐỀ XUẤT trên nền báo cáo khung giờ cao điểm: bốn số gốc của mỗi giờ (khách, chuyến,
/// mật độ, cao điểm) được lặp lại y nguyên, kèm số chuyến nên chạy và giãn cách tương ứng. Không
/// có ghi dữ liệu — quản lý xem rồi tự quyết định.
/// </summary>
public class FrequencyProposalResponse
{
    /// <summary>Ngày bắt đầu kỳ (lịch Việt Nam) — lặp lại tham số sau khi áp dụng mặc định.</summary>
    public DateOnly From { get; set; }

    /// <summary>Ngày kết thúc kỳ (lịch Việt Nam), TÍNH CẢ.</summary>
    public DateOnly To { get; set; }

    /// <summary>Số ngày lịch trong kỳ, tính cả hai đầu — mẫu số quy đổi chuyến/kỳ sang chuyến/ngày.</summary>
    public int PeriodDays { get; set; }

    /// <summary>
    /// False khi kỳ không có chuyến, không có khách, hoặc mật độ trung bình làm tròn 2 chữ số bằng
    /// 0 — khi đó không đề xuất gì (các trường đề xuất của mọi giờ là null).
    /// </summary>
    public bool HasEnoughData { get; set; }

    /// <summary>Tổng lượt khách cả kỳ — cùng nền với báo cáo gốc (vé huỷ không tính).</summary>
    public int TotalPassengers { get; set; }

    /// <summary>Tổng chuyến khởi hành trong kỳ.</summary>
    public int TotalTrips { get; set; }

    /// <summary>
    /// Mật độ trung bình cả kỳ = TotalPassengers / TotalTrips, làm tròn 2 chữ số — cũng là MẬT ĐỘ
    /// MỤC TIÊU mà mọi giờ được kéo về.
    /// </summary>
    public double AveragePassengersPerTrip { get; set; }

    /// <summary>Giờ có <see cref="HourlyFrequencyProposalResponse.Action"/> = Increase, tăng dần.</summary>
    public required IReadOnlyList<int> IncreaseHours { get; set; }

    /// <summary>Giờ có <see cref="HourlyFrequencyProposalResponse.Action"/> = Decrease, tăng dần.</summary>
    public required IReadOnlyList<int> DecreaseHours { get; set; }

    /// <summary>Đủ 24 bucket liên tục từ 0 đến 23 giờ — cùng trục với báo cáo gốc.</summary>
    public required IReadOnlyList<HourlyFrequencyProposalResponse> Hours { get; set; }
}

/// <summary>Một giờ trong ngày (giờ Việt Nam) — số hiện tại, số đề xuất và hành động tương ứng.</summary>
public class HourlyFrequencyProposalResponse
{
    /// <summary>Giờ trong ngày, 0–23.</summary>
    public int Hour { get; set; }

    /// <summary>Tổng lượt khách của các chuyến khởi hành trong giờ này — số gốc, không tính lại.</summary>
    public int Passengers { get; set; }

    /// <summary>Số chuyến khác nhau khởi hành trong giờ này — số gốc, không tính lại.</summary>
    public int Trips { get; set; }

    /// <summary>Mật độ của giờ = Passengers / Trips, làm tròn 2 chữ số — số gốc, không tính lại.</summary>
    public double AveragePassengersPerTrip { get; set; }

    /// <summary>Giờ cao điểm theo báo cáo gốc — giờ cao điểm không bao giờ bị đề xuất giảm.</summary>
    public bool IsPeak { get; set; }

    /// <summary>Trips / PeriodDays, làm tròn 2 chữ số.</summary>
    public double CurrentTripsPerDay { get; set; }

    /// <summary>
    /// 60 / CurrentTripsPerDay, làm tròn 2 chữ số. Null khi chưa đạt 1 chuyến/ngày — không có giãn
    /// cách trong giờ để nói.
    /// </summary>
    public double? CurrentFrequencyMinutes { get; set; }

    /// <summary>
    /// Số chuyến đề xuất cho CẢ KỲ. Null khi NoService/NoData — hai trạng thái đó cố ý không đề
    /// xuất gì, khác với đề xuất 0 chuyến (giờ có chạy nhưng không có khách).
    /// </summary>
    public int? SuggestedTripsInPeriod { get; set; }

    /// <summary>SuggestedTripsInPeriod / PeriodDays, làm tròn 2 chữ số. Null khi không đề xuất.</summary>
    public double? SuggestedTripsPerDay { get; set; }

    /// <summary>
    /// 60 / SuggestedTripsPerDay, làm tròn 2 chữ số. Null khi không đề xuất hoặc dưới 1 chuyến/ngày.
    /// </summary>
    public double? SuggestedFrequencyMinutes { get; set; }

    /// <summary>
    /// (SuggestedTripsInPeriod − Trips) / Trips × 100, làm tròn 2 chữ số. 0 khi Keep; null khi
    /// NoService/NoData.
    /// </summary>
    public double? ChangePercent { get; set; }

    /// <summary>
    /// Một trong <see cref="FrequencyActionCodes"/>: Increase · Decrease · Keep · NoService ·
    /// NoData. Dùng chuỗi thay vì enum vì dự án không bật JsonStringEnumConverter — enum sẽ ra số
    /// và frontend không đọc được.
    /// </summary>
    public required string Action { get; set; }
}

/// <summary>
/// Năm giá trị của <see cref="HourlyFrequencyProposalResponse.Action"/> — hằng số thay cho enum để
/// JSON giữ nguyên chuỗi (xem lý do ở thuộc tính Action).
/// </summary>
public static class FrequencyActionCodes
{
    /// <summary>Cần nhiều chuyến hơn để hạ mật độ mỗi xe — chỉ dành cho giờ cao điểm.</summary>
    public const string Increase = "Increase";

    /// <summary>Cần ít chuyến hơn — giờ thưa khách hơn mật độ trung bình của kỳ.</summary>
    public const string Decrease = "Decrease";

    /// <summary>Số đề xuất bằng số hiện tại (kể cả sau khi làm tròn) — không cần đổi.</summary>
    public const string Keep = "Keep";

    /// <summary>Giờ không có chuyến — không có dữ liệu khách để nói nên tăng hay giảm.</summary>
    public const string NoService = "NoService";

    /// <summary>Có chuyến nhưng cả kỳ không đủ dữ liệu để tính mật độ mục tiêu.</summary>
    public const string NoData = "NoData";
}
