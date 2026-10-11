namespace SmartBus.Api.Hubs;

/// <summary>
/// Tập sự kiện server đẩy xuống client của <see cref="SeatMapHub"/> — khai kiểu mạnh thay vì gọi
/// <c>Clients.Group(...).SendAsync("SeatMapChanged", …)</c> bằng chuỗi trần.
///
/// Tên method ở đây CHÍNH LÀ tên sự kiện trong hợp đồng (mục "Đồng bộ ghế realtime — /hubs/seat-map"
/// của docs/api-contract.md): frontend đăng ký bằng đúng chuỗi "SeatMapChanged", nên đổi tên method
/// này là đổi hình dạng API — phải sửa docs/api-contract.md trước (quy ước 5).
/// </summary>
public interface ISeatMapClient
{
    /// <summary>
    /// Báo cho client trong nhóm của một chuyến rằng sơ đồ ghế của chuyến vừa đổi. Payload CHỈ có
    /// tripId, không kèm sơ đồ ghế (xem "Vì sao sự kiện chỉ báo đã đổi" của hợp đồng): client tự
    /// gọi lại GET /trips/{id}/seats để trạng thái ghế chỉ có MỘT nguồn sự thật.
    /// </summary>
    Task SeatMapChanged(Guid tripId);
}
