using System.Collections.Concurrent;
using SmartBus.Api.Dtos.Trips;

namespace SmartBus.Api.Services;

/// <summary>
/// Đệm kết quả tìm chuyến trong RAM — task *"Cache kết quả tìm kiếm tuyến phổ biến để giảm tải DB"*
/// (Nguyễn Duy Kiên).
///
/// Đăng ký Singleton ở Program.cs vì kết quả phải sống qua nhiều request — cùng lối
/// <see cref="RateLimitService"/>. Lưu ý: dữ liệu nằm trong RAM nên chưa phù hợp khi chạy nhiều
/// instance — lúc đó sẽ chuyển sang lưu Redis/CSDL.
///
/// Ba con số của chính sách, đổi ở đúng một chỗ này:
/// <list type="bullet">
/// <item><see cref="DefaultTtl"/> = 60 giây: hết hạn TUYỆT ĐỐI (không trượt theo lượt hỏi), nên một
/// kết quả đã đệm không bao giờ cũ quá 60 giây. Hết hạn là quên cả kết quả lẫn số lượt đã đếm —
/// giữ số lượt thì một khoá cũ nóng sẽ được nhận lại ngay mà không phải qua ngưỡng phổ biến.</item>
/// <item><see cref="PopularityThreshold"/> = 2 lượt: khoá phải được hỏi LẶP LẠI mới chiếm chỗ trong
/// đệm — phần thực thi chữ "tuyến phổ biến". Endpoint là công khai (ai cũng đặt được khoá), nên
/// ngưỡng này cũng chặn một cơn bão khoá ngẫu nhiên (mỗi khoá một lần) lấp đầy đệm.</item>
/// <item><see cref="ITripSearchResultCache.MaxEntries"/> = 200 khoá: chạm trần thì dọn khoá hết hạn;
/// vẫn đầy thì thôi không nhận thêm, xem ghi chú ở <see cref="ITripSearchResultCache.MaxEntries"/>.</item>
/// </list>
/// </summary>
public class TripSearchResultCache : ITripSearchResultCache
{
    /// <summary>
    /// Thời gian sống của một kết quả đã đệm — cũng chính là giới hạn "dữ liệu cũ" đã ghi trong
    /// docs/api-contract.md mục "GET /trips/search". Khi bảng vé/giữ chỗ vào (Sprint 3) và
    /// <c>seatsRemaining</c> bắt đầu đổi theo từng lượt đặt thì con số này phải giảm.
    /// </summary>
    public static readonly TimeSpan DefaultTtl = TimeSpan.FromSeconds(60);

    /// <summary>Số lượt hỏi tối thiểu để một khoá được coi là "phổ biến" và được lưu kết quả.</summary>
    private const int PopularityThreshold = 2;

    private readonly ConcurrentDictionary<string, Entry> _entries = new();

    /// <summary>
    /// Cửa vào — chỉ khoá lúc TẠO một khoá mới (lượt trúng đệm không đi qua đây). Có nó thì trần
    /// <see cref="ITripSearchResultCache.MaxEntries"/> là trần thật, chứ không phải "gần đúng khi
    /// nhiều luồng cùng lúc cùng vượt qua phép kiểm tra số lượng".
    /// </summary>
    private readonly object _admissionGate = new();

    private readonly TimeSpan _ttl;

    private long _hits;

    private long _misses;

    /// <param name="ttl">
    /// Thời gian sống, mặc định <see cref="DefaultTtl"/>. Có tham số này để lớp test dựng trực tiếp
    /// với TTL ngắn (hoặc <see cref="TimeSpan.Zero"/> = hết hạn ngay) mà không phải chờ đồng hồ —
    /// <c>TestAppFactory</c> không có chỗ cấu hình tham số cho service.
    /// </param>
    public TripSearchResultCache(TimeSpan? ttl = null) => _ttl = ttl ?? DefaultTtl;

    public int Count => _entries.Count;

    public long Hits => Interlocked.Read(ref _hits);

    public long Misses => Interlocked.Read(ref _misses);

    public IReadOnlyList<TripSearchResult>? Lookup(string key)
    {
        var now = DateTimeOffset.UtcNow;
        var entry = GetOrAdd(key, now);

        if (entry is null)
        {
            // Đệm đầy và không dọn ra chỗ: lượt này đi thẳng xuống CSDL, không đếm được lượt hỏi.
            Interlocked.Increment(ref _misses);
            return null;
        }

        IReadOnlyList<TripSearchResult>? cached;
        lock (entry)
        {
            if (now >= entry.ExpiresAt)
            {
                entry.ExpiresAt = now + _ttl;
                entry.SeenCount = 0;
                entry.Results = null;
            }

            entry.SeenCount++;
            cached = entry.Results;
        }

        if (cached is null)
        {
            Interlocked.Increment(ref _misses);
            return null;
        }

        Interlocked.Increment(ref _hits);
        return cached;
    }

    public void Store(string key, IReadOnlyList<TripSearchResult> results)
    {
        var now = DateTimeOffset.UtcNow;
        var entry = GetOrAdd(key, now);

        if (entry is null)
        {
            return;
        }

        lock (entry)
        {
            // Chưa đủ ngưỡng phổ biến, hoặc khoá vừa hết hạn ở một lượt Lookup khác → chưa lưu.
            if (now >= entry.ExpiresAt || entry.SeenCount < PopularityThreshold)
            {
                return;
            }

            entry.Results = results;
        }
    }

    /// <summary>
    /// Lấy entry của khoá, tạo mới nếu chưa có. Trả <see langword="null"/> khi đệm đã chạm trần và
    /// không dọn ra chỗ — người gọi coi như trượt đệm.
    /// </summary>
    private Entry? GetOrAdd(string key, DateTimeOffset now)
    {
        if (_entries.TryGetValue(key, out var existing))
        {
            return existing;
        }

        lock (_admissionGate)
        {
            if (_entries.TryGetValue(key, out existing))
            {
                return existing;
            }

            if (_entries.Count >= ITripSearchResultCache.MaxEntries)
            {
                SweepExpired(now);

                if (_entries.Count >= ITripSearchResultCache.MaxEntries)
                {
                    return null;
                }
            }

            var entry = new Entry(now + _ttl);
            _entries[key] = entry;

            return entry;
        }
    }

    /// <summary>Xoá các khoá đã hết hạn để chỗ trở lại — cùng lối <c>RateLimitService.SweepIfNeeded</c>.</summary>
    private void SweepExpired(DateTimeOffset now)
    {
        foreach (var pair in _entries)
        {
            if (now >= pair.Value.ExpiresAt)
            {
                _entries.TryRemove(pair);
            }
        }
    }

    private sealed class Entry
    {
        public Entry(DateTimeOffset expiresAt) => ExpiresAt = expiresAt;

        public DateTimeOffset ExpiresAt { get; set; }

        /// <summary>Số lượt hỏi trong cửa sổ hiện tại — chưa đủ ngưỡng thì chưa lưu kết quả.</summary>
        public int SeenCount { get; set; }

        public IReadOnlyList<TripSearchResult>? Results { get; set; }
    }
}
