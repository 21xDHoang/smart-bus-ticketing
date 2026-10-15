namespace SmartBus.Api.Entities;

/// <summary>
/// Bảng <c>Refunds</c> — một KHOẢN HOÀN TIỀN: ý định trả lại tiền cho khách, cộng trạng thái hiện
/// tại của ý định đó (US 8 — *"Là hệ thống, tôi muốn tự động xử lý hoàn tiền theo quy định khi giao
/// dịch thanh toán thất bại hoặc vé bị hủy hợp lệ"*). Task Sprint 4 *"Migrate bảng Refunds,
/// RefundLogs"* (Vàng Thị Dăm, dòng 38).
///
/// Luồng đầy đủ, theo bảng phân công Sprint 4: service quyết định hoàn bao nhiêu và tạo dòng ở đây
/// (Nguyễn Duy Kiên, dòng 39) → gọi cổng để trả tiền (Trần Trung Hiếu, dòng 40) → khoản thất bại
/// được thử lại theo lịch (Phùng Duy Hoàng, dòng 41) → khách tra trạng thái (dòng 42) → đối soát
/// hằng ngày với sao kê cổng (Kiên, dòng 43) → màn hành khách (Dương Thị Hạnh, dòng 44) và màn quản
/// trị (Nguyễn Đình Băng, dòng 46–47).
///
/// 🔴 <b>Vì sao là bảng RIÊNG, tách hẳn khỏi <c>CancellationRequests</c>:</b> bảng yêu cầu huỷ trả
/// lời *"có cho huỷ không"*, bảng này trả lời *"đã trả được tiền chưa"*. Hai câu hỏi khác nhau, và
/// chúng có thể trả lời lệch nhau trong nhiều ngày — quản lý duyệt huỷ xong mà cổng hoàn tiền lỗi
/// thì yêu cầu đã <see cref="CancellationRequestStatus.Approved"/> trong khi khoản hoàn còn
/// <see cref="RefundStatus.Pending"/>. Gộp hai thứ vào một cột trạng thái là bịt mất đúng khoảng
/// lệch đó. Cùng lối lập luận đã ghi ở doc-comment <see cref="CancellationRequestStatus"/> (mục "vì
/// sao KHÔNG có trạng thái đã hoàn tiền"). Lý do đầy đủ: <c>docs/40-csdl-hoan-tien.md</c> §2.1.
///
/// Bảng CÓ bị sửa (trạng thái lật, thử lại), nên A4 buộc có <see cref="UpdatedAt"/>. Nhật ký từng
/// lượt gọi cổng nằm ở bảng anh em <see cref="RefundLog"/> (cùng file cấu hình
/// AppDbContext.Refund.cs) — bảng này chỉ giữ trạng thái cuối, cùng lối
/// <see cref="SeatHold"/> / <see cref="SeatHoldLog"/>.
/// </summary>
public class Refund
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Mã khoản hoàn nội bộ (dạng "RF-xxxxxxxx") — DUY NHẤT toàn hệ thống, có chỉ mục unique.
    ///
    /// Vai trò: khoá chống trùng lúc tạo, và là thứ để tra/đối soát bằng mắt — task dòng 42 (*"API
    /// tra cứu trạng thái hoàn tiền của giao dịch"*) cùng màn Admin (dòng 46) tra theo mã này, và
    /// bước đối soát hằng ngày (dòng 43) đối chiếu nó với sao kê cổng.
    ///
    /// Độ dài 64 là mức chừa rộng rãi, cùng khuôn <see cref="Payment.PaymentCode"/> — chuỗi thật chỉ
    /// khoảng 11 ký tự, phần dư để dành cho mã tra cứu phía cổng nếu sau này cần nhét vào.
    /// </summary>
    public string RefundCode { get; set; } = string.Empty;

    /// <summary>
    /// Vì sao có khoản hoàn này — xem <see cref="RefundSource"/>. Bắt buộc, và là cột mà bộ lọc màn
    /// Admin (dòng 46–47) cùng màn hành khách (dòng 44) lọc theo.
    ///
    /// ⚠️ Giá trị này gắn với một bất biến ở tầng nghiệp vụ:
    /// <see cref="CancellationRequestId"/> khác null <b>⟺</b> <see cref="Source"/> là
    /// <see cref="RefundSource.TicketCancellation"/>. Không diễn đạt được bằng CHECK constraint vì
    /// <c>Source</c> lưu dạng chuỗi (A3) — xem <c>docs/40-csdl-hoan-tien.md</c> §4.3.
    /// </summary>
    public RefundSource Source { get; set; }

    /// <summary>
    /// Giao dịch gốc đã thu tiền — khoá ngoại BẮT BUỘC, và là cột chống hoàn trùng.
    ///
    /// 🔴 Vì sao NOT NULL dù nguồn gốc là gì: tiền chỉ hoàn được khi đã có một giao dịch thu tiền
    /// trước đó, kể cả ở nhánh <see cref="RefundSource.PaymentFailure"/> — nhánh đó là *"cổng đã giữ
    /// tiền nhưng giao dịch báo hỏng"*, chứ không phải "chưa từng có giao dịch". Một khoản hoàn
    /// không gắn giao dịch nào là khoản hoàn không kiểm chứng được, tức là đúng thứ bước đối soát
    /// (dòng 43) không thể làm gì với nó.
    ///
    /// Cũng là cột mà chỉ mục duy nhất điều kiện <c>IX_Refunds_PaymentId_Active</c> canh: một giao
    /// dịch chỉ được có MỘT khoản hoàn chưa thất bại. Lý do ở AppDbContext.Refund.cs và
    /// <c>docs/40-csdl-hoan-tien.md</c> §4.1.
    /// </summary>
    public Guid PaymentId { get; set; }

    public Payment? Payment { get; set; }

    /// <summary>
    /// Yêu cầu huỷ vé đã duyệt sinh ra khoản hoàn này — CHỈ có ở nhánh
    /// <see cref="RefundSource.TicketCancellation"/>, null ở nhánh
    /// <see cref="RefundSource.PaymentFailure"/>.
    ///
    /// Vì sao trỏ thẳng vào yêu cầu huỷ chứ không chỉ trỏ vé: câu hỏi *"vì sao khoản này chỉ hoàn
    /// 7.200 trong khi vé 12.000"* trả lời được ngay từ dòng yêu cầu — nó giữ
    /// <see cref="CancellationRequest.CancellationFee"/> đã chụp lúc khách gửi. Trỏ mỗi vé thì phải
    /// đi tìm lại yêu cầu nào đã được duyệt, mà một vé có thể có nhiều yêu cầu theo thời gian.
    /// </summary>
    public Guid? CancellationRequestId { get; set; }

    public CancellationRequest? CancellationRequest { get; set; }

    /// <summary>
    /// Vé bị hoàn tiền. <b>Nullable</b> vì nhánh <see cref="RefundSource.PaymentFailure"/> thường
    /// không có vé nào: vé chỉ được phát hành sau khi tiền về thành công
    /// (docs/api-contract.md, mục "Phát hành vé sau khi thanh toán"), nên giao dịch hỏng thì chưa có
    /// vé. Bắt NOT NULL sẽ buộc service phải bỏ luôn khoản hoàn của đúng nhánh mà US 8 nêu ra đầu
    /// tiên.
    ///
    /// ⚠️ Đây là <b>cột hiển thị</b>, không phải khoá nghiệp vụ: màn Admin (dòng 46) và màn hành
    /// khách (dòng 44) hiện mã vé / tuyến / giờ chạy cạnh số tiền, còn khoá tra cứu là
    /// <see cref="RefundCode"/> và <see cref="PaymentId"/>.
    /// </summary>
    public Guid? TicketId { get; set; }

    public Ticket? Ticket { get; set; }

    /// <summary>
    /// Người NHẬN LẠI tiền — chủ giao dịch.
    ///
    /// Cố ý chép lại từ <see cref="Payment.UserId"/> chứ không suy ra qua khoá ngoại
    /// <see cref="Payment"/>: màn "Hoàn tiền của tôi" (dòng 44) lọc thẳng theo cột này, và đây là
    /// đúng lối <see cref="SeatHoldLog.UserId"/> đã đi trước — bảng nghiệp vụ về tiền ghi lại người
    /// liên quan tại thời điểm sự kiện để đứng đọc được một mình. Câu hỏi *"khoản hoàn này của ai"*
    /// là câu hỏi phân quyền, không phải câu hỏi suy diễn.
    /// </summary>
    public Guid UserId { get; set; }

    public User? User { get; set; }

    /// <summary>
    /// Số tiền hoàn, numeric(12,2) — A3: không dùng float/double cho tiền. Cùng đơn vị và cùng độ
    /// rộng với <see cref="Ticket.Price"/>, <see cref="Payment.Amount"/>,
    /// <see cref="CancellationRequest.CancellationFee"/>.
    ///
    /// 🔴 Cố ý LƯU LẠI con số chứ không tính lại lúc đọc — và đây KHÔNG phải chỗ mâu thuẫn với
    /// doc-comment <see cref="CancellationRequest.CancellationFee"/> (ở đó ghi số tiền <b>phải</b>
    /// hoàn là suy ra được nên không có cột). Hai con số khác nhau về bản chất:
    ///
    /// | | Là gì | Suy ra được không |
    /// |---|---|---|
    /// | <c>Ticket.Price - CancellationFee</c> | số tiền <b>phải</b> hoàn theo quy định hiện hành | được — vẫn là công thức suy diễn, không có cột |
    /// | <c>Refund.Amount</c> | số tiền hệ thống <b>đã thực sự gửi lên cổng</b> | <b>không</b> — xem bên dưới |
    ///
    /// Nhánh <see cref="RefundSource.PaymentFailure"/> không có vé nên không có
    /// <c>Ticket.Price</c> nào để trừ: số tiền hoàn ở nhánh đó đến từ số tiền cổng đã giữ, không
    /// phải từ một công thức trên vé. Và ở cả hai nhánh, bước đối soát hằng ngày (dòng 43) phải so
    /// <b>đúng con số đã gửi đi</b> với sao kê cổng — suy lại rồi đem so là so một con số được tính
    /// bằng quy định <i>hôm nay</i> với một giao dịch đã gửi bằng quy định <i>hôm đó</i>, và mọi lần
    /// nhà xe sửa quy định ở giữa sẽ làm phép đối soát báo lệch giả.
    /// </summary>
    public decimal Amount { get; set; }

    /// <summary>
    /// Trạng thái hiện tại của khoản hoàn — xem <see cref="RefundStatus"/>. Dòng mới luôn
    /// <c>Pending</c>.
    ///
    /// ⚠️ Cột này chỉ giữ trạng thái CUỐI. Việc khoản này đã bị gọi cổng mấy lần, mỗi lần cổng trả
    /// lời gì, nằm ở bảng <see cref="RefundLog"/> — và số lần thử lại là phép ĐẾM trên bảng đó, cố ý
    /// không có cột đếm ở đây (lý do ở <c>docs/40-csdl-hoan-tien.md</c> §2.4).
    /// </summary>
    public RefundStatus Status { get; set; } = RefundStatus.Pending;

    /// <summary>
    /// Mã giao dịch phía cổng của lượt hoàn <b>thành công</b> (MoMo transId / VNPay TransactionNo /
    /// ZaloPay zp_trans_id) — chỉ lượt lật sang <see cref="RefundStatus.Succeeded"/> mới ghi được.
    /// Là thứ đối chiếu với sao kê cổng ở bước đối soát (dòng 43).
    ///
    /// ⚠️ Cố ý KHÔNG suy ra từ <see cref="RefundLog.GatewayTransactionId"/> của lượt cuối: một lượt
    /// gọi <b>thất bại vẫn có thể</b> mang mã giao dịch phía cổng (cổng đã tạo lệnh hoàn rồi mới từ
    /// chối), nên "mã của lượt cuối" và "mã của lượt đã trả tiền" là hai giá trị khác nhau — và cặp
    /// lệch nhau đó chính là một trong những ca "lệch số liệu" mà dòng 43 phải bắt.
    /// </summary>
    public string? GatewayTransactionId { get; set; }

    /// <summary>
    /// Lý do lượt gọi GẦN NHẤT thất bại — chép từ thông điệp cổng trả về, hiển thị thẳng cho khách
    /// ở màn theo dõi (dòng 44) và cho quản trị ở màn quản lý (dòng 46).
    ///
    /// Vì sao chép lên đây dù đã có ở <see cref="RefundLog.Message"/>: đây là <b>dòng tiêu đề</b> của
    /// trạng thái hiện tại — mọi danh sách đều hiện nó mà không phải join sang bảng nhật ký, cùng
    /// lối <see cref="Payment.Message"/>. Bản đầy đủ của TỪNG lượt vẫn nằm ở bảng log; cột này là
    /// "lý do của lượt cuối", không phải lịch sử.
    ///
    /// ⚠️ Bất biến kèm theo: null khi <see cref="Status"/> là <see cref="RefundStatus.Pending"/>
    /// hoặc <see cref="RefundStatus.Succeeded"/>; chỉ khác null khi <see cref="Status"/> là
    /// <see cref="RefundStatus.Failed"/>. Là bất biến của service — cột <c>varchar(500)</c> cùng độ
    /// dài <see cref="Payment.Message"/>, vì đây đúng là chuỗi cổng trả về.
    /// </summary>
    public string? FailReason { get; set; }

    /// <summary>
    /// Mốc job thử lại theo lịch (dòng 41) sẽ gọi cổng cho khoản này — <c>null</c> nghĩa là khoản
    /// KHÔNG nằm trong lịch.
    ///
    /// 🔴 Vì sao phải là cột riêng chứ không suy ra từ <see cref="Status"/> và
    /// <see cref="CreatedAt"/>: lịch thử lại là <b>chính sách sửa được</b> (giãn cách tăng dần, số
    /// lần tối đa), không phải một hằng số của hệ thống. Suy ra lúc truy vấn nghĩa là chép chính sách
    /// vào câu truy vấn của job — đổi chính sách là phải sửa và triển khai lại job, thay vì chỉ đổi
    /// một dòng dữ liệu. Cùng lối lập luận đã khiến <see cref="CancellationRequest.CancellationFee"/>
    /// và <see cref="TicketValidation.ScannedAt"/> phải là cột chứ không phải phép tính.
    ///
    /// Ba trạng thái của cột, phân biệt được bằng mắt:
    /// - <c>Status = Failed</c> + có mốc → <b>đang chờ tới hạn</b>, job sẽ gọi lại;
    /// - <c>Status = Failed</c> + null → <b>đã hết lịch</b> (hết số lần thử, hoặc khoản cần người
    ///   xử lý). Vẫn thử được bằng tay ở màn Admin (dòng 46) — nút "Thử lại" sinh lượt
    ///   <see cref="RefundLogTrigger.ManualRetry"/>;
    /// - <c>Status</c> là <c>Pending</c> hoặc <c>Succeeded</c> → không nằm trong lịch.
    ///
    /// Chỉ mục <c>(Status, NextRetryAt)</c> ở AppDbContext.Refund.cs là đường quét của job.
    /// </summary>
    public DateTime? NextRetryAt { get; set; }

    /// <summary>
    /// Mốc cổng xác nhận kết quả CUỐI (thành công hoặc thất bại) — khác <see cref="UpdatedAt"/>,
    /// vốn chỉ là "dòng có bị sửa" và cũng nhảy khi Admin bấm thử lại.
    ///
    /// Nullable: khoản còn <see cref="RefundStatus.Pending"/> thì cổng chưa chốt được gì. Cùng lối
    /// tách mốc nghiệp vụ khỏi mốc kỹ thuật đã ghi ở <see cref="CancellationRequest.ReviewedAt"/>.
    /// </summary>
    public DateTime? ProcessedAt { get; set; }

    /// <summary>Thời điểm hệ thống tạo khoản hoàn — màn Admin sắp danh sách mới nhất trước theo cột này.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Khoản hoàn CÓ bị sửa (lật trạng thái, thử lại, đổi lịch) nên A4 buộc có <c>UpdatedAt</c>.
    /// </summary>
    public DateTime? UpdatedAt { get; set; }
}
