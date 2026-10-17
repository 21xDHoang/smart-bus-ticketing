using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartBus.Api.Data;
using SmartBus.Api.Hubs;
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

    // Thiếu khoá VAPID thì bề mặt Web Push hỏng tận lúc khách bấm "Bật thông báo" — báo ngay ở
    // đây thay vì để lỗi nổ xa chỗ khai báo (docs/54 §2). Cùng lối kiểm tra chuỗi kết nối ở trên.
    WebPushOptions.KiemTra(builder.Configuration.GetSection(WebPushOptions.SectionName));
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
// Thông báo trễ/đổi lịch trình (US 10, dòng 44 Sprint 4 — Phùng Duy Hoàng): từ đây
// IRouteTripsService resolve ra CHUỖI HAI LỚP BỌC, ngoài cùng là NotifyingRouteTripsService:
//
//   IRouteTripsService → NotifyingRouteTripsService → ArrivalEstimatingRouteTripsService
//                                                                   → RouteTripsService (lớp trần)
//
// - Notifying (ngoài cùng) báo tin khi PUT /routes/{routeId}/trips/{id} đổi giờ chạy thật.
// - ArrivalEstimating (giữa) điền giờ đến cho các chuyến vừa sinh hàng loạt — màn hình "Cấu hình
//   tần suất" không còn cột Giờ đến trống; job nhân bản ngày sau cũng có mốc để chép sang.
// Cả hai đều là decorator để RouteTripsService (file của Hiếu — luật E1) không phải sửa. Lớp ước
// lượng đăng ký bằng KIỂU RIÊNG (không phải IRouteTripsService) để tránh vòng lặp DI; Notifying
// nhận IRouteTripsService nên đọc được lớp giữa. Cùng lối RefundAttemptService bên dưới: lớp trần
// trước, lớp bọc sau; DI lấy bản đăng ký sau cùng nên ai thêm dòng IRouteTripsService nữa ở dưới sẽ
// thay thế im lặng (TripScheduleChangeWiringTests ghim Notifying ở ngoài cùng). Notifier tự quyết
// định "có gì để báo" bằng cách so ảnh chụp giờ cũ với dữ liệu đã lưu; hợp đồng không-bao-giờ-ném-
// lỗi + lý do bọc decorator ở Services/ITripScheduleChangeNotifier.cs và NotifyingRouteTripsService.cs.
builder.Services.AddScoped<RouteTripsService>();
builder.Services.AddScoped<ArrivalEstimatingRouteTripsService>(provider =>
    new ArrivalEstimatingRouteTripsService(
        provider.GetRequiredService<RouteTripsService>(),
        provider.GetRequiredService<AppDbContext>(),
        provider.GetRequiredService<ILogger<ArrivalEstimatingRouteTripsService>>()));
builder.Services.AddScoped<ITripScheduleChangeNotifier, TripScheduleChangeNotifier>();
builder.Services.AddScoped<IRouteTripsService>(provider => new NotifyingRouteTripsService(
    provider.GetRequiredService<ArrivalEstimatingRouteTripsService>(),
    provider.GetRequiredService<ITripScheduleChangeNotifier>(),
    provider.GetRequiredService<AppDbContext>(),
    provider.GetRequiredService<ILogger<NotifyingRouteTripsService>>()));

// Giờ đến TỪNG TRẠM của một chuyến: GET/PUT /routes/{routeId}/trips/{id}/stop-times.
// Giờ dự kiến suy ra từ RouteStops.DistanceKm khi đọc; bảng TripStopTimeOverrides chỉ giữ mốc
// quản trị viên sửa tay. Service riêng (không bọc IRouteTripsService) vì đây là dòng thời gian
// của một chuyến, không phải thao tác lịch trình — và để không phải sửa file của Hiếu.
// Hợp đồng: docs/api-contract.md mục "Giờ đến từng trạm".
builder.Services.AddScoped<ITripStopTimeService, TripStopTimeService>();

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

// Gợi ý trạm cho ô điểm đi/điểm đến của màn tra cứu tuyến: GET /api/stops/suggestions.
// Sửa lỗi trải nghiệm màn "Tra cứu tuyến" (Sprint 5 — Vàng Thị Dăm). Khác IStopSearchService ở
// trên: chỉ trả trạm thuộc tuyến Active và lọc được theo thứ tự trạm, để ô điểm đến không gợi ý
// trạm của tuyến đã xoá hay trạm ngược chiều.
// File này của Hoàng (Phùng Duy Hoàng) nên nhờ Hoàng xem qua trong PR.
builder.Services.AddScoped<IStopSuggestionService, StopSuggestionService>();

// Gợi ý tuyến kèm lịch chạy quanh ngày đã chọn: GET /api/routes/suggestions.
// Sửa lỗi trải nghiệm màn "Tra cứu tuyến" (Sprint 5 — Vàng Thị Dăm). Khác IRouteSearchService ở
// trên ở chỗ ngày KHÔNG lọc tuyến: ngày không có chuyến vẫn trả tuyến kèm ngày gần nhất và mã lý
// do trống, thay vì mảng rỗng.
// File này của Hoàng nên nhờ Hoàng xem qua trong PR.
builder.Services.AddScoped<IRouteSuggestionService, RouteSuggestionService>();

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

// Cập nhật vị trí hiện tại của chuyến: PUT /api/trips/{id}/location (US 10 "Thông báo trạm",
// Sprint 4). Task "API cập nhật vị trí chuyến (CurrentLat/CurrentLng/CurrentStopId)" — Trần
// Trung Hiếu. File này của Hoàng nên nhờ Hoàng xem qua trong PR.
// Chỉ tài xế được phân công của chuyến ghi được (kiểm theo DriverId ngay trong truy vấn); chuyến
// Scheduled/Running nhận vị trí, Completed/Cancelled → 409. Thay đổi thành công được
// AuditLogMiddleware ghi tự động (Update / "Trips:{id}") — cùng lối TripAssignmentService ở trên.
// Đặt cạnh khối chuyến xe vì cùng bảng Trips.
builder.Services.AddScoped<ITripLocationService, TripLocationService>();

// Danh sách xe buýt: CRUD /buses (biển số, loại xe, sức chứa, trạng thái).
// Task story 14 — Trần Trung Hiếu. File này của Hoàng nên nhờ Hoàng xem qua trong PR.
builder.Services.AddScoped<IBusService, BusService>();

