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
/// Test API gửi yêu cầu đổi chuyến — US 5, task *"API gửi yêu cầu đổi chuyến + tính chênh lệch
/// giá"* (Nguyễn Duy Kiên, dòng 4 Sprint 4). Hợp đồng: docs/api-contract.md, mục "Gửi yêu cầu đổi
/// chuyến".
///
/// File này phủ ba tầng:
///   1. <b>Hành vi ghi</b> — gửi thành công thì có đúng MỘT dòng <c>TicketChanges.Pending</c>, vé
///      gốc KHÔNG bị đụng (vẫn <c>Paid</c>), và KHÔNG có <c>SeatHold</c> nào được tạo (ghế mới
///      không được giữ — docs/32 §3.3).
///   2. <b>Tính chênh lệch giá</b> — <c>priceDifference = newPrice - price</c>, CÓ DẤU: dương =
///      khách trả thêm, âm = nhà xe hoàn lại, 0 = ngang giá. Đây là con số duy nhất của nghiệp vụ
///      này mà FE hiển thị, và nó KHÔNG có cột trong CSDL (docs/32 §6).
///   3. <b>Ma trận lỗi</b> — mọi nhánh 404/409 còn phải chứng minh <b>không ghi gì</b>: không có
///      dòng yêu cầu nào được tạo, vé vẫn <c>Paid</c>.
///
/// ⚠️ <b>InMemory KHÔNG cưỡng chế unique index</b> (docs/32 §4.4), nên KHÔNG có ca nào kiểu "gửi
/// hai yêu cầu cùng lúc rồi mong cái thứ hai lỗi" ở đây — ca đó sẽ xanh kể cả khi
/// <c>IX_TicketChanges_TicketId_Pending</c> bị xoá khỏi model, tức là một test nói dối. Ràng buộc
/// liên bảng và chỉ mục điều kiện đã được ghim ở tầng DDL bởi
/// <see cref="TicketRequestReviewApiTests"/> (Phần 5). Ở đây chỉ kiểm phép kiểm MÔ TẢ của service —
/// phép kiểm thật sự chạy được trên mọi provider.
///
/// Khoá ký QR là khoá GIẢ rõ ràng — không phải khoá thật (luật 2 của nhóm).
/// </summary>
public class TicketChangeSubmissionApiTests
{
    private const string QrSigningKey = "khoa-ky-gia-cua-test-dai-hon-32-ky-tu-abc";

    private const string Url = "/api/ticket-changes";

    private static readonly DateTime Moc1 = new(2026, 10, 1, 1, 0, 0, DateTimeKind.Utc);

    // Câu trả về của service, ghim nguyên văn: màn hình hiển thị thẳng chuỗi này cho khách.
    private const string KhongTimThayVe = "Không tìm thấy vé.";

    private const string VeKhongConHieuLuc =
        "Vé này không còn hiệu lực để đổi (đã bị huỷ, hoặc đã được soát vé ở cổng).";

    private const string DangCoYeuCauCho =
        "Vé này đang có yêu cầu huỷ hoặc đổi chờ duyệt. Vui lòng chờ quản lý xử lý trước khi gửi yêu cầu mới.";

    private const string KhongTimThayChuyen = "Không tìm thấy chuyến xe.";

    private const string ChuyenKhongNhanDoi =
        "Chuyến khách muốn đổi sang đã khởi hành hoặc không còn nhận đặt vé. Hãy chọn chuyến khác.";

    private const string KhongTimThayGhe = "Không tìm thấy ghế trong chuyến đã chọn.";

    private const string GheDaCoNguoi = "Ghế đã có người đặt. Hãy chọn ghế khác.";

    private const string ChuaCoGia =
        "Tuyến của chuyến khách muốn đổi sang chưa có giá vé. Hãy chọn chuyến khác.";

    // ===========================================================================================
    // 1. LUỒNG THÀNH CÔNG — ghi đúng một dòng Pending, không đụng vé, không giữ ghế
    // ===========================================================================================

