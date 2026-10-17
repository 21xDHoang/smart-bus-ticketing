namespace SmartBus.Api.Services;

/// <summary>
/// Một thiết bị trình duyệt đã đăng ký nhận Web Push của một tài khoản — thứ <c>PushManager.
/// subscribe()</c> trả về cho frontend và frontend gửi lên POST /api/web-push/devices.
///
/// ⚠️ <b>KHÔNG phải bảng <c>Subscriptions</c></b> (theo dõi TUYẾN/TRẠM, docs/37): lượt theo dõi là
/// "ai muốn nhận thông báo về tuyến nào", thiết bị này là "tin nhắn gửi tới đâu". Một khách có thể
/// đăng ký một tuyến trên BA trình duyệt — ba dòng thiết bị, một dòng theo dõi. Hai khái niệm tách
/// bạch, chung nhau ở chỗ người bắn tin (service của Kiên, dòng 15/29 Sprint 5) đọc bảng theo dõi để
/// biết GỬI CHO AI rồi hỏi store thiết bị để biết GỬI TỚI ĐÂU.
/// </summary>
public sealed record WebPushSubscriptionInfo
{
    /// <summary>Khoá của lượt đăng ký thiết bị — dùng cho DELETE /api/web-push/devices/{id}.</summary>
    public required Guid Id { get; init; }

    /// <summary>Chủ thiết bị — mọi truy vấn lọc theo cột này (cùng lối "phạm vi của tôi" của dự án).</summary>
    public required Guid UserId { get; init; }

    /// <summary>Push endpoint do trình duyệt cấp (https://fcm.googleapis.com/… với Chrome…) — nơi gửi tin.</summary>
    public required string Endpoint { get; init; }

    /// <summary>Khoá công khai P-256 của THIẾT BỊ (điểm uncompressed 65 byte, base64url) — để mã hoá tin.</summary>
    public required string P256Dh { get; init; }

    /// <summary>Bí mật xác thực 16 byte (base64url) — trộn vào HKDF, chống kẻ chặn suy được khoá phiên.</summary>
    public required string Auth { get; init; }

    /// <summary>Trình duyệt tự định ngày hết hạn subscription; null = không hẹn giờ.</summary>
    public DateTimeOffset? ExpirationTime { get; init; }

    /// <summary>Chuỗi User-Agent của trình duyệt lúc đăng ký — để màn quản lý thiết bị hiển thị được.</summary>
    public string UserAgent { get; init; } = string.Empty;

    /// <summary>Thời điểm thiết bị đăng ký (hoặc đăng ký lại) gần nhất (UTC).</summary>
    public DateTimeOffset CreatedAt { get; init; }
}

/// <summary>
/// Kho chứa thiết bị đã đăng ký Web Push — interface để tách "kho nào" khỏi "gửi thế nào".
///
/// 🔴 <b>Bản cài đặt hiện tại là trong bộ nhớ</b> (<see cref="InMemoryWebPushSubscriptionStore"/>):
/// chưa có bảng nào lưu thiết bị push — bảng <c>Subscriptions</c> đã migrate là cho nghiệp vụ theo
/// dõi tuyến/trạm, còn bảng thiết bị push là việc migration của Vàng Thị Dăm (luật 3: Entities/ và
/// Migrations/ không ai khác được chạm). Khi Dăm migrate bảng <c>WebPushSubscriptions</c>, chỉ cần
/// viết một bản EF đọc bảng đó rồi ĐỔI ĐÚNG MỘT DÒNG đăng ký ở Program.cs — cùng lối
/// <c>IRefundStatementSource</c> đổi từ bản <c>Unavailable…</c> sang nguồn thật. Đề xuất hình dạng
/// bảng: docs/54-tich-hop-web-push-pwa.md §6.
///
/// Hệ quả đã biết của bản trong bộ nhớ: backend khởi động lại là danh sách thiết bị bay — frontend
/// tự đăng ký lại mỗi lần mở app (subscription của trình duyệt KHÔNG đổi, gửi lại là cùng một
/// endpoint) nên chỉ thiếu hụt lúc backend đang tắt.
/// </summary>
public interface IWebPushSubscriptionStore
{
    /// <summary>
    /// Đăng ký hoặc đăng ký lại một thiết bị. Cùng (người dùng, endpoint) thì GHI ĐÈ khoá/cờ thời
    /// gian chứ không thêm dòng thứ hai — gửi hai tin tới một endpoint là tin nhận đúp trên cùng
    /// một màn hình. Trả về <c>true</c> khi là thiết bị MỚI (để controller chọn 201 hay 200).
    /// </summary>
    WebPushSubscriptionInfo Upsert(WebPushSubscriptionInfo subscription, out bool created);

    /// <summary>Danh sách thiết bị của một người dùng, mới nhất trước.</summary>
    IReadOnlyList<WebPushSubscriptionInfo> ListForUser(Guid userId);

    /// <summary>
    /// Tìm thiết bị theo id — trả <c>null</c> khi không có hoặc không phải của <paramref name="userId"/>
    /// (một câu trả lời cho cả hai ca, cùng lối "404 chung" của dự án).
    /// </summary>
    WebPushSubscriptionInfo? Find(Guid userId, Guid id);

    /// <summary>Bỏ thiết bị của một người dùng — trả <c>false</c> khi không có/của người khác.</summary>
    bool Remove(Guid userId, Guid id);

    /// <summary>
    /// Bỏ MỌI dòng có đúng endpoint này (không cần biết người dùng) — cho chỗ dọn subscription đã
    /// chết: push service trả 404/410 nghĩa là trình duyệt đã hủy đăng ký ở phía nó, giữ lại chỉ
    /// phí mỗi lượt gửi một lỗi.
    /// </summary>
    void RemoveByEndpoint(string endpoint);
}
