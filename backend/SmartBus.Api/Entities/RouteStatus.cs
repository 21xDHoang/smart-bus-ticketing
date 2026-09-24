namespace SmartBus.Api.Entities;

/// <summary>
/// Trạng thái tuyến đường. Lưu dạng chuỗi trong CSDL (quy ước A3):
/// "Active" đọc là hiểu, còn 1 thì phải tra bảng mã.
/// </summary>
public enum RouteStatus
{
    /// <summary>Đang khai thác.</summary>
    Active,

    /// <summary>
    /// Ngừng khai thác. Vẫn giữ bản ghi thay vì xoá — còn chuyến và vé cũ tham chiếu tới,
    /// và quy ước A4 cấm thêm cột IsDeleted nên trạng thái là cách "xoá mềm" duy nhất.
    /// </summary>
    Inactive
}
