namespace SmartBus.Api.Dtos.Trips;

/// <summary>
/// Kết quả GET /api/trips/{id}/seats — sơ đồ ghế theo chuyến kèm trạng thái từng ghế
/// (US 2 "Chọn vị trí ghế" — task *"API lấy sơ đồ ghế theo chuyến + trạng thái từng ghế"*,
/// Trần Trung Hiếu). Hợp đồng đầy đủ ở mục "GET /trips/{id}/seats" của docs/api-contract.md.
///
/// Hình dạng này khớp hợp đồng DỰ KIẾN mà frontend đã viết sẵn trong
/// <c>frontend/src/api/seatMapApi.ts</c> — hai chỗ khác có chủ đích:
/// • <see cref="PricePerSeat"/> / <see cref="VipSurcharge"/> / <see cref="TripSeatMapSeatResponse.Price"/>
///   là nullable: thiếu giá là trạng thái dữ liệu bình thường (tuyến chưa cấu hình giá,
///   phụ trội VIP chưa có nguồn), không phải lỗi — cùng lối <c>price</c> của GET /trips/search.
/// • Trạng thái "Paid" chưa xuất hiện vì bảng Tickets chưa migrate (US 4, Vàng Thị Dăm).
/// Khi bật cờ USE_MOCK_DATA trong seatMapApi.ts xuống false, frontend cập nhật hai điểm này
/// theo hợp đồng.
/// </summary>
public class TripSeatMapResponse
{
    /// <summary>Khoá chuyến — lặp lại để màn hình không phải tự nhớ đang gọi cho chuyến nào.</summary>
    public Guid TripId { get; set; }

    /// <summary>Loại xe của chuyến — "Xe buýt 45 chỗ".</summary>
    public string BusType { get; set; } = string.Empty;

    /// <summary>
    /// Số tầng của sơ đồ (<c>SeatLayouts.NumberOfFloors</c>). Loại xe chưa có sơ đồ → 0 kèm
    /// <see cref="Seats"/> rỗng — trạng thái dữ liệu hợp lệ, không phải lỗi.
    /// </summary>
    public int Floors { get; set; }

    /// <summary>
    /// Giá vé phổ thông của tuyến (VND) — cùng nguồn và cùng nghĩa với <c>price</c> của
    /// GET /trips/search. Tuyến chưa cấu hình giá → null.
    /// </summary>
    public decimal? PricePerSeat { get; set; }

    /// <summary>
    /// Phụ trội ghế VIP (VND). Chưa có nguồn dữ liệu nào lưu con số này (phần "giá theo ghế +
    /// tổng tiền tạm tính" là task của Dương Thị Hạnh) nên hôm nay luôn null — hình dạng trường
    /// đã chốt sẵn trong hợp đồng để frontend ghép được.
    /// </summary>
    public decimal? VipSurcharge { get; set; }

    /// <summary>
    /// Toàn bộ ghế của xe chạy chuyến — kể cả ghế đã bán / đang giữ, vì màn hình chọn ghế vẽ
    /// nguyên sơ đồ chứ không phải chỉ danh sách ghế trống.
    /// </summary>
    public List<TripSeatMapSeatResponse> Seats { get; set; } = [];
}
