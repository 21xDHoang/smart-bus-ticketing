namespace SmartBus.Api.Services;

/// <summary>
/// Ruột của job nền xuất báo cáo — task *"Xử lý xuất file lớn bất đồng bộ + thông báo khi xong"*
/// (Sprint 5 dòng 64, Vàng Thị Dăm).
///
/// Tách khỏi <see cref="ExportJobBackgroundService"/> theo đúng lối ba job nền đã có của dự án:
/// hosted service là singleton chỉ canh nhịp, còn phần chạm CSDL nằm ở service scoped và được gọi
/// trong một scope mới mỗi lượt. Nhờ vậy <c>AppDbContext</c> không sống suốt vòng đời app.
/// </summary>
public interface IExportJobProcessor
{
    /// <summary>
    /// Nhận job <c>Pending</c> cũ nhất, dựng file, chốt trạng thái và báo cho người đặt.
    /// Trả <see langword="true"/> khi có job được xử lý (thành công hay thất bại đều tính), để vòng
    /// lặp biết còn việc hay không. Không có job nào → <see langword="false"/>.
    ///
    /// Không nuốt lỗi hạ tầng (CSDL đứt): để vòng lặp nền ghi log rồi thử lại lượt sau. Lỗi khi
    /// DỰNG file thì bị bắt tại đây và chốt job <c>Failed</c> — người dùng phải nhận được câu giải
    /// thích thay vì một job treo mãi ở <c>Processing</c>.
    /// </summary>
    Task<bool> ProcessNextAsync(CancellationToken cancellationToken);
}
