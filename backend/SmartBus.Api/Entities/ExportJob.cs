namespace SmartBus.Api.Entities;

/// <summary>
/// Một yêu cầu xuất báo cáo chạy ở NỀN — task *"Xử lý xuất file lớn bất đồng bộ + thông báo khi
/// xong"* (US 24 "Xuất dữ liệu", Sprint 5 dòng 64, Vàng Thị Dăm).
///
/// Vì sao phải là bảng chứ không phải hàng đợi trong bộ nhớ: file lớn nhất của báo cáo lấp đầy /
/// nhật ký mất hàng chục giây tới vài phút để dựng. Giữ request HTTP mở suốt thời gian đó là bắt
/// trình duyệt chờ, dễ vượt timeout của proxy, và không có chỗ nào để báo "xong rồi" sau khi người
/// dùng đã đóng tab. Một dòng trong bảng là thứ duy nhất còn sống sau khi app restart — hàng đợi
/// trong RAM sẽ làm mất yêu cầu mà người dùng đã được hứa là đang xử lý.
///
/// Bảng này KHÔNG phải nguồn số liệu báo cáo. Nó chỉ giữ yêu cầu, trạng thái và đúng một file đã
/// dựng xong; số liệu nằm ở bảng nghiệp vụ và file chỉ là bản sao tại thời điểm dựng.
///
/// <para>
/// <b>Vì sao file nằm trong CSDL (<see cref="Content"/>) thay vì đĩa:</b> app chạy trên hosting có
/// ổ đĩa tạm không bền và có thể có nhiều instance; một file trên đĩa của instance A không tải được
/// từ instance B. Cột <c>bytea</c> giữ mọi thứ trong cùng nơi đã có sẵn cơ chế sao lưu, và job tự
/// hết hạn sau 24 giờ nên bảng không phình vô hạn. Đánh đổi đã biết: mỗi file chiếm băng thông
/// CSDL khi tải; chấp nhận được vì đây là thao tác thưa, không phải đường nóng.
/// </para>
/// </summary>
public class ExportJob
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Người yêu cầu xuất. Chỉ chủ job (và không ai khác) đọc/tải được file của nó.</summary>
    public Guid UserId { get; set; }

    public User? User { get; set; }

    /// <summary>
    /// Họ tên người yêu cầu tại THỜI ĐIỂM đặt job — ghi vào khối tự mô tả của file. Cố ý chép lại
    /// thay vì join <see cref="User"/> lúc dựng file: người dùng đổi tên sau đó thì chứng từ đã
    /// xuất phải giữ đúng người đã bấm nút, giống cách <c>AuditLog</c> đóng băng dữ liệu tại chỗ.
    /// </summary>
    public string RequestedByName { get; set; } = string.Empty;

    /// <summary>Loại báo cáo — xem <see cref="ExportReportType"/>.</summary>
    public ExportReportType ReportType { get; set; }

    /// <summary>
    /// <c>xlsx</c> hoặc <c>pdf</c>, đã chuẩn hoá về chữ thường. Giữ chuỗi thay vì enum để cột CSDL
    /// đọc được trực tiếp khi tra job hỏng, cùng tinh thần quy ước A3.
    /// </summary>
    public string Format { get; set; } = "xlsx";

    /// <summary>Trạng thái hiện tại — xem <see cref="ExportJobStatus"/>.</summary>
    public ExportJobStatus Status { get; set; } = ExportJobStatus.Pending;

    /// <summary>
    /// Ảnh chụp bộ lọc của yêu cầu, dạng JSON (xem <c>ExportJobParameters</c>). Đóng băng tham số
    /// ngay lúc đặt job: người dùng sửa bộ lọc trên màn hình trong lúc job đang chạy không được làm
    /// thay đổi nội dung file đã hứa.
    /// </summary>
    public string ParametersJson { get; set; } = "{}";

    /// <summary>Tên file ASCII không dấu, chỉ có sau khi job <c>Succeeded</c>.</summary>
    public string? FileName { get; set; }

    /// <summary>MIME thật của <see cref="Content"/>.</summary>
    public string? ContentType { get; set; }

    /// <summary>Toàn bộ bytes của file — <c>bytea</c>. Null khi job chưa xong hoặc đã thất bại.</summary>
    public byte[]? Content { get; set; }

    /// <summary>Kích thước file, để màn danh sách hiển thị mà không phải tải cả cột <see cref="Content"/>.</summary>
    public long? FileSizeBytes { get; set; }

    /// <summary>
    /// Số dòng dữ liệu trong file (không tính hàng tiêu đề). Null khi chưa xong, hoặc với báo cáo
    /// doanh thu — bản xuất doanh thu tối đa 366 bucket nên con số này không có giá trị vận hành.
    /// </summary>
    public int? RowCount { get; set; }

    /// <summary>
    /// Lý do thất bại, viết cho người dùng đọc. Bị cắt còn 1.000 ký tự ở tầng cấu hình — thông báo
    /// của báo cáo gốc vốn ngắn, cắt chỉ để một lỗi bất thường không làm vỡ cột.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Số lần job đã được job nền nhận. Dùng để chặn vòng lặp job độc: app chết giữa lúc dựng file
    /// ba lần thì lần thứ tư đánh <c>Failed</c> thay vì thử mãi.
    /// </summary>
    public int AttemptCount { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Sửa đổi gần nhất (UTC) — quy ước A4: bảng có sửa thì có <c>UpdatedAt</c> nullable.</summary>
    public DateTime? UpdatedAt { get; set; }

    /// <summary>Lúc job nền bắt đầu dựng file. Null khi còn <c>Pending</c>.</summary>
    public DateTime? StartedAt { get; set; }

    /// <summary>Lúc job chốt kết quả (thành công hoặc thất bại). Null khi còn chạy.</summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// Hạn tải file. Sau mốc này job nền xoá cả dòng lẫn file — file báo cáo là bản sao dữ liệu
    /// nghiệp vụ, không phải chứng từ phải lưu vĩnh viễn, và giữ mọi file mãi mãi là mở rộng bảng
    /// theo cấp số nhân. Job <c>Failed</c> cũng có hạn để thông báo lỗi không nằm lại quá lâu.
    /// </summary>
    public DateTime? ExpiresAt { get; set; }
}
