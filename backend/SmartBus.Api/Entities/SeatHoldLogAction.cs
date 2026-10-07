namespace SmartBus.Api.Entities;

/// <summary>
/// Loại sự kiện ghi vào <see cref="SeatHoldLog"/> — vòng đời một lượt giữ ghế (US 3 "Giữ chỗ tạm
/// thời"). Lưu dạng chuỗi trong CSDL (quy ước A3).
///
/// Mỗi lượt giữ sinh TỐI ĐA MỘT dòng cho mỗi giá trị ở đây. Đó không phải lời hứa suông: unique
/// index <c>(SeatHoldId, Action)</c> ở AppDbContext.Seat.cs cưỡng chế điều đó ngay ở tầng CSDL —
/// cùng lối partial unique index đang chặn hai khách giữ trùng một ghế.
///
/// Cả năm giá trị được khai ở đây dù job quét hết hạn mới chỉ ghi ra <see cref="Expired"/>: đây là
/// từ vựng của bảng, và <c>Entities/</c> là file của Vàng Thị Dăm (docs/26 §5) — người viết API giữ
/// ghế / gia hạn / huỷ (Kiên, Hiếu) dùng thẳng các giá trị có sẵn thay vì phải nhờ thêm.
/// </summary>
public enum SeatHoldLogAction
{
    /// <summary>Khách bắt đầu giữ ghế — một dòng <see cref="SeatHold"/> vừa được tạo.</summary>
    Held,

    /// <summary>Gia hạn thời gian giữ — US 3 cho tối đa 1 lần cho mỗi lượt giữ.</summary>
    Extended,

    /// <summary>Job quét lật sang <see cref="SeatHoldStatus.Expired"/> vì đã quá <see cref="SeatHold.ExpiresAt"/>.</summary>
    Expired,

    /// <summary>Khách chủ động nhả ghế trước hạn (huỷ thao tác đặt vé).</summary>
    Released,

    /// <summary>Lượt giữ đã chốt thành vé sau khi thanh toán xong.</summary>
    Confirmed,
}
