using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using GraphAudio.Core;

namespace GraphAudio.IO;

/// <summary>
/// A decoder for various audio formats using the libsndfile library.
/// This implementation reads from a seekable C# Stream and will take ownership of the stream.
/// </summary>
public sealed unsafe class AudioDecoder : IDisposable
{
    private readonly Stream _stream;
    private IntPtr _sndfileHandle;
    private GCHandle _streamHandle;
    private bool _disposed;

    public TimeSpan Duration { get; }
    public int SampleRate { get; }
    public int Channels { get; }

    public AudioDecoder(Stream stream)
    {
        if (!stream.CanRead) throw new ArgumentException("Stream must be readable.", nameof(stream));
        if (!stream.CanSeek) throw new ArgumentException("Stream must be seekable.", nameof(stream));

        _stream = stream;
        _streamHandle = GCHandle.Alloc(_stream);

        var virtualIo = new Libsndfile.SF_VIRTUAL_IO
        {
            get_filelen = &VioGetLength,
            seek = &VioSeek,
            read = &VioRead,
            write = &VioWrite,
            tell = &VioTell
        };

        var sfInfo = new Libsndfile.SF_INFO();
        _sndfileHandle = Libsndfile.sf_open_virtual(ref virtualIo, Libsndfile.SFM_READ, ref sfInfo, GCHandle.ToIntPtr(_streamHandle));

        if (_sndfileHandle == IntPtr.Zero)
        {
            if (_streamHandle.IsAllocated)
            {
                _streamHandle.Free();
            }
            _stream.Dispose();
            string errorMessage = Libsndfile.sf_strerror(IntPtr.Zero);
            throw new InvalidOperationException($"Libsndfile failed to open virtual stream: {errorMessage}");
        }

        Channels = sfInfo.channels;
        SampleRate = sfInfo.samplerate;
        Duration = (sfInfo.frames > 0 && sfInfo.samplerate > 0)
            ? TimeSpan.FromSeconds((double)sfInfo.frames / sfInfo.samplerate)
            : TimeSpan.Zero;
    }

    ~AudioDecoder()
    {
        Dispose(false);
    }

