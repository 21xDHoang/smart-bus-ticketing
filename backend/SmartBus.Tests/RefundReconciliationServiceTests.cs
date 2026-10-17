using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SmartBus.Api.Data;
using SmartBus.Api.Dtos.Refunds;
using SmartBus.Api.Entities;
using SmartBus.Api.Services;

namespace SmartBus.Tests;

/// <summary>
/// Test bước đối soát hoàn tiền hằng ngày — <see cref="RefundReconciliationService"/> (US 8, task
/// *"Đối soát hoàn tiền hằng ngày + cảnh báo khi lệch số liệu"*, dòng 32 Sprint 4, Nguyễn Duy Kiên).
///
/// Gọi thẳng service, KHÔNG dựng host: phép đối soát chỉ đọc CSDL và một nguồn sao kê, nên InMemory
/// + nguồn sao kê giả là đủ. Chỗ nối vào app (job có được đăng ký không, nguồn mặc định là gì) nằm ở
/// <see cref="RefundReconciliationWiringTests"/>.
///
/// <para>
/// ⚠️ <b>Hai nhóm phép kiểm được test riêng, đúng như service tách chúng:</b>
/// <list type="bullet">
/// <item>nhóm SỔ NỘI BỘ — luôn chạy, không phụ thuộc nguồn sao kê;</item>
/// <item>nhóm SAO KÊ CỔNG — chỉ chạy khi nguồn trả dữ liệu. Bài
/// <see cref="Nguon_sao_ke_tra_null_thi_bo_qua_phan_doi_chieu"/> ghim đúng ranh giới "không biết"
/// khác "sao kê rỗng" — nhầm hai thứ này là báo giả toàn bộ khoản hoàn trong ngày.
/// </item>
/// </list>
/// </para>
///
/// <para>
/// ⚠️ InMemory không cưỡng chế khoá ngoại: dữ liệu ở đây chỉ cần đủ các cột mà service ĐỌC. Test này
/// xanh không nói gì về <c>IX_Refunds_PaymentId_Active</c> — chỉ mục đó nằm trong migrations của Vàng
/// Thị Dăm, còn phép kiểm <c>MultipleActiveRefundsForPayment</c> ở đây canh đúng ca chỉ mục bị thiếu.
/// </para>
/// </summary>
public class RefundReconciliationServiceTests
{
    private const decimal SoTien = 175_000m;

    /// <summary>Mốc "bây giờ" cố định — không đồng hồ thật, không chập chờn.</summary>
    private static readonly DateTime BayGio = new(2026, 10, 17, 0, 5, 0, DateTimeKind.Utc);

    private readonly string _tenCsdl = $"doi-soat-hoan-tien-{Guid.NewGuid():N}";

    // ── Sổ nội bộ: khoản sạch im lặng ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Khoan_hoan_sach_khong_sinh_diem_lech()
    {
        await using var db = TaoDb();
        var payment = TaoPayment();
        var khoan = TaoRefund(
            payment,
            RefundStatus.Succeeded,
            processedAt: BayGio.AddHours(-1),
            gatewayTransactionId: "GW-1");
        await LuuAsync(db, payment, khoan);
        await ThemLogAsync(db, khoan, RefundLogResult.Succeeded, "GW-1");

        var baoCao = await TaoService(db).RunAsync(BayGio);

        // Không lệch là trạng thái BÌNH THƯỜNG — không cảnh báo, không sinh thông báo. Bài này là
        // mốc để mọi bài dưới đây biết mình đang thêm đúng MỘT nguyên nhân lệch.
        Assert.Empty(baoCao.Issues);
        Assert.False(baoCao.HasMismatch);
        Assert.Equal(0, baoCao.CriticalIssueCount);

        // Nguồn sao kê mặc định không có dữ liệu — báo cáo phải nói "không khả dụng", không được
        // suy ra "đã đối chiếu và khớp".
        Assert.False(baoCao.StatementAvailable);
        Assert.Equal(1, baoCao.ScannedRefundCount);
        Assert.Equal(BayGio.AddHours(-24), baoCao.WindowStartUtc);
        Assert.Equal(BayGio, baoCao.WindowEndUtc);
    }

    // ── Sổ nội bộ: bằng chứng thành công trên khoản CHƯA thành công ─────────────────────────────

