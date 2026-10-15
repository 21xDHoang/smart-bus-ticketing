using Microsoft.EntityFrameworkCore;
using SmartBus.Api.Data;
using SmartBus.Api.Dtos.TicketValidations;
using SmartBus.Api.Entities;

namespace SmartBus.Api.Services;

/// <summary>
/// Cài đặt <see cref="ITicketValidationRecorder"/> — nhiệm vụ dòng 18 (Phùng Duy Hoàng). Hợp đồng đầy
/// đủ ở doc-comment của interface; ở đây ghi lại BA QUYẾT ĐỊNH cài đặt mà người đọc code cần biết,
/// kèm lý do — mọi thứ khác đọc thẳng theo luồng.
///
/// <para>
/// <b>1. Chống trùng bằng ĐỌC TRƯỚC, không bằng phân tích mã lỗi.</b> Đường gửi lại bình thường (tuần
/// tự) được chặn ngay ở lượt đọc đầu; đường đua hiếm (hai lượt thử chồng nhau, cùng
/// <c>ClientScanId</c>) rơi vào <c>DbUpdateException</c> và được nhận diện bằng cách ĐỌC LẠI theo
/// <c>ClientScanId</c> — chứ không parse SqlState 23505 của Postgres (mã đó không tồn tại trên SQLite
/// của tầng test, và khoá cứng vào nhà cung cấp CSDL là thứ dự án này tránh).
/// </para>
///
/// <para>
/// <b>2. Đọc-trước nằm TRƯỚC mọi phép kiểm trường.</b> Cố ý: một lượt đã ghi rồi thì gửi lại luôn là
/// thành công, không xét lại gì. Nếu kiểm <c>ScannedAt</c> trước, một hàng đợi kẹt 4 ngày gửi lại
/// lượt đã đồng bộ thành công từ trước sẽ bị trả 400 oan — đúng thứ §6 của docs/34 cấm.
/// </para>
///
/// <para>
/// <b>3. Nuốt lỗi unique, KHÔNG nuốt lỗi khác.</b> <c>DbUpdateException</c> mà đọc lại không thấy
/// <c>ClientScanId</c> nào (ví dụ <c>TripId</c> trỏ tới chuyến không tồn tại — SQLite/Postgres đều chặn
/// khoá ngoại) được NÉM LẠI: đó là lỗi lập trình của bên gọi, phải ồn ào. Rows bẩn do lượt SaveChanges
/// hỏng được gỡ khỏi ChangeTracker trước khi ném, để context dùng chung của request không mang theo
/// một entity Added mồ côi sẽ nổ lại ở lần SaveChanges sau.
/// </para>
/// </summary>
public class TicketValidationRecorder : ITicketValidationRecorder
{
    private const string EmptyScannedCodeMessage = "Mã QR không được để trống.";
    private const string ScannedCodeTooLongMessage = "Mã QR quá dài (tối đa 200 ký tự).";
    private const string NotFoundWithTicketMessage =
        "Lượt soát mang mã vé nhưng kết quả là NotFound — chỉ lượt không đọc ra vé mới mang kết quả này.";
    private const string MissingTicketMessage =
        "Lượt soát có kết quả khác NotFound nhưng thiếu mã vé.";
    private const string ScannedAtTooFarFutureMessage =
        "Giờ quét nằm ở tương lai quá 5 phút — nhiều khả năng đồng hồ máy quét bị sai.";
    private const string ScannedAtTooOldMessage =
        "Lượt soát đã quá cũ (hơn 72 giờ) — hàng đợi ngoại tuyến để chờ quá lâu, không đồng bộ được nữa.";

