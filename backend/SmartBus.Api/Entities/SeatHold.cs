namespace SmartBus.Api.Entities;

/// <summary>
/// Bảng SeatHolds — một lượt GIỮ GHẾ TẠM THỜI của hành khách trong lúc thanh toán (US 3).
/// Task Sprint 3 "Migrate bảng Seats, SeatHolds, SeatLayouts" (Vàng Thị Dăm).
///
/// Đây là chỗ chống bán trùng ghế ở tầng CSDL: partial unique index <c>(TripId, SeatId)</c> với
/// điều kiện <c>Status = 'Holding'</c> (AppDbContext.Seat.cs) bảo đảm hai khách không cùng giữ một
/// ghế, kể cả khi hai request tới đồng thời. Tầng service không tự lo được việc này — hai request
/// cùng đọc "ghế còn trống" rồi cùng ghi là chuyện xảy ra được.
///
/// Một lượt giữ chỗ là một dòng, không phải một ghế: khách chọn 3 ghế một lượt thì có 3 dòng cùng
/// <see cref="SessionCode"/>. Đó cũng là lý do <see cref="ExpiresAt"/> nằm trên từng dòng chứ
/// không tách bảng "phiên giữ chỗ" riêng — gia hạn là cập nhật cả nhóm theo mã phiên.
/// </summary>
public class SeatHold
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Chuyến mà ghế bị giữ.</summary>
    public Guid TripId { get; set; }

    public Trip? Trip { get; set; }

    /// <summary>Ghế bị giữ.</summary>
    public Guid SeatId { get; set; }

    public Seat? Seat { get; set; }

    /// <summary>
    /// Hành khách giữ ghế — bắt buộc đăng nhập: đặt vé là nghiệp vụ gắn với tài khoản (vé, thanh
    /// toán, huỷ vé đều tra theo <c>Users</c>), không có luồng khách vãng lai.
    /// </summary>
    public Guid UserId { get; set; }

    public User? User { get; set; }

    /// <summary>
    /// Mã phiên giữ chỗ do API giữ ghế sinh ra. Một phiên giữ NHIỀU ghế nên cột này KHÔNG unique —
    /// chỉ đánh index thường. Mọi thao tác theo phiên (tra trạng thái, gia hạn, đếm ngược, nhả cả
    /// phiên) đều hỏi theo mã này.
    /// </summary>
    public string SessionCode { get; set; } = string.Empty;

    public SeatHoldStatus Status { get; set; } = SeatHoldStatus.Holding;

    /// <summary>
    /// Thời điểm hết hạn giữ chỗ — mặc định 10 phút kể từ lúc giữ, gia hạn tối đa 1 lần (US 3).
    /// Job quét của BackgroundService tìm các dòng <c>Status = Holding</c> có cột này đã qua.
    /// </summary>
    public DateTime ExpiresAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Trạng thái đổi (Holding → Expired / Released / Confirmed) nên có UpdatedAt (A4).</summary>
    public DateTime? UpdatedAt { get; set; }
}