    [Fact]
    public async Task Nhat_ky_co_luot_thanh_cong_nhung_khoan_chua_succeeded_bi_bao_nghiem_trong()
    {
        await using var db = TaoDb();
        var payment = TaoPayment();
        var khoan = TaoRefund(payment, RefundStatus.Pending, createdAt: BayGio.AddHours(-2));
        await LuuAsync(db, payment, khoan);

        // Đúng vết của nhánh DbUpdateConcurrencyException trong RefundAttemptService: cổng đã trả
        // tiền, lượt ghi kết quả thua, dòng Refunds nằm lại Pending trong khi nhật ký giữ bằng chứng.
        await ThemLogAsync(db, khoan, RefundLogResult.Succeeded, "GW-1");

        var baoCao = await TaoService(db).RunAsync(BayGio);

        var issue = Assert.Single(baoCao.Issues);
        Assert.Equal(RefundReconciliationIssueKind.SuccessEvidenceOnOpenRefund, issue.Kind);
        Assert.Equal(RefundReconciliationSeverity.Critical, issue.Severity);
        Assert.Equal(khoan.Id, issue.RefundId);
        Assert.Equal(khoan.RefundCode, issue.RefundCode);
        Assert.Equal(1, baoCao.CriticalIssueCount);
    }

    // ── Sổ nội bộ: Pending mắc kẹt ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Pending_qua_24_gio_bi_bao()
    {
        await using var db = TaoDb();
        var payment = TaoPayment();
        var khoan = TaoRefund(payment, RefundStatus.Pending, createdAt: BayGio.AddHours(-25));
        await LuuAsync(db, payment, khoan);

        var baoCao = await TaoService(db).RunAsync(BayGio);

        // Không job nào nhặt khoản Pending: job thử lại chỉ quét Failed. Quá 24 giờ nghĩa là lượt
        // gọi cổng trước đó không có câu trả lời chốt được — phải tra sao kê TRƯỚC khi gọi lại.
        var issue = Assert.Single(baoCao.Issues);
        Assert.Equal(RefundReconciliationIssueKind.StuckPending, issue.Kind);
        Assert.Equal(RefundReconciliationSeverity.Critical, issue.Severity);
    }

    [Fact]
    public async Task Pending_con_trong_nguong_thi_im_lang()
    {
        await using var db = TaoDb();
        var payment = TaoPayment();
        var khoan = TaoRefund(payment, RefundStatus.Pending, createdAt: BayGio.AddHours(-23));
        await LuuAsync(db, payment, khoan);

        var baoCao = await TaoService(db).RunAsync(BayGio);

        // 23 giờ vẫn có thể là một lượt gọi đang chạy hoặc vừa thua cuộc — chưa phải chuyện để báo.
        Assert.Empty(baoCao.Issues);
    }

    // ── Sổ nội bộ: bằng chứng của khoản Succeeded ─────────────────────────────────────────────

    [Fact]
    public async Task Succeeded_thieu_bang_chung_bi_bao()
    {
        await using var db = TaoDb();
        var payment = TaoPayment();
        var khoan = TaoRefund(payment, RefundStatus.Succeeded);
        await LuuAsync(db, payment, khoan);

        var baoCao = await TaoService(db).RunAsync(BayGio);

        // Thiếu mã cổng, mốc xử lý VÀ nhật ký — dòng này không đối chiếu được với bất kỳ sao kê nào.
        var issue = Assert.Single(baoCao.Issues);
        Assert.Equal(RefundReconciliationIssueKind.SucceededRefundMissingEvidence, issue.Kind);
        Assert.Equal(RefundReconciliationSeverity.Critical, issue.Severity);
    }

    [Fact]
    public async Task Ma_giao_dich_tren_khoan_khong_khop_nhat_ky_bi_bao()
    {
        await using var db = TaoDb();
        var payment = TaoPayment();
        var khoan = TaoRefund(
            payment,
            RefundStatus.Succeeded,
            processedAt: BayGio.AddHours(-1),
            gatewayTransactionId: "GW-LUOT-CUOI");
        await LuuAsync(db, payment, khoan);
        await ThemLogAsync(db, khoan, RefundLogResult.Succeeded, "GW-DA-TRA");

        var baoCao = await TaoService(db).RunAsync(BayGio);

        // Doc-comment Refund.GatewayTransactionId cảnh báo đúng cặp này: "mã của lượt cuối" và "mã
        // của lượt đã trả tiền" có thể khác nhau. Lệch cặp là dấu hiệu cần người tra tay.
        var issue = Assert.Single(baoCao.Issues);
        Assert.Equal(RefundReconciliationIssueKind.SucceededRefundGatewayIdMismatch, issue.Kind);
        Assert.Equal(RefundReconciliationSeverity.Critical, issue.Severity);
    }

