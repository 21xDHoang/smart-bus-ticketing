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
/// Test xuyên tầng cho chuỗi <b>phát hành vé → sinh mã QR → soát lại mã</b> (US 4 *"Vé điện tử QR"*,
/// Sprint 3, task *"Test API phát hành vé và kiểm tra hiệu lực QR (xUnit)"* — Giàng A Vàng, dòng 48).
///
/// ⚠️ <b>Vì sao file này KHÔNG gọi HTTP.</b> Hai mắt xích mà tên task nhắc tới đều chưa tồn tại trên
/// <c>main</c> tại thời điểm viết:
///   • <i>API phát hành vé</i> (dòng 40 — Trần Trung Hiếu): <c>docs/api-contract.md</c> mục *"Phát hành
///     vé sau khi thanh toán"* chốt đây là <b>service nội bộ</b> gọi từ IPN/đối soát, <b>không phải
///     endpoint</b> — và chưa có service nào tên <c>*Issuance*</c>. Mắt xích gần nhất đang tồn tại là
///     cờ <c>PaymentSettlementOutcome.ShouldIssueTickets</c> (đã có test riêng ở
///     <see cref="PaymentSettlementServiceTests"/>), chưa ai tiêu thụ.
///   • <i>API kiểm tra hiệu lực vé theo mã QR</i> (dòng 43): không có controller nào của vé trong 32
///     controller của <c>SmartBus.Api</c>; <c>docs/api-contract.md:3118-3120</c> ghi *"Chưa làm"*, và
///     <c>docs/29-ban-giao-api-kiem-tra-hieu-luc-qr.md</c> §5 ghi thẳng dòng 48 là <i>"Chờ endpoint"</i>.
///
/// Viết test gọi hai thứ đó chỉ tạo ra file không biên dịch được — CI đỏ, vi phạm Definition of Done.
/// Nên file này phủ <b>những tầng đã có thật</b>, đúng lệ đã dùng ở task dòng 36
/// (<c>feature/6-test-api-thanh-toan-sandbox-3-cong</c>: *"test xuyên tầng adapter → chốt giao dịch"*).
/// Khi Hiếu dựng xong hai endpoint, phần HTTP bổ sung vào file này — không thay thế.
///
/// Ba tầng được phủ:
///   1. <b>DDL PostgreSQL thật</b> — <see cref="AppDbContext"/> dựng bằng Npgsql rồi
///      <c>GenerateCreateScript()</c>. Đây là cách DUY NHẤT phủ được ràng buộc A6 ở tầng CSDL:
///      provider InMemory của <see cref="TestAppFactory"/> <b>không dựng unique index</b>, kể cả chỉ
///      mục điều kiện — giới hạn đã ghi ở doc-comment của <see cref="SeatHoldCreateApiTests"/>.
///   2. <b>Vòng đời qua CSDL của app thật</b> — phát hành một vé có mã QR sinh từ
///      <see cref="ITicketQrService"/> của chính app, ghi xuống, đọc lại, rồi soát mã đọc được.
///   3. <b>Ngân sách độ dài</b> — nối định dạng mã của service với cột <c>Code</c> mà nó phải nằm vừa.
///
/// Khoá ký là khoá GIẢ rõ ràng — không phải khoá thật (luật 2), cùng lối
/// <see cref="TicketQrServiceTests"/> và <see cref="TicketQrWiringTests"/>.
/// </summary>
public class TicketQrIssuanceChainTests
{
    private const string SigningKey = "khoa-ky-gia-cua-test-dai-hon-32-ky-tu-abc";

    /// <summary>
    /// Chuỗi kết nối GIẢ — chỉ để Npgsql dựng được model sinh DDL, KHÔNG kết nối tới đâu
    /// (<c>GenerateCreateScript()</c> là phép biến đổi model → SQL, chạy hoàn toàn ngoại tuyến).
    /// Không phải bí mật và không phải địa chỉ thật; cùng lối <see cref="ChuoiKetNoiCsdlTests"/>.
    /// </summary>
    private const string ChuoiKetNoiGia = "Host=localhost;Database=test-khong-ket-noi;Username=gia;Password=gia";

    // =======================================================================================
    // 1. DDL PostgreSQL thật — ràng buộc A5 (xoá) và A6 (khoá duy nhất) của bảng Tickets
    // =======================================================================================

    [Fact]
    public void Ddl_Code_la_varchar_200_bat_buoc()
    {
        // Cột Code chứa CẢ mã QR có ký số (docs/28 §2) — nới/co độ dài là đổi ngân sách của
        // TicketQrService. 200 là chỗ chừa cho payload 106 ký tự hôm nay.
        Assert.Contains("\"Code\" character varying(200) NOT NULL", Ddl());
    }

