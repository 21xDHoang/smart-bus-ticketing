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
// KiemTra đặt trong lambda vì lambda chỉ chạy khi app thật dựng AppDbContext — host test gỡ đăng
// ký này rồi thay bằng InMemory (TestAppFactory) nên không đi qua đây. Máy mới kéo repo về chưa
// cấu hình thì nhận thông báo nêu đúng cách sửa thay vì lỗi Npgsql khó hiểu.
builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseNpgsql(ChuoiKetNoiCsdl.KiemTra(builder.Configuration.GetConnectionString("Default"))));

// Kiểm tra sớm ngay lúc dựng service: thiếu chuỗi kết nối thì app thoát ngay với hướng dẫn 3 bước
// thay vì chạy lên rồi job nền thử lại + mọi request cùng lỗi — người mới không biết đường sửa.
// Bỏ qua ở môi trường Testing: host test thay CSDL bằng InMemory nên không cần chuỗi thật.
if (!builder.Environment.IsEnvironment("Testing"))
{
    ChuoiKetNoiCsdl.KiemTra(builder.Configuration.GetConnectionString("Default"));
}

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

// Job sinh chuyến tự động cho những ngày sắp tới (US 13).
// Task "BackgroundService sinh chuyến tự động từ lịch trình theo ngày" — Nguyễn Duy Kiên.
// File này của Hoàng nên nhờ Hoàng xem qua trong PR.
// AddHostedService (không phải AddScoped): job phải sống suốt vòng đời app. Ruột job là
// ITripGenerationService đăng ký ngay dưới — hosted service là singleton còn AppDbContext là scoped
// nên nó mở scope riêng cho mỗi lượt chạy; giải thích đầy đủ ở đầu TripGenerationBackgroundService.
// Đặt ngay sau IRouteTripsService vì cùng story 13 và cùng bàn về lịch trình: API sinh chuyến hàng
// loạt "gieo" lịch trình một lần, job này nhân bản nó sang những ngày sau — quy ước A8.3 không có
// bảng mẫu nên ngày đã có chuyến chính là mẫu (xem TripGenerationService).
builder.Services.AddScoped<ITripGenerationService, TripGenerationService>();
builder.Services.AddHostedService<TripGenerationBackgroundService>();

// Tra cứu danh sách chuyến theo ngày + lọc theo tuyến: GET /api/trips.
// Task story 13 — Phùng Duy Hoàng (file này cũng của Hoàng).
// Cùng bề mặt /trips với ITripService ở trên nhưng khác bề mặt nghiệp vụ (bộ lọc thay vì định
// danh) nên đứng riêng — cùng khuôn cặp IStopService / IRouteStopService.
builder.Services.AddScoped<ITripLookupService, TripLookupService>();

// Gợi ý trạm dừng cho hành khách: GET /api/stops/search (US 1.0 "Tra cứu tuyến" — ô chọn trạm).
// Task "API gợi ý trạm dừng theo từ khoá (autocomplete)" (Sprint 2 dòng 29) — Hoàng Văn Thịnh.
// File này của Hoàng (Phùng Duy Hoàng) nên nhờ Hoàng xem qua trong PR.
// Công khai có chủ đích, cùng nhóm với hai endpoint story 1 ngay dưới — đặt TRƯỚC
// IRouteSearchService vì đây là bước đứng trước trong luồng: chọn trạm (endpoint này) → tìm tuyến
// → tìm chuyến. Khác IStopService ở trên (CRUD trạm của quản lý, sau policy Admin/Manager):
// service này trả một lát cắt gợi ý và không biết ai đang gọi.
builder.Services.AddScoped<IStopSearchService, StopSearchService>();

// Tìm tuyến cho hành khách theo điểm đi/điểm đến: GET /api/routes/search (US 1 "Tra cứu tuyến").
// Task story 1 — Trần Trung Hiếu (file này của Hoàng nên nhờ Hoàng xem qua trong PR).
// Công khai có chủ đích — đặt ngay trước ITripSearchService bên dưới vì hai endpoint công khai
// của story 1 đứng liền một mạch (tìm tuyến → tìm chuyến), khác hẳn IRouteService ở trên (CRUD
// tuyến của quản lý, sau policy Admin/Manager).
builder.Services.AddScoped<IRouteSearchService, RouteSearchService>();

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
// Đăng ký vé tháng (POST /monthly-passes) là task riêng của Trần Trung Hiếu — service đăng ký ngay dưới.
builder.Services.AddScoped<IMonthlyPassRenewalService, MonthlyPassRenewalService>();

