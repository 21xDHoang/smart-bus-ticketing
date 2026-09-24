namespace SmartBus.Api.Dtos.Fares;

/// <summary>
/// Một dòng giá vé trong bảng giá của tuyến — khớp mục "Bảng giá vé" của docs/api-contract.md.
/// Đổi tên trường ở đây là đổi hình dạng API: phải sửa api-contract.md trước rồi báo
/// người viết frontend (Băng, Hạnh, Thịnh), xem docs/03-quy-uoc.md mục 2.2 điều 5.
///
/// Thư mục đặt tên số nhiều "Fares" có chủ ý: namespace SmartBus.Api.Dtos.Fare (số ít) sẽ
/// trùng tên với entity SmartBus.Api.Entities.Fare, và trong file nào import cả hai thì
/// compiler báo "Fare is a namespace but is used like a type".
/// </summary>
public class FareResponse
{
    public Guid Id { get; set; }

    public Guid RouteId { get; set; }

    /// <summary>
    /// Mã đối tượng hành khách — một trong các tên của <see cref="Entities.PassengerType"/>:
    /// Standard, Student, Senior, Child, Disabled.
    ///
    /// Trả về CHUỖI chứ không phải enum: Program.cs không đăng ký JsonStringEnumConverter,
    /// nên kiểu enum sẽ serialize thành 0, 1, 2… — đọc không hiểu, trái tinh thần quy ước A3
    /// ("lưu dạng chuỗi để đọc là hiểu, không phải tra bảng mã").
    /// </summary>
    public string PassengerType { get; set; } = string.Empty;

    /// <summary>
    /// Giá vé (VND). Cột numeric(12,2) trong CSDL — không bao giờ là float/double (quy ước A3).
    /// </summary>
    public decimal Price { get; set; }

    public DateTime CreatedAt { get; set; }

    /// <summary>null khi dòng giá chưa được sửa lần nào.</summary>
    public DateTime? UpdatedAt { get; set; }
}
