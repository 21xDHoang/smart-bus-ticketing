using Microsoft.EntityFrameworkCore;
using SmartBus.Api.Data;
using SmartBus.Api.Dtos.TicketValidations;
using SmartBus.Api.Entities;
using SmartBus.Api.Services;

namespace SmartBus.Tests;

/// <summary>
/// Test bộ phận GHI NHẬT KÝ SOÁT VÉ — <see cref="TicketValidationRecorder"/> (task *"Xử lý ngoại
/// tuyến: lưu tạm lượt soát khi mất mạng, đồng bộ lại sau"*, dòng 18 Sprint 4, Phùng Duy Hoàng).
///
/// Gọi thẳng service, KHÔNG dựng host: service chỉ nhận dữ liệu đã tính sẵn rồi ghi một dòng, không
/// xác thực chữ ký cũng không tra vé (hợp đồng ở doc-comment <see cref="ITicketValidationRecorder"/>).
///
/// Dùng provider InMemory như phần còn lại của bộ test. Không seed Users/Tickets: InMemory không
/// cưỡng chế khoá ngoài, mà service không đọc bảng nào khác ngoài <c>TicketValidations</c>.
///
/// ⚠️ InMemory cũng KHÔNG cưỡng chế chỉ mục UNIQUE của <c>ClientScanId</c>, nên các bài "gửi lại"
/// ở đây chỉ kiểm được ĐƯỜNG ĐỌC-TRƯỚC (đường tuần tự) — đường ĐUA hiếm (hai lượt chồng nhau cùng
/// đâm vào chỉ mục, service đọc-lại rồi nuốt lỗi) chỉ chạy được trên engine cưỡng chế unique index,
/// tức SQLite: xem <c>TicketValidationRecorderRaceTests</c>. Bộ đôi này cố ý tách hai file cùng lệ
/// "mỗi lớp test tự chép helper" của dự án.
/// </summary>
public class TicketValidationRecorderTests
{
    // ── Đường online (không ClientScanId) ────────────────────────────────────────────────────────

    [Fact]
    public async Task Ghi_luot_online_thieu_ClientScanId_va_ScannedAt()
    {
        await using var db = TaoDb();
        var service = new TicketValidationRecorder(db);
        var nguoiSoat = Guid.NewGuid();
        var veId = Guid.NewGuid();
        var truocKhiGoi = DateTime.UtcNow;

        var ketQua = await service.RecordAsync(
            TaoRequest(ticketId: veId), nguoiSoat, CancellationToken.None);

        Assert.True(ketQua.Success);
        var dong = Assert.Single(db.TicketValidations);
        Assert.Equal(dong.Id, ketQua.Data!.Id);
        Assert.Equal(nguoiSoat, dong.ValidatedByUserId);
        Assert.Equal(veId, dong.TicketId);
        Assert.Equal(TicketValidationResult.Valid, dong.Result);

        // Lượt online: máy quét không khai giờ, server tự lấy giờ mình — và giờ đó phải là UTC vì
        // Npgsql chỉ ghi được Utc (xem NormalizeToUtc ở service).
        Assert.Null(dong.ClientScanId);
        Assert.Equal(DateTimeKind.Utc, dong.ScannedAt.Kind);
        Assert.InRange(dong.ScannedAt, truocKhiGoi, DateTime.UtcNow);
        Assert.Equal(DateTimeKind.Utc, dong.CreatedAt.Kind);
    }

    [Fact]
    public async Task Hai_luot_online_khong_ClientScanId_tao_hai_dong()
    {
        await using var db = TaoDb();
        var service = new TicketValidationRecorder(db);

        // Cùng một mã vé, hai lượt quét riêng — KHÔNG chống trùng: chỉ mục UNIQUE của Postgres cho
        // phép nhiều NULL (doc-comment cột ClientScanId), và nhật ký soát vé là bảng append-only mà
        // mỗi lần quét thật phải để lại một dòng (kể cả quét lặp của cùng một khách).
        var lanDau = await service.RecordAsync(TaoRequest(), Guid.NewGuid(), CancellationToken.None);
        var lanHai = await service.RecordAsync(TaoRequest(), Guid.NewGuid(), CancellationToken.None);

        Assert.True(lanDau.Success);
        Assert.True(lanHai.Success);
        Assert.NotEqual(lanDau.Data!.Id, lanHai.Data!.Id);
        Assert.Equal(2, db.TicketValidations.Count());
    }

    // ── Gửi lại cùng ClientScanId (đường tuần tự — đọc trước) ───────────────────────────────────