// Sơ đồ ghế theo loại xe: CRUD /seat-layouts — lưu sơ đồ là SINH GHẾ luôn cho mọi xe cùng loại
// đang chưa có ghế (không có nút "sinh ghế" riêng); nhờ đó hành khách mới chọn được ghế.
// Task story 2 — Phùng Duy Hoàng (file này cũng của Hoàng).
builder.Services.AddScoped<ISeatLayoutService, SeatLayoutService>();

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

// Xuất nhật ký kiểm toán ra Excel/PDF: GET /api/audit-logs/export.
// Task story 23 — Phùng Duy Hoàng (file này cũng của Hoàng).
// Cố ý tách khỏi IAuditLogService ở trên: API truy vấn danh sách là việc của Kiên.
builder.Services.AddScoped<IAuditLogExportService, AuditLogExportService>();

// Truy vấn danh sách nhật ký kiểm toán: GET /api/audit-logs (lọc + phân trang).
// Task story 23 — Nguyễn Duy Kiên. File này của Hoàng nên nhờ Hoàng xem qua trong PR.
// Cùng tiền tố với IAuditLogExportService ở trên nhưng khác controller — template đầy đủ khác
// nhau nên hai đường dẫn không đụng nhau (đoạn literal "export" thắng đoạn tham số).
builder.Services.AddScoped<IAuditLogQueryService, AuditLogQueryService>();

// Service xuất PDF báo cáo (US 21 "Xuất dữ liệu", Sprint 4): báo cáo A4 có tiêu đề, logo, ngày
// xuất — viên gạch chung cho mọi báo cáo xuất PDF (doanh thu / lấp đầy / nhật ký).
// Task dòng 77 "Service xuất PDF có tiêu đề, logo, ngày xuất" — Trần Trung Hiếu.
// File này của Hoàng nên nhờ Hoàng xem qua trong PR.
// Service thuần (không CSDL, không mạng): ngày xuất do bên gọi truyền vào qua ReportPdfModel.
// Đặt cạnh khối xuất nhật ký ra Excel ở trên vì cùng chủ đề "xuất file" của Sprint 4.
builder.Services.AddScoped<IReportPdfService, ReportPdfService>();

// Service xuất Excel dùng chung (US 21 "Xuất dữ liệu", Sprint 5): ba báo cáo doanh thu / tỷ lệ
// lấp đầy / nhật ký cùng dựng file qua một khuôn thay vì ba bản BuildWorkbook lặp lại.
// Task dòng 61 "Service dùng chung để xuất Excel (ClosedXML) cho mọi báo cáo" — Nguyễn Duy Kiên.
// File này của Hoàng nên nhờ Hoàng xem qua trong PR.
// Service thuần (không CSDL, không mạng): nhận ReportExcelModel đã gom sẵn từ bên gọi.
builder.Services.AddScoped<IReportExcelService, ReportExcelService>();


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

// Voucher (US 18), phần đọc: thống kê hiệu quả voucher đã phát hành
// (GET /vouchers/statistics). Task "API thống kê hiệu quả voucher" — Nguyễn Duy Kiên.
// File này của Hoàng nên nhờ Hoàng xem qua trong PR.
// Controller tự được AddControllers quét ra, không cần dòng đăng ký riêng.
builder.Services.AddScoped<IVoucherStatisticsService, VoucherStatisticsService>();

// Voucher (US 18), phần ghi: CRUD voucher cho màn quản trị (GET/POST /vouchers,
// GET/PUT /vouchers/{id}, PATCH /vouchers/{id}/status). Task "API CRUD voucher: mã, loại giảm
// giá, điều kiện áp dụng, số lượng" — Trần Trung Hiếu.
// File này của Hoàng nên nhờ Hoàng xem qua trong PR.
// Đặt ngay sau IVoucherStatisticsService vì cùng story 18 và cùng bảng Vouchers: thống kê đọc
// (Kiên), CRUD ghi (Hiếu) — hai việc khác nhau trên cùng một bảng, đừng gộp. KHÁC hẳn cặp
// IVoucherValidationService/IVoucherRedemptionService ở trên (luồng khách dùng mã ở thanh toán).
builder.Services.AddScoped<IVoucherAdminService, VoucherAdminService>();

// Báo cáo doanh thu theo ngày / tuần / tháng: GET /api/reports/revenue (US 19 "Doanh thu",
// Sprint 4). Task "API báo cáo doanh thu theo ngày / tuần / tháng" — Trần Trung Hiếu.
// File này của Hoàng nên nhờ Hoàng xem qua trong PR.
// Nền là bảng Payments (chỉ đếm Success — tiền đã thu thực, khớp kiểm thử chéo của story), trục
// thời gian theo giờ Việt Nam UTC+7, mốc giao dịch là PaidAt (thiếu thì dùng CreatedAt).
// Đặt sau khối voucher vì cùng nhóm "đọc tổng hợp từ bảng đã có" — báo cáo không ghi dữ liệu.
builder.Services.AddScoped<IRevenueReportService, RevenueReportService>();

// Đệm kết quả báo cáo theo kỳ: GET /api/reports/revenue (US 19 "Doanh thu", Sprint 5 dòng 43).
// Task "Cache kết quả báo cáo theo kỳ để tăng tốc" — Vàng Thị Dăm.
// File này của Hoàng nên nhờ Hoàng xem qua trong PR.
// Không sửa RevenueReportService (file của Hiếu — quy ước E1): bọc bằng
// CachedRevenueReportService, cùng lối CachedTripSearchService bọc TripSearchService ở trên.
// DI lấy bản đăng ký SAU CÙNG nên ba dòng dưới đây phải nằm ngay sau dòng của Hiếu — ai đăng ký
// thêm IRevenueReportService ở dưới nữa thì bản có đệm bị thay thế im lặng. RevenueComparisonService
// (đăng ký ngay sau) cũng gọi IRevenueReportService nên được hưởng đệm mà không phải sửa gì.
builder.Services.AddScoped<RevenueReportService>();
builder.Services.AddSingleton<IRevenueReportCache, RevenueReportCache>();
builder.Services.AddScoped<IRevenueReportService>(provider => new CachedRevenueReportService(
    provider.GetRequiredService<RevenueReportService>(),
    provider.GetRequiredService<IRevenueReportCache>()));