    [Fact]
    public void Ddl_tien_la_numeric_12_2_khong_phai_dau_phay_dong()
    {
        var ddl = Ddl();

        // A3: tiền là numeric(12,2) — cùng độ rộng với Fares.Price và Payments.Amount.
        Assert.Contains("\"Price\" numeric(12,2) NOT NULL", ddl);

        // Vì sao ghim cả hai vế phủ định: sai số dấu phẩy động trên tiền là lỗi KHÔNG SỬA ĐƯỢC —
        // hoá đơn đã xuất không cộng lại đúng doanh thu. Test này đỏ nghĩa là kiểu cột đã bị đổi.
        Assert.DoesNotContain("\"Price\" double precision", ddl);
        Assert.DoesNotContain("\"Price\" real", ddl);
    }

    [Fact]
    public void Ddl_thoi_gian_la_timestamptz()
    {
        var ddl = Ddl();

        // A3: timestamptz (UTC), không phải timestamp thiếu múi giờ. Việt Nam UTC+7 nên cột thiếu
        // múi giờ là lỗi lệch giờ khi so sánh chuyến.
        Assert.Contains("\"CreatedAt\" timestamp with time zone NOT NULL", ddl);
        Assert.Contains("\"UsedAt\" timestamp with time zone", ddl);
        Assert.DoesNotContain("\"CreatedAt\" timestamp without time zone", ddl);
    }

    [Fact]
    public void Ddl_Status_luu_chuoi_chu_khong_phai_so()
    {
        // A3: trạng thái lưu bằng chuỗi qua .HasConversion<string>() — "Paid" đọc là hiểu, còn 2 thì
        // phải tra bảng mã. Ba giá trị của TicketStatus phải nằm gọn trong 20 ký tự.
        Assert.Contains("\"Status\" character varying(20) NOT NULL", Ddl());
    }

    [Fact]
    public void Ddl_chi_muc_Code_la_unique_toan_bang_va_khong_dieu_kien()
    {
        // A6: mã vé duy nhất TOÀN HỆ THỐNG, không phải "duy nhất trong một chuyến". Trùng mã nghĩa là
        // hai khách cùng cầm một mã QR — tức làm giả vé được.
        //
        // Ghim nguyên câu DDL tính cả dấu ';' ngay sau ("Code"): thêm bất kỳ mệnh đề WHERE nào vào
        // chỉ mục này là chuỗi dưới đây không còn khớp, và test đỏ.
        Assert.Contains("CREATE UNIQUE INDEX \"IX_Tickets_Code\" ON \"Tickets\" (\"Code\");", Ddl());
    }

    [Fact]
    public void Ddl_chong_trung_ghe_la_unique_co_dieu_kien_dung_chu_A6()
    {
        // 🔴 Ràng buộc quan trọng nhất của Sprint 3 (A6) — thứ chống bán trùng ghế khi nhiều người
        // đặt cùng lúc. EF Core không sinh được partial index bằng attribute nên toàn bộ ràng buộc
        // nằm ở đúng một dòng trong AppDbContext.Ticket.cs; test này ghim dòng DDL mà nó sinh ra.
        Assert.Contains(
            "CREATE UNIQUE INDEX \"IX_Tickets_TripId_SeatId_Active\" ON \"Tickets\" (\"TripId\", \"SeatId\") "
            + "WHERE \"Status\" IN ('Held', 'Paid');",
            Ddl());
    }

    [Fact]
    public void Ddl_ve_huy_khong_bi_chi_muc_chong_trung_chan()
    {
        // Nửa còn lại của A6, và là lý do chỉ mục phải là ĐIỀU KIỆN chứ không phải unique thường:
        // một ghế của một chuyến hợp lệ khi có nhiều vé theo thời gian — vé cũ đã huỷ rồi khách khác
        // mua lại chính ghế đó. Unique thường sẽ chặn vĩnh viễn việc bán lại.
        var dong = Ddl()
            .Split('\n')
            .Single(line => line.StartsWith("CREATE UNIQUE INDEX \"IX_Tickets_TripId_SeatId_Active\"", StringComparison.Ordinal));

        Assert.DoesNotContain("Cancelled", dong);
    }

