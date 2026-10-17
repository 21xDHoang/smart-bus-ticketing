using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SmartBus.Api.Data;
using SmartBus.Api.Dtos.Payments;
using SmartBus.Api.Dtos.Vouchers;
using SmartBus.Api.Entities;

namespace SmartBus.Api.Services;

/// <summary>
/// Cài đặt <see cref="IPaymentService"/> — tạo giao dịch thanh toán và tra trạng thái (US 6, task
/// *"Tạo giao dịch + gọi cổng"* — Trần Trung Hiếu). Hợp đồng đầy đủ ở mục "Thanh toán — /payments"
/// của docs/api-contract.md.
///
/// Luồng tạo giao dịch, theo đúng hợp đồng:
/// <list type="number">
/// <item>Kiểm mã phương thức (400 nếu chưa mở, kèm danh sách cổng đã mở); tra adapter qua
/// <see cref="IPaymentGatewayResolver"/> — thiếu cấu hình thì adapter ném ngay lúc dựng, nêu đúng
/// tên khoá (lỗi triển khai → 502, khác hẳn "chưa mở");</item>
/// <item>So khớp <c>total</c> với giá ghế tính lại từ <c>Fares</c> (400 theo trường total) và kiểm
/// ghế thuộc xe của chuyến (400 theo trường seatNumbers) — FE gửi được gì thì người ngoài cũng gửi
/// được nấy, không tin con số nào từ FE;</item>
/// <item>Kiểm lại voucher từ đầu (409 theo trường voucherCode nếu không dùng được) và chặn số tiền
/// tụt dưới sàn của cổng MoMo (10.000đ — 409 theo trường voucherCode, đúng chữ hợp đồng);</item>
/// <item>Lưu <c>Payment</c> Pending TRƯỚC rồi mới gọi cổng: callback đến giữa chừng vẫn thấy bản ghi;
/// cổng từ chối hoặc mạng chết thì ném lên để controller trả 502 — bản ghi Pending nằm lại cho job
/// đối soát hỏi cổng và chốt Failed.</item>
/// </list>
/// </summary>
public class PaymentService : IPaymentService
{
    /// <summary>Sàn của MoMo — tài liệu cổng: chỉ nhận giao dịch từ 10.000đ. Cố ý để hằng số (hợp đồng đã chốt).</summary>
    private const decimal MoMoMinimumAmount = 10_000m;

    /// <summary>Số lần thử lại khi mã giao dịch sinh ra đâm trúng chỉ mục unique (xác suất gần bằng 0).</summary>
    private const int PaymentCodeMaxAttempts = 3;

    private readonly AppDbContext _db;
    private readonly IPaymentGatewayResolver _resolver;
    private readonly IVoucherValidationService _voucherValidation;
    private readonly PaymentOptions _options;

    public PaymentService(
        AppDbContext db,
        IPaymentGatewayResolver resolver,
        IVoucherValidationService voucherValidation,
        IOptions<PaymentOptions> options)
    {
        _db = db;
        _resolver = resolver;
        _voucherValidation = voucherValidation;
        _options = options.Value;
    }

    /// <inheritdoc />
    public async Task<ServiceResult<CreatePaymentResult>> CreateAsync(
        Guid userId,
        CreatePaymentRequest request,
        string ipnUrl,
        string ipAddress,
        CancellationToken cancellationToken = default)
    {
        // Mã ngoài bốn mã đã mở → 400 kèm danh sách cổng mở — khác hẳn "có adapter nhưng thiếu cấu
        // hình" (lỗi triển khai, ném lên khi dựng adapter → 502).
        if (!_resolver.TryGet(request.MethodCode!, out var gateway))
        {
            return ServiceResult<CreatePaymentResult>.Invalid(
                $"Phương thức thanh toán '{request.MethodCode}' chưa mở",
                new Dictionary<string, string[]>
                {
                    ["methodCode"] = [$"Phương thức thanh toán '{request.MethodCode}' chưa mở"],
                });
        }

        // Mã khớp không phân biệt hoa thường (vnpay = VNPay) — lưu đúng mã chuẩn hoá của adapter.
        var methodCode = gateway.ProviderCode;

        var trip = await _db.Trips
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == request.TripId, cancellationToken);

        if (trip is null)
        {
            return ServiceResult<CreatePaymentResult>.NotFound("Không tìm thấy chuyến xe.");
        }

