using Microsoft.EntityFrameworkCore;
using SmartBus.Api.Data;
using SmartBus.Api.Dtos.Vouchers;
using SmartBus.Api.Entities;

namespace SmartBus.Api.Services;

/// <summary>
/// Cài đặt <see cref="IVoucherRedemptionService"/> — phần "áp dụng" của dòng 52 (US 18, Nguyễn Duy
/// Kiên). Hợp đồng đầy đủ ở mục "Voucher — /vouchers" của docs/api-contract.md.
///
/// Tiêu thụ một voucher = ghi một dòng <see cref="VoucherUsage"/> + tăng <c>Vouchers.UsedCount</c>,
/// cả hai trong MỘT <c>SaveChangesAsync</c>: hỏng là hỏng cả lượt, không để lại dòng usage mà bộ
/// đếm không nhích (hoặc ngược lại) — hai bên lệch nhau thì bảng thống kê của dòng 54 nói dối.
///
/// Chống tiêu thụ quá <c>Quantity</c> có HAI lớp, cùng lối chống trùng ghế của
/// <see cref="SeatHoldCreateService"/>:
///   1. Phép kiểm <c>UsedCount &gt;= Quantity</c> trong <see cref="VoucherValidationService.ReasonFor"/>
///      — ca thường gặp, và là lớp duy nhất bộ test chứng minh được.
///   2. Concurrency token <c>xmin</c> của <c>Vouchers</c> (AppDbContext.Voucher.cs) — lớp chặn thật
///      cho hai lượt tiêu thụ chạy song song: cả hai cùng đọc thấy còn lượt rồi cùng ghi, lượt thua
///      nhận <see cref="DbUpdateConcurrencyException"/>.
///      ⚠️ Provider InMemory của bộ test BỎ QUA token, nên nhánh này chỉ chạy ở PostgreSQL — nền
///      tảng nào cũng vậy: test xanh KHÔNG chứng minh được lớp 2.
///
/// Chốt idempotency là unique index trên <c>VoucherUsage.PaymentCode</c>: hai callback trùng của
/// cổng chạy song song thì lượt thua đâm vào index và nhận <see cref="DbUpdateException"/> — lúc đó
/// không phải lỗi, mà chính là bằng chứng bản ghi đã có, nên được đọc lại và trả như một bản gửi lại.
/// </summary>
public class VoucherRedemptionService : IVoucherRedemptionService
{
    private const string MissingPaymentCodeMessage = "Mã giao dịch không được để trống";
    private const string TripNotFoundMessage = "Không tìm thấy chuyến xe";
    private const string VoucherNotFoundMessage = "Mã voucher không tồn tại";

    /// <summary>
    /// Hai lượt tiêu thụ đua nhau (concurrency token) hoặc hai callback trùng đua nhau mà lượt thắng
    /// lại KHÔNG để lại dòng usage nào để đọc lại. Câu chung, không kèm chi tiết kỹ thuật — người gọi
    /// chỉ ghi log rồi trả 204 cho cổng, không sửa được gì từ phía này.
    /// </summary>
    private const string RaceMessage = "Mã voucher vừa được sử dụng, vui lòng thử lại";

    private readonly AppDbContext _db;

    public VoucherRedemptionService(AppDbContext db) => _db = db;

