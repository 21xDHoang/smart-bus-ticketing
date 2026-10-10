namespace SmartBus.Api.Entities;

/// <summary>
/// Kiểu giảm giá của một voucher (US 18). Lưu dạng chuỗi trong CSDL (quy ước A3) — "Percent" đọc là
/// hiểu, còn 1 thì phải tra bảng mã.
///
/// <see cref="DiscountType"/> quyết định Ý NGHĨA của cột <c>Voucher.DiscountValue</c>: cùng một cột
/// nhưng <see cref="Percent"/> đọc là phần trăm còn <see cref="FixedAmount"/> đọc là số tiền VND.
/// Vì vậy mọi chỗ đọc <c>DiscountValue</c> đều phải rẽ nhánh theo kiểu trước — không có con số nào
/// tự nói lên nghĩa của nó.
/// </summary>
public enum VoucherDiscountType
{
    /// <summary>Giảm theo phần trăm — <c>DiscountValue</c> từ 1 đến 100, có thể bị chặn trần bởi <c>MaxDiscount</c>.</summary>
    Percent,

    /// <summary>Giảm số tiền cố định — <c>DiscountValue</c> là VND, <c>MaxDiscount</c> không có nghĩa.</summary>
    FixedAmount
}