// Đăng ký vé tháng: POST /api/monthly-passes — chọn tuyến + loại vé, thời hạn và giá theo loại.
// Task story 16 — Trần Trung Hiếu (file này của Hoàng nên nhờ Hoàng xem qua trong PR).
// Đặt ngay sau IMonthlyPassRenewalService vì cùng bề mặt api/monthly-passes và cùng luật khoảng
// hiệu lực [ValidFrom, ValidTo): gia hạn nối đuôi vé cũ, đăng ký bắt đầu từ bây giờ.
builder.Services.AddScoped<IMonthlyPassRegistrationService, MonthlyPassRegistrationService>();

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


// Xử lý phản ánh phía quản trị: GET/PATCH /api/admin/feedbacks, GET /api/admin/feedbacks/{id},
// POST /api/admin/feedbacks/{id}/replies (US 24).
// Task "API Admin phản hồi và đổi trạng thái phản ánh" (Sprint 2) — Phùng Duy Hoàng (file này
// cũng của Hoàng).
// ⚠️ Hai bảng Feedbacks/FeedbackReplies đã có entity + cấu hình (Data/AppDbContext.Feedback.cs)
// nhưng CHƯA có migration — việc sinh migration là của Vàng Thị Dăm, xem
// docs/24-huong-dan-migrate-feedbacks.md. Test tích hợp chạy trên InMemory nên không chờ migration.
builder.Services.AddScoped<IFeedbackAdminService, FeedbackAdminService>();

// Phản ánh của hành khách: GET /api/feedbacks/me và GET /api/feedbacks/me/{id} (US 24).
// Task "API danh sách phản ánh của hành khách + theo dõi trạng thái xử lý" (Sprint 2) — Nguyễn Duy Kiên.
// File này của Hoàng nên nhờ Hoàng xem qua trong PR.
// Đặt ngay sau IFeedbackAdminService ở trên vì cùng story 24 và cùng hai bảng — nhưng KHÁC hẳn về
// quyền: nhóm /admin/feedbacks nằm sau policy ManagerOrAbove, còn hai endpoint này là [Authorize]
// trần cho hành khách tự xem phản ánh của mình, quyền sở hữu kiểm ngay trong truy vấn theo userId
// (phản ánh của người khác trả 404 chứ không phải 403). Cùng lối IMonthlyPassLookupService.
// ⚠️ Cũng nằm trên hai bảng CHƯA có migration đó — xem docs/24-huong-dan-migrate-feedbacks.md.
builder.Services.AddScoped<IFeedbackLookupService, FeedbackLookupService>();

// Thống kê phản ánh theo loại và theo tuyến: GET /api/admin/feedbacks/statistics (US 24).
// Task "API thống kê phản ánh theo loại và theo tuyến" (Sprint 2) — Nguyễn Duy Kiên.
// File này của Hoàng nên nhờ Hoàng xem qua trong PR.
// Đặt ngay sau IFeedbackLookupService ở trên để ba service của story 24 đứng liền một mạch: quản lý
// xử lý (Hoàng) → phản ánh của tôi → thống kê. Chỉ đọc và gộp nhóm ở tầng CSDL, không ghi gì.
// Quyền: cùng policy ManagerOrAbove với IFeedbackAdminService (thống kê là số liệu toàn hệ thống),
// KHÁC hẳn IFeedbackLookupService ở trên ([Authorize] trần cho hành khách).
builder.Services.AddScoped<IFeedbackStatisticsService, FeedbackStatisticsService>();

