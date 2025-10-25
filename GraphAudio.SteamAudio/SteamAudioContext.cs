using System;
using System.Collections.Concurrent;
using System.Numerics;
using System.Runtime.InteropServices;
using GraphAudio.Core;
using SteamAudio;

namespace GraphAudio.SteamAudio;

/// <summary>
/// Static manager that maps AudioContextBase instances to their corresponding SteamAudio resources.
/// </summary>
public static class SteamAudioContext
{
    private sealed class SteamAudioResources : IDisposable
    {
        public IPL.Context Context;
        public IPL.Hrtf Hrtf;
        public IPL.CoordinateSpace3 ListenerTransform;
        public readonly int SampleRate;
        public readonly int FrameSize;

        public SteamAudioResources(int sampleRate, int frameSize)
        {
            SampleRate = sampleRate;
            FrameSize = frameSize;
            ListenerTransform = CreateIdentityTransform();
        }

        public void Dispose()
        {
            if (Hrtf.Handle != IntPtr.Zero)
            {
                IPL.HrtfRelease(ref Hrtf);
                Hrtf = default;
            }

            if (Context.Handle != IntPtr.Zero)
            {
                IPL.ContextRelease(ref Context);
                Context = default;
            }
        }

        private static IPL.CoordinateSpace3 CreateIdentityTransform()
        {
            return new IPL.CoordinateSpace3
            {
                Right = new IPL.Vector3 { X = 1, Y = 0, Z = 0 },
                Up = new IPL.Vector3 { X = 0, Y = 1, Z = 0 },
                Ahead = new IPL.Vector3 { X = 0, Y = 0, Z = -1 },
                Origin = new IPL.Vector3 { X = 0, Y = 0, Z = 0 }
            };
        }
    }

    private static readonly ConcurrentDictionary<AudioContextBase, SteamAudioResources> _contextMap = new();

    /// <summary>
    /// Gets or creates SteamAudio resources for the given audio context.
    /// </summary>
    private static SteamAudioResources GetOrCreate(AudioContextBase context)
    {
        return _contextMap.GetOrAdd(context, ctx =>
        {
            var resources = new SteamAudioResources(ctx.SampleRate, AudioBuffer.FramesPerBlock);

            var contextSettings = new IPL.ContextSettings
            {
                Version = IPL.Version,
                LogCallback = null,
                AllocateCallback = null,
                FreeCallback = null,
                SimdLevel = IPL.SimdLevel.Avx2,
                Flags = 0
            };

            var error = IPL.ContextCreate(in contextSettings, out resources.Context);
            if (error != IPL.Error.Success)
                throw new InvalidOperationException($"Failed to create SteamAudio context: {error}");

            var hrtfSettings = new IPL.HrtfSettings
            {
                Type = IPL.HrtfType.Default,
                SofaFileName = null,
                SofaData = IntPtr.Zero,
                SofaDataSize = 0,
                Volume = 1.0f,
                NormType = IPL.HrtfNormType.None
            };

            var audioSettings = new IPL.AudioSettings
            {
                SamplingRate = ctx.SampleRate,
                FrameSize = AudioBuffer.FramesPerBlock
            };

            error = IPL.HrtfCreate(resources.Context, in audioSettings, in hrtfSettings, out resources.Hrtf);
            if (error != IPL.Error.Success)
            {
                resources.Dispose();
                throw new InvalidOperationException($"Failed to create HRTF: {error}");
            }

            return resources;
        });
    }

    /// <summary>
    /// Gets the SteamAudio context for the given audio context.
    /// </summary>
    public static IPL.Context GetContext(AudioContextBase context)
    {
        return GetOrCreate(context).Context;
    }

    /// <summary>
    /// Gets the default HRTF for the given audio context.
    /// </summary>
    public static IPL.Hrtf GetHrtf(AudioContextBase context)
    {
        return GetOrCreate(context).Hrtf;
    }

    /// <summary>
    /// Gets the listener transform for the given audio context.
    /// </summary>
    public static IPL.CoordinateSpace3 GetListenerTransform(AudioContextBase context)
    {
        return GetOrCreate(context).ListenerTransform;
    }

