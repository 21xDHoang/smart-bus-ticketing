namespace SmartBus.Api.Entities;

/// <summary>
/// Trạng thái xử lý một phản ánh — vòng đời do quản lý đổi qua PATCH /api/admin/feedbacks/{id}.
///
/// Đúng ba giá trị, không có "Rejected"/"Cancelled": hợp đồng không có luồng từ chối phản ánh,
/// cũng không có chuyện hành khách tự rút phản ánh. Cố ý KHÔNG có máy trạng thái — mọi chiều
/// chuyển đều hợp lệ, kể cả mở lại Resolved → InProgress (lý do ghi trong hợp đồng).
/// </summary>
public enum FeedbackStatus
{
    /// <summary>Mới tiếp nhận, chưa ai xử lý — giá trị khởi tạo của mọi phản ánh.</summary>
    New,

    /// <summary>Có người đã nhận, đang xử lý.</summary>
    InProgress,

    /// <summary>Đã trả lời / xử lý xong.</summary>
    Resolved,
}
