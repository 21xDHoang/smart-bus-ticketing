using System.ComponentModel.DataAnnotations;

namespace SmartBus.Api.Dtos.Trips;

/// <summary>
/// Body của PATCH /api/routes/{routeId}/trips/driver-assignment — gán MỘT tài xế cho NHIỀU chuyến
/// cùng lúc (màn hình "Phân công điều xe", US 14 — Nguyễn Duy Kiên).
/// Hợp đồng đầy đủ ở mục "Lịch trình chạy xe — /routes/{routeId}/trips" của docs/api-contract.md.
///
/// Không có "phụ xe": quy ước A8.4 chốt đúng 4 vai trò, bảng Trips cũng không có cột phụ xe.
/// </summary>
public class AssignDriverToTripsRequest
{
    /// <summary>
    /// Các chuyến cần gán tài xế. Khởi tạo sẵn mảng rỗng để body <c>{}</c> không biến thành null
    /// rồi nổ NullReferenceException ở tầng service: Program.cs đã tắt filter validate tự động
    /// của ASP.NET Core (ServiceResult tự lo phần lỗi), nên không có gì chặn trước hộ — cùng lối
    /// <see cref="RouteStops.ReorderRouteStopsRequest.Items"/>.
    ///
    /// <c>[Required]</c> bắt ca gửi hẳn <c>null</c>, còn <c>[MinLength]</c> bắt ca gửi mảng rỗng:
    /// <c>RequiredAttribute</c> coi mảng rỗng là hợp lệ nên một mình nó không đủ.
    ///
    /// Trần 200 lấy đúng con số trần 200 chuyến đang hoạt động/ngày của một tuyến
    /// (RouteTripsService) — một lô gán vượt quá số chuyến tối đa của một ngày là vô nghĩa.
    /// </summary>
    [Required(ErrorMessage = "Danh sách chuyến không được để trống")]
    [MinLength(1, ErrorMessage = "Danh sách chuyến không được để trống")]
    [MaxLength(200, ErrorMessage = "Một lần gán tối đa 200 chuyến")]
    public List<Guid> TripIds { get; set; } = [];

    /// <summary>
    /// Tài xế được gán — tài khoản mang vai trò <c>Driver</c> (quy ước A8.4, không có bảng Drivers
    /// riêng). Để <c>Guid?</c> chứ không phải <c>Guid</c>: thiếu hẳn trường này sẽ nhận
    /// <c>Guid.Empty</c> và <c>[Required]</c> không bắt được — cùng lý do
    /// <see cref="CreateTripRequest.BusId"/>.
    ///
    /// Cố ý KHÔNG nhận <c>null</c> để gỡ tài xế: màn hình phân công chỉ cần gán, mở thêm nhánh gỡ
    /// là mở thêm bề mặt chưa có người dùng.
    /// </summary>
    [Required(ErrorMessage = "Tài xế không được để trống")]
    public Guid? DriverId { get; set; }
}
