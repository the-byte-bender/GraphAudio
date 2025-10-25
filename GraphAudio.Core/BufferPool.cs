using System;
using System.Collections.Concurrent;
using System.Threading;

namespace GraphAudio.Core;

/// <summary>
/// Object pool for AudioBuffer instances to avoid allocations during real-time processing.
/// </summary>
public sealed class BufferPool
{
    private readonly ConcurrentDictionary<int, ConcurrentStack<AudioBuffer>> _pools;
    private readonly ConcurrentDictionary<int, ConcurrentStack<float[]>> _floatPools;
    private int _totalBuffersCreated;
    private int _totalRents;
    private int _totalReturns;

    public BufferPool()
    {
        _pools = new ConcurrentDictionary<int, ConcurrentStack<AudioBuffer>>();
        _floatPools = new ConcurrentDictionary<int, ConcurrentStack<float[]>>();
    }

    /// <summary>
    /// Rent an interleaved float buffer with the specified channel count. The buffer length will be AudioBuffer.FramesPerBlock * channelCount.
    /// Returns a cleared buffer (zeros).
    /// </summary>
    public float[] RentFloatBuffer(int channelCount)
    {
        if (channelCount < 1 || channelCount > 32)
            throw new ArgumentOutOfRangeException(nameof(channelCount));

        var pool = _floatPools.GetOrAdd(channelCount, _ => new ConcurrentStack<float[]>());

        if (!pool.TryPop(out var buffer))
        {
            buffer = new float[AudioBuffer.FramesPerBlock * channelCount];
        }

        Array.Clear(buffer, 0, buffer.Length);
        return buffer;
    }

    /// <summary>
    /// Return an interleaved float buffer to the pool for reuse.
    /// </summary>
    public void ReturnFloatBuffer(float[]? buffer)
    {
        if (buffer == null) return;

        int channelCount = buffer.Length / AudioBuffer.FramesPerBlock;
        if (channelCount < 1 || channelCount > 32) return;

        var pool = _floatPools.GetOrAdd(channelCount, _ => new ConcurrentStack<float[]>());
        const int MaxPoolSize = 64;
        if (pool.Count < MaxPoolSize)
        {
            pool.Push(buffer);
        }
    }

    /// <summary>
    /// Rent a buffer with the specified channel count.
    /// Returns a cleared buffer ready for use.
    /// </summary>
    public AudioBuffer Rent(int channelCount)
    {
        if (channelCount < 1 || channelCount > 32)
            throw new ArgumentOutOfRangeException(nameof(channelCount));

        Interlocked.Increment(ref _totalRents);

        var pool = _pools.GetOrAdd(channelCount, _ => new ConcurrentStack<AudioBuffer>());

        AudioBuffer? buffer;
        if (!pool.TryPop(out buffer))
        {
            buffer = new AudioBuffer(channelCount);
            Interlocked.Increment(ref _totalBuffersCreated);
        }

        buffer.Clear();
        return buffer;
    }

    /// <summary>
    /// Return a buffer to the pool for reuse.
    /// The buffer should not be used after returning it.
    /// </summary>
    public void Return(AudioBuffer? buffer)
    {
        if (buffer == null)
            return;

        Interlocked.Increment(ref _totalReturns);

        var pool = _pools.GetOrAdd(buffer.ChannelCount, _ => new ConcurrentStack<AudioBuffer>());

        const int MaxPoolSize = 64;
        if (pool.Count < MaxPoolSize)
        {
            pool.Push(buffer);
        }
    }

    /// <summary>
    /// Pre-warm the pool by creating buffers in advance.
    /// Useful for avoiding allocations during initial processing.
    /// </summary>
    public void Prewarm(int channelCount, int bufferCount)
    {
        if (channelCount < 1 || channelCount > 32)
            throw new ArgumentOutOfRangeException(nameof(channelCount));
        if (bufferCount < 0)
            throw new ArgumentOutOfRangeException(nameof(bufferCount));

        var pool = _pools.GetOrAdd(channelCount, _ => new ConcurrentStack<AudioBuffer>());

        int currentCount = pool.Count;
        int toCreate = bufferCount - currentCount;

        for (int i = 0; i < toCreate; i++)
        {
            var buffer = new AudioBuffer(channelCount);
            pool.Push(buffer);
            Interlocked.Increment(ref _totalBuffersCreated);
        }
    }

    /// <summary>
    /// Get statistics about pool usage (useful for diagnostics).
    /// </summary>
    public PoolStatistics GetStatistics()
    {
        var stats = new PoolStatistics
        {
            TotalBuffersCreated = Interlocked.CompareExchange(ref _totalBuffersCreated, 0, 0),
            TotalRents = Interlocked.CompareExchange(ref _totalRents, 0, 0),
            TotalReturns = Interlocked.CompareExchange(ref _totalReturns, 0, 0),
            PooledBufferCount = 0
        };

        foreach (var pool in _pools.Values)
        {
            stats.PooledBufferCount += pool.Count;
        }

        return stats;
    }

    /// <summary>
    /// Clear all pooled buffers.
    /// </summary>
    public void Clear()
    {
        _pools.Clear();
    }
}

public struct PoolStatistics
{
    public int TotalBuffersCreated;
    public int TotalRents;
    public int TotalReturns;
    public int PooledBufferCount;

    public int OutstandingBuffers => TotalRents - TotalReturns;

    public override string ToString()
    {
        return $"Created: {TotalBuffersCreated}, Rents: {TotalRents}, Returns: {TotalReturns}, " +
               $"Pooled: {PooledBufferCount}, Outstanding: {OutstandingBuffers}";
    }
}
