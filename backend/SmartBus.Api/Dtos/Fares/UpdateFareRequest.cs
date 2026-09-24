namespace SmartBus.Api.Dtos.Fares;

/// <summary>
/// Body của PUT /api/routes/{routeId}/fares/{id} — chỉ sửa giá.
///
/// Cố ý KHÔNG có PassengerType: đổi đối tượng tại chỗ rất dễ đâm vào ràng buộc unique
/// (RouteId, PassengerType) nếu tuyến đã có sẵn dòng cho đối tượng mới. Muốn đổi đối tượng
/// thì xoá dòng cũ rồi tạo dòng mới — hai thao tác đều đã có endpoint riêng.
/// Cùng lối với PUT /api/admin/users/{id}: chỉ sửa phần vô hại, không cho đổi thứ dùng làm khoá.
/// </summary>
public class UpdateFareRequest
{
    /// <summary>Giá vé mới (VND). Biên kiểm tra giống <see cref="CreateFareRequest.Price"/>.</summary>
    public decimal Price { get; set; }
}
