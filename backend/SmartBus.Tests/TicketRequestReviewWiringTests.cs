using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartBus.Api.Data;
using SmartBus.Api.Entities;
using SmartBus.Api.Services;
using UserEntity = SmartBus.Api.Entities.User;

namespace SmartBus.Tests;

/// <summary>
/// Nối dây hai endpoint duyệt / từ chối yêu cầu huỷ - đổi vé (US 5 — dòng 5 Sprint 4, Phùng Duy
/// Hoàng): <c>PATCH /api/cancellation-requests/{id}</c> và <c>PATCH /api/ticket-changes/{id}</c>.
/// Hợp đồng: docs/api-contract.md, mục "Duyệt / từ chối yêu cầu HUỶ" và "…ĐỔI".
///
/// Chạy trên app THẬT (<see cref="TestAppFactory"/>) nên ghim được bốn thứ mà chỉ app thật mới lộ:
///
/// 1. <b>Đăng ký DI</b> — <c>ITicketRequestReviewService</c> phân giải ra
///    <see cref="TicketRequestReviewService"/>. Dựng được nó cũng có nghĩa <c>ITicketQrService</c>,
///    <c>ITicketRequestResultNotifier</c> và <c>AppDbContext</c> đều phân giải được: cả ba nằm trong
///    constructor, thiếu một cái là lượt dựng ném ngay.
/// 2. <b>Route + chính sách quyền</b> — không token → 401, Passenger/Driver → 403, Manager/Admin đi
///    qua được tới tận service (404 trên một Guid ngẫu nhiên, không phải 404 của "không có route").
/// 3. <b>Hai lượt chạy trọn đường</b> — duyệt huỷ và duyệt đổi. Đây là chỗ chứng minh các mối nối
///    thật: service ghi vé, lượt đổi PHÁT HÀNH vé mới có mã QR ký được, và notifier được gọi SAU khi
///    lưu nên khách có dòng thông báo.
/// 4. <b>Hình dạng response dùng chung</b> — trường của loại kia vẫn CÓ MẶT và null, không bị lược
///    khỏi JSON (đọc bằng <c>GetProperty</c> nên thiếu trường là đỏ, không phải so sánh null lặng lẽ).
///
/// Ma trận đầy đủ của các nhánh 409 — vé đã soát ở cổng, chuyến xin đổi sang đã huỷ, ghế bị người
/// khác mua mất trong lúc chờ duyệt — là việc của dòng 12 (Giàng A Vàng); ở đây chỉ giữ đúng những
/// nhánh chứng minh nối dây còn sống.
///
/// Khoá ký QR giả của test, không phải khoá thật (luật 2).
/// </summary>
public class TicketRequestReviewWiringTests
{
    private const string QrSigningKey = "khoa-ky-gia-cua-test-dai-hon-32-ky-tu-abc";

    private const string CancellationUrl = "/api/cancellation-requests/";

    private const string ChangeUrl = "/api/ticket-changes/";

    private static readonly DateTime Moc1 = new(2026, 10, 1, 1, 0, 0, DateTimeKind.Utc);

    // ===========================================================================================
    // Nối dây
    // ===========================================================================================

