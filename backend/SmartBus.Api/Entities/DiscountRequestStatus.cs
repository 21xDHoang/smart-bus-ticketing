namespace SmartBus.Api.Entities;

/// <summary>
/// Trạng thái DUYỆT một hồ sơ ưu đãi (US 17). Lưu dạng chuỗi trong CSDL (quy ước A3).
///
/// ⚠️ Đây là trạng thái của HỒ SƠ, KHÔNG phải trạng thái HIỆU LỰC của ưu đãi — hai thứ khác
/// nhau. Một hồ sơ <see cref="Approved"/> vẫn có thể đã hết hạn (xem
/// <see cref="DiscountRequest.ExpiresAt"/>). Service áp giá và màn hình thống kê phải hỏi
/// "còn hiệu lực không", không được chỉ hỏi "đã duyệt chưa".
/// </summary>
public enum DiscountRequestStatus
{
    /// <summary>Vừa nộp, chờ HR/Quản lý duyệt. Trạng thái mặc định của hồ sơ mới.</summary>
    Pending,

    /// <summary>Đã duyệt — hành khách được hưởng giá ưu đãi cho tới <c>ExpiresAt</c>.</summary>
    Approved,

    /// <summary>Bị từ chối. Giữ bản ghi làm lịch sử, lý do nằm ở <c>DiscountRequest.RejectReason</c>.</summary>
    Rejected

    // KHÔNG có Expired: hết hạn SUY RA ĐƯỢC từ ExpiresAt < now, không phải trạng thái lưu trữ.
    // Thêm Expired là đẻ ra hai nguồn sự thật — phải có job lật trạng thái đúng hạn, không lật
    // thì dữ liệu sai, và service áp giá phải xử lý cả hai đường (Approved-quá-hạn và Expired).
    //
    // KHÔNG có Cancelled: không story nào cho hành khách rút hồ sơ (A8.3). Nếu sau này có thì
    // thêm một giá trị ở đây là thay đổi không phá vỡ gì — cột lưu varchar chứ không phải
    // Postgres native enum (A3), nên chỉ sửa code, không cần migration phức tạp.
}
