using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Logging.Abstractions;
using SmartBus.Api.Data;
using SmartBus.Api.Dtos.Refunds;
using SmartBus.Api.Entities;
using SmartBus.Api.Services;

namespace SmartBus.Tests;

/// <summary>
/// Chốt cơ chế giành dòng của job thử lại hoàn tiền (US 8 — task *"Xử lý hoàn tiền thất bại + retry
/// theo lịch"*): khoá phiên bản <c>xmin</c> trên mô hình, đường quét <c>(Status, NextRetryAt)</c>, và
/// cơ chế thua cuộc đua thật — <c>DbUpdateConcurrencyException</c> ở <c>SaveChanges()</c> giành dòng.
///
/// Chia hai phần vì hai phần chịu hai giới hạn khác nhau:
/// <list type="bullet">
/// <item><b>Phần A</b> — soi MÔ HÌNH: ba thứ mà cả InMemory lẫn SQLite đều không cưỡng chế (cột hệ
/// thống <c>xmin</c>, chỉ mục lọc duy nhất, chỉ mục đường quét). Dựng context Npgsql chỉ để đọc
/// metadata, KHÔNG kết nối CSDL — cùng lối 4 file test đã có (SeatHoldRaceFlowApiTests, …).
/// Chạy ở mọi máy, mọi lúc, không cần PostgreSQL.</item>
/// <item><b>Phần B</b> — dựng lại lượt thua cuộc đua trên SQLite thật: PostgreSQL tự tăng <c>xmin</c>
/// mỗi lượt ghi, SQLite không có cột hệ thống nào làm việc đó — nên bản test này tự tay tăng, bằng
/// một lượt ghi chen từ kết nối khác NGAY TRONG <c>SaveChanges()</c> đang chạy (xem
/// <see cref="BomXminDoiThuInterceptor"/>). Phần EF — so <c>xmin</c> cũ trong WHERE rồi ném
/// <c>DbUpdateConcurrencyException</c> — là phần THẬT, không mô phỏng.</item>
/// </list>
/// </summary>
public class RefundClaimConcurrencyTests
{
    private const decimal SoTien = 175_000m;

    private static readonly DateTime BayGio = new(2026, 10, 14, 8, 0, 0, DateTimeKind.Utc);

    // ==================================================================
    // Phần A — chốt trên mô hình
    // ==================================================================

    [Fact]
    public void Refund_giu_khoa_phien_ban_xmin_dung_cot_he_thong()
    {
        // Cả giao thức giành dòng (docs/40 §4.2) đứng trên đúng một giả định: Refund có khoá phiên
        // bản. Gỡ IsRowVersion là mọi lượt SaveChanges() ngừng so phiên bản — hai luồng cùng gọi cổng
        // cho một khoản mà không có gì chặn, im lặng hoàn tiền hai lần. Không test nào khác bắt được
        // việc gỡ đó: InMemory không có xmin, SQLite cũng không.
        using var db = TaoDbContextQuanHe();

        var refund = db.Model.FindEntityType(typeof(Refund));
        Assert.NotNull(refund);

        var xmin = refund.FindProperty("xmin");
        Assert.NotNull(xmin);
        Assert.True(xmin.IsConcurrencyToken);                              // có mặt trong WHERE
        Assert.Equal(ValueGenerated.OnAddOrUpdate, xmin.ValueGenerated);   // CSDL sinh, không phải app
        Assert.False(xmin.IsNullable);
        Assert.Equal(typeof(uint), xmin.ClrType);
        Assert.Equal("xmin", xmin.GetColumnName());
        Assert.Equal("xid", xmin.GetColumnType());                         // kiểu cột hệ thống PostgreSQL
    }

