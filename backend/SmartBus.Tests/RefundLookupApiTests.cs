using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using SmartBus.Api.Entities;
using RouteEntity = SmartBus.Api.Entities.Route;
using UserEntity = SmartBus.Api.Entities.User;

namespace SmartBus.Tests;

/// <summary>
/// Test tích hợp cho API tra cứu trạng thái hoàn tiền — <c>GET /api/refunds</c> và
/// <c>GET /api/refunds/{refundCode}</c> (US 8, task *"API tra cứu trạng thái hoàn tiền của giao dịch"*,
/// dòng 42 Sprint 4 — Vàng Thị Dăm). Hợp đồng đầy đủ ở mục "Hoàn tiền — /refunds" của
/// docs/api-contract.md.
///
/// Dùng lại <see cref="TestAppFactory"/> của JwtAuthTests: chạy trên app thật, mỗi test một CSDL
/// InMemory riêng. Endpoint yêu cầu đăng nhập nên mọi ca đều sign in trước (trừ ca 401).
/// Helper seed chép lại trong từng lớp test, không có lớp base chung (đúng lệ đang có của dự án).
///
/// ⚠️ Giới hạn của provider InMemory ảnh hưởng tới cách đọc kết quả ở đây:
///   • KHÔNG dựng khoá ngoại, nên seed được dòng Refund trỏ tới giao dịch/vé không tồn tại — không
///     ảnh hưởng endpoint này vì nó chỉ ĐỌC, và phép nối trong projection vẫn chạy trên InMemory.
///   • KHÔNG dựng unique index nào (kể cả <c>IX_Refunds_PaymentId_Active</c> và unique
///     <c>Refunds.RefundCode</c>): chốt "một giao dịch chỉ có một khoản hoàn chưa thất bại" ở tầng
///     PostgreSQL không có test nào phủ ở đây — đó là việc của migration (docs/35 §4.1).
///   • <c>COUNT</c> trên <c>RefundLogs</c> chạy được bình thường (đây là phép đếm trong bộ nhớ của
///     provider, không phải SQL), nên <c>retryCount</c> phủ được đầy đủ.
/// </summary>
public class RefundLookupApiTests
{
    private const string ListUrl = "/api/refunds";

