namespace SmartBus.Api.Services;

/// <summary>
/// Phát hiện tài khoản giữ chỗ quá nhiều lần và cảnh báo (US 3 "Giữ chỗ tạm thời") — task *"Ghi log
/// và cảnh báo khi một tài khoản giữ chỗ quá nhiều lần"* (Sprint 3, Vàng Thị Dăm).
///
/// Vì sao cần: US 3 cho khách giữ ghế 10 phút miễn phí, không ràng buộc gì. Một tài khoản cứ giữ
/// ghế rồi để hết hạn sẽ khoá ghế của người khác mà không mua vé — đúng hành vi mà màn chọn ghế
/// không thể phân biệt với một khách đang cân nhắc. Cái phân biệt được là TẦN SUẤT, và tần suất
/// chỉ nhìn thấy khi đếm qua thời gian.
///
/// Người gọi duy nhất trong app là <see cref="SeatHoldAbuseBackgroundService"/> — cùng lối
/// <see cref="ISeatHoldExpiryService"/>: phần ruột tách khỏi vòng lặp nền để test gọi thẳng, không
/// phải chờ đồng hồ thật.
/// </summary>
public interface ISeatHoldAbuseService
{
    /// <summary>
    /// Đếm số PHIÊN giữ chỗ khác nhau của từng tài khoản trong cửa sổ
    /// <c>[now - CuaSo, now]</c>; tài khoản đạt ngưỡng thì ghi một dòng
    /// <see cref="Entities.AuditLog"/> (<c>Action = Warning</c>) và một dòng cảnh báo vào log ứng
    /// dụng, rồi trả về số TÀI KHOẢN vừa bị cảnh báo (không phải số lượt giữ).
    ///
    /// Tài khoản đã có cảnh báo trong chính cửa sổ đó KHÔNG bị ghi thêm — xem chú thích ở
    /// <c>SeatHoldAbuseService</c>.
    ///
    /// <paramref name="now"/> là tham số chứ không đọc <c>DateTime.UtcNow</c> bên trong: job chạy
    /// theo lịch nên "bây giờ" là dữ liệu vào của nó, và test phải ghim được mốc đó mới thử được
    /// các ca biên (lượt giữ đúng mốc đầu cửa sổ, cảnh báo lặp trong cùng cửa sổ) mà không phải
    /// chờ đồng hồ thật — cùng lối <see cref="ISeatHoldExpiryService.ExpireDueHoldsAsync"/>.
    /// </summary>
    Task<int> ScanAndWarnAsync(DateTime now, CancellationToken cancellationToken = default);
}