    [Fact]
    public void Job_quet_di_thang_duong_index_Status_NextRetryAt()
    {
        // Truy vấn quét của RefundRetryService lọc đúng (Status = 'Failed' ∧ NextRetryAt ≤ now) và
        // sắp theo NextRetryAt — chỉ mục này là thứ giữ lượt quét rẻ khi bảng Refunds lớn dần.
        // Đổi khoá chỉ mục (thêm cột, đổi thứ tự) làm truy vấn rơi khỏi đường index mà không có
        // triệu chứng nào ở tầng code.
        using var db = TaoDbContextQuanHe();

        var refund = db.Model.FindEntityType(typeof(Refund));
        Assert.NotNull(refund);

        var index = Assert.Single(
            refund.GetIndexes(),
            i => i.GetDatabaseName() == "IX_Refunds_Status_NextRetryAt");

        Assert.Equal(new[] { "Status", "NextRetryAt" }, index.Properties.Select(p => p.Name));
        Assert.False(index.IsUnique);
    }

    [Fact]
    public void Chot_mot_khoan_hoan_song_mot_giao_dich_van_con_nguyen_tren_mo_hinh()
    {
        // Bất biến §4.3 (một khoản hoàn sống cho một giao dịch) được cưỡng chế bằng chỉ mục LỌC
        // DUY NHẤT: chỉ tính các dòng Pending/Succeeded, nên một giao dịch được hoàn lại sau khi
        // khoản cũ đã Failed (thang thử lại đã cạn, quản trị quyết định tạo khoản mới). Gỡ IsUnique
        // hoặc đổi điều kiện lọc là mở đường cho hai khoản cùng sống — hoàn tiền hai lần.
        using var db = TaoDbContextQuanHe();

        var refund = db.Model.FindEntityType(typeof(Refund));
        Assert.NotNull(refund);

        var index = Assert.Single(
            refund.GetIndexes(),
            i => i.GetDatabaseName() == "IX_Refunds_PaymentId_Active");

        Assert.True(index.IsUnique);
        Assert.Equal(new[] { "PaymentId" }, index.Properties.Select(p => p.Name));
        Assert.Equal("\"Status\" IN ('Pending', 'Succeeded')", index.GetFilter());
    }

    [Fact]
    public void RefundLog_khong_co_xmin_vi_la_bang_ghi_noi_them()
    {
        // Nhật ký gọi cổng là bằng chứng, không phải trạng thái: mỗi lượt gọi đẻ thêm một dòng, không
        // ai sửa dòng cũ. Thêm khoá phiên bản cho nó là mời gọi sửa/xoá nhật ký — đúng thứ các bất
        // biến §4.3 muốn chặn. (Số lượt thử lại đếm bằng số dòng có Trigger ≠ Initial, xem docs/40 §4.4.)
        using var db = TaoDbContextQuanHe();

        var nhatKy = db.Model.FindEntityType(typeof(RefundLog));
        Assert.NotNull(nhatKy);
        Assert.Null(nhatKy.FindProperty("xmin"));
    }

    private static AppDbContext TaoDbContextQuanHe() => new(
        new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=khong-ket-noi;Database=chi-soi-metadata;Username=khong;Password=khong")
            .Options);

    // ==================================================================
    // Phần B — lượt thua cuộc đua thật, trên SQLite
    // ==================================================================