        var seatNumbers = request.SeatNumbers!
            .Select(n => n.Trim())
            .Where(n => n.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (seatNumbers.Count != request.SeatNumbers!.Count)
        {
            return ServiceResult<CreatePaymentResult>.Invalid(
                "Danh sách ghế không hợp lệ",
                new Dictionary<string, string[]> { ["seatNumbers"] = ["Danh sách ghế không hợp lệ (trống hoặc trùng)"] });
        }

        // Giá ghế tính LẠI từ Fares — cùng nguồn và cùng nghĩa với price của GET /trips/search và
        // sơ đồ ghế (PassengerType.Standard). Tuyến chưa cấu hình giá là trạng thái dữ liệu chưa
        // sẵn sàng bán — chặn rõ chứ không để cổng thu một con số tự bịa.
        var pricePerSeat = await _db.Fares
            .AsNoTracking()
            .Where(f => f.RouteId == trip.RouteId && f.PassengerType == PassengerType.Standard)
            .Select(f => (decimal?)f.Price)
            .FirstOrDefaultAsync(cancellationToken);

        if (pricePerSeat is null)
        {
            return ServiceResult<CreatePaymentResult>.Invalid(
                "Tuyến chưa cấu hình giá vé",
                new Dictionary<string, string[]> { ["total"] = ["Tuyến chưa cấu hình giá vé"] });
        }

        var expectedTotal = pricePerSeat.Value * seatNumbers.Count;
        if (request.Total != expectedTotal)
        {
            return ServiceResult<CreatePaymentResult>.Invalid(
                "Tổng tiền không khớp tổng giá các ghế đã chọn",
                new Dictionary<string, string[]> { ["total"] = ["Tổng tiền không khớp tổng giá các ghế đã chọn"] });
        }

        // Ghế phải thuộc xe của chuyến — phát hành vé sau này tra SeatId từ chính danh sách này.
        var seatCount = await _db.Seats
            .AsNoTracking()
            .CountAsync(s => s.BusId == trip.BusId && seatNumbers.Contains(s.SeatNumber), cancellationToken);

        if (seatCount != seatNumbers.Count)
        {
            return ServiceResult<CreatePaymentResult>.Invalid(
                "Ghế không thuộc xe của chuyến đã chọn",
                new Dictionary<string, string[]> { ["seatNumbers"] = ["Ghế không thuộc xe của chuyến đã chọn"] });
        }

        // Voucher: kiểm lại từ đầu, không tin discountAmount của FE. "Mã không dùng được" ở đây là
        // LỖI CHẶN (409) chứ không phải câu trả lời như POST /vouchers/validate — khách đã bấm xác
        // nhận, âm thầm thu đủ tiền là lỗi nặng nhất của luồng này.
        decimal discount = 0m;
        if (!string.IsNullOrWhiteSpace(request.VoucherCode))
        {
            var validation = await _voucherValidation.ValidateAsync(
                new ValidateVoucherRequest
                {
                    Code = request.VoucherCode,
                    TripId = request.TripId,
                    OrderAmount = request.Total,
                },
                cancellationToken);

            if (!validation.Success || validation.Data is null)
            {
                // Trip không tồn tại là ca duy nhất ValidateAsync trả lỗi — đã chặn ở trên; rơi vào
                // đây là ca không ngờ tới, trả lỗi chung để không thu tiền trên dữ liệu chưa kiểm.
                return ServiceResult<CreatePaymentResult>.Invalid(
                    "Không kiểm được mã giảm giá",
                    new Dictionary<string, string[]> { ["voucherCode"] = ["Không kiểm được mã giảm giá"] });
            }

            if (!validation.Data.Valid)
            {
                return ServiceResult<CreatePaymentResult>.Conflict(
                    validation.Data.Message ?? "Mã giảm giá không dùng được",
                    new Dictionary<string, string[]> { ["voucherCode"] = [validation.Data.Message ?? "Mã giảm giá không dùng được"] });
            }

            discount = validation.Data.DiscountAmount;
        }

        var amount = request.Total!.Value - discount;

        // Sàn của cổng là ràng buộc của CỔNG, không phải của voucher: mã giảm làm số tiền tụt dưới
        // sàn thì cổng từ chối với một lỗi khó hiểu — chặn trước bằng 409 kèm lời rõ (hợp đồng).
        if (discount > 0 && methodCode == PaymentProviderCodes.MoMo && amount < MoMoMinimumAmount)
        {
            return ServiceResult<CreatePaymentResult>.Conflict(
                "Mã giảm quá nhiều, số tiền còn lại dưới mức tối thiểu của cổng",
                new Dictionary<string, string[]>
                {
                    ["voucherCode"] = ["Mã giảm quá nhiều, số tiền còn lại dưới mức tối thiểu của cổng"],
                });
        }

        // Cả ba cổng chỉ nhận VND nguyên — phần lẻ nghĩa là dữ liệu giá bất thường, chặn trước khi
        // (long)amount cắt lén đồng lẻ.
        if (amount != Math.Truncate(amount))
        {
            return ServiceResult<CreatePaymentResult>.Invalid(
                "Số tiền thanh toán có phần lẻ — các cổng chỉ nhận VND nguyên",
                new Dictionary<string, string[]> { ["total"] = ["Số tiền thanh toán có phần lẻ"] });
        }

        var routeCode = await _db.Routes
            .AsNoTracking()
            .Where(r => r.Id == trip.RouteId)
            .Select(r => r.Code)
            .FirstOrDefaultAsync(cancellationToken);

        // Lưu giao dịch Pending TRƯỚC khi gọi cổng: callback (IPN) có thể bay tới ngay giây sau —
        // phải thấy bản ghi để chốt. paymentCode là khoá chống trùng (unique index) — trùng thì
        // sinh mã mới rồi thử lại (xác suất gần bằng 0, nhưng không được để nổi thành 500).
        var paymentCode = await SavePendingPaymentAsync(
            userId, trip.Id, seatNumbers, amount, methodCode, cancellationToken);

        var initiation = await gateway.InitiateAsync(
            new PaymentInitiationRequest
            {
                PaymentCode = paymentCode,
                Amount = (long)amount,
                OrderInfo = $"Ve xe buyt tuyen {routeCode}",
                ReturnUrl = $"{_options.FrontendBaseUrl.TrimEnd('/')}{PaymentOptions.WaitingPagePath}?paymentCode={paymentCode}",
                IpnUrl = ipnUrl,
                // IP khách gọi lên — chỉ VNPay dùng (vnp_IpAddr bắt buộc, cổng ghi log chống gian lận).
                IpAddress = ipAddress,
            },
            cancellationToken);

        // Cổng từ chối (MoMo resultCode != 0) hoặc không trả lời: ném lên để controller trả 502 kèm
        // lời cổng. Bản ghi Pending nằm lại — job đối soát hỏi cổng rồi chốt Failed.
        if (!initiation.Success)
        {
            throw new InvalidOperationException(
                initiation.Message.Length > 0
                    ? initiation.Message
                    : "Cổng thanh toán từ chối tạo giao dịch.");
        }

        return ServiceResult<CreatePaymentResult>.Ok(new CreatePaymentResult
        {
            PaymentCode = paymentCode,
            OriginalAmount = request.Total.Value,
            DiscountAmount = discount,
            VoucherCode = string.IsNullOrWhiteSpace(request.VoucherCode) ? null : request.VoucherCode,
            Amount = amount,
            Method = methodCode,
            Status = PaymentStatus.Pending.ToString(),
            PayUrl = initiation.RedirectUrl,
        });
    }

