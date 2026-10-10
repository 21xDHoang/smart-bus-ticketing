using System.ComponentModel.DataAnnotations;

namespace SmartBus.Api.Dtos.SeatHolds;

/// <summary>
/// Body của POST /api/seat-holds — giữ tạm một nhóm ghế của một chuyến trong 10 phút (US 3 "Giữ chỗ
/// tạm thời", task *"API giữ ghế tạm thời (khóa ghế theo phiên)"* — Nguyễn Duy Kiên).
/// Hợp đồng đầy đủ ở mục "Giữ chỗ — /seat-holds" của docs/api-contract.md.
///
/// Cố ý KHÔNG có <c>userId</c>: người giữ ghế là người đang đăng nhập, lấy từ claim
/// <c>NameIdentifier</c> — cùng lối <see cref="Feedbacks.CreateFeedbackRequest"/>. Không có tham số
/// nào trong body cho phép giữ ghế hộ người khác.
/// </summary>
public class CreateSeatHoldRequest
{
    /// <summary>
    /// Chuyến mà khách đang chọn ghế. Để <c>Guid?</c> chứ không phải <c>Guid</c>: thiếu hẳn trường
    /// này sẽ nhận <c>Guid.Empty</c> và <c>[Required]</c> không bắt được — cùng lý do
    /// <see cref="Trips.AssignDriverToTripsRequest.DriverId"/>.
    ///
    /// Có giá trị mà không trỏ tới chuyến nào (kể cả <c>Guid.Empty</c>) → 404; chuyến đã huỷ hoặc đã
    /// chạy xong → 409 — service quyết định, vì "chuyến có tồn tại không" là câu hỏi của CSDL.
    /// </summary>
    [Required(ErrorMessage = "Chuyến xe không được để trống")]
    public Guid? TripId { get; set; }

    /// <summary>
    /// Các ghế khách chọn giữ. Bắt buộc có ít nhất một ghế: một phiên giữ chỗ rỗng không chặn được
    /// ghế nào mà vẫn chiếm một mã phiên, và màn hình đếm ngược sẽ đếm ngược một thứ không tồn tại.
    ///
    /// Khởi tạo sẵn mảng rỗng để body <c>{}</c> không biến thành null rồi nổ NullReferenceException ở
    /// tầng service: Program.cs đã tắt filter validate tự động của ASP.NET Core
    /// (<c>SuppressModelStateInvalidFilter</c>), nên không có gì chặn trước hộ — cùng lối
    /// <see cref="Trips.AssignDriverToTripsRequest.TripIds"/>. <c>[Required]</c> bắt ca gửi hẳn
    /// <c>null</c>, còn <c>[MinLength]</c> bắt ca gửi mảng rỗng: <c>RequiredAttribute</c> coi mảng
    /// rỗng là hợp lệ nên một mình nó không đủ.
    ///
    /// Trần số ghế KHÔNG khai ở đây mà nằm ở tầng service: mọi ghế phải thuộc xe của chuyến, nên
    /// không thể giữ nhiều ghế hơn sức chứa của xe — một con số cứng ở đây sẽ là bản sao dễ lệch của
    /// ràng buộc đó (cùng lối "không thêm cột đếm" của SeatHolds).
    /// </summary>
    [Required(ErrorMessage = "Danh sách ghế không được để trống")]
    [MinLength(1, ErrorMessage = "Danh sách ghế không được để trống")]
    public List<Guid> SeatIds { get; set; } = [];
}