// So sánh doanh thu kỳ hiện tại với kỳ liền trước cùng độ dài: GET /api/reports/revenue/comparison
// (US 19 "Doanh thu", Sprint 4). Task "API so sánh doanh thu giữa các kỳ (tăng/giảm %)" —
// Phùng Duy Hoàng. Cùng bề mặt /reports/revenue với báo cáo của Hiếu nhưng controller riêng
// (RevenueComparisonController) — mỗi task một controller.
// Service KHÔNG tự truy vấn Payments: gọi IRevenueReportService hai lần (kỳ hiện tại + kỳ trước)
// để hai endpoint dùng chung đúng một nguồn số. Đặt ngay sau báo cáo gốc vì phụ thuộc trực tiếp
// vào nó — nhìn danh sách đăng ký là thấy quan hệ.
builder.Services.AddScoped<IRevenueComparisonService, RevenueComparisonService>();

// Doanh thu theo tuyến / theo xe / theo phương thức thanh toán:
// GET /api/reports/revenue/by-route, /by-bus, /by-payment-method (US 19 "Doanh thu", Sprint 4).
// Task "API doanh thu theo tuyến, theo xe, theo phương thức thanh toán" — Nguyễn Duy Kiên.
// File này của Hoàng nên nhờ Hoàng xem qua trong PR.
// Cùng nền Payments Success + cùng trục giờ Việt Nam với báo cáo thời gian ở trên, nhưng controller
// riêng (RevenueBreakdownController) và KHÔNG đi qua đệm — ba báo cáo này trả dòng theo nhóm, không
// phải bucket thời gian, nên không dùng chung khoá đệm của RevenueReportCache.
builder.Services.AddScoped<IRevenueBreakdownService, RevenueBreakdownService>();

// Tỷ lệ lấp đầy theo chuyến / tuyến / khung giờ: GET /api/reports/fill-rate (US 20 "Tỷ lệ lấp
// đầy", Sprint 4). Task "API thống kê tỷ lệ lấp đầy theo chuyến, tuyến, khung giờ" — Nguyễn Duy
// Kiên. File này của Hoàng nên nhờ Hoàng xem qua trong PR.
// Cùng nền Trips + vé Paid/Used + trục giờ Việt Nam với báo cáo khung giờ cao điểm, nhưng mẫu số
// là sức chứa của xe (Buses.Capacity) chứ không phải số chuyến; một endpoint trả cả ba cách nhìn
// nên không tái dùng IPeakHoursReportService. Không đệm, không migration.
builder.Services.AddScoped<IFillRateReportService, FillRateReportService>();

// Xuất báo cáo doanh thu ra Excel/PDF: GET /api/reports/revenue/export (US 24 "Xuất dữ liệu",
// Sprint 4 dòng 78). Task "API xuất báo cáo doanh thu / lấp đầy / nhật ký ra file" — Phùng Duy
// Hoàng. File này của Hoàng nên nhờ Hoàng xem qua trong PR.
// Service KHÔNG tự tính doanh thu: gọi IRevenueReportService (bản có đệm 60 giây đăng ký ngay
// trên) rồi dàn kết quả ra file — số trong file luôn khớp số endpoint JSON trả về. Vì vậy đăng ký
// ở đây KHÔNG được chèn một dòng IRevenueReportService nào phía dưới (xem cảnh báo ở dòng đệm).
builder.Services.AddScoped<IRevenueExportService, RevenueExportService>();

// Xuất báo cáo tỷ lệ lấp đầy ra Excel/PDF: GET /api/reports/fill-rate/export (US 20 "Tỷ lệ lấp
// đầy", Sprint 4 dòng 78). Cùng task — Phùng Duy Hoàng.
// Service đọc thẳng Trips/Buses/Tickets theo định nghĩa đã chốt trong docs/api-contract.md. Báo cáo
// JSON cùng nội dung đã có ở GET /reports/fill-rate (dòng 66, Kiên), nhưng service xuất cố ý chưa
// gọi lại để PR này không phụ thuộc thứ tự merge; gộp phép tính về một chỗ là việc tiếp theo
// (docs/49 §6). Không ghi dữ liệu, không migration.
builder.Services.AddScoped<IFillRateExportService, FillRateExportService>();

// Mã QR vé điện tử (US 4, Sprint 3): sinh mã DUY NHẤT + ký HMAC-SHA256 chống làm giả — mã chính là
// cột Tickets.Code (unique toàn hệ thống; ngân sách 200 ký tự, docs/28 §2/§6).
// Task "Service sinh mã QR duy nhất + ký số chống làm giả" — Nguyễn Duy Kiên.
// Service THUẦN ký/kiểm, không chạm CSDL: bên phát hành vé (Hiếu) gọi GenerateCode rồi tự lưu và tự
// xử lý lượt đâm unique index (gọi lại là có mã mới — nonce); endpoint soát vé dòng 43 / Sprint 4
// dòng 15 gọi TryVerify trước khi tra bảng. Khoá ký KHÔNG có mặc định trong code (luật 2 — repo
// PUBLIC): đặt qua dotnet user-secrets hoặc biến môi trường TicketQr__SigningKey; mục "TicketQr"
// trong appsettings.Development.json.example chỉ là khuôn để điền.
// Đặt ở CUỐI danh sách đăng ký (sau khối voucher, trước phần hạ tầng JWT/RBAC/CORS) để không chèn
// vào đúng khe mà các nhánh Sprint 3 khác đang thêm dòng.
builder.Services.Configure<TicketQrOptions>(builder.Configuration.GetSection(TicketQrOptions.SectionName));
builder.Services.AddScoped<ITicketQrService, TicketQrService>();

// Vé điện tử PDF + gửi kèm qua email (US 4, Sprint 3): vẽ vé A5 có mã QR (QuestPDF + ZXing, font
// Roboto nhúng trong assembly) rồi gửi qua SMTP (MailKit). Task "Sinh file PDF vé có mã QR + gửi
// kèm qua email" — Phùng Duy Hoàng.
// Tầng gửi thư tách khỏi tầng soạn thư: IEmailSender là chỗ DUY NHẤT biết MailKit; ITicketEmailService
// chỉ soạn nội dung + đính kèm PDF nên test được bằng đồ giả. QR in từ Tickets.Code ĐÃ LƯU — không
// sinh lại (mỗi lượt sinh ra một mã khác — docs/30 §3.1).
// Cấu hình SMTP KHÔNG có mặc định trong code (luật 2 — repo PUBLIC): đặt qua dotnet user-secrets
// hoặc biến môi trường Email__Host/Email__UserName/Email__Password/Email__FromAddress; mục "Email"
// trong appsettings.Development.json.example chỉ là khuôn để điền.
// Bên gọi (luồng phát hành vé — Hiếu) bọc try/catch: email hỏng KHÔNG được làm hỏng việc phát hành
// vé (docs/31 §5).
// Đặt ở CUỐI danh sách đăng ký (sau khối mã QR, trước phần hạ tầng JWT/RBAC/CORS) để không chèn
// vào đúng khe mà các nhánh Sprint 3 khác đang thêm dòng.
builder.Services.Configure<EmailOptions>(builder.Configuration.GetSection(EmailOptions.SectionName));
builder.Services.AddScoped<IEmailSender, SmtpEmailSender>();
builder.Services.AddScoped<ITicketPdfService, TicketPdfService>();
builder.Services.AddScoped<ITicketEmailService, TicketEmailService>();

