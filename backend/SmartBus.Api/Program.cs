using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartBus.Api.Data;
using SmartBus.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

// Trả lỗi validate theo cấu trúc thống nhất { message, errors } của dự án
// thay vì ProblemDetails mặc định — xem docs/01-kien-truc.md.
builder.Services.Configure<ApiBehaviorOptions>(o => o.SuppressModelStateInvalidFilter = true);

builder.Services.AddOpenApi();

// CSDL PostgreSQL (Supabase). Chuỗi kết nối nằm ở appsettings.Development.json — không commit.
builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

// Cấu hình JWT — xem appsettings.Development.json.example
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));

builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<IAuthService, AuthService>();

// Quản trị người dùng: CRUD /admin/users, khóa/mở khóa, gán/thu hồi vai trò.
// Task story 22 — Nguyễn Duy Kiên. File này của Hoàng nên nhờ Hoàng xem qua trong PR.
builder.Services.AddScoped<IAdminUserService, AdminUserService>();

// Bảng giá vé theo tuyến và theo đối tượng ưu đãi: /routes/{routeId}/fares.
// Task story 12 — Phùng Duy Hoàng (file này cũng của Hoàng).
builder.Services.AddScoped<IFareService, FareService>();


// CRUD tuyến đường /routes và trạm dừng /stops.
// Task story 12 — Trần Trung Hiếu (file này của Hoàng nên nhờ Hoàng xem qua trong PR).
builder.Services.AddScoped<IRouteService, RouteService>();
builder.Services.AddScoped<IStopService, StopService>();

// Gán trạm vào tuyến và sắp xếp lại thứ tự trạm: /routes/{routeId}/stops.
// Task story 12 — Nguyễn Duy Kiên. File này của Hoàng nên nhờ Hoàng xem qua trong PR.
builder.Services.AddScoped<IRouteStopService, RouteStopService>();

// Chi tiết chuyến xe: GET /api/trips/{id} — giờ chạy, xe, sức chứa, danh sách trạm dừng.
// Task story 13 — Vàng Thị Dăm. File này của Hoàng nên nhờ Hoàng xem qua trong PR.
builder.Services.AddScoped<ITripService, TripService>();

// Lịch trình chạy xe theo tuyến: CRUD /routes/{routeId}/trips + sinh chuyến hàng loạt theo tần suất.
// Task story 13 — Trần Trung Hiếu. File này của Hoàng nên nhờ Hoàng xem qua trong PR.
// Đặt tên RouteTrips để tránh đụng ITripService/TripService của API chi tiết chuyến /trips/{id}
// (Vàng Thị Dăm) — cùng khuôn cặp IStopService / IRouteStopService ở Sprint 1.
builder.Services.AddScoped<IRouteTripsService, RouteTripsService>();

// Tra cứu danh sách chuyến theo ngày + lọc theo tuyến: GET /api/trips.
// Task story 13 — Phùng Duy Hoàng (file này cũng của Hoàng).
// Cùng bề mặt /trips với ITripService ở trên nhưng khác bề mặt nghiệp vụ (bộ lọc thay vì định
// danh) nên đứng riêng — cùng khuôn cặp IStopService / IRouteStopService.
builder.Services.AddScoped<ITripLookupService, TripLookupService>();

// Tìm chuyến cho hành khách: GET /api/trips/search (giá vé phổ thông + giờ chạy + số ghế còn trống).
// Task story 1 — Phùng Duy Hoàng (file này cũng của Hoàng).
// Công khai có chủ đích: story 1 là luồng tra cứu của hành khách, đăng nhập là bước của màn hình
// đặt vé (phân quyền của dự án là opt-in — không gắn [Authorize] nghĩa là ai cũng gọi được).
// Khác ITripLookupService ở trên (Admin/Manager — màn hình điều hành): service này nhận thẳng
// routeId đã chọn và trả thêm giá vé + số ghế, không phân trang.
builder.Services.AddScoped<ITripSearchService, TripSearchService>();

// Đệm kết quả tìm chuyến cho endpoint công khai ở trên: GET /api/trips/search.
// Task "Cache kết quả tìm kiếm tuyến phổ biến để giảm tải DB" (Sprint 2) — Nguyễn Duy Kiên.
// File này của Hoàng nên nhờ Hoàng xem qua trong PR.
// Không sửa TripSearchService (file của Hoàng — quy ước E1): ITripSearchService được bọc bằng
// CachedTripSearchService. DI lấy bản đăng ký SAU CÙNG nên ba dòng dưới đây phải nằm ngay sau dòng
// của Hoàng — ai đăng ký thêm ITripSearchService ở dưới nữa thì bản có đệm bị thay thế im lặng
// (TripSearchCacheApiTests ghim đúng chỗ nối này).
builder.Services.AddScoped<TripSearchService>();
builder.Services.AddSingleton<ITripSearchResultCache, TripSearchResultCache>();
builder.Services.AddScoped<ITripSearchService>(provider => new CachedTripSearchService(
    provider.GetRequiredService<TripSearchService>(),
    provider.GetRequiredService<ITripSearchResultCache>()));