// Gửi phản ánh: POST /api/feedbacks (US 24). Task "API gửi phản ánh: chọn chuyến, loại phản ánh,
// nội dung, đính kèm ảnh" (Sprint 2) — Trần Trung Hiếu. File này của Hoàng nên nhờ Hoàng xem qua
// trong PR.
// Đặt ngay sau IFeedbackStatisticsService ở trên để bốn service của story 24 đứng liền một mạch:
// quản lý xử lý (Hoàng) → phản ánh của tôi → thống kê → gửi phản ánh. Chỉ ghi vào bảng Feedbacks
// (entity + cấu hình của Dăm, migration 20261003124532_Sprint2_Feedbacks_FeedbackReplies đã có).
// Quyền: [Authorize] trần cho hành khách tự gửi — cùng lối IFeedbackLookupService, KHÁC hẳn hai
// service nằm sau policy ManagerOrAbove.
builder.Services.AddScoped<IFeedbackSubmissionService, FeedbackSubmissionService>();

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

// Giữ chỗ tạm thời (US 3): job quét các lượt giữ đã quá hạn để nhả ghế và ghi nhật ký SeatHoldLogs.
// Task "Migrate bảng SeatHoldLogs + job quét hold hết hạn" — Vàng Thị Dăm.
// File này của Hoàng nên nhờ Hoàng xem qua trong PR.
// AddHostedService (không phải AddScoped): job phải sống suốt vòng đời app. Ruột job là
// ISeatHoldExpiryService đăng ký ngay trên — hosted service là singleton còn AppDbContext là scoped
// nên nó mở scope riêng cho mỗi lượt quét; giải thích đầy đủ ở đầu SeatHoldExpiryBackgroundService.
// Đặt ở CUỐI danh sách đăng ký, cạnh ITripDriverAssignmentService ở trên, để không chèn vào đúng
// khe mà các nhánh Sprint 3 khác đang thêm dòng — cùng lý do dòng ITripDriverAssignmentService.
builder.Services.AddScoped<ISeatHoldExpiryService, SeatHoldExpiryService>();
builder.Services.AddHostedService<SeatHoldExpiryBackgroundService>();

// Cảnh báo tài khoản giữ chỗ quá nhiều lần (US 3): job canh tần suất giữ chỗ theo tài khoản, ghi
// AuditLogs (Action = Warning) + log ứng dụng khi vượt ngưỡng.
// Task "Ghi log và cảnh báo khi một tài khoản giữ chỗ quá nhiều lần" — Vàng Thị Dăm.
// File này của Hoàng nên nhờ Hoàng xem qua trong PR.
// Đặt ngay sau khối SeatHold ở trên vì cùng story 3 và cùng bảng SeatHolds: job kia NHẢ ghế hết hạn,
// job này ĐẾM số lần giữ chỗ của một tài khoản — hai việc khác nhau trên cùng một bảng, đừng gộp.
// Cùng lối AddScoped (ruột) + AddHostedService (vòng lặp): hosted service là singleton còn
// AppDbContext là scoped nên nó mở scope riêng cho mỗi lượt quét; giải thích đầy đủ ở đầu
// SeatHoldAbuseBackgroundService. Đăng ký ở CUỐI danh sách, cạnh hai dòng SeatHold kia, để không
// chèn vào đúng khe mà các nhánh Sprint 3 khác đang thêm dòng.
builder.Services.AddScoped<ISeatHoldAbuseService, SeatHoldAbuseService>();
builder.Services.AddHostedService<SeatHoldAbuseBackgroundService>();

// Gia hạn thời gian giữ chỗ theo mã phiên: POST /api/seat-holds/{sessionCode}/extend (US 3).
// Task "API gia hạn thời gian giữ chỗ (tối đa 1 lần)" — Trần Trung Hiếu.
// File này của Hoàng nên nhờ Hoàng xem qua trong PR.
// Yêu cầu đăng nhập nhưng KHÔNG gắn policy vai trò — hành khách tự gia hạn phiên giữ chỗ của
// mình, quyền sở hữu kiểm ngay trong truy vấn theo userId (phiên của người khác trả 404 chứ
// không 403). Đặt ở CUỐI danh sách đăng ký, cạnh khối SeatHold, để không chèn vào đúng khe mà
// các nhánh Sprint 3 khác đang thêm dòng.
builder.Services.AddScoped<ISeatHoldExtendService, SeatHoldExtendService>();

