using System.ComponentModel.DataAnnotations;

namespace SmartBus.Api.Dtos.Stops;

/// <summary>
/// Body dùng chung cho POST và PUT /api/stops — hai thao tác nhận đúng cùng bộ trường
/// <c>{ name, address, latitude, longitude }</c> như hợp đồng ở docs/api-contract.md.
/// Frontend cũng gửi cùng một kiểu <c>StopPayload</c> cho cả thêm và sửa
/// (frontend/src/api/stopApi.ts), nên không tách Create/Update thành hai lớp giống hệt nhau.
/// </summary>
public class StopRequest
{
    [Required(ErrorMessage = "Tên trạm không được để trống")]
    [StringLength(200, MinimumLength = 2, ErrorMessage = "Tên trạm phải từ 2 đến 200 ký tự")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Địa chỉ không được để trống")]
    [StringLength(300, ErrorMessage = "Địa chỉ tối đa 300 ký tự")]
    public string Address { get; set; } = string.Empty;

    /// <summary>
    /// Vĩ độ. [Range] dùng được ở đây vì biên là hai hằng số đơn giản -90 và 90 —
    /// khác tiền tệ phải khai báo bằng decimal chính xác ở tầng nghiệp vụ (xem FareService).
    /// </summary>
    [Range(-90, 90, ErrorMessage = "Vĩ độ phải từ -90 đến 90")]
    public double Latitude { get; set; }

    /// <summary>Kinh độ, cùng lối với <see cref="Latitude"/>.</summary>
    [Range(-180, 180, ErrorMessage = "Kinh độ phải từ -180 đến 180")]
    public double Longitude { get; set; }
}