// Vé điện tử của chính người gọi: GET /api/tickets/me và GET /api/tickets/{id} (US 4).
// Task "API tra cứu vé của tôi (đã mua / đã dùng / đã huỷ)" — Trần Trung Hiếu.
// File này của Hoàng nên nhờ Hoàng xem qua trong PR.
// Yêu cầu đăng nhập nhưng KHÔNG gắn policy vai trò — hành khách tự xem vé của mình, quyền sở hữu
// kiểm ngay trong truy vấn theo userId (vé của người khác trả 404 chứ không 403). Đặt ngay sau
// khối PDF/email vé vì cùng story 4 và cùng bảng Tickets: phát hành (service nội bộ), soạn vé
// (Hoàng) và tra cứu vé của tôi (endpoint này) đứng liền một mạch.
builder.Services.AddScoped<IMyTicketsService, MyTicketsService>();

// Khung giờ cao điểm theo mật độ hành khách: GET /api/reports/peak-hours (US 20 "Tỷ lệ lấp đầy",
// Sprint 4). Task "API phân tích khung giờ cao điểm theo mật độ hành khách" — Trần Trung Hiếu.
// File này của Hoàng nên nhờ Hoàng xem qua trong PR.
// Nền là bảng Tickets (vé Paid/Used của các chuyến khởi hành trong kỳ), trục cố định 24 giờ theo
// giờ Việt Nam UTC+7, mật độ = khách trung bình mỗi chuyến — giờ có mật độ ≥ trung bình cả kỳ là
// cao điểm. Đặt sau khối vé điện tử vì cùng họ "đọc tổng hợp từ bảng Tickets/Trips" — báo cáo
// không ghi dữ liệu, và KHÔNG chèn vào đúng khe sau khối voucher mà nhánh feature/19 (báo cáo
// doanh thu, cũng của Hiếu) đang thêm dòng.
builder.Services.AddScoped<IPeakHoursReportService, PeakHoursReportService>();

// Đề xuất tăng/giảm tần suất chạy xe theo dữ liệu: GET /api/reports/frequency-proposals (US 20
// "Tỷ lệ lấp đầy", Sprint 4). Task "API đề xuất tăng/giảm tần suất chạy xe theo dữ liệu" — Phùng
// Duy Hoàng. File này của Hoàng nên nhờ Hoàng xem qua trong PR.
// Service KHÔNG tự truy vấn Tickets/Trips: nó gọi IPeakHoursReportService (đăng ký ngay trên) rồi
// quy đổi mật độ từng giờ thành số chuyến nên chạy. Nhờ vậy mọi quy tắc nền của báo cáo gốc (vé
// Paid/Used, giờ Việt Nam, mặc định 30 ngày, trần 366 ngày, kiểm tra tuyến) chỉ nằm một chỗ. Đặt
// liền sau dòng đăng ký báo cáo gốc để thứ tự phụ thuộc đọc được ngay tại chỗ; không ghi dữ liệu,
// không migration.
builder.Services.AddScoped<IFrequencyProposalService, FrequencyProposalService>();

// (Đăng ký xuất báo cáo tỷ lệ lấp đầy ra file đã nằm cạnh khối báo cáo doanh thu ở trên — hai
// endpoint của cùng task dòng 78, đọc cùng một mạch.)

// Đồng bộ trạng thái ghế realtime giữa các phiên (US 2): hub SignalR /hubs/seat-map + cổng phát
// sự kiện cho ba chỗ làm đổi trạng thái ghế (giữ ghế, nhả ghế, quét hết hạn).
// Task "Đồng bộ trạng thái ghế realtime giữa các phiên (SignalR)" (dòng 11 Sprint 3) — Hoàng Văn Thịnh.
// File này của Hoàng (Phùng Duy Hoàng) nên nhờ Hoàng xem qua trong PR.
// Hợp đồng đầy đủ (URL, method JoinTrip/LeaveTrip, sự kiện SeatMapChanged) ở mục "Đồng bộ ghế
// realtime — /hubs/seat-map" của docs/api-contract.md.
// Không sửa ba service giữ/nhả/quét hết hạn (file của Kiên và của Hoàng — quy ước E1): chúng được
// BỌC bằng ba lớp Notifying* dưới đây, cùng lối CachedTripSearchService bọc TripSearchService ở
// trên. Ba service thật không phải biết gì về SignalR; lớp bọc chỉ thêm đúng một việc — thao tác
// THÀNH CÔNG thì phát "sơ đồ ghế chuyến này vừa đổi" cho các phiên đang xem.
// DI lấy bản đăng ký SAU CÙNG nên ba dòng factory phải nằm SAU ba dòng đăng ký interface ở trên
// (ISeatHoldExpiryService, ISeatHoldCreateService, ISeatHoldReleaseService) và phải tự đăng ký lớp
// THẬT để factory có cái mà bọc; ai đăng ký thêm các interface này ở dưới nữa thì lớp bọc bị thay
// thế im lặng (SeatMapRealtimeWiringTests ghim đúng chỗ nối này).
// AddSignalR để trần: giao thức JSON mặc định, hub ẩn danh (cùng mức GET /trips/{id}/seats), CHƯA
// có backplane — sự kiện chỉ chạm client nối vào chính máy chủ này, giới hạn đã biết ghi ở hợp đồng.
// Đặt ở CUỐI danh sách đăng ký (sau khối email vé, trước phần hạ tầng JWT/RBAC/CORS) để không chèn
// vào đúng khe mà các nhánh Sprint 3 khác đang thêm dòng.
builder.Services.AddSignalR();
builder.Services.AddScoped<ISeatMapNotifier, SeatMapNotifier>();

