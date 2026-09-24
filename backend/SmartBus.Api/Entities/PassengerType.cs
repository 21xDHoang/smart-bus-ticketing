namespace SmartBus.Api.Entities;

/// <summary>
/// Đối tượng hành khách dùng để tính giá vé — cơ sở của bảng Fares.
/// Lưu dạng chuỗi trong CSDL (quy ước A3).
/// Học sinh/sinh viên và người cao tuổi là hai đối tượng ưu đãi của US 17.
/// </summary>
public enum PassengerType
{
    /// <summary>Người lớn — vé phổ thông, giá gốc của tuyến.</summary>
    Standard,

    /// <summary>Học sinh, sinh viên (đối tượng ưu đãi — US 17).</summary>
    Student,

    /// <summary>Người cao tuổi (đối tượng ưu đãi — US 17).</summary>
    Senior,

    /// <summary>Trẻ em.</summary>
    Child,

    /// <summary>Người khuyết tật.</summary>
    Disabled
}
