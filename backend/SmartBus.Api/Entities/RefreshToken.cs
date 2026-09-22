using System.ComponentModel.DataAnnotations.Schema;

namespace SmartBus.Api.Entities;

/// <summary>
/// Refresh token đã cấp cho một phiên đăng nhập.
/// Chỉ lưu bản BĂM (SHA-256) — mất CSDL cũng không lộ token dùng được.
/// Thu hồi = set <see cref="RevokedAt"/>. Đây là cơ chế đăng xuất của hệ thống.
/// </summary>
public class RefreshToken
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }

    public User? User { get; set; }

    /// <summary>SHA-256 (hex, 64 ký tự) của token thô trả cho client.</summary>
    public string TokenHash { get; set; } = string.Empty;

    public DateTime ExpiresAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Null = còn hiệu lực. Có giá trị = đã bị thu hồi (logout hoặc xoay vòng).</summary>
    public DateTime? RevokedAt { get; set; }

    [NotMapped]
    public bool IsActive => RevokedAt is null && DateTime.UtcNow < ExpiresAt;
}
