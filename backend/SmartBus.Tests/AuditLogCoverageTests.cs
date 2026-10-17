using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartBus.Api.Data;
using SmartBus.Api.Entities;
using SmartBus.Api.Services;
using RouteEntity = SmartBus.Api.Entities.Route;
using UserEntity = SmartBus.Api.Entities.User;

namespace SmartBus.Tests;

/// <summary>
/// Kiểm thử chéo: xác nhận MỌI thao tác thay đổi dữ liệu đều được ghi nhật ký
/// (US 23, task 37 — Giàng A Vàng).
///
/// Khác <c>AuditLogWriteApiTests</c> (task 36) ở chỗ nào: task 36 kiểm vài endpoint tiêu biểu
/// để chứng minh đường ghi chạy đúng. Task này kiểm VÉT CẠN — không tin vào một danh sách
/// endpoint tự chép tay, mà hỏi thẳng routing của app xem nó đang phục vụ những endpoint nào,
/// rồi bắt bảng phủ sóng ở dưới phải khớp. Thêm một <c>[HttpPost]</c> ở Sprint sau mà quên
/// khai vào bảng là test đỏ ngay, thay vì lặng lẽ thiếu một dòng nhật ký.
///
/// Ba nhóm endpoint KHÔNG đi qua middleware, và đây là chỗ dễ hiểu nhầm nhất:
///   • <c>/api/auth/login</c>, <c>/api/auth/logout</c> — <c>AuthController</c> tự gọi
///     <see cref="IAuditLogService"/> để ghi Login/LoginFailed/Logout, vì ba hành động này
///     không suy ra được từ HTTP verb.
///   • <c>/api/auth/refresh-token</c>, <c>/api/auth/register</c> — KHÔNG ghi. Đánh đổi đã biết,
///     ghi ở doc-comment <c>AuditLogMiddleware.IgnoredPaths</c>.
///   • <c>/api/vouchers/validate</c> — KHÔNG ghi, nhưng vì lý do KHÁC HẲN: nó không đổi dữ liệu
///     (phép đọc thuần dùng POST chỉ vì có body). Xem dòng cuối bảng phủ sóng.
///
/// ⚠️ Provider InMemory KHÔNG dựng unique index, khoá ngoại hay HasData — không seed 4 vai trò
/// thì mọi request đều 403 dù token hợp lệ.
/// </summary>
public class AuditLogCoverageTests
{
    // =======================================================================================
    // PHẦN 1 — Bảng phủ sóng: mọi endpoint thay đổi dữ liệu, và cách nó được ghi
    // =======================================================================================