    /// <summary>
    /// Decodes audio frames into a float buffer (interleaved format).
    /// </summary>
    /// <param name="buffer">A span of floats to receive the decoded samples.</param>
    /// <returns>The number of frames read (each frame is Channels samples).</returns>
    public long Decode(Span<float> buffer)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(AudioDecoder));
        if (Channels == 0) return 0;

        long framesToRead = buffer.Length / Channels;
        if (framesToRead <= 0) return 0;

        long framesRead = Libsndfile.sf_readf_float(_sndfileHandle, buffer, framesToRead);
        return framesRead;
    }

    /// <summary>
    /// Decodes audio frames into separate per-channel buffers (planar format).
    /// Uses a temporary buffer for deinterleaving with minimal allocations.
    /// </summary>
    /// <param name="channels">Span of channel arrays, one array per channel.</param>
    /// <returns>The number of frames read.</returns>
    /// <remarks>All channel arrays must have the same length. This method deinterleaves the decoded data into separate channels.</remarks>
    public long DecodePlanar(Span<float[]> channels)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(AudioDecoder));
        if (Channels == 0 || channels.Length == 0) return 0;

        long framesToRead = channels[0].Length;
        if (framesToRead <= 0) return 0;

        for (int i = 1; i < channels.Length; i++)
        {
            if (channels[i].Length != framesToRead)
                throw new ArgumentException("All channel buffers must have the same length.", nameof(channels));
        }

        if (channels.Length != Channels)
            throw new ArgumentException($"Expected {Channels} channels, but got {channels.Length}.", nameof(channels));

        long totalSamples = framesToRead * Channels;

        const int StackThreshold = 8192;
        if (totalSamples <= StackThreshold)
        {
            Span<float> stackBuffer = stackalloc float[(int)totalSamples];
            long framesRead = Libsndfile.sf_readf_float(_sndfileHandle, stackBuffer, framesToRead);

            if (framesRead > 0)
            {
                DeinterleaveFrames(stackBuffer, channels, framesRead, Channels);
            }

            return framesRead;
        }
        else
        {
            float[]? leaseBuffer = System.Buffers.ArrayPool<float>.Shared.Rent((int)totalSamples);
            var interleavedBuffer = leaseBuffer.AsSpan(0, (int)totalSamples);

            try
            {
                long framesRead = Libsndfile.sf_readf_float(_sndfileHandle, interleavedBuffer, framesToRead);

                if (framesRead > 0)
                {
                    DeinterleaveFrames(interleavedBuffer, channels, framesRead, Channels);
                }

                return framesRead;
            }
            finally
            {
                System.Buffers.ArrayPool<float>.Shared.Return(leaseBuffer);
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void DeinterleaveFrames(ReadOnlySpan<float> interleaved, Span<float[]> channels, long frameCount, int channelCount)
    {
        int sampleIndex = 0;
        int frameCountInt = (int)frameCount;

        for (int frame = 0; frame < frameCountInt; frame++)
        {
            for (int ch = 0; ch < channelCount; ch++)
            {
                channels[ch][frame] = interleaved[sampleIndex++];
            }
        }
    }

    /// <summary>
    /// Seeks to a specific position in the audio.
    /// </summary>
    /// <param name="position">The target position.</param>
    /// <returns>True if the seek succeeded, false otherwise.</returns>
    public bool TrySeek(TimeSpan position)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(AudioDecoder));

        long frameOffset = (long)(position.TotalSeconds * SampleRate);
        long result = Libsndfile.sf_seek(_sndfileHandle, frameOffset, Libsndfile.SEEK_SET);

        return result != -1;
    }

    /// <summary>
    /// Rewinds to the beginning of the audio.
    /// </summary>
    /// <returns>True if the rewind succeeded, false otherwise.</returns>
    public bool TryRewind() => TrySeek(TimeSpan.Zero);

    /// <summary>
    /// Loads a PlayableAudioBuffer from a file path synchronously.
    /// </summary>
    public static PlayableAudioBuffer LoadFromFile(string filePath)
    {
        using var fileStream = File.OpenRead(filePath);
        return LoadFromStream(fileStream);
    }

    /// <summary>
    /// Loads a PlayableAudioBuffer from a stream synchronously.
    /// </summary>
    public static PlayableAudioBuffer LoadFromStream(Stream stream)
    {
        using var decoder = new AudioDecoder(stream);

        long totalFrames = (long)(decoder.Duration.TotalSeconds * decoder.SampleRate);
        if (totalFrames <= 0 || totalFrames > int.MaxValue)
            throw new InvalidOperationException($"Invalid audio duration or frame count: {totalFrames}");

        var buffer = new PlayableAudioBuffer(decoder.Channels, (int)totalFrames, decoder.SampleRate);
        var channels = new float[decoder.Channels][];

        for (int i = 0; i < decoder.Channels; i++)
        {
            channels[i] = new float[totalFrames];
        }

        long framesRead = decoder.DecodePlanar(channels);

        for (int ch = 0; ch < decoder.Channels; ch++)
        {
            buffer.CopyToChannel(channels[ch].AsSpan(0, (int)framesRead), ch);
        }

        buffer.MarkAsInitialized();
        return buffer;
    }

    /// <summary>
    /// Loads a PlayableAudioBuffer from a file path asynchronously.
    /// </summary>
    public static Task<PlayableAudioBuffer> LoadFromFileAsync(string filePath) => Task.Run(() =>
    {
        using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: false);
        return LoadFromStream(fileStream);
    });

    /// <summary>
    /// Loads a PlayableAudioBuffer from a stream asynchronously.
    /// </summary>
    public static Task<PlayableAudioBuffer> LoadFromStreamAsync(Stream stream) => Task.Run(() => LoadFromStream(stream));

    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing)
    {
        if (_disposed) return;

        if (_sndfileHandle != IntPtr.Zero)
        {
            Libsndfile.sf_close(_sndfileHandle);
            _sndfileHandle = IntPtr.Zero;
        }

        if (disposing)
        {
            if (_streamHandle.IsAllocated)
            {
                _streamHandle.Free();
            }
            _stream.Dispose();
        }

        _disposed = true;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static long VioGetLength(IntPtr userData)
    {
        var stream = (Stream)GCHandle.FromIntPtr(userData).Target!;
        return stream.CanSeek ? stream.Length : 0;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static long VioSeek(long offset, int whence, IntPtr userData)
    {
        var stream = (Stream)GCHandle.FromIntPtr(userData).Target!;
        if (!stream.CanSeek) return -1;

        SeekOrigin origin = whence switch
        {
            Libsndfile.SEEK_SET => SeekOrigin.Begin,
            Libsndfile.SEEK_CUR => SeekOrigin.Current,
            Libsndfile.SEEK_END => SeekOrigin.End,
            _ => SeekOrigin.Begin
        };

        return stream.Seek(offset, origin);
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static long VioRead(IntPtr ptr, long count, IntPtr userData)
    {
        var stream = (Stream)GCHandle.FromIntPtr(userData).Target!;
        var buffer = new Span<byte>(ptr.ToPointer(), (int)count);
        return stream.Read(buffer);
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static long VioWrite(IntPtr ptr, long count, IntPtr userData) => 0;

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static long VioTell(IntPtr userData)
    {
        var stream = (Stream)GCHandle.FromIntPtr(userData).Target!;
        return stream.CanSeek ? stream.Position : 0;
    }
}
