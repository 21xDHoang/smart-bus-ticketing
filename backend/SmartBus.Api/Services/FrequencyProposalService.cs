using SmartBus.Api.Dtos.Reports;

namespace SmartBus.Api.Services;

/// <summary>
/// Cài đặt <see cref="IFrequencyProposalService"/> — đề xuất tăng/giảm tần suất chạy xe theo dữ
/// liệu (US 20 "Tỷ lệ lấp đầy", Sprint 4, task *"API đề xuất tăng/giảm tần suất chạy xe theo dữ
/// liệu"* — Phùng Duy Hoàng). Hợp đồng đầy đủ ở mục "Đề xuất tần suất chạy xe —
/// /reports/frequency-proposals" của docs/api-contract.md.
///
/// Bốn chốt nghiệp vụ, đọc trước khi sửa:
///
/// 1. **Một nguồn số duy nhất.** Service gọi <see cref="IPeakHoursReportService.GetAsync"/> thay
///    vì tự truy vấn Tickets/Trips. Mọi quy tắc nền (vé Paid/Used, giờ Việt Nam UTC+7, mặc định 30
///    ngày, trần 366 ngày, kiểm tra tuyến) áp dụng y nguyên; bốn số gốc của mỗi giờ được lặp lại
///    đúng con số báo cáo gốc trả về.
/// 2. **Mật độ mục tiêu là trung bình cả kỳ.** Số chuyến đề xuất = round(khách của giờ / mật độ
///    mục tiêu): giờ đông hơn trung bình cần thêm chuyến để hạ tải mỗi xe, giờ thưa cần bớt. Không
///    dùng tỷ lệ ±% cố định vì tỷ lệ đó không đến từ dữ liệu.
/// 3. **Không mâu thuẫn với isPeak.** Giờ cao điểm chỉ được tăng hoặc giữ; giờ không cao điểm chỉ
///    được giảm hoặc giữ. Làm tròn có thể rơi đúng bằng hiện tại — khi đó là Keep.
/// 4. **Không đủ dữ liệu thì không đề xuất.** Kỳ chưa có chuyến, chưa có khách, hoặc mật độ trung
///    bình làm tròn bằng 0 → NoData/NoService, mọi trường đề xuất null. Dữ liệu mới hoặc seeder
///    rỗng không phải bằng chứng để cắt chuyến.
/// </summary>
public sealed class FrequencyProposalService : IFrequencyProposalService
{
    private const int FractionDigits = 2;

    private readonly IPeakHoursReportService _peakHoursReportService;

    public FrequencyProposalService(IPeakHoursReportService peakHoursReportService)
        => _peakHoursReportService = peakHoursReportService;

    public async Task<ServiceResult<FrequencyProposalResponse>> GetAsync(
        FrequencyProposalRequest request,
        CancellationToken cancellationToken)
    {
        // Giao nguyên tham số cho báo cáo gốc: nó tự áp mặc định, kiểm tra from/to + trần 366 ngày
        // + tuyến tồn tại, rồi trả lại from/to ĐÃ CHỐT cùng bốn số gốc của từng giờ.
        var peakHours = await _peakHoursReportService.GetAsync(
            new PeakHoursReportRequest
            {
                From = request.From,
                To = request.To,
                RouteId = request.RouteId,
            },
            cancellationToken);

        if (!peakHours.Success)
        {
            return Propagate(peakHours);
        }

        var data = peakHours.Data!;
        var periodDays = data.To.DayNumber - data.From.DayNumber + 1;
        var targetDensity = data.AveragePassengersPerTrip;

        // Kỳ chỉ đủ dữ liệu khi có chuyến, có khách, và mật độ mục tiêu KHÔNG làm tròn về 0 — nếu
        // về 0 thì phép chia bên dưới không có nghĩa (1 khách / 1000 chuyến = 0.00 khách/chuyến).
        var hasEnoughData = data.TotalTrips > 0
            && data.TotalPassengers > 0
            && Round(targetDensity) > 0d;

        var hours = new List<HourlyFrequencyProposalResponse>(24);
        var increaseHours = new List<int>();
        var decreaseHours = new List<int>();

        foreach (var bucket in data.Hours)
        {
            var hour = BuildHour(bucket, periodDays, targetDensity, hasEnoughData);
            hours.Add(hour);

            if (hour.Action == FrequencyActionCodes.Increase)
            {
                increaseHours.Add(hour.Hour);
            }
            else if (hour.Action == FrequencyActionCodes.Decrease)
            {
                decreaseHours.Add(hour.Hour);
            }
        }

        return ServiceResult<FrequencyProposalResponse>.Ok(new FrequencyProposalResponse
        {
            From = data.From,
            To = data.To,
            PeriodDays = periodDays,
            HasEnoughData = hasEnoughData,
            TotalPassengers = data.TotalPassengers,
            TotalTrips = data.TotalTrips,
            AveragePassengersPerTrip = targetDensity,
            IncreaseHours = increaseHours,
            DecreaseHours = decreaseHours,
            Hours = hours,
        });
    }

