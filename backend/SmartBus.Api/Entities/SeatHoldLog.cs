namespace SmartBus.Api.Entities;

/// <summary>
/// Bảng SeatHoldLogs — nhật ký vòng đời của từng lượt giữ ghế (US 3 "Giữ chỗ tạm thời").
/// Task *"Migrate bảng SeatHoldLogs + job quét hold hết hạn"* — Vàng Thị Dăm.
///
/// <para>
/// Vì sao cần bảng log riêng khi <see cref="SeatHold.Status"/> đã nói lên trạng thái: cột Status chỉ
/// giữ trạng thái CUỐI, còn câu hỏi của nghiệp vụ là QUÁ TRÌNH — "tài khoản này đã để hết hạn giữ
/// chỗ bao nhiêu lần", "lượt giữ này được gia hạn lúc nào". Ghi đè lên một cột thì trả lời được
/// trạng thái hiện tại, không trả lời được lịch sử.
/// </para>
///
/// <para>
/// Bảng CHỈ GHI THÊM: không có UpdatedAt (quy ước A4 — cùng lối <see cref="AuditLog"/>) và không
/// sửa/xoá bản ghi. Sửa được nhật ký thì nhật ký mất giá trị.
/// </para>
/// </summary>
public class SeatHoldLog
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Lượt giữ ghế mà sự kiện này thuộc về.</summary>
    public Guid SeatHoldId { get; set; }

    public SeatHold? SeatHold { get; set; }

    /// <summary>
    /// Hành khách của lượt giữ — chép lại từ <see cref="SeatHold.UserId"/>.
    ///
    /// Cố ý KHÔNG suy ra qua <see cref="SeatHold"/>: câu hỏi "tài khoản này giữ chỗ quá nhiều lần
    /// chưa" (task *"Ghi log và cảnh báo khi một tài khoản giữ chỗ quá nhiều lần"*) đếm thẳng trên
    /// bảng này theo chỉ mục <c>(UserId, CreatedAt)</c> thay vì join sang SeatHolds mỗi lần. Đây
    /// cũng là lối của <see cref="AuditLog.UserId"/>: bảng nhật ký ghi lại người thực hiện tại thời
    /// điểm sự kiện để đứng đọc được một mình.
    ///
    /// Khác <see cref="AuditLog.UserId"/> ở chỗ KHÔNG nullable: giữ ghế bắt buộc đăng nhập
    /// (US 3), không có lượt giữ nào của khách vãng lai để mà mất dấu người thực hiện.
    /// </summary>
    public Guid UserId { get; set; }

    public User? User { get; set; }

    /// <summary>
    /// Mã phiên giữ chỗ, chép từ <see cref="SeatHold.SessionCode"/> — cùng lý do với
    /// <see cref="UserId"/>: tra vết trọn một phiên (khách chọn 3 ghế là 3 lượt giữ) đọc thẳng bảng
    /// này, không phải join.
    /// </summary>
    public string SessionCode { get; set; } = string.Empty;

    /// <summary>Loại sự kiện. Lưu dạng chuỗi trong CSDL — quy ước A3.</summary>
    public SeatHoldLogAction Action { get; set; }

    /// <summary>Thời điểm sự kiện xảy ra (UTC) — cột "thời gian" của bảng nhật ký.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
