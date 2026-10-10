using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartBus.Api.Data;
using SmartBus.Api.Entities;
using UserEntity = SmartBus.Api.Entities.User;

namespace SmartBus.Tests;

/// <summary>
/// Test tích hợp cho API giữ ghế tạm thời theo phiên — <c>POST /api/seat-holds</c> (task *"API giữ
/// ghế tạm thời (khóa ghế theo phiên)"* — Nguyễn Duy Kiên, story 3).
///
/// Dùng lại <see cref="TestAppFactory"/> của JwtAuthTests: chạy trên app thật, mỗi test một CSDL
/// InMemory riêng. Endpoint yêu cầu đăng nhập nên mọi ca đều sign in trước (trừ ca 401).
///
/// Bộ test bám ĐÚNG câu chữ và thứ tự kiểm của bản đã merge (PR #135): kiểm hình dạng danh sách ghế
/// (rỗng/null → Guid.Empty → lặp) trước lượt tra chuyến, rồi mới tới 404 chuyến không tồn tại, 409
/// chuyến đã huỷ/đã chạy xong, và 409 ghế đang có người giữ kèm số ghế vướng.
///
/// Khác hai file anh em (SeatHoldExtendApiTests / SeatHoldReleaseApiTests) ở chỗ dựng dữ liệu: ca
/// tạo phiên CÓ đối chiếu <c>Seat.BusId</c> với <c>Trip.BusId</c> nên ghế phải seed cùng xe với
/// chuyến — không dùng được lệ "seed dòng mồ côi" của hai file kia. Phần còn lại của endpoint đọc
/// thẳng CSDL nên vẫn không cần seed Route/Bus thật (InMemory không cưỡng chế khoá ngoại).
///
/// ⚠️ Giới hạn của provider InMemory ảnh hưởng tới cách đọc kết quả ở đây:
///   • KHÔNG dựng unique index — gồm cả partial unique index (TripId, SeatId) WHERE Status =
///     'Holding' (docs/26 §3): nhánh hai request cùng giữ một ghế (bắt DbUpdateException trả 409)
///     chỉ chạy ở PostgreSQL, không phủ được ở đây. Vì vậy phép kiểm trước trong service là phép
///     kiểm DUY NHẤT chạy được dưới test — các ca 409 ở đây đều đi qua nó, và chúng cố ý chọn
///     ExpiresAt để chứng minh phép kiểm chỉ nhìn Status (xem ca "vừa quá hạn").
///   • KHÔNG chạy HasData — phải seed vai trò trước, không thì token hợp lệ mà vai trò không tồn tại.
/// </summary>
public class SeatHoldCreateApiTests
{
    private const string CreateUrl = "/api/seat-holds";