    [Fact]
    public void App_that_resolve_duoc_ITicketRequestReviewService()
    {
        using var factory = NewFactory();

        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ITicketRequestReviewService>();

        Assert.IsType<TicketRequestReviewService>(service);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Khong_gui_token_thi_tra_401(bool laYeuCauHuy)
    {
        using var factory = NewFactory();
        using var client = factory.CreateClient();

        var response = await client.PatchAsJsonAsync(
            Url(laYeuCauHuy, Guid.NewGuid()), new { status = "Approved" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(RoleCodes.Passenger, true)]
    [InlineData(RoleCodes.Passenger, false)]
    [InlineData(RoleCodes.Driver, true)]
    [InlineData(RoleCodes.Driver, false)]
    public async Task Hanh_khach_va_tai_xe_goi_thi_tra_403(string roleCode, bool laYeuCauHuy)
    {
        using var factory = NewFactory();

        var user = await SeedUserAsync(factory, RoleIdsFor(roleCode), roleCode, "Người Không Được Duyệt");
        using var client = AuthClientFor(factory, user);

        var response = await client.PatchAsJsonAsync(
            Url(laYeuCauHuy, Guid.NewGuid()), new { status = "Approved" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("Bạn không có quyền truy cập tính năng này.", MessageOf(await ReadJsonAsync(response)));
    }

    /// <summary>
    /// Manager/Admin đi qua được tới TẬN service: Guid ngẫu nhiên không có dòng nào nên service trả
    /// NotFound, controller dịch thành 404 kèm đúng câu của mình. Câu này là thứ phân biệt "route của
    /// mình đang chạy" với "route không tồn tại" — cả hai đều 404, nhưng 404 của route sai không có
    /// thân JSON mang câu này.
    /// </summary>
    [Theory]
    [InlineData(RoleCodes.Manager, true)]
    [InlineData(RoleCodes.Manager, false)]
    [InlineData(RoleCodes.Admin, true)]
    [InlineData(RoleCodes.Admin, false)]
    public async Task Quan_ly_va_admin_di_qua_duoc_toi_service(string roleCode, bool laYeuCauHuy)
    {
        using var factory = NewFactory();

        var user = await SeedUserAsync(factory, RoleIdsFor(roleCode), roleCode, "Quản Lý Test");
        using var client = AuthClientFor(factory, user);

        var response = await client.PatchAsJsonAsync(
            Url(laYeuCauHuy, Guid.NewGuid()), new { status = "Approved" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(
            laYeuCauHuy
                ? "Không tìm thấy yêu cầu huỷ vé."
                : "Không tìm thấy yêu cầu đổi vé.",
            MessageOf(await ReadJsonAsync(response)));
    }

    /// <summary>
    /// <c>"Pending"</c> là trạng thái KHÁCH gửi, không phải một quyết định — nhận nó là mở đường cho
    /// một lượt "duyệt" ghi ngược yêu cầu về hàng chờ. 400 và KHÔNG ghi gì cả.
    /// </summary>
    [Fact]
    public async Task Status_Pending_thi_tra_400_va_khong_ghi_gi()
    {
        using var factory = NewFactory();

        var passenger = await SeedUserAsync(factory, RoleIds.Passenger, RoleCodes.Passenger, "Khách Test");
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");

        var ve = NewPaidTicket(passenger.Id);
        var yeuCau = NewPendingCancellation(ve, passenger.Id);

        await SeedAsync(factory, db =>
        {
            db.Tickets.Add(ve);
            db.CancellationRequests.Add(yeuCau);
        });

        using var client = AuthClientFor(factory, manager);

        var response = await client.PatchAsJsonAsync(
            $"{CancellationUrl}{yeuCau.Id}", new { status = "Pending" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await ReadJsonAsync(response);
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("errors").GetProperty("status")[0].GetString()));

        // Không nhánh nào của lượt này được chạm vào dòng yêu cầu.
        var yeuCauSau = await QueryAsync(factory, db =>
            db.CancellationRequests.AsNoTracking().SingleAsync(c => c.Id == yeuCau.Id));

        Assert.Equal(CancellationRequestStatus.Pending, yeuCauSau.Status);
        Assert.Null(yeuCauSau.ReviewedByUserId);
        Assert.Null(yeuCauSau.ReviewedAt);
    }

    // ===========================================================================================
    // Hai lượt chạy trọn đường — chứng minh các mối nối
    // ===========================================================================================

    [Fact]
    public async Task Duyet_yeu_cau_huy_thi_huy_ve_va_bao_cho_khach()
    {
        using var factory = NewFactory();

        var passenger = await SeedUserAsync(factory, RoleIds.Passenger, RoleCodes.Passenger, "Khách Xin Huỷ");
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Duyệt");

        var ve = NewPaidTicket(passenger.Id);
        var yeuCau = NewPendingCancellation(ve, passenger.Id);

        await SeedAsync(factory, db =>
        {
            db.Tickets.Add(ve);
            db.CancellationRequests.Add(yeuCau);
        });

        using var client = AuthClientFor(factory, manager);

        var response = await client.PatchAsJsonAsync(
            $"{CancellationUrl}{yeuCau.Id}", new { status = "Approved" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await ReadJsonAsync(response);

        Assert.Equal("Cancel", body.GetProperty("type").GetString());
        Assert.Equal("Approved", body.GetProperty("status").GetString());
        Assert.Equal(ve.Code, body.GetProperty("ticketCode").GetString());
        Assert.Equal(250_000m, body.GetProperty("price").GetDecimal());
        Assert.Equal(25_000m, body.GetProperty("cancellationFee").GetDecimal());
        Assert.Equal(225_000m, body.GetProperty("refundAmount").GetDecimal());
        Assert.Equal(manager.Id, body.GetProperty("reviewedByUserId").GetGuid());

        // Trường của loại kia phải CÓ MẶT và null — hợp đồng cấm lược bỏ khỏi JSON (GetProperty ném
        // nếu thiếu, nên đây là phép ghim thật, không phải so null lặng lẽ).
        Assert.Equal(JsonValueKind.Null, body.GetProperty("newTicketId").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("newTripId").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("newSeatId").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("newPrice").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("priceDifference").ValueKind);

        // Đọc bằng scope mới: lượt PATCH ghi ở scope khác.
        var veSau = await QueryAsync(factory, db =>
            db.Tickets.AsNoTracking().SingleAsync(t => t.Id == ve.Id));

        Assert.Equal(TicketStatus.Cancelled, veSau.Status);
        Assert.NotNull(veSau.UpdatedAt);

        var yeuCauSau = await QueryAsync(factory, db =>
            db.CancellationRequests.AsNoTracking().SingleAsync(c => c.Id == yeuCau.Id));

        Assert.Equal(CancellationRequestStatus.Approved, yeuCauSau.Status);
        Assert.Equal(manager.Id, yeuCauSau.ReviewedByUserId);
        Assert.NotNull(yeuCauSau.ReviewedAt);

        // Mối nối sang notifier của Dăm (dòng 7): khách phải có đúng một dòng thông báo trỏ về yêu cầu.
        var thongBao = await QueryAsync(factory, db =>
            db.Notifications.AsNoTracking().Where(n => n.UserId == passenger.Id).ToListAsync());

        Assert.Equal(yeuCau.Id, Assert.Single(thongBao).RelatedEntityId);
    }

    [Fact]
    public async Task Tu_choi_yeu_cau_huy_thi_giu_nguyen_ve()
    {
        using var factory = NewFactory();

        var passenger = await SeedUserAsync(factory, RoleIds.Passenger, RoleCodes.Passenger, "Khách Bị Từ Chối");
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Từ Chối");

        var ve = NewPaidTicket(passenger.Id);
        var yeuCau = NewPendingCancellation(ve, passenger.Id);

        await SeedAsync(factory, db =>
        {
            db.Tickets.Add(ve);
            db.CancellationRequests.Add(yeuCau);
        });

        using var client = AuthClientFor(factory, manager);

        var response = await client.PatchAsJsonAsync(
            $"{CancellationUrl}{yeuCau.Id}",
            new { status = "Rejected", rejectReason = "  Vé đã quá sát giờ khởi hành.  " });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await ReadJsonAsync(response);

        Assert.Equal("Rejected", body.GetProperty("status").GetString());

        // Cắt khoảng trắng hai đầu — thông báo chép nguyên văn lý do này cho khách.
        Assert.Equal("Vé đã quá sát giờ khởi hành.", body.GetProperty("rejectReason").GetString());

        // Từ chối thì không khoản nào di chuyển: trả price - fee ở đây là nói dối.
        Assert.Equal(JsonValueKind.Null, body.GetProperty("refundAmount").ValueKind);

        var veSau = await QueryAsync(factory, db =>
            db.Tickets.AsNoTracking().SingleAsync(t => t.Id == ve.Id));

        Assert.Equal(TicketStatus.Paid, veSau.Status);
        Assert.Null(veSau.UpdatedAt);
    }

    /// <summary>
    /// Lượt nặng nhất của US 5: duyệt đổi vé phải huỷ vé CŨ và phát hành một vé MỚI (không sửa vé cũ —
    /// vé cũ đã in QR và đã nằm trong hoá đơn của <c>Payments</c>). Vé mới phải có mã QR KÝ ĐƯỢC, tức
    /// là id vé phải sinh trước khi ký.
    /// </summary>
    [Fact]
    public async Task Duyet_yeu_cau_doi_thi_huy_ve_cu_va_phat_hanh_ve_moi()
    {
        using var factory = NewFactory();

        var passenger = await SeedUserAsync(factory, RoleIds.Passenger, RoleCodes.Passenger, "Khách Xin Đổi");
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Duyệt");

        var veCu = NewPaidTicket(passenger.Id);

        // Chuyến xin đổi sang phải là chuyến thật, còn chạy — service kiểm trạng thái chuyến lúc duyệt.
        var chuyenMoi = new Trip
        {
            RouteId = Guid.NewGuid(),
            BusId = Guid.NewGuid(),
            DepartureTime = Moc1.AddDays(5),
            Status = TripStatus.Scheduled,
            CreatedAt = Moc1,
        };

        var gheMoi = Guid.NewGuid();

        var yeuCau = new TicketChange
        {
            TicketId = veCu.Id,
            UserId = passenger.Id,
            NewTripId = chuyenMoi.Id,
            NewSeatId = gheMoi,
            NewPrice = 300_000m,
            Reason = "Muốn đi chuyến sớm hơn",
            Status = TicketChangeStatus.Pending,
            CreatedAt = Moc1,
        };

        await SeedAsync(factory, db =>
        {
            db.Tickets.Add(veCu);
            db.Trips.Add(chuyenMoi);
            db.TicketChanges.Add(yeuCau);
        });

        using var client = AuthClientFor(factory, manager);

        var response = await client.PatchAsJsonAsync(
            $"{ChangeUrl}{yeuCau.Id}", new { status = "Approved" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await ReadJsonAsync(response);

        Assert.Equal("Change", body.GetProperty("type").GetString());
        Assert.Equal("Approved", body.GetProperty("status").GetString());
        Assert.Equal(chuyenMoi.Id, body.GetProperty("newTripId").GetGuid());
        Assert.Equal(gheMoi, body.GetProperty("newSeatId").GetGuid());
        Assert.Equal(300_000m, body.GetProperty("newPrice").GetDecimal());

        // Có dấu: dương = khách trả thêm. Và hai trường chỉ của loại HUỶ phải null.
        Assert.Equal(50_000m, body.GetProperty("priceDifference").GetDecimal());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("cancellationFee").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("refundAmount").ValueKind);

        var veMoiId = body.GetProperty("newTicketId").GetGuid();

        var veCuSau = await QueryAsync(factory, db =>
            db.Tickets.AsNoTracking().SingleAsync(t => t.Id == veCu.Id));

        Assert.Equal(TicketStatus.Cancelled, veCuSau.Status);
        Assert.NotNull(veCuSau.UpdatedAt);

        var veMoi = await QueryAsync(factory, db =>
            db.Tickets.AsNoTracking().SingleAsync(t => t.Id == veMoiId));

        Assert.Equal(TicketStatus.Paid, veMoi.Status);
        Assert.Equal(chuyenMoi.Id, veMoi.TripId);
        Assert.Equal(gheMoi, veMoi.SeatId);
        Assert.Equal(passenger.Id, veMoi.UserId);
        Assert.Equal(300_000m, veMoi.Price);

        // Mã vé mới là mã QR thật, ký trên chính id của vé đó — không phải chuỗi rỗng hay mã của vé cũ.
        using (var scope = factory.Services.CreateScope())
        {
            var qr = scope.ServiceProvider.GetRequiredService<ITicketQrService>();

            Assert.True(qr.TryVerify(veMoi.Code, out var idTrongMa));
            Assert.Equal(veMoiId, idTrongMa);
        }

        var yeuCauSau = await QueryAsync(factory, db =>
            db.TicketChanges.AsNoTracking().SingleAsync(c => c.Id == yeuCau.Id));

        Assert.Equal(TicketChangeStatus.Approved, yeuCauSau.Status);
        Assert.Equal(veMoiId, yeuCauSau.NewTicketId);
        Assert.Equal(manager.Id, yeuCauSau.ReviewedByUserId);

        var thongBao = await QueryAsync(factory, db =>
            db.Notifications.AsNoTracking().Where(n => n.UserId == passenger.Id).ToListAsync());

        Assert.Equal(yeuCau.Id, Assert.Single(thongBao).RelatedEntityId);
    }

    // ===========================================================================================
    // Nhật ký kiểm toán — hành vi của hai dòng khai ở AuditLogCoverageTests.BangPhuSong
    //
    // Vì sao nằm ở đây chứ không ở Phần 2 của file kia: lượt duyệt ĐỔI phát hành vé mới nên phải có
    // ITicketQrService, mà TestAppFactory của file đó không đặt TicketQr:SigningKey — dựng lượt này
    // bên ấy là phải lật cả lớp sang WithWebHostBuilder, kéo theo toàn bộ helper seed/ký token của nó.
    // Bên này khoá ký đã có sẵn ở NewFactory().
    // ===========================================================================================

    [Fact]
    public async Task Duyet_huy_ve_ghi_nhat_ky_Update_voi_Target_CancellationRequests()
    {
        using var factory = NewFactory();

        var passenger = await SeedUserAsync(factory, RoleIds.Passenger, RoleCodes.Passenger, "Khách Xin Huỷ");
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Duyệt");

        var ve = NewPaidTicket(passenger.Id);
        var yeuCau = NewPendingCancellation(ve, passenger.Id);

        await SeedAsync(factory, db =>
        {
            db.Tickets.Add(ve);
            db.CancellationRequests.Add(yeuCau);
        });

        using var client = AuthClientFor(factory, manager);

        var response = await client.PatchAsJsonAsync(
            $"{CancellationUrl}{yeuCau.Id}", new { status = "Approved" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var log = await NhatKyDuyNhatAsync(factory, manager);

        // Tên bảng suy từ đoạn đứng ngay trước {id} trên đường dẫn — xem ResolveTableName của
        // AuditLogMiddleware. Đổi route của controller là đổi luôn chuỗi này.
        Assert.Equal($"CancellationRequests:{yeuCau.Id}", log.Target);
    }

    /// <summary>
    /// Lượt đổi là ca duy nhất của US 5 mà thân response mang một id MỚI (<c>newTicketId</c>). Target
    /// vẫn phải là id YÊU CẦU trên đường dẫn: PATCH không đệm response nên middleware không đọc body —
    /// và cũng không nên đọc, vì cả hai lượt đều tác động lên chính dòng yêu cầu.
    /// </summary>
    [Fact]
    public async Task Duyet_doi_ve_ghi_nhat_ky_Update_voi_Target_TicketChanges()
    {
        using var factory = NewFactory();

        var passenger = await SeedUserAsync(factory, RoleIds.Passenger, RoleCodes.Passenger, "Khách Xin Đổi");
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Duyệt");

        var veCu = NewPaidTicket(passenger.Id);

        var chuyenMoi = new Trip
        {
            RouteId = Guid.NewGuid(),
            BusId = Guid.NewGuid(),
            DepartureTime = Moc1.AddDays(5),
            Status = TripStatus.Scheduled,
            CreatedAt = Moc1,
        };

        var yeuCau = new TicketChange
        {
            TicketId = veCu.Id,
            UserId = passenger.Id,
            NewTripId = chuyenMoi.Id,
            NewSeatId = Guid.NewGuid(),
            NewPrice = 300_000m,
            Reason = "Muốn đi chuyến sớm hơn",
            Status = TicketChangeStatus.Pending,
            CreatedAt = Moc1,
        };

        await SeedAsync(factory, db =>
        {
            db.Tickets.Add(veCu);
            db.Trips.Add(chuyenMoi);
            db.TicketChanges.Add(yeuCau);
        });

        using var client = AuthClientFor(factory, manager);

        var response = await client.PatchAsJsonAsync(
            $"{ChangeUrl}{yeuCau.Id}", new { status = "Approved" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var veMoiId = (await ReadJsonAsync(response)).GetProperty("newTicketId").GetGuid();

        var log = await NhatKyDuyNhatAsync(factory, manager);

        // Phép ghim đáng giá nhất của lượt này: mai kia ai đó cho middleware đệm cả thân PATCH để đọc
        // id thì Target lặng lẽ đổi sang id vé mới, và dòng dưới đỏ ngay tại đây.
        Assert.Equal($"TicketChanges:{yeuCau.Id}", log.Target);
        Assert.DoesNotContain(veMoiId.ToString(), log.Target!);
    }

    // ===========================================================================================
    // Helper — mỗi lớp test tự chép, không có lớp base chung (đúng lệ đang có của dự án)
    // ===========================================================================================

    /// <summary>
    /// App THẬT của <see cref="TestAppFactory"/> cộng khoá ký QR của test. Phải đi qua
    /// <c>WithWebHostBuilder</c> chứ không dùng thẳng <see cref="TestAppFactory"/>: khoá QR chỉ có
    /// nghĩa lúc Program.cs dựng service (<c>TicketQrService</c> ném ngay khi thiếu), nên phải có mặt
    /// trước khi host build — cùng lý do và cùng lối của <see cref="TicketQrWiringTests"/>.
    ///
    /// Vì <c>WithWebHostBuilder</c> trả về lớp cơ sở (và <see cref="TestAppFactory"/> là <c>sealed</c>
    /// nên không kế thừa được), lớp này tự cầm lấy phép seed và phép ký token thay vì gọi
    /// <c>TestAppFactory.SeedAsync</c> / <c>CreateTokenFor</c>.
    /// </summary>
    private static WebApplicationFactory<Program> NewFactory()
        => new TestAppFactory().WithWebHostBuilder(builder =>
            builder.UseSetting("TicketQr:SigningKey", QrSigningKey));

    private static string Url(bool laYeuCauHuy, Guid id)
        => (laYeuCauHuy ? CancellationUrl : ChangeUrl) + id;

    private static Ticket NewPaidTicket(Guid passengerId) => new()
    {
        Code = "SBT1:ve-goc-" + Guid.NewGuid().ToString("N"),
        TripId = Guid.NewGuid(),
        SeatId = Guid.NewGuid(),
        UserId = passengerId,
        Price = 250_000m,
        Status = TicketStatus.Paid,
        CreatedAt = Moc1,
    };

    private static CancellationRequest NewPendingCancellation(Ticket ve, Guid passengerId) => new()
    {
        TicketId = ve.Id,
        UserId = passengerId,
        Reason = "Bận việc gia đình",
        CancellationFee = 25_000m,
        DepartureAt = Moc1.AddDays(3),
        Status = CancellationRequestStatus.Pending,
        CreatedAt = Moc1,
    };

    /// <summary>Ghi dữ liệu vào CSDL test và lưu ngay — chép nguyên thân của <c>TestAppFactory.SeedAsync</c>.</summary>
    private static async Task SeedAsync(WebApplicationFactory<Program> factory, Action<AppDbContext> seed)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        seed(db);

        await db.SaveChangesAsync();
    }

    /// <summary>Đọc dữ liệu đã ghi bằng scope mới — lượt PATCH ghi ở scope riêng của nó.</summary>
    private static async Task<T> QueryAsync<T>(
        WebApplicationFactory<Program> factory, Func<AppDbContext, Task<T>> query)
    {
        using var scope = factory.Services.CreateScope();

        return await query(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    /// <summary>Sinh access token bằng chính <see cref="ITokenService"/> của app, tránh test lệch khỏi code thật.</summary>
    private static HttpClient AuthClientFor(WebApplicationFactory<Program> factory, UserEntity user)
    {
        using var scope = factory.Services.CreateScope();
        var token = scope.ServiceProvider.GetRequiredService<ITokenService>().CreateAccessToken(user).Token;

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return client;
    }

    private static Guid RoleIdsFor(string roleCode) => roleCode switch
    {
        RoleCodes.Admin => RoleIds.Admin,
        RoleCodes.Manager => RoleIds.Manager,
        RoleCodes.Driver => RoleIds.Driver,
        _ => RoleIds.Passenger,
    };

    /// <summary>
    /// Vai trò phải có mặt trong CSDL trước khi gọi endpoint: RBAC đọc vai trò ở tầng dữ liệu, không
    /// tin claim trong token (xem <c>JwtMiddleware</c>) — token đúng mà bảng Roles rỗng vẫn 403.
    /// </summary>
    private static async Task<UserEntity> SeedUserAsync(
        WebApplicationFactory<Program> factory, Guid roleId, string roleCode, string fullName)
    {
        await SeedAsync(factory, db =>
        {
            if (!db.Roles.Any(r => r.Id == roleId || r.Code == roleCode))
            {
                db.Roles.Add(new Role { Id = roleId, Code = roleCode, Name = roleCode });
            }
        });

        var user = new UserEntity
        {
            Id = Guid.NewGuid(),
            PhoneNumber = "09" + Random.Shared.Next(10_000_000, 99_999_999),
            FullName = fullName,
            PasswordHash = PasswordService.HashPassword("matkhau123"),
            IsActive = true,
            RoleId = roleId,
            // Vai trò chính phải luôn có mặt ở bảng nối — đúng bất biến mà AuthService giữ.
            UserRoles = [new UserRole { RoleId = roleId }],
        };

        await SeedAsync(factory, db => db.Users.Add(user));

        return user;
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return doc.RootElement.Clone();
    }

    private static string MessageOf(JsonElement body)
        => body.GetProperty("message").GetString()!;

    /// <summary>
    /// Dòng nhật ký duy nhất của lượt gọi vừa rồi, kèm hai phép ghim chung cho cả hai endpoint: PATCH
    /// đi qua <c>AuditLogMiddleware</c> nên hành động luôn là <c>Update</c>, và người ghi là người bấm
    /// duyệt lấy từ token — không phải khách gửi yêu cầu.
    ///
    /// "Duy nhất" là phép kiểm thật, không phải cho gọn: token ký thẳng bằng ITokenService nên không
    /// đi qua /api/auth nên không có dòng Login nào, và service duyệt không tự gọi IAuditLogService —
    /// thêm một dòng nữa là middleware ghi hai lượt hoặc service lấn sân.
    /// </summary>
    private static async Task<AuditLog> NhatKyDuyNhatAsync(
        WebApplicationFactory<Program> factory, UserEntity nguoiDuyet)
    {
        var log = Assert.Single(await QueryAsync(factory, db =>
            db.AuditLogs.AsNoTracking().ToListAsync()));

        Assert.Equal(AuditAction.Update, log.Action);
        Assert.Equal(nguoiDuyet.Id, log.UserId);

        return log;
    }
}