// Kiểm tra trạng thái giữ chỗ theo mã phiên: GET /api/seat-holds/{sessionCode} (US 3).
// Task "API kiểm tra trạng thái giữ chỗ theo mã phiên" — Trần Trung Hiếu.
// File này của Hoàng nên nhờ Hoàng xem qua trong PR.
// Yêu cầu đăng nhập nhưng KHÔNG gắn policy vai trò — hành khách tự xem phiên giữ chỗ của mình,
// quyền sở hữu kiểm ngay trong truy vấn theo userId (phiên của người khác trả 404 chứ không 403).
// Đặt ở CUỐI danh sách đăng ký, cạnh khối SeatHold, để không chèn vào đúng khe mà các nhánh
// Sprint 3 khác đang thêm dòng.
builder.Services.AddScoped<ISeatHoldLookupService, SeatHoldLookupService>();

// Sơ đồ ghế theo chuyến cho hành khách: GET /api/trips/{tripId}/seats (US 2 "Chọn vị trí ghế").
// Task "API lấy sơ đồ ghế theo chuyến + trạng thái từng ghế" — Trần Trung Hiếu.
// File này của Hoàng nên nhờ Hoàng xem qua trong PR.
// Công khai có chủ đích, cùng luồng tra cứu của hành khách với ITripSearchService ở trên.
// Đặt ở CUỐI danh sách đăng ký, cạnh khối SeatHold, để không chèn vào đúng khe mà các nhánh
// Sprint 3 khác đang thêm dòng.
builder.Services.AddScoped<ITripSeatMapService, TripSeatMapService>();

// Giữ ghế tạm thời (khóa ghế theo phiên): POST /api/seat-holds (US 2 "Chọn vị trí ghế còn trống").
// Task "API giữ ghế tạm thời (khóa ghế theo phiên)" — Nguyễn Duy Kiên.
// File này của Hoàng nên nhờ Hoàng xem qua trong PR.
// Yêu cầu đăng nhập nhưng KHÔNG gắn policy vai trò — hành khách tự giữ ghế cho mình, người giữ lấy
// từ claim NameIdentifier chứ không có trong body. Đây là endpoint SINH mã phiên của cả bề mặt
// /seat-holds: ba endpoint kia (tra trạng thái, gia hạn, nhả ghế) đều hỏi theo sessionCode do nó
// trả về. Đặt ở CUỐI danh sách đăng ký, cạnh khối SeatHold, để không chèn vào đúng khe mà các
// nhánh Sprint 3 khác đang thêm dòng.
builder.Services.AddScoped<ISeatHoldCreateService, SeatHoldCreateService>();

// Cổng thanh toán MoMo (US 6): cấu hình đối tác + client thuần HTTP nói chuyện với cổng.
// Task "Tích hợp SDK MoMo: tạo giao dịch, nhận callback" — Trần Trung Hiếu.
// File này của Hoàng nên nhờ Hoàng xem qua trong PR.
// PartnerCode/AccessKey/SecretKey KHÔNG có giá trị mặc định trong code (luật 2 — repo PUBLIC):
// đặt qua dotnet user-secrets hoặc biến môi trường MoMo__SecretKey…, mục "MoMo" trong
// appsettings.Development.json.example chỉ là khuôn để điền. Client chưa đụng CSDL — bảng
// Payments là migration của Dăm, service nghiệp vụ thanh toán sẽ gọi vào đây khi bảng có.
// AddHttpClient đăng ký MoMoGatewayService dạng transient (kèm HttpClient có BaseAddress từ
// cấu hình, timeout mặc định của factory); AddScoped cho interface để controller/service
// nghiệp vụ chỉ cần biết IMoMoGatewayService. Đặt ở CUỐI danh sách đăng ký để không chèn vào
// đúng khe mà các nhánh Sprint 3 khác đang thêm dòng.
builder.Services.Configure<MoMoOptions>(builder.Configuration.GetSection(MoMoOptions.SectionName));
builder.Services.AddHttpClient<MoMoGatewayService>();
builder.Services.AddScoped<IMoMoGatewayService>(provider => provider.GetRequiredService<MoMoGatewayService>());