// Thông báo kết quả xử lý yêu cầu huỷ/đổi cho hành khách (US 5, dòng 7 Sprint 4 — Vàng Thị Dăm):
// ghi một dòng Notifications rồi đẩy NotificationReceived qua /hubs/notifications (khối MapHub ở cuối
// file). Dùng CHUNG lượt AddSignalR ở trên, không gọi thêm. Hai chốt hợp đồng — không bao giờ ném lỗi,
// và không nhận CancellationToken — ghi ở Services/ITicketRequestResultNotifier.cs; lý do từng dòng ở
// docs/33-thong-bao-ket-qua-huy-doi-ve.md §3.1. TicketRequestResultWiringTests ghim đúng chỗ nối này.
builder.Services.AddScoped<ITicketRequestResultNotifier, TicketRequestResultNotifier>();

// Duyệt / từ chối yêu cầu huỷ và đổi vé (US 5, dòng 5 Sprint 4 — Phùng Duy Hoàng): PATCH
// /api/cancellation-requests/{id} và PATCH /api/ticket-changes/{id}. Service tiêm THẲNG
// ITicketRequestResultNotifier ở trên rồi gọi nó SAU khi lưu — không bọc decorator như ba khối
// SeatHold* bên dưới: decorator ở đó tồn tại để người thêm thông báo không phải sửa file của người
// khác (luật E1), chứ bản thân lớp bọc không có ích gì. Interface này không có factory nào bọc.
builder.Services.AddScoped<ITicketRequestReviewService, TicketRequestReviewService>();

// Gửi yêu cầu đổi chuyến — POST /api/ticket-changes (US 5, dòng 4 Sprint 4 — Nguyễn Duy Kiên):
// kiểm vé thuộc người gọi + còn Paid, kiểm không có yêu cầu chờ trên cùng vé (ràng buộc liên bảng
// với CancellationRequests), kiểm chuyến/ghế mới, chụp NewPrice từ Fares rồi ghi một dòng Pending.
// KHÔNG đụng dòng vé và KHÔNG giữ ghế mới — hai việc đó thuộc nửa duyệt ở trên. Chênh lệch giá là
// trường suy ra trong response, không ghi cột nào (docs/32 §6). Không có factory/decorator nào bọc.
// ⚠️ File này của Hoàng — nhờ Hoàng xem qua dòng đăng ký này trong PR.
builder.Services.AddScoped<ITicketChangeSubmissionService, TicketChangeSubmissionService>();

// Tính phí huỷ vé theo quy định 24h / 12h / 2h (US 5, dòng 6 Sprint 4 — Nguyễn Duy Kiên): hàm thuần,
// không đọc CSDL lẫn đồng hồ; người gọi truyền Ticket.Price, CancellationRequest.DepartureAt và now.
// Hiếu (dòng 3) tiêm interface này ở POST /api/cancellation-requests để lấy CẢ điều kiện thời gian
// lẫn con số ghi vào CancellationFee — đừng chép lại ngưỡng ở endpoint. Quy định: docs/32 §2.1.
// ⚠️ File này của Hoàng — nhờ Hoàng xem qua dòng đăng ký này trong PR.
builder.Services.AddScoped<ICancellationFeeService, CancellationFeeService>();

// Ghi nhật ký soát vé + chống trùng khi hàng đợi ngoại tuyến đồng bộ lại (US 15, dòng 18 Sprint 4 —
// Phùng Duy Hoàng). Endpoint soát vé POST /tickets/validate (dòng 15, Trần Trung Hiếu) tiêm interface
// này rồi gọi SAU khi đã xác thực chữ ký và tính xong kết quả — cả lượt online lẫn lượt đồng bộ đều đi
// qua đúng một cửa. Hợp đồng idempotent (gửi lại cùng ClientScanId = thành công, đọc dòng cũ; khoảng
// hợp lý của ScannedAt) ghi ở Services/ITicketValidationRecorder.cs; đặc tả phía máy quét ở
// docs/39-hang-doi-ngoai-tuyen-soat-ve.md. Không có factory/decorator nào bọc interface này.
builder.Services.AddScoped<ITicketValidationRecorder, TicketValidationRecorder>();

// Soát vé — POST /api/tickets/validate (US 15, dòng 15 + dòng 19 Sprint 4 — Trần Trung Hiếu):
// xác thực chữ ký QR (ITicketQrService, đã đăng ký ở trên) → tra vé → tính kết quả → ghi nhật ký
// qua ITicketValidationRecorder (khối ngay trên). Hợp đồng ở mục "Soát vé QR" của
// docs/api-contract.md; luồng ở docs/41-api-soat-ve-qr.md.
// ⚠️ File này của Hoàng — nhờ Hoàng xem qua dòng đăng ký này trong PR.
builder.Services.AddScoped<ITicketValidationService, TicketValidationService>();

// Quyết định trạm khách THỰC SỰ lên xe cho nhật ký soát vé (US 15, dòng 17 Sprint 4 — Nguyễn Duy
// Kiên): chỉ lượt Valid mới ghi trạm, trạm phải thuộc tuyến của chuyến TRÊN VÉ (khác
// Tickets.BoardingStopId — trạm khách chọn lúc mua, docs/34 §2.1). TicketValidationService ở khối
// ngay trên tiêm interface này để chuyển giá trị sang ITicketValidationRecorder. Hợp đồng endpoint ở
// mục "Soát vé QR" của docs/api-contract.md.
// ⚠️ File này của Hoàng — nhờ Hoàng xem qua dòng đăng ký này trong PR.
builder.Services.AddScoped<ITicketBoardingStopService, TicketBoardingStopService>();

builder.Services.AddScoped<SeatHoldCreateService>();
builder.Services.AddScoped<ISeatHoldCreateService>(provider => new NotifyingSeatHoldCreateService(
    provider.GetRequiredService<SeatHoldCreateService>(),
    provider.GetRequiredService<ISeatMapNotifier>()));

builder.Services.AddScoped<SeatHoldReleaseService>();
builder.Services.AddScoped<ISeatHoldReleaseService>(provider => new NotifyingSeatHoldReleaseService(
    provider.GetRequiredService<SeatHoldReleaseService>(),
    provider.GetRequiredService<ISeatMapNotifier>()));

builder.Services.AddScoped<SeatHoldExpiryService>();
builder.Services.AddScoped<ISeatHoldExpiryService>(provider => new NotifyingSeatHoldExpiryService(
    provider.GetRequiredService<SeatHoldExpiryService>(),
    provider.GetRequiredService<AppDbContext>(),
    provider.GetRequiredService<ISeatMapNotifier>()));

