using System.ComponentModel.DataAnnotations;

namespace SmartBus.Api.Dtos.Admin;

/// <summary>Body của PATCH /api/admin/users/{id}/status — khóa hoặc mở khóa tài khoản.</summary>
public class UpdateUserStatusRequest
{
    /// <summary>true = mở khóa, false = khóa.</summary>
    /// <remarks>
    /// Để <c>bool?</c> chứ không phải <c>bool</c>: kiểu <c>bool</c> nhận giá trị mặc định false,
    /// nên body thiếu trường <c>isActive</c> sẽ bị hiểu thành "khóa tài khoản" — một request hỏng
    /// lại thành thao tác phá hoại.
    /// </remarks>
    [Required(ErrorMessage = "Thiếu trạng thái isActive")]
    public bool? IsActive { get; set; }
}