    [Fact]
    public async Task Hai_luot_thanh_cong_tren_mot_khoan_bi_bao_nghi_hoan_hai_lan()
    {
        await using var db = TaoDb();
        var payment = TaoPayment();
        var khoan = TaoRefund(
            payment,
            RefundStatus.Succeeded,
            processedAt: BayGio.AddHours(-1),
            gatewayTransactionId: "GW-1");
        await LuuAsync(db, payment, khoan);
        await ThemLogAsync(db, khoan, RefundLogResult.Succeeded, "GW-1");
        await ThemLogAsync(db, khoan, RefundLogResult.Succeeded, "GW-2");

        var baoCao = await TaoService(db).RunAsync(BayGio);

        // Hai lượt báo thành công cho cùng một khoản là nguy cơ đã trả tiền hai lần — đúng ca mà
        // toàn bộ cơ chế giành dòng của RefundAttemptService tồn tại để chặn.
        var issue = Assert.Single(baoCao.Issues);
        Assert.Equal(RefundReconciliationIssueKind.MultipleSucceededLogs, issue.Kind);
        Assert.Equal(RefundReconciliationSeverity.Critical, issue.Severity);
    }

    [Fact]
    public async Task Cot_trang_thai_mau_thuan_bi_bao()
    {
        await using var db = TaoDb();
        var payment = TaoPayment();
        var khoan = TaoRefund(
            payment,
            RefundStatus.Succeeded,
            processedAt: BayGio.AddHours(-1),
            gatewayTransactionId: "GW-1",
            // Hai cột này chỉ được khác null khi Status = Failed.
            failReason: "Lý do của lượt cũ",
            nextRetryAt: BayGio.AddHours(1));
        await LuuAsync(db, payment, khoan);
        await ThemLogAsync(db, khoan, RefundLogResult.Succeeded, "GW-1");

        var baoCao = await TaoService(db).RunAsync(BayGio);

        var issue = Assert.Single(baoCao.Issues);
        Assert.Equal(RefundReconciliationIssueKind.StatusFieldInvariantBroken, issue.Kind);
        Assert.Equal(RefundReconciliationSeverity.Critical, issue.Severity);
    }

    // ── Sổ nội bộ: khoản Failed ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Failed_qua_han_thu_lai_bi_bao()
    {
        await using var db = TaoDb();
        var payment = TaoPayment();
        var khoan = TaoRefund(
            payment,
            RefundStatus.Failed,
            failReason: "Cổng bận",
            nextRetryAt: BayGio.AddHours(-2));
        await LuuAsync(db, payment, khoan);
        await ThemLogAsync(db, khoan, RefundLogResult.Failed);

        var baoCao = await TaoService(db).RunAsync(BayGio);

        // Job thử lại quét mỗi phút; trễ hơn một giờ gần như chắc chắn là job không chạy chứ không
        // phải nhiễu nhịp. Đây là cảnh báo, chưa nói gì về việc tiền đã đi hay chưa.
        var issue = Assert.Single(baoCao.Issues);
        Assert.Equal(RefundReconciliationIssueKind.RetryOverdue, issue.Kind);
        Assert.Equal(RefundReconciliationSeverity.Warning, issue.Severity);
    }

    [Fact]
    public async Task Failed_thieu_ly_do_va_nhat_ky_bi_bao_hai_diem()
    {
        await using var db = TaoDb();
        var payment = TaoPayment();
        var khoan = TaoRefund(
            payment,
            RefundStatus.Failed,
            nextRetryAt: BayGio.AddHours(1));
        await LuuAsync(db, payment, khoan);

        var baoCao = await TaoService(db).RunAsync(BayGio);

        Assert.Equal(2, baoCao.Issues.Count);
        Assert.Contains(baoCao.Issues, i => i.Kind == RefundReconciliationIssueKind.FailedRefundMissingReason);
        Assert.Contains(baoCao.Issues, i => i.Kind == RefundReconciliationIssueKind.MissingAttemptLogs);
        Assert.All(baoCao.Issues, i => Assert.Equal(RefundReconciliationSeverity.Warning, i.Severity));
    }

