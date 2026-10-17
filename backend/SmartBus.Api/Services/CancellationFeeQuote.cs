namespace SmartBus.Api.Services;

/// <summary>
/// Kết quả báo giá phí huỷ tại đúng thời điểm khách bấm gửi — đầu ra của
/// <see cref="ICancellationFeeService"/>.
///
/// <b>Phải đọc <see cref="CanCancel"/> TRƯỚC <see cref="Fee"/>.</b> Khi bị từ chối, <see cref="Fee"/>
/// bằng <c>0</c> chỉ vì kiểu <c>decimal</c> không có giá trị "không áp dụng" — <c>0</c> ở đây KHÔNG
/// có nghĩa là huỷ miễn phí. Lý do từ chối nằm ở <see cref="RefusalReason"/> và là câu hiển thị
/// thẳng cho khách.
/// </summary>
/// <param name="CanCancel">Có thoả điều kiện thời gian để gửi yêu cầu huỷ hay không.</param>
/// <param name="Fee">
/// Phí huỷ (VND) sẽ ghi vào <c>CancellationRequest.CancellationFee</c> — luôn là số nguyên đồng.
/// Chỉ có nghĩa khi <paramref name="CanCancel"/> là <c>true</c>.
/// </param>
/// <param name="AutoApprove">
/// <c>true</c> khi vé còn trong cửa sổ 10 phút kể từ lúc tạo: yêu cầu huỷ được ghi thẳng
/// <c>Approved</c>, không qua quản lý duyệt. Khi đó <paramref name="Fee"/> luôn bằng <c>0</c>.
/// Endpoint vẫn phải tự kiểm trạng thái vé và yêu cầu đang chờ — cờ này chỉ miễn bước duyệt.
/// </param>
/// <param name="RefusalReason">
/// Câu giải thích khi không được huỷ; <c>null</c> khi được huỷ. Endpoint dùng nguyên văn câu này
/// trong lỗi trả về để màn hình khách và thông báo không nói hai kiểu khác nhau.
/// </param>
public sealed record CancellationFeeQuote(
    bool CanCancel,
    decimal Fee,
    bool AutoApprove,
    string? RefusalReason);