    /// <summary>
    /// Toàn bộ endpoint thay đổi dữ liệu của app tính đến hết Sprint 1. Test
    /// <see cref="Bang_phu_song_khop_voi_routing_that_cua_app"/> đối chiếu danh sách này với
    /// routing thật, nên nó không thể lặng lẽ lạc hậu.
    /// </summary>
    private static readonly (string Verb, string Pattern, CachGhi Cach)[] BangPhuSong =
    [
        ("POST",   "/api/admin/users",                           CachGhi.Middleware),
        ("PUT",    "/api/admin/users/{id:guid}",                 CachGhi.Middleware),
        ("DELETE", "/api/admin/users/{id:guid}",                 CachGhi.Middleware),
        ("PATCH",  "/api/admin/users/{id:guid}/status",          CachGhi.Middleware),
        ("PUT",    "/api/admin/users/{id:guid}/roles",           CachGhi.Middleware),

        // Đặt lại mật khẩu tài khoản — Admin. Middleware tự ghi (PUT → Update); tên bảng suy từ
        // đoạn tĩnh ngay trước {id} là "Users", Target "Users:{id}". Service băm BCrypt + thu hồi
        // toàn bộ refresh token đang hoạt động, nhưng KHÔNG tự gọi IAuditLogService — verb đã nói
        // đủ hành động, và mật khẩu/bản băm không bao giờ được ghi vào nhật ký.
        ("PUT",    "/api/admin/users/{id:guid}/password",        CachGhi.Middleware),

        ("POST",   "/api/auth/login",                            CachGhi.TuGhi),
        ("POST",   "/api/auth/logout",                           CachGhi.TuGhi),
        ("POST",   "/api/auth/refresh-token",                    CachGhi.KhongGhi),
        ("POST",   "/api/auth/register",                         CachGhi.KhongGhi),

        ("POST",   "/api/routes",                                CachGhi.Middleware),
        ("PUT",    "/api/routes/{id:guid}",                      CachGhi.Middleware),
        ("DELETE", "/api/routes/{id:guid}",                      CachGhi.Middleware),

        ("POST",   "/api/routes/{routeId:guid}/fares",           CachGhi.Middleware),
        ("PUT",    "/api/routes/{routeId:guid}/fares/{id:guid}", CachGhi.Middleware),
        ("DELETE", "/api/routes/{routeId:guid}/fares/{id:guid}", CachGhi.Middleware),

        ("POST",   "/api/routes/{routeId:guid}/stops",           CachGhi.Middleware),
        ("PUT",    "/api/routes/{routeId:guid}/stops/order",     CachGhi.Middleware),
        ("DELETE", "/api/routes/{routeId:guid}/stops/{id:guid}", CachGhi.Middleware),

        // Lịch trình chạy xe — story 13, Trần Trung Hiếu. Cả bốn endpoint đều do
        // AuditLogMiddleware tự ghi (verb → hành động, tên bảng suy ra là "Trips").
        ("POST",   "/api/routes/{routeId:guid}/trips",           CachGhi.Middleware),
        ("PUT",    "/api/routes/{routeId:guid}/trips/{id:guid}", CachGhi.Middleware),
        ("DELETE", "/api/routes/{routeId:guid}/trips/{id:guid}", CachGhi.Middleware),
        ("POST",   "/api/routes/{routeId:guid}/trips/generate",  CachGhi.Middleware),

        // Sinh chuyến cho CẢ ĐỘI XE — nợ của đợt mở rộng đội xe 17/10/2026, Phùng Duy Hoàng. Cũng
        // do AuditLogMiddleware tự ghi (POST → Create). Route có tham số {routeId} nhưng không có
        // {id}, nên tên bảng suy từ đoạn tĩnh đầu tiên sau tham số cuối là "Trips". Response
        // (GenerateFleetTripsResponse) không có trường "id" ở nóc — id từng chuyến nằm trong
        // "items" — nên Target chỉ còn tên bảng, cùng giới hạn đã ghi ở
        // PATCH .../trips/driver-assignment; test Phần 2 ghim sự thật đó.
        ("POST",   "/api/routes/{routeId:guid}/trips/generate-fleet", CachGhi.Middleware),

        // Giờ đến từng trạm — story 13, Phùng Duy Hoàng. Middleware tự ghi (PUT → Update); tên
        // bảng suy từ đoạn ngay trước {id} là "Trips", Target "Trips:{id}". Đánh đổi đã biết:
        // dòng bị ghi thật là TripStopTimeOverrides, nhưng id chuyến trong Target tra ngược được
        // cả cụm giờ trạm của chuyến — endpoint là thao tác con của chuyến, cùng lối
        // PATCH /api/admin/feedbacks/{id}/replies ghi về "Feedbacks".
        ("PUT",    "/api/routes/{routeId:guid}/trips/{id:guid}/stop-times", CachGhi.Middleware),

        // Đổi xe/đổi tài xế khi có sự cố — story 14, Phùng Duy Hoàng. Middleware tự ghi
        // (PATCH → Update); {id} đặt tên đúng để tên bảng suy ra là "Trips", Target "Trips:{id}".
        ("PATCH",  "/api/trips/{id:guid}/assignment",            CachGhi.Middleware),

        // Cập nhật vị trí chuyến — story 10, Trần Trung Hiếu. Middleware tự ghi
        // (PUT → Update); {id} đặt tên đúng để tên bảng suy ra là "Trips", Target "Trips:{id}".
        ("PUT",    "/api/trips/{id:guid}/location",              CachGhi.Middleware),

        // Giờ đến từng trạm — story 13. Middleware tự ghi (PUT → Update); {id} đặt tên đúng để
        // tên bảng suy ra là "Trips", Target "Trips:{id}".
        ("PUT",    "/api/routes/{routeId:guid}/trips/{id:guid}/stop-times", CachGhi.Middleware),

        // Gán tài xế vào chuyến theo lô — story 14, Nguyễn Duy Kiên. Cũng do AuditLogMiddleware tự
        // ghi (PATCH → Update). Tên bảng suy ra là "Trips": route có tham số {routeId} nên tài
        // nguyên là đoạn tĩnh đầu tiên sau tham số cuối, đúng luật AuditLogMiddleware.ResourceSegmentIndex
        // — đặt endpoint ở /trips/driver-assignment (không có tham số nào) thì Target đã là
        // "DriverAssignment", một bảng không tồn tại.
        ("PATCH",  "/api/routes/{routeId:guid}/trips/driver-assignment", CachGhi.Middleware),

        // Danh sách xe buýt — story 14, Trần Trung Hiếu. Ba endpoint đều do
        // AuditLogMiddleware tự ghi (verb → hành động, tên bảng suy ra là "Buses").
        ("POST",   "/api/buses",                                 CachGhi.Middleware),
        ("PUT",    "/api/buses/{id:guid}",                       CachGhi.Middleware),
        ("DELETE", "/api/buses/{id:guid}",                       CachGhi.Middleware),

        // Sơ đồ ghế — story 2, Phùng Duy Hoàng. Ba endpoint đều do AuditLogMiddleware tự ghi
        // (verb → hành động, tên bảng suy ra là "SeatLayouts"). POST trả body có id nên Target
        // là id sơ đồ vừa tạo; PUT/DELETE lấy id từ đường dẫn.
        ("POST",   "/api/seat-layouts",           CachGhi.Middleware),
        ("PUT",    "/api/seat-layouts/{id:guid}", CachGhi.Middleware),
        ("DELETE", "/api/seat-layouts/{id:guid}", CachGhi.Middleware),

        // Hồ sơ tài xế — story 14, Trần Trung Hiếu. Ba endpoint đều do AuditLogMiddleware
        // tự ghi (verb → hành động). Tên bảng suy ra là "Drivers" dù bản ghi nằm ở bảng
        // Users — đánh đổi đã biết, ghi chú ở docs/api-contract.md mục "Hồ sơ tài xế".
        ("POST",   "/api/drivers",                               CachGhi.Middleware),
        ("PUT",    "/api/drivers/{id:guid}",                     CachGhi.Middleware),
        ("DELETE", "/api/drivers/{id:guid}",                     CachGhi.Middleware),

        // Đăng ký vé tháng — story 16, Trần Trung Hiếu. Middleware tự ghi (POST → Create);
        // route không có tham số nào nên tên bảng suy từ đoạn tĩnh cuối là "MonthlyPasses",
        // Target là id vé mới đọc từ body trả về.
        ("POST",   "/api/monthly-passes",                         CachGhi.Middleware),

        // Gia hạn vé tháng — story 16, Phùng Duy Hoàng. Middleware tự ghi (POST → Create, tên
        // bảng suy từ đoạn trước {id} là "MonthlyPasses"). Target là id vé MỚI đọc từ body trả
        // về, không phải vé cũ trên đường dẫn — ca này cố ý để lộ điều đó ra test Phần 2.
        ("POST",   "/api/monthly-passes/{id:guid}/renew",        CachGhi.Middleware),

        // Quản trị vé tháng — bổ sung ngoài story 16, Nguyễn Duy Kiên (kiêm chủ API). Middleware
        // tự ghi (PATCH → Update); {id} đặt tên đúng để tên bảng suy từ đoạn trước nó là
        // "MonthlyPasses", Target "MonthlyPasses:{id}".
        ("PATCH",  "/api/monthly-passes/{id:guid}/status",        CachGhi.Middleware),

        // CRUD loại vé tháng — bổ sung ngoài story 16, Nguyễn Duy Kiên. Cả ba endpoint đều do
        // AuditLogMiddleware tự ghi (POST → Create, PUT → Update, DELETE → Delete); tên bảng suy
        // từ đoạn tĩnh "pass-types" thành "PassTypes", POST lấy đoạn cuối, PUT/DELETE lấy đoạn
        // trước {id}. Target của POST là id loại vé mới đọc từ body trả về.
        ("POST",   "/api/pass-types",                             CachGhi.Middleware),
        ("PUT",    "/api/pass-types/{id:guid}",                   CachGhi.Middleware),
        ("DELETE", "/api/pass-types/{id:guid}",                   CachGhi.Middleware),

        // Gia hạn giữ chỗ — story 3, Trần Trung Hiếu. Middleware tự ghi (POST → Create) nhưng
        // Target chỉ là "Extend" chứ KHÔNG phải "SeatHolds:<mã phiên>": route dùng tham số
        // {sessionCode} (khớp hợp đồng chung của bề mặt /seat-holds) nên middleware không tìm
        // thấy {id}, rơi về đoạn tĩnh đầu tiên sau tham số cuối — chính là tên hành động.
        // Giới hạn đã ghi nhận ở AuditLogMiddleware.ResourceSegmentIndex ("POST /api/tickets/
        // validate" ở Sprint 4 cũng sẽ dính). Ca này cố ý để lộ điều đó ra test Phần 2; task
        // release của Hoàng gặp đúng vấn đề này — nhóm cần chốt một cách xử lý chung cho các
        // endpoint hành động con của bề mặt /seat-holds.
        ("POST",   "/api/seat-holds/{sessionCode}/extend",       CachGhi.Middleware),

        // Nhả giữ chỗ — story 3, Phùng Duy Hoàng. Middleware tự ghi (POST → Create) và DÍNH CÙNG
        // giới hạn như ca extend ngay trên: route dùng {sessionCode} nên Target chỉ là "Release",
        // không phải "SeatHolds:<mã phiên>" (giới hạn suy tên bảng ở AuditLogMiddleware.
        // ResourceSegmentIndex — giải thích đầy đủ ở comment khối trên). Test Phần 2 ghim sự thật
        // này; khi nhóm chốt cách xử lý chung cho các endpoint hành động con của /seat-holds thì
        // đổi hai ca này và middleware cùng một lúc.
        ("POST",   "/api/seat-holds/{sessionCode}/release",      CachGhi.Middleware),

        // Giữ ghế tạm thời — story 3, Nguyễn Duy Kiên. Middleware tự ghi (POST → Create); route
        // không có tham số nào nên tên bảng suy từ đoạn tĩnh cuối là "SeatHolds".
        // Đánh đổi đã biết: một phiên giữ NHIỀU ghế sinh NHIỀU dòng SeatHolds, nhưng body trả về là
        // hình dạng PHIÊN (SeatHoldSessionResponse) — không có trường "id" nào để middleware đọc, mà
        // đường dẫn cũng không có {id}. Target vì vậy chỉ còn tên bảng "SeatHolds" (không kèm id
        // nào), đúng nhánh "endpoint không trả về id thì ghi lại tên bảng" của ResolveTarget. Muốn
        // tra vết tới từng dòng phải hỏi theo sessionCode — nhưng khoá phiên nằm trong body, không
        // nằm trên đường dẫn. Cùng nhóm giới hạn với hai dòng {sessionCode} ở trên.
        ("POST",   "/api/seat-holds",                            CachGhi.Middleware),

        // Xử lý phản ánh — story 24, Phùng Duy Hoàng. Middleware tự ghi (PATCH → Update,
        // POST → Create); tên bảng suy từ đoạn tĩnh ngay trước {id} là "Feedbacks" — kể cả POST
        // .../{id}/replies (KHÔNG phải "FeedbackReplies": đây là endpoint con của phản ánh, nhật
        // ký trỏ về phản ánh để tra vết cả luồng đối thoại — ghi chú ở docs/api-contract.md).
        ("PATCH",  "/api/admin/feedbacks/{id:guid}",             CachGhi.Middleware),
        ("POST",   "/api/admin/feedbacks/{id:guid}/replies",     CachGhi.Middleware),

        // Gửi phản ánh — story 24, Trần Trung Hiếu. Middleware tự ghi (POST → Create); route không
        // có tham số nào nên tên bảng suy từ đoạn tĩnh cuối là "Feedbacks", Target là id phản ánh
        // mới đọc từ body trả về — cùng lối POST /api/monthly-passes.
        ("POST",   "/api/feedbacks",                              CachGhi.Middleware),

        ("POST",   "/api/stops",                                 CachGhi.Middleware),
        ("PUT",    "/api/stops/{id:guid}",                       CachGhi.Middleware),
        ("DELETE", "/api/stops/{id:guid}",                       CachGhi.Middleware),

        // Kiểm tra voucher — story 18, Nguyễn Duy Kiên. POST nhưng KHÔNG đổi dữ liệu: đây là phép
        // đọc thuần (tra voucher + tính tiền giảm), dùng POST chỉ vì có body. Màn thanh toán gọi
        // endpoint này mỗi lần khách gõ thêm một ký tự, nên để middleware ghi theo verb sẽ sinh một
        // dòng Create giả cho mỗi phím gõ — và Target suy ra là "Validate", một bảng không tồn tại.
        // Vì vậy đường dẫn này nằm trong AuditLogMiddleware.IgnoredPaths.
        //
        // ⚠️ Ca "POST chỉ đọc" đầu tiên của dự án; còn POST /api/tickets/validate (Sprint 4) và các
        // endpoint hành động con của /seat-holds sẽ gặp đúng vấn đề này — nhóm cần chốt MỘT cách
        // xử lý chung thay vì mỗi task thêm một dòng ngoại lệ.
        ("POST",   "/api/vouchers/validate",                     CachGhi.KhongGhi),

        // Soát vé — story 15, Trần Trung Hiếu (Sprint 4, dòng 15 + 19). POST CÓ đổi dữ liệu: mỗi
        // lượt quét ghi một dòng TicketValidations qua ITicketValidationRecorder, nên middleware tự
        // ghi AuditLogs (Create) chứ KHÔNG KhongGhi như /vouchers/validate ngay trên — phép đọc thuần
        // đằng kia không ghi gì, lượt soát bên này ghi thật.
        // ⚠️ GHI NHẬN — Target chỉ là "Validate", KHÔNG phải "TicketValidations:<id>": route không
        // có tham số {id} nên ResolveTableName rơi về đoạn tĩnh cuối (giới hạn đã được nhắc trước
        // trong doc-comment của AuditLogMiddleware), và response của hợp đồng không có trường "id"
        // ở nóc để ghép. Nhật ký thật của lượt soát nằm ở bảng TicketValidations; dòng AuditLogs
        // này chỉ chứng minh "có người đã gọi endpoint soát vé lúc nào". Cùng nhóm giới hạn với hai
        // dòng {sessionCode} của /seat-holds — nhóm cần chốt MỘT cách xử lý chung; test ghim sự thật
        // này nằm ở TicketValidationApiTests.
        ("POST",   "/api/tickets/validate",                     CachGhi.Middleware),

        // CRUD voucher — story 18, Trần Trung Hiếu. Ba endpoint đều do AuditLogMiddleware tự ghi
        // (POST → Create, PUT/PATCH → Update); tên bảng suy ra là "Vouchers" — POST không có tham số
        // nào nên lấy đoạn tĩnh cuối, PUT/PATCH lấy đoạn trước {id}. Target của POST là id voucher
        // mới đọc từ body trả về — cùng lối POST /api/monthly-passes.
        ("POST",   "/api/vouchers",                               CachGhi.Middleware),
        ("PUT",    "/api/vouchers/{id:guid}",                     CachGhi.Middleware),
        ("PATCH",  "/api/vouchers/{id:guid}/status",              CachGhi.Middleware),

        // Duyệt / từ chối yêu cầu huỷ - đổi vé — story 5, Phùng Duy Hoàng. Hai endpoint đều do
        // AuditLogMiddleware tự ghi (PATCH → Update); {id} đặt tên đúng để tên bảng suy từ đoạn
        // trước nó là "CancellationRequests" / "TicketChanges", Target "CancellationRequests:{id}" /
        // "TicketChanges:{id}". Id trong Target là id YÊU CẦU trên đường dẫn, không phải id vé mới
        // mà lượt duyệt đổi phát hành: PATCH không đệm response nên middleware không đọc body, và
        // cũng không cần — cả hai lượt đều tác động lên chính dòng yêu cầu.
        //
        // KHÔNG khai là TuGhi: service duyệt không gọi IAuditLogService, verb đã nói đủ hành động.
        // Test hành vi của hai dòng này nằm ở TicketRequestReviewWiringTests chứ không ở Phần 2
        // dưới đây — lượt duyệt ĐỔI phát hành vé mới nên cần TicketQrService, mà TestAppFactory của
        // lớp này không đặt TicketQr:SigningKey; dựng nó ở đây là phải đổi cả lớp sang
        // WithWebHostBuilder, kéo theo toàn bộ helper seed/token của file.
        ("PATCH",  "/api/cancellation-requests/{id:guid}",       CachGhi.Middleware),
        ("PATCH",  "/api/ticket-changes/{id:guid}",              CachGhi.Middleware),

        // Thanh toán — story 6, Trần Trung Hiếu (nợ Sprint 3). Ba endpoint đều do AuditLogMiddleware
        // tự ghi (POST → Create). POST /payments: tên bảng suy từ đoạn tĩnh cuối là "Payments"; response
        // không có trường "id" ở nóc (khoá là paymentCode) nên Target chỉ là "Payments" — giới hạn đã
        // biết của CreatedIdFromResponse. Hai callback: route không có tham số {id} nên tên bảng suy
        // ra là "Callback" (không ứng bảng nào) — đánh đổi đã biết, cùng nhóm giới hạn với các
        // endpoint hành động con của /seat-holds; dòng nhật ký chỉ chứng minh "cổng đã gọi callback
        // lúc nào" và UserId null vì cổng không mang token. Callback VNPay (GET) là task của Kiên,
        // không có ở nhánh này.
        ("POST",   "/api/payments",                               CachGhi.Middleware),
        ("POST",   "/api/payments/momo/callback",                 CachGhi.Middleware),
        ("POST",   "/api/payments/zalopay/callback",              CachGhi.Middleware),

        // Đăng ký / huỷ theo dõi thông báo — story 10, Trần Trung Hiếu (Sprint 5 dòng 17). Hai
        // endpoint đều do AuditLogMiddleware tự ghi (POST → Create, DELETE → Delete); tên bảng suy
        // ra là "Subscriptions" — POST không có tham số nào nên lấy đoạn tĩnh cuối, DELETE lấy đoạn
        // trước {id}. Target của POST là id dòng theo dõi mới đọc từ body trả về (response có trường
        // "id") — cùng lối POST /api/monthly-passes. Huỷ theo dõi là XOÁ dòng chứ không lật cờ
        // (docs/37 §3.2) nên DELETE ghi Delete, không có PATCH nào.
        ("POST",   "/api/subscriptions",                          CachGhi.Middleware),
        ("DELETE", "/api/subscriptions/{id:guid}",                CachGhi.Middleware),

        // Thiết bị Web Push — story 10, Trần Trung Hiếu (Sprint 4 dòng 42). Ba endpoint đều do
        // AuditLogMiddleware tự ghi (POST → Create, DELETE → Delete). Tên tài nguyên đặt là
        // "devices" chứ không phải "subscriptions" để Target KHÔNG đụng bảng Subscriptions thật
        // (theo dõi tuyến/trạm, docs/37) — "Devices" là tên bảng CHƯA tồn tại: store thiết bị hiện
        // ở trong bộ nhớ chờ migration của Dăm (docs/54 §6), dòng nhật ký là vết DUY NHẤT còn lại
        // sau khi backend khởi động lại. POST /devices trả body có id nên Target là "Devices:{id}"
        // đọc từ body; POST /devices/{id}/test là ca "POST nhưng chỉ đọc" (như /tickets/validate)
        // — vẫn ghi Create CÓ CHỦ ĐÍCH vì gửi thông báo là hành động đáng lưu vết, Target lấy id
        // từ đường dẫn. Test hành vi ở WebPushApiTests.
        ("POST",   "/api/web-push/devices",                       CachGhi.Middleware),
        ("POST",   "/api/web-push/devices/{id:guid}/test",        CachGhi.Middleware),
        ("DELETE", "/api/web-push/devices/{id:guid}",             CachGhi.Middleware),

        // Màn quản lý mẫu thông báo — story 10, Dương Thị Hạnh (Sprint 5 dòng 24). PUT tự ghi qua
        // AuditLogMiddleware (Update). Khoá trên đường dẫn là {type} chứ không phải {id} nên tên bảng
        // suy ra là "NotificationTemplates" nhưng Target KHÔNG kèm id — đủ biết "có người sửa mẫu
        // lúc nào", chấp nhận được (đã ghi chú ở controller và docs/api-contract.md).
        ("PUT",    "/api/notification-templates/{type}",          CachGhi.Middleware),

        // Gửi yêu cầu đổi chuyến (US 5, dòng 4 Sprint 4 — Nguyễn Duy Kiên). POST thường: middleware
        // suy hành động từ verb (Create), Target "TicketChanges:{id}" — id yêu cầu do service sinh và
        // trả về trong response; middleware đệm response với POST nên đọc được.
        ("POST",   "/api/ticket-changes",                        CachGhi.Middleware),

        // Đặt job xuất báo cáo lớn ở nền — story 24, Vàng Thị Dăm (Sprint 5 dòng 64). Middleware tự
        // ghi (POST → Create); route không có tham số nào nên tên bảng suy từ đoạn tĩnh cuối là
        // "Exports", Target là id job mới đọc từ body trả về — cùng lối POST /api/monthly-passes.
        // ⚠️ GHI NHẬN — tên bảng thật là "ExportJobs", không phải "Exports": cùng nhóm giới hạn với
        // "Drivers" (bảng thật là Users) ở trên. Id trong Target vẫn tra được đúng dòng ExportJobs;
        // chỉ tên bảng là tên tài nguyên trên đường dẫn chứ không phải tên bảng CSDL.
        ("POST",   "/api/exports",                                CachGhi.Middleware),
    ];

