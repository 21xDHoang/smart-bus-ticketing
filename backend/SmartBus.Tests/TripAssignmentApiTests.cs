using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartBus.Api.Data;
using SmartBus.Api.Entities;
using SmartBus.Api.Services;
using RouteEntity = SmartBus.Api.Entities.Route;
using UserEntity = SmartBus.Api.Entities.User;

namespace SmartBus.Tests;

/// <summary>
/// Test tích hợp cho API đổi xe / đổi tài xế khi có sự cố —
/// <c>PATCH /trips/{id}/assignment</c> (US 14 "Phân công điều xe", task *"API đổi xe/đổi tài xế khi
/// có sự cố + ghi log thay đổi"* — Phùng Duy Hoàng).
///
/// Hợp đồng đầy đủ ở mục "PATCH /trips/{id}/assignment" của docs/api-contract.md; lớp này phủ từng
/// gạch đầu dòng của mục đó. Đây là luồng ĐIỀU HÀNH KHI CÓ SỰ CỐ, khác
/// <c>PATCH /routes/{routeId}/trips/driver-assignment</c> (gán hàng loạt — xem
/// <see cref="TripDriverAssignmentApiTests"/>): ở đây thay xe VÀ/HOẶC tài xế của MỘT chuyến, không
/// đụng tới giờ chạy.
///
/// ⚠️ Hai điểm dễ hiểu sai, cả hai đều có ca riêng ở dưới:
/// <list type="number">
///   <item>Trùng lịch là CẢNH BÁO chứ không phải lỗi — 200 kèm <c>conflicts</c>, không phải 409.</item>
///   <item>Chỉ soi tài nguyên ĐƯỢC ĐỔI trong request: đổi mỗi tài xế thì vế xe không đem ra soi.</item>
/// </list>
///
/// Dùng lại <see cref="TestAppFactory"/> của JwtAuthTests: chạy trên app thật, mỗi test một CSDL
/// InMemory riêng.
/// </summary>
public class TripAssignmentApiTests
{
    // ---------------------------------------------------------------------------------------
    // Phân quyền
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Khong_gui_token_thi_tra_401()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        var route = await SeedRouteAsync(factory);
        var bus = await SeedBusAsync(factory, "29B-123.45");
        var trip = await SeedTripAsync(factory, route.Id, bus.Id, Gio8);

