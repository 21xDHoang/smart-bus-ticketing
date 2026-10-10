namespace SmartBus.Api.Entities;

/// <summary>
/// Một mã giảm giá (US 18 "Quản lý Voucher"). Bảng <c>Vouchers</c> — task *"API kiểm tra và áp dụng
/// voucher vào đơn hàng"* (dòng 52, Nguyễn Duy Kiên, Sprint 3). Hình dạng chốt ở mục
/// "Voucher — /vouchers" của docs/api-contract.md.
///
/// ⚠️ Bảng CHƯA có migration. Entity + cấu hình (Data/AppDbContext.Voucher.cs) viết để API biên dịch
/// và test được trên EF InMemory — Vàng Thị Dăm đã cho phép dựng bảng, và việc chạy
/// <c>dotnet ef migrations add</c> vẫn là của Dăm: xem docs/27-huong-dan-migrate-vouchers.md.
///
/// Vòng đời một voucher: quản lý tạo (dòng 51 — Trần Trung Hiếu) → khách gõ mã ở màn thanh toán →
/// <c>POST /vouchers/validate</c> trả lời dùng được hay không (KHÔNG tiêu thụ gì) → khách bấm xác
/// nhận → <c>POST /payments</c> trừ vào số phải trả → tiền về thì ghi một dòng
/// <see cref="VoucherUsage"/> và tăng <see cref="UsedCount"/>.
/// </summary>
public class Voucher
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Mã khách gõ — DUY NHẤT toàn hệ thống, lưu CHỮ HOA. Mọi đường đọc/ghi đều chuẩn hoá
    /// <c>Trim().ToUpperInvariant()</c> trước khi tra, nên gõ "summer10" vẫn ra.
    ///
    /// ⚠️ Unique index trong CSDL phân biệt hoa thường, nên chốt "duy nhất" được giữ bằng BA lớp:
    /// DTO chặn khuôn ký tự → service chuẩn hoá HOA → unique index là chốt cuối. Ghi thẳng xuống
    /// CSDL bằng chữ thường vẫn lọt qua index — xem ghi chú ở AppDbContext.Voucher.cs về lựa chọn
    /// index trên <c>upper("Code")</c>.
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Tên hiển thị ở màn quản lý và ở ô nhập mã của khách.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Kiểu giảm giá — quyết định cách đọc <see cref="DiscountValue"/>. Đọc cột này TRƯỚC khi đọc
    /// con số, xem <see cref="VoucherDiscountType"/>.
    /// </summary>
    public VoucherDiscountType DiscountType { get; set; }

    /// <summary>
    /// <see cref="VoucherDiscountType.Percent"/>: 1–100. <see cref="VoucherDiscountType.FixedAmount"/>:
    /// số tiền VND. Ràng buộc 1–100 thuộc tầng API, CSDL không có check constraint — cùng lối các
    /// cột nghiệp vụ khác của dự án (xem <see cref="Feedback.Rating"/>).
    /// </summary>
    public decimal DiscountValue { get; set; }

    /// <summary>
    /// Giá trị đơn tối thiểu để mã dùng được; <c>0</c> = không yêu cầu. So với tổng tiền **TRƯỚC**
    /// giảm giá — so với số phải trả thì mã tự khoá chính mình (đơn càng lớn càng dễ tụt dưới ngưỡng).
    /// </summary>
    public decimal MinOrderValue { get; set; }

    /// <summary>
    /// Trần số tiền giảm, chỉ có nghĩa khi <see cref="DiscountType"/> là
    /// <see cref="VoucherDiscountType.Percent"/> — "giảm 20% tối đa 30.000đ". <c>null</c> = không trần.
    /// Kiểu nullable vì với <see cref="VoucherDiscountType.FixedAmount"/> trần là khái niệm vô nghĩa
    /// (đã cố định rồi); để <c>0</c> thay cho null sẽ thành "trần 0đ" và vô hiệu hoá luôn voucher.
    /// </summary>
    public decimal? MaxDiscount { get; set; }

    /// <summary>
    /// Điều kiện TUYẾN: <c>null</c> = áp dụng cho mọi tuyến, có giá trị = chỉ tuyến đó. Đây là phần
    /// "tuyến" của task dòng 53. So với <c>Trip.RouteId</c> của chuyến khách đang đặt.
    ///
    /// ⚠️ Bản nháp FE (frontend/src/api/voucherApi.ts — Hạnh) chưa có trường này, nên hiện tại nó
    /// luôn <c>null</c> khi tạo voucher từ màn quản lý. Luật vẫn đúng và đã có test; màn quản lý cần
    /// bổ sung ô chọn tuyến thì mới dùng được từ giao diện.
    /// </summary>
    public Guid? RouteId { get; set; }

    public Route? Route { get; set; }

    /// <summary>Tổng số lượt phát hành. Cạn lượt là <see cref="UsedCount"/> chạm con số này.</summary>
    public int Quantity { get; set; }

    /// <summary>
    /// Số lượt đã tiêu thụ thật (chỉ tăng khi tiền về, không tăng lúc khách mới gõ mã). Do backend
    /// ghi — giá trị FE gửi lên ở request sửa bị bỏ qua.
    /// </summary>
    public int UsedCount { get; set; }

    /// <summary>Bắt đầu hiệu lực — tính CẢ đầu mút này (<c>now >= ValidFrom</c>).</summary>
    public DateTime ValidFrom { get; set; }

    /// <summary>Kết thúc hiệu lực — tính CẢ đầu mút này (<c>now &lt;= ValidUntil</c>).</summary>
    public DateTime ValidUntil { get; set; }

    /// <summary>Bật/tắt của người vận hành — xem <see cref="VoucherStatus"/>, KHÔNG phải trạng thái hiệu lực.</summary>
    public VoucherStatus Status { get; set; } = VoucherStatus.Active;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Đổi khi quản lý sửa voucher hoặc khi <see cref="UsedCount"/> tăng — bảng có sửa (A4).</summary>
    public DateTime? UpdatedAt { get; set; }

    /// <summary>Các lượt đã tiêu thụ — bảng <c>VoucherUsages</c>, chỉ ghi thêm.</summary>
    public ICollection<VoucherUsage> Usages { get; set; } = [];
}
