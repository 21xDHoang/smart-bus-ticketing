using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using SmartBus.Api.Data;
using SmartBus.Api.Entities;
using SmartBus.Api.Services;
using UserEntity = SmartBus.Api.Entities.User;

namespace SmartBus.Tests;

/// <summary>
/// Test API huỷ / đổi vé <b>và tính phí</b> — US 5 *"Hủy &amp; Đổi vé"*, Sprint 4 dòng 12 (Giàng A Vàng).
///
/// <b>Vì sao file này tồn tại.</b> <see cref="TicketRequestReviewWiringTests"/> (dòng 5, Phùng Duy Hoàng)
/// đã chứng minh nối dây của hai endpoint duyệt và ghi rõ phần còn lại là của file này:
/// *"Ma trận đầy đủ của các nhánh 409 — vé đã soát ở cổng, chuyến xin đổi sang đã huỷ, ghế bị người
/// khác mua mất trong lúc chờ duyệt — là việc của dòng 12 (Giàng A Vàng); ở đây chỉ giữ đúng những
/// nhánh chứng minh nối dây còn sống."* File này KHÔNG chép lại phần nối dây đó (DI, 401, 403, 404,
/// luồng duyệt thành công, nhật ký kiểm toán) — chỉ phủ phần được giao.
///
/// <b>Phạm vi và bốn dòng backlog của cùng một user story</b> (bảng phân công Sprint 4, sheet
/// "Sprint 4"), để không ai đọc nhầm file này là đã phủ cả US 5:
///
/// | Dòng | Task | Người | Trạng thái trong repo |
/// |---|---|---|---|
/// | 3 | *"API gửi yêu cầu huỷ vé + kiểm tra điều kiện thời gian"* | Trần Trung Hiếu | ⛔ <b>chưa tồn tại</b> |
/// | 4 | *"API gửi yêu cầu đổi chuyến + tính chênh lệch giá"* | Nguyễn Duy Kiên | ⛔ <b>chưa tồn tại</b> |
/// | 5 | *"API duyệt / từ chối yêu cầu huỷ - đổi vé"* | Phùng Duy Hoàng | ✅ có — <b>file này phủ</b> |
/// | 6 | *"Service tính phí huỷ theo quy định (trước 24h / 12h / 2h)"* | Nguyễn Duy Kiên | ⛔ <b>chưa tồn tại</b> |
///
/// 🔴 <b>Hai mắt xích chưa có nên KHÔNG thể test, và file này cố ý không giả vờ test:</b>
///   • <i>Hai endpoint gửi yêu cầu</i> (<c>POST /cancellation-requests</c>, <c>POST /ticket-changes</c>):
///     <c>docs/api-contract.md:3722-3723</c> ghi thẳng *"chưa có trong tài liệu này"*, và không có
///     controller nào của hai route đó trong <c>SmartBus.Api</c> — chỉ có hai lớp <c>…ReviewController</c>
///     của nửa duyệt. Viết test gọi chúng là tạo ra file không biên dịch được, CI đỏ, vi phạm DoD.
///   • <i>Service tính phí huỷ</i> (dòng 6): không tồn tại service nào tên <c>*CancellationFee*</c> /
///     <c>*FeePolicy*</c> trong repo. Nghĩa là <b>ba mốc 24h / 12h / 2h chưa được cài ở đâu cả</b> —
///     và đúng vì thế mà chúng KHÔNG có test nào ở đây: không có gì để gọi.
///
/// <b>Vậy phần "tính phí" của tên task được phủ bằng gì?</b> Bằng đúng thứ đã có thật và đã được
/// chốt: <c>CancellationFee</c> là con số <b>CHỤP LẠI</b> lúc khách gửi (doc-comment
/// <see cref="CancellationRequest.CancellationFee"/>), và số tiền PHẢI HOÀN là trường suy ra
/// <c>Ticket.Price - CancellationFee</c> chứ không phải một cột. Ba phép kiểm ở Phần 1 ghim đúng
/// tính bất biến đó — trong đó phép kiểm đáng giá nhất là <i>duyệt với <c>DepartureAt</c> đã đặt xa
/// giờ chạy mà phí vẫn ra nguyên con số đã chụp</i>: nó đỏ ngay nếu ai đó "cải tiến" lượt duyệt bằng
/// cách tính lại phí theo quy định 24h/12h/2h tại thời điểm duyệt.
///
/// <b>Ba tầng được phủ:</b>
///   1. <b>HTTP thật</b> trên app thật (<see cref="TestAppFactory"/>) — ma trận 409 và các nhánh 400.
///   2. <b>Dữ liệu sau lượt gọi</b> — mọi ca 409 còn phải chứng minh <b>không ghi gì</b>: yêu cầu
///      vẫn <c>Pending</c>, vé cũ vẫn <c>Paid</c>, không vé mới nào được phát hành. Một lượt 409 mà
///      đã lỡ huỷ vé là lỗi tiền thật, không phải lỗi hiển thị.
///   3. <b>DDL PostgreSQL thật</b> — <see cref="AppDbContext"/> dựng bằng Npgsql rồi
///      <c>GenerateCreateScript()</c>, đúng lối đã ghi ở cuối mục *"Huỷ &amp; Đổi vé"* của
///      <c>docs/api-contract.md</c>: *"muốn kiểm phải dựng DDL bằng <c>GenerateCreateScript()</c> với
///      chuỗi kết nối Npgsql giả"*.
///
/// <b>Vì sao Phần 5 phải tồn tại — chỉ thị viết thẳng cho dòng này.</b> <c>docs/32-csdl-huy-doi-ve.md</c>
/// §6, mục *"Việc còn lại của người khác"*, giao đích danh: *"**Giàng A Vàng** (dòng 12–13) — test API
/// huỷ/đổi + tính phí. ⚠️ InMemory **không** cưỡng chế unique index/FK (§4.4), nên **đừng** viết test
/// "gửi hai yêu cầu cho một vé thì cái thứ hai phải lỗi" rồi tin nó kiểm được §4.1 — trên InMemory nó
/// sẽ **xanh dù ràng buộc không tồn tại**. Muốn kiểm thật thì phải chạy trên PostgreSQL."*
///
/// File này tuân đúng chỉ thị đó: <b>KHÔNG có ca nào</b> kiểu "gửi hai yêu cầu cho một vé rồi mong cái
/// thứ hai lỗi" chạy trên InMemory — một ca như thế sẽ xanh kể cả khi chỉ mục §4.1 bị xoá khỏi
/// <c>AppDbContext</c>, tức là một test nói dối. Thay vào đó §4.1 được ghim ở <b>Phần 5</b> bằng DDL
/// sinh từ chính model — chỗ duy nhất trong môi trường không có PostgreSQL nhìn thấy được chỉ mục.
///
/// ⚠️ <b>Điều Phần 5 vẫn KHÔNG chứng minh được, để không ai đọc quá lời:</b> nó chứng minh ràng buộc
/// được <b>KHAI</b> đúng — không chứng minh nó <b>NỔ</b> khi có hai lượt ghi thật đua nhau. Muốn thấy
/// nó nổ thì phải chạy trên một PostgreSQL sống (hai connection cùng <c>INSERT</c>), việc không làm
/// được ở môi trường test hiện tại. Nói cách khác: xanh ở Phần 5 loại được lỗi "quên khai chỉ mục",
/// không loại được lỗi "khai rồi mà PostgreSQL không dựng" — ca sau chỉ bắt được bằng một lượt chạy
/// tích hợp thật.
///
/// Khoá ký QR là khoá GIẢ rõ ràng — không phải khoá thật (luật 2), cùng lối
/// <see cref="TicketRequestReviewWiringTests"/>.
/// </summary>
public class TicketRequestReviewApiTests
{
    private const string QrSigningKey = "khoa-ky-gia-cua-test-dai-hon-32-ky-tu-abc";

    private const string CancellationUrl = "/api/cancellation-requests/";

    private const string ChangeUrl = "/api/ticket-changes/";

