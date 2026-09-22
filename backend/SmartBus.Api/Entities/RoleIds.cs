namespace SmartBus.Api.Entities;

/// <summary>
/// ID cố định của 4 vai trò mặc định — dùng chung cho seed và cho code nghiệp vụ
/// (ví dụ API đăng ký gán mặc định vai trò Hành khách).
/// Cố định sẵn để migration seed và dữ liệu tham chiếu luôn khớp nhau.
/// </summary>
public static class RoleIds
{
    public static readonly Guid Admin = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public static readonly Guid Manager = Guid.Parse("22222222-2222-2222-2222-222222222222");

    public static readonly Guid Driver = Guid.Parse("33333333-3333-3333-3333-333333333333");

    public static readonly Guid Passenger = Guid.Parse("44444444-4444-4444-4444-444444444444");
}
