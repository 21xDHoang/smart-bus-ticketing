using Microsoft.EntityFrameworkCore;
using SmartBus.Api.Data;
using SmartBus.Api.Entities;

namespace SmartBus.Api.Services;

/// <summary>
/// Cài đặt <see cref="ISeatHoldAbuseService"/> — phần ruột của job canh tài khoản giữ chỗ quá nhiều
/// lần (US 3, task của Vàng Thị Dăm).
///
/// <para>
/// Hai chốt của phép đếm nằm ở hai hằng số dưới đây, và cả hai đều là lựa chọn nghiệp vụ chứ không
/// phải chi tiết kỹ thuật — đổi số là đổi ai bị cảnh báo.
/// </para>
/// </summary>
public class SeatHoldAbuseService : ISeatHoldAbuseService
{
    /// <summary>
    /// Cửa sổ trượt để đếm: 1 giờ.
    ///
    /// Vì sao không phải "từ trước tới giờ": hành vi bình thường của một hành khách cũng tích luỹ
    /// nhiều lượt giữ qua các tháng. Đếm luỹ kế thì tới một lúc mọi khách trung thành đều bị gắn cờ,
    /// và cảnh báo mất hết ý nghĩa. Cái cần bắt là giữ NHIỀU LẦN TRONG THỜI GIAN NGẮN.
    /// </summary>
    public static readonly TimeSpan CuaSo = TimeSpan.FromHours(1);

    /// <summary>
    /// Ngưỡng: đạt <b>5 phiên</b> giữ chỗ trong một cửa sổ là quá nhiều (<c>&gt;=</c>, không phải
    /// <c>&gt;</c>).
    ///
    /// Vì sao đếm PHIÊN chứ không đếm ghế: một khách đặt vé cho cả gia đình chọn 4 ghế trong MỘT
    /// phiên — đó là một lần giữ chỗ, không phải bốn. Đếm theo dòng <c>SeatHolds</c> thì chính khách
    /// mua nhiều vé nhất lại là người dễ bị gắn cờ nhất, tức là cảnh báo bắt nhầm đúng nhóm khách
    /// tốt. "Giữ chỗ quá nhiều lần" hỏi số LẦN, và một lần là một <c>SessionCode</c>.
    ///
    /// Vì sao 5: một khách thật hiếm khi tạo quá 2–3 phiên trong một giờ (chọn ghế, đổi ý, chọn
    /// lại). Mốc 5 đủ rộng để không bắt nhầm người đang cân nhắc, mà vẫn bắt được tài khoản giữ
    /// ghế hàng loạt.
    /// </summary>
    public const int NguongPhienGiu = 5;

    private readonly AppDbContext _db;

    private readonly ILogger<SeatHoldAbuseService> _logger;