    /// <summary>
    /// Dung sai đồng hồ máy quét nhanh hơn server. 5 phút đủ rộng cho lệch đồng hồ thông thường mà vẫn
    /// bắt được máy sai giờ hàng tuần — cố định, không cấu hình: đổi con số này là đổi hợp đồng với
    /// hàng đợi ngoại tuyến (docs/39 §3), không phải đổi vận hành.
    /// </summary>
    private static readonly TimeSpan MaxFutureSkew = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Trần tuổi của một lượt soát ngoại tuyến. Chọn 72 giờ vì hàng đợi được thiết kế để chờ "hàng
    /// giờ, thậm chí hôm sau" (doc-comment <see cref="TicketValidation.ScannedAt"/>) — 72 giờ phủ trọn
    /// một cuối tuần mất mạng. Lượt cũ hơn bị TỪ CHỐI chứ không ghi với giờ bịa: thống kê theo chuyến
    /// (dòng 24) đọc <c>ScannedAt</c>, nhận một mốc sai còn tệ hơn mất một dòng.
    /// </summary>
    private static readonly TimeSpan MaxQueueAge = TimeSpan.FromHours(72);

    private readonly AppDbContext _db;

    public TicketValidationRecorder(AppDbContext db)
    {
        _db = db;
    }

    /// <inheritdoc />
    public async Task<ServiceResult<TicketValidation>> RecordAsync(
        RecordTicketValidationRequest request,
        Guid validatedByUserId,
        CancellationToken ct)
    {
        // (1) Gửi lại — đọc trước mọi phép kiểm, xem quyết định 2 ở doc-comment lớp.
        if (request.ClientScanId is { } clientScanId)
        {
            var daGhi = await TimTheoClientScanIdAsync(clientScanId, ct);
            if (daGhi is not null)
            {
                return ServiceResult<TicketValidation>.Ok(daGhi);
            }
        }

        // (2) Kiểm trường của lượt MỚI.
        if (string.IsNullOrWhiteSpace(request.ScannedCode))
        {
            return Invalid("scannedCode", EmptyScannedCodeMessage);
        }

        // Trần cột varchar(200): để lọt xuống CSDL là Postgres ném 22001 → 500, còn SQLite của tầng
        // test thì không chặn gì — phép kiểm phải nằm ở đây để hai môi trường cư xử như nhau.
        if (request.ScannedCode.Length > 200)
        {
            return Invalid("scannedCode", ScannedCodeTooLongMessage);
        }

        // Bất biến của bảng: TicketId IS NULL ⟺ Result = 'NotFound' (cả hai chiều). Lệch là lỗi lập
        // trình của bên gọi — trả 400 kèm trường để lộ ra ngay, không âm thầm ghi dòng mâu thuẫn.
        if (request.Result == TicketValidationResult.NotFound && request.TicketId is not null)
        {
            return Invalid("ticketId", NotFoundWithTicketMessage);
        }

        if (request.Result != TicketValidationResult.NotFound && request.TicketId is null)
        {
            return Invalid("ticketId", MissingTicketMessage);
        }

        var now = DateTime.UtcNow;
        var scannedAt = request.ScannedAt is { } raw ? NormalizeToUtc(raw) : now;

        if (scannedAt > now + MaxFutureSkew)
        {
            return Invalid("scannedAt", ScannedAtTooFarFutureMessage);
        }

        if (scannedAt < now - MaxQueueAge)
        {
            return Invalid("scannedAt", ScannedAtTooOldMessage);
        }

        // (3) Ghi. Một lần SaveChanges = một transaction, nên không có cửa sổ nào để hai lượt cùng
        // ClientScanId lọt qua chỉ mục UNIQUE (AppDbContext.TicketValidation.cs §4.1).
        var luotSoat = new TicketValidation
        {
            TicketId = request.TicketId,
            TripId = request.TripId,
            ValidatedByUserId = validatedByUserId,
            BoardingStopId = request.BoardingStopId,
            ScannedCode = request.ScannedCode,
            Result = request.Result,
            ScannedAt = scannedAt,
            ClientScanId = request.ClientScanId,
            CreatedAt = now,
        };

        _db.TicketValidations.Add(luotSoat);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Gỡ entity Added mồ côi TRƯỚC mọi việc khác: DbContext là scoped dùng chung cả request,
            // để nguyên thì lần SaveChanges kế tiếp của endpoint (ví dụ đánh dấu vé Used) sẽ nổ lại
            // đúng lỗi này và lần này không còn ai hiểu vì sao.
            _db.Entry(luotSoat).State = EntityState.Detached;

            // Đua hiếm: lượt song sinh cùng ClientScanId đã kịp ghi. Đọc lại là cách nhận diện duy
            // nhất không khoá cứng vào nhà cung cấp CSDL — xem quyết định 1.
            if (request.ClientScanId is { } clientScanIdLap)
            {
                var daGhi = await TimTheoClientScanIdAsync(clientScanIdLap, ct);
                if (daGhi is not null)
                {
                    return ServiceResult<TicketValidation>.Ok(daGhi);
                }
            }

            // Không phải đua ClientScanId (khoá ngoại hỏng, cột quá dài…) — lỗi thật, phải ồn ào.
            throw;
        }

