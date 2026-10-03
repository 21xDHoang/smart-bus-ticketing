namespace SmartBus.Api.Dtos.Feedbacks;

/// <summary>
/// Thống kê phản ánh — GET /api/admin/feedbacks/statistics. Hợp đồng "Phản ánh — /feedbacks", mục
/// "Thống kê phản ánh" của docs/api-contract.md.
///
/// Hai bảng đếm của TOÀN BỘ phản ánh trong hệ thống (không lọc theo thời gian, không phân trang,
/// không lọc theo trạng thái — task chỉ hỏi "theo loại và theo tuyến").
///
/// Hai đẳng thức hợp đồng khoá lại, màn hình được phép dựa vào:
///
/// <code>
/// sum(ByType[].Count)                == Total
/// sum(ByRoute[].Count) + WithoutTrip == Total
/// </code>
///
/// Vế thứ hai là hệ quả của việc phản ánh hoặc gắn chuyến (⇒ quy được về tuyến của chuyến đó) hoặc
/// không (<see cref="WithoutTrip"/>) — không có ca thứ ba, nên hai vế phải bằng nhau. Test khoá lại
/// cả hai, vì đây là loại sai sót mà màn hình không tự phát hiện được: nó chỉ vẽ ra một biểu đồ
/// thiếu một mẩu.
/// </summary>
public class FeedbackStatisticsResponse
{
    /// <summary>Tổng số phản ánh trong hệ thống.</summary>
    public int Total { get; set; }

    /// <summary>Đếm theo loại — LUÔN đủ ba dòng theo thứ tự Complaint → Compliment → Suggestion.</summary>
    public IReadOnlyList<FeedbackTypeCountResponse> ByType { get; set; } = [];

    /// <summary>
    /// Đếm theo tuyến, chỉ gồm tuyến đã có phản ánh; sắp số lượng giảm dần, trùng số thì theo
    /// <c>routeCode</c> tăng dần.
    /// </summary>
    public IReadOnlyList<FeedbackRouteCountResponse> ByRoute { get; set; } = [];

    /// <summary>
    /// Số phản ánh không gắn chuyến (<c>tripId</c> null — A9 #20: phản ánh về giá vé, ứng dụng, dịch
    /// vụ chung) nên không quy được về tuyến nào. Tách riêng thay vì thành một dòng
    /// <c>routeId: null</c> trong <see cref="ByRoute"/> — trộn hai loại khác nhau vào một mảng thì
    /// màn hình phải tự đoán cách vẽ.
    /// </summary>
    public int WithoutTrip { get; set; }
}
