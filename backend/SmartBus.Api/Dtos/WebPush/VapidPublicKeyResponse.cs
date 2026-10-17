namespace SmartBus.Api.Dtos.WebPush;

/// <summary>
/// Hình dạng trả về của GET /api/web-push/vapid-public-key — khoá công khai VAPID cho trình duyệt
/// dùng làm <c>applicationServerKey</c> (US 10 "Thông báo trạm", Sprint 4 dòng 42 — Trần Trung
/// Hiếu). Điểm uncompressed P-256 65 byte, base64url — đúng dạng mọi trình duyệt chờ.
/// </summary>
public class VapidPublicKeyResponse
{
    /// <summary>Khoá công khai VAPID, base64url — truyền nguyên vào <c>PushManager.subscribe()</c>.</summary>
    public string PublicKey { get; set; } = string.Empty;
}