    public async Task<ServiceResult<VoucherRedemptionResult>> RedeemAsync(
        VoucherRedemptionRequest request, CancellationToken cancellationToken = default)
    {
        // PaymentCode là chốt idempotency, không phải dữ liệu hiển thị: thiếu nó thì lượt gọi này
        // không có cách nào phân biệt "lần đầu" với "bản gửi lại", và mọi callback trùng sẽ trừ thêm
        // lượt. Đây là lỗi lập trình ở phía gọi, chặn ngay chứ không đoán hộ.
        if (string.IsNullOrWhiteSpace(request.PaymentCode))
        {
            return ServiceResult<VoucherRedemptionResult>.Invalid(
                MissingPaymentCodeMessage,
                new Dictionary<string, string[]> { ["paymentCode"] = [MissingPaymentCodeMessage] });
        }

        var paymentCode = request.PaymentCode.Trim();

        // ── Chốt idempotency, hỏi TRƯỚC mọi thứ khác ──
        // Đặt trước cả phép kiểm "có mã voucher không" là cố ý: một bản ghi đã có nghĩa là lượt này
        // chắc chắn là bản gửi lại, và câu trả lời đúng nằm ở dòng đã ghi chứ không phải ở tham số
        // của lượt gọi mới (cổng có thể gửi lại thiếu trường, còn số tiền thật là số đã chốt).
        var existing = await FindRedeemedAsync(paymentCode, cancellationToken);

        if (existing is not null)
        {
            return ServiceResult<VoucherRedemptionResult>.Ok(existing);
        }

        var code = VoucherValidationService.NormalizeCode(request.VoucherCode);

        // Giao dịch không dùng mã: thành công rỗng, KHÔNG ghi dòng VoucherUsage nào. Nhờ nhánh này mà
        // luồng thanh toán gọi được vô điều kiện, không phải tự nhớ "chỉ gọi khi có voucherCode" —
        // một điều kiện nhớ hộ là một chỗ để quên.
        if (code.Length == 0)
        {
            return ServiceResult<VoucherRedemptionResult>.Ok(new VoucherRedemptionResult
            {
                VoucherId = Guid.Empty,
                VoucherCode = string.Empty,
                OrderAmount = request.OrderAmount,
                DiscountAmount = 0m,
                AlreadyRedeemed = false,
            });
        }

        // ── Tuyến của chuyến: cần cho điều kiện "tuyến" của dòng 53. Ép sang Guid? để phân biệt
        //    "không có chuyến" với "chuyến có tuyến" (cùng lối VoucherValidationService). ──
        var tripRouteId = await _db.Trips
            .Where(t => t.Id == request.TripId)
            .Select(t => (Guid?)t.RouteId)
            .FirstOrDefaultAsync(cancellationToken);

        if (tripRouteId is null)
        {
            return ServiceResult<VoucherRedemptionResult>.NotFound(TripNotFoundMessage);
        }

        // Theo dõi (KHÔNG AsNoTracking): lượt này còn phải tăng UsedCount trên chính thực thể này.
        var voucher = await _db.Vouchers
            .FirstOrDefaultAsync(v => v.Code == code, cancellationToken);

        if (voucher is null)
        {
            return ServiceResult<VoucherRedemptionResult>.NotFound(VoucherNotFoundMessage);
        }

        // ── Kiểm LẠI từ đầu trên số tiền thật của giao dịch, không tin kết quả validate khách đã
        //    thấy lúc bấm nút: giữa hai thời điểm mã có thể đã hết lượt hoặc hết hạn. ──
        // MỘT mốc now cho cả phép kiểm lẫn CreatedAt/UpdatedAt — hai mốc lệch vài mili giây trong
        // cùng một lượt là thứ không giải thích được lúc tra vết (cùng lối SeatHoldCreateService).
        var now = DateTime.UtcNow;
        var reason = VoucherValidationService.ReasonFor(voucher, tripRouteId, request.OrderAmount, now);

        if (reason is not null)
        {
            // Voucher không còn dùng được vào đúng lúc tiền về: Conflict chứ không phải Invalid —
            // dữ liệu gửi lên không sai, trạng thái voucher mới là thứ đã đổi. Người gọi ghi log rồi
            // vẫn trả 204 cho cổng; tiền đã về thì không có gì để cổng gửi lại.
            return ServiceResult<VoucherRedemptionResult>.Conflict(
                VoucherValidationService.MessageFor(reason, voucher));
        }

        var discount = VoucherValidationService.DiscountFor(voucher, request.OrderAmount);

        var usage = new VoucherUsage
        {
            VoucherId = voucher.Id,
            UserId = request.UserId,
            PaymentCode = paymentCode,
            OrderAmount = request.OrderAmount,
            DiscountAmount = discount,
            CreatedAt = now,
            // Bảng ghi thêm: không có UpdatedAt (A4) — dòng này không bao giờ sửa.
        };

        _db.VoucherUsages.Add(usage);

        voucher.UsedCount += 1;
        voucher.UpdatedAt = now;

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Hai lượt tiêu thụ song song cùng đọc thấy còn lượt; lượt thua làm xmin đổi và nhận lỗi
            // ở đây. Gỡ thay đổi dở rồi trả 409 — nhánh này chỉ chạy ở PostgreSQL.
            Detach(usage, voucher);
            return ServiceResult<VoucherRedemptionResult>.Conflict(RaceMessage);
        }
        catch (DbUpdateException)
        {
            // Hai callback trùng chạy song song cùng đâm vào unique index PaymentCode. Lượt thua
            // KHÔNG phải lỗi: bản ghi đã có nghĩa là bên thắng vừa ghi xong — đọc lại và trả như một
            // bản gửi lại, đúng cùng câu trả lời mà nhánh idempotency ở đầu hàm đã trả.
            Detach(usage, voucher);

            var winner = await FindRedeemedAsync(paymentCode, cancellationToken);

            return winner is null
                ? ServiceResult<VoucherRedemptionResult>.Conflict(RaceMessage)
                : ServiceResult<VoucherRedemptionResult>.Ok(winner);
        }