    [Fact]
    public void Ddl_nam_khoa_ngoai_cua_Tickets_deu_RESTRICT()
    {
        var bang = DoanBang("Tickets");

        // A5 — lỗi tốn kém nhất của dự án: EF mặc định Cascade cho mọi quan hệ bắt buộc, nên xoá một
        // tuyến sẽ kéo theo chuyến → vé → thanh toán. Đó là mất dữ liệu doanh thu, không phải lỗi
        // hiển thị. Tickets có đúng 5 FK nghiệp vụ và cả 5 phải Restrict tường minh.
        Assert.Equal(5, SoLan(bang, "FOREIGN KEY"));
        Assert.Equal(5, SoLan(bang, "ON DELETE RESTRICT"));

        // Nhánh Cascade duy nhất được phép trong dự án là RouteStops←Routes, Seats←Buses,
        // RefreshTokens←Users (A5) — không nhánh nào thuộc bảng này.
        Assert.DoesNotContain("ON DELETE CASCADE", bang);
    }

    [Fact]
    public void Ddl_co_chi_muc_TripId_va_UserId_khong_dieu_kien()
    {
        // A6 — hai chỉ mục này KHÔNG thừa dù chỉ mục chống trùng ở trên cũng mở đầu bằng TripId:
        // PostgreSQL chỉ dùng được partial index khi truy vấn CHỨNG MINH được điều kiện của nó, mà
        // "danh sách khách của chuyến" (WHERE TripId = @t) và "tra vé của tôi" (WHERE UserId = @u)
        // không nói gì về Status. Bỏ hai dòng này là hai truy vấn nóng nhất quét cả bảng.
        var ddl = Ddl();

        Assert.Contains("CREATE INDEX \"IX_Tickets_TripId\" ON \"Tickets\" (\"TripId\");", ddl);
        Assert.Contains("CREATE INDEX \"IX_Tickets_UserId\" ON \"Tickets\" (\"UserId\");", ddl);
    }

    // =======================================================================================
    // 2. Vòng đời xuyên tầng — phát hành, ghi xuống CSDL của app thật, đọc lại, soát mã
    // =======================================================================================

    [Fact]
    public async Task Ma_QR_phat_hanh_song_sot_qua_CSDL_roi_soat_lai_duoc()
    {
        using var factory = NewFactory();

        var (trip, seat, user) = await SeedChuyenGheKhachAsync(factory);

        string issued;
        var ticketId = Guid.NewGuid();

        // Bước phát hành: sinh mã bằng CHÍNH ITicketQrService của app (không dựng service rời) rồi
        // ghi vé xuống CSDL — đúng thứ tự luồng ở api-contract: tiền về → sinh mã → lưu vé.
        using (var scope = factory.Services.CreateScope())
        {
            var qr = scope.ServiceProvider.GetRequiredService<ITicketQrService>();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            issued = qr.GenerateCode(ticketId);

            db.Tickets.Add(new Ticket
            {
                Id = ticketId,
                Code = issued,
                TripId = trip.Id,
                SeatId = seat.Id,
                UserId = user.Id,
                Price = 24_000.50m,
            });

            await db.SaveChangesAsync();
        }

        // Bước soát vé: đọc lại vé bằng một scope MỚI (không dùng lại đối tượng còn nằm trong change
        // tracker — như vậy mới chứng minh được giá trị đi qua CSDL nguyên vẹn).
        using (var scope = factory.Services.CreateScope())
        {
            var qr = scope.ServiceProvider.GetRequiredService<ITicketQrService>();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var doc = await db.Tickets.SingleAsync(t => t.Id == ticketId);

            // Mã phải sống sót nguyên vẹn — không bị cắt theo maxLength, không bị đổi hoa/thường.
            // Lệch một ký tự là chữ ký HMAC sập.
            Assert.Equal(issued, doc.Code);

            // Vé sinh ra ở Paid, không có bước trung gian (TicketStatus).
            Assert.Equal(TicketStatus.Paid, doc.Status);

            // Tiền đi qua cột numeric(12,2) không mất phần thập phân (A3).
            Assert.Equal(24_000.50m, doc.Price);

            // Chốt cả chuỗi: mã đọc từ CSDL vẫn soát ra ĐÚNG vé đã phát hành.
            Assert.True(qr.TryVerify(doc.Code, out var soatDuoc));
            Assert.Equal(ticketId, soatDuoc);
        }
    }

