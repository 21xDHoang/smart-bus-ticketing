namespace SmartBus.Api.Entities;

/// <summary>
/// Vòng đời một vé điện tử (US 4 — "Vé điện tử QR"). Lưu dạng chuỗi trong CSDL (quy ước A3).
///
/// Ba giá trị, đúng bảng trường của <c>docs/api-contract.md</c> mục "Vé điện tử" và đúng type
/// <c>TicketStatus</c> mà frontend đã viết ở <c>frontend/src/api/ticketApi.ts</c>:
/// <c>Paid</c> lúc phát hành → <c>Used</c> khi soát → hoặc <c>Cancelled</c> khi huỷ.
///
/// ⚠️ Vì sao KHÔNG có <c>Held</c>. Chỉ mục chống bán trùng ghế của A6 lọc
/// <c>WHERE "Status" IN ('Held', 'Paid')</c> (xem AppDbContext.Ticket.cs), và
/// <c>TripSeatMapService</c> cũng ghi chú sẽ hỏi <c>Status = 'Paid'/'Held'</c> — nhưng KHÔNG nơi nào
/// phát hành một vé ở trạng thái <c>Held</c>: việc giữ ghế là của bảng <c>SeatHolds</c>
/// (<see cref="SeatHoldStatus.Holding"/>), còn vé chỉ sinh ra SAU khi tiền về và sinh thẳng ở
/// <c>Paid</c> (docs/api-contract.md, mục "Phát hành vé sau khi thanh toán").
///
/// Chốt 10/10/2026: giữ nguyên chữ của A6 trong điều kiện chỉ mục (nhánh <c>'Held'</c> thành dự
/// phòng, không dòng nào mang giá trị đó nên vô hại, và nếu sau này nhóm cho vé sinh ngay từ lúc
/// giữ chỗ thì chỉ mục đã sẵn sàng), nhưng enum chỉ có ba giá trị — thêm <c>Held</c> vào đây sẽ là
/// tự mở rộng một tập giá trị đã chốt với frontend.
/// </summary>
public enum TicketStatus
{
    /// <summary>
    /// Đã thanh toán — vé hợp lệ, chưa soát. Đây là trạng thái vé sinh ra ở bước phát hành
    /// (docs/api-contract.md bước 2: "tạo Ticket với <c>status = 'Paid'</c>").
    /// </summary>
    Paid,

    /// <summary>
    /// Đã soát vé (QR được quét qua cổng) — kèm <see cref="Ticket.UsedAt"/>. Vé đã dùng không soát
    /// lại được, và cũng không trả ghế về chợ.
    /// </summary>
    Used,

    /// <summary>
    /// Đã huỷ (hoàn tiền / khách huỷ). Đây là giá trị DUY NHẤT được phép trùng <c>(TripId, SeatId)</c>
    /// với một vé khác — vé huỷ không còn chiếm ghế, xem điều kiện chỉ mục trong AppDbContext.Ticket.cs.
    /// </summary>
    Cancelled
}