    /// <summary>Mốc thời gian cố định của bộ test — không dùng <c>DateTime.UtcNow</c> để lượt chạy lặp lại được.</summary>
    private static readonly DateTime Moc1 = new(2026, 10, 1, 1, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// Chuỗi kết nối GIẢ — chỉ để Npgsql dựng được model sinh DDL, KHÔNG kết nối tới đâu
    /// (<c>GenerateCreateScript()</c> là phép biến đổi model → SQL, chạy hoàn toàn ngoại tuyến).
    /// Không phải bí mật và không phải địa chỉ thật; cùng lối <see cref="TicketQrIssuanceChainTests"/>.
    /// </summary>
    private const string ChuoiKetNoiGia = "Host=localhost;Database=test-khong-ket-noi;Username=gia;Password=gia";

    // Câu trả về của tầng service, ghim nguyên văn: màn duyệt hiển thị thẳng chuỗi này cho quản lý,
    // nên đổi chữ là đổi thứ người dùng đọc. Ba câu dưới đây là BA ĐƯỜNG LUI khác nhau và phải phân
    // biệt được — "đã có người xử lý" (tải lại), "vé không còn hiệu lực" (không cứu được nữa), và
    // hai câu "mời khách chọn chuyến/ghế khác" (còn cứu được bằng một yêu cầu mới).
    private const string DaCoNguoiXuLy =
        "Yêu cầu đã được người khác xử lý. Hãy tải lại để xem kết quả mới nhất.";

    private const string VeKhongConHieuLuc =
        "Vé của yêu cầu này không còn hiệu lực để huỷ hoặc đổi (đã bị huỷ, hoặc đã được soát vé ở cổng).";

    private const string ChuyenMoiKhongChayDuoc =
        "Chuyến khách xin đổi sang đã bị huỷ hoặc đã khởi hành. Hãy từ chối yêu cầu và mời khách chọn chuyến khác.";

    private const string GheMoiDaCoNguoi =
        "Ghế khách xin đổi sang đã có người đặt trong lúc chờ duyệt. Hãy từ chối yêu cầu và mời khách chọn ghế khác.";

    // ===========================================================================================
    // PHẦN 1 — TÍNH PHÍ: phí CHỤP LẠI, tiền hoàn SUY RA
    // ===========================================================================================

    /// <summary>
    /// 🔴 Phép kiểm đắt nhất của file này. Phí huỷ là con số <b>chụp lúc khách gửi</b>, KHÔNG được
    /// tính lại lúc duyệt — và đây là chỗ duy nhất trong repo chứng minh được điều đó trên đường HTTP
    /// thật.
    ///
    /// Cách ghim: đặt <c>DepartureAt</c> cách <c>Moc1</c> 30 ngày, nhưng phí chụp là 50 000. Nếu ai đó
    /// sau này cho lượt duyệt tính lại phí theo quy định *"trước 24h / 12h / 2h"* của dòng 6, thì
    /// 30 ngày là mốc "trước 24h" ⇒ phí phải ra 0 và số hoàn phải bằng cả giá vé. Test đỏ ngay.
    ///
    /// Vì sao tính lại là lỗi tiền chứ không phải chuyện nhỏ: lượt duyệt có thể xảy ra vài giờ sau khi
    /// khách gửi, nên con số tính lại sẽ khác con số khách đã nhìn thấy và đã đồng ý — tức thu tiền
    /// sai so với thứ khách chấp nhận. Cùng lý do <see cref="Ticket.Price"/> chụp lại giá thay vì tra
    /// <c>Fares</c> lúc đọc.
    /// </summary>
    [Fact]
    public async Task Duyet_huy_tra_dung_phi_da_chup_chu_khong_tinh_lai_theo_gio_chay()
    {
        using var factory = NewFactory();

        var (khach, quanLy) = await SeedHaiNguoiAsync(factory);
        var ve = NewPaidTicket(khach.Id, 250_000m);

        var yeuCau = NewPendingCancellation(ve, khach.Id, phiHuy: 50_000m, departureAt: Moc1.AddDays(30));

        await SeedAsync(factory, db =>
        {
            db.Tickets.Add(ve);
            db.CancellationRequests.Add(yeuCau);
        });

        using var client = AuthClientFor(factory, quanLy);

        var response = await client.PatchAsJsonAsync(
            $"{CancellationUrl}{yeuCau.Id}", new { status = "Approved" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await ReadJsonAsync(response);

        // Nguyên con số đã chụp — không phải 0 của mốc "trước 24h".
        Assert.Equal(50_000m, body.GetProperty("cancellationFee").GetDecimal());

        // Và tiền hoàn là phép trừ của HAI con số bất biến: giá vé chụp lúc phát hành, phí chụp lúc gửi.
        Assert.Equal(200_000m, body.GetProperty("refundAmount").GetDecimal());

        // Dòng trong CSDL cũng không bị lượt duyệt sửa lại phí.
        var yeuCauSau = await QueryAsync(factory, db =>
            db.CancellationRequests.AsNoTracking().SingleAsync(c => c.Id == yeuCau.Id));

        Assert.Equal(50_000m, yeuCauSau.CancellationFee);
    }

    /// <summary>
    /// Tiền đi qua <c>numeric(12,2)</c> (A3) nên phép trừ phải giữ đúng hai chữ số thập phân — không
    /// được làm tròn, không được mất phần lẻ. Dùng số lẻ ở CẢ hai vế để một phép trừ sai kiểu
    /// (<c>double</c>, hay ép về số nguyên) lộ ra ngay: 199 999,99 − 19 999,99 = 180 000,00 chẵn,
    /// còn tính bằng số nguyên thì ra 180 000 nhưng mất vế lẻ ở các ca khác.
    /// </summary>
    [Fact]
    public async Task Duyet_huy_giu_nguyen_hai_chu_so_thap_phan_cua_tien()
    {
        using var factory = NewFactory();

        var (khach, quanLy) = await SeedHaiNguoiAsync(factory);
        var ve = NewPaidTicket(khach.Id, 199_999.99m);

        var yeuCau = NewPendingCancellation(ve, khach.Id, phiHuy: 19_999.99m, departureAt: Moc1.AddDays(2));

        await SeedAsync(factory, db =>
        {
            db.Tickets.Add(ve);
            db.CancellationRequests.Add(yeuCau);
        });

        using var client = AuthClientFor(factory, quanLy);

        var response = await client.PatchAsJsonAsync(
            $"{CancellationUrl}{yeuCau.Id}", new { status = "Approved" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await ReadJsonAsync(response);

        Assert.Equal(199_999.99m, body.GetProperty("price").GetDecimal());
        Assert.Equal(19_999.99m, body.GetProperty("cancellationFee").GetDecimal());
        Assert.Equal(180_000.00m, body.GetProperty("refundAmount").GetDecimal());
    }

    /// <summary>
    /// Huỷ miễn phí (<c>CancellationFee = 0</c>, quy định "trước 24h") phải hoàn lại TOÀN BỘ giá vé.
    /// Đây cũng là ca chứng minh vì sao cột phí KHÔNG dùng <c>null</c> để nói "chưa tính": <c>0</c> là
    /// một con số có nghĩa, còn <c>null</c> sẽ khiến phép trừ <c>price - fee</c> không thực hiện được
    /// — đúng ghi chú ở doc-comment <see cref="CancellationRequest.CancellationFee"/>.
    /// </summary>
    [Fact]
    public async Task Huy_mien_phi_thi_hoan_lai_toan_bo_gia_ve()
    {
        using var factory = NewFactory();

        var (khach, quanLy) = await SeedHaiNguoiAsync(factory);
        var ve = NewPaidTicket(khach.Id, 320_000m);

        var yeuCau = NewPendingCancellation(ve, khach.Id, phiHuy: 0m, departureAt: Moc1.AddDays(10));

        await SeedAsync(factory, db =>
        {
            db.Tickets.Add(ve);
            db.CancellationRequests.Add(yeuCau);
        });

        using var client = AuthClientFor(factory, quanLy);

        var response = await client.PatchAsJsonAsync(
            $"{CancellationUrl}{yeuCau.Id}", new { status = "Approved" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await ReadJsonAsync(response);

        Assert.Equal(0m, body.GetProperty("cancellationFee").GetDecimal());
        Assert.Equal(320_000m, body.GetProperty("refundAmount").GetDecimal());
    }

    /// <summary>
    /// Gửi <c>rejectReason</c> kèm <c>"Approved"</c> KHÔNG phải lỗi 400, nhưng cũng <b>không được
    /// lưu</b> — <c>docs/api-contract.md</c> chốt vậy, và lý do là dữ liệu: cột <c>RejectReason</c> chỉ
    /// có nghĩa ở nhánh từ chối, lưu rồi thì màn hình hiện "lý do từ chối" trên một yêu cầu ĐÃ DUYỆT.
    ///
    /// Cố ý không chặn 400 vì client đang cầm sẵn một object có cả hai trường (form giữ state cũ) —
    /// chặn ở đó là cái bẫy không đáng có. Nửa "không 400" đã có ở Phần 4 gián tiếp; ở đây ghim nửa
    /// còn lại, tức là <b>không lưu</b>.
    /// </summary>
    [Fact]
    public async Task Duyet_huy_khong_luu_rejectReason_client_gui_kem()
    {
        using var factory = NewFactory();

        var (khach, quanLy) = await SeedHaiNguoiAsync(factory);
        var ve = NewPaidTicket(khach.Id, 250_000m);

        var yeuCau = NewPendingCancellation(ve, khach.Id, phiHuy: 25_000m, departureAt: Moc1.AddDays(3));

        await SeedAsync(factory, db =>
        {
            db.Tickets.Add(ve);
            db.CancellationRequests.Add(yeuCau);
        });

        using var client = AuthClientFor(factory, quanLy);

        var response = await client.PatchAsJsonAsync(
            $"{CancellationUrl}{yeuCau.Id}",
            new { status = "Approved", rejectReason = "Lý do này không được lưu." });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await ReadJsonAsync(response);

        Assert.Equal(JsonValueKind.Null, body.GetProperty("rejectReason").ValueKind);

        var yeuCauSau = await QueryAsync(factory, db =>
            db.CancellationRequests.AsNoTracking().SingleAsync(c => c.Id == yeuCau.Id));

        Assert.Null(yeuCauSau.RejectReason);
    }

    /// <summary>
    /// Từ chối thì KHÔNG khoản nào di chuyển: <c>refundAmount</c> phải <c>null</c> chứ không phải
    /// <c>price - fee</c>. Trả con số ở đây là nói dối màn hình — khách sẽ tưởng nhà xe sắp chuyển
    /// tiền trong khi vé vẫn còn nguyên và chuyến vẫn phải đi.
    ///
    /// <c>cancellationFee</c> thì VẪN CÓ mặt kèm giá trị: nó là số khách đã nhìn thấy lúc gửi và là
    /// dữ liệu kiểm toán ("phí bao nhiêu" nằm ở dòng yêu cầu, không phụ thuộc ai duyệt).
    /// </summary>
    [Fact]
    public async Task Tu_choi_huy_van_tra_phi_nhung_refundAmount_null()
    {
        using var factory = NewFactory();

        var (khach, quanLy) = await SeedHaiNguoiAsync(factory);
        var ve = NewPaidTicket(khach.Id, 250_000m);

        var yeuCau = NewPendingCancellation(ve, khach.Id, phiHuy: 25_000m, departureAt: Moc1.AddDays(3));

        await SeedAsync(factory, db =>
        {
            db.Tickets.Add(ve);
            db.CancellationRequests.Add(yeuCau);
        });

        using var client = AuthClientFor(factory, quanLy);

        var response = await client.PatchAsJsonAsync(
            $"{CancellationUrl}{yeuCau.Id}", new { status = "Rejected" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await ReadJsonAsync(response);

        Assert.Equal("Rejected", body.GetProperty("status").GetString());
        Assert.Equal(25_000m, body.GetProperty("cancellationFee").GetDecimal());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("refundAmount").ValueKind);
    }

    /// <summary>
    /// Lý do từ chối chỉ có KHOẢNG TRẮNG phải được lưu thành <c>null</c>, không phải chuỗi khoảng
    /// trắng — service chuẩn hoá (<c>NormalizeRejectReason</c>).
    ///
    /// Hậu quả nếu bỏ phép chuẩn hoá: cột khác <c>null</c> nhưng rỗng nghĩa, nên màn hình vẽ một ô
    /// "Lý do từ chối" trắng trơn. Và vì đã "có giá trị", mọi phép kiểm kiểu <c>RejectReason != null</c>
    /// đều tưởng là có lý do — đúng loại dữ liệu nửa vời khó phát hiện khi truy vấn.
    /// </summary>
    [Fact]
    public async Task Tu_choi_huy_voi_ly_do_chi_khoang_trang_thi_luu_null()
    {
        using var factory = NewFactory();

        var (khach, quanLy) = await SeedHaiNguoiAsync(factory);
        var ve = NewPaidTicket(khach.Id, 250_000m);
        var yeuCau = NewPendingCancellation(ve, khach.Id, phiHuy: 25_000m, departureAt: Moc1.AddDays(3));

        await SeedAsync(factory, db =>
        {
            db.Tickets.Add(ve);
            db.CancellationRequests.Add(yeuCau);
        });

        using var client = AuthClientFor(factory, quanLy);

        var response = await client.PatchAsJsonAsync(
            $"{CancellationUrl}{yeuCau.Id}", new { status = "Rejected", rejectReason = "   \t  " });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await ReadJsonAsync(response);

        Assert.Equal(JsonValueKind.Null, body.GetProperty("rejectReason").ValueKind);

        var yeuCauSau = await QueryAsync(factory, db =>
            db.CancellationRequests.AsNoTracking().SingleAsync(c => c.Id == yeuCau.Id));

        Assert.Null(yeuCauSau.RejectReason);
    }

    /// <summary>
    /// Hợp đồng nói <c>TicketRequestResponse</c> là MỘT hình dạng dùng cho CẢ hai loại yêu cầu: trường
    /// của loại kia <b>có mặt</b> trong JSON với giá trị <c>null</c>, chứ không bị <c>JsonIgnore</c> cho
    /// biến mất.
    ///
    /// Vì sao phải ghim: màn hình duyệt gộp đọc chung một kiểu. Trường vắng mặt thì phía client phân
    /// biệt "không áp dụng" với "không có dữ liệu" bằng cách dò <c>in</c> / <c>undefined</c> — và mọi
    /// chỗ quên dò sẽ hiện <c>undefined</c> lên giao diện. Có mặt và <c>null</c> thì chỉ cần đọc thẳng.
    /// (Nửa còn lại — response của loại ĐỔI — đã ghim ở
    /// <see cref="Duyet_doi_tra_chenh_lech_gia_co_dau"/>.)
    /// </summary>
    [Fact]
    public async Task Duyet_huy_tra_du_nam_truong_cua_loai_doi_va_deu_null()
    {
        using var factory = NewFactory();

        var (khach, quanLy) = await SeedHaiNguoiAsync(factory);
        var ve = NewPaidTicket(khach.Id, 250_000m);
        var yeuCau = NewPendingCancellation(ve, khach.Id, phiHuy: 25_000m, departureAt: Moc1.AddDays(3));

        await SeedAsync(factory, db =>
        {
            db.Tickets.Add(ve);
            db.CancellationRequests.Add(yeuCau);
        });

        using var client = AuthClientFor(factory, quanLy);

        var response = await client.PatchAsJsonAsync(
            $"{CancellationUrl}{yeuCau.Id}", new { status = "Approved" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await ReadJsonAsync(response);

        // GetProperty sẽ NÉM nếu trường vắng mặt — đó chính là phép kiểm "có mặt".
        Assert.Equal("Cancel", body.GetProperty("type").GetString());

        foreach (var truong in new[]
                 {
                     "newTripId", "newSeatId", "newPrice", "priceDifference", "newTicketId",
                 })
        {
            Assert.Equal(JsonValueKind.Null, body.GetProperty(truong).ValueKind);
        }
    }

    // ===========================================================================================
    // PHẦN 2 — MA TRẬN 409 CỦA PATCH /cancellation-requests
    // ===========================================================================================

    /// <summary>
    /// Yêu cầu không còn <c>Pending</c> — quản lý khác vừa bấm xong, hoặc chính người này bấm hai lần
    /// (double-click, F5 rồi gửi lại). Cả hai ca dùng CHUNG một câu vì màn hình chỉ cần biết *tải lại
    /// đi*, không cần biết ai.
    ///
    /// Phép kiểm này chạy trên InMemory nên nó phủ <b>lớp 1</b> (kiểm trước) của hai lớp chống xử lý
    /// trùng. Lớp 2 (khoá lạc hậu <c>xmin</c>) chỉ chạy thật trên PostgreSQL — xem Phần 5,
    /// <see cref="Ddl_hai_bang_yeu_cau_deu_co_khoa_phien_ban_dong_xmin"/>.
    /// </summary>
    [Theory]
    [InlineData(CancellationRequestStatus.Approved)]
    [InlineData(CancellationRequestStatus.Rejected)]
    public async Task Duyet_lai_yeu_cau_huy_da_xu_ly_thi_409(CancellationRequestStatus trangThaiCu)
    {
        using var factory = NewFactory();

        var (khach, quanLy, nguoiDuyetTruoc) = await SeedBaNguoiAsync(factory);
        var ve = NewPaidTicket(khach.Id, 250_000m);

        var yeuCau = NewPendingCancellation(ve, khach.Id, phiHuy: 25_000m, departureAt: Moc1.AddDays(3));
        yeuCau.Status = trangThaiCu;
        yeuCau.ReviewedByUserId = nguoiDuyetTruoc.Id;
        yeuCau.ReviewedAt = Moc1.AddHours(1);

        // Vé đã chết theo ở ca Approved — đúng thế giới thật sau một lượt duyệt thành công.
        if (trangThaiCu == CancellationRequestStatus.Approved)
        {
            ve.Status = TicketStatus.Cancelled;
        }

        await SeedAsync(factory, db =>
        {
            db.Tickets.Add(ve);
            db.CancellationRequests.Add(yeuCau);
        });

        using var client = AuthClientFor(factory, quanLy);

        var response = await client.PatchAsJsonAsync(
            $"{CancellationUrl}{yeuCau.Id}", new { status = "Rejected", rejectReason = "Đổi ý." });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(DaCoNguoiXuLy, MessageOf(await ReadJsonAsync(response)));

        // Lượt thua không được ghi đè người xử lý trước, cũng không được lật trạng thái.
        var yeuCauSau = await QueryAsync(factory, db =>
            db.CancellationRequests.AsNoTracking().SingleAsync(c => c.Id == yeuCau.Id));

        Assert.Equal(trangThaiCu, yeuCauSau.Status);
        Assert.Equal(nguoiDuyetTruoc.Id, yeuCauSau.ReviewedByUserId);
        Assert.Null(yeuCauSau.RejectReason);
    }

    /// <summary>
    /// Vé không còn <c>Paid</c> ngay lúc duyệt. Hai đường dẫn tới cùng chỗ hỏng, và cả hai đều là lỗi
    /// tiền thật:
    ///   • <c>Cancelled</c> — vé đã được huỷ qua một yêu cầu khác (hoặc yêu cầu ĐỔI đã được duyệt
    ///     trước đó). Duyệt tiếp là huỷ lần thứ hai và hoàn tiền lần thứ hai cho một chiếc vé.
    ///   • <c>Used</c> — vé đã soát ở cổng trong lúc yêu cầu nằm chờ. Hoàn tiền cho chuyến khách ĐÃ ĐI.
    ///
    /// Đây là phép kiểm mà CSDL KHÔNG làm hộ được: chỉ mục duy nhất điều kiện nằm trong từng bảng,
    /// không với được sang bảng kia, nên "một vé chỉ một yêu cầu đang chờ" là việc của service
    /// (chú thích ngay tại <c>AppDbContext.CancellationRequest.cs</c>). Bỏ phép kiểm này là ghi ra dữ
    /// liệu sai chứ không phải ném lỗi.
    /// </summary>
    [Theory]
    [InlineData(TicketStatus.Cancelled)]
    [InlineData(TicketStatus.Used)]
    public async Task Duyet_huy_khi_ve_khong_con_Paid_thi_409(TicketStatus trangThaiVe)
    {
        using var factory = NewFactory();

        var (khach, quanLy) = await SeedHaiNguoiAsync(factory);
        var ve = NewPaidTicket(khach.Id, 250_000m);
        ve.Status = trangThaiVe;
        ve.UsedAt = trangThaiVe == TicketStatus.Used ? Moc1.AddMinutes(30) : null;

        var yeuCau = NewPendingCancellation(ve, khach.Id, phiHuy: 25_000m, departureAt: Moc1.AddDays(3));

        await SeedAsync(factory, db =>
        {
            db.Tickets.Add(ve);
            db.CancellationRequests.Add(yeuCau);
        });

        using var client = AuthClientFor(factory, quanLy);

        var response = await client.PatchAsJsonAsync(
            $"{CancellationUrl}{yeuCau.Id}", new { status = "Approved" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(VeKhongConHieuLuc, MessageOf(await ReadJsonAsync(response)));
    }

    /// <summary>
    /// 🔴 Nửa quan trọng nhất của mọi ca 409: lượt thất bại phải <b>không ghi gì</b>. Kiểm cái này
    /// riêng (thay vì tin rằng <c>return Conflict</c> là đủ) vì phép kiểm "vé còn <c>Paid</c>" nằm
    /// TRONG nhánh duyệt, sau khi <c>cancellation.Status = Approved</c> đã được gán ở một bản cài đặt
    /// khác — một lần đảo thứ tự là lượt 409 để lại yêu cầu <c>Approved</c> mà vé vẫn sống, tức là
    /// khách đi được bằng chiếc vé đã được duyệt hoàn tiền.
    ///
    /// Ghim cả <c>ReviewedByUserId</c>/<c>ReviewedAt</c>/<c>UpdatedAt</c>: một lượt bị từ chối vì vé
    /// chết thì cũng không phải một lượt có người xử lý.
    /// </summary>
    [Fact]
    public async Task Duyet_huy_that_bai_thi_khong_ghi_gi_vao_yeu_cau_lan_ve()
    {
        using var factory = NewFactory();

        var (khach, quanLy) = await SeedHaiNguoiAsync(factory);
        var ve = NewPaidTicket(khach.Id, 250_000m);
        ve.Status = TicketStatus.Cancelled;

        var yeuCau = NewPendingCancellation(ve, khach.Id, phiHuy: 25_000m, departureAt: Moc1.AddDays(3));

        await SeedAsync(factory, db =>
        {
            db.Tickets.Add(ve);
            db.CancellationRequests.Add(yeuCau);
        });

        using var client = AuthClientFor(factory, quanLy);

        var response = await client.PatchAsJsonAsync(
            $"{CancellationUrl}{yeuCau.Id}", new { status = "Approved" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var yeuCauSau = await QueryAsync(factory, db =>
            db.CancellationRequests.AsNoTracking().SingleAsync(c => c.Id == yeuCau.Id));

        Assert.Equal(CancellationRequestStatus.Pending, yeuCauSau.Status);
        Assert.Null(yeuCauSau.ReviewedByUserId);
        Assert.Null(yeuCauSau.ReviewedAt);
        Assert.Null(yeuCauSau.UpdatedAt);
    }

    /// <summary>
    /// Từ chối thì <b>luôn thành công được</b>, kể cả khi vé đã chết — nhánh từ chối không đụng dòng vé
    /// và không có phép kiểm nào để trượt.
    ///
    /// Vì sao phải ghim: một bản cài đặt "đối xứng" (kiểm vé còn <c>Paid</c> ở cả hai nhánh) sẽ khiến
    /// quản lý <b>không thể đóng</b> yêu cầu của một chiếc vé đã soát ở cổng — yêu cầu nằm chờ mãi
    /// trong hàng đợi, và màn duyệt đầy những dòng không ai xử lý được. Từ chối không cấp ghế cho ai
    /// nên không có gì phải kiểm.
    /// </summary>
    [Fact]
    public async Task Tu_choi_yeu_cau_huy_khi_ve_da_soat_van_thanh_cong()
    {
        using var factory = NewFactory();

        var (khach, quanLy) = await SeedHaiNguoiAsync(factory);
        var ve = NewPaidTicket(khach.Id, 250_000m);
        ve.Status = TicketStatus.Used;
        ve.UsedAt = Moc1.AddMinutes(30);

        var yeuCau = NewPendingCancellation(ve, khach.Id, phiHuy: 25_000m, departureAt: Moc1.AddDays(3));

        await SeedAsync(factory, db =>
        {
            db.Tickets.Add(ve);
            db.CancellationRequests.Add(yeuCau);
        });

        using var client = AuthClientFor(factory, quanLy);

        var response = await client.PatchAsJsonAsync(
            $"{CancellationUrl}{yeuCau.Id}",
            new { status = "Rejected", rejectReason = "Vé đã soát ở cổng." });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await ReadJsonAsync(response);

        Assert.Equal("Rejected", body.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("refundAmount").ValueKind);

        // Vé đã soát giữ nguyên `Used` — từ chối không "trả vé về" cho ai cả.
        var veSau = await QueryAsync(factory, db =>
            db.Tickets.AsNoTracking().SingleAsync(t => t.Id == ve.Id));

        Assert.Equal(TicketStatus.Used, veSau.Status);
    }

    // ===========================================================================================
    // PHẦN 3 — MA TRẬN 409 CỦA PATCH /ticket-changes
    // ===========================================================================================

    [Fact]
    public async Task Duyet_doi_lan_hai_thi_409()
    {
        using var factory = NewFactory();

        var (khach, quanLy, nguoiDuyetTruoc) = await SeedBaNguoiAsync(factory);
        var veCu = NewPaidTicket(khach.Id, 250_000m);
        var chuyenMoi = NewTrip(TripStatus.Scheduled);

        var yeuCau = NewPendingChange(veCu, khach.Id, chuyenMoi, newPrice: 300_000m);
        yeuCau.Status = TicketChangeStatus.Approved;
        yeuCau.ReviewedByUserId = nguoiDuyetTruoc.Id;
        yeuCau.ReviewedAt = Moc1.AddHours(1);

        await SeedAsync(factory, db =>
        {
            db.Tickets.Add(veCu);
            db.Trips.Add(chuyenMoi);
            db.TicketChanges.Add(yeuCau);
        });

        using var client = AuthClientFor(factory, quanLy);

        var response = await client.PatchAsJsonAsync(
            $"{ChangeUrl}{yeuCau.Id}", new { status = "Approved" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(DaCoNguoiXuLy, MessageOf(await ReadJsonAsync(response)));

        // Không vé mới nào được phát hành thêm — lượt thua tuyệt đối không được chạm bảng Tickets.
        var soVe = await QueryAsync(factory, db => db.Tickets.AsNoTracking().CountAsync());

        Assert.Equal(1, soVe);
    }

    /// <summary>
    /// Vé gốc không còn <c>Paid</c> — cùng hai đường dẫn như bên huỷ, nhưng hậu quả nặng hơn một bậc
    /// và đó là lý do ca này đứng riêng ở đây: nhánh duyệt ĐỔI <b>phát hành một vé mới</b>, nên duyệt
    /// trên một vé đã chết là phát hành vé thật cho một chuyến khách không còn quyền đi.
    ///
    /// Ca <c>Cancelled</c> ở đây cũng chính là <i>luật liên bảng</i> mà không chỉ mục nào chặn được:
    /// một vé có ĐỒNG THỜI một yêu cầu huỷ <c>Pending</c> và một yêu cầu đổi <c>Pending</c> — hai chỉ
    /// mục duy nhất điều kiện nằm trong hai bảng riêng nên CSDL cho qua; nếu yêu cầu huỷ được duyệt
    /// trước thì vé thành <c>Cancelled</c> và lượt duyệt đổi phải bị chặn ở đây.
    /// </summary>
    [Theory]
    [InlineData(TicketStatus.Cancelled)]
    [InlineData(TicketStatus.Used)]
    public async Task Duyet_doi_khi_ve_goc_khong_con_Paid_thi_409(TicketStatus trangThaiVe)
    {
        using var factory = NewFactory();

        var (khach, quanLy) = await SeedHaiNguoiAsync(factory);
        var veCu = NewPaidTicket(khach.Id, 250_000m);
        veCu.Status = trangThaiVe;
        veCu.UsedAt = trangThaiVe == TicketStatus.Used ? Moc1.AddMinutes(30) : null;

        var chuyenMoi = NewTrip(TripStatus.Scheduled);
        var yeuCau = NewPendingChange(veCu, khach.Id, chuyenMoi, newPrice: 300_000m);

        await SeedAsync(factory, db =>
        {
            db.Tickets.Add(veCu);
            db.Trips.Add(chuyenMoi);
            db.TicketChanges.Add(yeuCau);
        });

        using var client = AuthClientFor(factory, quanLy);

        var response = await client.PatchAsJsonAsync(
            $"{ChangeUrl}{yeuCau.Id}", new { status = "Approved" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(VeKhongConHieuLuc, MessageOf(await ReadJsonAsync(response)));
    }

    /// <summary>
    /// 🔴 Việc 1 của lượt duyệt đổi: <b>ghế mới KHÔNG được giữ lúc khách gửi</b> (không có dòng
    /// <c>SeatHolds</c> nào cho nó — xem <see cref="TicketChangeStatus.Pending"/>), nên giữa lúc gửi
    /// và lúc duyệt người khác hoàn toàn có thể mua mất. Tin dữ liệu đã kiểm lúc gửi là <b>bán trùng
    /// ghế</b>.
    ///
    /// Hai nguồn chiếm ghế phải hỏi CẢ HAI, vì chúng là hai bảng khác nhau: một vé đã bán
    /// (<c>Tickets.Status = Paid</c>) và một phiên giữ chỗ còn hiệu lực (<c>SeatHolds.Status = Holding</c>).
    /// Hỏi thiếu một bảng là phát hành vé thứ hai cho cùng một ghế.
    /// </summary>
    [Theory]
    [InlineData("ve")]
    [InlineData("giu-cho")]
    public async Task Duyet_doi_khi_ghe_moi_da_co_nguoi_thi_409(string nguonChiemGhe)
    {
        using var factory = NewFactory();

        var (khach, quanLy, nguoiKhac) = await SeedBaNguoiAsync(factory);
        var veCu = NewPaidTicket(khach.Id, 250_000m);

        var chuyenMoi = NewTrip(TripStatus.Scheduled);
        var gheMoi = Guid.NewGuid();

        var yeuCau = NewPendingChange(veCu, khach.Id, chuyenMoi, newPrice: 300_000m, newSeatId: gheMoi);

        await SeedAsync(factory, db =>
        {
            db.Tickets.Add(veCu);
            db.Trips.Add(chuyenMoi);
            db.TicketChanges.Add(yeuCau);

            if (nguonChiemGhe == "ve")
            {
                // Người khác đã mua ghế đó của chuyến mới.
                db.Tickets.Add(new Ticket
                {
                    Code = "SBT1:ve-nguoi-khac-" + Guid.NewGuid().ToString("N"),
                    TripId = chuyenMoi.Id,
                    SeatId = gheMoi,
                    UserId = nguoiKhac.Id,
                    Price = 300_000m,
                    Status = TicketStatus.Paid,
                    CreatedAt = Moc1,
                });
            }
            else
            {
                // Hoặc ghế đang bị một phiên giữ chỗ còn hiệu lực chiếm — vé chưa sinh nhưng ghế đã có chủ.
                db.SeatHolds.Add(new SeatHold
                {
                    TripId = chuyenMoi.Id,
                    SeatId = gheMoi,
                    UserId = nguoiKhac.Id,
                    SessionCode = "SH-" + Guid.NewGuid().ToString("N"),
                    Status = SeatHoldStatus.Holding,
                    ExpiresAt = Moc1.AddMinutes(10),
                    CreatedAt = Moc1,
                });
            }
        });

        using var client = AuthClientFor(factory, quanLy);

        var response = await client.PatchAsJsonAsync(
            $"{ChangeUrl}{yeuCau.Id}", new { status = "Approved" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(GheMoiDaCoNguoi, MessageOf(await ReadJsonAsync(response)));
    }

    /// <summary>
    /// Bản lề của phép kiểm ghế: bộ lọc phải chép ĐÚNG điều kiện của hai ràng buộc thật, không thêm
    /// không bớt. Một phiên giữ chỗ đã <c>Expired</c> KHÔNG còn chiếm ghế — chỉ <c>Holding</c> mới là
    /// điều kiện của chỉ mục duy nhất điều kiện trên <c>SeatHolds</c>.
    ///
    /// Nếu test này đỏ thì phép kiểm đã bị nới thành "có dòng SeatHolds nào không" — và mọi ghế từng
    /// được ai đó bỏ dở đều thành ghế chết vĩnh viễn: quản lý không duyệt được cho ai nữa, dù ghế
    /// thực sự trống.
    /// </summary>
    [Fact]
    public async Task Duyet_doi_khi_ghe_moi_chi_co_giu_cho_da_het_han_thi_van_thanh_cong()
    {
        using var factory = NewFactory();

        var (khach, quanLy, nguoiKhac) = await SeedBaNguoiAsync(factory);
        var veCu = NewPaidTicket(khach.Id, 250_000m);

        var chuyenMoi = NewTrip(TripStatus.Scheduled);
        var gheMoi = Guid.NewGuid();
        var yeuCau = NewPendingChange(veCu, khach.Id, chuyenMoi, newPrice: 300_000m, newSeatId: gheMoi);

        await SeedAsync(factory, db =>
        {
            db.Tickets.Add(veCu);
            db.Trips.Add(chuyenMoi);
            db.TicketChanges.Add(yeuCau);

            db.SeatHolds.Add(new SeatHold
            {
                TripId = chuyenMoi.Id,
                SeatId = gheMoi,
                UserId = nguoiKhac.Id,
                SessionCode = "SH-" + Guid.NewGuid().ToString("N"),
                Status = SeatHoldStatus.Expired,
                ExpiresAt = Moc1.AddMinutes(-30),
                CreatedAt = Moc1.AddMinutes(-40),
            });
        });

        using var client = AuthClientFor(factory, quanLy);

        var response = await client.PatchAsJsonAsync(
            $"{ChangeUrl}{yeuCau.Id}", new { status = "Approved" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// Chuyến khách xin đổi sang phải còn chạy được. Ghế của một chuyến đã huỷ thì trống THẬT, nhưng
    /// phát hành vé cho nó là bán một chuyến không bao giờ đi — vé mới là vé thật, in QR được.
    ///
    /// Đây cũng là ca chứng minh vì sao phép kiểm phải chạy LÚC DUYỆT chứ không tin phép kiểm lúc gửi:
    /// chuyến có thể bị huỷ trong khoảng thời gian yêu cầu nằm chờ.
    /// </summary>
    [Theory]
    [InlineData(TripStatus.Cancelled)]
    [InlineData(TripStatus.Completed)]
    public async Task Duyet_doi_khi_chuyen_moi_da_huy_hoac_da_chay_xong_thi_409(TripStatus trangThaiChuyen)
    {
        using var factory = NewFactory();

        var (khach, quanLy) = await SeedHaiNguoiAsync(factory);
        var veCu = NewPaidTicket(khach.Id, 250_000m);

        var chuyenMoi = NewTrip(trangThaiChuyen);
        var yeuCau = NewPendingChange(veCu, khach.Id, chuyenMoi, newPrice: 300_000m);

        await SeedAsync(factory, db =>
        {
            db.Tickets.Add(veCu);
            db.Trips.Add(chuyenMoi);
            db.TicketChanges.Add(yeuCau);
        });

        using var client = AuthClientFor(factory, quanLy);

        var response = await client.PatchAsJsonAsync(
            $"{ChangeUrl}{yeuCau.Id}", new { status = "Approved" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(ChuyenMoiKhongChayDuoc, MessageOf(await ReadJsonAsync(response)));
    }

    /// <summary>
    /// Bản lề còn lại của phép kiểm chuyến: chỉ <c>Cancelled</c> và <c>Completed</c> mới bị chặn.
    /// <c>Running</c> (xe đang trên đường) và <c>Scheduled</c> đều phải duyệt được — khách đổi sang
    /// chuyến đang chạy là chuyện có thật, và chặn nhầm là mở đường cho một bản cài đặt "chỉ cho
    /// Scheduled" làm hỏng cả tính năng.
    ///
    /// Ghim kèm hai kết quả của lượt duyệt: vé cũ chết, vé mới ra đời.
    /// </summary>
    [Theory]
    [InlineData(TripStatus.Running)]
    [InlineData(TripStatus.Scheduled)]
    public async Task Duyet_doi_khi_chuyen_moi_con_chay_duoc_thi_thanh_cong(TripStatus trangThaiChuyen)
    {
        using var factory = NewFactory();

        var (khach, quanLy) = await SeedHaiNguoiAsync(factory);
        var veCu = NewPaidTicket(khach.Id, 250_000m);

        var chuyenMoi = NewTrip(trangThaiChuyen);
        var gheMoi = Guid.NewGuid();
        var yeuCau = NewPendingChange(veCu, khach.Id, chuyenMoi, newPrice: 300_000m, newSeatId: gheMoi);

        await SeedAsync(factory, db =>
        {
            db.Tickets.Add(veCu);
            db.Trips.Add(chuyenMoi);
            db.TicketChanges.Add(yeuCau);
        });

        using var client = AuthClientFor(factory, quanLy);

        var response = await client.PatchAsJsonAsync(
            $"{ChangeUrl}{yeuCau.Id}", new { status = "Approved" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await ReadJsonAsync(response);
        var veMoiId = body.GetProperty("newTicketId").GetGuid();

        var veCuSau = await QueryAsync(factory, db =>
            db.Tickets.AsNoTracking().SingleAsync(t => t.Id == veCu.Id));

        Assert.Equal(TicketStatus.Cancelled, veCuSau.Status);

        var veMoi = await QueryAsync(factory, db =>
            db.Tickets.AsNoTracking().SingleAsync(t => t.Id == veMoiId));

        Assert.Equal(TicketStatus.Paid, veMoi.Status);
        Assert.Equal(chuyenMoi.Id, veMoi.TripId);
        Assert.Equal(gheMoi, veMoi.SeatId);
    }

    /// <summary>
    /// 🔴 Giá vé mới là con số <b>CHỤP LẠI</b> trong dòng yêu cầu (<c>NewPrice</c>), KHÔNG tra lại bảng
    /// <c>Fares</c> lúc duyệt — <c>docs/api-contract.md</c> chốt: *"đúng con số khách đã nhìn thấy và
    /// đã đồng ý — không tra lại <c>Fares</c>"*.
    ///
    /// Cách ghim: cố ý seed một dòng <c>Fares</c> của tuyến chuyến mới với giá KHÁC HẲN (999 000) rồi
    /// khẳng định vé mới vẫn mang <c>NewPrice</c> (300 000). Nếu ai đó "cải tiến" lượt duyệt thành tra
    /// bảng giá, con số trên vé khách cầm đổi sau khi khách đã đồng ý trả — và hoá đơn đã xuất không
    /// còn khớp.
    /// </summary>
    [Fact]
    public async Task Duyet_doi_dung_gia_da_chup_chu_khong_tra_lai_bang_gia()
    {
        using var factory = NewFactory();

        var (khach, quanLy) = await SeedHaiNguoiAsync(factory);
        var veCu = NewPaidTicket(khach.Id, 250_000m);

        var chuyenMoi = NewTrip(TripStatus.Scheduled);
        var yeuCau = NewPendingChange(veCu, khach.Id, chuyenMoi, newPrice: 300_000m);

        await SeedAsync(factory, db =>
        {
            db.Tickets.Add(veCu);
            db.Trips.Add(chuyenMoi);
            db.TicketChanges.Add(yeuCau);

            // Bảng giá của tuyến đó — giá KHÁC hẳn con số khách đã chốt.
            db.Fares.Add(new Fare
            {
                RouteId = chuyenMoi.RouteId,
                PassengerType = PassengerType.Standard,
                Price = 999_000m,
                CreatedAt = Moc1,
            });
        });

        using var client = AuthClientFor(factory, quanLy);

        var response = await client.PatchAsJsonAsync(
            $"{ChangeUrl}{yeuCau.Id}", new { status = "Approved" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var veMoiId = (await ReadJsonAsync(response)).GetProperty("newTicketId").GetGuid();

        var veMoi = await QueryAsync(factory, db =>
            db.Tickets.AsNoTracking().SingleAsync(t => t.Id == veMoiId));

        Assert.Equal(300_000m, veMoi.Price);
        Assert.NotEqual(999_000m, veMoi.Price);
    }

    /// <summary>
    /// Chênh lệch giá CÓ DẤU: dương = khách trả thêm, âm = nhà xe hoàn lại, 0 = ngang giá. Cả hai vế
    /// đều bất biến (chụp lúc khách gửi) nên phép trừ không bao giờ lệch — và cũng vì thế mà không có
    /// cột riêng cho nó.
    ///
    /// Ghim cả hai dấu trong một test: một bản cài đặt dùng <c>Math.Abs</c> hay đảo hai vế sẽ ra con
    /// số dương ở ca thứ hai, và màn duyệt sẽ nói "khách trả thêm 70 000" trong khi thật ra nhà xe
    /// phải trả lại 70 000 — sai chiều tiền.
    /// </summary>
    [Theory]
    [InlineData(300_000, 50_000)]
    [InlineData(180_000, -70_000)]
    public async Task Duyet_doi_tra_chenh_lech_gia_co_dau(int giaMoi, int chenhLechMongDoi)
    {
        using var factory = NewFactory();

        var (khach, quanLy) = await SeedHaiNguoiAsync(factory);
        var veCu = NewPaidTicket(khach.Id, 250_000m);

        var chuyenMoi = NewTrip(TripStatus.Scheduled);
        var yeuCau = NewPendingChange(veCu, khach.Id, chuyenMoi, newPrice: giaMoi);

        await SeedAsync(factory, db =>
        {
            db.Tickets.Add(veCu);
            db.Trips.Add(chuyenMoi);
            db.TicketChanges.Add(yeuCau);
        });

        using var client = AuthClientFor(factory, quanLy);

        var response = await client.PatchAsJsonAsync(
            $"{ChangeUrl}{yeuCau.Id}", new { status = "Approved" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await ReadJsonAsync(response);

        Assert.Equal(giaMoi, body.GetProperty("newPrice").GetDecimal());
        Assert.Equal(chenhLechMongDoi, body.GetProperty("priceDifference").GetDecimal());

        // Trường chỉ của loại HUỶ phải có mặt và null trên mọi response của loại ĐỔI.
        Assert.Equal(JsonValueKind.Null, body.GetProperty("cancellationFee").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("refundAmount").ValueKind);
    }

    /// <summary>
    /// 🔴 Nửa quan trọng nhất của lượt duyệt đổi bị chặn vì ghế: <b>không được ghi gì cả</b>. Ba phép
    /// kiểm của nhánh duyệt (vé còn <c>Paid</c>, chuyến còn chạy, ghế còn trống) phải đứng TRƯỚC mọi
    /// phép ghi. Chỉ cần một lần đảo thứ tự là lượt 409 để lại: vé cũ đã <c>Cancelled</c> mà khách
    /// vẫn chưa có vé mới, hoặc tệ hơn — một vé mới đã phát hành cho ghế của người khác.
    ///
    /// Đây là loại lỗi mà bộ test InMemory <b>bắt được</b> dù nó không dựng được chỉ mục: phép kiểm
    /// mô tả ở tầng service thấy ghế đã có người trước khi kịp sửa gì.
    /// </summary>
    [Fact]
    public async Task Duyet_doi_that_bai_vi_ghe_thi_khong_huy_ve_cu_va_khong_phat_hanh_ve_moi()
    {
        using var factory = NewFactory();

        var (khach, quanLy, nguoiKhac) = await SeedBaNguoiAsync(factory);
        var veCu = NewPaidTicket(khach.Id, 250_000m);

        var chuyenMoi = NewTrip(TripStatus.Scheduled);
        var gheMoi = Guid.NewGuid();
        var yeuCau = NewPendingChange(veCu, khach.Id, chuyenMoi, newPrice: 300_000m, newSeatId: gheMoi);

        await SeedAsync(factory, db =>
        {
            db.Tickets.Add(veCu);
            db.Trips.Add(chuyenMoi);
            db.TicketChanges.Add(yeuCau);

            db.Tickets.Add(new Ticket
            {
                Code = "SBT1:ve-nguoi-khac-" + Guid.NewGuid().ToString("N"),
                TripId = chuyenMoi.Id,
                SeatId = gheMoi,
                UserId = nguoiKhac.Id,
                Price = 300_000m,
                Status = TicketStatus.Paid,
                CreatedAt = Moc1,
            });
        });

        using var client = AuthClientFor(factory, quanLy);

        var response = await client.PatchAsJsonAsync(
            $"{ChangeUrl}{yeuCau.Id}", new { status = "Approved" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        // Vé cũ VẪN sống: khách còn nguyên chuyến cũ để đi.
        var veCuSau = await QueryAsync(factory, db =>
            db.Tickets.AsNoTracking().SingleAsync(t => t.Id == veCu.Id));

        Assert.Equal(TicketStatus.Paid, veCuSau.Status);
        Assert.Null(veCuSau.UpdatedAt);

        // Và không có vé mới nào được phát hành — vẫn đúng hai vé trong bảng (vé cũ + vé người khác).
        var soVe = await QueryAsync(factory, db => db.Tickets.AsNoTracking().CountAsync());

        Assert.Equal(2, soVe);

        var yeuCauSau = await QueryAsync(factory, db =>
            db.TicketChanges.AsNoTracking().SingleAsync(c => c.Id == yeuCau.Id));

        Assert.Equal(TicketChangeStatus.Pending, yeuCauSau.Status);
        Assert.Null(yeuCauSau.NewTicketId);
        Assert.Null(yeuCauSau.ReviewedByUserId);
    }

    /// <summary>
    /// Từ chối một yêu cầu đổi **không kiểm ghế** — và đó là chủ ý, không phải sót. Từ chối không cấp
    /// ghế cho ai nên không có gì phải kiểm; kiểm ở đây chỉ đẻ ra một lượt từ chối THẤT BẠI vì ghế đã
    /// có người, trong khi từ chối thì luôn phải thành công được để quản lý đóng được hàng đợi.
    ///
    /// Cùng lối với <see cref="Tu_choi_yeu_cau_huy_khi_ve_da_soat_van_thanh_cong"/>: nhánh từ chối là
    /// nhánh "đóng lại", nó không được có điều kiện nào để trượt.
    /// </summary>
    [Fact]
    public async Task Tu_choi_doi_khi_ghe_moi_da_co_nguoi_van_thanh_cong()
    {
        using var factory = NewFactory();

        var (khach, quanLy, nguoiKhac) = await SeedBaNguoiAsync(factory);
        var veCu = NewPaidTicket(khach.Id, 250_000m);

        var chuyenMoi = NewTrip(TripStatus.Scheduled);
        var gheMoi = Guid.NewGuid();
        var yeuCau = NewPendingChange(veCu, khach.Id, chuyenMoi, newPrice: 300_000m, newSeatId: gheMoi);

        await SeedAsync(factory, db =>
        {
            db.Tickets.Add(veCu);
            db.Trips.Add(chuyenMoi);
            db.TicketChanges.Add(yeuCau);

            db.Tickets.Add(new Ticket
            {
                Code = "SBT1:ve-nguoi-khac-" + Guid.NewGuid().ToString("N"),
                TripId = chuyenMoi.Id,
                SeatId = gheMoi,
                UserId = nguoiKhac.Id,
                Price = 300_000m,
                Status = TicketStatus.Paid,
                CreatedAt = Moc1,
            });
        });

        using var client = AuthClientFor(factory, quanLy);

        var response = await client.PatchAsJsonAsync(
            $"{ChangeUrl}{yeuCau.Id}", new { status = "Rejected", rejectReason = "Ghế đã có người đặt." });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await ReadJsonAsync(response);

        Assert.Equal("Rejected", body.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("newTicketId").ValueKind);

        var veCuSau = await QueryAsync(factory, db =>
            db.Tickets.AsNoTracking().SingleAsync(t => t.Id == veCu.Id));

        Assert.Equal(TicketStatus.Paid, veCuSau.Status);
    }

    /// <summary>
    /// Từ chối một yêu cầu đổi mà chuyến mới đã huỷ cũng phải thành công — phép kiểm chuyến nằm trong
    /// nhánh DUYỆT, không phải nhánh từ chối. Thứ tự này có thật trong service và nó đúng: chuyến đã
    /// huỷ là lý do CHÍNH ĐÁNG để từ chối, chặn lượt từ chối ở đó là bịt luôn đường thoát duy nhất.
    /// </summary>
    [Fact]
    public async Task Tu_choi_doi_khi_chuyen_moi_da_huy_van_thanh_cong()
    {
        using var factory = NewFactory();

        var (khach, quanLy) = await SeedHaiNguoiAsync(factory);
        var veCu = NewPaidTicket(khach.Id, 250_000m);

        var chuyenMoi = NewTrip(TripStatus.Cancelled);
        var yeuCau = NewPendingChange(veCu, khach.Id, chuyenMoi, newPrice: 300_000m);

        await SeedAsync(factory, db =>
        {
            db.Tickets.Add(veCu);
            db.Trips.Add(chuyenMoi);
            db.TicketChanges.Add(yeuCau);
        });

        using var client = AuthClientFor(factory, quanLy);

        var response = await client.PatchAsJsonAsync(
            $"{ChangeUrl}{yeuCau.Id}",
            new { status = "Rejected", rejectReason = "Chuyến khách xin đổi sang đã huỷ." });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await ReadJsonAsync(response);

        Assert.Equal("Rejected", body.GetProperty("status").GetString());
        // Chênh lệch giá vẫn trả về ở nhánh từ chối — nó là dữ liệu của dòng yêu cầu, không phải
        // kết quả của lượt duyệt.
        Assert.Equal(50_000m, body.GetProperty("priceDifference").GetDecimal());
    }

    // ===========================================================================================
    // PHẦN 4 — NHÁNH 400: giá trị `status`, độ dài `rejectReason`
    // ===========================================================================================

    /// <summary>
    /// 🔴 <c>"Pending"</c> là trạng thái KHÁCH gửi, KHÔNG phải một quyết định — nhận nó là mở đường
    /// cho một lượt gọi "duyệt" ghi ngược yêu cầu về hàng chờ, và hàng đợi của quản lý tự sinh việc.
    /// Cùng nhóm với nó: chuỗi SỐ (<c>"1"</c> sẽ thành <c>Rejected</c> nếu ai đó dùng
    /// <c>Enum.TryParse</c>) và chuỗi gần đúng (<c>"Approvedd"</c>).
    ///
    /// Vì sao ghim bằng <c>InlineData</c> chứ không một <c>[Fact]</c> mỗi ca: ba giá trị này cùng một
    /// đường đi (service so với danh sách TÊN của enum), nên tách ra chỉ làm bộ test dài mà không
    /// thêm độ phủ.
    /// </summary>
    [Theory]
    [InlineData("Pending")]
    [InlineData("1")]
    [InlineData("Approvedd")]
    [InlineData("Duyệt")]
    public async Task Status_khong_phai_mot_quyet_dinh_thi_400_kem_errors_status(string status)
    {
        using var factory = NewFactory();

        var (khach, quanLy) = await SeedHaiNguoiAsync(factory);
        var ve = NewPaidTicket(khach.Id, 250_000m);
        var yeuCau = NewPendingCancellation(ve, khach.Id, phiHuy: 25_000m, departureAt: Moc1.AddDays(3));

        await SeedAsync(factory, db =>
        {
            db.Tickets.Add(ve);
            db.CancellationRequests.Add(yeuCau);
        });

        using var client = AuthClientFor(factory, quanLy);

        var response = await client.PatchAsJsonAsync(
            $"{CancellationUrl}{yeuCau.Id}", new { status });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        // Lỗi trả về đúng một định dạng cho mọi endpoint: { message, errors: { field: [...] } }.
        var body = await ReadJsonAsync(response);

        Assert.False(string.IsNullOrWhiteSpace(MessageOf(body)));
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("errors").GetProperty("status")[0].GetString()));

        // 400 thì không ghi gì — yêu cầu vẫn nằm nguyên trong hàng chờ.
        var yeuCauSau = await QueryAsync(factory, db =>
            db.CancellationRequests.AsNoTracking().SingleAsync(c => c.Id == yeuCau.Id));

        Assert.Equal(CancellationRequestStatus.Pending, yeuCauSau.Status);
        Assert.Null(yeuCauSau.ReviewedByUserId);
    }

    /// <summary>
    /// Bản lề còn lại của phép so khớp: service cắt khoảng trắng và so KHÔNG phân biệt hoa/thường, nên
    /// <c>" aPpRoVeD "</c> phải nhận. Ghim chiều "dễ tính" này vì một bản cài đặt so khớp tuyệt đối
    /// (<c>value == "Approved"</c>) vẫn qua được toàn bộ Phần 2–3 mà lại chặn mất một client hợp lệ.
    /// </summary>
    [Theory]
    [InlineData(" aPpRoVeD ")]
    [InlineData("rejected\t")]
    public async Task Status_hoa_thuong_va_khoang_trang_thua_van_nhan(string status)
    {
        using var factory = NewFactory();

        var (khach, quanLy) = await SeedHaiNguoiAsync(factory);
        var ve = NewPaidTicket(khach.Id, 250_000m);
        var yeuCau = NewPendingCancellation(ve, khach.Id, phiHuy: 25_000m, departureAt: Moc1.AddDays(3));

        await SeedAsync(factory, db =>
        {
            db.Tickets.Add(ve);
            db.CancellationRequests.Add(yeuCau);
        });

        using var client = AuthClientFor(factory, quanLy);

        var response = await client.PatchAsJsonAsync(
            $"{CancellationUrl}{yeuCau.Id}", new { status });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// Trần 2 000 ký tự của <c>rejectReason</c>. Hai ca (đúng trần / quá trần một ký tự) đứng cạnh
    /// nhau để bản lề không trôi: nới lỏng phép kiểm thì ca 2 001 đỏ, siết chặt thì ca 2 000 đỏ.
    ///
    /// Lỗi này bắt ở tầng model binding (<c>[StringLength]</c>) nên vẫn theo đúng cấu trúc
    /// <c>{ message, errors }</c> — cùng lý do <c>status</c> để kiểu CHUỖI chứ không kiểu enum
    /// (mục D3 của <c>docs/03-quy-uoc.md</c>).
    /// </summary>
    [Fact]
    public async Task RejectReason_qua_2000_ky_tu_thi_400_kem_errors_rejectReason()
    {
        using var factory = NewFactory();

        var (khach, quanLy) = await SeedHaiNguoiAsync(factory);
        var ve = NewPaidTicket(khach.Id, 250_000m);
        var yeuCau = NewPendingCancellation(ve, khach.Id, phiHuy: 25_000m, departureAt: Moc1.AddDays(3));

        await SeedAsync(factory, db =>
        {
            db.Tickets.Add(ve);
            db.CancellationRequests.Add(yeuCau);
        });

        using var client = AuthClientFor(factory, quanLy);

        var response = await client.PatchAsJsonAsync(
            $"{CancellationUrl}{yeuCau.Id}",
            new { status = "Rejected", rejectReason = new string('a', 2001) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await ReadJsonAsync(response);

        Assert.False(string.IsNullOrWhiteSpace(
            body.GetProperty("errors").GetProperty("rejectReason")[0].GetString()));

        var yeuCauSau = await QueryAsync(factory, db =>
            db.CancellationRequests.AsNoTracking().SingleAsync(c => c.Id == yeuCau.Id));

        Assert.Equal(CancellationRequestStatus.Pending, yeuCauSau.Status);
    }

    [Fact]
    public async Task RejectReason_dung_2000_ky_tu_thi_nhan()
    {
        using var factory = NewFactory();

        var (khach, quanLy) = await SeedHaiNguoiAsync(factory);
        var ve = NewPaidTicket(khach.Id, 250_000m);
        var yeuCau = NewPendingCancellation(ve, khach.Id, phiHuy: 25_000m, departureAt: Moc1.AddDays(3));

        await SeedAsync(factory, db =>
        {
            db.Tickets.Add(ve);
            db.CancellationRequests.Add(yeuCau);
        });

        using var client = AuthClientFor(factory, quanLy);

        var lyDo = new string('a', 2000);

        var response = await client.PatchAsJsonAsync(
            $"{CancellationUrl}{yeuCau.Id}", new { status = "Rejected", rejectReason = lyDo });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await ReadJsonAsync(response);

        Assert.Equal(2000, body.GetProperty("rejectReason").GetString()!.Length);
    }

    // ===========================================================================================
    // PHẦN 5 — DDL PostgreSQL THẬT: hai ràng buộc mà InMemory không dựng nổi
    //
    // Phần này tồn tại vì lý do đã ghi ở cuối mục "Huỷ & Đổi vé" của docs/api-contract.md: bộ test
    // chạy EF InMemory KHÔNG dựng unique index lẫn khoá ngoại, nên các ca 409 ở Phần 2–3 chỉ chứng
    // minh được phép kiểm mô tả ở tầng service. Lưới thật — thứ chặn hai khoản hoàn tiền cho một vé
    // khi hai tab cùng bấm gửi, và thứ chặn mất dấu vết khi xoá một vé — nằm ở DDL, và chỉ đọc được
    // bằng GenerateCreateScript() với chuỗi kết nối Npgsql giả.
    //
    // Kỹ thuật và ba helper dưới đây (Ddl/DbNpgsql/DoanBang/SoLan) chép từ TicketQrIssuanceChainTests,
    // nơi đã dùng đúng lối này để phủ ràng buộc A6 (chống bán trùng ghế) — cũng là ràng buộc CSDL
    // không test được bằng InMemory. Giữ nguyên khuôn để hai file đọc như một.
    // ===========================================================================================

    /// <summary>
    /// 🔴 Ràng buộc quan trọng nhất của US 5, và là lý do Phần 5 phải tồn tại: <b>một vé chỉ được có
    /// MỘT yêu cầu huỷ ĐANG CHỜ</b>. Hai tab cùng bấm "Gửi yêu cầu huỷ" là chuyện xảy ra được — cả hai
    /// đọc "vé chưa có yêu cầu nào", cả hai ghi. Hai dòng <c>Pending</c> cho một vé nghĩa là hai lượt
    /// duyệt, tức <b>hai khoản hoàn tiền cho một chiếc vé</b>. Đây là lỗi tiền, đúng loại mà A5/A6
    /// sinh ra để chặn ở tầng CSDL chứ không giao hết cho service.
    ///
    /// Ghim nguyên câu DDL TÍNH CẢ dấu <c>;</c> ngay sau <c>("TicketId")</c>: thêm bất kỳ mệnh đề
    /// <c>WHERE</c> nào khác là chuỗi dưới đây không còn khớp và test đỏ.
    ///
    /// Vì sao phải là chỉ mục ĐIỀU KIỆN chứ không phải unique thường trên <c>TicketId</c>: một vé hợp
    /// lệ khi có nhiều yêu cầu theo thời gian — bị từ chối rồi xin lại. Unique thường sẽ chặn vĩnh
    /// viễn lần xin thứ hai. Nửa "không chặn dòng đã từ chối" được ghim ngay dưới.
    /// </summary>
    [Fact]
    public void Ddl_mot_ve_chi_mot_yeu_cau_huy_dang_cho()
    {
        Assert.Contains(
            "CREATE UNIQUE INDEX \"IX_CancellationRequests_TicketId_Pending\" "
            + "ON \"CancellationRequests\" (\"TicketId\") WHERE \"Status\" = 'Pending';",
            Ddl());
    }

    /// <summary>Bản song sinh cho bảng đổi vé — cùng ràng buộc, cùng tên quy ước. Hậu quả nếu thiếu còn nặng hơn một bậc: hai yêu cầu đổi cùng được duyệt là hai vé mới phát hành.</summary>
    [Fact]
    public void Ddl_mot_ve_chi_mot_yeu_cau_doi_dang_cho()
    {
        Assert.Contains(
            "CREATE UNIQUE INDEX \"IX_TicketChanges_TicketId_Pending\" "
            + "ON \"TicketChanges\" (\"TicketId\") WHERE \"Status\" = 'Pending';",
            Ddl());
    }

    /// <summary>
    /// Điều kiện của chỉ mục trên phải CHỈ là <c>'Pending'</c> — ghim vế phủ định riêng vì đây là chỗ
    /// dễ "sửa nhầm" nhất: thêm <c>Approved</c> vào điều kiện là chặn luôn việc khách xin lại sau một
    /// lần bị từ chối, và triệu chứng sẽ là một lỗi 500 lúc gửi yêu cầu — rất xa chỗ gây ra.
    /// </summary>
    [Fact]
    public void Ddl_chi_muc_dang_cho_khong_chan_dong_da_xu_ly()
    {
        var dong = Ddl()
            .Split('\n')
            .Single(line => line.StartsWith(
                "CREATE UNIQUE INDEX \"IX_CancellationRequests_TicketId_Pending\"", StringComparison.Ordinal));

        Assert.DoesNotContain("Approved", dong);
        Assert.DoesNotContain("Rejected", dong);
    }

    /// <summary>
    /// 🔴 Chỉ mục <c>TicketId</c> THƯỜNG phải tồn tại SONG SONG với chỉ mục điều kiện ở trên — không
    /// thừa, dù cả hai cùng mở đầu bằng <c>TicketId</c>. PostgreSQL chỉ dùng được partial index khi
    /// truy vấn CHỨNG MINH được điều kiện của nó, mà câu hỏi *"vé này từng bị xin huỷ chưa"* không nói
    /// gì về <c>Status</c> nên không thoả → bộ lập kế hoạch bỏ qua chỉ mục kia và quét cả bảng.
    ///
    /// Cạm bẫy thật của EF: <c>HasIndex</c> nhận diện chỉ mục theo DANH SÁCH CỘT, nên lần gọi thứ hai
    /// cho cùng cột trả về đúng builder của lần gọi đầu (đã unique + có điều kiện) và <b>im lặng</b>
    /// không sinh chỉ mục thứ hai. Hai dòng dưới đây là thứ bắt được lần im lặng đó.
    /// </summary>
    [Fact]
    public void Ddl_chi_muc_TicketId_thuong_van_con_ben_canh_chi_muc_dieu_kien()
    {
        var ddl = Ddl();

        Assert.Contains("CREATE INDEX \"IX_CancellationRequests_TicketId\" ON \"CancellationRequests\" (\"TicketId\");", ddl);
        Assert.Contains("CREATE INDEX \"IX_TicketChanges_TicketId\" ON \"TicketChanges\" (\"TicketId\");", ddl);
    }

    /// <summary>
    /// Tiền của hai bảng mới là <c>numeric(12,2)</c> — cùng đơn vị và cùng độ rộng với
    /// <c>Tickets.Price</c>, <c>Fares.Price</c>, <c>Payments.Amount</c> (A3). <c>CancellationFee</c> là
    /// phí huỷ và <c>NewPrice</c> là giá vé mới, cả hai đều là TIỀN.
    ///
    /// Ghim cả hai vế phủ định vì sai số dấu phẩy động trên tiền là lỗi KHÔNG SỬA ĐƯỢC: hoá đơn đã
    /// xuất không cộng lại đúng doanh thu. Và ghim <c>NOT NULL</c> của phí: <c>0</c> là "huỷ miễn phí",
    /// còn <c>null</c> sẽ làm phép trừ <c>price - fee</c> không thực hiện được — đúng ghi chú ở
    /// doc-comment <see cref="CancellationRequest.CancellationFee"/>.
    /// </summary>
    [Fact]
    public void Ddl_tien_cua_hai_bang_la_numeric_12_2_khong_phai_dau_phay_dong()
    {
        var ddl = Ddl();

        Assert.Contains("\"CancellationFee\" numeric(12,2) NOT NULL", ddl);
        Assert.Contains("\"NewPrice\" numeric(12,2) NOT NULL", ddl);

        Assert.DoesNotContain("\"CancellationFee\" double precision", ddl);
        Assert.DoesNotContain("\"CancellationFee\" real", ddl);
        Assert.DoesNotContain("\"NewPrice\" double precision", ddl);
        Assert.DoesNotContain("\"NewPrice\" real", ddl);
    }

    /// <summary>
    /// <c>DepartureAt</c> là <c>timestamptz</c> (A3) — chứ không phải <c>timestamp</c> thiếu múi giờ.
    /// Cột này là cột CHỤP LẠI giờ chạy để giải thích được vì sao hoá đơn ghi phí 30%, nên lệch múi
    /// giờ ở đây là lệch đúng con số mà câu hỏi kiểm toán sẽ hỏi.
    ///
    /// Ghim kèm cột <c>Status</c> lưu dạng CHUỖI 20 ký tự — "Pending" đọc là hiểu, còn 1 thì phải tra
    /// bảng mã, và <c>Approved</c>/<c>Rejected</c> phải nằm gọn trong 20 ký tự.
    ///
    /// Hai phép kiểm Cột được soi trong ĐOẠN CỦA TỪNG BẢNG chứ không trên cả script: 13 bảng khác
    /// trong dự án cũng có <c>"Status" character varying(20) NOT NULL</c>, nên đếm trên toàn script là
    /// đếm nhầm sang bảng của người khác — test sẽ đỏ khi người khác thêm bảng, mà chẳng nói gì về
    /// hai bảng này.
    /// </summary>
    [Fact]
    public void Ddl_DepartureAt_la_timestamptz_va_Status_luu_chuoi()
    {
        foreach (var ten in new[] { "CancellationRequests", "TicketChanges" })
        {
            var bang = DoanBang(ten);

            Assert.Contains("\"Status\" character varying(20) NOT NULL", bang);
        }

        Assert.Contains("\"DepartureAt\" timestamp with time zone NOT NULL", DoanBang("CancellationRequests"));

        Assert.DoesNotContain("timestamp without time zone", Ddl());
    }

    /// <summary>
    /// 🔴 A5 — lỗi tốn kém nhất của dự án: EF mặc định <c>Cascade</c> cho mọi quan hệ bắt buộc, nên
    /// xoá một TICKET sẽ kéo theo yêu cầu huỷ và yêu cầu đổi — tức là mất dấu vết kiểm toán của một
    /// khoản hoàn tiền. Cả hai bảng mới phải Restrict tường minh ở MỌI khoá ngoại.
    ///
    /// Đếm theo hai chiều: số khoá ngoại sinh ra phải bằng số lần Restrict (3 cho
    /// <c>CancellationRequests</c>: vé, người gửi, người duyệt — và 6 cho <c>TicketChanges</c>: vé gốc,
    /// người gửi, chuyến mới, ghế mới, vé mới, người duyệt). Đếm vế đầu bắt được khoá ngoại bị xoá
    /// mất, đếm vế sau bắt được một khoá ngoại còn đó nhưng đã đổi hành vi xoá.
    /// </summary>
    [Fact]
    public void Ddl_moi_khoa_ngoai_cua_hai_bang_deu_RESTRICT()
    {
        var bangHuy = DoanBang("CancellationRequests");
        var bangDoi = DoanBang("TicketChanges");

        Assert.Equal(3, SoLan(bangHuy, "FOREIGN KEY"));
        Assert.Equal(3, SoLan(bangHuy, "ON DELETE RESTRICT"));

        Assert.Equal(6, SoLan(bangDoi, "FOREIGN KEY"));
        Assert.Equal(6, SoLan(bangDoi, "ON DELETE RESTRICT"));

        // Nhánh Cascade duy nhất được phép trong dự án là RouteStops←Routes, Seats←Buses,
        // RefreshTokens←Users (A5) — không nhánh nào thuộc hai bảng này.
        Assert.DoesNotContain("ON DELETE CASCADE", bangHuy);
        Assert.DoesNotContain("ON DELETE CASCADE", bangDoi);
    }

    /// <summary>
    /// Khoá phiên bản dòng cho nhánh đua "hai quản lý cùng duyệt một yêu cầu" — lớp chặn thứ HAI, và
    /// là lớp duy nhất cứu được lượt đua thật: lúc cả hai cùng ghi thì cả hai đều đã rời khỏi tập
    /// <c>Pending</c> nên chỉ mục duy nhất điều kiện ở trên <b>không cứu được</b>.
    ///
    /// Với <c>xmin</c>, EF thêm <c>WHERE xmin = …</c> vào câu UPDATE; lượt thua nhận
    /// <c>DbUpdateConcurrencyException</c> và service trả 409 (⛔ tuyệt đối không để thành 500).
    /// Trên InMemory khoá này KHÔNG hoạt động — nên đây là phép kiểm duy nhất trong repo khẳng định
    /// nó thực sự có mặt trong model.
    ///
    /// <c>Assert.DoesNotContain("\"xmin\"")</c> là phép ghim thứ hai, đắt không kém: <c>xmin</c> là
    /// cột HỆ THỐNG của PostgreSQL, phải được khai bằng <c>HasColumnName("xmin")</c> để Npgsql nhận ra
    /// và KHÔNG sinh cột thật. Nếu tên đổi thành <c>Xmin</c> hay <c>Version</c>, EF sẽ sinh một cột
    /// mới trong migration — và cột đó không tự cập nhật như <c>xmin</c> làm, nên khoá lạc hậu im lặng
    /// không bao giờ chặn được lượt đua nào nữa.
    /// </summary>
    [Fact]
    public void Ddl_hai_bang_yeu_cau_deu_co_khoa_phien_ban_dong_xmin()
    {
        // Vế 1 — `xmin` KHÔNG được sinh thành cột thật. Npgsql nhận ra nó (khai bằng
        // HasColumnName("xmin")) và bỏ khỏi DDL, vì đó là cột HỆ THỐNG đã có sẵn trong mọi bảng
        // PostgreSQL và tự tăng sau mỗi UPDATE. Khai sai tên thì EF sinh thêm một cột thật — cột đó
        // đứng yên, không tự tăng, nên khoá lạc hậu im lặng không chặn được lượt đua nào nữa.
        Assert.DoesNotContain("\"xmin\"", Ddl());

        // Vế 2 — nhưng khoá phiên bản PHẢI có trong model. Mất nó là mệnh đề WHERE của câu UPDATE
        // không còn `xmin = …`, và nhánh 409 "đã có người xử lý" (Phần 2) mất đi lớp chặn thứ hai —
        // lớp DUY NHẤT cứu được lượt đua thật, vì lúc cả hai cùng ghi thì cả hai đều đã rời tập Pending
        // nên chỉ mục duy nhất điều kiện không phát hiện được gì.
        foreach (var kieu in new[] { typeof(CancellationRequest), typeof(TicketChange) })
        {
            var khoa = ModelNpgsql().FindEntityType(kieu)?.FindProperty("xmin");

            Assert.NotNull(khoa);
            Assert.True(khoa!.IsConcurrencyToken, $"{kieu.Name}.xmin phải là khoá phiên bản dòng.");
        }
    }

    // ===========================================================================================
    // Helper — mỗi lớp test tự chép, không có lớp base chung (đúng lệ đang có của dự án)
    // ===========================================================================================

    /// <summary>
    /// App THẬT của <see cref="TestAppFactory"/> cộng khoá ký QR của test, đúng lối
    /// <see cref="TicketRequestReviewWiringTests"/>. Phải đi qua <c>WithWebHostBuilder</c> chứ không
    /// dùng thẳng <see cref="TestAppFactory"/>: khoá QR chỉ có nghĩa lúc Program.cs dựng service
    /// (<c>TicketQrService</c> ném ngay khi thiếu) nên phải có mặt trước khi host build.
    /// </summary>
    private static WebApplicationFactory<Program> NewFactory()
        => new TestAppFactory().WithWebHostBuilder(builder =>
            builder.UseSetting("TicketQr:SigningKey", QrSigningKey));

    /// <summary>Một khách và một quản lý — bộ tối thiểu của mọi ca.</summary>
    private static async Task<(UserEntity Khach, UserEntity QuanLy)> SeedHaiNguoiAsync(
        WebApplicationFactory<Program> factory)
    {
        var khach = await SeedUserAsync(factory, RoleIds.Passenger, RoleCodes.Passenger, "Khách Test");
        var quanLy = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");

        return (khach, quanLy);
    }

    /// <summary>
    /// Thêm người thứ ba khi ca test cần một NGƯỜI KHÁC chiếm ghế, hoặc cần một người đã duyệt trước.
    /// </summary>
    private static async Task<(UserEntity Khach, UserEntity QuanLy, UserEntity NguoiThuBa)> SeedBaNguoiAsync(
        WebApplicationFactory<Program> factory)
    {
        var khach = await SeedUserAsync(factory, RoleIds.Passenger, RoleCodes.Passenger, "Khách Test");
        var quanLy = await SeedUserAsync(factory, RoleIds.Manager, RoleCodes.Manager, "Quản Lý Test");
        var nguoiKhac = await SeedUserAsync(factory, RoleIds.Passenger, RoleCodes.Passenger, "Người Khác");

        return (khach, quanLy, nguoiKhac);
    }

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

    private static Trip NewTrip(TripStatus trangThai) => new()
    {
        RouteId = Guid.NewGuid(),
        BusId = Guid.NewGuid(),
        DepartureTime = Moc1.AddDays(5),
        Status = trangThai,
        CreatedAt = Moc1,
    };

    private static CancellationRequest NewPendingCancellation(
        Ticket ve, Guid khachId, decimal phiHuy, DateTime departureAt) => new()
    {
        TicketId = ve.Id,
        UserId = khachId,
        Reason = "Bận việc gia đình",
        CancellationFee = phiHuy,
        DepartureAt = departureAt,
        Status = CancellationRequestStatus.Pending,
        CreatedAt = Moc1,
    };

    private static TicketChange NewPendingChange(
        Ticket veCu, Guid khachId, Trip chuyenMoi, decimal newPrice, Guid? newSeatId = null) => new()
    {
        TicketId = veCu.Id,
        UserId = khachId,
        NewTripId = chuyenMoi.Id,
        NewSeatId = newSeatId ?? Guid.NewGuid(),
        NewPrice = newPrice,
        Reason = "Muốn đi chuyến khác",
        Status = TicketChangeStatus.Pending,
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

    /// <summary>Đọc dữ liệu đã ghi bằng scope MỚI — lượt PATCH ghi ở change tracker của scope nó.</summary>
    private static async Task<T> QueryAsync<T>(
        WebApplicationFactory<Program> factory, Func<AppDbContext, Task<T>> query)
    {
        using var scope = factory.Services.CreateScope();

        return await query(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

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

    /// <summary>Sinh access token bằng chính <see cref="ITokenService"/> của app, tránh test lệch khỏi code thật.</summary>
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

    /// <summary>
    /// DDL PostgreSQL đầy đủ sinh từ chính <see cref="AppDbContext"/> của app. Dựng context bằng
    /// Npgsql (không mở kết nối nào) chứ KHÔNG dùng <see cref="TestAppFactory"/> — provider InMemory
    /// bỏ qua <c>HasFilter</c> nên sẽ không sinh ra chỉ mục điều kiện cần kiểm.
    /// </summary>
    private static string Ddl()
    {
        using var db = DbNpgsql();

        return db.Database.GenerateCreateScript();
    }

    /// <summary>Model EF dựng trên Npgsql — để đọc metadata (khoá phiên bản, kiểu cột) chứ không chỉ DDL.</summary>
    private static IModel ModelNpgsql()
    {
        using var db = DbNpgsql();

        return db.Model;
    }

    /// <summary>
    /// Context dựng bằng Npgsql nhưng KHÔNG mở kết nối nào — cả <c>GenerateCreateScript()</c> lẫn việc
    /// đọc <c>db.Model</c> đều là phép biến đổi model, chạy hoàn toàn ngoại tuyến.
    /// </summary>
    private static AppDbContext DbNpgsql()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(ChuoiKetNoiGia)
            .Options;

        return new AppDbContext(options);
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
}
