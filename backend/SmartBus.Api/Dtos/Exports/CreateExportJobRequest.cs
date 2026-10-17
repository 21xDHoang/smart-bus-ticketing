using System.ComponentModel.DataAnnotations;

namespace SmartBus.Api.Dtos.Exports;

/// <summary>
/// Body của <c>POST /api/exports</c> — đặt một lượt xuất báo cáo chạy ở nền. Hợp đồng đầy đủ ở
/// mục "Xuất file bất đồng bộ — /exports" của docs/api-contract.md.
///
/// Một lớp duy nhất cho cả ba loại báo cáo thay vì ba endpoint: ba bản xuất đồng bộ dùng chung bốn
/// tham số lọc (<c>from</c>/<c>to</c>/<c>routeId</c> + một tham số riêng), còn phần khác nhau chỉ là
/// vài trường nullable. Tách ba endpoint sẽ nhân ba lần phần đặt job / phân quyền / trả 202 mà
/// không thêm thông tin gì cho frontend.
///
/// Trường nào dùng cho loại nào:
/// <list type="table">
/// <item><term><c>revenue</c></term><description><c>granularity</c>, <c>from</c>, <c>to</c>, <c>routeId</c></description></item>
/// <item><term><c>fill-rate</c></term><description><c>from</c>, <c>to</c>, <c>routeId</c>, <c>timeSlot</c></description></item>
/// <item><term><c>audit-logs</c></term><description><c>from</c>, <c>to</c>, <c>userId</c>, <c>action</c></description></item>
/// </list>
///
/// Trường không thuộc loại đã chọn bị BỎ QUA (không phải lỗi): frontend dùng chung một form bộ lọc
/// sẽ gửi thừa trường, và từ chối vì thế là bắt FE phải lọc theo loại — một ràng buộc không đem lại
/// an toàn nào, vì trường thừa không làm thay đổi nội dung file.
/// </summary>
public class CreateExportJobRequest
{
    /// <summary>
    /// <c>revenue</c> / <c>fill-rate</c> / <c>audit-logs</c> (không phân biệt hoa/thường).
    /// Giá trị khác → 400 gắn vào <c>errors.reportType</c>.
    /// </summary>
    public string? ReportType { get; set; }

    /// <summary>
    /// <c>xlsx</c> (mặc định) hoặc <c>pdf</c>, không phân biệt hoa/thường. Giá trị khác → 400 gắn
    /// vào <c>errors.format</c>.
    /// </summary>
    public string? Format { get; set; }

    /// <summary>Độ mịn của báo cáo doanh thu: <c>day</c> / <c>week</c> / <c>month</c>. Trống = <c>day</c>.</summary>
    public string? Granularity { get; set; }

    /// <summary>
    /// Ngày bắt đầu, tính cả. Doanh thu/lấp đầy hiểu theo LỊCH VIỆT NAM; nhật ký hiểu theo UTC —
    /// đúng như ba bản xuất đồng bộ.
    /// </summary>
    public DateOnly? From { get; set; }

    /// <summary>Ngày kết thúc, tính cả. Cùng múi giờ với <see cref="From"/> theo từng loại báo cáo.</summary>
    public DateOnly? To { get; set; }

    /// <summary>Lọc theo tuyến (doanh thu, lấp đầy). Không tồn tại → 404 ngay khi đặt job.</summary>
    public Guid? RouteId { get; set; }

    /// <summary>
    /// Khung giờ khởi hành của báo cáo lấp đầy: <c>early</c> / <c>morning</c> / <c>afternoon</c> /
    /// <c>evening</c> / <c>night</c>. Trống = mọi khung; giá trị lạ → 400 <c>errors.timeSlot</c>.
    /// </summary>
    public string? TimeSlot { get; set; }

    /// <summary>
    /// Người thao tác cần lọc của nhật ký kiểm toán. GUID sai định dạng do model binding bắt → 400
    /// <c>errors.userId</c>.
    /// </summary>
    public Guid? UserId { get; set; }

    /// <summary>
    /// Một mã hành động của nhật ký: <c>Login</c> / <c>Logout</c> / <c>LoginFailed</c> /
    /// <c>Create</c> / <c>Update</c> / <c>Delete</c>. Mã lạ cho ra file rỗng chứ không phải lỗi —
    /// cùng lối bản xuất đồng bộ.
    /// </summary>
    [StringLength(20, ErrorMessage = "Hành động tối đa 20 ký tự")]
    public string? Action { get; set; }
}
