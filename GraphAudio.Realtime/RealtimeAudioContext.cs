using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using MiniaudioSharp;
using GraphAudio.Core;
using static GraphAudio.Core.RingBuffer;

namespace GraphAudio.Realtime;

/// <summary>
/// Real-time audio context using miniaudio.
/// </summary>
public unsafe class RealtimeAudioContext : AudioContextBase
{
    private ma_device* _device;
    private ma_context* _maContext;

    private readonly RingBuffer _ringBuffer;
    private readonly int _deviceChannels;
    private readonly Thread _renderThread;
    private volatile bool _isRunning;
    private bool _isStarted;
    private bool _isDisposed;

    public bool IsPlaying => _isStarted && !_isDisposed;
    public AudioDeviceInfo? CurrentDevice { get; private set; }

    public RealtimeAudioContext(int sampleRate = 48000, int channels = 2, int bufferSize = 256, AudioDeviceInfo? deviceInfo = null)
        : base(sampleRate)
    {
        if (channels < 1 || channels > 32) throw new ArgumentOutOfRangeException(nameof(channels));
        if (bufferSize < 32 || bufferSize > 8192) throw new ArgumentOutOfRangeException(nameof(bufferSize));

        Destination.SetChannelCount(channels);
        _deviceChannels = channels;
        _ringBuffer = new RingBuffer(channels, bufferSize * 5);

        _maContext = (ma_context*)Marshal.AllocHGlobal(sizeof(ma_context));
        if (Miniaudio.ma_context_init(null, 0, null, _maContext) != ma_result.MA_SUCCESS)
            throw new InvalidOperationException("Failed to initialize miniaudio context");

        _device = (ma_device*)Marshal.AllocHGlobal(sizeof(ma_device));
        InitializeDevice(deviceInfo, channels, bufferSize);

        _isRunning = true;
        _renderThread = new Thread(RenderLoop) { IsBackground = true, Priority = ThreadPriority.Highest };
        _renderThread.Start();
    }

    public static List<AudioDeviceInfo> GetAvailableDevices()
    {
        var devices = new List<AudioDeviceInfo>();
        ma_context* ctx = (ma_context*)Marshal.AllocHGlobal(sizeof(ma_context));
        try
        {
            if (Miniaudio.ma_context_init(null, 0, null, ctx) != ma_result.MA_SUCCESS) return devices;

            ma_device_info* pPlayback;
            uint playbackCount;
            if (Miniaudio.ma_context_get_devices(ctx, &pPlayback, &playbackCount, null, null) == ma_result.MA_SUCCESS)
            {
                for (uint i = 0; i < playbackCount; i++)
                    devices.Add(new AudioDeviceInfo(pPlayback[i], (int)i));
            }
            Miniaudio.ma_context_uninit(ctx);
        }
        finally { Marshal.FreeHGlobal((IntPtr)ctx); }
        return devices;
    }

    public void Start()
    {
        if (_isDisposed) throw new ObjectDisposedException(nameof(RealtimeAudioContext));
        if (_isStarted) return;

        if (Miniaudio.ma_device_start(_device) != ma_result.MA_SUCCESS)
            throw new InvalidOperationException("Failed to start audio device");
        _isStarted = true;
    }

    public void Stop()
    {
        if (_isDisposed) throw new ObjectDisposedException(nameof(RealtimeAudioContext));
        if (!_isStarted) return;

        Miniaudio.ma_device_stop(_device);
        _isStarted = false;
    }

    public void SwitchDevice(AudioDeviceInfo? deviceInfo)
    {
        if (_isDisposed) throw new ObjectDisposedException(nameof(RealtimeAudioContext));

        bool wasPlaying = _isStarted;
        if (_isStarted) Stop();

        Miniaudio.ma_device_uninit(_device);
        InitializeDevice(deviceInfo, _ringBuffer.Channels, _ringBuffer.Capacity / 5);

        if (wasPlaying) Start();
    }

    private void InitializeDevice(AudioDeviceInfo? deviceInfo, int channels, int bufferSize)
    {
        var config = Miniaudio.ma_device_config_init(ma_device_type.ma_device_type_playback);
        config.playback.format = ma_format.ma_format_f32;
        config.playback.channels = (uint)channels;
        config.sampleRate = (uint)SampleRate;
        config.periodSizeInFrames = (uint)bufferSize;

        config.dataCallback = (nint)(delegate* unmanaged[Cdecl]<ma_device*, void*, void*, uint, void>)&DataCallback;
        config.pUserData = _ringBuffer.GetNativePointer();

        if (deviceInfo is not null)
        {
            var deviceId = deviceInfo.Value.GetDeviceId();
            config.playback.pDeviceID = &deviceId;
        }

        if (Miniaudio.ma_device_init(_maContext, &config, _device) != ma_result.MA_SUCCESS)
            throw new InvalidOperationException($"Failed to initialize device: {deviceInfo?.Name ?? "default"}");

        CurrentDevice = deviceInfo ?? GetDefaultDevice();
    }

