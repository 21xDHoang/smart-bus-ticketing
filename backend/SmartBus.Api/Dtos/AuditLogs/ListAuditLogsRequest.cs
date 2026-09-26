using System.ComponentModel.DataAnnotations;

namespace SmartBus.Api.Dtos.AuditLogs;

/// <summary>
/// Tham số lọc + phân trang của GET /api/audit-logs — bind từ query string.
/// Cùng khuôn với <see cref="Routes.ListRoutesRequest"/> và <see cref="Admin.ListAdminUsersRequest"/>.
///
/// Bốn trường lọc ở đây CỐ Ý trùng tên và trùng ngữ nghĩa với <see cref="ExportAuditLogsRequest"/>:
/// hợp đồng đã chốt hai API dùng chung bốn tên <c>from</c>, <c>to</c>, <c>userId</c>, <c>action</c>
/// để frontend không phải nói hai thứ tiếng.
///
/// Nhưng KHÔNG dùng lại chính lớp đó: <c>ExportAuditLogsRequest</c> là file của người khác và
/// đường xuất file không có khái niệm trang. Trùng tên trường là <i>hợp đồng</i>; dùng chung lớp là
/// <i>ràng buộc code</i> — hai chuyện khác nhau, nên hai lớp đứng riêng.
/// </summary>
public class ListAuditLogsRequest
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
    ///
    /// Bản ghi có <c>UserId</c> là <c>null</c> (đăng nhập thất bại với SĐT không tồn tại) KHÔNG
    /// bao giờ khớp tham số này — muốn xem chúng thì để trống bộ lọc người dùng.
    /// </summary>
    public Guid? UserId { get; set; }

    /// <summary>
    /// Chỉ lấy một loại hành động: Login | Logout | LoginFailed | Create | Update | Delete.
    ///
    /// Giá trị không khớp mã nào trả về DANH SÁCH RỖNG chứ không phải 400 — cùng lối
    /// <see cref="Routes.ListRoutesRequest.Status"/>. Đây là <i>mã</i>, mà một mã lạ có thể là
    /// giá trị hợp lệ trong tương lai. Khác <see cref="UserId"/> là <i>định danh</i>: GUID gõ sai
    /// thì trả danh sách rỗng là che mất lỗi.
    /// </summary>
    [StringLength(20, ErrorMessage = "Hành động tối đa 20 ký tự")]
    public string? Action { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Số trang phải từ 1 trở lên")]
    public int? Page { get; set; }

    /// <summary>
    /// Trần 100 nằm ở đây chứ không phải một phép cắt im lặng trong service: vượt trần là request
    /// hỏng → 400, giống hệt <see cref="Routes.ListRoutesRequest.PageSize"/>. Cắt im lặng sẽ tạo ra
    /// hành vi thứ hai khác với hai màn hình danh sách đã có.
    /// </summary>
    [Range(1, 100, ErrorMessage = "Số dòng mỗi trang phải từ 1 đến 100")]
    public int? PageSize { get; set; }
}
