using System.ComponentModel.DataAnnotations;

namespace SmartBus.Api.Dtos.AuditLogs;

/// <summary>
/// Tham số lọc của GET /api/audit-logs/export — bind từ query string.
/// Cùng khuôn với <see cref="Routes.ListRoutesRequest"/> và <see cref="Admin.ListAdminUsersRequest"/>.
///
/// Bốn tên trường ở đây đã chốt trong docs/api-contract.md và API truy vấn danh sách
/// (GET /api/audit-logs) phải dùng đúng bốn tên này — frontend không nên phải nói hai thứ tiếng.
/// </summary>
public class ExportAuditLogsRequest
{
    /// <summary>
    /// Ngày bắt đầu, tính TRỌN ngày theo UTC. Bỏ trống = 29 ngày trước hôm nay (UTC).
    ///
    /// Cố ý dùng <see cref="DateOnly"/> chứ không <c>DateTime</c>: binder của DateTime gặp chuỗi
    /// có <c>Z</c> sẽ quy về giờ máy chủ và trả <c>Kind = Local</c> — trên Npgsql tình cờ vẫn
    /// đúng, nhưng test chạy trên provider InMemory thì so sánh là so DateTime trần, nên kết quả
    /// phụ thuộc múi giờ máy chạy (máy dev UTC+7 khác CI UTC). DateOnly xoá sạch bẫy đó.
    /// </summary>
    public DateOnly? From { get; set; }

    /// <summary>Ngày kết thúc, tính TRỌN ngày theo UTC. Bỏ trống = hôm nay (UTC).</summary>
    public DateOnly? To { get; set; }

    /// <summary>
    /// Chỉ lấy thao tác của một người.
    ///
    /// GUID sai định dạng là request hỏng → <b>400</b>, khác lối "giá trị lạ trả rỗng" của
    /// <see cref="Action"/> — xem chú thích ở đó.
    /// </summary>
    public Guid? UserId { get; set; }

    /// <summary>
    /// Chỉ lấy một loại hành động: Login | Logout | LoginFailed | Create | Update | Delete.
    ///
    /// Giá trị không khớp mã nào trả về FILE RỖNG chứ không phải 400 — cùng lối
    /// <see cref="Routes.ListRoutesRequest.Status"/>. Đây là <i>mã</i>, mà một mã lạ có thể là
    /// giá trị hợp lệ trong tương lai. Khác <see cref="UserId"/> là <i>định danh</i>: GUID gõ sai
    /// thì trả file rỗng là che mất lỗi.
    /// </summary>
    [StringLength(20, ErrorMessage = "Hành động tối đa 20 ký tự")]
    public string? Action { get; set; }
}
