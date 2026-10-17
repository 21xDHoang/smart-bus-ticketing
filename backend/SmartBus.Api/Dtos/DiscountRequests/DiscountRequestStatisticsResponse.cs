namespace SmartBus.Api.Dtos.DiscountRequests;

/// <summary>
/// Thống kê số lượng hồ sơ ưu đãi theo trạng thái — GET /api/discount-requests/statistics.
/// Task *"API thống kê số lượng hồ sơ ưu đãi theo trạng thái"* (Sprint 4 dòng 55, US 17 —
/// Vàng Thị Dăm). Hợp đồng đầy đủ ở mục "Hồ sơ ưu đãi — /discount-requests" của
/// docs/api-contract.md.
///
/// <b>Hai tầng con số, cố ý tách rời:</b>
///
/// - Bốn con <see cref="Total"/> / <see cref="Pending"/> / <see cref="Approved"/> /
///   <see cref="Rejected"/> đến từ <b>bốn truy vấn đếm độc lập</b>, không con nào suy ra từ con
///   khác. Nhờ vậy đẳng thức thứ nhất dưới đây là phép kiểm THẬT: nếu ai đó lọc rơi một trạng thái
///   (ví dụ quên <c>Rejected</c>) thì test đỏ ngay. Nếu cộng dồn từ một truy vấn gộp thì đẳng thức
///   đúng theo định nghĩa và không test nào có thể đỏ — đúng lối đã ghi ở
///   <see cref="Vouchers.VoucherStatisticsResponse"/>.
/// - <see cref="ApprovedActive"/> / <see cref="ApprovedExpired"/> là <b>tập con của
///   <see cref="Approved"/></b>, không phải hai trạng thái thứ tư và thứ năm. Lý do: bảng không lưu
///   trạng thái <c>Expired</c> — hết hạn SUY RA từ <c>ExpiresAt &lt; now</c> (xem
///   <see cref="Entities.DiscountRequestStatus"/>). Chúng trả lời câu hỏi mà màn hình HR thật sự
///   cần: trong số hồ sơ đã duyệt, bao nhiêu còn hiệu lực để áp giá ưu đãi.
///
/// Hai đẳng thức hợp đồng khoá lại, màn hình được phép dựa vào:
///
/// <code>
/// total    == pending + approved + rejected
/// approved == approvedActive + approvedExpired
/// </code>
///
/// Cả hai đều đúng tuyệt đối — không có số hạng bù — vì mọi hồ sơ đều thuộc đúng một trong ba
/// trạng thái lưu trữ, và mọi hồ sơ đã duyệt hoặc còn hiệu lực hoặc đã hết hạn.
/// </summary>
public class DiscountRequestStatisticsResponse
{
    /// <summary>Tổng số hồ sơ ưu đãi trong hệ thống — mọi trạng thái, mọi đối tượng ưu đãi.</summary>
    public int Total { get; set; }

    /// <summary>Số hồ sơ đang chờ HR/Quản lý duyệt.</summary>
    public int Pending { get; set; }

    /// <summary>
    /// Số hồ sơ đã duyệt — <b>gồm cả hồ sơ đã hết hạn</b>. Đây là số theo trạng thái LƯU TRỮ;
    /// muốn biết bao nhiêu còn dùng được thì đọc <see cref="ApprovedActive"/>.
    /// </summary>
    public int Approved { get; set; }

    /// <summary>Số hồ sơ bị từ chối. Giữ bản ghi làm lịch sử nên vẫn đếm.</summary>
    public int Rejected { get; set; }

    /// <summary>
    /// Số hồ sơ đã duyệt và <b>còn hiệu lực</b> — <c>ExpiresAt</c> null (thẻ không có ngày hết hạn,
    /// ví dụ người cao tuổi) hoặc <c>ExpiresAt</c> nằm trong tương lai.
    ///
    /// Đây là con số service áp giá và màn hình nên dùng khi hỏi "bao nhiêu người đang thật sự được
    /// hưởng ưu đãi", không phải <see cref="Approved"/>.
    /// </summary>
    public int ApprovedActive { get; set; }

    /// <summary>
    /// Số hồ sơ đã duyệt nhưng <c>ExpiresAt</c> đã qua. <b>Tập con của <see cref="Approved"/></b>,
    /// không phải trạng thái lưu trữ — xem ghi chú đầu lớp.
    /// </summary>
    public int ApprovedExpired { get; set; }
}
