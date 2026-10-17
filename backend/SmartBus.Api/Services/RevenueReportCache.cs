using System.Collections.Concurrent;
using SmartBus.Api.Dtos.Reports;

namespace SmartBus.Api.Services;

/// <summary>
/// Đệm kết quả báo cáo doanh thu trong RAM — task *"Cache kết quả báo cáo theo kỳ để tăng tốc"*
/// (US 19 "Doanh thu", Sprint 5 dòng 43 — Vàng Thị Dăm).
///
/// Đăng ký Singleton ở Program.cs vì kết quả phải sống qua nhiều request — cùng lối
/// <see cref="TripSearchResultCache"/>. Dữ liệu nằm trong RAM nên chưa phù hợp khi chạy nhiều
/// instance; lúc đó chuyển sang Redis/CSDL. Chưa có cơ chế huỷ theo sự kiện: một giao dịch
/// Success vừa ghi sẽ xuất hiện ở báo cáo sau tối đa <see cref="DefaultTtl"/>.
///
/// Hai con số của chính sách, đổi ở đúng một chỗ này:
/// <list type="bullet">
/// <item><see cref="DefaultTtl"/> = 60 giây: hết hạn TUYỆT ĐỐI (không trượt theo lượt hỏi), nên
/// một kết quả đã đệm không bao giờ cũ quá 60 giây. Cùng con số với đệm tìm chuyến — báo cáo là
/// số liệu đã thu, không cần tươi tới từng giây, nhưng cũng không nên đứng yên lâu hơn một phút
/// khi quản lý vừa nhận thanh toán.</item>
/// <item><see cref="IRevenueReportCache.MaxEntries"/> = 200 khoá: chạm trần thì dọn khoá hết hạn;
/// vẫn đầy thì thôi không nhận thêm, xem ghi chú ở <see cref="IRevenueReportCache.MaxEntries"/>.</item>
/// </list>
///
/// Khác <see cref="TripSearchResultCache"/>: KHÔNG có ngưỡng "phổ biến". Endpoint báo cáo đã có
/// phân quyền Admin/Quản lý và mỗi tổ hợp tham số là một lượt xem có chủ đích; lưu ngay từ lượt
/// đầu để dashboard đổi bộ lọc rồi quay lại vẫn được hưởng đệm.
/// </summary>
public sealed class RevenueReportCache : IRevenueReportCache
{
    /// <summary>
    /// Thời gian sống của một kết quả đã đệm — cũng chính là giới hạn "dữ liệu cũ" đã ghi trong
    /// docs/48 §3. Lớp test dựng trực tiếp với TTL ngắn/0 mà không phải chờ đồng hồ.
    /// </summary>
    public static readonly TimeSpan DefaultTtl = TimeSpan.FromSeconds(60);

    private readonly ConcurrentDictionary<string, Entry> _entries = new();

    /// <summary>
    /// Cửa vào — chỉ khoá lúc TẠO một khoá mới. Có nó thì trần
    /// <see cref="IRevenueReportCache.MaxEntries"/> là trần thật, chứ không phải "gần đúng khi
    /// nhiều luồng cùng lúc cùng vượt qua phép kiểm tra số lượng".
    /// </summary>
    private readonly object _admissionGate = new();

    private readonly TimeSpan _ttl;

    private long _hits;

    private long _misses;

    /// <param name="ttl">
    /// Thời gian sống, mặc định <see cref="DefaultTtl"/>. Có tham số này để lớp test dựng trực
    /// tiếp với TTL ngắn (hoặc <see cref="TimeSpan.Zero"/> = hết hạn ngay) mà không phải chờ
    /// đồng hồ — <c>TestAppFactory</c> không có chỗ cấu hình tham số cho service.
    /// </param>
    public RevenueReportCache(TimeSpan? ttl = null) => _ttl = ttl ?? DefaultTtl;

    public int Count => _entries.Count;

    public long Hits => Interlocked.Read(ref _hits);

    public long Misses => Interlocked.Read(ref _misses);

    public RevenueReportResponse? Lookup(string key)
    {
        var now = DateTimeOffset.UtcNow;

        if (!_entries.TryGetValue(key, out var entry))
        {
            Interlocked.Increment(ref _misses);
            return null;
        }

        lock (entry)
        {
            if (now >= entry.ExpiresAt)
            {
                // Chỉ xoá đúng entry đã hết hạn — một luồng khác có thể vừa thay nó bằng entry mới.
                _entries.TryRemove(new KeyValuePair<string, Entry>(key, entry));
                Interlocked.Increment(ref _misses);
                return null;
            }

            Interlocked.Increment(ref _hits);
            return entry.Report;
        }
    }

    public void Store(string key, RevenueReportResponse report)
    {
        var now = DateTimeOffset.UtcNow;

        if (_entries.TryGetValue(key, out var entry))
        {
            lock (entry)
            {
                entry.Report = report;
                entry.ExpiresAt = now + _ttl;
            }

            return;
        }

        lock (_admissionGate)
        {
            if (_entries.TryGetValue(key, out entry))
            {
                lock (entry)
                {
                    entry.Report = report;
                    entry.ExpiresAt = now + _ttl;
                }

                return;
            }

            if (_entries.Count >= IRevenueReportCache.MaxEntries)
            {
                SweepExpired(now);

                if (_entries.Count >= IRevenueReportCache.MaxEntries)
                {
                    return;
                }
            }

            _entries[key] = new Entry(now + _ttl, report);
        }
    }

    /// <summary>Xoá các khoá đã hết hạn để chỗ trở lại — cùng lối <c>RateLimitService.SweepIfNeeded</c>.</summary>
    private void SweepExpired(DateTimeOffset now)
    {
        foreach (var pair in _entries)
        {
            lock (pair.Value)
            {
                if (now >= pair.Value.ExpiresAt)
                {
                    _entries.TryRemove(pair);
                }
            }
        }
    }

    private sealed class Entry
    {
        public Entry(DateTimeOffset expiresAt, RevenueReportResponse report)
        {
            ExpiresAt = expiresAt;
            Report = report;
        }

        public DateTimeOffset ExpiresAt { get; set; }

        public RevenueReportResponse Report { get; set; }
    }
}
