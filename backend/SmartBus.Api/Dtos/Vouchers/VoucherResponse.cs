namespace SmartBus.Api.Dtos.Vouchers;

/// <summary>
/// Một voucher trả về cho màn quản trị — mọi endpoint của task *"API CRUD voucher"* (dòng 51, US 18,
/// Trần Trung Hiếu). Hình dạng khớp bảng trường "CRUD voucher" của docs/api-contract.md và interface
/// <c>Voucher</c> trong bản nháp FE (frontend/src/api/voucherApi.ts), CỘNG thêm
/// <see cref="RouteCode"/>/<see cref="RouteName"/> để màn quản lý hiển thị điều kiện tuyến không
/// phải gọi thêm GET /routes/{id} — cùng lối <see cref="VoucherStatisticsItemResponse"/>.
///
/// <see cref="DiscountType"/> và <see cref="Status"/> là chuỗi chứ không phải enum: dự án không bật
/// JsonStringEnumConverter nên enum tuôn ra JSON thành con số; service cố ý <c>ToString()</c> trong
/// bộ nhớ — cùng lối VoucherStatisticsService.
///
/// Cố ý KHÔNG nhận <c>usedCount</c> từ request: con số này do backend giữ và chỉ tăng khi tiền về
/// (VoucherRedemptionService) — FE gửi lên ở request sửa bị bỏ qua theo hợp đồng.
/// </summary>
public class VoucherResponse
{
    public Guid Id { get; set; }

    /// <summary>Mã khách gõ — đã chuẩn hoá CHỮ HOA trên mọi đường ghi.</summary>
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary><c>Percent</c> / <c>FixedAmount</c> — quyết định cách đọc <see cref="DiscountValue"/>.</summary>
    public string DiscountType { get; set; } = string.Empty;

    public decimal DiscountValue { get; set; }

    public decimal MinOrderValue { get; set; }

    public decimal? MaxDiscount { get; set; }

    /// <summary>Điều kiện tuyến — null = áp dụng cho mọi tuyến.</summary>
    public Guid? RouteId { get; set; }

    /// <summary>Mã tuyến hiển thị — null khi voucher dùng cho mọi tuyến.</summary>
    public string? RouteCode { get; set; }

    /// <summary>Tên tuyến hiển thị — null khi voucher dùng cho mọi tuyến.</summary>
    public string? RouteName { get; set; }

    /// <summary>Tổng số lượt phát hành.</summary>
    public int Quantity { get; set; }

    /// <summary>Số lượt đã tiêu thụ thật — backend tăng, FE chỉ đọc.</summary>
    public int UsedCount { get; set; }

    public DateTime ValidFrom { get; set; }

    public DateTime ValidUntil { get; set; }

    /// <summary><c>Active</c> / <c>Inactive</c> — trạng thái NGƯỜI VẬN HÀNH bật/tắt.</summary>
    public string Status { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }
}
