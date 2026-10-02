namespace SmartBus.Api.Entities;

/// <summary>
/// Bảng PassTypes — danh mục loại vé tháng (US 16 "Đăng ký vé tháng"): thời hạn và giá gói.
/// Task migrate bảng này là của Vàng Thị Dăm (story 16).
///
/// ⚠️ Bảng này KHÔNG có trong danh sách A9 — đã báo nhóm trong mô tả PR.
/// Vì sao vẫn tạo, trong khi tiền lệ PriorityGroups thì gộp lại theo A9:
///   - Ở tiền lệ kia, A9 MÂU THUẪN với backlog (A9 ghi một bảng DiscountRequests thay cho hai
///     bảng PriorityGroups + PriorityApprovals) và bảng danh mục kia không chứa gì ngoài một
///     enum đã có sẵn là PassengerType — nên gộp được, và A9 thắng.
///   - Ở đây A9 chỉ IM LẶNG, không mâu thuẫn: A9 ghi MonthlyPasses "có Code" mà không nói gì
///     tới loại vé. Còn Code / Name / DurationMonths / Price thì KHÔNG thứ nào đang tồn tại
///     chứa được — nhét vào MonthlyPasses là lặp lại tên và giá trên mọi vé, sửa giá phải sửa
///     từng dòng, và không có chỗ để khai một loại vé chưa ai mua.
/// Luật cấm của nhóm là "tự thêm bảng ngoài A9 MÀ KHÔNG BÁO NHÓM" — điều kiện vi phạm là không
/// báo, không phải việc thêm. Đã báo.
///
/// Bảng này cũng KHÔNG được seed sẵn: giá gói là quyết định kinh doanh, không phải quyết định
/// kỹ thuật. Task story 16 chỉ nói "migrate bảng", không nói "seed" (khác task story 22 ghi rõ
/// "+ seed 4 vai trò mặc định"). Bốn loại vé mà màn hình đang tạm giữ ở client là
/// OneMonth / ThreeMonths / SixMonths / TwelveMonths kèm giá tạm — xem frontend/src/api/monthlyPassApi.ts.
/// </summary>
public class PassType
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Mã loại vé — khoá nghiệp vụ dùng trong hợp đồng API (body POST /monthly-passes nhận
    /// <c>passTypeCode</c>), ví dụ "OneMonth", "TwelveMonths". Cùng lối với Routes.Code và
    /// Roles.Code: mã là thứ con người trao đổi với nhau, Guid chỉ là khoá kỹ thuật.
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Tên hiển thị cho hành khách, ví dụ "Vé tháng 3 tháng".</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Thời hạn hiệu lực tính bằng tháng. Kiểu số nguyên chứ không phải số ngày: vé tháng bán
    /// theo tháng, và cộng tháng mới đúng ý người mua (31/01 + 1 tháng = 28/02, không phải 03/03).
    /// </summary>
    public int DurationMonths { get; set; }

    /// <summary>
    /// Giá gói cho TOÀN BỘ thời hạn (VND), không phải giá mỗi tháng — mua dài hạn được chiết
    /// khấu nên giá gói không chia hết cho số tháng.
    ///
    /// ⚠️ Cố ý KHÔNG theo khuôn bảng Fares: Fares định giá theo (RouteId, PassengerType) vì giá
    /// vé lượt phụ thuộc độ dài tuyến. Vé tháng thì không — quy ước A4/A8.2 chốt "vé tháng =
    /// quyền đi lại trên MỘT tuyến trong khoảng thời gian", và cả ba task story 16 nói tới "loại
    /// vé" đều hỏi giá theo loại vé. Nếu sau này cần giá vé tháng theo tuyến thì thêm cột RouteId
    /// vào bảng này rồi đổi khoá tra — không phải sửa bảng MonthlyPasses.
    /// </summary>
    public decimal Price { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Sửa được giá gói nên có UpdatedAt (A4).</summary>
    public DateTime? UpdatedAt { get; set; }

    // KHÔNG có IsActive / IsDeleted (A4 cấm IsDeleted, và không story nào cần ngừng bán một loại
    // vé): MonthlyPasses trỏ tới bảng này bằng khoá ngoại Restrict nên dòng đã dùng không xoá
    // được, còn dòng chưa ai dùng thì xoá thẳng. Thêm cột trạng thái bây giờ là thêm nhánh
    // "loại vé đang ẩn" cho mọi truy vấn mà không có màn hình nào khai thác.
}
