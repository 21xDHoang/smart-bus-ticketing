using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SmartBus.Api.Data;
using SmartBus.Api.Dtos.TicketValidations;
using SmartBus.Api.Entities;
using SmartBus.Api.Services;

namespace SmartBus.Tests;

/// <summary>
/// Test ĐƯỜNG ĐUA của bộ phận ghi nhật ký soát vé — <see cref="TicketValidationRecorder"/> (task
/// *"Xử lý ngoại tuyến: lưu tạm lượt soát khi mất mạng, đồng bộ lại sau"*, dòng 18 Sprint 4, Phùng
/// Duy Hoàng).
///
/// <para>
/// <b>Vì sao phải có file này.</b> Chốt chống trùng của hàng đợi ngoại tuyến là chỉ mục UNIQUE trên
/// <c>ClientScanId</c> (AppDbContext.TicketValidation.cs §4.1). Hai lượt gửi lại chồng nhau — ca
/// hiếm nhưng có thật khi máy quét bấm gửi đúng lúc mạng chập chờn — cùng đọc không thấy dòng nào
/// rồi cùng ghi; lượt thua đâm vào chỉ mục và nhận <c>DbUpdateException</c>, service đọc lại và trả
/// về dòng mà lượt thắng đã ghi. <c>TicketValidationRecorderTests</c> chỉ kiểm được đường tuần tự
/// (đọc-trước), vì <c>Microsoft.EntityFrameworkCore.InMemory</c> không cưỡng chế unique index nào
/// cả. File này chạy nhánh đua trên SQLite — engine DUY NHẤT chạy trong tiến trình mà cưỡng chế
/// được chỉ mục, cùng lối đã dùng ở <c>SeatHoldRaceFlowApiTests</c>.
/// </para>
///
/// <para>
/// <b>Dựng khe đua bằng interceptor.</b> Ở môi trường thật, khe này do hai request đồng thời tự tìm
/// thấy nhau; trong test không có cách nào cho phép đọc-trước "nhìn hụt" một dòng chưa tồn tại —
/// phải chèn dòng đối thủ SAU phép đọc, tức là ngay trước lúc service ghi.
/// <c>SaveChangesInterceptor.SavingChangesAsync</c> là điểm móc duy nhất chen được vào giữa hai bước
/// của service mà KHÔNG phải sửa code nghiệp vụ (ranh giới 2.4: người kiểm thử không sửa code để
/// test xanh).
/// </para>
///
/// ⚠️ SQLite cưỡng chế khoá ngoại, nên tài khoản phụ xe phải là dòng thật (Role + User); riêng
/// TicketId mồ côi là CỐ Ý ở bài thứ hai — nó là lỗi đại diện cho "DbUpdateException không phải đua
/// ClientScanId", thứ phải được ném lại chứ không nuốt.
/// </summary>
public class TicketValidationRecorderRaceTests : IDisposable
{
    private readonly string _tepCsdl =
        Path.Combine(Path.GetTempPath(), $"smartbus-soat-{Guid.NewGuid():N}.db");

    private string ChuoiKetNoi => $"Data Source={_tepCsdl};Default Timeout=30";