    public static AudioDeviceInfo? GetDefaultDevice()
    {
        var devices = GetAvailableDevices();
        if (devices.Count == 0) return null;

        var defaultDev = devices.FirstOrDefault(d => d.IsDefault);
        return defaultDev.IsDefault ? defaultDev : devices[0];
    }

    private void RenderLoop()
    {
        while (_isRunning)
        {
            if (_ringBuffer.AvailableWrite >= AudioBuffer.FramesPerBlock)
            {
                var interleaved = BufferPool.RentFloatBuffer(_deviceChannels);
                try
                {
                    ProcessBlockInterleaved(interleaved, _deviceChannels);
                    _ringBuffer.WriteInterleaved(interleaved, _deviceChannels);
                }
                finally
                {
                    BufferPool.ReturnFloatBuffer(interleaved);
                }
            }
            else
            {
                Thread.SpinWait(2048);
            }
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
    private static void DataCallback(ma_device* pDevice, void* pOutput, void* pInput, uint frameCount)
    {
        // This is a real mess, but we needed all that to avoid touching managed memory in the audio callback, and to keep it away from gc interruptions, so we use all native memory.
        // In theory, this should be safe. In practice, this is as far as my brain can stretch and it could be wrong fo all I know. I'm really not an expert in this area. Please improve if you can.
        // This part gives me anxiety because there's no real way to verify it's correct without it exploding first. We could write a tiny bit of C code that handles the ring buffer and calls into C# for processing, tolerating gc pauses, if this proves unreliable.

        var ringBuffer = (NativeRingBuffer*)pDevice->pUserData;
        if (ringBuffer == null) return;

        float* outputPtr = (float*)pOutput;
        int framesRemaining = (int)frameCount;
        int samplesWritten = 0;

        while (framesRemaining > 0)
        {
            int writePos = ringBuffer->WritePos;
            int readPos = ringBuffer->ReadPos;
            int availableFrames = writePos >= readPos
                ? writePos - readPos
                : ringBuffer->Capacity - readPos + writePos;

            if (availableFrames == 0) break;

            int framesToRead = framesRemaining < availableFrames ? framesRemaining : availableFrames;

            int framesAtEnd = ringBuffer->Capacity - readPos;

            if (framesToRead <= framesAtEnd)
            {
                int samples = framesToRead * ringBuffer->Channels;
                NativeMemory.Copy(ringBuffer->BufferPtr + (readPos * ringBuffer->Channels),
                    outputPtr + samplesWritten,
                    (uint)(samples * sizeof(float)));
                samplesWritten += samples;
            }
            else
            {
                int samples1 = framesAtEnd * ringBuffer->Channels;
                NativeMemory.Copy(ringBuffer->BufferPtr + (readPos * ringBuffer->Channels),
                    outputPtr + samplesWritten,
                    (uint)(samples1 * sizeof(float)));
                samplesWritten += samples1;

                int frames2Needed = framesToRead - framesAtEnd;
                int samples2 = frames2Needed * ringBuffer->Channels;
                NativeMemory.Copy(ringBuffer->BufferPtr,
                    outputPtr + samplesWritten,
                    (uint)(samples2 * sizeof(float)));
                samplesWritten += samples2;
            }

            int newReadPos = readPos + framesToRead;
            ringBuffer->ReadPos = newReadPos >= ringBuffer->Capacity ? newReadPos - ringBuffer->Capacity : newReadPos;
            framesRemaining -= framesToRead;
        }

        if (framesRemaining > 0)
        {
            int silenceCount = framesRemaining * ringBuffer->Channels;
            NativeMemory.Fill(outputPtr + samplesWritten, (uint)(silenceCount * sizeof(float)), 0);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (_isDisposed) return;

        if (disposing)
        {
            _isRunning = false;
            _renderThread?.Join(1000);

            if (_isStarted) Miniaudio.ma_device_stop(_device);

            if (_device != null)
            {
                Miniaudio.ma_device_uninit(_device);
                Marshal.FreeHGlobal((IntPtr)_device);
                _device = null;
            }
            if (_maContext != null)
            {
                Miniaudio.ma_context_uninit(_maContext);
                Marshal.FreeHGlobal((IntPtr)_maContext);
                _maContext = null;
            }

            _ringBuffer?.Dispose();
        }

        _isDisposed = true;
        base.Dispose(disposing);
    }
}

/// <summary>
/// Information about an audio device.
/// </summary>
public struct AudioDeviceInfo
{
    private readonly ma_device_info _info;

    /// <summary>
    /// Device name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Device index.
    /// </summary>
    public int Index { get; }

    /// <summary>
    /// Whether this is the default device.
    /// </summary>
    public bool IsDefault { get; }

    internal AudioDeviceInfo(ma_device_info info, int index)
    {
        _info = info;
        Index = index;
        IsDefault = info.isDefault > 0;

        unsafe
        {
            Name = Marshal.PtrToStringAnsi((IntPtr)(&info.name[0])) ?? "Unknown Device";
        }
    }

    internal ma_device_id GetDeviceId()
    {
        return _info.id;
    }

    public override string ToString()
    {
        return IsDefault ? $"{Name} (Default)" : Name;
    }
}