    /// <summary>Cách một endpoint thay đổi dữ liệu đi vào nhật ký.</summary>
    private enum CachGhi
    {
        /// <summary>AuditLogMiddleware tự ghi, suy hành động từ HTTP verb.</summary>
        Middleware,

        /// <summary>Controller tự gọi IAuditLogService vì verb không nói lên được hành động.</summary>
        TuGhi,

        /// <summary>Không ghi — đánh đổi đã biết, có ghi lý do trong mã nguồn.</summary>
        KhongGhi,
    }

    [Fact]
    public void Bang_phu_song_khop_voi_routing_that_cua_app()
    {
        using var factory = new TestAppFactory();

        var thucTe = EndpointsThayDoiDuLieu(factory);
        var khaiBao = BangPhuSong
            .Select(ca => $"{ca.Verb} {ca.Pattern}")
            .ToHashSet(StringComparer.Ordinal);

        var thieuTrongBang = thucTe.Except(khaiBao).OrderBy(row => row, StringComparer.Ordinal).ToList();
        var thuaTrongBang = khaiBao.Except(thucTe).OrderBy(row => row, StringComparer.Ordinal).ToList();

        // Endpoint mới mà không khai vào bảng: người thêm phải TỰ QUYẾT nó được ghi thế nào,
        // và viết test hành vi cho nó. Đây là điều kiện để câu "mọi thao tác đều được ghi log"
        // còn kiểm chứng được ở Sprint sau, chứ không chỉ đúng ở Sprint 1.
        Assert.True(
            thieuTrongBang.Count == 0,
            "Endpoint thay đổi dữ liệu CHƯA khai trong bảng phủ sóng:\n  "
                + string.Join("\n  ", thieuTrongBang));

        // Chiều ngược lại: bảng trỏ tới endpoint không còn tồn tại, tức là bảng đang nói dối
        // về phạm vi đã kiểm.
        Assert.True(
            thuaTrongBang.Count == 0,
            "Bảng phủ sóng trỏ tới endpoint KHÔNG còn tồn tại:\n  "
                + string.Join("\n  ", thuaTrongBang));
    }

