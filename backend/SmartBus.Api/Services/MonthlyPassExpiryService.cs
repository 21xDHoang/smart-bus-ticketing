using Microsoft.EntityFrameworkCore;
using SmartBus.Api.Data;
using SmartBus.Api.Entities;

namespace SmartBus.Api.Services;

/// <summary>
/// Cài đặt <see cref="IMonthlyPassExpiryService"/> — phần ruột của job quét vé tháng hết hạn
/// (US 16, task của Nguyễn Duy Kiên).
///
/// Đọc kèm <see cref="MonthlyPassStatus"/> trước khi sửa: Expired là trạng thái LƯU, luôn có độ trễ
/// so với thực tế, và <b>không phải</b> nguồn sự thật về hiệu lực của vé. Job ở đây chỉ làm rẻ việc
/// lọc/thống kê — mọi truy vấn hỏi "vé này còn dùng được không" vẫn phải so cặp ValidFrom/ValidTo.
/// </summary>
public class MonthlyPassExpiryService : IMonthlyPassExpiryService
{
    private readonly AppDbContext _db;

    public MonthlyPassExpiryService(AppDbContext db) => _db = db;

    public async Task<int> ExpireDuePassesAsync(
        DateTime now,
        CancellationToken cancellationToken = default)
    {
        // "Đến hạn lật" = còn Active mà ValidTo đã qua.
        //
        // So sánh là ValidTo < now (NGHIÊM NGẶT), không phải <=: hợp đồng API ghi rõ khoảng hiệu lực
        // "tính cả hai mốc", nên đúng lúc ValidTo vé vẫn còn hiệu lực — lật ở chính mốc đó là thu hồi
        // sớm một vé khách đã trả tiền.
        //
        // Cố ý KHÔNG lọc thêm theo ValidFrom. Vé đăng ký trước cho kỳ sau (ValidFrom > now) đã tự
        // được loại vì ValidTo của nó còn ở tương lai, nên điều kiện thêm không lọc được gì; nó chỉ
        // giấu đi một dòng dữ liệu hỏng (ValidTo < now < ValidFrom) đáng ra phải nhìn thấy.
        var due = await _db.MonthlyPasses
            .Where(p => p.Status == MonthlyPassStatus.Active && p.ValidTo < now)
            .ToListAsync(cancellationToken);

        if (due.Count == 0)
        {
            return 0;
        }

        foreach (var pass in due)
        {
            pass.Status = MonthlyPassStatus.Expired;

            // A4: vé tháng có sửa dữ liệu nên có UpdatedAt. Ghi lại chính mốc đã dùng để xét hạn chứ
            // không gọi DateTime.UtcNow lần nữa — hai mốc lệch nhau vài mili giây trong cùng một lượt
            // quét là thứ không giải thích được lúc tra vết.
            pass.UpdatedAt = now;
        }

        // Nạp entity rồi SaveChanges thay vì ExecuteUpdateAsync — quyết định có chủ ý, ba lý do:
        //   - ExecuteUpdate không kiểm chứng được bằng provider InMemory của bộ test (cả bộ test
        //     đang chạy trên đó), nên nhánh quan trọng nhất của job sẽ không có test nào phủ.
        //   - Cần đúng số dòng đã lật để job ghi log.
        //   - An toàn khi có nhiều bản app chạy song song: thứ duy nhất ghi vào Status của một vé
        //     ĐANG TỒN TẠI là job này (gia hạn ghi thêm dòng MỚI chứ không sửa dòng cũ — xem
        //     MonthlyPass), và hai bản cùng lật một vé thì ghi cùng một giá trị, không mất mát gì.
        //
        // Chi phí nạp entity không đáng kể: mỗi lượt chỉ có những vé vừa hết hạn kể từ lượt trước.
        await _db.SaveChangesAsync(cancellationToken);

        return due.Count;
    }
}