// Kiểm tra trùng lịch điều xe: xe trùng chuyến, tài xế trùng chuyến (US 14 "Phân công điều xe").
// Task story 14 — Phùng Duy Hoàng (file này cũng của Hoàng).
// Chưa gắn endpoint — cùng một phép kiểm tra dùng cho API gán xe + tài xế vào chuyến (task 113 —
// Nguyễn Duy Kiên) và API đổi xe/đổi tài xế khi có sự cố (task kế tiếp của Hoàng): "cùng một hàm
// kiểm tra, chỉ khác điểm gọi" — ghi chú cuối mục "Hai kiểm tra khi tạo lịch trình" của
// api-contract.md.
builder.Services.AddScoped<ITripConflictService, TripConflictService>();

// Đổi xe/đổi tài xế khi có sự cố: PATCH /api/trips/{id}/assignment (US 14 "Phân công điều xe").
// Task story 14 — Phùng Duy Hoàng (file này cũng của Hoàng).
// Gọi ITripConflictService ở trên để kiểm tra trùng lịch nhưng chỉ CẢNH BÁO trong response,
// không chặn — chuyến có sự cố cần đổi được ngay. Thay đổi thành công được AuditLogMiddleware
// ghi tự động (Update / "Trips:{id}") — đúng yêu cầu "ghi log thay đổi" của task, không phải
// gọi ghi log tay.
builder.Services.AddScoped<ITripAssignmentService, TripAssignmentService>();

// Danh sách xe buýt: CRUD /buses (biển số, loại xe, sức chứa, trạng thái).
// Task story 14 — Trần Trung Hiếu. File này của Hoàng nên nhờ Hoàng xem qua trong PR.
builder.Services.AddScoped<IBusService, BusService>();

// Hồ sơ tài xế: CRUD /drivers + ca làm việc /drivers/{id}/trips.
// Tài xế là Users mang vai trò Driver (quy ước A8.4) — không có bảng Drivers riêng.
// Task story 14 — Trần Trung Hiếu. File này của Hoàng nên nhờ Hoàng xem qua trong PR.
builder.Services.AddScoped<IDriverService, DriverService>();

// Gia hạn vé tháng: POST /api/monthly-passes/{id}/renew — ghi thêm MỘT dòng mới, tính ngày hiệu
// lực kế tiếp từ ValidTo của vé cũ (dòng cũ giữ nguyên làm lịch sử).
// Task story 16 — Phùng Duy Hoàng (file này cũng của Hoàng).
// Yêu cầu đăng nhập nhưng KHÔNG gắn policy vai trò (RBAC của dự án chỉ có AdminOnly/ManagerOrAbove):
// hành khách tự gia hạn vé của mình, quyền sở hữu kiểm ở tầng service — vé của người khác trả 404.
// Đăng ký vé tháng (POST /monthly-passes) là task riêng của Trần Trung Hiếu — sẽ có service riêng khi làm.
builder.Services.AddScoped<IMonthlyPassRenewalService, MonthlyPassRenewalService>();

// Tra cứu vé tháng đang hoạt động của chính người gọi: GET /api/monthly-passes/me — chỉ trả vé có
// hiệu lực NGAY LÚC NÀY (ValidFrom <= now <= ValidTo); cố ý không đọc cột Status vì cột đó có độ
// trễ job quét (luật nền của mục hợp đồng).
// Task story 16 — Phùng Duy Hoàng (file này cũng của Hoàng).
// Yêu cầu đăng nhập nhưng KHÔNG gắn policy vai trò — cùng lối IMonthlyPassRenewalService ở trên.
builder.Services.AddScoped<IMonthlyPassLookupService, MonthlyPassLookupService>();

// Job quét vé tháng hết hạn: lật Status Active → Expired cho vé đã qua ValidTo (US 16).
// Task "BackgroundService tự động chuyển vé tháng hết hạn sang trạng thái Expired" — Nguyễn Duy Kiên.
// File này của Hoàng nên nhờ Hoàng xem qua trong PR.
// AddHostedService (không phải AddScoped): job phải sống suốt vòng đời app. Ruột job là
// IMonthlyPassExpiryService ở trên — hosted service là singleton còn AppDbContext là scoped nên nó
// mở scope riêng cho mỗi lượt quét; giải thích đầy đủ ở đầu MonthlyPassExpiryBackgroundService.
// Đặt ngay sau nhóm vé tháng để đọc cùng một mạch.
builder.Services.AddScoped<IMonthlyPassExpiryService, MonthlyPassExpiryService>();
builder.Services.AddHostedService<MonthlyPassExpiryBackgroundService>();

