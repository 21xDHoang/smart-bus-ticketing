namespace SmartBus.Api.Entities;

/// <summary>
/// Bảng DiscountRequests — hồ sơ xin hưởng giá vé ưu đãi (US 17): hành khách nộp ảnh thẻ
/// minh chứng, HR/Quản lý duyệt hoặc từ chối.
/// Task migrate bảng này là của Vàng Thị Dăm (story 17).
///
/// Bảng riêng chứ KHÔNG thêm cột vào Users (A9 #13): một hành khách có thể nộp nhiều hồ sơ theo
/// thời gian (bị từ chối rồi nộp lại, hoặc gia hạn thẻ), mà Users chỉ giữ được một trạng thái.
/// Vì vậy "đang hưởng ưu đãi gì" luôn phải suy ra từ hồ sơ, không đọc từ Users.
/// </summary>
public class DiscountRequest
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Hành khách nộp hồ sơ.</summary>
    public Guid UserId { get; set; }

    public User? User { get; set; }

    /// <summary>
    /// Đối tượng ưu đãi xin hưởng. Dùng lại enum <see cref="PassengerType"/> sẵn có của bảng Fares
    /// thay vì đẻ enum mới: giá trị phải khớp đúng khoá tra bảng giá, lệch một chữ là tra không ra.
    /// Lưu dạng chuỗi trong CSDL — xem quy ước A3.
    ///
    /// US 17 chỉ nói tới học sinh/sinh viên (<see cref="PassengerType.Student"/>) và người cao
    /// tuổi (<see cref="PassengerType.Senior"/>). Enum rộng hơn thế là cố ý — chặn các giá trị
    /// còn lại là việc validate ở tầng service, không phải ràng buộc CSDL.
    /// </summary>
    public PassengerType PassengerType { get; set; }

    /// <summary>
    /// Ảnh thẻ minh chứng (thẻ học sinh/sinh viên, CCCD...). Nullable vì hồ sơ có thể được tạo
    /// trước khi ảnh tải xong.
    ///
    /// Cột để kiểu <c>text</c> chứ không <c>varchar(500)</c>: nếu nơi lưu ảnh trả về signed URL
    /// thì URL đó dài 300–800+ ký tự vì chứa cả token, varchar(500) sẽ tràn cột.
    /// Tầng service quyết định lưu đường dẫn object hay URL đầy đủ — schema chứa được cả hai.
    /// </summary>
    public string? EvidenceUrl { get; set; }

    /// <summary>
    /// Trạng thái duyệt hồ sơ — xem <see cref="DiscountRequestStatus"/>.
    /// Hồ sơ mới luôn bắt đầu ở Pending, nên có giá trị mặc định.
    /// </summary>
    public DiscountRequestStatus Status { get; set; } = DiscountRequestStatus.Pending;

    /// <summary>
    /// Ngày hết hạn của thẻ minh chứng, do người duyệt nhập lúc approve. Nullable vì:
    /// hồ sơ chưa duyệt thì chưa có mốc này, và người cao tuổi không có thẻ hết hạn — job
    /// "nhắc hết hạn thẻ ưu đãi" trong backlog ghi rõ chỉ áp dụng cho học sinh/sinh viên.
    /// </summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>
    /// HR/Quản lý đã duyệt hồ sơ. Nullable vì hồ sơ Pending chưa có người duyệt — để NOT NULL
    /// thì mọi hồ sơ mới đều không lưu được.
    ///
    /// ⚠️ Tên này LỆCH khuôn A2 (<c>&lt;TênEntity&gt;Id</c>): bảng có HAI khoá ngoại cùng trỏ
    /// Users nên buộc phải đặt tên phân biệt. Ngoại lệ có chủ ý, không phải đặt bừa.
    /// </summary>
    public Guid? ReviewedByUserId { get; set; }

    public User? ReviewedBy { get; set; }

    /// <summary>Thời điểm duyệt/từ chối. Tách khỏi <see cref="UpdatedAt"/> vì đó là mốc nghiệp vụ.</summary>
    public DateTime? ReviewedAt { get; set; }

    /// <summary>Lý do từ chối — mô tả dài nên dùng text (A3). Chỉ có nghĩa khi Status là Rejected.</summary>
    public string? RejectReason { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Hồ sơ có sửa dữ liệu (đổi trạng thái) nên có UpdatedAt (A4).</summary>
    public DateTime? UpdatedAt { get; set; }
}