    /// <summary>
    /// Sets the listener transform for the given audio context.
    /// </summary>
    public static void SetListenerTransform(AudioContextBase context, IPL.CoordinateSpace3 transform)
    {
        var resources = GetOrCreate(context);
        resources.ListenerTransform = transform;
    }

    /// <summary>
    /// Sets the listener position and orientation for the given audio context.
    /// </summary>
    public static void SetListener(AudioContextBase context,
        Vector3 position,
        Vector3 forward,
        Vector3 up)
    {
        var resources = GetOrCreate(context);

        var iplForward = ToIpl(Vector3.Normalize(forward));
        var iplUp = ToIpl(Vector3.Normalize(up));
        var iplPosition = ToIpl(position);
        var iplRight = Cross(iplForward, iplUp);

        resources.ListenerTransform = new IPL.CoordinateSpace3
        {
            Right = iplRight,
            Up = iplUp,
            Ahead = new IPL.Vector3 { X = -iplForward.X, Y = -iplForward.Y, Z = -iplForward.Z },
            Origin = iplPosition
        };
    }

    /// <summary>
    /// Converts System.Numerics.Vector3 to IPL.Vector3.
    /// </summary>
    internal static IPL.Vector3 ToIpl(Vector3 v)
    {
        return new IPL.Vector3 { X = v.X, Y = v.Y, Z = v.Z };
    }

    /// <summary>
    /// Converts IPL.Vector3 to System.Numerics.Vector3.
    /// </summary>
    internal static Vector3 FromIpl(IPL.Vector3 v)
    {
        return new Vector3(v.X, v.Y, v.Z);
    }

    private static IPL.Vector3 Normalize(IPL.Vector3 v)
    {
        float len = MathF.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);
        if (len > 0.0001f)
        {
            float invLen = 1.0f / len;
            return new IPL.Vector3 { X = v.X * invLen, Y = v.Y * invLen, Z = v.Z * invLen };
        }
        return new IPL.Vector3 { X = 0, Y = 0, Z = 1 };
    }

    private static IPL.Vector3 Cross(IPL.Vector3 a, IPL.Vector3 b)
    {
        return new IPL.Vector3
        {
            X = a.Y * b.Z - a.Z * b.Y,
            Y = a.Z * b.X - a.X * b.Z,
            Z = a.X * b.Y - a.Y * b.X
        };
    }

    /// <summary>
    /// Manually disposes SteamAudio resources for a context. Usually not needed.
    /// </summary>
    public static void Dispose(AudioContextBase context)
    {
        if (_contextMap.TryRemove(context, out var resources))
        {
            resources.Dispose();
        }
    }
}

/// <summary>
/// Extension methods for AudioContextBase to provide fluent SteamAudio integration.
/// </summary>
public static class AudioContextSteamAudioExtensions
{
    /// <summary>
    /// Gets the SteamAudio context for this audio context.
    /// </summary>
    public static IPL.Context GetSteamAudioContext(this AudioContextBase context)
    {
        return SteamAudioContext.GetContext(context);
    }

    /// <summary>
    /// Gets the default HRTF for this audio context.
    /// </summary>
    public static IPL.Hrtf GetHrtf(this AudioContextBase context)
    {
        return SteamAudioContext.GetHrtf(context);
    }

    /// <summary>
    /// Gets the listener transform for this audio context.
    /// </summary>
    public static IPL.CoordinateSpace3 GetListenerTransform(this AudioContextBase context)
    {
        return SteamAudioContext.GetListenerTransform(context);
    }

    /// <summary>
    /// Sets the listener transform for this audio context.
    /// </summary>
    public static void SetListenerTransform(this AudioContextBase context, IPL.CoordinateSpace3 transform)
    {
        SteamAudioContext.SetListenerTransform(context, transform);
    }

    /// <summary>
    /// Sets the listener position and orientation for this audio context.
    /// </summary>
    public static void SetListener(this AudioContextBase context,
        Vector3 position,
        Vector3 forward,
        Vector3 up)
    {
        SteamAudioContext.SetListener(context, position, forward, up);
    }

    /// <summary>
    /// Manually disposes SteamAudio resources for this context. Usually not needed.
    /// </summary>
    public static void DisposeSteamAudio(this AudioContextBase context)
    {
        SteamAudioContext.Dispose(context);
    }
}