// Nhật ký hoạt động: ghi tự động mọi thao tác thay đổi dữ liệu (US 23).
// Task story 23 — Vàng Thị Dăm. File này của Hoàng nên nhờ Hoàng xem qua trong PR.
builder.Services.AddScoped<IAuditLogService, AuditLogService>();

// Xuất nhật ký kiểm toán ra Excel: GET /api/audit-logs/export.
// Task story 23 — Phùng Duy Hoàng (file này cũng của Hoàng).
// Cố ý tách khỏi IAuditLogService ở trên: API truy vấn danh sách là việc của Kiên.
builder.Services.AddScoped<IAuditLogExportService, AuditLogExportService>();

// Truy vấn danh sách nhật ký kiểm toán: GET /api/audit-logs (lọc + phân trang).
// Task story 23 — Nguyễn Duy Kiên. File này của Hoàng nên nhờ Hoàng xem qua trong PR.
// Cùng tiền tố với IAuditLogExportService ở trên nhưng khác controller — template đầy đủ khác
// nhau nên hai đường dẫn không đụng nhau (đoạn literal "export" thắng đoạn tham số).
builder.Services.AddScoped<IAuditLogQueryService, AuditLogQueryService>();


// Hạn mức gọi API (chống brute-force đăng ký) — Singleton vì bộ đếm phải dùng chung mọi request.
// Task rate-limit của Hiếu (story 22); file này của Hoàng nên nhờ Hoàng xem qua trong PR.
builder.Services.AddSingleton<IRateLimitService, RateLimitService>();

// Gán tài xế vào chuyến theo lô: PATCH /api/routes/{routeId}/trips/driver-assignment (US 14).
// Task story 14 — Nguyễn Duy Kiên. File này của Hoàng nên nhờ Hoàng xem qua trong PR.
// Đây chính là điểm gọi mà ITripConflictService chờ từ lúc được viết: nó lo phép kiểm tra trùng
// lịch, còn chặn hay chỉ cảnh báo là quyết định của nơi gọi — ở đây là CẢNH BÁO trong response.
// Cố ý đăng ký ở CUỐI danh sách (không nằm cạnh ITripConflictService ở trên) để không chèn vào
// đúng khe mà branch feature/14-api-doi-xe-doi-tai-xe của Hoàng đang thêm dòng của anh ấy —
// hai nhánh cùng sửa file này, đặt xa nhau thì merge không phải gỡ tay.
builder.Services.AddScoped<ITripDriverAssignmentService, TripDriverAssignmentService>();

// Xác thực JWT Bearer — cấu hình nằm ở Services/JwtMiddleware.cs
builder.Services.AddJwtAuthentication(builder.Configuration);

// Phân quyền theo vai trò + trả 403 dạng JSON — cấu hình nằm ở Services/RbacMiddleware.cs
builder.Services.AddRbacAuthorization();

// Cho phép frontend gọi API. Origin đọc từ cấu hình (Cors:Origins, nhiều origin cách
// nhau bằng dấu phẩy): lúc chạy local là http://localhost:5173, khi deploy là domain
// thật của frontend — đặt qua biến môi trường Cors__Origins, không phải sửa code.
const string AppCors = "AppCors";
var corsOrigins = (builder.Configuration["Cors:Origins"] ?? "http://localhost:5173")
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
builder.Services.AddCors(o => o.AddPolicy(AppCors, p => p
    .WithOrigins(corsOrigins)
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()));

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Đăng ký vô điều kiện. Trước đây lời gọi này nằm trong khối IsDevelopment() nên khi
// chạy Production (Render) middleware CORS không được gắn vào pipeline, trình duyệt
// chặn mọi lời gọi API dù API vẫn trả 200 khi gọi bằng curl.
app.UseCors(AppCors);

app.UseAuthentication();
app.UseAuthorization();

// Ghi nhật ký hoạt động — cấu hình ở Services/AuditLogMiddleware.cs.
// Đặt sau UseAuthentication để có sẵn người thực hiện trong HttpContext.Items,
// và trước MapControllers để bọc được lời gọi controller.
app.UseAuditLog();

app.MapControllers();

app.Run();

/// <summary>
/// Khai báo tường minh để project test dùng được <c>WebApplicationFactory&lt;Program&gt;</c> —
/// top-level statements mặc định sinh ra class Program ở mức internal.
/// </summary>
public partial class Program;