// Hoàn tiền (US 8, Sprint 4): MỘT lượt gọi cổng hoàn tiền cho một khoản — kiểm điều kiện theo
// trigger, GIÀNH DÒNG bằng xmin TRƯỚC khi gọi cổng (docs/40 §4.2), ghi kết quả + đúng một dòng
// RefundLog. Task "Xử lý hoàn tiền thất bại + retry theo lịch" — Phùng Duy Hoàng (file này cũng
// của Hoàng).
// Ba người gọi phân biệt bằng trigger (chi tiết ở IRefundAttemptService): service hoàn tiền tự
// động khi duyệt huỷ vé (dòng 28 — Kiên, chưa dựng) → Initial; nút "Thử lại" của màn Admin
// (dòng 35 — Băng, chưa nối) → ManualRetry; job theo lịch ngay dưới → ScheduledRetry.
// Cổng hoàn tiền THẬT là task dòng 29 (Trần Trung Hiếu) — đã dựng: RefundGatewayCaller tra client
// theo MethodCode của giao dịch gốc (docs/40 §2.5) và bọc lỗi vận chuyển thành Unresolved, TUYỆT ĐỐI
// không đoán. Thay chỗ đăng ký Placeholder trước đây (class Placeholder vẫn nằm ở Services/ — file
// của Hoàng, nhờ Hoàng xem qua PR này).
// Ba client đăng ký qua AddHttpClient (transient) và chỉ được dựng khi có lời gọi — máy cấu hình
// thiếu cổng nào thì cổng đó không kéo sập dây chuyền (cùng lối PaymentGatewayResolver).
// Đặt ở CUỐI danh sách đăng ký (sau khối SignalR, trước phần hạ tầng JWT/RBAC/CORS) để không chèn
// vào đúng khe mà các nhánh Sprint 4 khác đang thêm dòng.
builder.Services.Configure<VnPayRefundOptions>(builder.Configuration.GetSection(VnPayRefundOptions.SectionName));
builder.Services.AddScoped<IRefundGatewayCaller, RefundGatewayCaller>();
builder.Services.AddHttpClient<MoMoRefundClient>();
builder.Services.AddHttpClient<VnPayRefundClient>();
builder.Services.AddHttpClient<ZaloPayRefundClient>();
// Thông báo kết quả hoàn tiền cho khách (US 8, dòng 34 Sprint 4 — Phùng Duy Hoàng): từ đây
// IRefundAttemptService resolve ra LỚP BỌC NotifyingRefundAttemptService thay vì lớp trần, nên CẢ BA
// đường gọi cổng ở trên đi qua chỗ báo tin mà không đường nào phải tự nhớ — kể cả hai đường chưa
// dựng (dòng 35, dòng 28). Cùng lối đăng ký SeatHoldCreateService ở trên: lớp trần trước, lớp bọc
// sau. Notifier tự quyết định "có gì để báo" bằng dòng Refunds đã lưu (chỉ kết quả CUỐI — thành
// công, hoặc thất bại hết lịch thử lại); hợp đồng không-bao-giờ-ném-lỗi + lý do bọc decorator thay vì
// sửa thẳng service ghi ở Services/IRefundResultNotifier.cs và NotifyingRefundAttemptService.cs.
// RefundRetryWiringTests ghim đúng chỗ nối này.
builder.Services.AddScoped<RefundAttemptService>();
builder.Services.AddScoped<IRefundResultNotifier, RefundResultNotifier>();
builder.Services.AddScoped<IRefundAttemptService>(provider => new NotifyingRefundAttemptService(
    provider.GetRequiredService<RefundAttemptService>(),
    provider.GetRequiredService<IRefundResultNotifier>()));

// Job thử lại hoàn tiền theo lịch (US 8, Sprint 4): mỗi phút quét khoản Failed tới mốc NextRetryAt
// rồi gọi lại cổng theo nấc thang RefundRetryPolicy (5' → 30' → 2h → 12h → 24h; hết thang thì dừng,
// khoản chờ quản trị bấm thử tay). Task "Xử lý hoàn tiền thất bại + retry theo lịch" — Phùng Duy
// Hoàng (file này cũng của Hoàng).
// AddHostedService (không phải AddScoped): job phải sống suốt vòng đời app. Ruột job là
// IRefundRetryService đăng ký ngay trên — hosted service là singleton còn AppDbContext là scoped
// nên nó mở scope riêng cho mỗi lượt quét, và RefundRetryService mở thêm scope riêng cho TỪNG
// khoản; giải thích đầy đủ ở đầu RefundRetryBackgroundService.
// Đặt ở CUỐI danh sách đăng ký (sau khối SignalR, trước phần hạ tầng JWT/RBAC/CORS) để không chèn
// vào đúng khe mà các nhánh Sprint 4 khác đang thêm dòng.
builder.Services.AddScoped<IRefundRetryService, RefundRetryService>();
builder.Services.AddHostedService<RefundRetryBackgroundService>();

// Đối soát hoàn tiền hằng ngày + cảnh báo khi lệch số liệu (US 8, Sprint 4):
// job chạy 00:05 UTC (07:05 giờ Việt Nam), kiểm bất biến sổ nội bộ (bằng chứng cổng báo thành công
// trên khoản chưa Succeeded, khoản Pending quá 24 giờ, mã giao dịch lệch, hoàn quá số tiền giao dịch
// gốc, một giao dịch có nhiều khoản hoàn cùng sống…) rồi đối chiếu sao kê cổng KHI có nguồn sao kê.
// Task "Đối soát hoàn tiền hằng ngày + cảnh báo khi lệch số liệu" (dòng 32 Sprint 4) — Nguyễn Duy
// Kiên. File này của Hoàng nên nhờ Hoàng xem qua trong PR.
// Cảnh báo ghi vào bảng Notifications + đẩy SignalR cho mọi Admin/Manager đang hoạt động, dùng loại
// NotificationType.RefundReconciliationAlert (loại thứ chín — là thay đổi hợp đồng frontend, đã ghi ở
// docs/api-contract.md và nhờ Băng cập nhật notificationApi.ts/NotificationBell).
// 🔴 IRefundStatementSource mặc định là UnavailableRefundStatementSource: bản chạy hiện tại CHƯA có
// cổng nào cung cấp sao kê hoàn tiền (MoMo/ZaloPay chỉ có API tra giao dịch THU tiền). Nó trả null
// ("không biết"), không trả sao kê rỗng — đổi sang nguồn thật chỉ cần đổi đúng dòng đăng ký này.
// Đăng ký AddScoped thay vì Singleton để nguồn thật sau này được dùng AppDbContext/HttpClient scoped.
// AddHostedService (không phải AddScoped): job phải sống suốt vòng đời app; ruột job là service scoped
// nên vòng lặp mở scope riêng cho mỗi lượt chạy.
// Đặt ở CUỐI danh sách đăng ký (sau khối SignalR, trước phần hạ tầng JWT/RBAC/CORS) để không chèn
// vào đúng khe mà các nhánh Sprint 4 khác đang thêm dòng.
builder.Services.AddScoped<IRefundStatementSource, UnavailableRefundStatementSource>();
builder.Services.AddScoped<IRefundReconciliationService, RefundReconciliationService>();
builder.Services.AddScoped<IRefundReconciliationNotifier, RefundReconciliationNotifier>();
builder.Services.AddHostedService<RefundReconciliationBackgroundService>();

