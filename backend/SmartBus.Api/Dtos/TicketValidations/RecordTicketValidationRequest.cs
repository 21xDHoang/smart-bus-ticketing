using SmartBus.Api.Entities;

namespace SmartBus.Api.Dtos.TicketValidations;

/// <summary>
/// Tham số của <see cref="Services.ITicketValidationRecorder.RecordAsync"/> — một LƯỢT SOÁT cần ghi vào
/// nhật ký <c>TicketValidations</c>. Task dòng 18 *"Xử lý ngoại tuyến: lưu tạm lượt soát khi mất mạng,
/// đồng bộ lại sau"* (Phùng Duy Hoàng), thuộc US 15 — bàn giao đầy đủ ở
/// <c>docs/39-hang-doi-ngoai-tuyen-soat-ve.md</c>.
///
/// ⚠️ Đây KHÔNG phải body JSON của một endpoint — nó là tham số C# giữa endpoint soát vé (dòng 15,
/// Trần Trung Hiếu) và service ghi nhật ký. Không có <c>[Required]</c>/<c>[Range]</c> ở đây vì không
/// có model binding nào chạy qua: mọi phép kiểm nằm ở tầng service, nơi trả về
/// <c>ServiceResult.Invalid</c> kèm lỗi theo trường.
///
/// 🔴 Kết quả <see cref="Result"/> do BÊN GỌI tính (endpoint soát vé đã xác thực chữ ký và tra vé
/// xong mới gọi), không phải service này tự phán. Với hàng đợi ngoại tuyến, kết quả được tính ở THỜI
/// ĐIỂM ĐỒNG BỘ — máy quét xếp hàng lúc mất mạng, lúc nối lại mới có câu trả lời; xem docs/39 §5.
/// </summary>
public class RecordTicketValidationRequest
{
    /// <summary>
    /// NGUYÊN VĂN chuỗi máy quét đọc được — chép thẳng vào <see cref="TicketValidation.ScannedCode"/>
    /// (cột là bản ghi điều tra, không phải khoá tra cứu). Service từ chối chuỗi rỗng/toàn khoảng
    /// trắng và chuỗi dài quá 200 ký tự (trần cột <c>varchar(200)</c>; để lọt là Postgres ném 22001
    /// thành 500).
    /// </summary>
    public string ScannedCode { get; init; } = string.Empty;

    /// <summary>
    /// Vé mà chữ ký QR trỏ tới, sau khi endpoint đã xác thực. Null ⟺ <see cref="Result"/> là
    /// <see cref="TicketValidationResult.NotFound"/> — bất biến của bảng (xem doc-comment
    /// <see cref="TicketValidation.TicketId"/>); service trả 400 nếu hai vế lệch nhau, vì tổ hợp lệch
    /// là lỗi lập trình của bên gọi chứ không phải dữ liệu người dùng.
    /// </summary>
    public Guid? TicketId { get; init; }

    /// <summary>
    /// Chuyến phụ xe ĐANG CHẠY lúc quét — nguồn duy nhất kết luận được <c>WrongTrip</c>. Tuỳ chọn theo
    /// hợp đồng endpoint (docs/29 §3). Service chép thẳng, không kiểm.
    /// </summary>
    public Guid? TripId { get; init; }

    /// <summary>
    /// Kết quả endpoint đã tính. Không có giá trị mặc định hợp lệ nào để lấy — xem ⚠️ ở
    /// <see cref="TicketValidationResult"/>: enum cố ý KHÔNG có <c>Valid</c> ở vị trí 0, nên lượt ghi
    /// quên gán lộ ra thành một lượt bị từ chối thay vì im lặng thành hợp lệ.
    /// </summary>
    public TicketValidationResult Result { get; init; }

    /// <summary>
    /// Mã lượt soát do CHÍNH MÁY QUÉT sinh lúc quét (dòng 18) — khoá chống trùng khi đồng bộ lại.
    ///
    /// Null = lượt online không cần chống trùng (gửi thẳng một lần) — cùng lý do cột nullable ở
    /// <see cref="TicketValidation.ClientScanId"/>. Có giá trị = gửi lại cùng mã này là chuyện bình
    /// thường và KHÔNG bao giờ được đẻ thêm dòng hay nổ 500.
    ///
    /// ⚠️ Máy quét phải sinh mã này NGAY LÚC QUÉT, không phải lúc gửi: sinh lúc gửi thì mỗi lần thử
    /// lại là một mã mới và tính chống trùng biến mất — xem docs/39 §3.
    /// </summary>
    public Guid? ClientScanId { get; init; }

    /// <summary>
    /// Giờ MÁY QUÉT ghi nhận lượt soát — khác giờ dòng tới server (<c>CreatedAt</c>). Null = lấy giờ
    /// server (lượt online). Có giá trị = giờ thiết bị khai, service kiểm khoảng hợp lý (không quá 5
    /// phút tương lai, không quá 72 giờ quá khứ — docs/34 §4.2) trước khi nhận.
    /// </summary>
    public DateTime? ScannedAt { get; init; }

    /// <summary>
    /// Trạm khách thực sự lên xe, khi phụ xe có ghi — task dòng 17 (Nguyễn Duy Kiên) lo phần nghiệp vụ
    /// quyết định giá trị; ở đây chỉ là đường chuyển tiếp để hàng đợi ngoại tuyến mang được trường này
    /// qua cùng lượt gửi, không kiểm gì.
    /// </summary>
    public Guid? BoardingStopId { get; init; }
}
