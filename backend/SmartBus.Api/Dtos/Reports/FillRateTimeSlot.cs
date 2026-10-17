namespace SmartBus.Api.Dtos.Reports;

/// <summary>
/// Khung giờ khởi hành của báo cáo tỷ lệ lấp đầy (US 20 "Tỷ lệ lấp đầy", Sprint 4 — Nguyễn Duy
/// Kiên). Năm khung phủ trọn 24 giờ; <c>night</c> là khung duy nhất vắt qua nửa đêm.
///
/// Bốn khung đầu trùng đúng lựa chọn mà màn hình "Thống kê tỷ lệ lấp đầy theo chuyến" đang dùng
/// (<c>frontend/src/api/fillRateReportApi.ts</c>); <c>night</c> bổ sung để mọi chuyến đều thuộc
/// đúng một khung — nhờ vậy tổng của dải <c>timeSlots[]</c> luôn bằng dải tóm tắt.
/// </summary>
public enum FillRateTimeSlot
{
    /// <summary>05:00–08:59 giờ Việt Nam.</summary>
    Early,

    /// <summary>09:00–12:59 giờ Việt Nam.</summary>
    Morning,

    /// <summary>13:00–16:59 giờ Việt Nam.</summary>
    Afternoon,

    /// <summary>17:00–21:59 giờ Việt Nam.</summary>
    Evening,

    /// <summary>22:00–04:59 giờ Việt Nam — vắt qua nửa đêm.</summary>
    Night,
}

/// <summary>
/// Đọc/ghi mã khung giờ dạng chữ thường (<c>early</c>, <c>morning</c>, …) — cùng lối
/// <see cref="RevenueGranularityCodes"/>: so với danh sách tên enum thay vì <c>Enum.TryParse</c>
/// (TryParse chấp nhận cả chuỗi số), bỏ qua hoa/thường, trống = không lọc.
/// </summary>
public static class FillRateTimeSlotCodes
{
    /// <summary>
    /// Đọc tham số <c>timeSlot</c>. Trống/whitespace = không lọc (<paramref name="slot"/> null).
    /// Giá trị lạ trả <see langword="false"/> để service dựng lỗi 400 theo trường.
    /// </summary>
    public static bool TryParse(string? value, out FillRateTimeSlot? slot)
    {
        var trimmed = value?.Trim();

        if (string.IsNullOrEmpty(trimmed))
        {
            slot = null;
            return true;
        }

        foreach (var name in Enum.GetNames<FillRateTimeSlot>())
        {
            if (string.Equals(name, trimmed, StringComparison.OrdinalIgnoreCase))
            {
                slot = Enum.Parse<FillRateTimeSlot>(name);
                return true;
            }
        }

        slot = null;
        return false;
    }

    /// <summary>Mã trả về trong JSON — chữ thường, đúng giá trị tham số.</summary>
    public static string ToCode(this FillRateTimeSlot slot) => slot.ToString().ToLowerInvariant();

    /// <summary>Giờ bắt đầu (tính) của khung, theo giờ Việt Nam.</summary>
    public static int FromHour(this FillRateTimeSlot slot) => slot switch
    {
        FillRateTimeSlot.Early => 5,
        FillRateTimeSlot.Morning => 9,
        FillRateTimeSlot.Afternoon => 13,
        FillRateTimeSlot.Evening => 17,
        _ => 22,
    };

    /// <summary>Giờ kết thúc (KHÔNG tính). Riêng <c>night</c> nhỏ hơn giờ bắt đầu vì vắt qua nửa đêm.</summary>
    public static int ToHour(this FillRateTimeSlot slot) => slot switch
    {
        FillRateTimeSlot.Early => 9,
        FillRateTimeSlot.Morning => 13,
        FillRateTimeSlot.Afternoon => 17,
        FillRateTimeSlot.Evening => 22,
        _ => 5,
    };

    /// <summary>
    /// Giờ <paramref name="vietnamHour"/> (0–23, giờ Việt Nam) có thuộc khung không. Cận trên
    /// KHÔNG tính; <c>night</c> bao gồm 22–23 và 0–4.
    /// </summary>
    public static bool Contains(this FillRateTimeSlot slot, int vietnamHour)
        => slot == FillRateTimeSlot.Night
            ? vietnamHour >= 22 || vietnamHour < 5
            : vietnamHour >= slot.FromHour() && vietnamHour < slot.ToHour();
}