    /// <summary>
    /// Quy đổi một bucket giờ của báo cáo gốc thành đề xuất. Thuần tính toán, không truy vấn.
    /// </summary>
    private static HourlyFrequencyProposalResponse BuildHour(
        PeakHourBucketResponse bucket,
        int periodDays,
        double targetDensity,
        bool hasEnoughData)
    {
        var currentTripsPerDay = Round((double)bucket.Trips / periodDays);

        var hour = new HourlyFrequencyProposalResponse
        {
            Hour = bucket.Hour,
            Passengers = bucket.Passengers,
            Trips = bucket.Trips,
            AveragePassengersPerTrip = bucket.AveragePassengersPerTrip,
            IsPeak = bucket.IsPeak,
            CurrentTripsPerDay = currentTripsPerDay,
            CurrentFrequencyMinutes = FrequencyMinutes(currentTripsPerDay),
            Action = FrequencyActionCodes.NoService,
        };

        // Giờ không có chuyến: không có dữ liệu khách để nói nên tăng hay giảm. Cố ý không đề xuất
        // 0 chuyến — "không chạy" đã là hiện trạng, không phải đề xuất.
        if (bucket.Trips == 0)
        {
            return hour;
        }

        if (!hasEnoughData)
        {
            hour.Action = FrequencyActionCodes.NoData;
            return hour;
        }

        // Mật độ mục tiêu > 0 đã được bảo đảm bởi hasEnoughData. Khách bằng 0 thì đề xuất 0 chuyến
        // (tín hiệu cân nhắc bỏ khung giờ), không rơi vào phép chia.
        var raw = bucket.Passengers == 0
            ? 0
            : (int)Math.Round(bucket.Passengers / targetDensity, MidpointRounding.AwayFromZero);

        // Chốt 3: kẹp về phía đúng của isPeak để phép làm tròn không tạo ra đề xuất mâu thuẫn với
        // chính báo cáo gốc (ví dụ giờ thưa khách ra số chuyến cao hơn hiện tại).
        var suggested = bucket.IsPeak ? Math.Max(raw, bucket.Trips) : Math.Min(raw, bucket.Trips);

        var suggestedPerDay = Round((double)suggested / periodDays);

        hour.SuggestedTripsInPeriod = suggested;
        hour.SuggestedTripsPerDay = suggestedPerDay;
        hour.SuggestedFrequencyMinutes = FrequencyMinutes(suggestedPerDay);
        hour.ChangePercent = Round((suggested - bucket.Trips) * 100d / bucket.Trips);
        hour.Action = suggested > bucket.Trips
            ? FrequencyActionCodes.Increase
            : suggested < bucket.Trips
                ? FrequencyActionCodes.Decrease
                : FrequencyActionCodes.Keep;

        return hour;
    }

    /// <summary>Làm tròn 2 chữ số, AwayFromZero — cùng quy tắc với báo cáo gốc.</summary>
    private static double Round(double value)
        => Math.Round(value, FractionDigits, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Giãn cách (phút) từ số chuyến/ngày. Dưới 1 chuyến/ngày thì không có giãn cách trong giờ để
    /// nói — trả null thay vì một con số vô nghĩa (60 / 0.5 = 120 phút nằm ngoài giờ).
    /// </summary>
    private static double? FrequencyMinutes(double tripsPerDay)
        => tripsPerDay >= 1d ? Round(60d / tripsPerDay) : null;

    /// <summary>
    /// Chuyển lỗi của báo cáo gốc sang kiểu kết quả của service này — controller chỉ thấy một hình
    /// dạng lỗi, không cần biết lỗi đến từ đâu. Báo cáo gốc chỉ trả Invalid/NotFound.
    /// </summary>
    private static ServiceResult<FrequencyProposalResponse> Propagate(
        ServiceResult<PeakHoursReportResponse> source) => source.ErrorKind switch
        {
            ServiceErrorKind.NotFound => ServiceResult<FrequencyProposalResponse>.NotFound(source.Error!),
            ServiceErrorKind.Conflict => ServiceResult<FrequencyProposalResponse>.Conflict(source.Error!),
            _ => ServiceResult<FrequencyProposalResponse>.Invalid(
                source.Error ?? "Dữ liệu đầu vào không hợp lệ",
                source.Errors ?? new Dictionary<string, string[]>()),
        };
}
