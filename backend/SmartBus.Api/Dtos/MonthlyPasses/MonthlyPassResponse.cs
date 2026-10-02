namespace SmartBus.Api.Dtos.MonthlyPasses;

/// <summary>
/// Hình dạng trả về của endpoint vé tháng — khớp interface <c>MonthlyPass</c> của frontend
/// (frontend/src/api/monthlyPassApi.ts). Gia hạn, tra cứu vé đang hoạt động (GET
/// /monthly-passes/me) và endpoint đăng ký vé tháng (POST /monthly-passes — chưa làm, task của
/// Trần Trung Hiếu) dùng chung DTO này thay vì dựng hình dạng thứ hai cho cùng một tài nguyên.
/// Hợp đồng đầy đủ ở mục "Vé tháng — /monthly-passes" của docs/api-contract.md.
/// </summary>
public class MonthlyPassResponse
{
    public Guid Id { get; set; }

    /// <summary>Mã vé tháng (nội dung mã QR soát vé) — khuôn "MP-{mã tuyến}-{6 ký tự}".</summary>
    public string Code { get; set; } = string.Empty;

    public Guid RouteId { get; set; }

    /// <summary>Mã loại vé trong bảng PassTypes — quyết định thời hạn và giá gói.</summary>
    public string PassTypeCode { get; set; } = string.Empty;

    /// <summary>Số tiền thực trả — ảnh chụp tại thời điểm đăng ký/gia hạn, không tra lại bảng giá.</summary>
    public decimal Price { get; set; }

    /// <summary>Ngày bắt đầu hiệu lực (UTC). Hiệu lực thật suy từ cặp mốc này, không đọc Status.</summary>
    public DateTime ValidFrom { get; set; }

    /// <summary>Ngày hết hiệu lực (UTC), tính cả mốc.</summary>
    public DateTime ValidTo { get; set; }

    /// <summary>"Active" / "Expired" — trạng thái LƯU, có độ trễ so với hiệu lực thật (job quét của Kiên).</summary>
    public string Status { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
}