        var response = await client.PatchAsJsonAsync(AssignmentUrl(trip.Id), ReassignBody(bus.Id));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(RoleCodes.Driver)]
    [InlineData(RoleCodes.Passenger)]
    public async Task Vai_tro_khac_quan_ly_goi_thi_tra_403(string roleCode)
    {
        using var factory = new TestAppFactory();

        var client = await SignInAsync(factory, RoleIdsFor(roleCode), roleCode);
        var route = await SeedRouteAsync(factory);
        var bus = await SeedBusAsync(factory, "29B-123.45");
        var trip = await SeedTripAsync(factory, route.Id, bus.Id, Gio8);

        var response = await client.PatchAsJsonAsync(AssignmentUrl(trip.Id), ReassignBody(bus.Id));

        // Story 14 là nghiệp vụ điều hành của quản lý — tài xế tự đổi xe cho chuyến mình chạy là
        // quyền khác, chưa có trong backlog.
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Admin_doi_duoc_xe()
    {
        using var factory = new TestAppFactory();

        var client = await SignInAsync(factory, RoleIds.Admin, RoleCodes.Admin);
        var route = await SeedRouteAsync(factory);
        var cu = await SeedBusAsync(factory, "29B-123.45");
        var moi = await SeedBusAsync(factory, "29B-678.90");
        var trip = await SeedTripAsync(factory, route.Id, cu.Id, Gio8);

        var response = await client.PatchAsJsonAsync(AssignmentUrl(trip.Id), ReassignBody(moi.Id));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(moi.Id, (await ReadJsonAsync(response)).GetProperty("busId").GetGuid());
    }

    // ---------------------------------------------------------------------------------------
    // Đổi thành công
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Doi_ca_xe_lan_tai_xe_tra_ve_thong_tin_moi()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var route = await SeedRouteAsync(factory);
        var xeCu = await SeedBusAsync(factory, "29B-123.45");
        var xeMoi = await SeedBusAsync(factory, "29B-678.90");
        var taiXeCu = await SeedDriverAsync(factory, "Nguyễn Văn An");
        var taiXeMoi = await SeedDriverAsync(factory, "Trần Văn Bình");
        var trip = await SeedTripAsync(factory, route.Id, xeCu.Id, Gio8, Gio8.AddMinutes(45), driverId: taiXeCu.Id);

        var body = await ReadJsonAsync(await client.PatchAsJsonAsync(
            AssignmentUrl(trip.Id), ReassignBody(xeMoi.Id, taiXeMoi.Id)));

        Assert.Equal(xeMoi.Id, body.GetProperty("busId").GetGuid());
        // Biển số đi kèm ngay trong response để màn hình sự cố không phải gọi thêm GET /buses/{id}.
        Assert.Equal("29B-678.90", body.GetProperty("busLicensePlate").GetString());
        Assert.Equal(taiXeMoi.Id, body.GetProperty("driverId").GetGuid());
        Assert.Equal("Trần Văn Bình", body.GetProperty("driverName").GetString());

        // Không trùng gì nên hai danh sách cảnh báo rỗng — màn hình không hiện banner nào.
        var conflicts = body.GetProperty("conflicts");
        Assert.Equal(0, conflicts.GetProperty("busConflicts").GetArrayLength());
        Assert.Equal(0, conflicts.GetProperty("driverConflicts").GetArrayLength());
    }

    [Fact]
    public async Task Doi_xe_khong_dung_toi_gio_chay()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var route = await SeedRouteAsync(factory);
        var xeCu = await SeedBusAsync(factory, "29B-123.45");
        var xeMoi = await SeedBusAsync(factory, "29B-678.90");
        var trip = await SeedTripAsync(factory, route.Id, xeCu.Id, Gio8, Gio8.AddMinutes(45));

        var body = await ReadJsonAsync(await client.PatchAsJsonAsync(
            AssignmentUrl(trip.Id), ReassignBody(xeMoi.Id)));

        // Đây là luồng "xe hỏng giữa đường": giờ chạy là chuyện của lịch trình, không phải của
        // điều xe. Lệch giờ ở đây là đổi luôn chuyến khác.
        Assert.Equal(Gio8, body.GetProperty("departureTime").GetDateTime());
        Assert.Equal(Gio8.AddMinutes(45), body.GetProperty("arrivalTime").GetDateTime());
        // Trạng thái giữ nguyên: đổi xe không làm chuyến đang chạy thành chuyến khác.
        Assert.Equal("Scheduled", body.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Doi_xe_that_su_thi_updatedAt_duoc_danh_dau_lai()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var route = await SeedRouteAsync(factory);
        var xeCu = await SeedBusAsync(factory, "29B-123.45");
        var xeMoi = await SeedBusAsync(factory, "29B-678.90");
        var trip = await SeedTripAsync(factory, route.Id, xeCu.Id, Gio8);

        var body = await ReadJsonAsync(await client.PatchAsJsonAsync(
            AssignmentUrl(trip.Id), ReassignBody(xeMoi.Id)));

        // Chuyến vừa seed chưa từng được sửa nên null; sau một thay đổi thật phải có mốc.
        Assert.NotEqual(JsonValueKind.Null, body.GetProperty("updatedAt").ValueKind);
    }

    [Fact]
    public async Task Doi_xe_thi_tai_xe_giu_nguyen()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var route = await SeedRouteAsync(factory);
        var xeCu = await SeedBusAsync(factory, "29B-123.45");
        var xeMoi = await SeedBusAsync(factory, "29B-678.90");
        var taiXe = await SeedDriverAsync(factory, "Nguyễn Văn An");
        var trip = await SeedTripAsync(factory, route.Id, xeCu.Id, Gio8, driverId: taiXe.Id);

        var body = await ReadJsonAsync(await client.PatchAsJsonAsync(
            AssignmentUrl(trip.Id), ReassignBody(xeMoi.Id)));

        // PATCH là sửa MỘT PHẦN: trường bỏ trống là "giữ nguyên", không phải "xoá".
        Assert.Equal(taiXe.Id, body.GetProperty("driverId").GetGuid());
        Assert.Equal("Nguyễn Văn An", body.GetProperty("driverName").GetString());
    }

    [Fact]
    public async Task Doi_chi_tai_xe_thi_xe_giu_nguyen()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var route = await SeedRouteAsync(factory);
        var bus = await SeedBusAsync(factory, "29B-123.45");
        var taiXe = await SeedDriverAsync(factory, "Nguyễn Văn An");
        var trip = await SeedTripAsync(factory, route.Id, bus.Id, Gio8);

        var body = await ReadJsonAsync(await client.PatchAsJsonAsync(
            AssignmentUrl(trip.Id), ReassignBody(driverId: taiXe.Id)));

        Assert.Equal(bus.Id, body.GetProperty("busId").GetGuid());
        Assert.Equal("29B-123.45", body.GetProperty("busLicensePlate").GetString());
        Assert.Equal(taiXe.Id, body.GetProperty("driverId").GetGuid());
    }

    [Fact]
    public async Task Doi_giua_chuyen_dang_chay_van_duoc()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var route = await SeedRouteAsync(factory);
        var xeCu = await SeedBusAsync(factory, "29B-123.45");
        var xeMoi = await SeedBusAsync(factory, "29B-678.90");
        var trip = await SeedTripAsync(factory, route.Id, xeCu.Id, Gio8, status: TripStatus.Running);

        var response = await client.PatchAsJsonAsync(AssignmentUrl(trip.Id), ReassignBody(xeMoi.Id));

        // Running là trạng thái DUY NHẤT mà luồng sự cố thực sự cần — xe hỏng giữa đường. Chặn nó
        // là vô hiệu hoá cả tính năng.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Running", (await ReadJsonAsync(response)).GetProperty("status").GetString());
    }

    // ---------------------------------------------------------------------------------------
    // Idempotent — truyền đúng giá trị hiện tại
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Truyen_dung_xe_hien_tai_thi_khong_doi_gi_va_giu_nguyen_updatedAt()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var route = await SeedRouteAsync(factory);
        var bus = await SeedBusAsync(factory, "29B-123.45");
        var trip = await SeedTripAsync(factory, route.Id, bus.Id, Gio8);

        var lanDau = await ReadJsonAsync(await client.PatchAsJsonAsync(
            AssignmentUrl(trip.Id), ReassignBody(bus.Id)));
        Assert.Equal(JsonValueKind.Null, lanDau.GetProperty("updatedAt").ValueKind);

        var response = await client.PatchAsJsonAsync(AssignmentUrl(trip.Id), ReassignBody(bus.Id));
        var body = await ReadJsonAsync(response);

        // Không có gì đổi thì không được đánh dấu sửa: một thao tác không sửa gì mà đóng dấu
        // updatedAt là làm hỏng cột audit.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("updatedAt").ValueKind);
        Assert.Equal(bus.Id, body.GetProperty("busId").GetGuid());
    }

    [Fact]
    public async Task Truyen_dung_tai_xe_hien_tai_thi_khong_bao_trung_chinh_chuyen_do()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var route = await SeedRouteAsync(factory);
        var bus = await SeedBusAsync(factory, "29B-123.45");
        var taiXe = await SeedDriverAsync(factory, "Nguyễn Văn An");
        var trip = await SeedTripAsync(factory, route.Id, bus.Id, Gio8, driverId: taiXe.Id);

        var body = await ReadJsonAsync(await client.PatchAsJsonAsync(
            AssignmentUrl(trip.Id), ReassignBody(driverId: taiXe.Id)));

        // Không thay đổi gì thì cũng không tốn một vòng kiểm tra trùng lịch — và quan trọng hơn,
        // không được báo chuyến tự trùng chính nó (nó đang mang đúng tài xế đó).
        Assert.Equal(0, body.GetProperty("conflicts").GetProperty("driverConflicts").GetArrayLength());
        Assert.Equal(0, body.GetProperty("conflicts").GetProperty("busConflicts").GetArrayLength());
    }

    // ---------------------------------------------------------------------------------------
    // 400 — thiếu tài nguyên để đổi
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Khong_truyen_tai_nguyen_nao_tra_400_voi_loi_o_ca_hai_truong()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var route = await SeedRouteAsync(factory);
        var bus = await SeedBusAsync(factory, "29B-123.45");
        var trip = await SeedTripAsync(factory, route.Id, bus.Id, Gio8);

        var response = await client.PatchAsJsonAsync(AssignmentUrl(trip.Id), new { });
        var body = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Cần cung cấp xe mới hoặc tài xế mới", body.GetProperty("message").GetString());

        // Gắn lỗi vào CẢ HAI ô: form gửi rỗng thì cả hai ô đang thiếu, tô đỏ một ô là đoán bừa.
        var errors = body.GetProperty("errors");
        Assert.True(errors.TryGetProperty("busId", out _));
        Assert.True(errors.TryGetProperty("driverId", out _));
    }

    [Fact]
    public async Task Chuyen_khong_ton_tai_tra_404()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var bus = await SeedBusAsync(factory, "29B-123.45");

        var response = await client.PatchAsJsonAsync(
            AssignmentUrl(Guid.NewGuid()), ReassignBody(bus.Id));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(
            "Không tìm thấy chuyến xe",
            (await ReadJsonAsync(response)).GetProperty("message").GetString());
    }

    [Fact]
    public async Task Id_khong_phai_guid_tra_404()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var bus = await SeedBusAsync(factory, "29B-123.45");

        // Ràng buộc {id:guid} trên route template không khớp thì không controller nào nhận — và
        // không có route nào khác dưới /api/trips nhận "/{gì đó}/assignment".
        var response = await client.PatchAsJsonAsync(
            "/api/trips/khong-phai-guid/assignment", ReassignBody(bus.Id));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---------------------------------------------------------------------------------------
    // Xe / tài xế không dùng được
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Xe_khong_ton_tai_tra_404()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var route = await SeedRouteAsync(factory);
        var bus = await SeedBusAsync(factory, "29B-123.45");
        var trip = await SeedTripAsync(factory, route.Id, bus.Id, Gio8);

        var response = await client.PatchAsJsonAsync(AssignmentUrl(trip.Id), ReassignBody(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(
            "Không tìm thấy xe buýt",
            (await ReadJsonAsync(response)).GetProperty("message").GetString());
    }

    [Theory]
    [InlineData(BusStatus.Maintenance)]
    [InlineData(BusStatus.Inactive)]
    public async Task Xe_khong_khai_thac_tra_409_va_chuyen_giu_nguyen_xe_cu(BusStatus trangThai)
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var route = await SeedRouteAsync(factory);
        var xeCu = await SeedBusAsync(factory, "29B-123.45");
        var xeHong = await SeedBusAsync(factory, "29B-678.90", trangThai);
        var trip = await SeedTripAsync(factory, route.Id, xeCu.Id, Gio8);

        var response = await client.PatchAsJsonAsync(AssignmentUrl(trip.Id), ReassignBody(xeHong.Id));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(
            "Xe không trong trạng thái khai thác nên không thể gán vào chuyến",
            (await ReadJsonAsync(response)).GetProperty("message").GetString());

        // Xe hỏng thì chuyến PHẢI còn nguyên xe cũ: nếu service kịp ghi rồi mới kiểm thì chuyến
        // đang chạy đã bị gán vào một xe đang nằm xưởng.
        Assert.Equal(xeCu.Id, await BusIdOfTripAsync(factory, trip.Id));
    }

    [Fact]
    public async Task Tai_xe_khong_ton_tai_tra_404()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var route = await SeedRouteAsync(factory);
        var bus = await SeedBusAsync(factory, "29B-123.45");
        var trip = await SeedTripAsync(factory, route.Id, bus.Id, Gio8);

        var response = await client.PatchAsJsonAsync(
            AssignmentUrl(trip.Id), ReassignBody(driverId: Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(
            "Không tìm thấy tài xế",
            (await ReadJsonAsync(response)).GetProperty("message").GetString());
    }

    [Fact]
    public async Task Tai_khoan_khong_phai_tai_xe_tra_404()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var route = await SeedRouteAsync(factory);
        var bus = await SeedBusAsync(factory, "29B-123.45");
        var trip = await SeedTripAsync(factory, route.Id, bus.Id, Gio8);

        // Hành khách có thật trong CSDL nhưng không mang vai trò Driver — truy vấn lọc theo vai trò
        // nên ca này rơi vào đúng nhánh 404 của GUID không tồn tại.
        var hanhKhach = await SeedUserAsync(factory, RoleIds.Passenger, RoleCodes.Passenger);

        var response = await client.PatchAsJsonAsync(
            AssignmentUrl(trip.Id), ReassignBody(driverId: hanhKhach.Id));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Tai_khoan_tai_xe_bi_khoa_tra_409()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var route = await SeedRouteAsync(factory);
        var bus = await SeedBusAsync(factory, "29B-123.45");
        var trip = await SeedTripAsync(factory, route.Id, bus.Id, Gio8);
        var biKhoa = await SeedDriverAsync(factory, "Nguyễn Văn An", isActive: false);

        var response = await client.PatchAsJsonAsync(
            AssignmentUrl(trip.Id), ReassignBody(driverId: biKhoa.Id));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(
            "Tài khoản tài xế đang bị khóa nên không thể gán vào chuyến",
            (await ReadJsonAsync(response)).GetProperty("message").GetString());
    }

    // ---------------------------------------------------------------------------------------
    // Chuyến không còn đổi được
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Chuyen_da_huy_tra_409()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var route = await SeedRouteAsync(factory);
        var xeCu = await SeedBusAsync(factory, "29B-123.45");
        var xeMoi = await SeedBusAsync(factory, "29B-678.90");
        var trip = await SeedTripAsync(factory, route.Id, xeCu.Id, Gio8, status: TripStatus.Cancelled);

        var response = await client.PatchAsJsonAsync(AssignmentUrl(trip.Id), ReassignBody(xeMoi.Id));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(
            "Chuyến đã hủy, không thể đổi xe hoặc tài xế",
            (await ReadJsonAsync(response)).GetProperty("message").GetString());
        Assert.Equal(xeCu.Id, await BusIdOfTripAsync(factory, trip.Id));
    }

    [Fact]
    public async Task Chuyen_da_hoan_thanh_tra_409()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var route = await SeedRouteAsync(factory);
        var xeCu = await SeedBusAsync(factory, "29B-123.45");
        var xeMoi = await SeedBusAsync(factory, "29B-678.90");
        var trip = await SeedTripAsync(factory, route.Id, xeCu.Id, Gio8, status: TripStatus.Completed);

        var response = await client.PatchAsJsonAsync(AssignmentUrl(trip.Id), ReassignBody(xeMoi.Id));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(
            "Chuyến đã hoàn thành, không thể đổi xe hoặc tài xế",
            (await ReadJsonAsync(response)).GetProperty("message").GetString());
        Assert.Equal(xeCu.Id, await BusIdOfTripAsync(factory, trip.Id));
    }

    [Fact]
    public async Task Chuyen_da_huy_va_khong_truyen_tai_nguyen_thi_van_tra_400()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var route = await SeedRouteAsync(factory);
        var bus = await SeedBusAsync(factory, "29B-123.45");
        var trip = await SeedTripAsync(factory, route.Id, bus.Id, Gio8, status: TripStatus.Cancelled);

        var response = await client.PatchAsJsonAsync(AssignmentUrl(trip.Id), new { });

        // Request sai hình dạng được chặn TRƯỚC khi tra CSDL: kết quả không được phụ thuộc vào
        // việc chuyến có tồn tại hay không.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---------------------------------------------------------------------------------------
    // Trùng lịch — cảnh báo, KHÔNG chặn
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Trung_lich_xe_thi_van_200_kem_canh_bao()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var route = await SeedRouteAsync(factory, code: "01");
        var otherRoute = await SeedRouteAsync(factory, code: "02");
        var xeCu = await SeedBusAsync(factory, "29B-123.45");
        var xeMoi = await SeedBusAsync(factory, "29B-678.90");

        // Xe mới đang chạy chuyến khác ở TUYẾN KHÁC, khung giờ chồng lên chuyến sắp đổi.
        var dangChay = await SeedTripAsync(
            factory, otherRoute.Id, xeMoi.Id, Gio8.AddMinutes(30), Gio8.AddHours(1));
        var sapDoi = await SeedTripAsync(factory, route.Id, xeCu.Id, Gio8, Gio8.AddHours(1));

        var response = await client.PatchAsJsonAsync(AssignmentUrl(sapDoi.Id), ReassignBody(xeMoi.Id));
        var body = await ReadJsonAsync(response);

        // 200 chứ không 409: luồng sự cố cần thay xe NGAY, người điều hành được phép cố ý chấp nhận
        // trùng — khác hẳn POST /routes/{routeId}/trips (409).
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(xeMoi.Id, body.GetProperty("busId").GetGuid());

        var canhBao = Assert.Single(body.GetProperty("conflicts").GetProperty("busConflicts").EnumerateArray());
        Assert.Equal(dangChay.Id, canhBao.GetProperty("id").GetGuid());
        // Tuyến của chuyến TRÙNG (02), không phải tuyến của chuyến đang đổi — đây là lý do
        // ConflictingTripResponse mang theo cả mã lẫn tên tuyến.
        Assert.Equal("02", canhBao.GetProperty("routeCode").GetString());
        Assert.Equal("Tuyến 02", canhBao.GetProperty("routeName").GetString());
        Assert.Equal(xeMoi.Id, canhBao.GetProperty("busId").GetGuid());

        // Vế tài xế rỗng: request không đổi tài xế nên không soi.
        Assert.Equal(0, body.GetProperty("conflicts").GetProperty("driverConflicts").GetArrayLength());
    }

    [Fact]
    public async Task Trung_lich_tai_xe_thi_van_200_kem_canh_bao()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var route = await SeedRouteAsync(factory, code: "01");
        var otherRoute = await SeedRouteAsync(factory, code: "02");
        var bus = await SeedBusAsync(factory, "29B-123.45");
        var taiXeMoi = await SeedDriverAsync(factory, "Trần Văn Bình");

        var dangChay = await SeedTripAsync(
            factory, otherRoute.Id, bus.Id, Gio8.AddMinutes(30), Gio8.AddHours(1), driverId: taiXeMoi.Id);
        var sapDoi = await SeedTripAsync(factory, route.Id, bus.Id, Gio8, Gio8.AddHours(1));

        var body = await ReadJsonAsync(await client.PatchAsJsonAsync(
            AssignmentUrl(sapDoi.Id), ReassignBody(driverId: taiXeMoi.Id)));

        var canhBao = Assert.Single(body.GetProperty("conflicts").GetProperty("driverConflicts").EnumerateArray());
        Assert.Equal(dangChay.Id, canhBao.GetProperty("id").GetGuid());
        Assert.Equal(taiXeMoi.Id, canhBao.GetProperty("driverId").GetGuid());
        Assert.Equal(0, body.GetProperty("conflicts").GetProperty("busConflicts").GetArrayLength());
    }

    [Fact]
    public async Task Doi_moi_tai_xe_thi_khong_doi_chieu_ve_xe()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var route = await SeedRouteAsync(factory);
        var bus = await SeedBusAsync(factory, "29B-123.45");
        var taiXeMoi = await SeedDriverAsync(factory, "Trần Văn Bình");

        // Chuyến khác ĐANG dùng đúng chiếc xe này, khung giờ chồng lên — dữ liệu vướng sẵn từ
        // trước (PUT /routes/{routeId}/trips/{id} cố ý không kiểm tra trùng).
        await SeedTripAsync(factory, route.Id, bus.Id, Gio8.AddMinutes(15), Gio8.AddHours(1));
        var sapDoi = await SeedTripAsync(factory, route.Id, bus.Id, Gio8, Gio8.AddHours(1));

        var body = await ReadJsonAsync(await client.PatchAsJsonAsync(
            AssignmentUrl(sapDoi.Id), ReassignBody(driverId: taiXeMoi.Id)));

        // Đổi mỗi tài xế mà dội lại cảnh báo trùng XE có sẵn thì vô lý: người điều hành đang sửa
        // một chuyện khác, và cảnh báo đó không do thay đổi này sinh ra.
        Assert.Equal(0, body.GetProperty("conflicts").GetProperty("busConflicts").GetArrayLength());
        Assert.Equal(0, body.GetProperty("conflicts").GetProperty("driverConflicts").GetArrayLength());
    }

    [Fact]
    public async Task Chuyen_dang_doi_khong_tu_bao_trung_chinh_no()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var route = await SeedRouteAsync(factory);
        var xeCu = await SeedBusAsync(factory, "29B-123.45");
        var xeMoi = await SeedBusAsync(factory, "29B-678.90");
        var dangChay = await SeedTripAsync(factory, route.Id, xeMoi.Id, Gio8.AddMinutes(30), Gio8.AddHours(1));
        var sapDoi = await SeedTripAsync(factory, route.Id, xeCu.Id, Gio8, Gio8.AddHours(1));

        var body = await ReadJsonAsync(await client.PatchAsJsonAsync(
            AssignmentUrl(sapDoi.Id), ReassignBody(xeMoi.Id)));

        // Đúng MỘT cảnh báo: chuyến kia. Chuyến đang đổi không được tự báo chính nó (nó sẽ mang xe
        // mới sau thao tác này).
        var canhBao = Assert.Single(body.GetProperty("conflicts").GetProperty("busConflicts").EnumerateArray());
        Assert.Equal(dangChay.Id, canhBao.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Chuyen_noi_duoi_khong_tinh_la_trung()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var route = await SeedRouteAsync(factory);
        var xeCu = await SeedBusAsync(factory, "29B-123.45");
        var xeMoi = await SeedBusAsync(factory, "29B-678.90");

        // Chuyến kia kết thúc đúng lúc chuyến này khởi hành — nối đuôi là chuyện bình thường.
        await SeedTripAsync(factory, route.Id, xeMoi.Id, Gio8, Gio8.AddHours(1));
        var sapDoi = await SeedTripAsync(factory, route.Id, xeCu.Id, Gio8.AddHours(1), Gio8.AddHours(2));

        var body = await ReadJsonAsync(await client.PatchAsJsonAsync(
            AssignmentUrl(sapDoi.Id), ReassignBody(xeMoi.Id)));

        Assert.Equal(0, body.GetProperty("conflicts").GetProperty("busConflicts").GetArrayLength());
    }

    [Theory]
    [InlineData(TripStatus.Cancelled)]
    [InlineData(TripStatus.Completed)]
    public async Task Chuyen_khong_con_hoat_dong_khong_tinh_la_trung(TripStatus trangThai)
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var route = await SeedRouteAsync(factory);
        var xeCu = await SeedBusAsync(factory, "29B-123.45");
        var xeMoi = await SeedBusAsync(factory, "29B-678.90");

        // Chuyến đã huỷ / đã chạy xong cùng khung giờ: nó KHÔNG còn chiếm chỗ trên thời gian biểu,
        // nên đổi xe sang đó không tạo xung đột nào.
        await SeedTripAsync(factory, route.Id, xeMoi.Id, Gio8, Gio8.AddHours(1), trangThai);
        var sapDoi = await SeedTripAsync(factory, route.Id, xeCu.Id, Gio8.AddMinutes(30), Gio8.AddHours(1));

        var body = await ReadJsonAsync(await client.PatchAsJsonAsync(
            AssignmentUrl(sapDoi.Id), ReassignBody(xeMoi.Id)));

        Assert.Equal(0, body.GetProperty("conflicts").GetProperty("busConflicts").GetArrayLength());
    }

    [Fact]
    public async Task Chuyen_chua_co_gio_den_trung_dung_gio_khoi_hanh_van_bao()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var route = await SeedRouteAsync(factory);
        var xeCu = await SeedBusAsync(factory, "29B-123.45");
        var xeMoi = await SeedBusAsync(factory, "29B-678.90");

        // Hai "mốc" cùng giờ khởi hành: khoảng của cả hai đều rỗng nên phép so khoảng không tự bắt
        // được — đây là ca mà điều kiện "DepartureTime == departureUtc" tồn tại để bắt.
        var moc = await SeedTripAsync(factory, route.Id, xeMoi.Id, Gio8);
        var sapDoi = await SeedTripAsync(factory, route.Id, xeCu.Id, Gio8);

        var body = await ReadJsonAsync(await client.PatchAsJsonAsync(
            AssignmentUrl(sapDoi.Id), ReassignBody(xeMoi.Id)));

        var canhBao = Assert.Single(body.GetProperty("conflicts").GetProperty("busConflicts").EnumerateArray());
        Assert.Equal(moc.Id, canhBao.GetProperty("id").GetGuid());
        // Chuyến chưa có giờ đến thì trường đó null trong cảnh báo, không phải chuỗi rỗng.
        Assert.Equal(JsonValueKind.Null, canhBao.GetProperty("arrivalTime").ValueKind);
    }

    [Fact]
    public async Task Chuyen_dang_chay_cung_khung_gio_van_tinh_la_trung()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var route = await SeedRouteAsync(factory);
        var xeCu = await SeedBusAsync(factory, "29B-123.45");
        var xeMoi = await SeedBusAsync(factory, "29B-678.90");

        // Running cũng chiếm chỗ trên thời gian biểu — phép kiểm tra lọc Scheduled HOẶC Running.
        // Bỏ vế Running là bỏ sót đúng ca nguy hiểm nhất: chiếc xe đang chạy ngoài đường.
        var dangChay = await SeedTripAsync(
            factory, route.Id, xeMoi.Id, Gio8.AddMinutes(30), Gio8.AddHours(1), TripStatus.Running);
        var sapDoi = await SeedTripAsync(factory, route.Id, xeCu.Id, Gio8, Gio8.AddHours(1));

        var body = await ReadJsonAsync(await client.PatchAsJsonAsync(
            AssignmentUrl(sapDoi.Id), ReassignBody(xeMoi.Id)));

        var canhBao = Assert.Single(body.GetProperty("conflicts").GetProperty("busConflicts").EnumerateArray());
        Assert.Equal(dangChay.Id, canhBao.GetProperty("id").GetGuid());
        // Trạng thái đi kèm cảnh báo để màn hình phân biệt "xe đang chạy" với "xe sắp chạy" —
        // hai mức độ khẩn cấp khác nhau.
        Assert.Equal("Running", canhBao.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Mot_chuyen_trung_ca_xe_lan_tai_xe_thi_co_mat_o_ca_hai_danh_sach()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var route = await SeedRouteAsync(factory);
        var otherRoute = await SeedRouteAsync(factory, code: "02");
        var xeCu = await SeedBusAsync(factory, "29B-123.45");
        var xeMoi = await SeedBusAsync(factory, "29B-678.90");
        var taiXeMoi = await SeedDriverAsync(factory, "Trần Văn Bình");

        // Chuyến kia dùng ĐÚNG cả xe mới lẫn tài xế mới. Câu truy vấn lọc theo (xe HOẶC tài xế)
        // nên dòng này khớp cả hai vế — và phải có mặt ở CẢ HAI danh sách, không bị vế nào nuốt.
        var vuaTrung = await SeedTripAsync(
            factory, otherRoute.Id, xeMoi.Id, Gio8.AddMinutes(30), Gio8.AddHours(1), driverId: taiXeMoi.Id);
        var sapDoi = await SeedTripAsync(factory, route.Id, xeCu.Id, Gio8, Gio8.AddHours(1));

        var body = await ReadJsonAsync(await client.PatchAsJsonAsync(
            AssignmentUrl(sapDoi.Id), ReassignBody(xeMoi.Id, taiXeMoi.Id)));
        var conflicts = body.GetProperty("conflicts");

        Assert.Equal(
            vuaTrung.Id,
            Assert.Single(conflicts.GetProperty("busConflicts").EnumerateArray()).GetProperty("id").GetGuid());
        Assert.Equal(
            vuaTrung.Id,
            Assert.Single(conflicts.GetProperty("driverConflicts").EnumerateArray()).GetProperty("id").GetGuid());
    }

    // ---------------------------------------------------------------------------------------
    // Hình dạng response
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Response_du_dung_muoi_hai_truong_cua_hop_dong()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var route = await SeedRouteAsync(factory);
        var bus = await SeedBusAsync(factory, "29B-123.45");
        var xeMoi = await SeedBusAsync(factory, "29B-678.90");
        var trip = await SeedTripAsync(factory, route.Id, bus.Id, Gio8, Gio8.AddMinutes(45));

        var body = await ReadJsonAsync(await client.PatchAsJsonAsync(
            AssignmentUrl(trip.Id), ReassignBody(xeMoi.Id)));

        // Không lặp lại createdAt: ai cần hồ sơ đầy đủ thì gọi GET /trips/{id}. Response này trả lời
        // đúng câu hỏi của luồng sự cố.
        var truong = body.EnumerateObject().Select(p => p.Name).OrderBy(n => n).ToArray();

        Assert.Equal(
            [
                "arrivalTime", "busId", "busLicensePlate", "conflicts", "departureTime",
                "driverId", "driverName", "id", "routeId", "status", "updatedAt",
            ],
            truong);

        Assert.Equal(trip.Id, body.GetProperty("id").GetGuid());
        Assert.Equal(route.Id, body.GetProperty("routeId").GetGuid());

        // Hình dạng của conflicts cũng là hợp đồng: hai danh sách, luôn có mặt kể cả khi rỗng.
        var conflicts = body.GetProperty("conflicts").EnumerateObject().Select(p => p.Name).OrderBy(n => n).ToArray();
        Assert.Equal(["busConflicts", "driverConflicts"], conflicts);
    }

    // ---------------------------------------------------------------------------------------
    // Nhật ký kiểm toán (US 23) — "ghi log thay đổi" nằm trong chính tên task này
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Doi_thanh_cong_ghi_dung_mot_dong_Update_nham_dung_chuyen()
    {
        using var factory = new TestAppFactory();
        var (client, quanLy) = await SignInAsManagerWithUserAsync(factory);

        var route = await SeedRouteAsync(factory);
        var xeCu = await SeedBusAsync(factory, "29B-123.45");
        var xeMoi = await SeedBusAsync(factory, "29B-678.90");
        var trip = await SeedTripAsync(factory, route.Id, xeCu.Id, Gio8);

        var response = await client.PatchAsJsonAsync(AssignmentUrl(trip.Id), ReassignBody(xeMoi.Id));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Assert.Single chứ không phải Assert.Contains: endpoint KHÔNG tự gọi ghi log — middleware
        // phủ sẵn. Nếu ai đó thêm một lời gọi thủ công vào controller, ca này đỏ ngay.
        var log = Assert.Single(await NhatKyAsync(factory));

        Assert.Equal(AuditAction.Update, log.Action);
        // Target là chỗ DUY NHẤT nói được thao tác chạm vào đâu. AuditLogMiddleware suy tên bảng từ
        // đoạn đứng ngay trước tham số {id} trên route template — đổi {id} thành {tripId} là dòng
        // nhật ký tụt xuống "Assignment:{...}", và ca này bắt được.
        Assert.Equal($"Trips:{trip.Id}", log.Target);
        Assert.Equal(quanLy.Id, log.UserId);
    }

    [Theory]
    [InlineData("chuyen-khong-ton-tai", HttpStatusCode.NotFound)]
    [InlineData("khong-truyen-tai-nguyen", HttpStatusCode.BadRequest)]
    [InlineData("xe-khong-khai-thac", HttpStatusCode.Conflict)]
    public async Task Request_that_bai_khong_sinh_dong_nhat_ky_nao(string tinhHuong, HttpStatusCode mongDoi)
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsManagerAsync(factory);

        var route = await SeedRouteAsync(factory);
        var xeCu = await SeedBusAsync(factory, "29B-123.45");
        var xeHong = await SeedBusAsync(factory, "29B-678.90", BusStatus.Maintenance);
        var trip = await SeedTripAsync(factory, route.Id, xeCu.Id, Gio8);

        var response = tinhHuong switch
        {
            // 404 — chặn trước khi kịp tra chuyến.
            "chuyen-khong-ton-tai" => await client.PatchAsJsonAsync(
                AssignmentUrl(Guid.NewGuid()), ReassignBody(xeCu.Id)),
            // 400 — chặn trước cả bước tra CSDL.
            "khong-truyen-tai-nguyen" => await client.PatchAsJsonAsync(AssignmentUrl(trip.Id), new { }),
            // 409 — đã qua hết các bước tra cứu rồi mới chặn, sát ranh giới ghi nhất.
            _ => await client.PatchAsJsonAsync(AssignmentUrl(trip.Id), ReassignBody(xeHong.Id)),
        };

        Assert.Equal(mongDoi, response.StatusCode);

        // Ba mã lỗi, cùng một kết luận: dữ liệu chưa hề đổi thì nhật ký không được có dòng nào —
        // middleware chỉ ghi khi response 2xx.
        Assert.Empty(await NhatKyAsync(factory));
    }

    // ---------------------------------------------------------------------------------------
    // Helper — mỗi lớp test tự chép, không có lớp base chung (đúng lệ đang có của dự án)
    // ---------------------------------------------------------------------------------------

    private static string AssignmentUrl(Guid tripId) => $"/api/trips/{tripId}/assignment";

    /// <summary>
    /// Body PATCH. Hai tham số đều có mặc định null để ca "chỉ đổi xe" / "chỉ đổi tài xế" đọc ra
    /// đúng ý mà không phải dựng anonymous type riêng.
    /// </summary>
    private static object ReassignBody(Guid? busId = null, Guid? driverId = null)
        => new { busId, driverId };

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
        => await response.Content.ReadFromJsonAsync<JsonElement>();

    /// <summary>Đọc thẳng cột BusId trong CSDL — để khẳng định một request lỗi KHÔNG kịp ghi gì.</summary>
    private static async Task<Guid> BusIdOfTripAsync(TestAppFactory factory, Guid tripId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider
            .GetRequiredService<SmartBus.Api.Data.AppDbContext>();

        return (await db.Trips.FindAsync(tripId))!.BusId;
    }

    /// <summary>
    /// Đọc thẳng bảng nhật ký. Đăng nhập ở các ca này đi bằng token dựng sẵn
    /// (<c>factory.CreateTokenFor</c>) chứ không qua <c>/api/auth/login</c>, nên bảng chỉ có thể
    /// chứa dòng do middleware ghi — không lẫn bản ghi <c>Login</c> nào.
    /// </summary>
    private static async Task<List<AuditLog>> NhatKyAsync(TestAppFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await db.AuditLogs
            .AsNoTracking()
            .OrderBy(log => log.CreatedAt)
            .ThenBy(log => log.Id)
            .ToListAsync();
    }

    private static async Task<HttpClient> SignInAsync(TestAppFactory factory, Guid roleId, string roleCode)
    {
        await EnsureAllRolesAsync(factory);
        var user = await SeedUserAsync(factory, roleId, roleCode);

        return ClientWith(factory, factory.CreateTokenFor(user));
    }

    private static async Task<HttpClient> SignInAsManagerAsync(TestAppFactory factory)
        => (await SignInAsManagerWithUserAsync(factory)).Client;

    /// <summary>
    /// Như <see cref="SignInAsManagerAsync"/> nhưng trả thêm tài khoản quản lý — ca nhật ký cần
    /// đối chiếu <c>AuditLogs.UserId</c> với đúng người đã thực hiện thao tác.
    /// </summary>
    private static async Task<(HttpClient Client, UserEntity Manager)> SignInAsManagerWithUserAsync(
        TestAppFactory factory)
    {
        await EnsureAllRolesAsync(factory);
        var manager = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager);

        return (ClientWith(factory, factory.CreateTokenFor(manager)), manager);
    }

    private static HttpClient ClientWith(TestAppFactory factory, string accessToken)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

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
    /// Dựng đúng nền dữ liệu mà migration tạo sẵn ở production: 4 vai trò chuẩn.
    /// Provider InMemory KHÔNG chạy <c>HasData</c>, nên không seed thì tài khoản trỏ tới vai trò
    /// không tồn tại và mọi request đều 403 dù token hợp lệ.
    /// </summary>
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

    /// <summary>Bốn vai trò migration seed sẵn — khớp <c>AppDbContext.UserRoles.cs</c>.</summary>
    private static readonly (Guid Id, string Code)[] CanonicalRoles =
    [
        (RoleIds.Admin, RoleCodes.Admin),
        (RoleIds.Manager, RoleCodes.Manager),
        (RoleIds.Driver, RoleCodes.Driver),
        (RoleIds.Passenger, RoleCodes.Passenger),
    ];

    private static async Task<UserEntity> SeedUserAsync(
        TestAppFactory factory,
        Guid roleId,
        string roleCode,
        string fullName = "Người Dùng Test",
        bool isActive = true)
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
            PhoneNumber = "09" + Random.Shared.Next(10_000_000, 99_999_999),
            FullName = fullName,
            PasswordHash = PasswordService.HashPassword("matkhau123"),
            IsActive = isActive,
            RoleId = roleId,
            // Vai trò chính phải luôn có mặt ở bảng nối — đúng bất biến mà AuthService giữ, và cũng
            // là vế thứ hai trong truy vấn lọc tài xế (quy ước A8.4).
            UserRoles = [new UserRole { RoleId = roleId }],
        };

        await factory.SeedAsync(db => db.Users.Add(user));

        return user;
    }

    private static async Task<UserEntity> SeedDriverAsync(
        TestAppFactory factory,
        string fullName,
        bool isActive = true)
        => await SeedUserAsync(factory, RoleIds.Driver, RoleCodes.Driver, fullName, isActive);

    private static async Task<RouteEntity> SeedRouteAsync(TestAppFactory factory, string code = "01")
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

    private static async Task<Bus> SeedBusAsync(
        TestAppFactory factory,
        string licensePlate,
        BusStatus trangThai = BusStatus.Active)
    {
        var bus = new Bus
        {
            Id = Guid.NewGuid(),
            LicensePlate = licensePlate,
            BusType = "Hyundai County 29 chỗ",
            Capacity = 29,
            Status = trangThai,
        };

        await factory.SeedAsync(db => db.Buses.Add(bus));

        return bus;
    }

    private static async Task<Trip> SeedTripAsync(
        TestAppFactory factory,
        Guid routeId,
        Guid busId,
        DateTime departureTime,
        DateTime? arrivalTime = null,
        TripStatus status = TripStatus.Scheduled,
        Guid? driverId = null)
    {
        // Chỉ gán khoá ngoại, KHÔNG gán navigation: Route/Bus/User được seed ở scope khác, gán
        // navigation vào đây sẽ khiến EF tưởng chúng là bản ghi mới và chèn trùng khoá chính.
        var trip = new Trip
        {
            Id = Guid.NewGuid(),
            RouteId = routeId,
            BusId = busId,
            DriverId = driverId,
            DepartureTime = departureTime,
            ArrivalTime = arrivalTime,
            Status = status,
        };

        await factory.SeedAsync(db => db.Trips.Add(trip));

        return trip;
    }

    /// <summary>Giờ khởi hành gốc của các chuyến trong lớp này — UTC, như mọi cột thời gian (A3).</summary>
    private static readonly DateTime Gio8 = new(2026, 10, 2, 8, 0, 0, DateTimeKind.Utc);
}
