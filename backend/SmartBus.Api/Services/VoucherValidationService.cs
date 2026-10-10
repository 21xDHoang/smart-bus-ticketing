using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SmartBus.Api.Data;
using SmartBus.Api.Dtos.Vouchers;
using SmartBus.Api.Entities;

namespace SmartBus.Api.Services;

/// <summary>
/// Bộ luật voucher + cách tính tiền giảm — phần ruột của dòng 52 *"API kiểm tra và áp dụng voucher
/// vào đơn hàng"* và toàn bộ dòng 53 *"Validate điều kiện voucher: thời gian, tuyến, giá trị đơn tối
/// thiểu"* (hai dòng của bảng phân công là một việc). Nguyễn Duy Kiên, Sprint 3 — US 18.
///
/// Thứ tự kiểm tra và cách tính tiền đã chốt ở mục "Voucher — /vouchers" của docs/api-contract.md.
/// Đổi một trong hai thứ đó là đổi hình dạng API theo luật 5 — sửa hợp đồng trước.
/// </summary>
public class VoucherValidationService : IVoucherValidationService
{
    private readonly AppDbContext _db;

    public VoucherValidationService(AppDbContext db) => _db = db;

    public async Task<ServiceResult<VoucherValidationResponse>> ValidateAsync(
        ValidateVoucherRequest request, CancellationToken cancellationToken = default)
    {
        // Chuẩn hoá TRƯỚC khi tra: mã lưu trong CSDL luôn là chữ HOA, nên không chuẩn hoá ở đây thì
        // khách gõ "summer10" sẽ nhận "mã không tồn tại" cho một mã có thật — câu trả lời sai mà
        // không có lỗi nào để lần theo.
        var code = NormalizeCode(request.Code);
        var orderAmount = request.OrderAmount ?? 0m;

        // Chuyến tra trước, và tra bằng một truy vấn CHỈ lấy RouteId: chuyến không tồn tại là lỗi
        // phía gọi (404), phải chặn trước khi tốn công tra voucher.
        //
        // Ép sang Guid? để phân biệt được "không có chuyến" với "chuyến có tuyến": FirstOrDefault
        // trên Guid? trả null ĐÚNG khi không có dòng nào, còn trả Guid.Empty nếu chuyến thật sự
        // trỏ tới Guid.Empty — hai ca khác nhau và không được lẫn.
        var tripRouteId = await _db.Trips
            .Where(t => t.Id == request.TripId)
            .Select(t => (Guid?)t.RouteId)
            .FirstOrDefaultAsync(cancellationToken);

        if (tripRouteId is null)
        {
            return ServiceResult<VoucherValidationResponse>.NotFound("Không tìm thấy chuyến xe");
        }

        var voucher = await _db.Vouchers
            .AsNoTracking()
            .FirstOrDefaultAsync(v => v.Code == code, cancellationToken);

        var now = DateTime.UtcNow;
        var reason = ReasonFor(voucher, tripRouteId, orderAmount, now);

        if (reason is not null)
        {
            // Mã không dùng được VẪN là Ok — xem IVoucherValidationService. DiscountAmount = 0 và
            // FinalAmount = orderAmount để FE không phải rẽ nhánh khi hiển thị.
            return ServiceResult<VoucherValidationResponse>.Ok(new VoucherValidationResponse
            {
                Valid = false,
                ReasonCode = reason,
                Message = MessageFor(reason, voucher),
                VoucherId = voucher?.Id,
                Code = voucher?.Code ?? code,
                DiscountType = voucher?.DiscountType.ToString(),
                DiscountAmount = 0m,
                FinalAmount = orderAmount,
                MaxDiscount = voucher?.MaxDiscount,
            });
        }

        var discount = DiscountFor(voucher!, orderAmount);

        return ServiceResult<VoucherValidationResponse>.Ok(new VoucherValidationResponse
        {
            Valid = true,
            ReasonCode = null,
            Message = null,
            VoucherId = voucher!.Id,
            Code = voucher.Code,
            DiscountType = voucher.DiscountType.ToString(),
            DiscountAmount = discount,
            FinalAmount = orderAmount - discount,
            MaxDiscount = voucher.MaxDiscount,
        });
    }

    /// <summary>
    /// Chuẩn hoá mã về đúng hình dạng lưu trong CSDL. Dùng ở MỌI đường đọc/ghi mã voucher — cả
    /// service này lẫn service CRUD của dòng 51.
    /// </summary>
    public static string NormalizeCode(string? code) => (code ?? string.Empty).Trim().ToUpperInvariant();

