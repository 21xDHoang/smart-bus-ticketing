using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace SmartBus.Api.Hubs;

/// <summary>
/// Hub SignalR thông báo trong ứng dụng (A8.5) — task Sprint 4 *"Ghi log và thông báo kết quả xử lý
/// cho hành khách"* (Vàng Thị Dăm, dòng 7). Client nối vào là nhận được thông báo của CHÍNH MÌNH;
/// việc đẩy sự kiện nằm ở <c>Services/TicketRequestResultNotifier.cs</c>, service nghiệp vụ không
/// cầm Hub trực tiếp mà đi qua cổng <c>ITicketRequestResultNotifier</c>.
///
/// Khác <see cref="SeatMapHub"/> ở đúng điểm quan trọng nhất: hub này CÓ <c>[Authorize]</c>. Sơ đồ ghế
/// là thông tin khách tra cứu được trước khi có tài khoản, còn thông báo là dữ liệu RIÊNG của một
/// người — không xác thực được thì không có cách nào biết đẩy cho ai, và để ẩn danh thì mọi client
/// nối vào đều nằm ngoài mọi nhóm (im lặng, không lỗi — tệ hơn hẳn một lượt 401 rõ ràng).
///
/// 🔴 Client PHẢI gửi access token khi nối, và với trình duyệt thì token đi bằng query string
/// <c>?access_token=…</c> — WebSocket không đặt được header <c>Authorization</c>. Phía server, việc đọc
/// token từ query string nằm ở <c>JwtMiddleware.OnMessageReceived</c> (chỉ áp cho đường dẫn
/// <c>/hubs</c>). Thiếu một trong hai đầu thì kết nối vẫn mở được nhưng <see cref="Context"/> không có
/// người dùng, và thông báo rơi vào khoảng không — xem docs/33 §3.
///
/// Client KHÔNG phải gọi method nào để vào nhóm: <see cref="OnConnectedAsync"/> tự thêm connection vào
/// nhóm của chính chủ tài khoản. Nhờ vậy không có cách nào một client xin vào nhóm của người khác —
/// nhóm suy ra từ token, không từ tham số do client gửi lên.
/// </summary>
[Authorize]
public class NotificationHub : Hub<INotificationClient>
{
    private readonly ILogger<NotificationHub> _logger;

    public NotificationHub(ILogger<NotificationHub> logger) => _logger = logger;

    /// <summary>
    /// Thêm connection vào nhóm của chủ tài khoản. Đây là CHỖ DUY NHẤT quyết định ai nhận thông báo,
    /// nên khi không xác định được người dùng thì phải để lại dấu vết: hub có <c>[Authorize]</c> nên
    /// nhánh này chỉ chạy khi cấu hình xác thực cho hub bị hỏng (thiếu
    /// <c>OnMessageReceived</c> đọc query string), tức là một lỗi cấu hình chứ không phải một lượt
    /// khách bình thường. Im lặng ở đây nghĩa là "thông báo không tới ai cả" mà không ai biết.
    /// </summary>
    public override async Task OnConnectedAsync()
    {
        if (Context.UserIdentifier is not { Length: > 0 } userId)
        {
            _logger.LogWarning(
                "Kết nối {ConnectionId} vào NotificationHub không có định danh người dùng — không vào nhóm nào, "
                + "mọi thông báo của tài khoản này sẽ không được đẩy. Kiểm tra OnMessageReceived của JwtMiddleware "
                + "(đọc access_token từ query string) và MapHub ở Program.cs.",
                Context.ConnectionId);
        }
        else
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, GroupOf(userId));
        }

        await base.OnConnectedAsync();
    }

    /// <summary>
    /// Tên nhóm của một tài khoản — MỘT chỗ sinh tên nhóm duy nhất cho cả hub lẫn
    /// <c>TicketRequestResultNotifier</c>. Hai chỗ tự ghép chuỗi là hai chỗ có thể lệch nhau, và lệch ở
    /// đây nghĩa là thông báo bắn vào một nhóm không có ai trong đó: khách không nhận được gì mà không
    /// có lỗi nào để lần. Cùng lý do đã ghi ở <see cref="SeatMapHub.GroupOf"/>.
    /// </summary>
    internal static string GroupOf(string userId) => $"user:{userId}";

    /// <summary>
    /// Bản nhận <see cref="Guid"/> của <see cref="GroupOf(string)"/> — dùng ở phía service đẩy sự kiện,
    /// nơi id người nhận là khoá chính của bảng <c>Notifications</c>.
    ///
    /// ⚠️ <c>Guid.ToString()</c> ở đây phải ra ĐÚNG chuỗi mà claim <c>NameIdentifier</c> mang
    /// (<c>TokenService</c> phát claim bằng <c>user.Id.ToString()</c>), vì <see cref="Context.UserIdentifier"/>
    /// chính là chuỗi thô của claim đó. Hai đầu lệch định dạng (chữ hoa/chữ thường) là hai nhóm khác
    /// nhau, nên luôn đi qua hàm này thay vì tự ghép chuỗi ở chỗ gọi.
    /// </summary>
    internal static string GroupOf(Guid userId) => GroupOf(userId.ToString());
}
