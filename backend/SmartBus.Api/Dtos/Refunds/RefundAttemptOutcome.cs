using SmartBus.Api.Entities;

namespace SmartBus.Api.Dtos.Refunds;

/// <summary>
/// Kết quả MỘT lượt thực hiện gọi cổng hoàn tiền mà <c>RefundAttemptService</c> trả về cho người
/// gọi (job thử lại theo lịch — task dòng 30; nút "Thử lại" của màn Admin sau này). Đây là DTO nội
/// bộ để người gọi ghi log/tổng hợp, KHÔNG phải hình dạng trả cho khách qua HTTP (task này không
/// mở endpoint — xem docs/35 §6).
/// </summary>
public class RefundAttemptOutcome
{
    /// <summary>Khoá khoản hoàn. Khi khoản không tồn tại, đây là id mà người gọi đưa vào.</summary>
    public Guid RefundId { get; set; }

    /// <summary>Mã khoản hoàn dạng <c>RF-xxxxxxxx</c>; rỗng khi khoản không tồn tại (không có gì để đọc).</summary>
    public string RefundCode { get; set; } = string.Empty;

    /// <summary>
    /// <c>true</c> ⟺ lượt này ĐÃ giành được dòng và có gọi cổng (kể cả khi cổng trả
    /// <c>Unresolved</c>). <c>false</c> = không tìm thấy khoản / không đủ điều kiện với
    /// <c>trigger</c> / thua cuộc đua <c>xmin</c> — chưa có lời gọi nào đi ra ngoài.
    /// </summary>
    public bool Claimed { get; set; }

    /// <summary>
    /// Lời cổng trả về — <c>null</c> khi <see cref="Claimed"/> là <c>false</c> (không gọi thì không
    /// có kết quả để nói).
    /// </summary>
    public RefundLogResult? Result { get; set; }

    /// <summary>
    /// Câu chữ để người gọi ghi log: với lượt thất bại là lý do ĐÃ CHUẨN HOÁ ghi vào
    /// <c>Refund.FailReason</c> (kèm chuỗi dự phòng khi cổng không nêu lý do); các lượt khác là lời
    /// cổng nguyên bản. Không dùng để hiển thị thẳng cho khách.
    /// </summary>
    public string? Message { get; set; }

    /// <summary>
    /// Lịch thử lại vừa được ghi cho khoản — chỉ khác <c>null</c> khi lượt vừa rồi thất bại và còn
    /// nấc thang (người gọi ghi log "sẽ thử lại lúc …"). <c>null</c> khi đã hết lịch, khi lượt
    /// thành công, hoặc khi không giành được dòng.
    /// </summary>
    public DateTime? NextRetryAt { get; set; }
}
