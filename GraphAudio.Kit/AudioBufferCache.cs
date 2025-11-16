using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GraphAudio.Core;
using GraphAudio.Kit.DataProviders;

namespace GraphAudio.Kit;

/// <summary>
/// Configuration options for the audio buffer cache.
/// </summary>
/// <param name="MaxCachedBuffers">Maximum number of buffers to cache. Set to 0 for unlimited cache size.</param>
public readonly record struct AudioBufferCacheOptions(int MaxCachedBuffers = 256);

/// <summary>
/// A thread-safe Least Recently Used cache for PlayableAudioBuffers.
/// When the cache is full, the least recently accessed buffer is evicted.
/// </summary>
public sealed class AudioBufferCache
{
    private readonly Dictionary<string, CacheEntry> _cache = new();
    private readonly LinkedList<string> _accessOrder = new();
    private readonly ReaderWriterLockSlim _lock = new();

    /// <summary>
    /// The configuration options for this cache.
    /// </summary>
    public readonly AudioBufferCacheOptions Options;

    private sealed record CacheEntry(PlayableAudioBuffer Buffer, LinkedListNode<string> AccessNode)
    {
        public LinkedListNode<string> AccessNode { get; set; } = AccessNode;
    }

    /// <summary>
    /// Gets the number of buffers currently in the cache.
    /// </summary>
    public int Count
    {
        get
        {
            _lock.EnterReadLock();
            try
            {
                return _cache.Count;
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }
    }

    /// <summary>
    /// Creates a new AudioBufferCache with default options.
    /// </summary>
    public AudioBufferCache() : this(new AudioBufferCacheOptions())
    {
    }

    /// <summary>
    /// Creates a new AudioBufferCache with the specified options.
    /// </summary>
    public AudioBufferCache(AudioBufferCacheOptions options)
    {
        Options = options;
    }

    /// <summary>
    /// Gets a buffer from the cache, or loads it using the provider if not cached.
    /// </summary>
    public async Task<PlayableAudioBuffer> GetOrLoadAsync(
        string key,
        IDataProvider provider,
        CancellationToken cancellationToken = default)
    {
        if (TryGet(key, out var cachedBuffer))
        {
            return cachedBuffer;
        }

        var buffer = await provider.GetPlayableBufferAsync(key, cancellationToken).ConfigureAwait(false);
        Add(key, buffer);
        return buffer;
    }

    /// <summary>
    /// Tries to get a buffer from the cache without loading.
    /// </summary>
    public bool TryGet(string key, out PlayableAudioBuffer buffer)
    {
        _lock.EnterUpgradeableReadLock();
        try
        {
            if (_cache.TryGetValue(key, out var entry))
            {
                _lock.EnterWriteLock();
                try
                {
                    _accessOrder.Remove(entry.AccessNode);
                    entry.AccessNode = _accessOrder.AddLast(key);
                }
                finally
                {
                    _lock.ExitWriteLock();
                }

                buffer = entry.Buffer;
                return true;
            }

            buffer = null!;
            return false;
        }
        finally
        {
            _lock.ExitUpgradeableReadLock();
        }
    }

    /// <summary>
    /// Adds a buffer to the cache. If the key already exists, it will be replaced.
    /// </summary>
    public void Add(string key, PlayableAudioBuffer buffer)
    {
        _lock.EnterWriteLock();
        try
        {
            if (_cache.TryGetValue(key, out var existingEntry))
            {
                _accessOrder.Remove(existingEntry.AccessNode);
                _cache.Remove(key);
            }

            if (Options.MaxCachedBuffers > 0 && _cache.Count >= Options.MaxCachedBuffers)
            {
                EvictOne();
            }

            var accessNode = _accessOrder.AddLast(key);
            _cache[key] = new CacheEntry(buffer, accessNode);
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Removes a buffer from the cache.
    /// </summary>
    public bool Remove(string key)
    {
        _lock.EnterWriteLock();
        try
        {
            if (_cache.TryGetValue(key, out var entry))
            {
                _accessOrder.Remove(entry.AccessNode);
                _cache.Remove(key);
                return true;
            }

            return false;
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Clears all buffers from the cache.
    /// </summary>
    public void Clear()
    {
        _lock.EnterWriteLock();
        try
        {
            _cache.Clear();
            _accessOrder.Clear();
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Checks if a key exists in the cache.
    /// </summary>
    public bool Contains(string key)
    {
        _lock.EnterReadLock();
        try
        {
            return _cache.ContainsKey(key);
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    private void EvictOne()
    {
        if (_cache.Count == 0) return;

        var keyToRemove = _accessOrder.First!.Value;

        if (_cache.TryGetValue(keyToRemove, out var entry))
        {
            _accessOrder.Remove(entry.AccessNode);
            _cache.Remove(keyToRemove);
        }
    }
}