    [Fact]
    public async Task Hai_luot_chong_nhau_cung_ClientScanId_tra_ve_dong_thang_khong_no_500()
    {
        var clientScanId = Guid.NewGuid();
        var doiThuId = Guid.NewGuid();
        var chenGiua = new ChenLuotSoatDoiThuInterceptor(ChuoiKetNoi);

        await using var db = await DungDbAsync(chenGiua);
        var nguoiSoatId = await SeedNguoiSoatAsync(db);
        var service = new TicketValidationRecorder(db);

        // Nạp đạn: lượt "đối thủ" mang CÙNG ClientScanId sẽ được chèn bằng kết nối khác ngay trước
        // SaveChanges của service — mô phỏng đúng chuyện lượt song sinh đã ghi xong trước.
        chenGiua.Nap(new TicketValidation
        {
            Id = doiThuId,
            ValidatedByUserId = nguoiSoatId,
            ScannedCode = "SBT1:luot-doi-thu",
            Result = TicketValidationResult.NotFound,
            TicketId = null,
            ScannedAt = DateTime.UtcNow,
            ClientScanId = clientScanId,
            CreatedAt = DateTime.UtcNow,
        });

        var ketQua = await service.RecordAsync(
            TaoLuotNotFound(clientScanId), nguoiSoatId, CancellationToken.None);

        // Cả hai vế của hợp đồng idempotent: THÀNH CÔNG (không nổ 500 vì lỗi unique), và trả về
        // ĐÚNG dòng mà lượt thắng đã ghi — kết quả gốc đứng vững, không đẻ dòng thứ hai.
        Assert.True(ketQua.Success);
        Assert.Equal(doiThuId, ketQua.Data!.Id);

        // Và nó KHÔNG đẻ dòng thứ hai: đúng một dòng dưới CSDL. Đếm bằng AsNoTracking để không dính
        // entity nào còn trong ChangeTracker.
        Assert.Equal(1, await db.TicketValidations.AsNoTracking().CountAsync());

        // Entity Added mồ côi đã bị gỡ trước khi trả về — DbContext là scoped dùng chung cả request,
        // để sót thì lần SaveChanges kế tiếp của endpoint (ví dụ đánh dấu vé Used) sẽ nổ lại đúng lỗi
        // unique này và lần này không còn ai hiểu vì sao.
        Assert.Empty(db.ChangeTracker.Entries<TicketValidation>());
    }

    [Fact]
    public async Task Loi_CSDL_khong_phai_dua_ClientScanId_thi_nem_lai_chu_khong_nuot()
    {
        await using var db = await DungDbAsync();
        var nguoiSoatId = await SeedNguoiSoatAsync(db);
        var service = new TicketValidationRecorder(db);

        // Vé không tồn tại → SQLite chặn khoá ngoại → DbUpdateException. Lượt này CÓ ClientScanId
        // nhưng đọc lại không thấy dòng nào: không phải đua, mà là lỗi thật của bên gọi — phải ném
        // lại ồn ào thay vì nuốt thành công giả (nuốt ở đây là giấu mất một vé rác lọt vào nhật ký
        // kiểm toán).
        await Assert.ThrowsAsync<DbUpdateException>(() => service.RecordAsync(
            new RecordTicketValidationRequest
            {
                ScannedCode = "SBT1:ve-khong-ton-tai",
                TicketId = Guid.NewGuid(),
                Result = TicketValidationResult.Valid,
                ClientScanId = Guid.NewGuid(),
                ScannedAt = DateTime.UtcNow,
            },
            nguoiSoatId,
            CancellationToken.None));

        Assert.Empty(db.ChangeTracker.Entries<TicketValidation>());
        Assert.Equal(0, await db.TicketValidations.AsNoTracking().CountAsync());
    }

    // =======================================================================================
    // Helper dựng dữ liệu — mỗi lớp test tự chép, không có lớp base chung (lệ đang có của dự án)
    // =======================================================================================

    /// <summary>Context trên tệp SQLite riêng của bài test, schema dựng sẵn. Tuỳ chọn ghim interceptor.</summary>
    private async Task<AppDbContext> DungDbAsync(ChenLuotSoatDoiThuInterceptor? chenGiua = null)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(ChuoiKetNoi);

        if (chenGiua is not null)
        {
            optionsBuilder.AddInterceptors(chenGiua);
        }

        var db = new AppDbContext(optionsBuilder.Options);
        await db.Database.EnsureCreatedAsync();