    [Fact]
    public async Task Gui_lai_cung_ClientScanId_tra_ve_dung_dong_da_ghi()
    {
        await using var db = TaoDb();
        var service = new TicketValidationRecorder(db);
        var nguoiSoat = Guid.NewGuid();
        var clientScanId = Guid.NewGuid();

        var lanDau = await service.RecordAsync(
            TaoRequest(clientScanId: clientScanId), nguoiSoat, CancellationToken.None);
        var lanHai = await service.RecordAsync(
            TaoRequest(clientScanId: clientScanId), nguoiSoat, CancellationToken.None);

        Assert.True(lanDau.Success);
        Assert.True(lanHai.Success);
        Assert.Equal(lanDau.Data!.Id, lanHai.Data!.Id);
        Assert.Single(db.TicketValidations);
    }

    [Fact]
    public async Task Gui_lai_tra_ve_ket_qua_goc_chu_khong_phai_ket_qua_vua_tinh()
    {
        await using var db = TaoDb();
        var service = new TicketValidationRecorder(db);
        var clientScanId = Guid.NewGuid();

        // Lượt gốc kết luận Vé hỏng (Expired). Lần gửi lại — máy quét có thể đã tính lại theo trạng
        // thái mới nhất và ra kết quả khác — vẫn phải nhận ĐÚNG kết quả đã ghi: sổ nhật ký là append
        // only, sửa câu trả lời cho một lượt đã ghi là bịa lịch sử (hợp đồng ở ITicketValidationRecorder).
        var lanDau = await service.RecordAsync(
            TaoRequest(clientScanId: clientScanId, result: TicketValidationResult.Expired),
            Guid.NewGuid(), CancellationToken.None);
        var lanHai = await service.RecordAsync(
            TaoRequest(clientScanId: clientScanId, result: TicketValidationResult.AlreadyUsed),
            Guid.NewGuid(), CancellationToken.None);

        Assert.True(lanHai.Success);
        Assert.Equal(TicketValidationResult.Expired, lanHai.Data!.Result);
        Assert.Equal(lanDau.Data!.Id, lanHai.Data.Id);
        Assert.Single(db.TicketValidations);
    }

    [Fact]
    public async Task Gui_lai_luot_da_ghi_tu_truoc_qua_lau_van_thanh_cong()
    {
        await using var db = TaoDb();
        var service = new TicketValidationRecorder(db);
        var nguoiSoat = Guid.NewGuid();
        var clientScanId = Guid.NewGuid();
        var gioQuetCu = DateTime.UtcNow.AddHours(-100);

        // Dòng đã nằm trong nhật ký từ trước (mô phỏng lượt đã đồng bộ thành công hôm trước), và
        // hàng đợi kẹt gửi lại y nguyên payload cũ có giờ quét đã quá trần 72 giờ. Phép ĐỌC-TRƯỚC
        // phải chạy trước cửa sổ ScannedAt, nếu không lượt gửi lại vô hại này bị trả 400 oan —
        // đúng thứ §6 docs/34 cấm (máy quét sẽ coi là lỗi không thử lại được rồi vứt lượt đi).
        db.TicketValidations.Add(new TicketValidation
        {
            ValidatedByUserId = nguoiSoat,
            ScannedCode = "SBT1:luot-cu",
            Result = TicketValidationResult.Valid,
            TicketId = Guid.NewGuid(),
            ScannedAt = gioQuetCu,
            ClientScanId = clientScanId,
            CreatedAt = gioQuetCu,
        });
        await db.SaveChangesAsync();

        var ketQua = await service.RecordAsync(
            TaoRequest(clientScanId: clientScanId, scannedAt: gioQuetCu),
            nguoiSoat, CancellationToken.None);

        Assert.True(ketQua.Success);
        Assert.Single(db.TicketValidations);
    }

    // ── Cửa sổ hợp lý của ScannedAt (docs/34 §4.2) ──────────────────────────────────────────────

    [Fact]
    public async Task ScannedAt_tuong_lai_qua_5_phut_bi_tu_choi()
    {
        await using var db = TaoDb();
        var service = new TicketValidationRecorder(db);

        var ketQua = await service.RecordAsync(
            TaoRequest(clientScanId: Guid.NewGuid(), scannedAt: DateTime.UtcNow.AddMinutes(6)),
            Guid.NewGuid(), CancellationToken.None);

        Assert.False(ketQua.Success);
        Assert.Equal(ServiceErrorKind.Invalid, ketQua.ErrorKind);
        Assert.Contains("scannedAt", ketQua.Errors!.Keys);
        Assert.Empty(db.TicketValidations);
    }

    [Fact]
    public async Task ScannedAt_tuong_lai_trong_dung_sai_dong_ho_van_nhan()
    {
        await using var db = TaoDb();
        var service = new TicketValidationRecorder(db);

        // 4 phút nhanh hơn server vẫn nằm trong dung sai đồng hồ — máy quét lệch vài phút là chuyện
        // thường, từ chối nó là bắt cả đội xe đi chỉnh đồng hồ vô ích.
        var ketQua = await service.RecordAsync(
            TaoRequest(clientScanId: Guid.NewGuid(), scannedAt: DateTime.UtcNow.AddMinutes(4)),
            Guid.NewGuid(), CancellationToken.None);

        Assert.True(ketQua.Success);
        Assert.Single(db.TicketValidations);
    }