    // ── Sổ nội bộ: số tiền và nguồn ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Hoan_nhieu_hon_giao_dich_goc_bi_bao()
    {
        await using var db = TaoDb();
        var payment = TaoPayment(amount: 100_000m);
        var khoan = TaoRefund(payment, RefundStatus.Pending, amount: SoTien);
        await LuuAsync(db, payment, khoan);

        var baoCao = await TaoService(db).RunAsync(BayGio);

        var issue = Assert.Single(baoCao.Issues);
        Assert.Equal(RefundReconciliationIssueKind.InvalidAmount, issue.Kind);
        Assert.Equal(RefundReconciliationSeverity.Critical, issue.Severity);

        // Cố ý KHÔNG so với công thức phí huỷ: Refund.Amount là số ĐÃ GỬI LÊN CỔNG, còn công thức
        // hôm nay có thể khác chính sách hôm gửi. Chỉ kiểm được "dương" và "không vượt giao dịch gốc".
    }

    [Fact]
    public async Task Nguon_khoan_hoan_mau_thuan_bi_bao()
    {
        await using var db = TaoDb();
        var payment = TaoPayment();
        var khoan = TaoRefund(
            payment,
            RefundStatus.Pending,
            cancellationHopLe: false,
            ticketHopLe: false);
        await LuuAsync(db, payment, khoan);

        var baoCao = await TaoService(db).RunAsync(BayGio);

        // Bất biến docs/40 §4.3: CancellationRequestId ≠ null ⟺ Source = TicketCancellation, và vé
        // là cột hiển thị bắt buộc của nhánh đó. Thiếu cả hai là dòng không truy được nguồn.
        var issue = Assert.Single(baoCao.Issues);
        Assert.Equal(RefundReconciliationIssueKind.SourceInvariantBroken, issue.Kind);
        Assert.Equal(RefundReconciliationSeverity.Critical, issue.Severity);
        Assert.Contains("CancellationRequestId", issue.Message, StringComparison.Ordinal);
        Assert.Contains("TicketId", issue.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Hai_khoan_hoan_cung_giao_dich_bi_bao()
    {
        await using var db = TaoDb();
        var payment = TaoPayment();
        var khoanA = TaoRefund(
            payment,
            RefundStatus.Succeeded,
            processedAt: BayGio.AddHours(-1),
            gatewayTransactionId: "GW-A",
            refundCode: "RF-aaaa1111");
        var khoanB = TaoRefund(
            payment,
            RefundStatus.Succeeded,
            processedAt: BayGio.AddHours(-1),
            gatewayTransactionId: "GW-B",
            refundCode: "RF-bbbb2222");
        await LuuAsync(db, payment, khoanA, khoanB);
        await ThemLogAsync(db, khoanA, RefundLogResult.Succeeded, "GW-A");
        await ThemLogAsync(db, khoanB, RefundLogResult.Succeeded, "GW-B");

        var baoCao = await TaoService(db).RunAsync(BayGio);

        // Chỉ mục điều kiện IX_Refunds_PaymentId_Active lẽ ra đã chặn ở tầng CSDL. Phép kiểm này bắt
        // ca chỉ mục bị thiếu hoặc dữ liệu được nạp tay — mỗi khoản vẫn "sạch" khi xét riêng.
        var issue = Assert.Single(baoCao.Issues);
        Assert.Equal(RefundReconciliationIssueKind.MultipleActiveRefundsForPayment, issue.Kind);
        Assert.Equal(RefundReconciliationSeverity.Critical, issue.Severity);
        Assert.Contains("RF-aaaa1111", issue.Message, StringComparison.Ordinal);
        Assert.Contains("RF-bbbb2222", issue.Message, StringComparison.Ordinal);
    }

    // ── Phạm vi quét ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Khoan_cu_da_chot_khong_bi_kiem_lai_moi_ngay()
    {
        await using var db = TaoDb();
        var payment = TaoPayment();

        // Khoản Succeeded mười ngày trước, không có nhật ký — nếu bị nạp lại thì SucceededRefundMissingEvidence
        // sẽ nổ. Nó KHÔNG được nạp: đã chốt và không chạm trong khoảng.
        var khoan = TaoRefund(
            payment,
            RefundStatus.Succeeded,
            createdAt: BayGio.AddDays(-10),
            processedAt: BayGio.AddDays(-10),
            gatewayTransactionId: "GW-CU");
        await LuuAsync(db, payment, khoan);

        var baoCao = await TaoService(db).RunAsync(BayGio);

        Assert.Equal(0, baoCao.ScannedRefundCount);
        Assert.Empty(baoCao.Issues);
    }

    [Fact]
    public async Task Khoan_cu_vua_bi_sua_van_duoc_kiem_lai()
    {
        await using var db = TaoDb();
        var payment = TaoPayment();
        var khoan = TaoRefund(
            payment,
            RefundStatus.Failed,
            createdAt: BayGio.AddDays(-10),
            failReason: "Cổng bận");
        khoan.UpdatedAt = BayGio.AddHours(-2);
        await LuuAsync(db, payment, khoan);

        // Ai đó bấm "Thử lại" hôm qua: dòng cũ nhưng vừa chạm, nên vẫn phải nằm trong lượt kiểm —
        // nếu không, mọi thao tác tay trên khoản cũ đều thoát khỏi tầm đối soát.
        var baoCao = await TaoService(db).RunAsync(BayGio);

        Assert.Equal(1, baoCao.ScannedRefundCount);
        Assert.Contains(baoCao.Issues, i => i.Kind == RefundReconciliationIssueKind.MissingAttemptLogs);
    }

    // ── Sao kê cổng ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Nguon_sao_ke_tra_null_thi_bo_qua_phan_doi_chieu()
    {
        await using var db = TaoDb();
        var payment = TaoPayment();
        var khoan = TaoRefund(
            payment,
            RefundStatus.Succeeded,
            processedAt: BayGio.AddHours(-1),
            gatewayTransactionId: "GW-1");
        await LuuAsync(db, payment, khoan);
        await ThemLogAsync(db, khoan, RefundLogResult.Succeeded, "GW-1");

        var nguon = SaoKeGia.Hong();
        var baoCao = await TaoService(db, nguon).RunAsync(BayGio);

        // null = KHÔNG BIẾT, không phải "sao kê rỗng". Kết luận "khoản này không có trên sao kê" từ
        // chỗ không có dữ liệu là báo giả; lượt đối soát nội bộ vẫn phải giữ nguyên kết quả.
        Assert.False(baoCao.StatementAvailable);
        Assert.Empty(baoCao.Issues);

        // Khoảng hỏi sao kê đúng bằng cửa sổ 24 giờ của báo cáo.
        Assert.Equal(BayGio.AddHours(-24), nguon.FromUtc);
        Assert.Equal(BayGio, nguon.ToUtc);
    }

    [Fact]
    public async Task Sao_ke_rong_nhung_doc_duoc_van_la_co_doi_chieu()
    {
        await using var db = TaoDb();
        var payment = TaoPayment();
        var khoan = TaoRefund(
            payment,
            RefundStatus.Succeeded,
            processedAt: BayGio.AddHours(-1),
            gatewayTransactionId: "GW-1");
        await LuuAsync(db, payment, khoan);
        await ThemLogAsync(db, khoan, RefundLogResult.Succeeded, "GW-1");

        var baoCao = await TaoService(db, SaoKeGia.Co()).RunAsync(BayGio);

        // Sao kê rỗng là một KHẲNG ĐỊNH của cổng: "kỳ này không có lệnh hoàn nào". Khi đó khoản
        // Succeeded trong kỳ thật sự thiếu trên sao kê — đây chính là ca phải báo.
        Assert.True(baoCao.StatementAvailable);
        var issue = Assert.Single(baoCao.Issues);
        Assert.Equal(RefundReconciliationIssueKind.StatementMissingRefund, issue.Kind);
        Assert.Equal(RefundReconciliationSeverity.Critical, issue.Severity);
    }

    [Fact]
    public async Task Sao_ke_ghi_so_tien_khac_bi_bao()
    {
        await using var db = TaoDb();
        var payment = TaoPayment(amount: 300_000m);
        var khoan = TaoRefund(
            payment,
            RefundStatus.Succeeded,
            processedAt: BayGio.AddHours(-1),
            gatewayTransactionId: "GW-1");
        await LuuAsync(db, payment, khoan);
        await ThemLogAsync(db, khoan, RefundLogResult.Succeeded, "GW-1");

        var baoCao = await TaoService(db, SaoKeGia.Co(Dong("GW-1", 150_000m))).RunAsync(BayGio);

        var issue = Assert.Single(baoCao.Issues);
        Assert.Equal(RefundReconciliationIssueKind.StatementAmountMismatch, issue.Kind);
        Assert.Equal(RefundReconciliationSeverity.Critical, issue.Severity);
        Assert.Contains("150000", issue.Message, StringComparison.Ordinal);
        Assert.Contains("175000", issue.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sao_ke_co_lenh_hoan_la_bi_bao()
    {
        await using var db = TaoDb();

        var baoCao = await TaoService(db, SaoKeGia.Co(Dong("GW-LA", 90_000m))).RunAsync(BayGio);

        // Tiền đã rời cổng mà hệ thống không có dòng nào — ca nghiêm trọng nhất của chiều thứ hai.
        // Điểm lệch không gắn được với khoản hoàn nào nên RefundId phải null.
        var issue = Assert.Single(baoCao.Issues);
        Assert.Equal(RefundReconciliationIssueKind.StatementUnknownRefund, issue.Kind);
        Assert.Equal(RefundReconciliationSeverity.Critical, issue.Severity);
        Assert.Null(issue.RefundId);
        Assert.Contains("GW-LA", issue.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Lenh_that_bai_khop_qua_nhat_ky_khong_bao_sai()
    {
        await using var db = TaoDb();
        var payment = TaoPayment();
        var khoan = TaoRefund(payment, RefundStatus.Pending, createdAt: BayGio.AddHours(-1));
        await LuuAsync(db, payment, khoan);

        // Lượt thất bại vẫn mang mã phía cổng (cổng tạo lệnh rồi mới từ chối). Đối chiếu phải nạp cả
        // mã trong nhật ký, nếu không lệnh đó bị báo "hệ thống không có khoản hoàn tương ứng".
        await ThemLogAsync(db, khoan, RefundLogResult.Failed, "GW-F");

        var baoCao = await TaoService(db, SaoKeGia.Co(
            Dong("GW-F", SoTien, RefundStatementEntryStatus.Failed))).RunAsync(BayGio);

        Assert.True(baoCao.StatementAvailable);
        Assert.Empty(baoCao.Issues);
    }

    [Fact]
    public async Task Sao_ke_ghi_thanh_cong_nhung_he_thong_con_pending_bi_bao()
    {
        await using var db = TaoDb();
        var payment = TaoPayment();
        var khoan = TaoRefund(
            payment,
            RefundStatus.Pending,
            createdAt: BayGio.AddHours(-2),
            // Mã nằm trên dòng Refunds là đúng nhánh "cổng trả lời nhưng lượt ghi thua" — dù bất biến
            // nội bộ nói Pending không được có mã, phép kiểm sao kê vẫn phải khớp được sang khoản này.
            // Ở đây cố ý để trống và khớp qua nhật ký.
            gatewayTransactionId: null);
        await LuuAsync(db, payment, khoan);
        await ThemLogAsync(db, khoan, RefundLogResult.Unresolved, "GW-DANG-BAY");

        var baoCao = await TaoService(db, SaoKeGia.Co(
            Dong("GW-DANG-BAY", SoTien, RefundStatementEntryStatus.Succeeded))).RunAsync(BayGio);

        // Bằng chứng "tiền đã đi" đến từ CỔNG chứ không từ nhật ký nội bộ — vẫn là cảnh báo đỏ:
        // gọi lại khoản này là hoàn hai lần.
        var issue = Assert.Single(baoCao.Issues);
        Assert.Equal(RefundReconciliationIssueKind.StatementStatusMismatch, issue.Kind);
        Assert.Equal(RefundReconciliationSeverity.Critical, issue.Severity);
        Assert.Equal(khoan.Id, issue.RefundId);
    }

    // ── Hạ tầng dùng chung của lớp test ─────────────────────────────────────────────────────────

    private AppDbContext TaoDb()
        => new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(_tenCsdl)
            .Options);

    private static RefundReconciliationService TaoService(
        AppDbContext db,
        IRefundStatementSource? nguon = null)
        => new(
            db,
            nguon ?? new UnavailableRefundStatementSource(),
            NullLogger<RefundReconciliationService>.Instance);

    private static Payment TaoPayment(
        decimal amount = SoTien,
        PaymentStatus status = PaymentStatus.Success,
        Guid? userId = null)
        => new()
        {
            PaymentCode = $"PM-{Guid.NewGuid():N}"[..11],
            MethodCode = "momo",
            Amount = amount,
            Status = status,
            UserId = userId ?? Guid.NewGuid(),
            TripId = Guid.NewGuid(),
        };

    /// <summary>
    /// Khoản hoàn "đúng chuẩn" của nhánh <see cref="RefundSource.TicketCancellation"/>: có yêu cầu
    /// huỷ, có vé, giao dịch gốc Success, cùng chủ. Từng bài chỉ lệch ĐÚNG một thứ so với khuôn này,
    /// nhờ vậy điểm lệch nhận được là do nguyên nhân bài đó dựng.
    /// </summary>
    private static Refund TaoRefund(
        Payment payment,
        RefundStatus status,
        RefundSource source = RefundSource.TicketCancellation,
        decimal? amount = null,
        DateTime? createdAt = null,
        DateTime? processedAt = null,
        string? gatewayTransactionId = null,
        string? failReason = null,
        DateTime? nextRetryAt = null,
        bool cancellationHopLe = true,
        bool ticketHopLe = true,
        string refundCode = "RF-abcdef12")
        => new()
        {
            RefundCode = refundCode,
            Source = source,
            PaymentId = payment.Id,
            CancellationRequestId = source == RefundSource.TicketCancellation && cancellationHopLe
                ? Guid.NewGuid()
                : null,
            TicketId = source == RefundSource.TicketCancellation && ticketHopLe
                ? Guid.NewGuid()
                : null,
            UserId = payment.UserId,
            Amount = amount ?? SoTien,
            Status = status,
            GatewayTransactionId = gatewayTransactionId,
            FailReason = failReason,
            NextRetryAt = nextRetryAt,
            ProcessedAt = processedAt,
            CreatedAt = createdAt ?? BayGio.AddHours(-1),
        };

    private static async Task LuuAsync(AppDbContext db, Payment payment, params Refund[] refunds)
    {
        db.Payments.Add(payment);
        db.Refunds.AddRange(refunds);
        await db.SaveChangesAsync();
    }

    private static async Task ThemLogAsync(
        AppDbContext db,
        Refund refund,
        RefundLogResult result,
        string? gatewayTransactionId = null,
        DateTime? createdAt = null)
    {
        db.RefundLogs.Add(new RefundLog
        {
            RefundId = refund.Id,
            Result = result,
            Trigger = RefundLogTrigger.Initial,
            GatewayTransactionId = gatewayTransactionId,
            Message = "Cổng trả lời trong test",
            CreatedAt = createdAt ?? BayGio.AddHours(-2),
        });
        await db.SaveChangesAsync();
    }

    private static RefundStatementEntry Dong(
        string gatewayTransactionId,
        decimal amount,
        RefundStatementEntryStatus status = RefundStatementEntryStatus.Succeeded)
        => new()
        {
            GatewayTransactionId = gatewayTransactionId,
            Amount = amount,
            Status = status,
        };

    /// <summary>
    /// Nguồn sao kê giả. <see cref="Co"/> trả một sao kê CÓ THẬT (kể cả rỗng — đó là khẳng định của
    /// cổng), còn <see cref="Hong"/> ném lỗi để service phải hạ xuống "không khả dụng". Hai câu trả
    /// lời này khác nhau và không được lẫn, nên chúng là hai hàm factory riêng.
    /// </summary>
    private sealed class SaoKeGia : IRefundStatementSource
    {
        private readonly RefundStatement? _saoKe;

        private readonly Exception? _loi;

        private SaoKeGia(RefundStatement? saoKe, Exception? loi)
        {
            _saoKe = saoKe;
            _loi = loi;
        }

        public DateTime? FromUtc { get; private set; }

        public DateTime? ToUtc { get; private set; }

        public static SaoKeGia Co(params RefundStatementEntry[] entries)
            => new(new RefundStatement { Entries = entries }, null);

        public static SaoKeGia Hong()
            => new(null, new InvalidOperationException("Cổng không trả lời"));

        public Task<RefundStatement?> GetStatementAsync(
            DateTime fromUtc,
            DateTime toUtc,
            CancellationToken cancellationToken = default)
        {
            FromUtc = fromUtc;
            ToUtc = toUtc;

            if (_loi is not null)
            {
                throw _loi;
            }

            return Task.FromResult(_saoKe);
        }
    }
}