        return db;
    }

    /// <summary>
    /// Tài khoản phụ xe tối thiểu — SQLite cưỡng chế khoá ngoại nên <c>ValidatedByUserId</c> phải
    /// trỏ tới User thật.
    ///
    /// KHÔNG thêm Role ở đây: bốn vai trò đã có sẵn trong bảng Roles ngay sau <c>EnsureCreatedAsync</c>
    /// — <c>AppDbContext.UserRoles.cs</c> seed chúng bằng <c>HasData</c>, và <c>EnsureCreated</c> áp
    /// dụng luôn <c>HasData</c> của model (khác đường migration, nơi nó nằm trong file migration).
    /// Thêm nữa là chết ngay với <c>UNIQUE constraint failed: Roles.Code</c>.
    /// </summary>
    private static async Task<Guid> SeedNguoiSoatAsync(AppDbContext db)
    {
        var nguoiSoat = new User
        {
            PhoneNumber = "0900000001",
            FullName = "Phụ xe test",
            PasswordHash = "hash-khong-dung-toi-trong-test",
            IsActive = true,
            RoleId = RoleIds.Driver,
        };

        db.Users.Add(nguoiSoat);
        await db.SaveChangesAsync();

        return nguoiSoat.Id;
    }

    /// <summary>
    /// Lượt "không đọc ra vé" — vế duy nhất KHÔNG cần vé thật, nên cả dòng đối thủ lẫn dòng của
    /// service đều dùng được mà không kéo thêm khoá ngoại nào vào bài đua: lỗi duy nhất có thể xảy ra
    /// trên đường ghi là chính chỉ mục UNIQUE cần đo.
    /// </summary>
    private static RecordTicketValidationRequest TaoLuotNotFound(Guid clientScanId)
        => new()
        {
            ScannedCode = "SBT1:khong-doc-ra-ve",
            TicketId = null,
            Result = TicketValidationResult.NotFound,
            ClientScanId = clientScanId,
            ScannedAt = DateTime.UtcNow,
        };

    public void Dispose()
    {
        // Pool giữ handle trên tệp, không xoá hết thì File.Delete ném IOException vì tệp "đang được
        // dùng bởi tiến trình khác" — dù "tiến trình khác" chính là mình.
        SqliteConnection.ClearAllPools();

        try
        {
            File.Delete(_tepCsdl);
        }
        catch (IOException)
        {
            // Tệp tạm trong thư mục temp: không xoá được cũng không ảnh hưởng kết quả test, và làm
            // đỏ một bài test đã chạy xong chỉ vì dọn dẹp là sai chỗ để báo lỗi.
        }
    }
}

/// <summary>
/// Chèn một lượt soát của NGƯỜI KHÁC mang cùng <c>ClientScanId</c> vào đúng khe giữa hai bước của
/// <c>TicketValidationRecorder</c>: sau khi phép đọc-trước đã trả về rỗng, trước khi
/// <c>SaveChangesAsync</c> ghi.
///
/// <para>
/// <b>Vì sao ghi bằng kết nối KHÁC.</b> Chèn ngay trên DbContext đang ghi thì dòng đối thủ nằm cùng
/// một lượt ghi với dòng của người thua, và hai dòng ấy đụng nhau trong CÙNG một câu lệnh — hỏng theo
/// kiểu khác hẳn. Một kết nối riêng, commit xong rồi mới trả về, mô phỏng đúng chuyện người thắng đã
/// ghi xong trước: lượt ghi sau đó của service đâm vào chỉ mục UNIQUE và nhận
/// <c>DbUpdateException</c> — đúng nhánh cần kiểm.
/// </para>
///
/// <para>
/// Cùng lối <c>ChenLuotGiuDoiThuInterceptor</c> của <c>SeatHoldRaceFlowApiTests</c>; chép riêng theo
/// lệ "mỗi lớp test tự chép helper" của dự án, và bản này nạp ĐÚNG MỘT lượt rồi tự tắt.
/// </para>
/// </summary>
internal sealed class ChenLuotSoatDoiThuInterceptor : SaveChangesInterceptor
{
    private readonly string _chuoiKetNoi;

    private TicketValidation? _doiThu;

    private bool _daNap;

    public ChenLuotSoatDoiThuInterceptor(string chuoiKetNoi) => _chuoiKetNoi = chuoiKetNoi;

    /// <summary>Nạp đạn cho ĐÚNG MỘT lượt ghi kế tiếp — lượt ghi của service, không phải lượt seed nào.</summary>
    public void Nap(TicketValidation doiThu)
    {
        _doiThu = doiThu;
        _daNap = true;
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (_daNap)
        {
            _daNap = false;

            // Context KHÔNG ghim interceptor này (dựng thẳng từ chuỗi kết nối), nên lượt ghi của nó
            // không quay lại gọi chính nó.
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(_chuoiKetNoi)
                .Options;

            await using var ketNoiKhac = new AppDbContext(options);

            ketNoiKhac.TicketValidations.Add(_doiThu!);

            await ketNoiKhac.SaveChangesAsync(cancellationToken);
        }

        return await base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}