    public SeatHoldAbuseService(AppDbContext db, ILogger<SeatHoldAbuseService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<int> ScanAndWarnAsync(
        DateTime now,
        CancellationToken cancellationToken = default)
    {
        var tuMoc = now - CuaSo;

        // Cửa sổ ĐÓNG hai đầu [tuMoc, now]. Cận trên không thừa: nó chặn luôn lượt giữ có CreatedAt
        // nằm sau mốc đang xét (đồng hồ hai máy lệch nhau, hoặc một bản app khác vừa ghi xong), để
        // "số phiên trong cửa sổ" luôn là con số dựng lại được từ chính tham số now.
        //
        // Chỉ nạp hai cột cần dùng chứ không nạp entity: lượt quét này không sửa gì trên SeatHolds,
        // nó chỉ đếm. Cùng lối nạp-rồi-tính-trong-bộ-nhớ của SeatHoldExpiryService, và cùng lý do:
        // phép GroupBy + Distinct().Count() dịch xuống CSDL được nhưng provider InMemory của bộ test
        // không dịch được, nên nhánh quan trọng nhất sẽ thành nhánh không có test nào phủ. Cửa sổ
        // 1 giờ là lượng dữ liệu nhỏ và IX_SeatHolds_UserId_CreatedAt lo phần lọc.
        var luotGiu = await _db.SeatHolds
            .Where(h => h.CreatedAt >= tuMoc && h.CreatedAt <= now)
            .Select(h => new { h.UserId, h.SessionCode })
            .ToListAsync(cancellationToken);

        if (luotGiu.Count == 0)
        {
            return 0;
        }

        var vuotNguong = luotGiu
            .GroupBy(x => x.UserId)
            .Select(nhom => new
            {
                UserId = nhom.Key,
                SoPhien = nhom.Select(x => x.SessionCode).Distinct().Count(),
            })
            .Where(x => x.SoPhien >= NguongPhienGiu)
            .ToList();

        if (vuotNguong.Count == 0)
        {
            return 0;
        }

        // 🔴 Chống ghi trùng — chốt dễ quên nhất của job này.
        //
        // Nhịp quét 5 phút, cửa sổ 1 giờ: một tài khoản vượt ngưỡng sẽ còn nằm trong cửa sổ suốt
        // 12 lượt quét tiếp theo. Không có bước này thì mỗi hành vi sinh 12 dòng AuditLogs giống hệt
        // nhau — đúng loại log rác mà SeatHoldExpiryBackgroundService đã phải tránh, nhưng ở đây
        // nặng hơn vì bảng AuditLogs là màn hình làm việc của Admin, không phải log kỹ thuật.
        //
        // Đã cảnh báo trong cửa sổ này rồi thì thôi. Hệ quả có chủ ý: một tài khoản giữ chỗ điên
        // cuồng suốt ngày sẽ được nhắc lại mỗi giờ một lần — đúng nhịp cần thiết để Admin thấy là
        // "vẫn đang tiếp diễn", mà không ngập trong bản ghi.
        var daCanhBao = await _db.AuditLogs
            .Where(a => a.Action == AuditAction.Warning && a.CreatedAt >= tuMoc && a.CreatedAt <= now)
            .Select(a => a.UserId)
            .ToListAsync(cancellationToken);

        // UserId nullable ở AuditLogs (đăng nhập thất bại với SĐT không tồn tại thì không tra ra
        // tài khoản). Cảnh báo này luôn có người, nhưng bộ lọc phải nói được điều đó với trình biên
        // dịch thay vì ép kiểu mù.
        var daCanhBaoRoi = daCanhBao
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .ToHashSet();

        var canCanhBao = vuotNguong
            .Where(x => !daCanhBaoRoi.Contains(x.UserId))
            .ToList();

        if (canCanhBao.Count == 0)
        {
            return 0;
        }

        foreach (var taiKhoan in canCanhBao)
        {
            _db.AuditLogs.Add(new AuditLog
            {
                Action = AuditAction.Warning,

                // Người bị cảnh báo đứng ở cột UserId chứ không phải ở Target: đây là hành động CỦA
                // tài khoản đó do hệ thống ghi thay, cùng lối một dòng Create có UserId là người bấm
                // nút. Nhờ vậy màn nhật ký lọc theo người dùng vẫn ra được toàn bộ dấu vết của họ.
                UserId = taiKhoan.UserId,

                // "<Tên bảng>:<Id>" — A9. Ở đây trỏ về chính tài khoản bị gắn cờ, không phải một
                // lượt giữ cụ thể: cảnh báo nói về một CHUỖI hành vi, không nói về một dòng SeatHolds.
                Target = $"Users:{taiKhoan.UserId}",

                // Job nền không có request nào để lấy IP. Cột này nullable đúng vì ca đó — mất IP
                // không phải lý do để mất bản ghi.
                IpAddress = null,

                // Ghim đúng mốc đang xét thay vì để initializer gọi DateTime.UtcNow lần nữa: hai mốc
                // lệch nhau vài mili giây trong cùng một lượt quét là thứ không giải thích được lúc
                // tra vết, và nó còn phá luôn phép chống ghi trùng ở lượt sau. Cùng lối
                // SeatHoldExpiryService.
                CreatedAt = now,
            });

            // Số liệu đầy đủ nằm ở đây chứ không ở AuditLogs: bảng nhật ký chỉ có UserId/Action/
            // Target, không có chỗ cho "bao nhiêu phiên". Log ứng dụng có cấu trúc nên vẫn lọc và
            // thống kê được, còn bản ghi AuditLogs là thứ Admin NHÌN THẤY trên màn hình.
            _logger.LogWarning(
                "Tài khoản {UserId} giữ chỗ {SoPhien} phiên trong {CuaSoPhut} phút qua (ngưỡng {Nguong}) — đã ghi cảnh báo vào nhật ký.",
                taiKhoan.UserId,
                taiKhoan.SoPhien,
                (int)CuaSo.TotalMinutes,
                NguongPhienGiu);
        }

        // Một SaveChanges cho cả lượt quét: mọi dòng cảnh báo của lượt này cùng sống hoặc cùng chết.
        // Nếu để mỗi tài khoản tự lưu thì một lượt quét đứt giữa đường sẽ để lại nhật ký ghi một nửa,
        // và lần chạy sau (đã thấy vài dòng Warning) sẽ bỏ sót những tài khoản chưa kịp ghi.
        await _db.SaveChangesAsync(cancellationToken);

        return canCanhBao.Count;
    }
}
