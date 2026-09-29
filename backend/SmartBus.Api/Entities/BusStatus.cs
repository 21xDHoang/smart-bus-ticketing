namespace SmartBus.Api.Entities;

/// <summary>
/// Trạng thái xe buýt trong đội xe. Lưu dạng chuỗi trong CSDL (quy ước A3):
/// "Maintenance" đọc là hiểu, còn 2 thì phải tra bảng mã.
/// </summary>
public enum BusStatus
{
    /// <summary>Đang khai thác — được phép gán vào chuyến.</summary>
    Active,

    /// <summary>Đang bảo dưỡng — tạm rút khỏi đội xe, không gán vào chuyến mới.</summary>
    Maintenance,

    /// <summary>
    /// Ngừng khai thác. Vẫn giữ bản ghi thay vì xoá — còn chuyến cũ tham chiếu tới,
    /// và quy ước A4 cấm thêm cột IsDeleted nên trạng thái là cách "xoá mềm" duy nhất.
    /// </summary>
    Inactive
}
