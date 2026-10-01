namespace SmartBus.Api.Dtos.Drivers;

/// <summary>
/// Một hồ sơ tài xế — khớp mục "Hồ sơ tài xế — /drivers" của docs/api-contract.md.
/// Tài xế là một User mang vai trò Driver (quy ước A8.4) nên đây là lát cắt hồ sơ của bảng
/// Users, không phải một bảng riêng. Đổi tên trường ở đây là đổi hình dạng API — phải sửa
/// api-contract.md trước rồi báo người viết frontend (Thịnh).
/// </summary>
public class DriverResponse
{
    public Guid Id { get; set; }

    public string FullName { get; set; } = string.Empty;

    /// <summary>SĐT đăng nhập, duy nhất toàn hệ thống.</summary>
    public string PhoneNumber { get; set; } = string.Empty;

    /// <summary>Không bắt buộc — tài xế vẫn đăng nhập được bằng SĐT khi chưa có email.</summary>
    public string? Email { get; set; }

    /// <summary>Hồ sơ còn hoạt động (true) hay đã bị khóa (false) — xoá mềm ở DELETE /drivers/{id}.</summary>
    public bool IsActive { get; set; }

    public DateTime CreatedAt { get; set; }
}