    /// <inheritdoc />
    public async Task<ServiceResult<PaymentStatusResponse>> GetStatusAsync(
        Guid userId,
        string paymentCode,
        CancellationToken cancellationToken = default)
    {
        var payment = await _db.Payments
            .AsNoTracking()
            .FirstOrDefaultAsync(
                p => p.PaymentCode == paymentCode && p.UserId == userId,
                cancellationToken);

        // Giao dịch của người khác và giao dịch không tồn tại cùng một câu 404 — không để lộ giao
        // dịch nào đang tồn tại (cùng lối /seat-holds/{sessionCode}).
        if (payment is null)
        {
            return ServiceResult<PaymentStatusResponse>.NotFound("Không tìm thấy giao dịch thanh toán.");
        }

        return ServiceResult<PaymentStatusResponse>.Ok(new PaymentStatusResponse
        {
            PaymentCode = payment.PaymentCode,
            Amount = payment.Amount,
            Method = payment.MethodCode,
            Status = payment.Status.ToString(),
            PaidAt = payment.PaidAt,
            GatewayTransactionId = payment.GatewayTransactionId,
            Message = payment.Message,
        });
    }

    /// <summary>
    /// Ghi dòng Payment Pending rồi trả paymentCode. Trùng mã (đâm unique index) thì sinh mã mới và
    /// thử lại, tối đa <see cref="PaymentCodeMaxAttempts"/> lần.
    /// </summary>
    private async Task<string> SavePendingPaymentAsync(
        Guid userId,
        Guid tripId,
        List<string> seatNumbers,
        decimal amount,
        string methodCode,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            var paymentCode = $"PM-{Guid.NewGuid().ToString("N")[..8]}";

            _db.Payments.Add(new Payment
            {
                PaymentCode = paymentCode,
                MethodCode = methodCode,
                Amount = amount,
                Status = PaymentStatus.Pending,
                UserId = userId,
                TripId = tripId,
                SeatNumbers = string.Join(';', seatNumbers),
            });

            try
            {
                await _db.SaveChangesAsync(cancellationToken);
                return paymentCode;
            }
            catch (DbUpdateException) when (attempt < PaymentCodeMaxAttempts)
            {
                // Trùng mã giao dịch (xác suất ~2^-32 mỗi lần sinh) — bỏ dòng đang theo dõi rồi sinh
                // mã mới thử lại; hết số lần thử thì để lỗi nổi lên thành 500 (không thể tự vá).
                _db.ChangeTracker.Clear();
            }
        }
    }
}
