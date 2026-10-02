using SmartBus.Api.Dtos.Trips;

namespace SmartBus.Api.Services;

/// <summary>
/// Bộ nhớ đệm kết quả tìm chuyến của GET /api/trips/search — task *"Cache kết quả tìm kiếm tuyến phổ
/// biến để giảm tải DB"* (Nguyễn Duy Kiên). Hợp đồng đầy đủ ở mục "GET /trips/search" của
/// docs/api-contract.md.
///
/// Đứng TRƯỚC <see cref="TripSearchService"/> qua <see cref="CachedTripSearchService"/> chứ không nằm
/// trong service: service, controller và DTO của endpoint này là file của Phùng Duy Hoàng (quy ước
/// E1) nên nhóm không sửa — cache móc vào bằng lớp bọc.
///
/// Chỉ đệm kết quả THÀNH CÔNG. Lỗi (400 khoảng thời gian sai, 404 tuyến không tồn tại) không bao giờ
/// vào đệm: tuyến vừa được tạo phải tra được ngay ở lượt gọi kế tiếp.
/// </summary>
public interface ITripSearchResultCache
{
    /// <summary>
    /// Trần số khoá giữ trong đệm. Chạm trần thì dọn khoá đã hết hạn; vẫn đầy thì thôi không nhận
    /// thêm — cố ý KHÔNG đuổi khoá cũ, để một tuyến đang nóng không bị đẩy ra ngoài.
    /// </summary>
    const int MaxEntries = 200;

    /// <summary>
    /// Số khoá đang giữ — kể cả khoá mới đếm được một lượt hỏi mà chưa đủ ngưỡng "phổ biến" để lưu
    /// kết quả. Không bao giờ vượt <see cref="MaxEntries"/>.
    /// </summary>
    int Count { get; }

    /// <summary>Số lượt trả được kết quả từ đệm — đúng bằng số lượt KHÔNG phải chạm CSDL.</summary>
    long Hits { get; }

    /// <summary>
    /// Số lượt phải hỏi CSDL (trượt đệm). Đây là con số chứng minh "giảm tải DB": trước khi có đệm,
    /// mỗi lượt gọi endpoint tốn ba truy vấn CSDL.
    /// </summary>
    long Misses { get; }

    /// <summary>
    /// Ghi nhận một lượt hỏi <paramref name="key"/> — đây chính là chỗ đếm để biết khoá nào "phổ
    /// biến" (được hỏi lặp lại) — rồi trả kết quả đã đệm nếu có.
    ///
    /// Cố ý có tác dụng phụ: người gọi PHẢI gọi hàm này đúng một lần cho mỗi lượt hỏi thật, kể cả
    /// khi sau đó phải hỏi CSDL, nếu không thì ngưỡng "phổ biến" không bao giờ đạt được.
    /// </summary>
    /// <returns>Kết quả đã đệm, hoặc <see langword="null"/> khi chưa có (người gọi phải hỏi CSDL).</returns>
    IReadOnlyList<TripSearchResult>? Lookup(string key);

    /// <summary>
    /// Lưu kết quả vừa đọc từ CSDL cho <paramref name="key"/>. Chỉ thật sự lưu khi khoá đã được hỏi
    /// đủ ngưỡng "phổ biến" và đệm còn chỗ — nên gọi hàm này không bảo đảm lượt sau sẽ trúng đệm.
    /// </summary>
    void Store(string key, IReadOnlyList<TripSearchResult> results);
}