        return ServiceResult<TicketValidation>.Ok(luotSoat);
    }

    /// <summary>
    /// Đọc lại một lượt theo <c>ClientScanId</c>. <c>AsNoTracking</c> bắt buộc: lượt vừa ghi hỏng vẫn
    /// còn trong ChangeTracker cho tới lúc bị gỡ, và identity resolution sẽ trả về chính nó thay vì
    /// dòng thật dưới CSDL.
    /// </summary>
    private Task<TicketValidation?> TimTheoClientScanIdAsync(Guid clientScanId, CancellationToken ct)
        => _db.TicketValidations
            .AsNoTracking()
            .FirstOrDefaultAsync(v => v.ClientScanId == clientScanId, ct);

    /// <summary>
    /// Đưa mọi kiểu <see cref="DateTime"/> về UTC trước khi lưu — Npgsql chỉ ghi được
    /// <c>DateTimeKind.Utc</c>, gặp Unspecified/Local là ném ngay lúc SaveChanges (500).
    ///
    /// Ba nhánh, ba lý do khác nhau:
    /// <list type="bullet">
    ///   <item><c>Utc</c> — giữ nguyên (System.Text.Json trả kiểu này cho chuỗi ISO kết thúc bằng
    ///   <c>Z</c>, tức là mọi client JS gửi <c>toISOString()</c>).</item>
    ///   <item><c>Local</c> — ĐỔI GIỜ thật bằng <c>ToUniversalTime</c>, không dán nhãn: System.Text.Json
    ///   trả kiểu này cho chuỗi ISO mang offset (ví dụ <c>+07:00</c> của máy quét trong nước), dán
    ///   nhãn Utc lên đó là lệch đúng 7 tiếng. Khác lối
    ///   <c>Ticket.ReportPdfModel</c> (dán nhãn cho cả Unspecified lẫn Local) một cách CÓ Ý: ở đó dữ
    ///   liệu là nội bộ server, ở đây là JSON qua dây.</item>
    ///   <item><c>Unspecified</c> — không có thông tin vùng, dán nhãn Utc theo quy ước dự án: hợp đồng
    ///   api-contract quy định mọi mốc thời gian là UTC, client gửi thiếu hậu tố bị coi là đã theo hợp
    ///   đồng.</item>
    /// </list>
    /// </summary>
    private static DateTime NormalizeToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };

    /// <summary>
    /// Lỗi 400 kèm đúng một trường — cùng cấu trúc <c>{ message, errors }</c> của mọi endpoint (docs/03
    /// mục D3). Trường để màn quét đọc được lượt nào là lỗi KHÔNG THỂ thử lại (docs/39 §3).
    /// </summary>
    private static ServiceResult<TicketValidation> Invalid(string field, string message)
        => ServiceResult<TicketValidation>.Invalid(
            message, new Dictionary<string, string[]> { [field] = [message] });
}
