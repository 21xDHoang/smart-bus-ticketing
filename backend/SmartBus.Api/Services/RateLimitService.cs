using System.Collections.Concurrent;

namespace SmartBus.Api.Services;

/// <summary>Hạn mức gọi API theo khoá (ví dụ địa chỉ IP) trong một cửa sổ thời gian.</summary>
public interface IRateLimitService
{
    /// <summary>
    /// Ghi nhận một lần gọi của <paramref name="key"/> và trả <see langword="true"/> nếu còn
    /// trong hạn mức. Trả <see langword="false"/> khi đã vượt quá — người gọi phải chặn request.
    /// </summary>
    /// <param name="key">Khoá định danh người gọi, thường là địa chỉ IP.</param>
    /// <param name="maxRequests">Số lần gọi tối đa trong một cửa sổ.</param>
    /// <param name="window">Độ dài cửa sổ, mặc định 1 phút.</param>
    bool TryAcquire(string key, int maxRequests = 5, TimeSpan? window = null);
}

/// <summary>
/// Hạn mức kiểu "cửa sổ cố định" lưu trong bộ nhớ, dùng để chống brute-force
/// và spam API công khai (đăng ký tài khoản hàng loạt).
/// Task rate-limit chống brute-force — story 22, Trần Trung Hiếu.
///
/// Đăng ký Singleton ở Program.cs vì bộ đếm phải sống qua nhiều request.
/// Lưu ý: dữ liệu nằm trong RAM nên chưa phù hợp khi chạy nhiều instance —
/// lúc đó sẽ chuyển sang lưu Redis/CSDL.
/// </summary>
public class RateLimitService : IRateLimitService
{
    private static readonly TimeSpan DefaultWindow = TimeSpan.FromMinutes(1);

    /// <summary>Quá số mục này thì dọn một lượt các cửa sổ đã hết hạn, tránh phình RAM vô hạn.</summary>
    private const int SweepThreshold = 1000;

    private readonly ConcurrentDictionary<string, FixedWindow> _windows = new();

    public bool TryAcquire(string key, int maxRequests = 5, TimeSpan? window = null)
    {
        var windowSpan = window ?? DefaultWindow;
        var now = DateTimeOffset.UtcNow;

        var entry = _windows.GetOrAdd(key, static _ => new FixedWindow());

        // Chỉ khoá theo từng key nên các IP khác nhau không chờ nhau.
        bool allowed;
        lock (entry)
        {
            if (now - entry.Start >= windowSpan)
            {
                entry.Start = now;
                entry.Count = 0;
            }

            entry.Count++;
            allowed = entry.Count <= maxRequests;
        }

        SweepIfNeeded(now, windowSpan);

        return allowed;
    }

    /// <summary>Thỉnh thoảng xoá các cửa sổ không ai dùng nữa để bộ nhớ không tăng mãi.</summary>
    private void SweepIfNeeded(DateTimeOffset now, TimeSpan windowSpan)
    {
        if (_windows.Count <= SweepThreshold)
        {
            return;
        }

        foreach (var pair in _windows)
        {
            if (now - pair.Value.Start >= windowSpan)
            {
                _windows.TryRemove(pair);
            }
        }
    }

    private sealed class FixedWindow
    {
        public DateTimeOffset Start { get; set; } = DateTimeOffset.UtcNow;

        public int Count { get; set; }
    }
}