// Hoàn tiền (US 8, Sprint 4), phần đọc của hành khách: GET /api/refunds và
// GET /api/refunds/{refundCode}.
// Task "API tra cứu trạng thái hoàn tiền của giao dịch" (dòng 42 Sprint 4) — Vàng Thị Dăm.
// File này của Hoàng nên nhờ Hoàng xem qua trong PR.
// Yêu cầu đăng nhập nhưng KHÔNG gắn policy vai trò — hành khách tự xem khoản hoàn của mình, quyền sở
// hữu kiểm ngay trong truy vấn theo userId (khoản của người khác trả 404 chứ không 403). Lọc theo
// paymentCode chính là câu "trạng thái hoàn tiền CỦA MỘT GIAO DỊCH" của task; lọc theo status trả
// mảng rỗng khi mã lạ chứ không 400 (mục D3).
// Hai bảng Refunds/RefundLogs đứng NGOÀI danh sách A9 nên service đọc thẳng DbSet, không đi qua
// interface trung gian nào; migration đã có ở nhánh feature/8-migrate-refunds-refundlogs.
// Các phần CÒN LẠI của bề mặt hoàn tiền chưa dựng và không thuộc task này: tạo khoản hoàn (Kiên,
// dòng 39), gọi cổng (Hiếu, dòng 40), job thử lại theo lịch (Hoàng, dòng 41), đối soát (Kiên,
// dòng 43) và ba endpoint quản trị GET /admin/refunds, POST /admin/refunds/{id}/retry,
// GET /admin/refunds/export (dòng 46–47) — chưa có dòng đăng ký nào ở đây vì chưa có service.
// Đặt ở CUỐI danh sách đăng ký (sau khối SignalR ghế, trước phần hạ tầng JWT/RBAC/CORS) để không
// chèn vào đúng khe mà các nhánh Sprint 4 khác đang thêm dòng.
builder.Services.AddScoped<IRefundLookupService, RefundLookupService>();

// Đăng ký / huỷ theo dõi thông báo theo tuyến và trạm: GET/POST/DELETE /api/subscriptions.
// Task "API đăng ký / huỷ theo dõi thông báo theo tuyến và trạm" (US 10, Sprint 5 dòng 17) —
// Trần Trung Hiếu. File này của Hoàng nên nhờ Hoàng xem qua trong PR.
// Bảng Subscriptions đứng NGOÀI danh sách A9 nên service đọc thẳng DbSet (docs/37); hai service
// của Kiên (bắn tin khi xe cách trạm <500m, đẩy sự cố theo tuyến) và component công tắc của Thịnh
// sẽ đọc cùng bảng này ở Sprint 5.
// Đặt ở CUỐI danh sách đăng ký (sau khối SignalR ghế, trước phần hạ tầng JWT/RBAC/CORS) để không
// chèn vào đúng khe mà các nhánh Sprint 4 khác đang thêm dòng.
builder.Services.AddScoped<ISubscriptionService, SubscriptionService>();

// Thông báo trong ứng dụng (US 10, Sprint 5), phần đọc của hành khách:
// GET /api/notifications/me.
// Task "API lịch sử thông báo đã nhận của hành khách" (dòng 19 Sprint 5) — Vàng Thị Dăm.
// File này của Hoàng nên nhờ Hoàng xem qua trong PR (cùng lối đã làm ở khối IRefundLookupService
// ngay trên, commit d5bd049).
// Yêu cầu đăng nhập nhưng KHÔNG gắn policy vai trò — hành khách tự xem thông báo của mình, quyền sở
// hữu kiểm ngay trong truy vấn theo userId. Đây là bề mặt REST của thông báo; kênh ĐẨY vẫn là
// NotificationHub ở /hubs/notifications (đăng ký phía trên, trong khối SignalR).
// Chỉ đọc nên không có migration kèm theo: bảng Notifications và chỉ mục (UserId, CreatedAt) đã dựng
// từ 20261013143603_Sprint4_Notifications.
// Các phần CÒN LẠI của bề mặt thông báo chưa dựng và không thuộc task này: service bắn thông báo khi
// xe cách trạm dưới 500m (Kiên, dòng 15), API đăng ký/huỷ theo dõi trạm (Hiếu, dòng 17), màn quản lý
// mẫu thông báo (dòng 24) và test luồng thông báo (Giàng A Vàng, dòng 30) — chưa có dòng đăng ký nào
// ở đây vì chưa có service.
// Đặt ở CUỐI danh sách đăng ký (sau khối SignalR, trước phần hạ tầng JWT/RBAC/CORS) để không chèn
// vào đúng khe mà các nhánh Sprint 5 khác đang thêm dòng.
builder.Services.AddScoped<IMyNotificationsService, MyNotificationsService>();

// Màn quản lý mẫu thông báo (US 10, Sprint 5 dòng 24) — GET/PUT /api/notification-templates.
// Task "Màn hình Admin quản lý mẫu thông báo" — Dương Thị Hạnh.
// File này của Hoàng nên nhờ Hoàng xem qua trong PR (cùng lối các khối Sprint 5 ngay trên).
// Mẫu là phần GHI ĐÈ của câu mặc định (docs/37 §2.1): bảng NotificationTemplates đã dựng từ
// 20261014164805_Sprint4_NotificationTemplates_Subscriptions, không cần migration mới.
// Đặt ở CUỐI danh sách đăng ký (sau khối SignalR, trước phần hạ tầng JWT/RBAC/CORS) để không
// chèn vào đúng khe mà các nhánh Sprint 5 khác đang thêm dòng.
builder.Services.AddScoped<INotificationTemplateService, NotificationTemplateService>();