    [Fact]
    public async Task Ma_QR_dai_khong_vuot_suc_chua_cua_cot_Code()
    {
        using var factory = NewFactory();

        using var scope = factory.Services.CreateScope();
        var qr = scope.ServiceProvider.GetRequiredService<ITicketQrService>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Phép kiểm XUYÊN TẦNG: ngân sách độ dài của TicketQrService đối chiếu thẳng với cột mà nó
        // phải nằm vừa, đọc từ model — chứ không ghim con số 200 lần thứ hai trong test. Đổi định dạng
        // mã (mở SBT2 dài hơn) mà quên nới cột là test này đỏ, không phải lỗi cắt cụt lúc chạy thật.
        var cotCode = db.Model.FindEntityType(typeof(Ticket))!
            .FindProperty(nameof(Ticket.Code))!;

        var maDaiNhat = qr.GenerateCode(Guid.NewGuid());

        Assert.Equal(200, cotCode.GetMaxLength());
        Assert.True(
            maDaiNhat.Length <= cotCode.GetMaxLength(),
            $"Mã QR dài {maDaiNhat.Length} ký tự, vượt sức chứa {cotCode.GetMaxLength()} của cột Code.");
    }

    [Fact]
    public async Task Sua_ma_luu_trong_CSDL_thi_soat_khong_qua()
    {
        using var factory = NewFactory();

        var (trip, seat, user) = await SeedChuyenGheKhachAsync(factory);
        var ticketId = Guid.NewGuid();

        using (var scope = factory.Services.CreateScope())
        {
            var qr = scope.ServiceProvider.GetRequiredService<ITicketQrService>();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            db.Tickets.Add(new Ticket
            {
                Id = ticketId,
                Code = qr.GenerateCode(ticketId),
                TripId = trip.Id,
                SeatId = seat.Id,
                UserId = user.Id,
                Price = 24_000m,
            });

            await db.SaveChangesAsync();
        }

        // Mô phỏng kẻ làm giả sửa thẳng dòng trong CSDL: lật ký tự cuối của mã, giữ nguyên nonce và
        // chữ ký cũ. Đây là gian lận kinh điển mà chữ ký gắn cứng vào chuỗi đem ký phải chặn.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var ve = await db.Tickets.SingleAsync(t => t.Id == ticketId);
            ve.Code = ve.Code[..^1] + (ve.Code[^1] == 'A' ? 'B' : 'A');

            await db.SaveChangesAsync();
        }

