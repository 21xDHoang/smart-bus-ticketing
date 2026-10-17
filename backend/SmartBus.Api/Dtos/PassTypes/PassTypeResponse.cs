namespace SmartBus.Api.Dtos.PassTypes;

/// <summary>
/// Một loại vé tháng trả về từ /api/pass-types (màn quản trị danh mục).
/// Hợp đồng đầy đủ ở mục "Loại vé tháng — /pass-types" của docs/api-contract.md.
/// </summary>
public class PassTypeResponse
{
    public Guid Id { get; set; }

    /// <summary>
    /// Mã nghiệp vụ — nội dung ô <c>passTypeCode</c> của POST đăng ký/gia hạn vé tháng.
    /// Không sửa được sau khi tạo: vé tháng tra loại vé theo Id, nhưng khách và client đang
    /// trao đổi với nhau bằng mã này.
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Tên hiển thị cho hành khách, ví dụ "Vé tháng 3 tháng".</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Thời hạn hiệu lực tính bằng tháng lịch.</summary>
    public int DurationMonths { get; set; }

    /// <summary>Giá gói cho toàn bộ thời hạn (VND).</summary>
    public decimal Price { get; set; }

    /// <summary>
    /// Số vé tháng đang tham chiếu loại vé này. Lớn hơn 0 thì KHÔNG xoá được — khoá ngoại
    /// Restrict giữ lịch sử mua bán.
    /// </summary>
    public int MonthlyPassCount { get; set; }

    public DateTime CreatedAt { get; set; }

    /// <summary>null khi loại vé chưa được sửa lần nào.</summary>
    public DateTime? UpdatedAt { get; set; }
}
