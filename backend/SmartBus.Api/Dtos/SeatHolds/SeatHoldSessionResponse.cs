namespace SmartBus.Api.Dtos.SeatHolds;

/// <summary>
/// Một phiên giữ chỗ nhìn từ phía hành khách — hình dạng trả về CHUNG của bề mặt api/seat-holds
/// (US 3 "Giữ chỗ tạm thời"). Hợp đồng đầy đủ ở mục "Giữ chỗ — /seat-holds" của docs/api-contract.md.
///
/// Một phiên giữ NHIỀU ghế = nhiều dòng SeatHold cùng <c>SessionCode</c> (cột KHÔNG unique —
/// docs/26-csdl-so-do-ghe.md §1), nên đối tượng trả về gom các ghế lại thành một PHIÊN chứ không
/// phải một dòng: mọi thao tác (tra trạng thái, gia hạn, đếm ngược, nhả cả phiên) đều hỏi theo
/// <c>SessionCode</c>. Ba endpoint còn lại của bề mặt (giữ ghế, nhả ghế, gia hạn) cũng trả về đúng
/// hình dạng này khi task của chúng hoàn thành.
/// </summary>
public class SeatHoldSessionResponse
{
    /// <summary>Mã phiên do API giữ ghế sinh ra — khoá của mọi thao tác theo phiên.</summary>
    public string SessionCode { get; set; } = string.Empty;

    /// <summary>Chuyến đang giữ ghế.</summary>
    public Guid TripId { get; set; }

    /// <summary>Số ghế đang giữ ("A1", "T2-A1") xếp theo toạ độ sơ đồ — chỉ để hiển thị cho hành khách.</summary>
    public List<string> SeatNumbers { get; set; } = [];

    /// <summary>
    /// "Holding" / "Confirmed" / "Expired" / "Released" — giá trị cột <c>SeatHolds.Status</c>
    /// (<see cref="Entities.SeatHoldStatus"/> ghi chuỗi, quy ước A3).
    /// </summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>Hạn giữ chỗ (UTC). Mọi dòng cùng phiên cùng hạn — docs/26 §1.</summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>
    /// Còn lượt gia hạn không — màn hình dùng để bật/tắt nút "Gia hạn". Suy từ SeatHoldLogs: chốt
    /// "tối đa 1 lần" của US 3 nằm ở unique index (SeatHoldId, Action), không có cột đếm trên SeatHolds.
    /// </summary>
    public bool CanExtend { get; set; }
}