// Xuất file lớn bất đồng bộ (US 24 "Xuất dữ liệu", Sprint 5 dòng 64):
// POST /api/exports, GET /api/exports/{id}, GET /api/exports/{id}/download.
// Task "Xử lý xuất file lớn bất đồng bộ + thông báo khi xong" — Vàng Thị Dăm.
// File này của Hoàng nên nhờ Hoàng xem qua trong PR.
// Bảng ExportJobs là bảng MỚI (migration Sprint5_AsyncExportJobs — xem docs/52), khác ba bản xuất
// đồng bộ ở khối IRevenueExportService/IFillRateExportService phía trên: chúng trả file ngay trong
// request, còn bốn dòng dưới đây nhận yêu cầu rồi để job nền dựng file và báo qua Notifications.
// IExportJobProcessor dùng lại IRevenueExportService + IFillRateReportService đã đăng ký ở trên,
// nên nó KHÔNG có định nghĩa doanh thu/tỷ lệ lấp đầy thứ hai.
// AddHostedService (không phải AddScoped): job phải sống suốt vòng đời app. Ruột job là
// IExportJobProcessor/IExportJobService scoped nên vòng lặp mở scope riêng cho mỗi job — giải thích
// đầy đủ ở đầu ExportJobBackgroundService.
// Đặt ở CUỐI danh sách đăng ký (sau khối SignalR, trước phần hạ tầng JWT/RBAC/CORS) để không chèn
// vào đúng khe mà các nhánh Sprint 5 khác đang thêm dòng.
builder.Services.AddScoped<IExportJobService, ExportJobService>();
builder.Services.AddScoped<IExportJobNotifier, ExportJobNotifier>();
builder.Services.AddScoped<IExportJobProcessor, ExportJobProcessor>();
builder.Services.AddHostedService<ExportJobBackgroundService>();

// Thống kê số lượng hồ sơ ưu đãi theo trạng thái (US 17, Sprint 4 dòng 55):
// GET /api/discount-requests/statistics.
// Task "API thống kê số lượng hồ sơ ưu đãi theo trạng thái" — Vàng Thị Dăm.
// File này của Hoàng nên nhờ Hoàng xem qua trong PR (Hoàng đã đồng ý cho sửa trong phiên này).
// Bảng DiscountRequests đã dựng từ Sprint 2 dưới tên gộp theo A9 #13 (xem
// AppDbContext.DiscountRequest.cs); service CHỈ ĐỌC nên không có migration kèm theo.
// Hai endpoint còn lại của bề mặt api/discount-requests chưa dựng và không thuộc task này: gửi hồ sơ
// + tải ảnh minh chứng (Hiếu, dòng 51), duyệt/từ chối hồ sơ (Kiên, dòng 52) — chưa có dòng đăng ký
// nào ở đây vì chưa có service.
// Đặt ở CUỐI danh sách đăng ký (sau khối SignalR, trước phần hạ tầng JWT/RBAC/CORS) để không chèn
// vào đúng khe mà các nhánh khác đang thêm dòng.
builder.Services.AddScoped<IDiscountRequestStatisticsService, DiscountRequestStatisticsService>();

// Web Push (PWA) — thông báo trình duyệt khi tab đã đóng: kho thiết bị + client gửi tin + service
// đăng ký thiết bị. Task "Tích hợp Web Push (PWA) để nhận thông báo trên trình duyệt" (US 10
// "Thông báo trạm", Sprint 4 dòng 42) — Trần Trung Hiếu. File này của Hoàng nên nhờ Hoàng xem qua
// trong PR.
// Khoá VAPID KHÔNG có giá trị mặc định trong code (luật 2 — repo PUBLIC): đặt qua dotnet
// user-secrets hoặc biến môi trường WebPush__PublicKey/WebPush__PrivateKey; mục "WebPush" trong
// appsettings.Development.json.example chỉ là khuôn để điền, cách sinh khoá ở docs/54 §2.
// Store thiết bị đăng ký là bản TRONG BỘ NHỚ vì chưa có bảng thiết bị push (migration của Dăm) —
// khi có bảng, đổi đúng dòng AddSingleton<IWebPushSubscriptionStore> này sang bản EF, cùng lối
// IRefundStatementSource đổi nguồn thật (docs/54 §6).
// WebPushClient đăng ký qua AddHttpClient (transient) như các client cổng thanh toán; AddScoped
// cho interface để controller/service nghiệp vụ chỉ cần biết IWebPushDeviceService/IWebPushSender.
// AddSingleton cho store vì nó là chỗ dùng chung duy nhất giữa mọi request gửi tin.
// Đặt ở CUỐI danh sách đăng ký (trước phần hạ tầng JWT/RBAC/CORS) để không chèn vào đúng khe mà
// các nhánh khác đang thêm dòng.
builder.Services.Configure<WebPushOptions>(builder.Configuration.GetSection(WebPushOptions.SectionName));
builder.Services.AddSingleton<IWebPushSubscriptionStore, InMemoryWebPushSubscriptionStore>();
builder.Services.AddHttpClient<WebPushClient>();
builder.Services.AddScoped<IWebPushSender, WebPushSender>();
builder.Services.AddScoped<IWebPushDeviceService, WebPushDeviceService>();

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

// Hub SignalR đồng bộ sơ đồ ghế realtime (US 2) — cùng khối đăng ký ở phần services.
// Đặt sau MapControllers cho dễ đọc, nhưng điểm BẮT BUỘC là phải nằm SAU app.UseCors(AppCors) ở
// trên: negotiate và WebSocket handshake đều là request đi qua CORS, thiếu UseCors là trình duyệt
// chặn ngay từ bước negotiate chứ không tới được hub.
app.MapHub<SeatMapHub>("/hubs/seat-map");

// Hub thông báo kết quả xử lý yêu cầu huỷ/đổi (US 5) — cùng khối đăng ký ở phần services.
// KHÁC hub ghế: NotificationHub mang [Authorize] vì thông báo là dữ liệu riêng của một người, và
// token đến từ query string "?access_token=…" (trình duyệt không đặt được header cho WebSocket) —
// JwtMiddleware.ReadAccessTokenFromHubQueryAsync đọc token cho ĐÚNG tiền tố /hubs. Client không có
// hàm nào gọi được lên hub: nhóm nhận suy từ ID người dùng trong token, không từ tham số client
// (Hubs/NotificationHub.cs). Cùng yêu cầu SAU app.UseCors(AppCors) như hub ghế ở trên.
app.MapHub<NotificationHub>("/hubs/notifications");

app.Run();

/// <summary>
/// Khai báo tường minh để project test dùng được <c>WebApplicationFactory&lt;Program&gt;</c> —
/// top-level statements mặc định sinh ra class Program ở mức internal.
/// </summary>
public partial class Program;
