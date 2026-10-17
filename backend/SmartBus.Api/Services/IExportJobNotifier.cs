using SmartBus.Api.Entities;

namespace SmartBus.Api.Services;

/// <summary>
/// Báo kết quả một job xuất báo cáo cho người đã đặt nó — task *"Xử lý xuất file lớn bất đồng bộ +
/// thông báo khi xong"* (Sprint 5 dòng 64, Vàng Thị Dăm).
///
/// Hai chốt hợp đồng, cùng lối <see cref="ITicketRequestResultNotifier"/>:
///
/// 1. <b>KHÔNG BAO GIỜ NÉM.</b> Người gọi là job nền vừa dựng xong một file có thể mất vài phút;
///    một lỗi khi ghi thông báo không được phép biến job <c>Succeeded</c> thành thất bại. Dòng
///    <c>Notifications</c> là kênh bền, SignalR là kênh phụ trợ — cả hai hỏng thì người dùng vẫn
///    thấy trạng thái qua <c>GET /api/exports/{id}</c>.
/// 2. <b>Không nhận <c>CancellationToken</c>.</b> Job đã xong; huỷ nửa chừng chỉ làm mất thông báo
///    của một kết quả có thật. Cùng lý do đã ghi ở <see cref="ITicketRequestResultNotifier"/>.
/// </summary>
public interface IExportJobNotifier
{
    /// <summary>File đã sẵn sàng — ghi thông báo <c>ExportCompleted</c> rồi đẩy sự kiện SignalR.</summary>
    Task NotifyCompletedAsync(ExportJob job);

    /// <summary>
    /// Job thất bại — ghi thông báo <c>ExportFailed</c> kèm lý do người dùng đọc được
    /// (<paramref name="reason"/> đã được cắt và làm sạch trước khi gọi).
    /// </summary>
    Task NotifyFailedAsync(ExportJob job, string reason);
}
