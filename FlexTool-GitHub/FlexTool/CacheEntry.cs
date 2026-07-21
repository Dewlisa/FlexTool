namespace FlexTool;

/// <summary>
/// Generic cache wrapper with configurable TTL and optional key-based invalidation.
/// Used by <see cref="RimWorldSaveReader"/> to standardize caching across all data methods.
/// </summary>
internal sealed class CacheEntry<T>
{
    private T? _value;
    private string? _key;
    private DateTime _timestamp;
    private readonly TimeSpan _ttl;

    /// <param name="ttl">Time-to-live. Use <see cref="TimeSpan.MaxValue"/> for key-only invalidation.</param>
    public CacheEntry(TimeSpan ttl) => _ttl = ttl;

    /// <summary>Whether the cache holds a non-null value within its TTL window.</summary>
    public bool IsValid => _value is not null && (DateTime.Now - _timestamp) < _ttl;

    /// <summary>Tries to return the cached value if still valid (TTL not expired).</summary>
    public bool TryGet(out T value)
    {
        if (IsValid) { value = _value!; return true; }
        value = default!;
        return false;
    }

    /// <summary>Tries to return the cached value if the key matches and TTL has not expired.</summary>
    public bool TryGet(string key, out T value)
    {
        if (IsValid && _key == key) { value = _value!; return true; }
        value = default!;
        return false;
    }

    /// <summary>Stores a value with an optional cache key and resets the TTL clock.</summary>
    public void Set(T value, string? key = null)
    {
        _value = value;
        _key = key;
        _timestamp = DateTime.Now;
    }

    /// <summary>Forces the next access to miss, regardless of TTL.</summary>
    public void Invalidate()
    {
        _value = default;
        _key = null;
    }
}
