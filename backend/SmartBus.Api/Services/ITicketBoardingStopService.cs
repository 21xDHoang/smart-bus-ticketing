using SmartBus.Api.Entities;

namespace SmartBus.Api.Services;

/// <summary>
/// Quyết định giá trị <c>TicketValidations.BoardingStopId</c> cho một lượt soát — task *"API ghi nhận
/// lượt lên xe theo trạm"* (US 15, dòng 17 Sprint 4 — Nguyễn Duy Kiên). Đặc tả cột ở
/// <c>docs/34-csdl-soat-ve-qr.md</c> §2; hợp đồng endpoint ở mục "Soát vé QR" của
/// <c>docs/api-contract.md</c>.
///
/// Ba luật, theo đúng thứ tự service xét:
///
/// 1. <b>Chỉ lượt <see cref="TicketValidationResult.Valid"/> mới là một lượt lên xe.</b> Lượt bị từ
///    chối (<c>Cancelled</c>, <c>AlreadyUsed</c>, <c>WrongTrip</c>, <c>Expired</c>, <c>NotFound</c>)
///    luôn trả <c>null</c> — request có gửi trạm cũng bỏ qua, không phải lỗi.
/// 2. <b>Trạm phải thuộc tuyến của chuyến ghi trên vé</b> (<c>RouteStops</c>). Không so với
///    <c>Tickets.BoardingStopId</c>: đó là trạm khách CHỌN LÚC MUA, còn cột này là trạm khách THỰC SỰ
///    bước lên (A8.1; docs/34 §2.1). Trạm không thuộc tuyến → lỗi theo trường <c>boardingStopId</c>.
/// 3. <b>Không gửi trạm = không ghi gì</b> (<c>null</c>) — trường tuỳ chọn, phụ xe có thể chỉ quét vé.
///
/// Hàm trả <c>ServiceResult&lt;Guid?&gt;</c>: <c>Data</c> là giá trị để chép vào nhật ký (hoặc
/// <c>null</c>), <c>Invalid</c> là lỗi phía gọi. Endpoint soát vé gọi hàm này SAU khi đã tính
/// <c>Result</c> và TRƯỚC <see cref="ITicketValidationRecorder.RecordAsync"/>, rồi chuyển
/// <c>Data</c> vào <c>RecordTicketValidationRequest.BoardingStopId</c>.
/// </summary>
public interface ITicketBoardingStopService
{
    /// <summary>
    /// Trả trạm lên xe sẽ ghi vào nhật ký, hoặc lỗi nếu trạm không thuộc tuyến của chuyến.
    /// </summary>
    /// <param name="requestedStopId">
    /// Trạm phụ xe khai lúc quét; <c>null</c> = không khai. Không kiểm sự tồn tại của dòng
    /// <c>Stops</c> riêng — là thành viên <c>RouteStops</c> đã bao hàm điều đó (khoá ngoại
    /// <c>RouteStops.StopId → Stops</c>).
    /// </param>
    /// <param name="ticketTripId">
    /// Chuyến ghi trên vé (<c>Tickets.TripId</c>), KHÔNG phải chuyến phụ xe khai trong request — khi
    /// hai giá trị lệch nhau thì kết quả đã là <c>WrongTrip</c> và trạm bị bỏ qua.
    /// </param>
    /// <param name="result">Kết quả soát endpoint vừa tính; chỉ <c>Valid</c> mới có trạm.</param>
    Task<ServiceResult<Guid?>> ResolveAsync(
        Guid? requestedStopId,
        Guid? ticketTripId,
        TicketValidationResult result,
        CancellationToken cancellationToken = default);
}
