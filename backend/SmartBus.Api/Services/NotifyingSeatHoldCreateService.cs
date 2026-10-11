using SmartBus.Api.Dtos.SeatHolds;

namespace SmartBus.Api.Services;

/// <summary>
/// Lớp bọc phát sự kiện realtime cho <see cref="ISeatHoldCreateService"/> — task *"Đồng bộ trạng thái
/// ghế realtime giữa các phiên (SignalR)"*, dòng 11 Sprint 3 (US 2).
///
/// Vì sao là lớp bọc chứ không sửa thẳng <see cref="SeatHoldCreateService"/>: service đó là file của
/// Nguyễn Duy Kiên (quy ước E1 — nhóm không sửa file của người khác), cùng lối
/// <see cref="CachedTripSearchService"/> bọc <see cref="TripSearchService"/>. Service thật không phải
/// biết gì về SignalR; lớp bọc chỉ thêm đúng một việc: giữ ghế THÀNH CÔNG thì báo cho các phiên đang
/// xem sơ đồ ghế của chuyến đó.
///
/// Chỉ phát khi <see cref="ServiceResult{T}.Success"/>: mọi nhánh hỏng (409 ghế vừa bị phiên khác
/// giữ, 400 danh sách ghế sai, 404 chuyến không tồn tại) đều KHÔNG đổi trạng thái ghế nào — không có
/// gì để báo, và báo ở đó chỉ làm mọi client gọi lại sơ đồ ghế vô ích.
/// </summary>
public class NotifyingSeatHoldCreateService : ISeatHoldCreateService
{
    private readonly SeatHoldCreateService _inner;

    private readonly ISeatMapNotifier _notifier;

    public NotifyingSeatHoldCreateService(SeatHoldCreateService inner, ISeatMapNotifier notifier)
    {
        _inner = inner;
        _notifier = notifier;
    }

    public async Task<ServiceResult<SeatHoldSessionResponse>> CreateAsync(
        Guid userId,
        CreateSeatHoldRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await _inner.CreateAsync(userId, request, cancellationToken);

        // TripId đọc từ REQUEST (chuyến khách gửi lên) chứ không từ result.Data: giữ ghế thành công thì
        // hai nguồn luôn bằng nhau, mà request là nguồn không phụ thuộc hình dạng response của service
        // thật — người khác đổi response thì lớp bọc này không hỏng theo.
        // DTO khai TripId là Guid? ([Required] bắt buộc có giá trị), nên mở bằng pattern `is { }` thay
        // vì dấu !: thành công thì service thật đã tìm thấy chuyến nên giá trị luôn có — nhánh null
        // chỉ là đường không bao giờ tới, và nếu tới thật thì bỏ qua lời nhắc là hướng hỏng vô hại
        // (client vẫn thấy đúng sau lần tải trang kế tiếp).
        if (result.Success && request.TripId is { } tripId)
        {
            await _notifier.SeatMapChangedAsync(tripId);
        }

        return result;
    }
}
