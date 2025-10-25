using System;
using System.Runtime.InteropServices;

namespace GraphAudio.Core;

/// <summary>
/// Lock-free ring buffer for triple buffering between render and audio threads.
/// </summary>
internal unsafe class RingBuffer : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeRingBuffer
    {
        public float* BufferPtr;
        public volatile int WritePos;
        public volatile int ReadPos;
        public int Capacity;
        public int Channels;
    }

    private readonly NativeRingBuffer* _native;
    private readonly GCHandle _bufferHandle;

    public int Channels => _native->Channels;
    public int Capacity => _native->Capacity;

    public RingBuffer(int channels, int capacity)
    {
        _native = (NativeRingBuffer*)Marshal.AllocHGlobal(sizeof(NativeRingBuffer));
        _native->Channels = channels;
        _native->Capacity = capacity;
        _native->ReadPos = 0;
        _native->WritePos = 0;

        var buffer = new float[capacity * channels];
        _bufferHandle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        _native->BufferPtr = (float*)_bufferHandle.AddrOfPinnedObject();
    }

    internal NativeRingBuffer* GetNativePointer() => _native;

    public int AvailableWrite => (Capacity + _native->ReadPos - _native->WritePos - 1) % Capacity;

    /// <summary>
    /// Write an interleaved buffer (frames x channels) into the ring buffer.
    /// </summary>
    public void WriteInterleaved(float[] interleaved, int channels)
    {
        if (interleaved == null) throw new ArgumentNullException(nameof(interleaved));

        int framesAvailable = Math.Min(AudioBuffer.FramesPerBlock, AvailableWrite);
        int framesToWrite = Math.Min(AudioBuffer.FramesPerBlock, interleaved.Length / channels);
        int frames = Math.Min(framesAvailable, framesToWrite);

        int samplesToWrite = frames * channels;
        int writePos = _native->WritePos;
        int samplesAtEnd = (Capacity - writePos) * Channels;

        fixed (float* srcBase = &interleaved[0])
        {
            float* src = srcBase;
            if (samplesToWrite <= samplesAtEnd)
            {
                NativeMemory.Copy(src, _native->BufferPtr + (writePos * Channels), (uint)(samplesToWrite * sizeof(float)));
            }
            else
            {
                int firstChunk = samplesAtEnd;
                int secondChunk = samplesToWrite - firstChunk;

                NativeMemory.Copy(src, _native->BufferPtr + (writePos * Channels), (uint)(firstChunk * sizeof(float)));
                NativeMemory.Copy(src + firstChunk, _native->BufferPtr, (uint)(secondChunk * sizeof(float)));
            }
        }

        _native->WritePos = (_native->WritePos + frames) % Capacity;
    }

    public void Dispose()
    {
        if (_bufferHandle.IsAllocated)
            _bufferHandle.Free();
        if (_native != null)
            Marshal.FreeHGlobal((IntPtr)_native);
    }
}
