using SmartBus.Api.Dtos.Reports;

namespace SmartBus.Api.Services;

/// <summary>
/// Bộ nhớ đệm kết quả báo cáo doanh thu theo kỳ — task *"Cache kết quả báo cáo theo kỳ để tăng
/// tốc"* (US 19 "Doanh thu", Sprint 5 dòng 43 — Vàng Thị Dăm).
///
/// Đứng TRƯỚC <see cref="RevenueReportService"/> qua <see cref="CachedRevenueReportService"/> chứ
/// không nằm trong service: <c>Services/RevenueReportService.cs</c> là file của Trần Trung Hiếu
/// (quy ước E1 — không sửa file của người khác), nên đệm móc vào bằng lớp bọc. Nhờ vậy hình dạng
/// response và toàn bộ nghiệp vụ (chỉ đếm Success, trục giờ Việt Nam, bucket liên tục) giữ nguyên.
///
/// Chỉ đệm kết quả THÀNH CÔNG. Lỗi (400 granularity/khoảng ngày sai, 404 tuyến không tồn tại)
/// không bao giờ vào đệm: tuyến vừa được tạo hay tham số vừa sửa phải trả lời tươi ở lượt kế tiếp.
/// </summary>
public interface IRevenueReportCache
{
    /// <summary>
    /// Trần số khoá giữ trong đệm. Chạm trần thì dọn khoá đã hết hạn; vẫn đầy thì thôi không nhận
    /// thêm — cố ý KHÔNG đuổi khoá cũ, cùng lối <see cref="ITripSearchResultCache"/>.
    /// </summary>
    const int MaxEntries = 200;

    /// <summary>Số khoá đang giữ — không bao giờ vượt <see cref="MaxEntries"/>.</summary>
    int Count { get; }

    /// <summary>Số lượt trả được kết quả từ đệm — đúng bằng số lượt KHÔNG chạm CSDL.</summary>
    long Hits { get; }

    /// <summary>Số lượt phải hỏi CSDL (trượt đệm).</summary>
    long Misses { get; }

    /// <summary>
    /// Trả kết quả đã đệm cho <paramref name="key"/>, hoặc <see langword="null"/> khi chưa có/hết
    /// hạn (người gọi phải hỏi CSDL). Khoá do <see cref="CachedRevenueReportService"/> dựng từ đúng
    /// bộ tham số ảnh hưởng tới kết quả.
    /// </summary>
    RevenueReportResponse? Lookup(string key);

    /// <summary>
    /// Lưu kết quả vừa đọc từ CSDL. Không bảo đảm lượt sau sẽ trúng đệm: đệm có thể đã chạm trần.
    /// </summary>
    void Store(string key, RevenueReportResponse report);
}