// Nhả ghế khi khách huỷ thao tác giữ chỗ theo mã phiên:
// POST /api/seat-holds/{sessionCode}/release (US 3).
// Task "API nhả ghế khi hết hạn hoặc khách huỷ thao tác" — Phùng Duy Hoàng.
// Yêu cầu đăng nhập nhưng KHÔNG gắn policy vai trò — hành khách tự nhả phiên giữ chỗ của mình,
// quyền sở hữu kiểm ngay trong truy vấn theo userId (phiên của người khác trả 404 chứ không 403).
// KHÁC khối gia hạn ngay trên ở một điểm: endpoint này nhận CẢ phiên vừa quá hạn mà job nền
// SeatHoldExpiryBackgroundService chưa quét (cố ý không kiểm ExpiresAt) và trả 200 khi phiên đã
// Expired/Released từ trước — giải thích đầy đủ ở đầu SeatHoldReleaseService.
// Đặt ở CUỐI danh sách đăng ký (sau khối MoMo, trước phần hạ tầng JWT/RBAC/CORS) để không chèn
// vào đúng khe mà các nhánh Sprint 3 khác đang thêm dòng.
builder.Services.AddScoped<ISeatHoldReleaseService, SeatHoldReleaseService>();

// Cổng thanh toán VNPay (US 6): cấu hình đối tác + client dựng URL ký sẵn để chuyển khách sang cổng
// và kiểm chữ ký tham số cổng trả về (Return URL + IPN).
// Task "Tích hợp VNPay: tạo URL thanh toán + verify chữ ký" — Nguyễn Duy Kiên.
// File này của Hoàng nên nhờ Hoàng xem qua trong PR.
// Khác khối MoMo ngay trên một điểm bản chất: VNPay không có API server-to-server để tạo giao dịch
// — client này KHÔNG gọi mạng nên không cần AddHttpClient, chỉ hai dòng đăng ký thuần. TmnCode/
// HashSecret KHÔNG có giá trị mặc định trong code (luật 2 — repo PUBLIC): đặt qua dotnet
// user-secrets hoặc biến môi trường VnPay__HashSecret…, mục "VnPay" trong
// appsettings.Development.json.example chỉ là khuôn để điền. Client chưa đụng CSDL — bảng Payments
// là migration của Dăm, service nghiệp vụ thanh toán sẽ gọi vào đây khi bảng có.
// Đặt ở CUỐI danh sách đăng ký (sau khối SeatHold, trước phần hạ tầng JWT/RBAC/CORS) để không chèn
// vào đúng khe mà các nhánh Sprint 3 khác đang thêm dòng.
builder.Services.Configure<VnPayOptions>(builder.Configuration.GetSection(VnPayOptions.SectionName));
builder.Services.AddScoped<IVnPayGatewayService, VnPayGatewayService>();

// Cổng thanh toán ZaloPay (US 6): cấu hình đối tác + client thuần HTTP gọi cổng.
// Task "Tích hợp ZaloPay và thẻ ngân hàng" — Nguyễn Duy Kiên.
// File này của Hoàng nên nhờ Hoàng xem qua trong PR.
// Cùng lối khối MoMo ở trên (AddHttpClient), KHÁC khối VNPay ngay trên: ZaloPay tạo đơn bằng HTTP
// POST sang cổng nên cần HttpClient. Ba giá trị AppId/Key1/Key2 KHÔNG có mặc định trong code
// (luật 2 — repo PUBLIC): đặt qua dotnet user-secrets hoặc biến môi trường ZaloPay__Key1…, mục
// "ZaloPay" trong appsettings.Development.json.example chỉ là khuôn để điền. Client chưa đụng CSDL —
// bảng Payments là migration của Dăm, service nghiệp vụ thanh toán sẽ gọi vào đây khi bảng có.
// Đặt ở CUỐI danh sách đăng ký (sau khối VNPay, trước phần hạ tầng JWT/RBAC/CORS) để không chèn
// vào đúng khe mà các nhánh Sprint 3 khác đang thêm dòng.
builder.Services.Configure<ZaloPayOptions>(builder.Configuration.GetSection(ZaloPayOptions.SectionName));
builder.Services.AddHttpClient<ZaloPayGatewayService>();
builder.Services.AddScoped<IZaloPayGatewayService>(provider => provider.GetRequiredService<ZaloPayGatewayService>());