    // ---------------------------------------------------------------------------------------
    // Phân quyền
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Khong_gui_token_thi_tra_401()
    {
        using var factory = new TestAppFactory();

        var response = await factory.CreateClient().PostAsJsonAsync(
            CreateUrl, new { tripId = Guid.NewGuid(), seatIds = new[] { Guid.NewGuid() } });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ---------------------------------------------------------------------------------------
    // Kiểm tra đầu vào — 400, và không có gì được ghi khi request bị từ chối
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Thieu_tripId_thi_tra_400()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsPassengerAsync(factory);

        var response = await client.PostAsJsonAsync(CreateUrl, new { seatIds = new[] { Guid.NewGuid() } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await ReadJsonAsync(response);

        // 400 của ModelState dùng thông báo chung, lỗi nằm trong "errors" theo tên trường (cấu trúc
        // { message, errors } thống nhất của dự án) — tripId camelCase đúng như frontend đọc.
        Assert.Equal("Dữ liệu đầu vào không hợp lệ", body.GetProperty("message").GetString());
        Assert.Equal(["Chuyến xe không được để trống"], ErrorMessagesOf(body, "tripId"));
    }

    [Fact]
    public async Task SeatIds_rong_thi_tra_400()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsPassengerAsync(factory);

        var response = await client.PostAsJsonAsync(
            CreateUrl, new { tripId = Guid.NewGuid(), seatIds = Array.Empty<Guid>() });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await ReadJsonAsync(response);
        Assert.Equal("Dữ liệu đầu vào không hợp lệ", body.GetProperty("message").GetString());

        // Câu chữ của [MinLength] trên CreateSeatHoldRequest — DTO chặn tại model binding.
        Assert.Equal(["Danh sách ghế không được để trống"], ErrorMessagesOf(body, "seatIds"));
    }

    [Fact]
    public async Task SeatIds_trung_nhau_thi_tra_400_va_khong_ghi_gi()
    {
        using var factory = new TestAppFactory();
        var user = await SeedUserAsync(factory);
        var busId = Guid.NewGuid();
        var trip = await SeedTripAsync(factory, busId);
        var seat = await SeedSeatAsync(factory, busId, floor: 1, row: 1, column: 1, "A1");

        var client = ClientWith(factory, factory.CreateTokenFor(user));
        var response = await client.PostAsJsonAsync(
            CreateUrl, new { tripId = trip.Id, seatIds = new[] { seat.Id, seat.Id } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await ReadJsonAsync(response);
        Assert.Equal("Danh sách ghế có ghế bị lặp lại", body.GetProperty("message").GetString());
        Assert.Equal(["Danh sách ghế có ghế bị lặp lại"], ErrorMessagesOf(body, "seatIds"));

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Ghế trùng trong cùng request là lỗi phía gọi — service chặn ngay từ đầu, TRƯỚC cả lượt tra
        // chuyến, nên không lưu gì. (Thứ tự này được ghim riêng ở ca "…chạy trước khi tra chuyến".)
        Assert.Equal(0, await db.SeatHolds.CountAsync());
        Assert.Equal(0, await db.SeatHoldLogs.CountAsync());
    }

    [Fact]
    public async Task Ghe_khong_ton_tai_thi_tra_400_va_khong_ghi_gi()
    {
        using var factory = new TestAppFactory();
        var user = await SeedUserAsync(factory);
        var busId = Guid.NewGuid();
        var trip = await SeedTripAsync(factory, busId);

        var client = ClientWith(factory, factory.CreateTokenFor(user));
        var response = await client.PostAsJsonAsync(
            CreateUrl, new { tripId = trip.Id, seatIds = new[] { Guid.NewGuid() } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await ReadJsonAsync(response);
        Assert.Equal("Danh sách ghế có ghế không tồn tại", body.GetProperty("message").GetString());
        Assert.Equal(["Danh sách ghế có ghế không tồn tại"], ErrorMessagesOf(body, "seatIds"));

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(0, await db.SeatHolds.CountAsync());
    }

    [Fact]
    public async Task Ghe_thuoc_xe_khac_thi_tra_400()
    {
        using var factory = new TestAppFactory();
        var user = await SeedUserAsync(factory);
        var busId = Guid.NewGuid();
        var trip = await SeedTripAsync(factory, busId);

        // Ghế có thật nhưng thuộc xe KHÁC: service có câu RIÊNG cho nhánh này ("không thuộc xe của
        // chuyến") — khác câu "không tồn tại" của ca lệch số lượng id tìm thấy, vì hai bệnh khác
        // nhau: một bên là id sai hẳn, một bên là ghế của xe khác bị gắn vào chuyến này.
        var seatXeKhac = await SeedSeatAsync(factory, Guid.NewGuid(), floor: 1, row: 1, column: 1, "A1");

        var client = ClientWith(factory, factory.CreateTokenFor(user));
        var response = await client.PostAsJsonAsync(
            CreateUrl, new { tripId = trip.Id, seatIds = new[] { seatXeKhac.Id } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await ReadJsonAsync(response);
        Assert.Equal("Danh sách ghế có ghế không thuộc xe của chuyến", body.GetProperty("message").GetString());
        Assert.Equal(["Danh sách ghế có ghế không thuộc xe của chuyến"], ErrorMessagesOf(body, "seatIds"));
    }

    [Fact]
    public async Task SeatIds_chua_Guid_Empty_thi_tra_400_va_khong_ghi_gi()
    {
        using var factory = new TestAppFactory();
        var user = await SeedUserAsync(factory);
        var busId = Guid.NewGuid();
        var trip = await SeedTripAsync(factory, busId);

        var client = ClientWith(factory, factory.CreateTokenFor(user));
        var response = await client.PostAsJsonAsync(
            CreateUrl, new { tripId = trip.Id, seatIds = new[] { Guid.Empty } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        // Guid.Empty không trỏ tới ghế nào: chặn sớm để câu lỗi nói đúng bệnh ("không hợp lệ") thay
        // vì đổ xuống truy vấn rồi trả câu "không tồn tại" như một id sai bình thường.
        var body = await ReadJsonAsync(response);
        Assert.Equal("Danh sách ghế có ghế không hợp lệ", body.GetProperty("message").GetString());
        Assert.Equal(["Danh sách ghế có ghế không hợp lệ"], ErrorMessagesOf(body, "seatIds"));

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(0, await db.SeatHolds.CountAsync());
    }

    [Fact]
    public async Task SeatIds_null_thi_tra_400()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsPassengerAsync(factory);

        // Gửi hẳn null chứ không phải mảng rỗng: [Required] và [MinLength] của DTO mỗi thuộc tính bắt
        // một ca — thiếu một trong hai là một kiểu dữ liệu rác lọt xuống tầng service.
        var response = await client.PostAsJsonAsync(
            CreateUrl, new { tripId = Guid.NewGuid(), seatIds = (Guid[]?)null });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await ReadJsonAsync(response);
        Assert.Equal("Dữ liệu đầu vào không hợp lệ", body.GetProperty("message").GetString());
        Assert.Equal(["Danh sách ghế không được để trống"], ErrorMessagesOf(body, "seatIds"));
    }

    [Fact]
    public async Task Kiem_hinh_dang_ghe_chay_truoc_khi_tra_chuyen()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsPassengerAsync(factory);
        var chuyenKhongTonTai = Guid.NewGuid();
        var idGhe = Guid.NewGuid();

        // Chuyến không tồn tại (đáng lẽ 404) NHƯNG danh sách ghế sai hình dạng → 400. Ghim thứ tự
        // kiểm của service: hình dạng ghế chặn trước lượt tra chuyến — đảo thứ tự là đảo kết quả
        // quan sát được (400 thành 404), nên có ca đứng canh.
        var gheLap = await client.PostAsJsonAsync(
            CreateUrl, new { tripId = chuyenKhongTonTai, seatIds = new[] { idGhe, idGhe } });
        var gheEmpty = await client.PostAsJsonAsync(
            CreateUrl, new { tripId = chuyenKhongTonTai, seatIds = new[] { Guid.Empty } });

        Assert.Equal(HttpStatusCode.BadRequest, gheLap.StatusCode);
        Assert.Equal("Danh sách ghế có ghế bị lặp lại", await MessageAsync(gheLap));
        Assert.Equal(HttpStatusCode.BadRequest, gheEmpty.StatusCode);
        Assert.Equal("Danh sách ghế có ghế không hợp lệ", await MessageAsync(gheEmpty));
    }

    [Fact]
    public async Task Chuyen_khong_ton_tai_thi_tra_404()
    {
        using var factory = new TestAppFactory();
        var client = await SignInAsPassengerAsync(factory);

        var response = await client.PostAsJsonAsync(
            CreateUrl, new { tripId = Guid.NewGuid(), seatIds = new[] { Guid.NewGuid() } });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        // Cùng thông báo của GET /trips/{id}/seats — hai endpoint cùng nói về một chuyến phải trả
        // lời giống nhau (đã ghim trong hợp đồng POST /seat-holds).
        Assert.Equal("Không tìm thấy chuyến xe", await MessageAsync(response));
    }

    // ---------------------------------------------------------------------------------------
    // Chuyến không còn nhận giữ ghế — 409 (đã huỷ / đã chạy xong)
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Chuyen_da_huy_thi_tra_409_va_khong_ghi_gi()
    {
        using var factory = new TestAppFactory();
        var user = await SeedUserAsync(factory);
        var busId = Guid.NewGuid();
        var trip = await SeedTripAsync(factory, busId, TripStatus.Cancelled);
        var seat = await SeedSeatAsync(factory, busId, floor: 1, row: 1, column: 1, "A1");

        var client = ClientWith(factory, factory.CreateTokenFor(user));
        var response = await client.PostAsJsonAsync(
            CreateUrl, new { tripId = trip.Id, seatIds = new[] { seat.Id } });

        // Giữ ghế trên chuyến đã huỷ là giữ một chỗ không còn tồn tại: 409 chứ không phải 404 —
        // chuyến CÓ thật, chỉ là không còn nhận giữ ghế (cùng lối chặn 409 của ITripAssignmentService
        // khi đổi xe/đổi tài xế trên chuyến đã huỷ). Ghế seed hợp lệ để ca này chỉ còn một biến số.
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("Chuyến đã hủy, không thể giữ ghế", await MessageAsync(response));

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(0, await db.SeatHolds.CountAsync());
        Assert.Equal(0, await db.SeatHoldLogs.CountAsync());
    }

    [Fact]
    public async Task Chuyen_da_hoan_thanh_thi_tra_409()
    {
        using var factory = new TestAppFactory();
        var user = await SeedUserAsync(factory);
        var busId = Guid.NewGuid();
        var trip = await SeedTripAsync(factory, busId, TripStatus.Completed);
        var seat = await SeedSeatAsync(factory, busId, floor: 1, row: 1, column: 1, "A1");

        var client = ClientWith(factory, factory.CreateTokenFor(user));
        var response = await client.PostAsJsonAsync(
            CreateUrl, new { tripId = trip.Id, seatIds = new[] { seat.Id } });

        // Chuyến đã chạy xong cũng bị chặn — câu chữ RIÊNG để khách phân biệt được với chuyến bị huỷ.
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("Chuyến đã hoàn thành, không thể giữ ghế", await MessageAsync(response));
    }

    // ---------------------------------------------------------------------------------------
    // Ghế đang bị chặn — 409, chặn cả lô, không ghi gì
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Ghe_dang_duoc_phien_khac_giu_thi_tra_409_va_chan_ca_lo()
    {
        using var factory = new TestAppFactory();
        var chuPhienKhac = await SeedUserAsync(factory);
        var busId = Guid.NewGuid();
        var trip = await SeedTripAsync(factory, busId);
        var seatA1 = await SeedSeatAsync(factory, busId, floor: 1, row: 1, column: 1, "A1");
        var seatA2 = await SeedSeatAsync(factory, busId, floor: 1, row: 1, column: 2, "A2");

        // Phiên của người khác còn hạn, đang giữ A2; người gọi xin cả A1 lẫn A2.
        await SeedHoldAsync(
            factory, chuPhienKhac.Id, trip.Id, seatA2.Id, "PHIEN-NGUOI-KHAC",
            expiresAt: DateTime.UtcNow.AddMinutes(5));

        var client = await SignInAsPassengerAsync(factory);
        var response = await client.PostAsJsonAsync(
            CreateUrl, new { tripId = trip.Id, seatIds = new[] { seatA1.Id, seatA2.Id } });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        // Câu 409 kèm SỐ GHẾ VƯỚNG (A2 — ghế đang bị giữ), không kể A1 vừa trống vừa bị chặn theo
        // lô: khách nhìn màn hình chọn ghế là đối chiếu được ngay ghế nào phải đổi.
        Assert.Equal("Ghế A2 đang được giữ cho chuyến này.", await MessageAsync(response));

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Chặn CẢ LÔ: A1 còn trống nhưng cả request bị từ chối — chỉ chặn riêng A2 thì khách nhận
        // 409 kèm một phiên nửa vời không ai xin. Đúng 1 dòng cũ còn nguyên, không log mới.
        Assert.Equal(1, await db.SeatHolds.CountAsync());
        Assert.Equal(0, await db.SeatHoldLogs.CountAsync());
    }

    [Fact]
    public async Task Nhieu_ghe_cung_bi_giu_thi_thong_bao_liet_ke_het_theo_so_do()
    {
        using var factory = new TestAppFactory();
        var chuPhienKhac = await SeedUserAsync(factory);
        var busId = Guid.NewGuid();
        var trip = await SeedTripAsync(factory, busId);
        var seatA1 = await SeedSeatAsync(factory, busId, floor: 1, row: 1, column: 1, "A1");
        var seatA2 = await SeedSeatAsync(factory, busId, floor: 1, row: 1, column: 2, "A2");

        // Cả hai ghế đều bị giữ, seed NGƯỢC thứ tự sơ đồ (A2 trước A1) để chứng minh câu thông báo
        // sắp theo toạ độ (Floor → RowIndex → ColumnIndex) chứ không theo thứ tự tìm thấy trong CSDL.
        await SeedHoldAsync(factory, chuPhienKhac.Id, trip.Id, seatA2.Id, "PHIEN-NGUOI-KHAC-2");
        await SeedHoldAsync(factory, chuPhienKhac.Id, trip.Id, seatA1.Id, "PHIEN-NGUOI-KHAC-1");

        var client = await SignInAsPassengerAsync(factory);
        var response = await client.PostAsJsonAsync(
            CreateUrl, new { tripId = trip.Id, seatIds = new[] { seatA2.Id, seatA1.Id } });

        // Phép kiểm tầng service biết chính xác ghế nào vướng nên liệt kê đủ; chỉ nhánh thua cuộc
        // đua ở CSDL (DbUpdateException) mới phải dùng câu chung vì lúc đó không còn biết ghế nào.
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("Ghế A1, A2 đang được giữ cho chuyến này.", await MessageAsync(response));
    }

    [Fact]
    public async Task Ghe_vua_qua_han_ma_job_chua_quet_van_tra_409()
    {
        using var factory = new TestAppFactory();
        var chuPhienKhac = await SeedUserAsync(factory);
        var busId = Guid.NewGuid();
        var trip = await SeedTripAsync(factory, busId);
        var seat = await SeedSeatAsync(factory, busId, floor: 1, row: 1, column: 1, "A1");

        // Hạn đã qua nhưng status vẫn Holding — khe hở dưới một phút giữa hai lượt quét của
        // SeatHoldExpiryBackgroundService (docs/26 §4).
        await SeedHoldAsync(
            factory, chuPhienKhac.Id, trip.Id, seat.Id, "PHIEN-VUA-QUA-HAN",
            expiresAt: DateTime.UtcNow.AddMinutes(-1));

        var client = await SignInAsPassengerAsync(factory);
        var response = await client.PostAsJsonAsync(
            CreateUrl, new { tripId = trip.Id, seatIds = new[] { seat.Id } });

        // Phép kiểm CỐ Ý chỉ nhìn Status, không nhìn ExpiresAt — vì chốt chặn thật ở CSDL là partial
        // unique index (TripId, SeatId) WHERE Status = 'Holding', mà index không biết gì về thời
        // gian. Nếu phép kiểm bỏ qua dòng vừa quá hạn, lượt chèn sau đó vẫn đâm index và ra 409 qua
        // DbUpdateException: cùng một request, hai đường ra hai kết quả. Giữ hai bên cùng luật là
        // cách duy nhất để trước và sau luôn nhất quán; khe hở tối đa một phút tự khép ở lượt quét
        // kế tiếp.
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("Ghế A1 đang được giữ cho chuyến này.", await MessageAsync(response));
    }

    [Fact]
    public async Task Ghe_chinh_nguoi_goi_dang_giu_thi_tra_409()
    {
        using var factory = new TestAppFactory();
        var user = await SeedUserAsync(factory);
        var busId = Guid.NewGuid();
        var trip = await SeedTripAsync(factory, busId);
        var seat = await SeedSeatAsync(factory, busId, floor: 1, row: 1, column: 1, "A1");

        // Phiên CŨ của CHÍNH người gọi, còn Holding, cùng ghế — khách bấm "Giữ chỗ" lại lần hai.
        await SeedHoldAsync(factory, user.Id, trip.Id, seat.Id, "PHIEN-CU-CUA-TOI");

        var client = ClientWith(factory, factory.CreateTokenFor(user));
        var response = await client.PostAsJsonAsync(
            CreateUrl, new { tripId = trip.Id, seatIds = new[] { seat.Id } });

        // Quyết định có chủ đích, không phải sót: hai dòng Holding cùng (TripId, SeatId) là điều
        // partial unique index không cho phép, kể cả hai dòng cùng một người (docs/26 §3). Muốn giữ
        // tiếp thì gia hạn phiên cũ (POST .../extend) hoặc nhả rồi giữ lại. Ghim lại kẻo có người
        // "sửa" phép kiểm thành bỏ qua phiên của chính mình: ở PostgreSQL kết quả vẫn là 409 nhờ
        // index đỡ hộ, nhưng dưới InMemory ca này đỏ ngay vì không có index — hành vi phải giống
        // nhau ở cả hai môi trường, và 409 phải đến từ luật nghiệp vụ chứ không từ lỗi CSDL.
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("Ghế A1 đang được giữ cho chuyến này.", await MessageAsync(response));

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Không sinh phiên mới, không ghi log — 409 nghĩa là chưa có gì đổi.
        Assert.Equal(1, await db.SeatHolds.CountAsync());
        Assert.Equal(0, await db.SeatHoldLogs.CountAsync());
    }

    [Fact]
    public async Task Ghe_da_Expired_hoac_Released_thi_khong_chan_phien_moi()
    {
        using var factory = new TestAppFactory();
        var chuPhienKhac = await SeedUserAsync(factory);
        var busId = Guid.NewGuid();
        var trip = await SeedTripAsync(factory, busId);
        var seatA1 = await SeedSeatAsync(factory, busId, floor: 1, row: 1, column: 1, "A1");
        var seatA2 = await SeedSeatAsync(factory, busId, floor: 1, row: 1, column: 2, "A2");

        // Hai trạng thái "đã trả ghế về sơ đồ" (docs/26 §1): job đã quét, hoặc khách đã nhả.
        await SeedHoldAsync(
            factory, chuPhienKhac.Id, trip.Id, seatA1.Id, "PHIEN-HET-HAN",
            SeatHoldStatus.Expired, DateTime.UtcNow.AddMinutes(-5));
        await SeedHoldAsync(
            factory, chuPhienKhac.Id, trip.Id, seatA2.Id, "PHIEN-DA-NHA",
            SeatHoldStatus.Released, DateTime.UtcNow.AddMinutes(-5));

        var client = await SignInAsPassengerAsync(factory);
        var response = await client.PostAsJsonAsync(
            CreateUrl, new { tripId = trip.Id, seatIds = new[] { seatA1.Id, seatA2.Id } });

        // Chặn ở đây là ghế đã tự do mà không ai giữ được nữa — Holding là trạng thái DUY NHẤT
        // đang chặn ghế.
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await ReadJsonAsync(response);
        Assert.Equal(["A1", "A2"], SeatNumbersOf(body));
    }

    // ---------------------------------------------------------------------------------------
    // Tạo thành công — 201, cả nhóm dòng cùng phiên, log Held, không đụng phiên cũ
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Tao_thanh_cong_tra_201_dung_hinh_dang()
    {
        using var factory = new TestAppFactory();
        var user = await SeedUserAsync(factory);
        var busId = Guid.NewGuid();
        var trip = await SeedTripAsync(factory, busId);
        var seatA1 = await SeedSeatAsync(factory, busId, floor: 1, row: 1, column: 1, "A1");
        var seatA2 = await SeedSeatAsync(factory, busId, floor: 1, row: 1, column: 2, "A2");
        var seatB1 = await SeedSeatAsync(factory, busId, floor: 2, row: 1, column: 1, "B1");

        // Gửi ngược thứ tự (tầng trên trước) để chứng minh số ghế trong response được SẮP theo toạ
        // độ sơ đồ: đúng thứ tự vẽ của GET /trips/{id}/seats, không phải thứ tự client gửi lên.
        var client = ClientWith(factory, factory.CreateTokenFor(user));
        var before = DateTime.UtcNow;
        var response = await client.PostAsJsonAsync(
            CreateUrl, new { tripId = trip.Id, seatIds = new[] { seatB1.Id, seatA2.Id, seatA1.Id } });
        var after = DateTime.UtcNow;

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await ReadJsonAsync(response);
        var sessionCode = body.GetProperty("sessionCode").GetString()!;

        // Mã phiên do TẦNG SERVICE sinh (docs/26 §6): PHIEN- + 8 ký tự hex — màn hình chỉ hiển thị
        // và gửi lại, không tự sinh (cột SessionCode không unique: một phiên là một NHÓM dòng).
        Assert.Matches("^PHIEN-[0-9a-f]{8}$", sessionCode);

        Assert.Equal(trip.Id, body.GetProperty("tripId").GetGuid());
        Assert.Equal(["A1", "A2", "B1"], SeatNumbersOf(body));
        Assert.Equal("Holding", body.GetProperty("status").GetString());

        // Hạn = lúc tạo + 10 phút (US 3). Không so một mốc cứng vì đồng hồ chạy thật; kẹp giữa hai
        // mốc đo quanh lời gọi — đủ chặt để bắt lệch 5 phút lẫn 15 phút, đủ rộng để không chập chờn.
        var expiresAt = body.GetProperty("expiresAt").GetDateTime();
        Assert.InRange(expiresAt, before.AddMinutes(10).AddSeconds(-1), after.AddMinutes(10).AddSeconds(1));

        // Phiên vừa sinh chưa dùng lượt gia hạn nào (US 3: tối đa 1 lần) — nút "Gia hạn" phải sáng.
        Assert.True(body.GetProperty("canExtend").GetBoolean());
    }

    [Fact]
    public async Task Tao_ghi_mot_dong_SeatHold_cho_moi_ghe()
    {
        using var factory = new TestAppFactory();
        var user = await SeedUserAsync(factory);
        var busId = Guid.NewGuid();
        var trip = await SeedTripAsync(factory, busId);
        var seatA1 = await SeedSeatAsync(factory, busId, floor: 1, row: 1, column: 1, "A1");
        var seatA2 = await SeedSeatAsync(factory, busId, floor: 1, row: 1, column: 2, "A2");

        var client = ClientWith(factory, factory.CreateTokenFor(user));
        var body = await ReadJsonAsync(await client.PostAsJsonAsync(
            CreateUrl, new { tripId = trip.Id, seatIds = new[] { seatA1.Id, seatA2.Id } }));
        var sessionCode = body.GetProperty("sessionCode").GetString()!;

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rows = await db.SeatHolds.Where(h => h.SessionCode == sessionCode).ToListAsync();

        // docs/26 §1: một phiên là một NHÓM dòng — N ghế thì N dòng cùng SessionCode (cột
        // SessionCode KHÔNG unique, chính vì vậy), tất cả Holding và cùng một người giữ.
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r =>
        {
            Assert.Equal(SeatHoldStatus.Holding, r.Status);
            Assert.Equal(user.Id, r.UserId);
            Assert.Equal(trip.Id, r.TripId);

            // A4: dòng vừa tạo chưa sửa lần nào nên UpdatedAt còn null.
            Assert.Null(r.UpdatedAt);
        });

        // Cùng một mốc: ExpiresAt và CreatedAt của mọi dòng phải TRÙNG nhau — lệch vài mili giây
        // giữa các ghế trong cùng một phiên là thứ không giải thích được lúc tra vết.
        Assert.Single(rows.Select(r => r.ExpiresAt).Distinct());
        Assert.Single(rows.Select(r => r.CreatedAt).Distinct());
        Assert.NotEqual(default, rows[0].CreatedAt);

        // Hạn trong CSDL đúng bằng hạn trả về response — màn hình đếm ngược theo response, lệch
        // một bên là đồng hồ hiển thị sai.
        Assert.Equal(rows[0].ExpiresAt, body.GetProperty("expiresAt").GetDateTime());
    }

    [Fact]
    public async Task Tao_ghi_mot_dong_log_Held_cho_moi_ghe()
    {
        using var factory = new TestAppFactory();
        var user = await SeedUserAsync(factory);
        var busId = Guid.NewGuid();
        var trip = await SeedTripAsync(factory, busId);
        var seatA1 = await SeedSeatAsync(factory, busId, floor: 1, row: 1, column: 1, "A1");
        var seatA2 = await SeedSeatAsync(factory, busId, floor: 1, row: 1, column: 2, "A2");

        var client = ClientWith(factory, factory.CreateTokenFor(user));
        var body = await ReadJsonAsync(await client.PostAsJsonAsync(
            CreateUrl, new { tripId = trip.Id, seatIds = new[] { seatA1.Id, seatA2.Id } }));
        var sessionCode = body.GetProperty("sessionCode").GetString()!;

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var holds = await db.SeatHolds.Where(h => h.SessionCode == sessionCode).ToListAsync();
        var logs = await db.SeatHoldLogs.Where(l => l.SessionCode == sessionCode).ToListAsync();

        // docs/26 §6: mỗi lượt giữ vừa tạo một dòng log Held, trỏ về đúng dòng SeatHolds của nó —
        // bảng nhật ký chỉ có giá trị nếu mọi mốc vòng đời đều được ghi.
        Assert.Equal(2, logs.Count);
        Assert.All(logs, l =>
        {
            Assert.Equal(SeatHoldLogAction.Held, l.Action);
            Assert.Equal(user.Id, l.UserId);
            Assert.Contains(l.SeatHoldId, holds.Select(h => h.Id));
            Assert.NotEqual(default, l.CreatedAt);
        });
    }

    [Fact]
    public async Task Tao_phien_moi_khong_dung_toi_phien_cu_cua_nguoi_goi()
    {
        using var factory = new TestAppFactory();
        var user = await SeedUserAsync(factory);
        var busId = Guid.NewGuid();
        var trip = await SeedTripAsync(factory, busId);
        var seatA1 = await SeedSeatAsync(factory, busId, floor: 1, row: 1, column: 1, "A1");
        var seatA2 = await SeedSeatAsync(factory, busId, floor: 1, row: 1, column: 2, "A2");

        // Phiên CŨ của chính người gọi ở ghế khác — hành vi khi khách đổi ý chọn ghế khác rồi bấm
        // giữ lại (docs/26 §8: tạo phiên mới KHÔNG tự nhả phiên cũ).
        var phienCu = "PHIEN-CU-CUA-TOI";
        var holdCu = await SeedHoldAsync(factory, user.Id, trip.Id, seatA2.Id, phienCu);

        var client = ClientWith(factory, factory.CreateTokenFor(user));
        var response = await client.PostAsJsonAsync(
            CreateUrl, new { tripId = trip.Id, seatIds = new[] { seatA1.Id } });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await db.SeatHolds.SingleAsync(h => h.Id == holdCu.Id);

        // Phiên cũ giữ nguyên trạng thái lẫn hạn — không gia hạn, không nhả, không ghi log mới.
        // Việc dọn phiên cũ là của khách (gọi release) hoặc của job hết hạn, không phải của lượt tạo.
        Assert.Equal(SeatHoldStatus.Holding, stored.Status);
        Assert.Equal(holdCu.ExpiresAt, stored.ExpiresAt);
        Assert.Null(stored.UpdatedAt);
        Assert.Equal(0, await db.SeatHoldLogs.CountAsync(l => l.SessionCode == phienCu));
    }

    // ---------------------------------------------------------------------------------------
    // Hình dạng response — khoá lại đúng những gì docs/api-contract.md đã chốt (⛔5)
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Response_chi_tra_dung_cac_truong_trong_hop_dong()
    {
        using var factory = new TestAppFactory();
        var user = await SeedUserAsync(factory);
        var busId = Guid.NewGuid();
        var trip = await SeedTripAsync(factory, busId);
        var seat = await SeedSeatAsync(factory, busId, floor: 1, row: 1, column: 1, "A1");

        var client = ClientWith(factory, factory.CreateTokenFor(user));
        var body = await ReadJsonAsync(await client.PostAsJsonAsync(
            CreateUrl, new { tripId = trip.Id, seatIds = new[] { seat.Id } }));

        // Thêm một trường vào response là ĐỔI HÌNH DẠNG API: theo ⛔5 phải sửa api-contract.md
        // trước rồi báo người viết frontend. Ca này làm đổ test ngay lúc đó, để việc sửa hợp đồng
        // là một quyết định có ý thức chứ không phải một dòng code lỡ tay. SeatHoldSession là hình
        // dạng DÙNG CHUNG của cả bốn endpoint bề mặt /seat-holds — đổi ở đây là đổi cả bốn.
        Assert.Equal(
            ["canExtend", "expiresAt", "seatNumbers", "sessionCode", "status", "tripId"],
            PropertyNamesOf(body));
    }

    // ---------------------------------------------------------------------------------------
    // Helper — mỗi lớp test tự chép, không có lớp base chung (đúng lệ đang có của dự án)
    // ---------------------------------------------------------------------------------------

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
        => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task<string> MessageAsync(HttpResponseMessage response)
    {
        var body = await ReadJsonAsync(response);
        return body.GetProperty("message").GetString() ?? string.Empty;
    }

    private static string[] ErrorMessagesOf(JsonElement body, string field)
        => body.GetProperty("errors").GetProperty(field).EnumerateArray()
            .Select(message => message.GetString() ?? string.Empty)
            .ToArray();

    private static string[] PropertyNamesOf(JsonElement element)
        => element.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray();

    private static string[] SeatNumbersOf(JsonElement body)
        => body.GetProperty("seatNumbers").EnumerateArray()
            .Select(number => number.GetString() ?? string.Empty)
            .ToArray();

    private static async Task<HttpClient> SignInAsPassengerAsync(TestAppFactory factory)
    {
        var user = await SeedUserAsync(factory);

        return ClientWith(factory, factory.CreateTokenFor(user));
    }

    private static HttpClient ClientWith(TestAppFactory factory, string accessToken)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        return client;
    }

    /// <summary>
    /// Tài khoản hành khách kèm vai trò. Provider InMemory KHÔNG chạy <c>HasData</c> nên phải seed
    /// vai trò trước — không thì token hợp lệ mà người dùng trỏ tới vai trò không tồn tại.
    /// </summary>
    private static async Task<UserEntity> SeedUserAsync(TestAppFactory factory)
    {
        await factory.SeedAsync(db =>
        {
            if (!db.Roles.Any(r => r.Id == RoleIds.Passenger || r.Code == RoleCodes.Passenger))
            {
                db.Roles.Add(new Role { Id = RoleIds.Passenger, Code = RoleCodes.Passenger, Name = RoleCodes.Passenger });
            }
        });

        var user = new UserEntity
        {
            Id = Guid.NewGuid(),
            PhoneNumber = "09" + Random.Shared.Next(10_000_000, 99_999_999),
            FullName = "Hành Khách Test",
            PasswordHash = "hash-khong-dung-toi-trong-test",
            IsActive = true,
            RoleId = RoleIds.Passenger,
            UserRoles = [new UserRole { RoleId = RoleIds.Passenger }],
        };

        await factory.SeedAsync(db => db.Users.Add(user));

        return user;
    }

    /// <summary>
    /// Chuyến gắn với <paramref name="busId"/> — ca tạo phiên đối chiếu <c>Seat.BusId</c> với
    /// <c>Trip.BusId</c> nên chuyến và ghế phải cùng xe (khác lệ "seed dòng mồ côi" của hai file
    /// test gia hạn/nhả). Không cần seed Route/Bus thật: InMemory không cưỡng chế khoá ngoại.
    /// Mặc định <see cref="TripStatus.Scheduled"/> — đúng mặc định của entity nên các ca cũ không đổi
    /// kết quả; ca chặn 409 truyền <see cref="TripStatus.Cancelled"/>/<see cref="TripStatus.Completed"/>.
    /// </summary>
    private static async Task<Trip> SeedTripAsync(
        TestAppFactory factory,
        Guid busId,
        TripStatus status = TripStatus.Scheduled)
    {
        var trip = new Trip
        {
            Id = Guid.NewGuid(),
            RouteId = Guid.NewGuid(),
            BusId = busId,
            Status = status,
            DepartureTime = new DateTime(2026, 10, 1, 1, 0, 0, DateTimeKind.Utc),
        };

        await factory.SeedAsync(db => db.Trips.Add(trip));

        return trip;
    }

    private static async Task<Seat> SeedSeatAsync(
        TestAppFactory factory,
        Guid busId,
        int floor,
        int row,
        int column,
        string seatNumber)
    {
        var seat = new Seat
        {
            Id = Guid.NewGuid(),
            BusId = busId,
            SeatLayoutId = Guid.NewGuid(),
            Floor = floor,
            RowIndex = row,
            ColumnIndex = column,
            SeatNumber = seatNumber,
        };

        await factory.SeedAsync(db => db.Seats.Add(seat));

        return seat;
    }

    /// <summary>Lượt giữ chỗ seed sẵn — hạn mặc định còn hiệu lực để ca 409 phản ánh phiên "đang sống".</summary>
    private static async Task<SeatHold> SeedHoldAsync(
        TestAppFactory factory,
        Guid userId,
        Guid tripId,
        Guid seatId,
        string sessionCode,
        SeatHoldStatus status = SeatHoldStatus.Holding,
        DateTime? expiresAt = null)
    {
        var hold = new SeatHold
        {
            Id = Guid.NewGuid(),
            TripId = tripId,
            SeatId = seatId,
            UserId = userId,
            SessionCode = sessionCode,
            Status = status,
            ExpiresAt = expiresAt ?? DateTime.UtcNow.AddMinutes(5),
        };

        await factory.SeedAsync(db => db.SeatHolds.Add(hold));

        return hold;
    }
}