    [Fact]
    public async Task ScannedAt_qua_khu_qua_72_gio_bi_tu_choi()
    {
        await using var db = TaoDb();
        var service = new TicketValidationRecorder(db);

        var ketQua = await service.RecordAsync(
            TaoRequest(clientScanId: Guid.NewGuid(), scannedAt: DateTime.UtcNow.AddHours(-73)),
            Guid.NewGuid(), CancellationToken.None);

        Assert.False(ketQua.Success);
        Assert.Equal(ServiceErrorKind.Invalid, ketQua.ErrorKind);
        Assert.Contains("scannedAt", ketQua.Errors!.Keys);
        Assert.Empty(db.TicketValidations);
    }

    [Fact]
    public async Task ScannedAt_hom_truoc_van_dong_bo_duoc()
    {
        await using var db = TaoDb();
        var service = new TicketValidationRecorder(db);
        var homTruoc = DateTime.UtcNow.AddHours(-25);

        // "Mất mạng hàng giờ, thậm chí hôm sau" là kịch bản thiết kế của hàng đợi (doc-comment cột
        // ScannedAt) — lượt hôm qua phải qua được, và qua với ĐÚNG giờ máy quét khai, không bị ghi
        // đè bằng giờ đồng bộ: thống kê theo chuyến (dòng 24) đọc chính cột này.
        var ketQua = await service.RecordAsync(
            TaoRequest(clientScanId: Guid.NewGuid(), scannedAt: homTruoc),
            Guid.NewGuid(), CancellationToken.None);

        Assert.True(ketQua.Success);
        Assert.Equal(homTruoc, ketQua.Data!.ScannedAt);
    }

    [Fact]
    public async Task ScannedAt_khong_vung_gio_duoc_dong_nhan_thanh_Utc()
    {
        await using var db = TaoDb();
        var service = new TicketValidationRecorder(db);
        var khongVung = DateTime.SpecifyKind(DateTime.UtcNow.AddHours(-2), DateTimeKind.Unspecified);

        // Client gửi chuỗi ISO thiếu hậu tố Z/offset thì System.Text.Json trả Kind=Unspecified —
        // hợp đồng api-contract quy định mọi mốc thời gian là UTC nên dán nhãn Utc là đúng quy ước;
        // nếu không dán, Npgsql ném ngay lúc SaveChanges (500).
        var ketQua = await service.RecordAsync(
            TaoRequest(clientScanId: Guid.NewGuid(), scannedAt: khongVung),
            Guid.NewGuid(), CancellationToken.None);

        Assert.True(ketQua.Success);
        Assert.Equal(khongVung.Ticks, ketQua.Data!.ScannedAt.Ticks);
        Assert.Equal(DateTimeKind.Utc, ketQua.Data.ScannedAt.Kind);
    }

    [Fact]
    public async Task ScannedAt_gio_dia_phuong_doi_dung_lech_mui_gio()
    {
        await using var db = TaoDb();
        var service = new TicketValidationRecorder(db);
        var gioMayQuet = DateTime.SpecifyKind(DateTime.UtcNow.AddHours(-1), DateTimeKind.Local);

        // Chuỗi ISO mang offset (ví dụ "+07:00" của máy quét trong nước) cũng về tay service dưới
        // dạng Kind=Local — nhánh này phải ĐỔI GIỜ thật bằng ToUniversalTime, không dán nhãn: dán
        // nhãn Utc lên 09:00+07:00 là ghi 09:00Z, lệch đúng 7 tiếng. Trên CI chạy múi giờ UTC phép
        // kiểm này thoái hoá thành trùng nhau, nhưng trên máy dev vi-VN nó ghim đúng phép đổi.
        var ketQua = await service.RecordAsync(
            TaoRequest(clientScanId: Guid.NewGuid(), scannedAt: gioMayQuet),
            Guid.NewGuid(), CancellationToken.None);

        Assert.True(ketQua.Success);
        Assert.Equal(gioMayQuet.ToUniversalTime(), ketQua.Data!.ScannedAt);
    }

    // ── Bất biến TicketId ⟺ NotFound ────────────────────────────────────────────────────────────