    [Fact]
    public async Task Luot_gianh_dong_thua_thi_khong_goi_cong_va_khong_ghi_gi()
    {
        var gateway = new FakeRefundGatewayCaller();
        var tepCsdl = Path.Combine(Path.GetTempPath(), $"smartbus-hoan-tien-{Guid.NewGuid():N}.db");
        var chuoiKetNoi = $"Data Source={tepCsdl};Default Timeout=30";

        try
        {
            var tuyChonThuong = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(chuoiKetNoi)
                .Options;

            Guid refundId;

            using (var dbTao = new AppDbContext(tuyChonThuong))
            {
                await dbTao.Database.EnsureCreatedAsync();

                refundId = Guid.NewGuid();

                // Gieo tay thay vì qua EF: cột xmin là cột CSDL sinh (rowVersion) nên EF bỏ nó khỏi
                // INSERT — mà SQLite không có cột hệ thống nào tự sinh giá trị. CẢ HAI bảng ở đây đều
                // mang xmin: thiếu nó ở Payments là câu INSERT đứt ngay ở NOT NULL, còn thiếu ở Refunds
                // là bản ghi mang xmin NULL và lượt UPDATE giành dòng không còn gì để so. Gieo xmin = 1
                // để mọi dòng có đúng hình dạng của PostgreSQL: luôn có xmin, tăng dần theo mỗi lượt ghi.
                // Tham số (Guid, DateTime, decimal) đi qua EF nên được định dạng đúng y như EF đọc lại.
                // FailReason có mặt vì bất biến §4.3 điều 2: dòng Failed thì lý do KHÔNG được null —
                // gieo thiếu là dựng sẵn một trạng thái mà sản phẩm không bao giờ tạo ra.
                //
                // Payments PHẢI có dòng: RefundAttemptService nạp kèm giao dịch gốc
                // (Include(r => r.Payment)) mà quan hệ này bắt buộc, nên EF dịch thành INNER JOIN —
                // thiếu dòng Payment là khoản hoàn biến mất khỏi lượt đọc, service rơi vào nhánh
                // "không tìm thấy", và test lặng lẽ không chạm tới cuộc đua mà nó khai là đang dựng.
                //
                // Khoá ngoại tắt trong riêng lượt gieo: phần còn lại (UserId/TripId) là Guid ngẫu
                // nhiên — dựng đủ chuỗi Users→Trips chỉ để lót đường là dựng cả một mạng dữ liệu
                // không liên quan tới lượt UPDATE đang được ghim. Pragma theo TỪNG kết nối, nên phải
                // mở tường minh rồi gieo trên chính kết nối đó — để EF tự mở thì pragma và câu
                // INSERT có thể rơi vào hai kết nối khác nhau. FK vẫn nguyên vẹn ở CSDL thật; câu
                // INSERT này không phải đường sản phẩm.
                var paymentId = Guid.NewGuid();
                var userId = Guid.NewGuid();

                await dbTao.Database.OpenConnectionAsync();
                await dbTao.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = 0");

                await dbTao.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO "Payments"
                        ("Id", "PaymentCode", "MethodCode", "Amount", "Status", "UserId",
                         "TripId", "SeatNumbers", "CreatedAt", "xmin")
                    VALUES
                        ({paymentId}, {"PM-DUA"}, {"MoMo"}, {SoTien}, {"Success"}, {userId},
                         {Guid.NewGuid()}, {"A1"}, {BayGio.AddHours(-2)}, 1)
                    """);

                await dbTao.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO "Refunds"
                        ("Id", "RefundCode", "Source", "PaymentId", "UserId", "Amount",
                         "Status", "FailReason", "NextRetryAt", "CreatedAt", "xmin")
                    VALUES
                        ({refundId}, {"RF-DUA"}, {"PaymentFailure"}, {paymentId},
                         {userId}, {SoTien}, {"Failed"}, {"Cổng bận của lượt trước"},
                         {BayGio.AddMinutes(-1)}, {BayGio.AddHours(-1)}, 1)
                    """);

                await dbTao.Database.CloseConnectionAsync();
            }

            // Lượt chen: đúng lúc SaveChanges() giành dòng của service sắp chạy, một kết nối khác
            // tăng xmin — y hệt lượt ghi của phiên bản job/phiên khác trên PostgreSQL.
            var chen = new BomXminDoiThuInterceptor(chuoiKetNoi, refundId);
            chen.Nap();

            var tuyChonCoChen = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(chuoiKetNoi)
                .AddInterceptors(chen)
                .Options;

            RefundAttemptOutcome ketQua;
            using (var db = new AppDbContext(tuyChonCoChen))
            {
                var service = new RefundAttemptService(
                    db, gateway, NullLogger<RefundAttemptService>.Instance);

                ketQua = await service.ExecuteAsync(refundId, RefundLogTrigger.ScheduledRetry, BayGio);
            }

            // Thua cuộc đua = bỏ lượt: không gọi cổng, không kết quả, không ghi gì.
            Assert.False(ketQua.Claimed);
            Assert.Null(ketQua.Result);
            Assert.Equal("RF-DUA", ketQua.RefundCode);   // vẫn nhận diện được khoản đã đọc
            Assert.Equal(0, gateway.SoLanGoi);

            using (var dbDoc = new AppDbContext(tuyChonThuong))
            {
                // Dòng còn nguyên trạng thái của lượt chen: lượt thua KHÔNG được ghi đè gì cả —
                // không phải "ghi rồi sửa lại", mà là WHERE không khớp nên không có dòng nào đổi.
                var sau = await dbDoc.Refunds.SingleAsync(r => r.Id == refundId);
                Assert.Equal(RefundStatus.Failed, sau.Status);
                Assert.Equal(BayGio.AddMinutes(-1), sau.NextRetryAt);
                Assert.Equal("Cổng bận của lượt trước", sau.FailReason);

                var xminSau = await dbDoc.Database
                    .SqlQueryRaw<long>("SELECT \"xmin\" AS \"Value\" FROM \"Refunds\" WHERE \"Id\" = {0}", refundId)
                    .SingleAsync();
                Assert.Equal(2, xminSau);

                Assert.Empty(await dbDoc.RefundLogs.ToListAsync());
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try
            {
                File.Delete(tepCsdl);
            }
            catch (IOException)
            {
                // Windows còn giữ file — bản tạm, lượt dọn sau của hệ điều hành.
            }
        }
    }
}