    /// <summary>
    /// Bộ luật voucher — hàm THUẦN, không chạm CSDL, để test ghim được từng điều kiện và ghim được
    /// ĐÚNG thứ tự (thứ tự là một phần của hợp đồng: câu trả về là điều kiện đầu tiên không thoả).
    ///
    /// Trả <c>null</c> nghĩa là dùng được.
    ///
    /// Thứ tự: nhóm "sự thật tuyệt đối về voucher" (khách sửa gì trong giỏ cũng không cứu được) hỏi
    /// trước, nhóm "phụ thuộc giỏ hàng" hỏi sau. Trong nhóm tuyệt đối, <c>Inactive</c> đứng trước
    /// nhóm thời gian vì đó là quyết định hiện tại của người vận hành; <c>OutOfStock</c> đứng trước
    /// hai điều kiện giỏ hàng vì bảo khách "thêm tiền nữa đi" cho một mã đã hết lượt là câu sai
    /// đường. Trong nhóm giỏ hàng, tuyến trước giá trị đơn: tuyến là dùng được hay không, còn giá
    /// trị đơn là gợi ý khách làm được gì đó.
    ///
    /// <paramref name="tripRouteId"/> là tuyến của chuyến khách đang đặt, đã tra sẵn.
    /// </summary>
    public static string? ReasonFor(Voucher? voucher, Guid? tripRouteId, decimal orderAmount, DateTime now)
    {
        if (voucher is null)
        {
            return VoucherReasonCodes.NotFound;
        }

        if (voucher.Status != VoucherStatus.Active)
        {
            return VoucherReasonCodes.Inactive;
        }

        if (now < voucher.ValidFrom)
        {
            return VoucherReasonCodes.NotStarted;
        }

        // Cả hai đầu mút đều TÍNH LÀ trong hiệu lực (>= ValidFrom, <= ValidUntil) — hợp đồng chốt vậy.
        if (now > voucher.ValidUntil)
        {
            return VoucherReasonCodes.Expired;
        }

        if (voucher.UsedCount >= voucher.Quantity)
        {
            return VoucherReasonCodes.OutOfStock;
        }

        // RouteId null = áp dụng mọi tuyến, nên chỉ so khi voucher CÓ gắn tuyến.
        if (voucher.RouteId is Guid routeId && routeId != tripRouteId)
        {
            return VoucherReasonCodes.WrongRoute;
        }

        if (orderAmount < voucher.MinOrderValue)
        {
            return VoucherReasonCodes.BelowMinOrder;
        }

        return null;
    }

    /// <summary>
    /// Số tiền được giảm — hàm THUẦN, ghim bằng test với số tính tay.
    ///
    /// 🔴 Làm tròn về ĐỒNG NGUYÊN (<c>AwayFromZero</c>), không giữ phần lẻ. VND không có đơn vị lẻ,
    /// và quan trọng hơn: <c>PaymentInitiationRequest.Amount</c> là <c>long</c> nên số gửi lên cổng
    /// buộc phải nguyên. Giữ hai số thập phân ở đây thì số gửi cổng (đã làm tròn) KHÔNG khớp
    /// <c>Payment.Amount</c> trong CSDL, và <c>PaymentSettlementService.SettleAsync</c> — nơi so
    /// <c>payment.Amount != callback.Amount</c> — sẽ trả 404 cho MỌI callback, không giao dịch nào
    /// chốt được. Làm tròn tại đây giữ cho <c>originalAmount - discountAmount == amount</c> đúng
    /// tuyệt đối, tức hoá đơn cộng lại khớp.
    ///
    /// 📌 Dòng <c>Math.Min(discount, orderAmount)</c> là BẮT BUỘC: thiếu nó thì một mã
    /// <c>FixedAmount</c> 50.000đ áp lên đơn 20.000đ cho ra <c>finalAmount = -30.000</c> — số tiền âm
    /// chảy thẳng xuống cổng.
    /// </summary>
    public static decimal DiscountFor(Voucher voucher, decimal orderAmount)
    {
        var discount = voucher.DiscountType == VoucherDiscountType.Percent
            ? decimal.Round(orderAmount * voucher.DiscountValue / 100m, 0, MidpointRounding.AwayFromZero)
            : voucher.DiscountValue;

        // Trần chỉ có nghĩa với kiểu Percent — với FixedAmount thì con số đã cố định, áp thêm trần
        // là để một cấu hình sai (MaxDiscount sót lại) âm thầm vô hiệu hoá voucher.
        if (voucher.DiscountType == VoucherDiscountType.Percent && voucher.MaxDiscount is decimal cap)
        {
            discount = Math.Min(discount, cap);
        }

        return Math.Min(discount, orderAmount);
    }

    /// <summary>
    /// Câu tiếng Việt hiển thị thẳng cho khách. FE nên DỊCH THEO <c>reasonCode</c> chứ đừng so chuỗi
    /// ở đây — câu chữ sẽ còn đổi.
    ///
    /// Số tiền định dạng theo <c>vi-VN</c> ("100.000") chứ không theo culture của máy chủ: câu này
    /// hiển thị cho khách Việt, mà culture máy chủ có thể là en-US ("100,000") — để nguyên thì câu
    /// tiếng Việt lẫn dấu phân cách tiếng Anh.
    /// </summary>
    public static string MessageFor(string reason, Voucher? voucher) => reason switch
    {
        VoucherReasonCodes.NotFound => "Mã voucher không tồn tại",
        VoucherReasonCodes.Inactive => "Mã voucher đã ngừng áp dụng",
        VoucherReasonCodes.NotStarted => "Mã voucher chưa tới thời gian áp dụng",
        VoucherReasonCodes.Expired => "Mã voucher đã hết hiệu lực",
        VoucherReasonCodes.OutOfStock => "Mã voucher đã hết lượt sử dụng",
        VoucherReasonCodes.WrongRoute => "Mã voucher không áp dụng cho tuyến này",
        VoucherReasonCodes.BelowMinOrder =>
            $"Đơn hàng chưa đạt giá trị tối thiểu {voucher!.MinOrderValue.ToString("N0", CultureInfo.GetCultureInfo("vi-VN"))}đ",
        _ => "Mã voucher không dùng được",
    };
}