        return ServiceResult<VoucherRedemptionResult>.Ok(new VoucherRedemptionResult
        {
            VoucherId = voucher.Id,
            VoucherCode = voucher.Code,
            OrderAmount = request.OrderAmount,
            DiscountAmount = discount,
            AlreadyRedeemed = false,
        });
    }

    /// <summary>
    /// Bản ghi tiêu thụ đã có của một mã giao dịch, đọc thẳng ra hình dạng trả về —
    /// <c>null</c> nghĩa là giao dịch này chưa tiêu thụ voucher nào.
    ///
    /// Mã voucher lấy qua quan hệ <c>Voucher</c> chứ không lưu lại trong <c>VoucherUsage</c>: hợp
    /// đồng không có cột <c>voucherCode</c> (xem bảng "Entity VoucherUsage"), và FK <c>Restrict</c>
    /// bảo đảm voucher còn nguyên nên phép nối này không bao giờ hụt.
    /// </summary>
    private async Task<VoucherRedemptionResult?> FindRedeemedAsync(
        string paymentCode, CancellationToken cancellationToken)
        => await _db.VoucherUsages
            .AsNoTracking()
            .Where(u => u.PaymentCode == paymentCode)
            .Select(u => new VoucherRedemptionResult
            {
                VoucherId = u.VoucherId,
                VoucherCode = u.Voucher!.Code,
                OrderAmount = u.OrderAmount,
                DiscountAmount = u.DiscountAmount,
                AlreadyRedeemed = true,
            })
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>
    /// Gỡ hai thực thể của riêng lượt này khỏi change tracker sau một <c>SaveChangesAsync</c> hỏng.
    ///
    /// Không dùng <c>ChangeTracker.Clear()</c>: luồng thanh toán gọi service này trên CÙNG DbContext
    /// theo phạm vi request, và lúc đó nó có thể đang giữ thay đổi chưa lưu của chính nó — xoá sạch
    /// tracker là xoá luôn việc của người gọi. Chỉ gỡ đúng phần mình vừa thêm, để thay đổi dở của
    /// lượt này không lọt vào lần <c>SaveChanges</c> sau của người gọi.
    /// </summary>
    private void Detach(VoucherUsage usage, Voucher voucher)
    {
        _db.Entry(usage).State = EntityState.Detached;
        _db.Entry(voucher).State = EntityState.Detached;
    }
}
