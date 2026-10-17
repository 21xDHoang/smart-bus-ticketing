namespace SmartBus.Api.Dtos.MonthlyPasses.Admin;

/// <summary>
/// Một dòng vé tháng của màn quản trị — mở rộng <see cref="MonthlyPassResponse"/> bằng thông tin
/// hành khách, tuyến và loại vé để bảng không phải ghép dữ liệu ở client.
/// Hợp đồng đầy đủ ở mục "Vé tháng — /monthly-passes" của docs/api-contract.md.
/// </summary>
public class MonthlyPassAdminResponse
{
    public Guid Id { get; set; }

    /// <summary>Mã vé tháng (nội dung mã QR soát vé) — khuôn "MP-{mã tuyến}-{6 ký tự}".</summary>
    public string Code { get; set; } = string.Empty;

    public Guid UserId { get; set; }

    /// <summary>Họ tên hành khách. Chuỗi rỗng nếu dữ liệu mồ côi (FK Restrict chặn xoá thật).</summary>
    public string UserFullName { get; set; } = string.Empty;

    public string UserPhoneNumber { get; set; } = string.Empty;

    public Guid RouteId { get; set; }

    public string RouteCode { get; set; } = string.Empty;

    public string RouteName { get; set; } = string.Empty;

    /// <summary>Mã loại vé trong bảng PassTypes — quyết định thời hạn và giá gói.</summary>
    public string PassTypeCode { get; set; } = string.Empty;

    public string PassTypeName { get; set; } = string.Empty;

    /// <summary>Số tiền thực trả — ảnh chụp tại thời điểm đăng ký/gia hạn.</summary>
    public decimal Price { get; set; }

    /// <summary>Ngày bắt đầu hiệu lực (UTC).</summary>
    public DateTime ValidFrom { get; set; }

    /// <summary>Ngày hết hiệu lực (UTC), tính cả mốc.</summary>
    public DateTime ValidTo { get; set; }

    /// <summary>
    /// "Active" / "Expired" / "Cancelled". Active/Expired có độ trễ so với hiệu lực thật;
    /// Cancelled do quản trị viên đặt và là chặn cứng.
    /// </summary>
    public string Status { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    /// <summary>null khi vé chưa được sửa lần nào (job hết hạn hoặc quản trị viên huỷ/mở lại).</summary>
    public DateTime? UpdatedAt { get; set; }
}