// Lớp adapter thống nhất cổng thanh toán (US 6): endpoint POST /payments và hai endpoint callback
// tra cổng qua IPaymentGatewayResolver theo mã phương thức thay vì gọi thẳng client từng cổng —
// thêm cổng mới chỉ là viết một adapter + một dòng đăng ký ở đây và một dòng trong bảng của
// PaymentGatewayResolver, không phải sửa endpoint.
// Task "Adapter pattern thống nhất cổng thanh toán (dễ thêm cổng mới)" — Phùng Duy Hoàng.
// File này của Hoàng nên nhờ Hoàng xem qua trong PR.
// Adapter là lớp MỎNG bọc nguyên client MoMo (Hiếu) và VNPay/ZaloPay (Kiên) ngay trên — các client
// đó không sửa gì. Adapter không có cấu hình riêng; thiếu khoá bí mật của cổng nào thì client cổng
// đó ném ngay khi adapter được dựng, và resolver chỉ dựng đúng cổng được hỏi (luật 2).
// Bốn adapter ứng với bốn mã phương thức của hợp đồng: MoMo, VNPay, ZaloPay, BankCard (BankCard
// dùng lại client VNPay với kênh thẻ ghim sẵn — xem BankCardGatewayAdapter).
// Đặt ở CUỐI danh sách đăng ký (sau khối ZaloPay, trước phần hạ tầng JWT/RBAC/CORS) để không chèn
// vào đúng khe mà các nhánh Sprint 3 khác đang thêm dòng.
builder.Services.AddScoped<MoMoGatewayAdapter>();
builder.Services.AddScoped<VnPayGatewayAdapter>();
builder.Services.AddScoped<ZaloPayGatewayAdapter>();
builder.Services.AddScoped<BankCardGatewayAdapter>();
builder.Services.AddScoped<IPaymentGatewayResolver, PaymentGatewayResolver>();

// Chốt thanh toán idempotent (US 6): điểm vào duy nhất cho callback cổng (MoMo IPN / VNPay
// Return+IPN) và job đối soát — kiểm trạng thái trước khi lật, callback trùng bị bỏ qua.
// Task "Xử lý idempotency: chống trừ tiền 2 lần khi callback trùng" — Phùng Duy Hoàng.
// File này của Hoàng nên nhờ Hoàng xem qua trong PR.
// Service chỉ chốt TRẠNG THÁI giao dịch; bước phát hành vé + xác nhận giữ chỗ là của người gọi
// (chưa dựng — chờ bảng Tickets; đọc cờ ShouldIssueTickets trong PaymentSettlementOutcome).
// Đặt ở CUỐI danh sách đăng ký (sau khối adapter cổng, trước phần hạ tầng JWT/RBAC/CORS) để
// không chèn vào đúng khe mà các nhánh Sprint 3 khác đang thêm dòng.
builder.Services.AddScoped<IPaymentSettlementService, PaymentSettlementService>();

// Voucher (US 18): kiểm tra mã cho một đơn hàng (POST /vouchers/validate) và tiêu thụ mã khi giao
// dịch đã thu được tiền. Task "API kiểm tra và áp dụng voucher vào đơn hàng" — Nguyễn Duy Kiên.
// File này của Hoàng nên nhờ Hoàng xem qua trong PR.
// Hai service dùng CHUNG một bộ luật (VoucherValidationService.ReasonFor/DiscountFor): chép luật
// sang nhánh tiêu thụ là hai bản sẽ lệch nhau, và bản lệch sẽ là bản quyết định tiền.
// Đặt ở CUỐI danh sách đăng ký (sau khối chốt thanh toán, trước phần hạ tầng JWT/RBAC/CORS) để
// không chèn vào đúng khe mà các nhánh Sprint 3 khác đang thêm dòng.
builder.Services.AddScoped<IVoucherValidationService, VoucherValidationService>();
builder.Services.AddScoped<IVoucherRedemptionService, VoucherRedemptionService>();

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