    [Fact]
    public async Task Gui_yeu_cau_doi_chuyen_ghi_dong_pending_va_khong_dung_den_ve_goc()
    {
        using var factory = NewFactory();
        var khach = await SeedKhachAsync(factory);

        var (chuyenMoi, gheMoi, _) = NewChuyenVaGhe();
        var veGoc = NewPaidTicket(khach.Id, 250_000m);

        await SeedAsync(factory, db =>
        {
            db.Tickets.Add(veGoc);
            db.Trips.Add(chuyenMoi);
            db.Seats.Add(gheMoi);
            db.Fares.Add(NewFare(chuyenMoi.RouteId, 300_000m));
        });

        using var client = AuthClientFor(factory, khach);

        var response = await client.PostAsJsonAsync(Url, new
        {
            ticketId = veGoc.Id,
            newTripId = chuyenMoi.Id,
            newSeatId = gheMoi.Id,
            reason = "  Có việc gia đình, muốn đi chuyến sớm hơn.  ",
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await ReadJsonAsync(response);

        Assert.Equal("Change", body.GetProperty("type").GetString());
        Assert.Equal("Pending", body.GetProperty("status").GetString());
        Assert.Equal(veGoc.Id, body.GetProperty("ticketId").GetGuid());
        Assert.Equal(veGoc.Code, body.GetProperty("ticketCode").GetString());
        Assert.Equal(250_000m, body.GetProperty("price").GetDecimal());
        Assert.Equal(300_000m, body.GetProperty("newPrice").GetDecimal());

        // 🔴 Dương = khách trả thêm. Đây là phép trừ của hai con số BẤT BIẾN, không phải cột.
        Assert.Equal(50_000m, body.GetProperty("priceDifference").GetDecimal());

        // Lý do được cắt khoảng trắng thừa.
        Assert.Equal("Có việc gia đình, muốn đi chuyến sớm hơn.", body.GetProperty("reason").GetString());

        // Trường của nửa duyệt còn null, và trường của loại "Cancel" có mặt + null (không lược bỏ).
        Assert.Equal(JsonValueKind.Null, body.GetProperty("newTicketId").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("reviewedByUserId").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("cancellationFee").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("refundAmount").ValueKind);

        var sau = await QueryAsync(factory, db => db.TicketChanges.AsNoTracking()
            .SingleAsync(c => c.TicketId == veGoc.Id));

        Assert.Equal(TicketChangeStatus.Pending, sau.Status);
        Assert.Equal(khach.Id, sau.UserId);
        Assert.Equal(chuyenMoi.Id, sau.NewTripId);
        Assert.Equal(gheMoi.Id, sau.NewSeatId);
        Assert.Equal(300_000m, sau.NewPrice);
        Assert.Null(sau.NewTicketId);
        Assert.Null(sau.ReviewedAt);
        Assert.Null(sau.UpdatedAt);

        // Vé gốc KHÔNG bị đụng — khách còn nguyên chuyến cũ cho tới khi quản lý duyệt.
        var veSau = await QueryAsync(factory, db => db.Tickets.AsNoTracking()
            .SingleAsync(t => t.Id == veGoc.Id));

        Assert.Equal(TicketStatus.Paid, veSau.Status);
        Assert.Null(veSau.UpdatedAt);

        // Ghế mới KHÔNG được giữ — chủ ý của thiết kế (docs/32 §3.3).
        var soLanGiuGhe = await QueryAsync(factory, db => db.SeatHolds
            .CountAsync(h => h.TripId == chuyenMoi.Id && h.SeatId == gheMoi.Id));

        Assert.Equal(0, soLanGiuGhe);
    }

    /// <summary>
    /// Chuyến mới RẺ hơn vé gốc: chênh lệch phải âm (nhà xe hoàn lại), không được lấy trị tuyệt đối
    /// hay ép về 0. Dùng số lẻ ở cả hai vế để một phép trừ sai kiểu (double, số nguyên) lộ ra.
    /// </summary>
    [Fact]
    public async Task Chuyen_moi_re_hon_thi_chenh_lech_am_va_giu_hai_chu_so_thap_phan()
    {
        using var factory = NewFactory();
        var khach = await SeedKhachAsync(factory);

        var (chuyenMoi, gheMoi, _) = NewChuyenVaGhe();
        var veGoc = NewPaidTicket(khach.Id, 250_000.00m);

        await SeedAsync(factory, db =>
        {
            db.Tickets.Add(veGoc);
            db.Trips.Add(chuyenMoi);
            db.Seats.Add(gheMoi);
            db.Fares.Add(NewFare(chuyenMoi.RouteId, 199_999.99m));
        });

        using var client = AuthClientFor(factory, khach);

        var response = await client.PostAsJsonAsync(Url, new
        {
            ticketId = veGoc.Id,
            newTripId = chuyenMoi.Id,
            newSeatId = gheMoi.Id,
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await ReadJsonAsync(response);

        Assert.Equal(199_999.99m, body.GetProperty("newPrice").GetDecimal());
        Assert.Equal(-50_000.01m, body.GetProperty("priceDifference").GetDecimal());
    }

    /// <summary>Đổi ngang giá: chênh lệch đúng <c>0</c>, không phải null — null có nghĩa "không áp dụng".</summary>
    [Fact]
    public async Task Doi_ngang_gia_thi_chenh_lech_bang_khong()
    {
        using var factory = NewFactory();
        var khach = await SeedKhachAsync(factory);

        var (chuyenMoi, gheMoi, _) = NewChuyenVaGhe();
        var veGoc = NewPaidTicket(khach.Id, 250_000m);

        await SeedAsync(factory, db =>
        {
            db.Tickets.Add(veGoc);
            db.Trips.Add(chuyenMoi);
            db.Seats.Add(gheMoi);
            db.Fares.Add(NewFare(chuyenMoi.RouteId, 250_000m));
        });

        using var client = AuthClientFor(factory, khach);

        var response = await client.PostAsJsonAsync(Url, new
        {
            ticketId = veGoc.Id,
            newTripId = chuyenMoi.Id,
            newSeatId = gheMoi.Id,
        });

        var body = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(0m, body.GetProperty("priceDifference").GetDecimal());
    }

    // ===========================================================================================
    // 2. VÉ — sở hữu và trạng thái
    // ===========================================================================================

    [Fact]
    public async Task Ve_cua_nguoi_khac_tra_404_va_khong_ghi_gi()
    {
        using var factory = NewFactory();
        var khach = await SeedKhachAsync(factory);
        var nguoiKhac = await SeedKhachAsync(factory, "Người Khác");

        var (chuyenMoi, gheMoi, _) = NewChuyenVaGhe();
        var veCuaNguoiKhac = NewPaidTicket(nguoiKhac.Id, 250_000m);

        await SeedAsync(factory, db =>
        {
            db.Tickets.Add(veCuaNguoiKhac);
            db.Trips.Add(chuyenMoi);
            db.Seats.Add(gheMoi);
            db.Fares.Add(NewFare(chuyenMoi.RouteId, 300_000m));
        });

        using var client = AuthClientFor(factory, khach);

        var response = await client.PostAsJsonAsync(Url, new
        {
            ticketId = veCuaNguoiKhac.Id,
            newTripId = chuyenMoi.Id,
            newSeatId = gheMoi.Id,
        });

        // 404 chứ không phải 403 — không tiết lộ vé có tồn tại (docs/28 §6).
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(KhongTimThayVe, MessageOf(await ReadJsonAsync(response)));

        Assert.Equal(0, await QueryAsync(factory, db => db.TicketChanges.CountAsync()));
    }

    [Theory]
    [InlineData(TicketStatus.Used)]
    [InlineData(TicketStatus.Cancelled)]
    public async Task Ve_khong_con_paid_tra_409_va_khong_ghi_gi(TicketStatus trangThai)
    {
        using var factory = NewFactory();
        var khach = await SeedKhachAsync(factory);

        var (chuyenMoi, gheMoi, _) = NewChuyenVaGhe();
        var veGoc = NewPaidTicket(khach.Id, 250_000m);
        veGoc.Status = trangThai;

        await SeedAsync(factory, db =>
        {
            db.Tickets.Add(veGoc);
            db.Trips.Add(chuyenMoi);
            db.Seats.Add(gheMoi);
            db.Fares.Add(NewFare(chuyenMoi.RouteId, 300_000m));
        });

        using var client = AuthClientFor(factory, khach);

        var response = await client.PostAsJsonAsync(Url, new
        {
            ticketId = veGoc.Id,
            newTripId = chuyenMoi.Id,
            newSeatId = gheMoi.Id,
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(VeKhongConHieuLuc, MessageOf(await ReadJsonAsync(response)));
        Assert.Equal(0, await QueryAsync(factory, db => db.TicketChanges.CountAsync()));
    }

    // ===========================================================================================
    // 3. RÀNG BUỘC LIÊN BẢNG — một vé không thể có hai yêu cầu chờ
    // ===========================================================================================

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Ve_dang_co_yeu_cau_huy_hoac_doi_cho_duyet_tra_409(bool laYeuCauHuy)
    {
        using var factory = NewFactory();
        var khach = await SeedKhachAsync(factory);

        var (chuyenMoi, gheMoi, _) = NewChuyenVaGhe();
        var veGoc = NewPaidTicket(khach.Id, 250_000m);

        await SeedAsync(factory, db =>
        {
            db.Tickets.Add(veGoc);
            db.Trips.Add(chuyenMoi);
            db.Seats.Add(gheMoi);
            db.Fares.Add(NewFare(chuyenMoi.RouteId, 300_000m));

            if (laYeuCauHuy)
            {
                db.CancellationRequests.Add(new CancellationRequest
                {
                    TicketId = veGoc.Id,
                    UserId = khach.Id,
                    CancellationFee = 0m,
                    DepartureAt = Moc1.AddDays(3),
                    Status = CancellationRequestStatus.Pending,
                    CreatedAt = Moc1,
                });
            }
            else
            {
                db.TicketChanges.Add(new TicketChange
                {
                    TicketId = veGoc.Id,
                    UserId = khach.Id,
                    NewTripId = chuyenMoi.Id,
                    NewSeatId = gheMoi.Id,
                    NewPrice = 300_000m,
                    Status = TicketChangeStatus.Pending,
                    CreatedAt = Moc1,
                });
            }
        });

        using var client = AuthClientFor(factory, khach);

        var response = await client.PostAsJsonAsync(Url, new
        {
            ticketId = veGoc.Id,
            newTripId = chuyenMoi.Id,
            newSeatId = gheMoi.Id,
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(DangCoYeuCauCho, MessageOf(await ReadJsonAsync(response)));

        // Nhánh yêu cầu huỷ: KHÔNG có dòng TicketChanges nào. Nhánh yêu cầu đổi: chỉ đúng dòng đã
        // seed, không có dòng mới. Cả hai đều chứng minh lượt gọi không ghi gì.
        Assert.Equal(
            laYeuCauHuy ? 0 : 1,
            await QueryAsync(factory, db => db.TicketChanges.CountAsync()));
    }

    /// <summary>
    /// Yêu cầu ĐÃ TỪ CHỐI không chặn lần gửi mới: chỉ mục duy nhất điều kiện chỉ chặn <c>Pending</c>
    /// (docs/32 §4.1). Hành vi này quan trọng vì nếu service lọc sai, khách bị từ chối một lần là
    /// không bao giờ xin đổi được nữa.
    /// </summary>
    [Fact]
    public async Task Yeu_cau_cu_da_tu_choi_khong_chan_lan_gui_moi()
    {
        using var factory = NewFactory();
        var khach = await SeedKhachAsync(factory);

        var (chuyenMoi, gheMoi, _) = NewChuyenVaGhe();
        var veGoc = NewPaidTicket(khach.Id, 250_000m);

        await SeedAsync(factory, db =>
        {
            db.Tickets.Add(veGoc);
            db.Trips.Add(chuyenMoi);
            db.Seats.Add(gheMoi);
            db.Fares.Add(NewFare(chuyenMoi.RouteId, 300_000m));
            db.TicketChanges.Add(new TicketChange
            {
                TicketId = veGoc.Id,
                UserId = khach.Id,
                NewTripId = chuyenMoi.Id,
                NewSeatId = gheMoi.Id,
                NewPrice = 300_000m,
                Status = TicketChangeStatus.Rejected,
                CreatedAt = Moc1,
            });
        });

        using var client = AuthClientFor(factory, khach);

        var response = await client.PostAsJsonAsync(Url, new
        {
            ticketId = veGoc.Id,
            newTripId = chuyenMoi.Id,
            newSeatId = gheMoi.Id,
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(2, await QueryAsync(factory, db => db.TicketChanges.CountAsync()));
    }

    // ===========================================================================================
    // 4. CHUYẾN MỚI — tồn tại và còn nhận đổi
    // ===========================================================================================

    [Fact]
    public async Task Chuyen_moi_khong_ton_tai_tra_404()
    {
        using var factory = NewFactory();
        var khach = await SeedKhachAsync(factory);

        var gheMoi = NewSeat(Guid.NewGuid());
        var veGoc = NewPaidTicket(khach.Id, 250_000m);

        await SeedAsync(factory, db =>
        {
            db.Tickets.Add(veGoc);
            db.Seats.Add(gheMoi);
        });

        using var client = AuthClientFor(factory, khach);

        var response = await client.PostAsJsonAsync(Url, new
        {
            ticketId = veGoc.Id,
            newTripId = Guid.NewGuid(),
            newSeatId = gheMoi.Id,
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(KhongTimThayChuyen, MessageOf(await ReadJsonAsync(response)));
        Assert.Equal(0, await QueryAsync(factory, db => db.TicketChanges.CountAsync()));
    }

    [Theory]
    [InlineData(TripStatus.Cancelled)]
    [InlineData(TripStatus.Completed)]
    [InlineData(TripStatus.Running)]
    public async Task Chuyen_moi_khong_con_scheduled_tra_409(TripStatus trangThai)
    {
        using var factory = NewFactory();
        var khach = await SeedKhachAsync(factory);

        var (chuyenMoi, gheMoi, _) = NewChuyenVaGhe();
        chuyenMoi.Status = trangThai;
        var veGoc = NewPaidTicket(khach.Id, 250_000m);

        await SeedAsync(factory, db =>
        {
            db.Tickets.Add(veGoc);
            db.Trips.Add(chuyenMoi);
            db.Seats.Add(gheMoi);
            db.Fares.Add(NewFare(chuyenMoi.RouteId, 300_000m));
        });

        using var client = AuthClientFor(factory, khach);

        var response = await client.PostAsJsonAsync(Url, new
        {
            ticketId = veGoc.Id,
            newTripId = chuyenMoi.Id,
            newSeatId = gheMoi.Id,
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(ChuyenKhongNhanDoi, MessageOf(await ReadJsonAsync(response)));
        Assert.Equal(0, await QueryAsync(factory, db => db.TicketChanges.CountAsync()));
    }

    /// <summary>
    /// Chuyến còn ghi <c>Scheduled</c> nhưng giờ khởi hành đã qua — trạng thái chuyến do job nền
    /// cập nhật nên có độ trễ. Kiểm thêm <c>DepartureTime</c> là cách duy nhất không nhận đổi sang
    /// một chuyến đã rời bến.
    /// </summary>
    [Fact]
    public async Task Chuyen_moi_da_qua_gio_khoi_hanh_tra_409()
    {
        using var factory = NewFactory();
        var khach = await SeedKhachAsync(factory);

        var (chuyenMoi, gheMoi, _) = NewChuyenVaGhe();
        chuyenMoi.DepartureTime = DateTime.UtcNow.AddHours(-1);
        var veGoc = NewPaidTicket(khach.Id, 250_000m);

        await SeedAsync(factory, db =>
        {
            db.Tickets.Add(veGoc);
            db.Trips.Add(chuyenMoi);
            db.Seats.Add(gheMoi);
            db.Fares.Add(NewFare(chuyenMoi.RouteId, 300_000m));
        });

        using var client = AuthClientFor(factory, khach);

        var response = await client.PostAsJsonAsync(Url, new
        {
            ticketId = veGoc.Id,
            newTripId = chuyenMoi.Id,
            newSeatId = gheMoi.Id,
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(ChuyenKhongNhanDoi, MessageOf(await ReadJsonAsync(response)));
    }

    // ===========================================================================================
    // 5. GHẾ MỚI — tồn tại, thuộc xe của chuyến, còn trống
    // ===========================================================================================

    [Fact]
    public async Task Ghe_thuoc_xe_khac_tra_404()
    {
        using var factory = NewFactory();
        var khach = await SeedKhachAsync(factory);

        var (chuyenMoi, _, _) = NewChuyenVaGhe();
        var gheCuaXeKhac = NewSeat(Guid.NewGuid());
        var veGoc = NewPaidTicket(khach.Id, 250_000m);

        await SeedAsync(factory, db =>
        {
            db.Tickets.Add(veGoc);
            db.Trips.Add(chuyenMoi);
            db.Seats.Add(gheCuaXeKhac);
            db.Fares.Add(NewFare(chuyenMoi.RouteId, 300_000m));
        });

        using var client = AuthClientFor(factory, khach);

        var response = await client.PostAsJsonAsync(Url, new
        {
            ticketId = veGoc.Id,
            newTripId = chuyenMoi.Id,
            newSeatId = gheCuaXeKhac.Id,
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(KhongTimThayGhe, MessageOf(await ReadJsonAsync(response)));
    }

    [Fact]
    public async Task Ghe_da_ban_tra_409()
    {
        using var factory = NewFactory();
        var khach = await SeedKhachAsync(factory);
        var nguoiKhac = await SeedKhachAsync(factory, "Người Khác");

        var (chuyenMoi, gheMoi, _) = NewChuyenVaGhe();
        var veGoc = NewPaidTicket(khach.Id, 250_000m);
        var veChiemGhe = NewPaidTicket(nguoiKhac.Id, 300_000m);
        veChiemGhe.TripId = chuyenMoi.Id;
        veChiemGhe.SeatId = gheMoi.Id;

        await SeedAsync(factory, db =>
        {
            db.Tickets.AddRange(veGoc, veChiemGhe);
            db.Trips.Add(chuyenMoi);
            db.Seats.Add(gheMoi);
            db.Fares.Add(NewFare(chuyenMoi.RouteId, 300_000m));
        });

        using var client = AuthClientFor(factory, khach);

        var response = await client.PostAsJsonAsync(Url, new
        {
            ticketId = veGoc.Id,
            newTripId = chuyenMoi.Id,
            newSeatId = gheMoi.Id,
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(GheDaCoNguoi, MessageOf(await ReadJsonAsync(response)));
    }

    [Fact]
    public async Task Ghe_dang_bi_giu_tra_409()
    {
        using var factory = NewFactory();
        var khach = await SeedKhachAsync(factory);

        var (chuyenMoi, gheMoi, _) = NewChuyenVaGhe();
        var veGoc = NewPaidTicket(khach.Id, 250_000m);

        await SeedAsync(factory, db =>
        {
            db.Tickets.Add(veGoc);
            db.Trips.Add(chuyenMoi);
            db.Seats.Add(gheMoi);
            db.Fares.Add(NewFare(chuyenMoi.RouteId, 300_000m));
            db.SeatHolds.Add(new SeatHold
            {
                TripId = chuyenMoi.Id,
                SeatId = gheMoi.Id,
                UserId = khach.Id,
                SessionCode = "phien-khac",
                Status = SeatHoldStatus.Holding,
                ExpiresAt = DateTime.UtcNow.AddMinutes(10),
                CreatedAt = Moc1,
            });
        });

        using var client = AuthClientFor(factory, khach);

        var response = await client.PostAsJsonAsync(Url, new
        {
            ticketId = veGoc.Id,
            newTripId = chuyenMoi.Id,
            newSeatId = gheMoi.Id,
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(GheDaCoNguoi, MessageOf(await ReadJsonAsync(response)));
    }

    // ===========================================================================================
    // 6. GIÁ — tuyến chưa cấu hình thì không có số nào để khách đồng ý
    // ===========================================================================================

    [Fact]
    public async Task Tuyen_chua_co_gia_tra_409_va_khong_ghi_gi()
    {
        using var factory = NewFactory();
        var khach = await SeedKhachAsync(factory);

        var (chuyenMoi, gheMoi, _) = NewChuyenVaGhe();
        var veGoc = NewPaidTicket(khach.Id, 250_000m);

        await SeedAsync(factory, db =>
        {
            db.Tickets.Add(veGoc);
            db.Trips.Add(chuyenMoi);
            db.Seats.Add(gheMoi);
            // Cố ý KHÔNG thêm Fares.
        });

        using var client = AuthClientFor(factory, khach);

        var response = await client.PostAsJsonAsync(Url, new
        {
            ticketId = veGoc.Id,
            newTripId = chuyenMoi.Id,
            newSeatId = gheMoi.Id,
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(ChuaCoGia, MessageOf(await ReadJsonAsync(response)));
        Assert.Equal(0, await QueryAsync(factory, db => db.TicketChanges.CountAsync()));
    }

    // ===========================================================================================
    // 7. XÁC THỰC VÀ HÌNH THỨC
    // ===========================================================================================

    [Fact]
    public async Task Khong_gui_token_tra_401()
    {
        using var factory = NewFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(Url, new
        {
            ticketId = Guid.NewGuid(),
            newTripId = Guid.NewGuid(),
            newSeatId = Guid.NewGuid(),
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("ticketId")]
    [InlineData("newTripId")]
    [InlineData("newSeatId")]
    public async Task Thieu_truong_bat_buoc_tra_400_kem_errors(string truongThieu)
    {
        using var factory = NewFactory();
        var khach = await SeedKhachAsync(factory);
        using var client = AuthClientFor(factory, khach);

        var body = new Dictionary<string, object?>
        {
            ["ticketId"] = Guid.NewGuid(),
            ["newTripId"] = Guid.NewGuid(),
            ["newSeatId"] = Guid.NewGuid(),
        };
        body.Remove(truongThieu);

        var response = await client.PostAsJsonAsync(Url, body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var json = await ReadJsonAsync(response);
        Assert.True(json.GetProperty("errors").TryGetProperty(truongThieu, out _));
    }

    /// <summary>
    /// Dòng lý do quá dài bị model binding chặn trước khi vào service → 400, không phải 500.
    /// </summary>
    [Fact]
    public async Task Ly_do_qua_2000_ky_tu_tra_400()
    {
        using var factory = NewFactory();
        var khach = await SeedKhachAsync(factory);
        using var client = AuthClientFor(factory, khach);

        var response = await client.PostAsJsonAsync(Url, new
        {
            ticketId = Guid.NewGuid(),
            newTripId = Guid.NewGuid(),
            newSeatId = Guid.NewGuid(),
            reason = new string('a', 2001),
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var json = await ReadJsonAsync(response);
        Assert.True(json.GetProperty("errors").TryGetProperty("reason", out _));
    }

    [Fact]
    public void App_that_resolve_duoc_ITicketChangeSubmissionService()
    {
        using var factory = NewFactory();
        using var scope = factory.Services.CreateScope();

        var service = scope.ServiceProvider.GetRequiredService<ITicketChangeSubmissionService>();

        Assert.IsType<TicketChangeSubmissionService>(service);
    }

    // ===========================================================================================
    // HELPERS
    // ===========================================================================================

    private static WebApplicationFactory<Program> NewFactory()
        => new TestAppFactory().WithWebHostBuilder(builder =>
            builder.UseSetting("TicketQr:SigningKey", QrSigningKey));

    private static async Task<UserEntity> SeedKhachAsync(
        WebApplicationFactory<Program> factory,
        string hoTen = "Khách Test")
    {
        await SeedAsync(factory, db =>
        {
            if (!db.Roles.Any(r => r.Id == RoleIds.Passenger || r.Code == RoleCodes.Passenger))
            {
                db.Roles.Add(new Role
                {
                    Id = RoleIds.Passenger,
                    Code = RoleCodes.Passenger,
                    Name = RoleCodes.Passenger,
                });
            }
        });

        var user = new UserEntity
        {
            Id = Guid.NewGuid(),
            PhoneNumber = "09" + Random.Shared.Next(10_000_000, 99_999_999),
            FullName = hoTen,
            PasswordHash = PasswordService.HashPassword("matkhau123"),
            IsActive = true,
            RoleId = RoleIds.Passenger,
            UserRoles = [new UserRole { RoleId = RoleIds.Passenger }],
        };

        await SeedAsync(factory, db => db.Users.Add(user));

        return user;
    }

    /// <summary>
    /// Chuyến mới + ghế của xe đó. Cố ý KHÔNG tự thêm <see cref="Fare"/> — để ca "tuyến chưa có
    /// giá" kiểm được đúng nhánh của nó; nơi gọi tự thêm giá khi cần.
    /// </summary>
    private static (Trip Chuyen, Seat Ghe, Guid RouteId) NewChuyenVaGhe()
    {
        var routeId = Guid.NewGuid();
        var busId = Guid.NewGuid();

        var chuyen = new Trip
        {
            RouteId = routeId,
            BusId = busId,
            DepartureTime = DateTime.UtcNow.AddDays(3),
            Status = TripStatus.Scheduled,
            CreatedAt = Moc1,
        };

        return (chuyen, NewSeat(busId), routeId);
    }

    private static Seat NewSeat(Guid busId) => new()
    {
        BusId = busId,
        SeatLayoutId = Guid.NewGuid(),
        SeatNumber = "A" + Random.Shared.Next(1, 99),
        Floor = 1,
        RowIndex = 1,
        ColumnIndex = 1,
        SeatType = SeatType.Standard,
    };

    private static Fare NewFare(Guid routeId, decimal gia) => new()
    {
        RouteId = routeId,
        PassengerType = PassengerType.Standard,
        Price = gia,
        CreatedAt = Moc1,
    };

    private static Ticket NewPaidTicket(Guid chuVeId, decimal gia) => new()
    {
        Code = "SBT1:ve-goc-" + Guid.NewGuid().ToString("N"),
        TripId = Guid.NewGuid(),
        SeatId = Guid.NewGuid(),
        UserId = chuVeId,
        Price = gia,
        Status = TicketStatus.Paid,
        CreatedAt = Moc1,
    };

    private static async Task SeedAsync(WebApplicationFactory<Program> factory, Action<AppDbContext> seed)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        seed(db);

        await db.SaveChangesAsync();
    }

    private static async Task<T> QueryAsync<T>(
        WebApplicationFactory<Program> factory,
        Func<AppDbContext, Task<T>> query)
    {
        using var scope = factory.Services.CreateScope();

        return await query(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private static HttpClient AuthClientFor(WebApplicationFactory<Program> factory, UserEntity user)
    {
        using var scope = factory.Services.CreateScope();
        var token = scope.ServiceProvider.GetRequiredService<ITokenService>().CreateAccessToken(user).Token;

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return client;
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return doc.RootElement.Clone();
    }

    private static string MessageOf(JsonElement body)
        => body.GetProperty("message").GetString()!;
}
