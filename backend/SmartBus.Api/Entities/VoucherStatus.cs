namespace SmartBus.Api.Entities;

/// <summary>
/// Trạng thái áp dụng của một voucher (US 18). Lưu dạng chuỗi trong CSDL (quy ước A3).
///
/// ⚠️ Đây là trạng thái NGƯỜI VẬN HÀNH bật/tắt, KHÔNG phải trạng thái hiệu lực — hai thứ khác nhau.
/// Một voucher <see cref="Active"/> vẫn có thể chưa tới <c>ValidFrom</c>, đã quá <c>ValidUntil</c>,
/// hoặc đã hết lượt. Service kiểm tra phải hỏi "lúc này dùng được không", không được chỉ hỏi
/// "có đang bật không" — cùng lối <see cref="DiscountRequestStatus"/>.
/// </summary>
public enum VoucherStatus
{
    /// <summary>Đang áp dụng. Trạng thái mặc định của voucher mới.</summary>
    Active,

    /// <summary>
    /// Ngừng áp dụng — người vận hành tắt tay. Giữ bản ghi làm lịch sử, không xoá: các lượt đã tiêu
    /// thụ vẫn trỏ vào voucher này.
    /// </summary>
    Inactive,

    // KHÔNG có Expired / OutOfStock: cả hai SUY RA ĐƯỢC (ValidUntil < now, UsedCount >= Quantity),
    // không phải trạng thái lưu trữ. Thêm chúng là đẻ ra hai nguồn sự thật — phải có job lật trạng
    // thái đúng hạn, không lật thì dữ liệu sai, và service phải xử lý cả hai đường. Cùng lý do
    // DiscountRequestStatus không có Expired.
    //
    // ⚠️ Hệ quả cho màn quản lý voucher (dòng 51 — Trần Trung Hiếu): danh sách trả về cột `status`
    // chỉ có hai giá trị này. Màn muốn hiện "hết hạn"/"hết lượt" thì tự suy từ validUntil/quantity,
    // đừng chờ backend trả một trạng thái thứ ba.
}
