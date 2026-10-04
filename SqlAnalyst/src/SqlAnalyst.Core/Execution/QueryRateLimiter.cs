namespace SqlAnalyst.Core.Execution;

/// <summary>Скользящее окно в одну минуту: не даёт зациклившемуся агенту засыпать базу запросами.</summary>
public sealed class QueryRateLimiter(TimeProvider time, int perMinute)
{
    private readonly Queue<DateTimeOffset> _recent = new();
    private readonly Lock _lock = new();

    public bool TryAcquire()
    {
        var now = time.GetUtcNow();

        lock (_lock)
        {
            while (_recent.Count > 0 && now - _recent.Peek() >= TimeSpan.FromMinutes(1))
            {
                _recent.Dequeue();
            }

            if (_recent.Count >= perMinute)
            {
                return false;
            }

            _recent.Enqueue(now);
            return true;
        }
    }
}
