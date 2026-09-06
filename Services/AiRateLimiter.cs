using System.Collections.Concurrent;

namespace FridgeManager.Services;

public sealed class AiRateLimiter
{
    private readonly ConcurrentDictionary<string, Queue<DateTime>> _windows = new(StringComparer.Ordinal);
    private readonly TimeProvider _clock;

    public AiRateLimiter(TimeProvider? clock = null)
        => _clock = clock ?? TimeProvider.System;

    public bool TryAcquire(string userId, int limit, TimeSpan window)
    {
        ArgumentException.ThrowIfNullOrEmpty(userId);

        if (limit < 1)
        {
            return false;
        }

        var now = _clock.GetUtcNow().UtcDateTime;
        var cutoff = now - window;
        var queue = _windows.GetOrAdd(userId, static _ => new Queue<DateTime>());
        lock (queue)
        {
            while (queue.Count > 0 && queue.Peek() <= cutoff)
            {
                queue.Dequeue();
            }

            if (queue.Count >= limit)
            {
                return false;
            }

            queue.Enqueue(now);
            return true;
        }
    }
}
