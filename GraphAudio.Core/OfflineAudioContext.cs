using System;

namespace GraphAudio.Core;

/// <summary>
/// Offline audio context for non-real-time rendering. 
/// </summary>
public class OfflineAudioContext : AudioContextBase
{
    private float[][] _bufferCache;
    private int _cachedFrameCount;
    private int _cacheCapacity;
    private const int InitialCacheCapacity = AudioBuffer.FramesPerBlock * 4;

    /// <summary>
    /// Create an offline audio context.
    /// </summary>
    public OfflineAudioContext(int sampleRate = 48000) : base(sampleRate)
    {
        _bufferCache = Array.Empty<float[]>();
        _cachedFrameCount = 0;
        _cacheCapacity = 0;
    }

    /// <summary>
    /// Render audio frames directly into the provided output buffer.
    /// The buffer should be arranged as output[channel][frame].
    /// Channel count is derived from the output buffer length.
    /// </summary>
    public void Render(float[][] output, int frameCount, int startIndex = 0)
    {
        if (output.Length == 0)
            throw new ArgumentException("Output buffer must have at least one channel.", nameof(output));

        if (frameCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(frameCount), "Frame count must be positive.");

        if (startIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(startIndex), "Start index must be non-negative.");

        int channels = output.Length;

        for (int ch = 0; ch < channels; ch++)
        {
            if (output[ch] is null)
                throw new ArgumentException($"Channel {ch} buffer is null.", nameof(output));

            int requiredLength = startIndex + frameCount;
            if (output[ch].Length < requiredLength)
                throw new ArgumentException($"Channel {ch} buffer is too small. Required: {requiredLength}, Available: {output[ch].Length}", nameof(output));
        }

        int framesWritten = 0;

        if (_cachedFrameCount > 0)
        {
            int framesToCopy = Math.Min(_cachedFrameCount, frameCount);

            for (int ch = 0; ch < channels; ch++)
            {
                _bufferCache[ch].AsSpan(0, framesToCopy).CopyTo(output[ch].AsSpan(startIndex));
            }

            if (framesToCopy < _cachedFrameCount)
            {
                int remainingFrames = _cachedFrameCount - framesToCopy;
                for (int ch = 0; ch < channels; ch++)
                {
                    _bufferCache[ch].AsSpan(framesToCopy, remainingFrames).CopyTo(_bufferCache[ch]);
                }
            }

            framesWritten = framesToCopy;
            _cachedFrameCount -= framesToCopy;
        }

        while (framesWritten < frameCount)
        {
            var buffer = ProcessBlock();
            int framesToCopy = Math.Min(AudioBuffer.FramesPerBlock, frameCount - framesWritten);

            for (int ch = 0; ch < channels; ch++)
            {
                buffer.GetChannelSpan(ch).Slice(0, framesToCopy).CopyTo(output[ch].AsSpan(startIndex + framesWritten));
            }

            framesWritten += framesToCopy;

            int excessFrames = AudioBuffer.FramesPerBlock - framesToCopy;
            if (excessFrames > 0)
            {
                EnsureCacheCapacity(channels, excessFrames);

                for (int ch = 0; ch < channels; ch++)
                {
                    buffer.GetChannelSpan(ch).Slice(framesToCopy, excessFrames).CopyTo(_bufferCache[ch].AsSpan(_cachedFrameCount));
                }

                _cachedFrameCount += excessFrames;
            }
        }
    }

    /// <summary>
    /// Render audio frames and return them as a new float array.
    /// Prefer using the overload that takes a pre-allocated buffer for better performance.
    /// </summary>
    public float[][] Render(int frameCount)
    {
        if (frameCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(frameCount), "Frame count must be positive.");

        var buffer = Destination.GetOutputBuffer();
        int channels = buffer?.ChannelCount ?? 2;

        float[][] output = new float[channels][];
        for (int ch = 0; ch < channels; ch++)
        {
            output[ch] = new float[frameCount];
        }

        Render(output, frameCount);
        return output;
    }

    private void EnsureCacheCapacity(int channels, int additionalFrames)
    {
        int requiredCapacity = _cachedFrameCount + additionalFrames;

        if (_bufferCache.Length == 0)
        {
            _cacheCapacity = Math.Max(InitialCacheCapacity, requiredCapacity);
            _bufferCache = new float[channels][];
            for (int ch = 0; ch < channels; ch++)
            {
                _bufferCache[ch] = new float[_cacheCapacity];
            }
            return;
        }

        if (requiredCapacity > _cacheCapacity)
        {
            int newCapacity = Math.Max(_cacheCapacity * 2, requiredCapacity);

            for (int ch = 0; ch < channels; ch++)
            {
                var newBuffer = new float[newCapacity];
                if (_cachedFrameCount > 0)
                {
                    Array.Copy(_bufferCache[ch], 0, newBuffer, 0, _cachedFrameCount);
                }
                _bufferCache[ch] = newBuffer;
            }

            _cacheCapacity = newCapacity;
        }
    }
}
