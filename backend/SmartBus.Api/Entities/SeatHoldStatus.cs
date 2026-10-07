namespace SmartBus.Api.Entities;

/// <summary>
/// Vòng đời một lượt giữ ghế tạm thời (US 3 — "tự động giữ chỗ trong 10 phút khi khách đang thao
/// tác thanh toán"). Lưu dạng chuỗi trong CSDL (quy ước A3).
///
/// Chỉ <see cref="Holding"/> mới chặn ghế — đó là điều kiện của partial unique index
/// trong AppDbContext.Seat.cs.
/// </summary>
public enum SeatHoldStatus
{
    /// <summary>Đang giữ — ghế bị chặn cho tới <see cref="SeatHold.ExpiresAt"/>.</summary>
    Holding,

    /// <summary>Đã chốt thành vé sau khi thanh toán xong — hết là giữ tạm.</summary>
    Confirmed,

    /// <summary>Hết hạn: job quét của BackgroundService chuyển sang trạng thái này rồi nhả ghế.</summary>
    Expired,

    /// <summary>Khách chủ động nhả trước hạn (huỷ thao tác đặt vé).</summary>
    Released
}