/// <summary>
/// Gài lượt ghi chen cho đúng một khoản, ngay bên trong <c>SavingChangesAsync</c> của lượt
/// <c>SaveChanges()</c> ĐANG chạy: kết nối riêng (context riêng, không mang interceptor — không đệ
/// quy) tăng <c>xmin</c> lên 1, rồi mới để lượt ghi gốc đi tiếp.
///
/// Vì sao phải chen đúng lúc này: EF đọc <c>xmin</c> từ lúc NẠP khoản, còn câu UPDATE dựng
/// <c>WHERE "xmin" = &lt;giá trị cũ&gt;</c> từ bản ghi trong bộ nhớ. Chen trước khi nạp thì lượt sau
/// đọc luôn giá trị mới và không có gì để đua; chen SAU khi ghi xong thì dòng đã bị đổi. Cửa sổ đua
/// của PostgreSQL nằm giữa hai mốc đó — SavingChanges là điểm duy nhất trong pipeline của EF chạm
/// được vào nó.
///
/// Khi khoá <c>["Id"]</c> không khớp dòng nào (khoản biến mất trước lượt ghi), UPDATE không đổi gì —
/// lượt gốc vẫn chạy bình thường, đúng như khi bản ghi bị xoá thật.
/// </summary>
internal sealed class BomXminDoiThuInterceptor : SaveChangesInterceptor
{
    private readonly Guid _refundId;
    private readonly DbContextOptions<AppDbContext> _tuyChonKetNoiRieng;
    private bool _daNap;

    public BomXminDoiThuInterceptor(string chuoiKetNoi, Guid refundId)
    {
        _refundId = refundId;
        _tuyChonKetNoiRieng = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(chuoiKetNoi)
            .Options;
    }

    /// <summary>Nạp một lần: lượt <c>SaveChanges()</c> kế tiếp sẽ có lượt chen.</summary>
    public void Nap() => _daNap = true;

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (_daNap)
        {
            _daNap = false;   // một phát duy nhất — lượt ghi dọn dẹp sau đó (nếu có) không bị chen nữa

            await using var ketNoiRieng = new AppDbContext(_tuyChonKetNoiRieng);
            await ketNoiRieng.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE \"Refunds\" SET \"xmin\" = \"xmin\" + 1 WHERE \"Id\" = {_refundId}",
                cancellationToken);
        }

        return await base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}