    // ---------------------------------------------------------------------------------------
    // Xác thực
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Khong_gui_token_thi_tra_401()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(ListUrl)).StatusCode);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await client.GetAsync(DetailUrl("RF-BAT-KY"))).StatusCode);
    }

    // ---------------------------------------------------------------------------------------
    // Danh sách của tôi — mảng trần, không phân trang, rỗng cũng là 200
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Chua_co_khoan_hoan_nao_thi_tra_mang_rong_khong_phai_404()
    {
        using var factory = new TestAppFactory();
        var client = await ClientAsPassengerAsync(factory);

        var response = await client.GetAsync(ListUrl);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Array, body.RootElement.ValueKind);
        Assert.Equal(0, body.RootElement.GetArrayLength());
    }

    [Fact]
    public async Task Danh_sach_chi_tra_khoan_hoan_cua_chinh_nguoi_goi()
    {
        using var factory = new TestAppFactory();
        var chuKhoan = await SeedUserAsync(factory, "Nguyễn Văn Chủ");
        var nguoiKhac = await SeedUserAsync(factory, "Trần Thị Khác");
        var client = ClientWith(factory, factory.CreateTokenFor(chuKhoan));

        var (paymentCuaToi, _) = await SeedPaymentAsync(factory, chuKhoan.Id);
        var (paymentCuaNguoiKhac, _) = await SeedPaymentAsync(factory, nguoiKhac.Id);

        await SeedRefundAsync(factory, chuKhoan.Id, paymentCuaToi.Id,
            refundCode: "RF-MOI-NHAT", createdAt: Ngay(3));
        await SeedRefundAsync(factory, chuKhoan.Id, paymentCuaToi.Id,
            refundCode: "RF-CU-HON", createdAt: Ngay(1));
        await SeedRefundAsync(factory, nguoiKhac.Id, paymentCuaNguoiKhac.Id,
            refundCode: "RF-CUA-NGUOI-KHAC", createdAt: Ngay(2));

        var body = await ReadJsonAsync(await client.GetAsync(ListUrl));
        var items = body.EnumerateArray().ToArray();

        // Đúng hai khoản của chủ — khoản của người khác không lọt vào, và không có tham số nào hỏi
        // khoản của người khác được (phạm vi "của tôi" nằm trong truy vấn).
        Assert.Equal(2, items.Length);
        Assert.DoesNotContain(items, item => item.GetProperty("refundCode").GetString() == "RF-CUA-NGUOI-KHAC");

        // Service sắp CreatedAt giảm dần (rồi tới Id) để kết quả ổn định giữa các lượt gọi — hợp
        // đồng không hứa thứ tự, nhưng ca này ghim hành vi thật lại để ai đổi cũng thấy ngay.
        Assert.Equal("RF-MOI-NHAT", items[0].GetProperty("refundCode").GetString());
        Assert.Equal("RF-CU-HON", items[1].GetProperty("refundCode").GetString());
    }

    // ---------------------------------------------------------------------------------------
    // Lọc theo giao dịch — đây chính là câu "trạng thái hoàn tiền CỦA MỘT GIAO DỊCH" của task
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Loc_theo_paymentCode_chi_ra_khoan_cua_giao_dich_do()
    {
        using var factory = new TestAppFactory();
        var chuKhoan = await SeedUserAsync(factory, "Nguyễn Văn Chủ");
        var client = ClientWith(factory, factory.CreateTokenFor(chuKhoan));

        var (giaoDichA, _) = await SeedPaymentAsync(factory, chuKhoan.Id, paymentCode: "PM-0000000A");
        var (giaoDichB, _) = await SeedPaymentAsync(factory, chuKhoan.Id, paymentCode: "PM-0000000B");

        await SeedRefundAsync(factory, chuKhoan.Id, giaoDichA.Id, refundCode: "RF-CUA-A", status: RefundStatus.Failed);
        await SeedRefundAsync(factory, chuKhoan.Id, giaoDichB.Id, refundCode: "RF-CUA-B", status: RefundStatus.Succeeded);

        Assert.Equal(
            "Failed",
            await StatusOfAsync(client, "PM-0000000A"));
        Assert.Equal(
            "Succeeded",
            await StatusOfAsync(client, "PM-0000000B"));
    }

    [Fact]
    public async Task PaymentCode_cua_nguoi_khac_tra_mang_rong_khong_phai_404()
    {
        using var factory = new TestAppFactory();
        var chuKhoan = await SeedUserAsync(factory, "Nguyễn Văn Chủ");
        var nguoiKhac = await SeedUserAsync(factory, "Trần Thị Khác");

        var (giaoDichCuaNguoiKhac, _) = await SeedPaymentAsync(
            factory, nguoiKhac.Id, paymentCode: "PM-CUA-NGUOI-KHAC");
        await SeedRefundAsync(factory, nguoiKhac.Id, giaoDichCuaNguoiKhac.Id, refundCode: "RF-KHONG-PHAI-CUA-TOI");

        var client = ClientWith(factory, factory.CreateTokenFor(chuKhoan));
        var body = await ReadJsonAsync(await client.GetAsync(QueryUrl(paymentCode: "PM-CUA-NGUOI-KHAC")));

        // Giao dịch của người khác trả lời GIỐNG HỆT giao dịch không tồn tại — cùng một câu, cùng
        // một mã trạng thái. Khác nhau ở đây là để lộ cho người đang dò biết mã nào có thật.
        Assert.Equal(JsonValueKind.Array, body.ValueKind);
        Assert.Equal(0, body.GetArrayLength());
    }

    [Fact]
    public async Task PaymentCode_khong_ton_tai_thi_tra_mang_rong_khong_phai_404()
    {
        using var factory = new TestAppFactory();
        var client = await ClientAsPassengerAsync(factory);

        var response = await client.GetAsync(QueryUrl(paymentCode: "PM-KHONG-CO-THAT"));
        var body = await ReadJsonAsync(response);

        // 🔴 "Giao dịch này không phát sinh khoản hoàn nào" là câu trả lời thường gặp nhất — mọi
        // giao dịch thành công đều rơi vào ca đó. Hệ quả có chủ ý: endpoint không dùng được để dò
        // xem một mã giao dịch có tồn tại hay không.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, body.GetArrayLength());
    }

    [Fact]
    public async Task PaymentCode_phai_khop_chinh_xac_khong_bo_qua_hoa_thuong()
    {
        using var factory = new TestAppFactory();
        var chuKhoan = await SeedUserAsync(factory, "Nguyễn Văn Chủ");
        var client = ClientWith(factory, factory.CreateTokenFor(chuKhoan));

        var (giaoDich, _) = await SeedPaymentAsync(factory, chuKhoan.Id, paymentCode: "PM-ABC123");
        await SeedRefundAsync(factory, chuKhoan.Id, giaoDich.Id, refundCode: "RF-ABC123");

        // Mã giao dịch là mã máy sinh, tra bằng mắt — so khớp chính xác, cùng lối
        // SeatHoldLookupService (sessionCode). Không có trim, không có hạ hoa/thường.
        Assert.Equal(0, (await ReadJsonAsync(
            await client.GetAsync(QueryUrl(paymentCode: "pm-abc123")))).GetArrayLength());
        Assert.Equal(1, (await ReadJsonAsync(
            await client.GetAsync(QueryUrl(paymentCode: "PM-ABC123")))).GetArrayLength());
    }

    // ---------------------------------------------------------------------------------------
    // Lọc theo status — mã lạ là mảng rỗng, KHÔNG phải 400 (mục D3 của docs/03-quy-uoc.md)
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Status_go_sai_thi_tra_mang_rong_khong_phai_400()
    {
        using var factory = new TestAppFactory();
        var chuKhoan = await SeedUserAsync(factory, "Nguyễn Văn Chủ");
        var client = ClientWith(factory, factory.CreateTokenFor(chuKhoan));

        var (giaoDich, _) = await SeedPaymentAsync(factory, chuKhoan.Id);
        await SeedRefundAsync(factory, chuKhoan.Id, giaoDich.Id, refundCode: "RF-CO-THAT");

        var response = await client.GetAsync(QueryUrl(status: "DangXuLy"));

        // Mã trạng thái lạ có thể là trạng thái hợp lệ trong tương lai — 400 ở đây làm màn hình cũ
        // vỡ khi backend thêm giá trị mới.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, (await ReadJsonAsync(response)).GetArrayLength());
    }

    [Fact]
    public async Task Status_hop_le_thi_loc_dung_va_bo_qua_hoa_thuong()
    {
        using var factory = new TestAppFactory();
        var chuKhoan = await SeedUserAsync(factory, "Nguyễn Văn Chủ");
        var client = ClientWith(factory, factory.CreateTokenFor(chuKhoan));

        var (paymentA, _) = await SeedPaymentAsync(factory, chuKhoan.Id);
        var (paymentB, _) = await SeedPaymentAsync(factory, chuKhoan.Id);
        var (paymentC, _) = await SeedPaymentAsync(factory, chuKhoan.Id);
        await SeedRefundAsync(factory, chuKhoan.Id, paymentA.Id, refundCode: "RF-CHO", status: RefundStatus.Pending);
        await SeedRefundAsync(factory, chuKhoan.Id, paymentB.Id, refundCode: "RF-XONG", status: RefundStatus.Succeeded);
        await SeedRefundAsync(factory, chuKhoan.Id, paymentC.Id, refundCode: "RF-HONG", status: RefundStatus.Failed);

        var thatBai = await ReadJsonAsync(await client.GetAsync(QueryUrl(status: "Failed")));
        Assert.Equal("RF-HONG", Assert.Single(thatBai.EnumerateArray()).GetProperty("refundCode").GetString());

        // "succeeded" viết thường vẫn ra kết quả: đọc thì bỏ qua hoa/thường, còn giá trị LƯU xuống
        // CSDL luôn là tên chuẩn của enum (A3).
        var thanhCong = await ReadJsonAsync(await client.GetAsync(QueryUrl(status: "succeeded")));
        Assert.Equal("RF-XONG", Assert.Single(thanhCong.EnumerateArray()).GetProperty("refundCode").GetString());
    }

    [Fact]
    public async Task Loc_ket_hop_paymentCode_va_status()
    {
        using var factory = new TestAppFactory();
        var chuKhoan = await SeedUserAsync(factory, "Nguyễn Văn Chủ");
        var client = ClientWith(factory, factory.CreateTokenFor(chuKhoan));

        // Cùng một giao dịch có thể có NHIỀU dòng Refunds theo thời gian: khoản đầu Failed hết lịch
        // thử lại rồi bị bỏ, sau đó phát sinh khoản mới (chỉ mục duy nhất điều kiện chỉ chặn hai
        // khoản CHƯA thất bại cùng lúc). Vì vậy lọc theo mỗi paymentCode chưa đủ để trả lời "giờ ra
        // sao" — phải đọc status của từng dòng, ĐỪNG đếm dòng.
        var (giaoDich, _) = await SeedPaymentAsync(factory, chuKhoan.Id, paymentCode: "PM-HAI-LAN");
        await SeedRefundAsync(factory, chuKhoan.Id, giaoDich.Id, refundCode: "RF-LAN-DAU",
            status: RefundStatus.Failed, createdAt: Ngay(2));
        await SeedRefundAsync(factory, chuKhoan.Id, giaoDich.Id, refundCode: "RF-LAN-SAO",
            status: RefundStatus.Succeeded, createdAt: Ngay(3));

        var caHai = await ReadJsonAsync(await client.GetAsync(QueryUrl(paymentCode: "PM-HAI-LAN")));
        Assert.Equal(2, caHai.GetArrayLength());

        var chiXong = await ReadJsonAsync(
            await client.GetAsync(QueryUrl(paymentCode: "PM-HAI-LAN", status: "Succeeded")));
        Assert.Equal("RF-LAN-SAO", Assert.Single(chiXong.EnumerateArray()).GetProperty("refundCode").GetString());
    }

    // ---------------------------------------------------------------------------------------
    // Chặn độ dài tham số — đây là hai ca 400 duy nhất của bề mặt này
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task PaymentCode_qua_dai_tra_400()
    {
        using var factory = new TestAppFactory();
        var client = await ClientAsPassengerAsync(factory);

        var response = await client.GetAsync(QueryUrl(paymentCode: new string('P', 65)));
        var body = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Dữ liệu đầu vào không hợp lệ", body.GetProperty("message").GetString());
        Assert.Equal(
            "Mã giao dịch tối đa 64 ký tự",
            body.GetProperty("errors").GetProperty("paymentCode")[0].GetString());
    }

    [Fact]
    public async Task Status_qua_dai_tra_400()
    {
        using var factory = new TestAppFactory();
        var client = await ClientAsPassengerAsync(factory);

        var response = await client.GetAsync(QueryUrl(status: new string('S', 21)));
        var body = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            "Mã trạng thái tối đa 20 ký tự",
            body.GetProperty("errors").GetProperty("status")[0].GetString());
    }

    // ---------------------------------------------------------------------------------------
    // Tra một khoản theo mã hoàn tiền
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Tra_theo_refundCode_tra_dung_khoan()
    {
        using var factory = new TestAppFactory();
        var chuKhoan = await SeedUserAsync(factory, "Nguyễn Văn Chủ");
        var client = ClientWith(factory, factory.CreateTokenFor(chuKhoan));

        var route = await SeedRouteAsync(factory, "B10");
        var trip = await SeedTripAsync(factory, route.Id);
        var seat = await SeedSeatAsync(factory, "D02");
        var ticket = await SeedTicketAsync(factory, chuKhoan.Id, trip.Id, seat.Id, code: "SBT-B10-3E4F5A");
        var (payment, _) = await SeedPaymentAsync(factory, chuKhoan.Id, methodCode: "MoMo", amount: 15_000m);

        await SeedRefundAsync(factory, chuKhoan.Id, payment.Id,
            refundCode: "RF-TRA-CHI-TIET",
            source: RefundSource.TicketCancellation,
            status: RefundStatus.Succeeded,
            ticketId: ticket.Id,
            amount: 10_800m,
            gatewayTransactionId: "MOMO-REFUND-0001",
            processedAt: Ngay(4));

        var body = await ReadJsonAsync(await client.GetAsync(DetailUrl("RF-TRA-CHI-TIET")));

        Assert.Equal("RF-TRA-CHI-TIET", body.GetProperty("refundCode").GetString());
        Assert.Equal("TicketCancellation", body.GetProperty("source").GetString());
        Assert.Equal("Succeeded", body.GetProperty("status").GetString());
        Assert.Equal(10_800m, body.GetProperty("amount").GetDecimal());
        Assert.Equal("MOMO-REFUND-0001", body.GetProperty("gatewayTransactionId").GetString());
        Assert.Equal("B10", body.GetProperty("routeCode").GetString());
        Assert.Equal("D02", body.GetProperty("seatNumber").GetString());
    }

    [Fact]
    public async Task RefundCode_khong_ton_tai_tra_404()
    {
        using var factory = new TestAppFactory();
        var client = await ClientAsPassengerAsync(factory);

        var response = await client.GetAsync(DetailUrl("RF-KHONG-CO-THAT"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("hoàn tiền", await MessageAsync(response), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RefundCode_cua_nguoi_khac_tra_404_khong_phai_403()
    {
        using var factory = new TestAppFactory();
        var chuKhoan = await SeedUserAsync(factory, "Nguyễn Văn Chủ");
        var nguoiKhac = await SeedUserAsync(factory, "Trần Thị Khác");

        var (giaoDich, _) = await SeedPaymentAsync(factory, nguoiKhac.Id);
        await SeedRefundAsync(factory, nguoiKhac.Id, giaoDich.Id, refundCode: "RF-CUA-NGUOI-KHAC");

        var client = ClientWith(factory, factory.CreateTokenFor(chuKhoan));
        var response = await client.GetAsync(DetailUrl("RF-CUA-NGUOI-KHAC"));

        // 403 là xác nhận mã đó CÓ tồn tại — tức là để lộ thông tin cho người đang dò. Không tồn
        // tại và của người khác phải trả lời giống hệt nhau, cùng lối vé của tôi và phiên giữ chỗ.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("hoàn tiền", await MessageAsync(response), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RefundCode_phai_khop_chinh_xac_khong_bo_qua_hoa_thuong()
    {
        using var factory = new TestAppFactory();
        var chuKhoan = await SeedUserAsync(factory, "Nguyễn Văn Chủ");
        var client = ClientWith(factory, factory.CreateTokenFor(chuKhoan));

        var (giaoDich, _) = await SeedPaymentAsync(factory, chuKhoan.Id);
        await SeedRefundAsync(factory, chuKhoan.Id, giaoDich.Id, refundCode: "RF-HOA-THUONG");

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await client.GetAsync(DetailUrl("rf-hoa-thuong"))).StatusCode);
    }

    // ---------------------------------------------------------------------------------------
    // Hình dạng response — khoá lại đúng những gì docs/api-contract.md đã chốt (⛔5)
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Response_chi_tra_dung_cac_truong_trong_hop_dong()
    {
        using var factory = new TestAppFactory();
        var chuKhoan = await SeedUserAsync(factory, "Nguyễn Văn Chủ");
        var client = ClientWith(factory, factory.CreateTokenFor(chuKhoan));

        var route = await SeedRouteAsync(factory, "01");
        var trip = await SeedTripAsync(factory, route.Id);
        var seat = await SeedSeatAsync(factory, "A12");
        var ticket = await SeedTicketAsync(factory, chuKhoan.Id, trip.Id, seat.Id, code: "SBT1-AAA");
        var (payment, _) = await SeedPaymentAsync(factory, chuKhoan.Id);
        await SeedRefundAsync(factory, chuKhoan.Id, payment.Id, refundCode: "RF-KHOA-HINH-DANG", ticketId: ticket.Id);

        var body = await ReadJsonAsync(await client.GetAsync(ListUrl));
        var item = Assert.Single(body.EnumerateArray());

        // Thêm một trường vào response là ĐỔI HÌNH DẠNG API: theo ⛔5 phải sửa api-contract.md trước
        // rồi báo người viết frontend (cả hai màn hoàn tiền đã merge đều đọc đúng bộ tên này). Ca
        // này làm đổ test ngay lúc đó, để việc sửa hợp đồng là một quyết định có ý thức.
        Assert.Equal(
            [
                "amount", "createdAt", "departureTime", "destination", "failReason",
                "gatewayTransactionId", "id", "methodName", "origin", "passengerName",
                "paymentCode", "processedAt", "refundCode", "retryCount", "routeCode",
                "routeName", "seatNumber", "source", "status", "ticketCode", "updatedAt",
            ],
            PropertyNamesOf(item));
    }

    [Fact]
    public async Task Khoan_hoan_day_du_truong_hien_thi()
    {
        using var factory = new TestAppFactory();
        var chuKhoan = await SeedUserAsync(factory, "Nguyễn Văn Chủ");
        var client = ClientWith(factory, factory.CreateTokenFor(chuKhoan));

        var route = await SeedRouteAsync(factory, "B10");
        var trip = await SeedTripAsync(factory, route.Id, departure: Ngay(5));
        var seat = await SeedSeatAsync(factory, "D02");
        var ticket = await SeedTicketAsync(factory, chuKhoan.Id, trip.Id, seat.Id, code: "SBT-B10-3E4F5A");
        var (payment, _) = await SeedPaymentAsync(
            factory, chuKhoan.Id, paymentCode: "PM-3E4F5A", methodCode: "VNPay", amount: 15_000m);

        await SeedRefundAsync(factory, chuKhoan.Id, payment.Id,
            refundCode: "RF-DAY-DU",
            source: RefundSource.TicketCancellation,
            status: RefundStatus.Succeeded,
            ticketId: ticket.Id,
            amount: 10_800m,
            gatewayTransactionId: "VNPAY-REFUND-77",
            processedAt: Ngay(6),
            createdAt: Ngay(5),
            updatedAt: Ngay(6));

        var refund = Assert.Single(
            (await ReadJsonAsync(await client.GetAsync(ListUrl))).EnumerateArray());

        // Tên tuyến / điểm đi / điểm đến / giờ chạy / số ghế nối sẵn ở tầng CSDL để màn "Theo dõi
        // hoàn tiền" vẽ một dòng mà không phải gọi thêm endpoint nào — cùng lối bảng vé của tôi.
        Assert.Equal("B10", refund.GetProperty("routeCode").GetString());
        Assert.Equal("Tuyến B10", refund.GetProperty("routeName").GetString());
        Assert.Equal("Bến Thành", refund.GetProperty("origin").GetString());
        Assert.Equal("Chợ Lớn", refund.GetProperty("destination").GetString());
        Assert.Equal(Ngay(5), refund.GetProperty("departureTime").GetDateTime());
        Assert.Equal("D02", refund.GetProperty("seatNumber").GetString());
        Assert.Equal("SBT-B10-3E4F5A", refund.GetProperty("ticketCode").GetString());

        // Người NHẬN LẠI tiền, lấy từ Users.FullName — màn hành khách không hiện nhưng vẫn nhận, vì
        // hai màn dùng chung một hình dạng.
        Assert.Equal("Nguyễn Văn Chủ", refund.GetProperty("passengerName").GetString());

        // Mã phương thức là MÃ của hợp đồng (Payments.MethodCode), không phải nhãn tiếng Việt.
        Assert.Equal("VNPay", refund.GetProperty("methodName").GetString());
        Assert.Equal("PM-3E4F5A", refund.GetProperty("paymentCode").GetString());

        Assert.Equal("TicketCancellation", refund.GetProperty("source").GetString());
        Assert.Equal("Succeeded", refund.GetProperty("status").GetString());
        Assert.Equal(10_800m, refund.GetProperty("amount").GetDecimal());
        Assert.Equal(Ngay(5), refund.GetProperty("createdAt").GetDateTime());
        Assert.Equal(Ngay(6), refund.GetProperty("processedAt").GetDateTime());
        Assert.Equal(Ngay(6), refund.GetProperty("updatedAt").GetDateTime());
        Assert.Equal("VNPAY-REFUND-77", refund.GetProperty("gatewayTransactionId").GetString());

        // Bất biến: FailReason chỉ khác null khi status là Failed — khoản đã hoàn thì lý do cũ đã bị
        // xoá khi lật trạng thái.
        Assert.Equal(JsonValueKind.Null, refund.GetProperty("failReason").ValueKind);
    }

    [Fact]
    public async Task Khoan_PaymentFailure_khong_co_ve_thi_bay_truong_suy_tu_ve_deu_null()
    {
        using var factory = new TestAppFactory();
        var chuKhoan = await SeedUserAsync(factory, "Nguyễn Văn Chủ");
        var client = ClientWith(factory, factory.CreateTokenFor(chuKhoan));

        // Nhánh US 8 nêu ra ĐẦU TIÊN: giao dịch hỏng nhưng cổng đã giữ tiền → hoàn lại. Vé chỉ phát
        // hành sau khi tiền về nên khoản này KHÔNG có vé, và TicketId để null.
        var (payment, _) = await SeedPaymentAsync(
            factory, chuKhoan.Id, paymentCode: "PM-GIAO-DICH-HONG", methodCode: "MoMo", amount: 12_000m);

        await SeedRefundAsync(factory, chuKhoan.Id, payment.Id,
            refundCode: "RF-GIAO-DICH-HONG",
            source: RefundSource.PaymentFailure,
            status: RefundStatus.Failed,
            ticketId: null,
            amount: 12_000m,
            failReason: "Cổng từ chối: mã giao dịch gốc không tồn tại");

        var refund = Assert.Single(
            (await ReadJsonAsync(await client.GetAsync(ListUrl))).EnumerateArray());

        // Bảy trường suy từ vé null CÙNG LÚC. 🔴 Cố ý KHÔNG trả chuỗi rỗng: departureTime: "" đi qua
        // new Date("") ra Invalid Date — màn hình hiện NaN chứ không hiện ô trống.
        foreach (var field in new[]
                 {
                     "ticketCode", "routeCode", "routeName", "origin", "destination",
                     "departureTime", "seatNumber",
                 })
        {
            Assert.Equal(JsonValueKind.Null, refund.GetProperty(field).ValueKind);
        }

        // Ba trường CÒN LẠI của khoản hoàn vẫn có giá trị đầy đủ — chúng không suy từ vé.
        Assert.Equal("MoMo", refund.GetProperty("methodName").GetString());
        Assert.Equal("PM-GIAO-DICH-HONG", refund.GetProperty("paymentCode").GetString());
        Assert.Equal("Nguyễn Văn Chủ", refund.GetProperty("passengerName").GetString());
        Assert.Equal(12_000m, refund.GetProperty("amount").GetDecimal());

        Assert.Equal("PaymentFailure", refund.GetProperty("source").GetString());
        Assert.Equal("Failed", refund.GetProperty("status").GetString());
        Assert.Equal(
            "Cổng từ chối: mã giao dịch gốc không tồn tại",
            refund.GetProperty("failReason").GetString());
        Assert.Equal(JsonValueKind.Null, refund.GetProperty("processedAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, refund.GetProperty("updatedAt").ValueKind);
    }

    // ---------------------------------------------------------------------------------------
    // retryCount — phép ĐẾM trên RefundLogs, không phải cột của Refunds
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task RetryCount_bang_0_khi_moi_co_luot_goi_dau()
    {
        using var factory = new TestAppFactory();
        var chuKhoan = await SeedUserAsync(factory, "Nguyễn Văn Chủ");
        var client = ClientWith(factory, factory.CreateTokenFor(chuKhoan));

        var (payment, _) = await SeedPaymentAsync(factory, chuKhoan.Id);
        var refund = await SeedRefundAsync(factory, chuKhoan.Id, payment.Id, refundCode: "RF-MOT-LAN");
        await SeedRefundLogAsync(factory, refund.Id, RefundLogTrigger.Initial, RefundLogResult.Failed);

        // Lượt gọi ĐẦU TIÊN không tính là "thử lại" — dòng vừa tạo trong bản nháp FE có retryCount = 0.
        Assert.Equal(0, await RetryCountOfAsync(client));
    }

    [Fact]
    public async Task RetryCount_chi_dem_luot_goi_lai_khong_dem_luot_dau()
    {
        using var factory = new TestAppFactory();
        var chuKhoan = await SeedUserAsync(factory, "Nguyễn Văn Chủ");
        var client = ClientWith(factory, factory.CreateTokenFor(chuKhoan));

        var (payment, _) = await SeedPaymentAsync(factory, chuKhoan.Id);
        var refund = await SeedRefundAsync(factory, chuKhoan.Id, payment.Id, refundCode: "RF-GOI-LAI");

        await SeedRefundLogAsync(factory, refund.Id, RefundLogTrigger.Initial, RefundLogResult.Failed);
        await SeedRefundLogAsync(factory, refund.Id, RefundLogTrigger.ScheduledRetry, RefundLogResult.Failed);
        await SeedRefundLogAsync(factory, refund.Id, RefundLogTrigger.ManualRetry, RefundLogResult.Failed);
        await SeedRefundLogAsync(factory, refund.Id, RefundLogTrigger.ManualRetry, RefundLogResult.Succeeded);

        // Refunds CỐ Ý không có cột đếm (docs/35 §2.4): 4 dòng nhật ký, 1 lượt Initial → 3 lượt gọi
        // lại, và khoản này đã thành công ở lượt cuối.
        Assert.Equal(3, await RetryCountOfAsync(client));
    }

    [Fact]
    public async Task RetryCount_chi_dem_log_cua_chinh_khoan_do()
    {
        using var factory = new TestAppFactory();
        var chuKhoan = await SeedUserAsync(factory, "Nguyễn Văn Chủ");
        var client = ClientWith(factory, factory.CreateTokenFor(chuKhoan));

        var (paymentA, _) = await SeedPaymentAsync(factory, chuKhoan.Id);
        var (paymentB, _) = await SeedPaymentAsync(factory, chuKhoan.Id);
        var refundA = await SeedRefundAsync(factory, chuKhoan.Id, paymentA.Id,
            refundCode: "RF-A", createdAt: Ngay(3));
        var refundB = await SeedRefundAsync(factory, chuKhoan.Id, paymentB.Id,
            refundCode: "RF-B", createdAt: Ngay(2));

        await SeedRefundLogAsync(factory, refundA.Id, RefundLogTrigger.Initial, RefundLogResult.Succeeded);
        await SeedRefundLogAsync(factory, refundB.Id, RefundLogTrigger.Initial, RefundLogResult.Failed);
        await SeedRefundLogAsync(factory, refundB.Id, RefundLogTrigger.ManualRetry, RefundLogResult.Failed);

        var items = (await ReadJsonAsync(await client.GetAsync(ListUrl))).EnumerateArray().ToArray();

        // Phép đếm phải khoá theo RefundId — nếu quên, khoản RF-A sẽ báo 1 lượt gọi lại của khoản B.
        Assert.Equal(0, items[0].GetProperty("retryCount").GetInt32());
        Assert.Equal(1, items[1].GetProperty("retryCount").GetInt32());
    }

    // ---------------------------------------------------------------------------------------
    // Helper — mỗi lớp test tự chép, không có lớp base chung (đúng lệ đang có của dự án)
    // ---------------------------------------------------------------------------------------

    private static string DetailUrl(string refundCode) => $"{ListUrl}/{refundCode}";

    private static string QueryUrl(string? paymentCode = null, string? status = null)
    {
        var parts = new List<string>();

        if (paymentCode is not null)
        {
            parts.Add($"paymentCode={Uri.EscapeDataString(paymentCode)}");
        }

        if (status is not null)
        {
            parts.Add($"status={Uri.EscapeDataString(status)}");
        }

        return parts.Count == 0 ? ListUrl : $"{ListUrl}?{string.Join('&', parts)}";
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
        => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task<string> MessageAsync(HttpResponseMessage response)
    {
        var body = await ReadJsonAsync(response);
        return body.GetProperty("message").GetString() ?? string.Empty;
    }

    private static string[] PropertyNamesOf(JsonElement element)
        => element.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray();

    /// <summary>Trạng thái của khoản hoàn duy nhất gắn với một mã giao dịch.</summary>
    private static async Task<string> StatusOfAsync(HttpClient client, string paymentCode)
    {
        var body = await ReadJsonAsync(await client.GetAsync(QueryUrl(paymentCode: paymentCode)));

        return Assert.Single(body.EnumerateArray()).GetProperty("status").GetString() ?? string.Empty;
    }

    /// <summary>retryCount của khoản hoàn duy nhất trong danh sách của người gọi.</summary>
    private static async Task<int> RetryCountOfAsync(HttpClient client)
    {
        var body = await ReadJsonAsync(await client.GetAsync(ListUrl));

        return Assert.Single(body.EnumerateArray()).GetProperty("retryCount").GetInt32();
    }

    private static async Task<HttpClient> ClientAsPassengerAsync(TestAppFactory factory)
        => ClientWith(factory, factory.CreateTokenFor(await SeedUserAsync(factory, "Hành Khách Test")));

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
    private static async Task<UserEntity> SeedUserAsync(TestAppFactory factory, string fullName)
    {
        var role = new Role { Id = RoleIds.Passenger, Code = RoleCodes.Passenger, Name = RoleCodes.Passenger };

        await factory.SeedAsync(db =>
        {
            if (!db.Roles.Any(r => r.Id == role.Id || r.Code == role.Code))
            {
                db.Roles.Add(role);
            }
        });

        var user = new UserEntity
        {
            Id = Guid.NewGuid(),
            PhoneNumber = "09" + Random.Shared.Next(10_000_000, 99_999_999),
            FullName = fullName,
            PasswordHash = "hash-khong-dung-toi-trong-test",
            IsActive = true,
            RoleId = role.Id,
            UserRoles = [new UserRole { RoleId = role.Id }],
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

    private static async Task<Seat> SeedSeatAsync(TestAppFactory factory, string seatNumber)
    {
        var seat = new Seat
        {
            Id = Guid.NewGuid(),
            BusId = Guid.NewGuid(),
            SeatLayoutId = Guid.NewGuid(),
            Floor = 1,
            RowIndex = 1,
            ColumnIndex = 1,
            SeatNumber = seatNumber,
        };

        await factory.SeedAsync(db => db.Seats.Add(seat));

        return seat;
    }

    /// <summary>
    /// Chuyến chỉ có khoá ngoại, không gán navigation — InMemory không cưỡng chế khoá ngoại nên
    /// không cần seed Bus cho từng chuyến (đúng lệ "seed dòng mồ côi" của MyTicketsApiTests).
    /// </summary>
    private static async Task<Trip> SeedTripAsync(TestAppFactory factory, Guid routeId, DateTime? departure = null)
    {
        var trip = new Trip
        {
            Id = Guid.NewGuid(),
            RouteId = routeId,
            BusId = Guid.NewGuid(),
            DepartureTime = departure ?? Ngay(1),
        };

        await factory.SeedAsync(db => db.Trips.Add(trip));

        return trip;
    }

    private static async Task<Ticket> SeedTicketAsync(
        TestAppFactory factory,
        Guid userId,
        Guid tripId,
        Guid seatId,
        string code)
    {
        var ticket = new Ticket
        {
            Id = Guid.NewGuid(),
            Code = code,
            TripId = tripId,
            SeatId = seatId,
            UserId = userId,
            Price = 15_000m,
            Status = TicketStatus.Paid,
            CreatedAt = Ngay(1),
        };

        await factory.SeedAsync(db => db.Tickets.Add(ticket));

        return ticket;
    }

    /// <summary>Giao dịch gốc — trả về cả dòng vừa seed để lấy Id làm khoá ngoại cho khoản hoàn.</summary>
    private static async Task<(Payment Payment, string PaymentCode)> SeedPaymentAsync(
        TestAppFactory factory,
        Guid userId,
        string? paymentCode = null,
        string methodCode = "MoMo",
        decimal amount = 12_000m)
    {
        var code = paymentCode ?? "PM-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

        var payment = new Payment
        {
            Id = Guid.NewGuid(),
            PaymentCode = code,
            MethodCode = methodCode,
            Amount = amount,
            Status = PaymentStatus.Success,
            UserId = userId,
            TripId = Guid.NewGuid(),
            SeatNumbers = "A1",
            CreatedAt = Ngay(1),
        };

        await factory.SeedAsync(db => db.Payments.Add(payment));

        return (payment, code);
    }

    private static async Task<Refund> SeedRefundAsync(
        TestAppFactory factory,
        Guid userId,
        Guid paymentId,
        string refundCode,
        RefundSource source = RefundSource.PaymentFailure,
        RefundStatus status = RefundStatus.Pending,
        Guid? ticketId = null,
        decimal amount = 12_000m,
        string? gatewayTransactionId = null,
        string? failReason = null,
        DateTime? processedAt = null,
        DateTime? createdAt = null,
        DateTime? updatedAt = null)
    {
        var refund = new Refund
        {
            Id = Guid.NewGuid(),
            RefundCode = refundCode,
            Source = source,
            PaymentId = paymentId,
            TicketId = ticketId,
            UserId = userId,
            Amount = amount,
            Status = status,
            GatewayTransactionId = gatewayTransactionId,
            FailReason = failReason,
            ProcessedAt = processedAt,
            CreatedAt = createdAt ?? Ngay(1),
            UpdatedAt = updatedAt,
        };

        await factory.SeedAsync(db => db.Refunds.Add(refund));

        return refund;
    }

    private static async Task SeedRefundLogAsync(
        TestAppFactory factory,
        Guid refundId,
        RefundLogTrigger trigger,
        RefundLogResult result)
    {
        var log = new RefundLog
        {
            Id = Guid.NewGuid(),
            RefundId = refundId,
            Trigger = trigger,
            Result = result,
            CreatedAt = Ngay(1),
        };

        await factory.SeedAsync(db => db.RefundLogs.Add(log));
    }

    /// <summary>Mốc UTC trong quá khứ xa — `day` càng lớn càng gần hiện tại.</summary>
    private static DateTime Ngay(int day)
        => new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc).AddDays(day);
}