        using (var scope = factory.Services.CreateScope())
        {
            var qr = scope.ServiceProvider.GetRequiredService<ITicketQrService>();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var ve = await db.Tickets.SingleAsync(t => t.Id == ticketId);

            Assert.False(qr.TryVerify(ve.Code, out var soatDuoc));
            Assert.Equal(Guid.Empty, soatDuoc);
        }
    }

    [Fact]
    public async Task Hai_ve_cung_chuyen_khac_ghe_moi_ve_giu_rieng_ma_cua_minh()
    {
        using var factory = NewFactory();

        var (trip, seatA, user) = await SeedChuyenGheKhachAsync(factory);
        var seatB = await SeedGheAsync(factory, seatA.BusId, "B2");

        var veA = Guid.NewGuid();
        var veB = Guid.NewGuid();
        string maA;
        string maB;

        // Hai ghế khác nhau của CÙNG một chuyến — ca hợp lệ của chỉ mục chống trùng (TripId, SeatId):
        // khác SeatId nên không đụng nhau.
        using (var scope = factory.Services.CreateScope())
        {
            var qr = scope.ServiceProvider.GetRequiredService<ITicketQrService>();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            maA = qr.GenerateCode(veA);
            maB = qr.GenerateCode(veB);

            db.Tickets.Add(new Ticket { Id = veA, Code = maA, TripId = trip.Id, SeatId = seatA.Id, UserId = user.Id, Price = 24_000m });
            db.Tickets.Add(new Ticket { Id = veB, Code = maB, TripId = trip.Id, SeatId = seatB.Id, UserId = user.Id, Price = 24_000m });

            await db.SaveChangesAsync();
        }

        Assert.NotEqual(maA, maB);

        using (var scope = factory.Services.CreateScope())
        {
            var qr = scope.ServiceProvider.GetRequiredService<ITicketQrService>();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var docA = await db.Tickets.SingleAsync(t => t.Id == veA);
            var docB = await db.Tickets.SingleAsync(t => t.Id == veB);

            // Mỗi vé soát ra CHÍNH NÓ — không lẫn sang vé kia, dù hai vé cùng chuyến cùng khách.
            Assert.True(qr.TryVerify(docA.Code, out var tuA));
            Assert.True(qr.TryVerify(docB.Code, out var tuB));
            Assert.Equal(veA, tuA);
            Assert.Equal(veB, tuB);
        }
    }

    // =======================================================================================
    // Helper — mỗi lớp test tự chép, không có lớp base chung (đúng lệ đang có của dự án)
    // =======================================================================================

    /// <summary>
    /// App THẬT của <see cref="TestAppFactory"/> cộng khoá ký của test. Phải đi qua
    /// <c>WithWebHostBuilder</c> chứ không dùng thẳng <see cref="TestAppFactory"/>: khoá QR chỉ có
    /// nghĩa lúc Program.cs dựng service, nên phải có mặt trước khi host build — cùng lý do và cùng
    /// lối của <see cref="TicketQrWiringTests"/>.
    ///
    /// Vì <c>WithWebHostBuilder</c> trả về lớp cơ sở (và <see cref="TestAppFactory"/> là <c>sealed</c>
    /// nên không kế thừa được), lớp này tự cầm lấy phép seed thay vì gọi <c>TestAppFactory.SeedAsync</c>.
    /// </summary>
    private static WebApplicationFactory<Program> NewFactory()
        => new TestAppFactory().WithWebHostBuilder(builder =>
            builder.UseSetting("TicketQr:SigningKey", SigningKey));

    /// <summary>Ghi dữ liệu vào CSDL test và lưu ngay — chép nguyên thân của <c>TestAppFactory.SeedAsync</c>.</summary>
    private static async Task SeedAsync(WebApplicationFactory<Program> factory, Action<AppDbContext> seed)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        seed(db);

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// DDL PostgreSQL đầy đủ sinh từ chính <see cref="AppDbContext"/> của app. Dựng context bằng
    /// Npgsql (không mở kết nối nào) chứ KHÔNG dùng <see cref="TestAppFactory"/> — provider InMemory
    /// bỏ qua <c>HasFilter</c> nên sẽ không sinh ra chỉ mục điều kiện cần kiểm.
    /// </summary>
    private static string Ddl()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(ChuoiKetNoiGia)
            .Options;

        using var db = new AppDbContext(options);

        return db.Database.GenerateCreateScript();
    }

    /// <summary>Đoạn DDL của một bảng — từ <c>CREATE TABLE "X" (</c> tới dòng <c>);</c> đóng bảng.</summary>
    private static string DoanBang(string ten)
    {
        var ddl = Ddl();
        var mo = ddl.IndexOf($"CREATE TABLE \"{ten}\" (", StringComparison.Ordinal);
        var dong = ddl.IndexOf("\n);", mo, StringComparison.Ordinal);

        return ddl[mo..dong];
    }

    /// <summary>Đếm số lần <paramref name="mau"/> xuất hiện — tránh kéo cả Regex vào chỉ để đếm.</summary>
    private static int SoLan(string chuoi, string mau)
        => chuoi.Split(mau, StringSplitOptions.None).Length - 1;

    /// <summary>
    /// Một chuyến + một ghế + một khách, đủ để một vé có chỗ dựa. InMemory không cưỡng chế khoá ngoại
    /// nên không cần seed Route/Bus/SeatLayout thật — cùng lệ của <see cref="SeatHoldCreateApiTests"/>.
    /// </summary>
    private static async Task<(Trip Trip, Seat Seat, UserEntity User)> SeedChuyenGheKhachAsync(
        WebApplicationFactory<Program> factory)
    {
        var trip = new Trip
        {
            Id = Guid.NewGuid(),
            RouteId = Guid.NewGuid(),
            BusId = Guid.NewGuid(),
            Status = TripStatus.Scheduled,
            DepartureTime = new DateTime(2026, 11, 1, 1, 0, 0, DateTimeKind.Utc),
        };

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

        // Vai trò phải seed trước: provider InMemory KHÔNG chạy HasData, nên khoá ngoại RoleId của
        // khách sẽ trỏ vào hư không — cùng lý do và cùng lối của SeatHoldCreateApiTests.
        await SeedAsync(factory, db =>
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

        await SeedAsync(factory, db =>
        {
            db.Trips.Add(trip);
            db.Seats.Add(seat);
            db.Users.Add(user);
        });

        return (trip, seat, user);
    }

    private static async Task<Seat> SeedGheAsync(WebApplicationFactory<Program> factory, Guid busId, string seatNumber)
    {
        var seat = new Seat
        {
            Id = Guid.NewGuid(),
            BusId = busId,
            SeatLayoutId = Guid.NewGuid(),
            Floor = 1,
            RowIndex = 1,
            ColumnIndex = 2,
            SeatNumber = seatNumber,
        };

        await SeedAsync(factory, db => db.Seats.Add(seat));

        return seat;
    }
}