    [Fact]
    public async Task NotFound_kem_TicketId_bi_tu_choi()
    {
        await using var db = TaoDb();
        var service = new TicketValidationRecorder(db);

        // Tổ hợp lệch bất biến của bảng (TicketId IS NULL ⟺ Result = 'NotFound') — lỗi lập trình
        // của bên gọi, phải lộ ra thành 400 kèm trường chứ không được âm thầm ghi dòng mâu thuẫn.
        var ketQua = await service.RecordAsync(
            new RecordTicketValidationRequest
            {
                ScannedCode = "SBT1:ma-khong-doc-ra-ve",
                Result = TicketValidationResult.NotFound,
                TicketId = Guid.NewGuid(),
            },
            Guid.NewGuid(), CancellationToken.None);

        Assert.False(ketQua.Success);
        Assert.Equal(ServiceErrorKind.Invalid, ketQua.ErrorKind);
        Assert.Contains("ticketId", ketQua.Errors!.Keys);
        Assert.Empty(db.TicketValidations);
    }

    [Fact]
    public async Task Ket_qua_khac_NotFound_thieu_TicketId_bi_tu_choi()
    {
        await using var db = TaoDb();
        var service = new TicketValidationRecorder(db);

        var ketQua = await service.RecordAsync(
            new RecordTicketValidationRequest
            {
                ScannedCode = "SBT1:ma-khong-doc-ra-ve",
                Result = TicketValidationResult.Valid,
                TicketId = null,
            },
            Guid.NewGuid(), CancellationToken.None);

        Assert.False(ketQua.Success);
        Assert.Equal(ServiceErrorKind.Invalid, ketQua.ErrorKind);
        Assert.Contains("ticketId", ketQua.Errors!.Keys);
        Assert.Empty(db.TicketValidations);
    }

    // ── ScannedCode ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ScannedCode_rong_hoac_khoang_trang_bi_tu_choi()
    {
        await using var db = TaoDb();
        var service = new TicketValidationRecorder(db);

        var rong = await service.RecordAsync(
            TaoRequest(scannedCode: ""), Guid.NewGuid(), CancellationToken.None);
        var khoangTrang = await service.RecordAsync(
            TaoRequest(scannedCode: "   "), Guid.NewGuid(), CancellationToken.None);

        Assert.False(rong.Success);
        Assert.Contains("scannedCode", rong.Errors!.Keys);
        Assert.False(khoangTrang.Success);
        Assert.Contains("scannedCode", khoangTrang.Errors!.Keys);
        Assert.Empty(db.TicketValidations);
    }

    [Fact]
    public async Task ScannedCode_dai_qua_200_ky_tu_bi_tu_choi()
    {
        await using var db = TaoDb();
        var service = new TicketValidationRecorder(db);

        // Trần cột varchar(200) — để lọt là Postgres ném 22001 thành 500, còn SQLite/InMemory của
        // tầng test không chặn gì; phép kiểm phải nằm ở service để hai môi trường cư xử như nhau.
        var ketQua = await service.RecordAsync(
            TaoRequest(scannedCode: new string('x', 201)), Guid.NewGuid(), CancellationToken.None);

        Assert.False(ketQua.Success);
        Assert.Contains("scannedCode", ketQua.Errors!.Keys);
        Assert.Empty(db.TicketValidations);
    }

    [Fact]
    public async Task ScannedCode_dung_200_ky_tu_van_ghi_duoc()
    {
        await using var db = TaoDb();
        var service = new TicketValidationRecorder(db);

        // Biên trên của cặp biên 200/201 — 200 phải qua, nếu không thì trần thật của code là 199 và
        // sớm muộn có lượt QR dài đúng 200 bị chặn oan.
        var ketQua = await service.RecordAsync(
            TaoRequest(scannedCode: new string('x', 200)), Guid.NewGuid(), CancellationToken.None);

        Assert.True(ketQua.Success);
        Assert.Single(db.TicketValidations);
    }

    // =======================================================================================
    // Helper dựng dữ liệu — mỗi lớp test tự chép, không có lớp base chung (lệ đang có của dự án)
    // =======================================================================================

    private static AppDbContext TaoDb()
        => new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    /// <summary>
    /// Lượt soát HỢP LỆ theo mặc định — các bài chỉ muốn chạm một phép kiểm thì để nguyên phần còn
    /// lại. Tổ hợp CỐ Ý sai (lệch bất biến TicketId/Result, mã QR rỗng) được dựng tay ngay trong bài
    /// kiểm tương ứng, không luồn qua helper này.
    /// </summary>
    private static RecordTicketValidationRequest TaoRequest(
        Guid? clientScanId = null,
        DateTime? scannedAt = null,
        Guid? ticketId = null,
        TicketValidationResult result = TicketValidationResult.Valid,
        string scannedCode = "SBT1:ve-gia-lap")
        => new()
        {
            ScannedCode = scannedCode,
            TicketId = result == TicketValidationResult.NotFound ? ticketId : ticketId ?? Guid.NewGuid(),
            Result = result,
            ClientScanId = clientScanId,
            ScannedAt = scannedAt,
        };
}