    /// <summary>
    /// Endpoint thay đổi dữ liệu theo routing THẬT của app — đọc từ
    /// <see cref="EndpointDataSource"/> sau khi host đã dựng, không đọc bằng mắt từ mã nguồn.
    /// Bỏ qua nhóm <c>/api/_test/</c>: đó là controller của chính bộ test, không phải bề mặt API
    /// của dự án.
    /// </summary>
    private static HashSet<string> EndpointsThayDoiDuLieu(TestAppFactory factory)
    {
        // Dựng host rồi mới hỏi được routing — MapControllers chạy trong lúc build pipeline.
        factory.CreateClient();

        string[] verbThayDoi = ["POST", "PUT", "PATCH", "DELETE"];

        return factory.Services
            .GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .SelectMany(endpoint => (endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [])
                .Select(verb => $"{verb} /{endpoint.RoutePattern.RawText}"))
            .Where(row => verbThayDoi.Contains(row.Split(' ')[0], StringComparer.Ordinal))
            .Where(row => !row.Contains("/api/_test/", StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);
    }

    // =======================================================================================
    // PHẦN 2 — Từng endpoint thay đổi dữ liệu: có ghi, đúng hành động, đúng đối tượng
    // =======================================================================================

    // ----------------------------------------------------------------------------------- Users

    [Fact]
    public async Task Post_admin_users_ghi_Create_dung_ban_ghi()
    {
        using var factory = new TestAppFactory();
        var (client, admin) = await SignInAsAdminAsync(factory);

        var response = await client.PostAsJsonAsync(UsersUrl, new
        {
            fullName = "Tài Xế Mới",
            phoneNumber = "0912345679",
            password = MatKhau,
            roleCode = RoleCodes.Driver,
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        await AssertMotBanGhiAsync(factory, AuditAction.Create, $"Users:{await IdOfAsync(response)}", admin);
    }

    [Fact]
    public async Task Put_admin_users_ghi_Update_dung_ban_ghi()
    {
        using var factory = new TestAppFactory();
        var (client, admin) = await SignInAsAdminAsync(factory);
        var target = await SeedUserAsync(factory, RoleIds.Driver, RoleCodes.Driver, phoneNumber: "0911111111");

        var response = await client.PutAsJsonAsync($"{UsersUrl}/{target.Id}", new
        {
            fullName = "Tài Xế Đổi Tên",
            phoneNumber = "0911111111",
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await AssertMotBanGhiAsync(factory, AuditAction.Update, $"Users:{target.Id}", admin);
    }

    [Fact]
    public async Task Delete_admin_users_ghi_Delete_dung_ban_ghi()
    {
        using var factory = new TestAppFactory();
        var (client, admin) = await SignInAsAdminAsync(factory);
        var target = await SeedUserAsync(factory, RoleIds.Driver, RoleCodes.Driver, phoneNumber: "0911111111");

        // Xoá mềm: khoá tài khoản, dữ liệu vẫn còn. Không tự xoá được chính mình nên phải
        // nhắm vào tài khoản khác.
        var response = await client.DeleteAsync($"{UsersUrl}/{target.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await AssertMotBanGhiAsync(factory, AuditAction.Delete, $"Users:{target.Id}", admin);
    }

    [Fact]
    public async Task Patch_admin_users_status_ghi_Update_dung_ban_ghi()
    {
        using var factory = new TestAppFactory();
        var (client, admin) = await SignInAsAdminAsync(factory);
        var target = await SeedUserAsync(factory, RoleIds.Driver, RoleCodes.Driver, phoneNumber: "0911111111");

        var response = await client.PatchAsJsonAsync($"{UsersUrl}/{target.Id}/status", new { isActive = false });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await AssertMotBanGhiAsync(factory, AuditAction.Update, $"Users:{target.Id}", admin);
    }

    [Fact]
    public async Task Put_admin_users_roles_ghi_Update_dung_ban_ghi()
    {
        using var factory = new TestAppFactory();
        var (client, admin) = await SignInAsAdminAsync(factory);
        var target = await SeedUserAsync(factory, RoleIds.Passenger, RoleCodes.Passenger, phoneNumber: "0911111111");

        var response = await client.PutAsJsonAsync(
            $"{UsersUrl}/{target.Id}/roles", new { roleCodes = new[] { RoleCodes.Driver } });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await AssertMotBanGhiAsync(factory, AuditAction.Update, $"Users:{target.Id}", admin);
    }

    // ----------------------------------------------------------------------------------- Stops

    [Fact]
    public async Task Post_stops_ghi_Create_dung_ban_ghi()
    {
        using var factory = new TestAppFactory();
        var (client, admin) = await SignInAsAdminAsync(factory);

        var response = await client.PostAsJsonAsync(StopsUrl, StopBody("Trạm Cầu Giấy"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        await AssertMotBanGhiAsync(factory, AuditAction.Create, $"Stops:{await IdOfAsync(response)}", admin);
    }

    [Fact]
    public async Task Put_stops_ghi_Update_dung_ban_ghi()
    {
        using var factory = new TestAppFactory();
        var (client, admin) = await SignInAsAdminAsync(factory);
        var stop = await SeedStopAsync(factory, "Trạm Cầu Giấy");

        var response = await client.PutAsJsonAsync($"{StopsUrl}/{stop.Id}", StopBody("Trạm Cầu Giấy (đổi tên)"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await AssertMotBanGhiAsync(factory, AuditAction.Update, $"Stops:{stop.Id}", admin);
    }

    [Fact]
    public async Task Delete_stops_ghi_Delete_dung_ban_ghi()
    {
        using var factory = new TestAppFactory();
        var (client, admin) = await SignInAsAdminAsync(factory);
        var stop = await SeedStopAsync(factory, "Trạm Cầu Giấy");

        var response = await client.DeleteAsync($"{StopsUrl}/{stop.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        await AssertMotBanGhiAsync(factory, AuditAction.Delete, $"Stops:{stop.Id}", admin);
    }

    // ---------------------------------------------------------------------------------- Routes

    [Fact]
    public async Task Post_routes_ghi_Create_dung_ban_ghi()
    {
        using var factory = new TestAppFactory();
        var (client, admin) = await SignInAsAdminAsync(factory);

        var response = await client.PostAsJsonAsync(RoutesUrl, RouteBody("01"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        await AssertMotBanGhiAsync(factory, AuditAction.Create, $"Routes:{await IdOfAsync(response)}", admin);
    }

    [Fact]
    public async Task Put_routes_ghi_Update_dung_ban_ghi()
    {
        using var factory = new TestAppFactory();
        var (client, admin) = await SignInAsAdminAsync(factory);
        var route = await SeedRouteAsync(factory, "01");

        var response = await client.PutAsJsonAsync($"{RoutesUrl}/{route.Id}", RouteBody("01", ten: "Tuyến 01 (đổi tên)"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await AssertMotBanGhiAsync(factory, AuditAction.Update, $"Routes:{route.Id}", admin);
    }

    [Fact]
    public async Task Delete_routes_ghi_Delete_dung_ban_ghi()
    {
        using var factory = new TestAppFactory();
        var (client, admin) = await SignInAsAdminAsync(factory);
        var route = await SeedRouteAsync(factory, "01");

        var response = await client.DeleteAsync($"{RoutesUrl}/{route.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await AssertMotBanGhiAsync(factory, AuditAction.Delete, $"Routes:{route.Id}", admin);
    }

    // ----------------------------------------------------------------------------------- Fares

    [Fact]
    public async Task Post_fares_ghi_Create_voi_ten_bang_Fares()
    {
        using var factory = new TestAppFactory();
        var (client, admin) = await SignInAsAdminAsync(factory);
        var route = await SeedRouteAsync(factory, "01");

        var response = await client.PostAsJsonAsync(
            FaresUrl(route.Id), new { passengerType = "Standard", price = 7000m });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        await AssertMotBanGhiAsync(factory, AuditAction.Create, $"Fares:{await IdOfAsync(response)}", admin);
    }

    [Fact]
    public async Task Put_fares_ghi_Update_voi_ten_bang_Fares()
    {
        using var factory = new TestAppFactory();
        var (client, admin) = await SignInAsAdminAsync(factory);
        var route = await SeedRouteAsync(factory, "01");
        var fare = await SeedFareAsync(factory, route.Id, PassengerType.Standard, 7000m);

        var response = await client.PutAsJsonAsync($"{FaresUrl(route.Id)}/{fare.Id}", new { price = 8000m });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await AssertMotBanGhiAsync(factory, AuditAction.Update, $"Fares:{fare.Id}", admin);
    }

    [Fact]
    public async Task Delete_fares_ghi_Delete_voi_ten_bang_Fares()
    {
        using var factory = new TestAppFactory();
        var (client, admin) = await SignInAsAdminAsync(factory);
        var route = await SeedRouteAsync(factory, "01");
        var fare = await SeedFareAsync(factory, route.Id, PassengerType.Standard, 7000m);

        var response = await client.DeleteAsync($"{FaresUrl(route.Id)}/{fare.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        await AssertMotBanGhiAsync(factory, AuditAction.Delete, $"Fares:{fare.Id}", admin);
    }

    // ------------------------------------------------------------------------------- RouteStops

    [Fact]
    public async Task Post_route_stops_ghi_Create()
    {
        using var factory = new TestAppFactory();
        var (client, admin) = await SignInAsAdminAsync(factory);
        var route = await SeedRouteAsync(factory, "01");
        var stop = await SeedStopAsync(factory, "Trạm Cầu Giấy");

        var response = await client.PostAsJsonAsync(
            RouteStopsUrl(route.Id), new { stopId = stop.Id, distanceKm = 3.5m });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var log = await AssertMotBanGhiAsync(factory, AuditAction.Create, target: null, admin);
        Assert.NotNull(log.Target);

        // ⚠️ GHI NHẬN — không phải khẳng định hành vi đúng, mà là ghim sự thật lại:
        // bản ghi vừa tạo là một DÒNG BẢNG NỐI RouteStops, nhưng Target lại ghi bảng "Stops".
        // Test ngay dưới chứng minh id đó không tồn tại trong bảng Stops.
        //
        // So sánh bằng Guid đã parse chứ không so chuỗi: middleware lấy id từ body JSON còn
        // response ở đây đi qua System.Text.Json, hai bên có thể khác nhau về chữ hoa/thường.
        Assert.StartsWith("Stops:", log.Target, StringComparison.Ordinal);
        Assert.Equal(await IdOfAsync(response), Guid.Parse(log.Target!["Stops:".Length..]));
    }

    [Fact]
    public async Task Post_route_stops_Target_tro_toi_id_khong_ton_tai_trong_bang_Stops()
    {
        using var factory = new TestAppFactory();
        var (client, _) = await SignInAsAdminAsync(factory);
        var route = await SeedRouteAsync(factory, "01");
        var stop = await SeedStopAsync(factory, "Trạm Cầu Giấy");

        var response = await client.PostAsJsonAsync(
            RouteStopsUrl(route.Id), new { stopId = stop.Id, distanceKm = 3.5m });
        var idTrongTarget = await IdOfAsync(response);

        var log = Assert.Single(await LogsAsync(factory));

        // Target nói "Stops:<id>" nhưng id đó là khoá của DÒNG RouteStops. Kiểm toán viên tra
        // id này trong bảng Stops sẽ không thấy gì — dấu vết dẫn tới ngõ cụt.
        //
        // Đây là hệ quả của luật suy tên bảng trong AuditLogMiddleware: route không có tham số
        // {id} thì lấy đoạn tĩnh CUỐI CÙNG, mà đoạn cuối ở đây là "stops" — tên bảng của tài
        // nguyên CHA trong đường dẫn, không phải của bản ghi bị tạo.
        Assert.Equal($"Stops:{idTrongTarget}", log.Target);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        Assert.False(await db.Stops.AnyAsync(item => item.Id == idTrongTarget));
        Assert.True(await db.RouteStops.AnyAsync(item => item.Id == idTrongTarget));
    }

    [Fact]
    public async Task Put_route_stops_order_ghi_Update()
    {
        using var factory = new TestAppFactory();
        var (client, admin) = await SignInAsAdminAsync(factory);
        var route = await SeedRouteAsync(factory, "01");
        var a = await SeedStopAsync(factory, "Trạm A");
        var b = await SeedStopAsync(factory, "Trạm B");
        await SeedRouteStopAsync(factory, route.Id, a.Id, stopOrder: 1);
        await SeedRouteStopAsync(factory, route.Id, b.Id, stopOrder: 2);

        var response = await client.PutAsJsonAsync(
            $"{RouteStopsUrl(route.Id)}/order",
            new { items = new[] { new { stopId = b.Id }, new { stopId = a.Id } } });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var log = await AssertMotBanGhiAsync(factory, AuditAction.Update, target: null, admin);

        // Tài nguyên bị tác động là NHÓM TRẠM của tuyến, không phải hành động "order".
        //
        // Đây từng là ca hỏng nặng nhất trong ba endpoint RouteStops: middleware lấy đoạn tĩnh
        // cuối cùng làm tên bảng nên ra "Order" — không ứng với bảng nào; và vì route không có
        // tham số {id} nên Target mất luôn id đối tượng, dòng nhật ký chỉ còn "có người đã sửa
        // một thứ gì đó". Doc-comment của ResolveTableName khi đó tự trấn an rằng "bản ghi vẫn có
        // id đối tượng trong Target nên tra ngược được" — lời trấn an đó không đúng ở đây.
        //
        // Đã sửa ở AuditLogMiddleware.ResourceSegmentIndex: route có tham số thì tài nguyên là
        // đoạn tĩnh ĐẦU TIÊN đứng sau tham số cuối, không phải đoạn tĩnh cuối cùng. "Stops" cũng
        // khớp tên bảng mà hai endpoint anh em POST/DELETE .../stops đang ghi.
        Assert.Equal("Stops", log.Target);
    }

    [Fact]
    public async Task Delete_route_stops_ghi_Delete()
    {
        using var factory = new TestAppFactory();
        var (client, admin) = await SignInAsAdminAsync(factory);
        var route = await SeedRouteAsync(factory, "01");
        var stop = await SeedStopAsync(factory, "Trạm Cầu Giấy");
        var row = await SeedRouteStopAsync(factory, route.Id, stop.Id, stopOrder: 1);

        var response = await client.DeleteAsync($"{RouteStopsUrl(route.Id)}/{row.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var log = await AssertMotBanGhiAsync(factory, AuditAction.Delete, target: null, admin);

        // ⚠️ GHI NHẬN — cùng vấn đề như POST: dòng bị gỡ là RouteStops nhưng Target ghi "Stops".
        // Id trên đường dẫn ĐÃ đúng tên {id} (RouteStopsController.Remove có doc-comment nói rõ
        // phải đặt tên vậy để middleware nhận ra), nên phần id là chính xác — chỉ tên bảng sai.
        Assert.Equal($"Stops:{row.Id}", log.Target);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        Assert.False(await db.Stops.AnyAsync(item => item.Id == row.Id));
    }

    // ---------------------------------------------------------------------------------- Drivers

    [Fact]
    public async Task Post_drivers_ghi_Create_dung_ban_ghi()
    {
        using var factory = new TestAppFactory();
        var (client, admin) = await SignInAsAdminAsync(factory);

        var response = await client.PostAsJsonAsync(DriversUrl, new
        {
            fullName = "Tài Xế Mới",
            phoneNumber = "0912345679",
            password = MatKhau,
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        // ⚠️ GHI NHẬN — tên bảng trong Target là "Drivers" dù bản ghi vừa tạo nằm ở bảng Users:
        // AuditLogMiddleware suy tên từ route (api/drivers) chứ không đọc entity. Đã ghi chú
        // ở docs/api-contract.md mục "Hồ sơ tài xế — /drivers".
        await AssertMotBanGhiAsync(factory, AuditAction.Create, $"Drivers:{await IdOfAsync(response)}", admin);
    }

    [Fact]
    public async Task Put_drivers_ghi_Update_dung_ban_ghi()
    {
        using var factory = new TestAppFactory();
        var (client, admin) = await SignInAsAdminAsync(factory);
        var target = await SeedUserAsync(factory, RoleIds.Driver, RoleCodes.Driver, phoneNumber: "0911111111");

        var response = await client.PutAsJsonAsync($"{DriversUrl}/{target.Id}", new
        {
            fullName = "Tài Xế Đổi Tên",
            phoneNumber = "0911111111",
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // ⚠️ GHI NHẬN — cùng ca như POST: tên bảng trong Target là "Drivers", bảng thật là Users.
        await AssertMotBanGhiAsync(factory, AuditAction.Update, $"Drivers:{target.Id}", admin);
    }

    [Fact]
    public async Task Delete_drivers_ghi_Delete_dung_ban_ghi()
    {
        using var factory = new TestAppFactory();
        var (client, admin) = await SignInAsAdminAsync(factory);
        var target = await SeedUserAsync(factory, RoleIds.Driver, RoleCodes.Driver, phoneNumber: "0911111111");

        // Xoá mềm: khóa tài khoản, dữ liệu vẫn còn. Không tự xoá được chính mình nên phải
        // nhắm vào tài khoản khác.
        var response = await client.DeleteAsync($"{DriversUrl}/{target.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // ⚠️ GHI NHẬN — cùng ca như POST: tên bảng trong Target là "Drivers", bảng thật là Users.
        await AssertMotBanGhiAsync(factory, AuditAction.Delete, $"Drivers:{target.Id}", admin);
    }

    // ----------------------------------------------------------------------------------- Trips

    // Story 14, Phùng Duy Hoàng: đổi xe/đổi tài xế khi có sự cố. Phần ghi nhật ký của endpoint
    // này chạy qua middleware như mọi endpoint khác — khác biệt duy nhất là việc đổi phân công
    // KHÔNG bị chặn khi trùng lịch (200 kèm cảnh báo), nên có một ca dễ tưởng là không ghi:
    // request thành công nhưng thực tế có đổi dữ liệu thì vẫn phải có ĐÚNG MỘT bản ghi.
    [Fact]
    public async Task Patch_trips_assignment_ghi_Update_dung_ban_ghi()
    {
        using var factory = new TestAppFactory();
        var (client, admin) = await SignInAsAdminAsync(factory);
        var route = await SeedRouteAsync(factory, "01");
        var busCu = await SeedBusAsync(factory, "29B-111.11");
        var busMoi = await SeedBusAsync(factory, "29B-222.22");
        var trip = await SeedTripAsync(factory, route.Id, busCu.Id);

        var response = await client.PatchAsJsonAsync(
            $"{TripsUrl}/{trip.Id}/assignment", new { busId = busMoi.Id });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await AssertMotBanGhiAsync(factory, AuditAction.Update, $"Trips:{trip.Id}", admin);
    }

    // Gán tài xế theo lô (task 113 — Nguyễn Duy Kiên) cũng là một endpoint "Trips" nữa, nhưng
    // route của nó KHÔNG có {id} nên dòng nhật ký chỉ ghi được tên bảng. Ca này ghim sự thật đó.
    [Fact]
    public async Task Patch_route_trips_driver_assignment_ghi_Update_voi_ten_bang_Trips()
    {
        using var factory = new TestAppFactory();
        var (client, admin) = await SignInAsAdminAsync(factory);
        var route = await SeedRouteAsync(factory, "01");
        var bus = await SeedBusAsync(factory, "29B-123.45");
        var trip = await SeedTripAsync(factory, route.Id, bus.Id);
        var driver = await SeedUserAsync(factory, RoleIds.Driver, RoleCodes.Driver, phoneNumber: "0911111111");

        var response = await client.PatchAsJsonAsync(
            $"{RouteTripsUrl(route.Id)}/driver-assignment",
            new { tripIds = new[] { trip.Id }, driverId = driver.Id });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var log = await AssertMotBanGhiAsync(factory, AuditAction.Update, target: null, admin);

        // PATCH → Update, và tên bảng đúng là "Trips" — cùng tên bảng mà bốn endpoint anh em
        // POST/PUT/DELETE .../trips đang ghi (xem chú thích ở bảng phủ sóng, Phần 1).
        //
        // Target CHỈ có tên bảng, không kèm id: route chỉ có {routeId} (khoá của tài nguyên cha,
        // middleware cố ý không nhận — xem AuditLogMiddleware.RouteId), mà body trả về cũng không
        // có trường "id" ở mức gốc (id từng chuyến nằm trong "items"). Đối tượng bị sửa nằm trong
        // thân request nên middleware không đọc được — cùng giới hạn đã ghi nhận ở
        // PUT .../stops/order. Test này ghim sự thật đó lại để lần sau có ai đổi hình dạng
        // response thì thấy ngay nhật ký đổi theo.
        Assert.Equal("Trips", log.Target);
    }

    // Sinh chuyến cho cả đội xe (nợ của đợt mở rộng đội xe 17/10/2026 — Phùng Duy Hoàng).
    // Endpoint mới nhưng vẫn đi qua middleware như mọi POST khác; ca này chứng minh điều đó và
    // ghim luôn giới hạn Target: route không có {id}, còn response không có trường "id" ở nóc
    // (id từng chuyến nằm trong "items"), nên nhật ký chỉ ghi được tên bảng "Trips".
    [Fact]
    public async Task Post_route_trips_generate_fleet_ghi_Create_voi_ten_bang_Trips()
    {
        using var factory = new TestAppFactory();
        var (client, admin) = await SignInAsAdminAsync(factory);
        var route = await SeedRouteAsync(factory, "01");
        var bus = await SeedBusAsync(factory, "29B-123.45");

        var response = await client.PostAsJsonAsync(
            $"{RouteTripsUrl(route.Id)}/generate-fleet",
            new
            {
                busIds = new[] { bus.Id },
                startTime = "2026-10-17T06:00:00Z",
                endTime = "2026-10-17T06:15:00Z",
                frequencyMinutes = 15,
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var log = await AssertMotBanGhiAsync(factory, AuditAction.Create, target: null, admin);
        Assert.Equal("Trips", log.Target);
    }

    // -------------------------------------------------------- Vé tháng — gia hạn (story 16)

    // Story 16, Phùng Duy Hoàng: gia hạn vé tháng. Người gọi phải là CHÍNH CHỦ VÉ — service lọc
    // theo UserId trong truy vấn nên token Admin gia hạn hộ vé của người khác sẽ ra 404, không
    // phải 200. Vì vậy ca này đăng nhập bằng tài khoản hành khách, không dùng SignInAsAdminAsync.
    [Fact]
    public async Task Post_monthly_passes_renew_ghi_Create_dung_ban_ghi()
    {
        using var factory = new TestAppFactory();
        await EnsureAllRolesAsync(factory);
        var chuVe = await SeedUserAsync(factory, RoleIds.Passenger, RoleCodes.Passenger, phoneNumber: "0911111111");
        var client = ClientWith(factory, factory.CreateTokenFor(chuVe));

        var route = await SeedRouteAsync(factory, "01");
        var passType = await SeedPassTypeAsync(factory, "OneMonth", durationMonths: 1, price: 200_000m);
        var veCu = await SeedMonthlyPassAsync(factory, chuVe.Id, route.Id, passType.Id);

        var response = await client.PostAsync($"{MonthlyPassesUrl}/{veCu.Id}/renew", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // ⚠️ GHI NHẬN — Target là id vé MỚI (đọc từ body trả về), KHÔNG phải {id} vé cũ trên đường
        // dẫn: middleware ưu tiên CreatedIdFromResponse cho POST. Dòng nhật ký trỏ đúng bản ghi
        // vừa sinh — cùng lối mọi endpoint POST khác, nhưng ở đây dễ tưởng nhầm vì đường dẫn đã
        // có sẵn một id vé.
        var veMoi = await IdOfAsync(response);
        Assert.NotEqual(veCu.Id, veMoi);

        await AssertMotBanGhiAsync(factory, AuditAction.Create, $"MonthlyPasses:{veMoi}", chuVe);
    }

    // ------------------------------------- Quản trị vé tháng — huỷ/mở lại (ngoài story 16)

    // Nguyễn Duy Kiên: PATCH đổi trạng thái vé. Middleware tự ghi (PATCH → Update); {id} đặt tên
    // đúng để tên bảng suy từ đoạn trước nó là "MonthlyPasses".
    [Fact]
    public async Task Patch_monthly_passes_status_ghi_Update_dung_ban_ghi()
    {
        using var factory = new TestAppFactory();
        var (client, quanLy) = await SignInAsManagerAsync(factory);
        var chuVe = await SeedUserAsync(factory, RoleIds.Passenger, RoleCodes.Passenger, phoneNumber: "0911111111");
        var route = await SeedRouteAsync(factory, "01");
        var passType = await SeedPassTypeAsync(factory, "OneMonth", durationMonths: 1, price: 200_000m);
        var ve = await SeedMonthlyPassAsync(factory, chuVe.Id, route.Id, passType.Id);

        var response = await client.PatchAsJsonAsync(
            $"{MonthlyPassesUrl}/{ve.Id}/status", new { status = "Cancelled" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await AssertMotBanGhiAsync(factory, AuditAction.Update, $"MonthlyPasses:{ve.Id}", quanLy);
    }

    // ------------------------------------------------- CRUD loại vé tháng (ngoài story 16)

    // Nguyễn Duy Kiên: danh mục loại vé tháng. Ba endpoint đều đi qua AuditLogMiddleware;
    // policy ManagerOrAbove nên đăng nhập bằng tài khoản quản lý.

    [Fact]
    public async Task Post_pass_types_ghi_Create_dung_ban_ghi()
    {
        using var factory = new TestAppFactory();
        var (client, quanLy) = await SignInAsManagerAsync(factory);

        var response = await client.PostAsJsonAsync("/api/pass-types", new
        {
            code = "ThreeMonths",
            name = "Vé tháng 3 tháng",
            durationMonths = 3,
            price = 550_000m,
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        await AssertMotBanGhiAsync(factory, AuditAction.Create, $"PassTypes:{await IdOfAsync(response)}", quanLy);
    }

    [Fact]
    public async Task Put_pass_types_ghi_Update_dung_ban_ghi()
    {
        using var factory = new TestAppFactory();
        var (client, quanLy) = await SignInAsManagerAsync(factory);
        var passType = await SeedPassTypeAsync(factory, "OneMonth", durationMonths: 1, price: 200_000m);

        var response = await client.PutAsJsonAsync(
            $"/api/pass-types/{passType.Id}",
            new { name = "Vé tháng 1 tháng (mới)", durationMonths = 1, price = 220_000m });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await AssertMotBanGhiAsync(factory, AuditAction.Update, $"PassTypes:{passType.Id}", quanLy);
    }

    [Fact]
    public async Task Delete_pass_types_ghi_Delete_dung_ban_ghi()
    {
        using var factory = new TestAppFactory();
        var (client, quanLy) = await SignInAsManagerAsync(factory);
        // Loại vé chưa có vé tháng nào tham chiếu — xoá được (loại đang dùng trả 409).
        var passType = await SeedPassTypeAsync(factory, "SixMonths", durationMonths: 6, price: 1_000_000m);

        var response = await client.DeleteAsync($"/api/pass-types/{passType.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await AssertMotBanGhiAsync(factory, AuditAction.Delete, $"PassTypes:{passType.Id}", quanLy);
    }

    // ------------------------------------------------------- Giữ chỗ — gia hạn (story 3)

    // Story 3, Trần Trung Hiếu: gia hạn thời gian giữ chỗ. Người gọi phải là CHÍNH CHỦ PHIÊN —
    // service lọc theo UserId trong truy vấn nên token Admin gia hạn hộ phiên của người khác sẽ
    // ra 404, không phải 200. Vì vậy ca này đăng nhập bằng tài khoản hành khách, không dùng
    // SignInAsAdminAsync (cùng lối ca renew vé tháng ở trên).
    [Fact]
    public async Task Post_seat_holds_extend_ghi_Create_voi_Target_Extend()
    {
        using var factory = new TestAppFactory();
        await EnsureAllRolesAsync(factory);
        var chuPhien = await SeedUserAsync(factory, RoleIds.Passenger, RoleCodes.Passenger, phoneNumber: "0911111111");
        var client = ClientWith(factory, factory.CreateTokenFor(chuPhien));

        var trip = await SeedTripAsync(factory);
        var seat = await SeedSeatAsync(factory);
        await SeedHoldAsync(factory, chuPhien.Id, trip.Id, seat.Id, "PHIEN-AUDIT");

        var response = await client.PostAsync("/api/seat-holds/PHIEN-AUDIT/extend", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // ⚠️ GHI NHẬN — Target chỉ là "Extend", KHÔNG phải "SeatHolds:<mã phiên>": route dùng tham
        // số {sessionCode} để khớp hợp đồng chung của bề mặt (GET dùng chính tên này) nên
        // middleware không tìm thấy {id} để suy tên bảng — rơi về đoạn tĩnh đầu tiên sau tham số
        // cuối, đúng vào tên hành động "extend". Response của bề mặt (SeatHoldSession) không có
        // trường "id" nên không có id nào để ghép vào Target. Hành động Create do POST suy ra, dù
        // nghiệp vụ thật là cập nhật hạn — cùng kiểu đánh đổi đã ghi nhận ở ca renew vé tháng.
        // Giới hạn suy tên bảng nằm ở AuditLogMiddleware.ResourceSegmentIndex; test này ghim sự
        // thật lại để khi nhóm chốt cách xử lý cho các endpoint hành động con của /seat-holds
        // (release của Hoàng cũng dính) thì đổi ở đây và ở AuditLogMiddleware cùng một lúc.
        await AssertMotBanGhiAsync(factory, AuditAction.Create, "Extend", chuPhien);
    }

    // ----------------------------------------------------------- Giữ chỗ — nhả (story 3)

    // Story 3, Phùng Duy Hoàng: nhả ghế khi khách huỷ thao tác. Cùng ràng buộc chủ phiên như ca
    // gia hạn ngay trên — service lọc theo UserId trong truy vấn nên ca này đăng nhập bằng tài
    // khoản hành khách, không dùng SignInAsAdminAsync.
    [Fact]
    public async Task Post_seat_holds_release_ghi_Create_voi_Target_Release()
    {
        using var factory = new TestAppFactory();
        await EnsureAllRolesAsync(factory);
        var chuPhien = await SeedUserAsync(factory, RoleIds.Passenger, RoleCodes.Passenger, phoneNumber: "0911111111");
        var client = ClientWith(factory, factory.CreateTokenFor(chuPhien));

        var trip = await SeedTripAsync(factory);
        var seat = await SeedSeatAsync(factory);
        await SeedHoldAsync(factory, chuPhien.Id, trip.Id, seat.Id, "PHIEN-AUDIT");

        var response = await client.PostAsync("/api/seat-holds/PHIEN-AUDIT/release", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // ⚠️ GHI NHẬN — Target chỉ là "Release", KHÔNG phải "SeatHolds:<mã phiên>": cùng giới hạn
        // của ca extend ngay trên (route dùng {sessionCode} nên middleware không tìm thấy {id} để
        // suy tên bảng — rơi về đoạn tĩnh đầu tiên sau tham số cuối, đúng vào tên hành động; hợp
        // đồng SeatHoldSession cũng không có trường "id" để ghép vào Target). Hành động Create do
        // POST suy ra, dù nghiệp vụ thật là kết thúc lượt giữ. Khi nhóm chốt cách xử lý chung cho
        // các endpoint hành động con của /seat-holds thì đổi ở đây, ở ca extend và ở
        // AuditLogMiddleware cùng một lúc; test này ghim sự thật lại để lúc đó có chỗ mà sửa.
        await AssertMotBanGhiAsync(factory, AuditAction.Create, "Release", chuPhien);
    }

    // ------------------------------------------------- Phản ánh — Admin phản hồi (story 24)

    // Story 24, Phùng Duy Hoàng: Admin/Manager xử lý phản ánh. Hai endpoint ghi đều đi qua
    // AuditLogMiddleware; người thực hiện là quản lý đang đăng nhập (policy ManagerOrAbove).
    [Fact]
    public async Task Patch_admin_feedbacks_ghi_Update_dung_ban_ghi()
    {
        using var factory = new TestAppFactory();
        var (client, quanLy) = await SignInAsManagerAsync(factory);
        var hanhKhach = await SeedUserAsync(factory, RoleIds.Passenger, RoleCodes.Passenger, phoneNumber: "0911111111");
        var phanAnh = await SeedFeedbackAsync(factory, hanhKhach.Id);

        var response = await client.PatchAsJsonAsync(
            $"{AdminFeedbacksUrl}/{phanAnh.Id}", new { status = "InProgress" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await AssertMotBanGhiAsync(factory, AuditAction.Update, $"Feedbacks:{phanAnh.Id}", quanLy);
    }

    [Fact]
    public async Task Post_admin_feedbacks_replies_ghi_Create_dung_ban_ghi()
    {
        using var factory = new TestAppFactory();
        var (client, quanLy) = await SignInAsManagerAsync(factory);
        var hanhKhach = await SeedUserAsync(factory, RoleIds.Passenger, RoleCodes.Passenger, phoneNumber: "0911111111");
        var phanAnh = await SeedFeedbackAsync(factory, hanhKhach.Id);

        var response = await client.PostAsJsonAsync(
            $"{AdminFeedbacksUrl}/{phanAnh.Id}/replies", new { content = "Nhà xe xin lỗi vì sự cố." });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // ⚠️ GHI NHẬN — Target là id PHẢN ÁNH dù dòng vừa sinh nằm ở bảng FeedbackReplies:
        // middleware suy tên bảng từ đoạn đường dẫn trước {id} ("feedbacks") và ưu tiên id đọc từ
        // body trả về cho POST — body ở đây là phản ánh đầy đủ nên id đó chính là id phản ánh.
        await AssertMotBanGhiAsync(factory, AuditAction.Create, $"Feedbacks:{phanAnh.Id}", quanLy);
    }

    // ------------------------------------------------------------------------------- Sơ đồ ghế

    [Fact]
    public async Task Post_seat_layouts_ghi_Create_dung_ban_ghi()
    {
        using var factory = new TestAppFactory();
        var (client, admin) = await SignInAsAdminAsync(factory);

        var response = await client.PostAsJsonAsync(SeatLayoutsUrl, SeatLayoutBody("Xe buýt 45 chỗ"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        await AssertMotBanGhiAsync(factory, AuditAction.Create, $"SeatLayouts:{await IdOfAsync(response)}", admin);
    }

    [Fact]
    public async Task Put_seat_layouts_ghi_Update_dung_ban_ghi()
    {
        using var factory = new TestAppFactory();
        var (client, admin) = await SignInAsAdminAsync(factory);
        var layout = await SeedSeatLayoutAsync(factory, "Xe buýt 45 chỗ");

        var response = await client.PutAsJsonAsync($"{SeatLayoutsUrl}/{layout.Id}", SeatLayoutUpdateBody("1-1-1"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await AssertMotBanGhiAsync(factory, AuditAction.Update, $"SeatLayouts:{layout.Id}", admin);
    }

    [Fact]
    public async Task Delete_seat_layouts_ghi_Delete_dung_ban_ghi()
    {
        using var factory = new TestAppFactory();
        var (client, admin) = await SignInAsAdminAsync(factory);
        var layout = await SeedSeatLayoutAsync(factory, "Xe buýt 45 chỗ");

        var response = await client.DeleteAsync($"{SeatLayoutsUrl}/{layout.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        await AssertMotBanGhiAsync(factory, AuditAction.Delete, $"SeatLayouts:{layout.Id}", admin);
    }

    // ------------------------------------------------- Mẫu thông báo (story 10, dòng 24)

    // Story 10, Dương Thị Hạnh: quản trị viên soạn/sửa mẫu thông báo. PUT đi qua AuditLogMiddleware
    // (Update); policy ManagerOrAbove nên đăng nhập bằng tài khoản quản lý, không phải hành khách.
    [Fact]
    public async Task Put_notification_templates_ghi_Update_dung_ban_ghi()
    {
        using var factory = new TestAppFactory();
        var (client, quanLy) = await SignInAsManagerAsync(factory);

        var response = await client.PutAsJsonAsync(
            "/api/notification-templates/CancellationApproved",
            new { title = "Đã duyệt yêu cầu huỷ vé", body = "Yêu cầu huỷ vé của bạn đã được duyệt.", isActive = true });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // ⚠️ GHI NHẬN — Target là "NotificationTemplates" KHÔNG kèm id: khoá trên đường dẫn là {type}
        // chứ không phải {id}, nên AuditLogMiddleware suy được tên bảng nhưng không đọc được id (đã
        // ghi chú ở NotificationTemplatesController và docs/api-contract.md). Vẫn đủ để biết "có người
        // sửa mẫu lúc nào".
        await AssertMotBanGhiAsync(factory, AuditAction.Update, "NotificationTemplates", quanLy);
    }

    // --------------------------------------- Xuất báo cáo bất đồng bộ (story 24, dòng 64)

    // Story 24, Vàng Thị Dăm: đặt job xuất báo cáo lớn ở nền. Việc đặt job CÓ đổi dữ liệu (sinh
    // một dòng ExportJobs) nên đi qua AuditLogMiddleware như mọi POST khác. Policy ManagerOrAbove
    // nên đăng nhập bằng tài khoản quản lý, không phải hành khách.
    [Fact]
    public async Task Post_exports_ghi_Create_dung_ban_ghi()
    {
        using var factory = new TestAppFactory();
        var (client, quanLy) = await SignInAsManagerAsync(factory);

        var response = await client.PostAsJsonAsync(
            "/api/exports", new { reportType = "revenue", format = "xlsx" });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        // ⚠️ GHI NHẬN — Target ghi "Exports:<id>", KHÔNG phải "ExportJobs:<id>": middleware suy tên
        // bảng từ đoạn tĩnh cuối trên đường dẫn (api/exports) chứ không đọc entity. Cùng nhóm giới
        // hạn với "Drivers" (bảng thật là Users). Id vẫn tra đúng dòng ExportJobs.
        await AssertMotBanGhiAsync(factory, AuditAction.Create, $"Exports:{await IdOfAsync(response)}", quanLy);
    }

    // ------------------------------------------------------------------------------------ Auth

    [Fact]
    public async Task Post_auth_login_ghi_Login_bang_chinh_controller()
    {
        using var factory = new TestAppFactory();
        var user = await SeedUserAsync(
            factory, RoleIds.Passenger, RoleCodes.Passenger, phoneNumber: SoDienThoai);

        var response = await factory.CreateClient()
            .PostAsJsonAsync(LoginUrl, new { phoneNumber = SoDienThoai, password = MatKhau });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await AssertMotBanGhiAsync(factory, AuditAction.Login, target: null, user);
    }

    [Fact]
    public async Task Post_auth_logout_ghi_Logout_bang_chinh_controller()
    {
        using var factory = new TestAppFactory();
        var user = await SeedUserAsync(
            factory, RoleIds.Passenger, RoleCodes.Passenger, phoneNumber: SoDienThoai);
        var refreshToken = await RefreshTokenAsync(factory);

        var response = await factory.CreateClient()
            .PostAsJsonAsync(LogoutUrl, new { refreshToken });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // Hai bản ghi: Login của lần đăng nhập lấy token, và Logout. Không có bản ghi thứ ba.
        Assert.Equal(2, (await LogsAsync(factory)).Count);

        var log = Assert.Single(await LogsAsync(factory), item => item.Action == AuditAction.Logout);
        Assert.Equal(user.Id, log.UserId);
        Assert.Null(log.Target);
    }

    [Fact]
    public async Task Post_auth_refresh_token_khong_ghi_nhat_ky()
    {
        using var factory = new TestAppFactory();
        await SeedUserAsync(factory, RoleIds.Passenger, RoleCodes.Passenger, phoneNumber: SoDienThoai);
        var refreshToken = await RefreshTokenAsync(factory);

        var response = await factory.CreateClient()
            .PostAsJsonAsync(RefreshUrl, new { refreshToken });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Chỉ có bản ghi Login. Làm mới token không đổi dữ liệu và xảy ra vài phút một lần.
        var log = Assert.Single(await LogsAsync(factory));
        Assert.Equal(AuditAction.Login, log.Action);
    }

    [Fact]
    public async Task Post_auth_register_khong_ghi_nhat_ky()
    {
        using var factory = new TestAppFactory();
        await EnsureAllRolesAsync(factory);

        var response = await factory.CreateClient().PostAsJsonAsync(RegisterUrl, new
        {
            fullName = "Hành Khách Mới",
            email = "hanhkhach@example.com",
            phoneNumber = SoDienThoai,
            password = MatKhau,
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        // Đánh đổi đã biết: đăng ký nằm dưới /api/auth nên middleware bỏ qua, mà Register thì
        // không tự gọi IAuditLogService. Tạo tài khoản qua /api/admin/users thì VẪN được ghi.
        Assert.Empty(await LogsAsync(factory));
    }

    // =======================================================================================
    // PHẦN 3 — Tính chất chung của cả bề mặt: không sót, không ghi hai lần
    // =======================================================================================

    [Fact]
    public async Task Moi_thao_tac_thanh_cong_ghi_dung_MOT_ban_ghi_khong_nhan_doi()
    {
        using var factory = new TestAppFactory();
        var (client, _) = await SignInAsAdminAsync(factory);

        var route = await SeedRouteAsync(factory, "01");
        var stop = await SeedStopAsync(factory, "Trạm Cầu Giấy");
        var fare = await SeedFareAsync(factory, route.Id, PassengerType.Standard, 7000m);
        var row = await SeedRouteStopAsync(factory, route.Id, stop.Id, stopOrder: 1);
        var user = await SeedUserAsync(factory, RoleIds.Passenger, RoleCodes.Passenger, phoneNumber: "0911111111");

        // Mỗi endpoint thay đổi dữ liệu đúng MỘT lần, cộng lại thành 7 thao tác thành công.
        var thaoTac = new (string Ten, HttpResponseMessage Response)[]
        {
            ("POST /routes", await client.PostAsJsonAsync(RoutesUrl, RouteBody("02"))),
            ("PUT /routes/{id}", await client.PutAsJsonAsync($"{RoutesUrl}/{route.Id}", RouteBody("01", ten: "Tuyến 01"))),
            ("POST /routes/{id}/fares", await client.PostAsJsonAsync(FaresUrl(route.Id), new { passengerType = "Student", price = 3500m })),
            ("PUT /routes/{id}/fares/{id}", await client.PutAsJsonAsync($"{FaresUrl(route.Id)}/{fare.Id}", new { price = 8000m })),
            ("PUT /routes/{id}/stops/order", await client.PutAsJsonAsync($"{RouteStopsUrl(route.Id)}/order", new { items = new[] { new { stopId = stop.Id } } })),
            ("DELETE /routes/{id}/stops/{id}", await client.DeleteAsync($"{RouteStopsUrl(route.Id)}/{row.Id}")),
            ("PATCH /admin/users/{id}/status", await client.PatchAsJsonAsync($"{UsersUrl}/{user.Id}/status", new { isActive = false })),
        };

        Assert.All(thaoTac, item => Assert.True(
            (int)item.Response.StatusCode is >= 200 and < 300,
            $"{item.Ten} phải thành công nhưng trả {(int)item.Response.StatusCode}"));

        // Đúng 7 bản ghi cho 7 thao tác. Con số này bắt được cả hai kiểu hỏng: middleware bỏ sót
        // một endpoint (ra 6), hoặc ghi hai lần cho cùng một request (ra 8).
        var logs = await LogsAsync(factory);

        Assert.Equal(thaoTac.Length, logs.Count);
        Assert.All(logs, log => Assert.NotNull(log.UserId));
    }

    [Fact]
    public async Task Khong_endpoint_chi_doc_nao_ghi_nhat_ky()
    {
        using var factory = new TestAppFactory();
        var (client, _) = await SignInAsAdminAsync(factory);
        var route = await SeedRouteAsync(factory, "01");
        var stop = await SeedStopAsync(factory, "Trạm Cầu Giấy");
        await SeedFareAsync(factory, route.Id, PassengerType.Standard, 7000m);
        await SeedRouteStopAsync(factory, route.Id, stop.Id, stopOrder: 1);

        // Toàn bộ endpoint GET của Sprint 1.
        var duongDan = new[]
        {
            RoutesUrl,
            $"{RoutesUrl}/{route.Id}",
            StopsUrl,
            $"{StopsUrl}/{stop.Id}",
            FaresUrl(route.Id),
            $"{FaresUrl(route.Id)}/{Guid.Empty}",
            RouteStopsUrl(route.Id),
            UsersUrl,
            $"{UsersUrl}/{Guid.Empty}",
            AuditLogsUrl,
            $"{AuditLogsUrl}/export",
        };

        foreach (var path in duongDan)
        {
            await client.GetAsync(path);
        }

        // Bảng AuditLogs chỉ ghi thêm, không dọn được — ghi cho GET thì bảng phình theo lưu lượng
        // đọc và chôn mất thao tác thật.
        Assert.Empty(await LogsAsync(factory));
    }

    // =======================================================================================
    // Helper — mỗi lớp test tự chép, không có lớp base chung (đúng lệ đang có của dự án)
    // =======================================================================================

    /// <summary>
    /// Khẳng định ĐÚNG MỘT bản ghi nhật ký, đúng hành động, đúng người thực hiện — và nếu có
    /// truyền <paramref name="target"/> thì đúng cả đối tượng. Trả về bản ghi để test gọi thêm
    /// khẳng định riêng khi chưa chốt được giá trị Target.
    ///
    /// Dùng chung cho mọi endpoint ở Phần 2: cùng một khẳng định lặp 21 lần là chủ ý — mỗi
    /// endpoint phải tự chứng minh nó có ghi, không endpoint nào được "thừa hưởng" từ endpoint
    /// khác cùng controller.
    /// </summary>
    private static async Task<AuditLog> AssertMotBanGhiAsync(
        TestAppFactory factory,
        AuditAction hanhDong,
        string? target,
        UserEntity nguoiThucHien)
    {
        var log = Assert.Single(await LogsAsync(factory));

        Assert.Equal(hanhDong, log.Action);
        Assert.Equal(nguoiThucHien.Id, log.UserId);

        if (target is not null)
        {
            Assert.Equal(target, log.Target);
        }

        return log;
    }

    private static async Task<Guid> IdOfAsync(HttpResponseMessage response)
        => (await BodyAsync(response)).GetProperty("id").GetGuid();

    private static async Task<List<AuditLog>> LogsAsync(TestAppFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Sắp xếp tiếp theo Id vì nhiều bản ghi sinh trong cùng một mili giây nên CreatedAt bằng
        // nhau — chỉ OrderBy(CreatedAt) là thứ tự không xác định.
        return await db.AuditLogs
            .AsNoTracking()
            .OrderBy(log => log.CreatedAt)
            .ThenBy(log => log.Id)
            .ToListAsync();
    }

    private static string FaresUrl(Guid routeId) => $"{RoutesUrl}/{routeId}/fares";

    private static string RouteStopsUrl(Guid routeId) => $"{RoutesUrl}/{routeId}/stops";

    private static string RouteTripsUrl(Guid routeId) => $"{RoutesUrl}/{routeId}/trips";

    private static object RouteBody(string code, string? ten = null)
        => new
        {
            code,
            name = ten ?? $"Tuyến {code}",
            origin = "Bến Thành",
            destination = "Chợ Lớn",
            distanceKm = 12.5m,
        };

    private static object StopBody(string name)
        => new { name, address = $"Địa chỉ {name}", latitude = 21.0307, longitude = 105.8034 };

    private static object SeatLayoutBody(string busType)
        => new { busType, numberOfFloors = 1, rowsPerFloor = 2, columnsPerRow = 2, vipSeatPositions = Array.Empty<string>() };

    // PUT cố ý KHÔNG có busType — loại xe là khoá nghiệp vụ, đổi khoá là xoá-rồi-tạo
    // (UpdateSeatLayoutRequest của docs/api-contract.md).
    private static object SeatLayoutUpdateBody(params string[] vipSeatPositions)
        => new { numberOfFloors = 1, rowsPerFloor = 2, columnsPerRow = 2, vipSeatPositions };

    private static async Task<string> RefreshTokenAsync(TestAppFactory factory)
    {
        var response = await factory.CreateClient()
            .PostAsJsonAsync(LoginUrl, new { phoneNumber = SoDienThoai, password = MatKhau });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await BodyAsync(response)).GetProperty("refreshToken").GetString()!;
    }

    private static async Task<JsonElement> BodyAsync(HttpResponseMessage response)
        => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static HttpClient ClientWith(TestAppFactory factory, string accessToken)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        return client;
    }

    private static async Task<(HttpClient Client, UserEntity User)> SignInAsAdminAsync(TestAppFactory factory)
    {
        await EnsureAllRolesAsync(factory);
        var admin = await SeedUserAsync(factory, RoleIds.Admin, RoleCodes.Admin, fullName: "Quản Trị Viên");

        // Token phát thẳng bằng ITokenService, KHÔNG qua /api/auth/login — để nền nhật ký sạch,
        // không lẫn bản ghi Login vào phép đếm của từng ca.
        return (ClientWith(factory, factory.CreateTokenFor(admin)), admin);
    }

    /// <summary>
    /// Quản lý đăng nhập — cần cho story 24: endpoint xử lý phản ánh đòi policy ManagerOrAbove.
    /// Token phát thẳng bằng ITokenService để nền nhật ký sạch, cùng lý do SignInAsAdminAsync.
    /// </summary>
    private static async Task<(HttpClient Client, UserEntity User)> SignInAsManagerAsync(TestAppFactory factory)
    {
        await EnsureAllRolesAsync(factory);
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, fullName: "Quản Lý Vận Hành");

        return (ClientWith(factory, factory.CreateTokenFor(manager)), manager);
    }

    /// <summary>Bốn vai trò migration seed sẵn — InMemory KHÔNG chạy <c>HasData</c>.</summary>
    private static readonly (Guid Id, string Code)[] CanonicalRoles =
    [
        (RoleIds.Admin, RoleCodes.Admin),
        (RoleIds.Manager, RoleCodes.Manager),
        (RoleIds.Driver, RoleCodes.Driver),
        (RoleIds.Passenger, RoleCodes.Passenger),
    ];

    private static async Task EnsureAllRolesAsync(TestAppFactory factory)
    {
        await factory.SeedAsync(db =>
        {
            foreach (var (id, code) in CanonicalRoles)
            {
                if (!db.Roles.Any(r => r.Id == id || r.Code == code))
                {
                    db.Roles.Add(new Role { Id = id, Code = code, Name = code });
                }
            }
        });
    }

    private static async Task<UserEntity> SeedUserAsync(
        TestAppFactory factory,
        Guid roleId,
        string roleCode,
        string fullName = "Người Dùng Test",
        string? phoneNumber = null)
    {
        await factory.SeedAsync(db =>
        {
            if (!db.Roles.Any(r => r.Id == roleId || r.Code == roleCode))
            {
                db.Roles.Add(new Role { Id = roleId, Code = roleCode, Name = roleCode });
            }
        });

        var user = new UserEntity
        {
            Id = Guid.NewGuid(),
            PhoneNumber = phoneNumber ?? "09" + Random.Shared.Next(10_000_000, 99_999_999),
            FullName = fullName,
            PasswordHash = PasswordService.HashPassword(MatKhau),
            IsActive = true,
            RoleId = roleId,
            UserRoles = [new UserRole { RoleId = roleId }],
        };

        await factory.SeedAsync(db => db.Users.Add(user));

        return user;
    }

    private static async Task<RouteEntity> SeedRouteAsync(TestAppFactory factory, string code)
    {
        var route = new RouteEntity
        {
            Id = Guid.NewGuid(),
            Code = code,
            Name = $"Tuyến {code}",
            Origin = "Bến Thành",
            Destination = "Chợ Lớn",
        };

        await factory.SeedAsync(db => db.Routes.Add(route));

        return route;
    }

    private static async Task<Bus> SeedBusAsync(TestAppFactory factory, string licensePlate)
    {
        var bus = new Bus
        {
            Id = Guid.NewGuid(),
            LicensePlate = licensePlate,
            BusType = "Xe buýt 45 chỗ",
            Capacity = 45,
        };

        await factory.SeedAsync(db => db.Buses.Add(bus));

        return bus;
    }

    /// <summary>Chuyến Scheduled 01:00–01:45 UTC ngày 01/10/2026 — đủ để PATCH phân công.</summary>
    private static async Task<Trip> SeedTripAsync(TestAppFactory factory, Guid routeId, Guid busId)
    {
        var trip = new Trip
        {
            Id = Guid.NewGuid(),
            RouteId = routeId,
            BusId = busId,
            DepartureTime = new DateTime(2026, 10, 1, 1, 0, 0, DateTimeKind.Utc),
            ArrivalTime = new DateTime(2026, 10, 1, 1, 45, 0, DateTimeKind.Utc),
        };

        await factory.SeedAsync(db => db.Trips.Add(trip));

        return trip;
    }

    private static async Task<Stop> SeedStopAsync(TestAppFactory factory, string name)
    {
        var stop = new Stop
        {
            Id = Guid.NewGuid(),
            Name = name,
            Address = $"Địa chỉ {name}",
            Latitude = 21.0307,
            Longitude = 105.8034,
        };

        await factory.SeedAsync(db => db.Stops.Add(stop));

        return stop;
    }

    private static async Task<SeatLayout> SeedSeatLayoutAsync(TestAppFactory factory, string busType)
    {
        var layout = new SeatLayout
        {
            Id = Guid.NewGuid(),
            BusType = busType,
            NumberOfFloors = 1,
            RowsPerFloor = 2,
            ColumnsPerRow = 2,
            TotalSeats = 4,
            VipSeatPositions = string.Empty,
            CreatedAt = DateTime.UtcNow,
        };

        await factory.SeedAsync(db => db.SeatLayouts.Add(layout));

        return layout;
    }

    private static async Task<RouteStop> SeedRouteStopAsync(
        TestAppFactory factory,
        Guid routeId,
        Guid stopId,
        int stopOrder,
        decimal distanceKm = 0m)
    {
        // Chỉ gán khoá ngoại, KHÔNG gán navigation: Route/Stop được seed ở scope khác, gán
        // navigation vào đây sẽ khiến EF tưởng chúng là bản ghi mới và chèn trùng khoá chính.
        var row = new RouteStop
        {
            Id = Guid.NewGuid(),
            RouteId = routeId,
            StopId = stopId,
            StopOrder = stopOrder,
            DistanceKm = distanceKm,
        };

        await factory.SeedAsync(db => db.RouteStops.Add(row));

        return row;
    }

    private static async Task<Fare> SeedFareAsync(
        TestAppFactory factory,
        Guid routeId,
        PassengerType passengerType,
        decimal price)
    {
        var fare = new Fare
        {
            Id = Guid.NewGuid(),
            RouteId = routeId,
            PassengerType = passengerType,
            Price = price,
        };

        await factory.SeedAsync(db => db.Fares.Add(fare));

        return fare;
    }

    private static async Task<PassType> SeedPassTypeAsync(
        TestAppFactory factory,
        string code,
        int durationMonths,
        decimal price)
    {
        var passType = new PassType
        {
            Id = Guid.NewGuid(),
            Code = code,
            Name = $"Vé {code}",
            DurationMonths = durationMonths,
            Price = price,
        };

        await factory.SeedAsync(db => db.PassTypes.Add(passType));

        return passType;
    }

    /// <summary>Vé tháng còn hạn quanh lúc chạy test — đủ làm mốc nối để sinh vé kỳ sau.</summary>
    private static async Task<MonthlyPass> SeedMonthlyPassAsync(
        TestAppFactory factory,
        Guid userId,
        Guid routeId,
        Guid passTypeId)
    {
        var pass = new MonthlyPass
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            RouteId = routeId,
            PassTypeId = passTypeId,
            Price = 200_000m,
            ValidFrom = DateTime.UtcNow.AddDays(-10),
            ValidTo = DateTime.UtcNow.AddDays(10),
            Code = $"MP-01-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}",
        };

        await factory.SeedAsync(db => db.MonthlyPasses.Add(pass));

        return pass;
    }

    /// <summary>
    /// Phản ánh mới ở trạng thái New — đủ để PATCH trạng thái và ghi phản hồi (story 24). API không
    /// join bảng Trips nên chuyến để trống; tripId null là hợp lệ theo A9 #20.
    /// </summary>
    private static async Task<Feedback> SeedFeedbackAsync(TestAppFactory factory, Guid userId)
    {
        var feedback = new Feedback
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Type = FeedbackType.Complaint,
            Content = "Xe chạy trễ 30 phút so với giờ trên vé.",
        };

        await factory.SeedAsync(db => db.Feedbacks.Add(feedback));

        return feedback;
    }

    /// <summary>
    /// Chuyến chỉ có khoá ngoại, không gán navigation — InMemory không cưỡng chế khoá ngoại nên
    /// không cần seed Route/Bus cho ca này (đúng lệ "seed dòng mồ côi" của TripDetailApiTests).
    /// </summary>
    private static async Task<Trip> SeedTripAsync(TestAppFactory factory)
    {
        var trip = new Trip
        {
            Id = Guid.NewGuid(),
            RouteId = Guid.NewGuid(),
            BusId = Guid.NewGuid(),
            DepartureTime = new DateTime(2026, 10, 1, 1, 0, 0, DateTimeKind.Utc),
        };

        await factory.SeedAsync(db => db.Trips.Add(trip));

        return trip;
    }

    private static async Task<Seat> SeedSeatAsync(TestAppFactory factory)
    {
        var seat = new Seat
        {
            Id = Guid.NewGuid(),
            BusId = Guid.NewGuid(),
            SeatLayoutId = Guid.NewGuid(),
            Floor = 1,
            RowIndex = 1,
            ColumnIndex = 1,
            SeatNumber = "A1",
        };

        await factory.SeedAsync(db => db.Seats.Add(seat));

        return seat;
    }

    /// <summary>Lượt giữ chỗ đang Holding — đủ để endpoint gia hạn chạy thành công (story 3).</summary>
    private static async Task SeedHoldAsync(
        TestAppFactory factory,
        Guid userId,
        Guid tripId,
        Guid seatId,
        string sessionCode)
    {
        var hold = new SeatHold
        {
            Id = Guid.NewGuid(),
            TripId = tripId,
            SeatId = seatId,
            UserId = userId,
            SessionCode = sessionCode,
            Status = SeatHoldStatus.Holding,
            ExpiresAt = new DateTime(2026, 10, 1, 1, 10, 0, DateTimeKind.Utc),
        };

        await factory.SeedAsync(db => db.SeatHolds.Add(hold));
    }

    private const string LoginUrl = "/api/auth/login";

    private const string LogoutUrl = "/api/auth/logout";

    private const string RefreshUrl = "/api/auth/refresh-token";

    private const string RegisterUrl = "/api/auth/register";

    private const string RoutesUrl = "/api/routes";

    private const string StopsUrl = "/api/stops";

    private const string SeatLayoutsUrl = "/api/seat-layouts";

    private const string UsersUrl = "/api/admin/users";

    private const string DriversUrl = "/api/drivers";

    private const string TripsUrl = "/api/trips";

    private const string MonthlyPassesUrl = "/api/monthly-passes";

    private const string AdminFeedbacksUrl = "/api/admin/feedbacks";

    private const string AuditLogsUrl = "/api/audit-logs";

    private const string MatKhau = "matkhau123";

    private const string SoDienThoai = "0912345678";
}
