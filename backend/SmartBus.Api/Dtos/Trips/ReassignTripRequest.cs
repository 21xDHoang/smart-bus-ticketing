namespace SmartBus.Api.Dtos.Trips;

/// <summary>
/// Body của <c>PATCH /trips/{id}/assignment</c> — đổi xe và/hoặc tài xế của một chuyến khi có
/// sự cố (task "API đổi xe/đổi tài xế khi có sự cố + ghi log thay đổi", story 14).
///
/// Cả hai trường đều không bắt buộc: bỏ trống một trường là GIỮ NGUYÊN giá trị hiện tại của
/// chuyến — đây là sửa một phần (PATCH), khác <c>PUT /routes/{routeId}/trips/{id}</c> ghi đè
/// toàn phần. Ít nhất một trường phải có giá trị; không truyền gì thì không có gì để đổi,
/// service trả 400 với lỗi ở cả hai trường.
///
/// Không có cách gán tài xế về null qua API này: cột <c>DriverId</c> nullable vì chuyến sinh
/// hàng loạt ra đời trước khi phân công, còn luồng sự cố luôn là "thay người khác vào".
/// </summary>
public class ReassignTripRequest
{
    /// <summary>Xe mới của chuyến. Bỏ trống = giữ nguyên xe hiện tại.</summary>
    public Guid? BusId { get; set; }

    /// <summary>Tài xế mới của chuyến — tài khoản mang vai trò Driver. Bỏ trống = giữ nguyên.</summary>
    public Guid? DriverId { get; set; }
}
