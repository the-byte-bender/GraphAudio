using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace GraphAudio.Realtime;

// ===== ENUMS =====

internal enum ma_result : int
    {
        MA_SUCCESS = 0,
        MA_ERROR = -1,
        MA_INVALID_ARGS = -2,
        MA_INVALID_OPERATION = -3,
        MA_OUT_OF_MEMORY = -4,
        MA_OUT_OF_RANGE = -5,
        MA_ACCESS_DENIED = -6,
        MA_DOES_NOT_EXIST = -7,
        MA_ALREADY_EXISTS = -8,
        MA_TOO_MANY_OPEN_FILES = -9,
        MA_INVALID_FILE = -10,
        MA_TOO_BIG = -11,
        MA_PATH_TOO_LONG = -12,
        MA_NAME_TOO_LONG = -13,
        MA_NOT_DIRECTORY = -14,
        MA_IS_DIRECTORY = -15,
        MA_DIRECTORY_NOT_EMPTY = -16,
        MA_AT_END = -17,
        MA_NO_SPACE = -18,
        MA_BUSY = -19,
        MA_IO_ERROR = -20,
        MA_INTERRUPT = -21,
        MA_UNAVAILABLE = -22,
        MA_ALREADY_IN_USE = -23,
        MA_BAD_ADDRESS = -24,
        MA_BAD_SEEK = -25,
        MA_BAD_PIPE = -26,
        MA_DEADLOCK = -27,
        MA_TOO_MANY_LINKS = -28,
        MA_NOT_IMPLEMENTED = -29,
        MA_NO_MESSAGE = -30,
        MA_BAD_MESSAGE = -31,
        MA_NO_DATA_AVAILABLE = -32,
        MA_INVALID_DATA = -33,
        MA_TIMEOUT = -34,
        MA_NO_NETWORK = -35,
        MA_NOT_UNIQUE = -36,
        MA_NOT_SOCKET = -37,
        MA_NO_ADDRESS = -38,
        MA_BAD_PROTOCOL = -39,
        MA_PROTOCOL_UNAVAILABLE = -40,
        MA_PROTOCOL_NOT_SUPPORTED = -41,
        MA_PROTOCOL_FAMILY_NOT_SUPPORTED = -42,
        MA_ADDRESS_FAMILY_NOT_SUPPORTED = -43,
        MA_SOCKET_NOT_SUPPORTED = -44,
        MA_CONNECTION_RESET = -45,
        MA_ALREADY_CONNECTED = -46,
        MA_NOT_CONNECTED = -47,
        MA_CONNECTION_REFUSED = -48,
        MA_NO_HOST = -49,
        MA_IN_PROGRESS = -50,
        MA_CANCELLED = -51,
        MA_MEMORY_ALREADY_MAPPED = -52,
        MA_FORMAT_NOT_SUPPORTED = -53,
        MA_DEVICE_TYPE_NOT_SUPPORTED = -54,
        MA_SHARE_MODE_NOT_SUPPORTED = -55,
        MA_NO_BACKEND = -56,
        MA_NO_DEVICE = -57,
        MA_API_NOT_FOUND = -58,
        MA_INVALID_DEVICE_CONFIG = -59,
        MA_LOOP = -60,
        MA_BACKEND_NOT_ENABLED = -61,
        MA_DEVICE_NOT_INITIALIZED = -62,
        MA_DEVICE_ALREADY_INITIALIZED = -63,
        MA_DEVICE_NOT_STARTED = -64,
        MA_DEVICE_NOT_STOPPED = -65,
        MA_FAILED_TO_INIT_BACKEND = -66,
        MA_FAILED_TO_OPEN_BACKEND_DEVICE = -67,
        MA_FAILED_TO_START_BACKEND_DEVICE = -68,
        MA_FAILED_TO_STOP_BACKEND_DEVICE = -69
    }

internal enum ma_format : uint
    {
        ma_format_unknown = 0,
        ma_format_u8 = 1,
        ma_format_s16 = 2,
        ma_format_s24 = 3,
        ma_format_s32 = 4,
        ma_format_f32 = 5,
        ma_format_count
    }

internal enum ma_device_type : uint
    {
        ma_device_type_playback = 1,
        ma_device_type_capture = 2,
        ma_device_type_duplex = 3,
        ma_device_type_loopback = 4
    }

// ===== STRUCTS =====

[StructLayout(LayoutKind.Explicit, Size = 256)]
internal unsafe struct ma_device_id
    {
        // Windows WASAPI
        [FieldOffset(0)]
        public fixed ushort wasapi[64];

        // DirectSound GUID
        [FieldOffset(0)]
        public fixed byte dsound[16];

        // WinMM device ID
        [FieldOffset(0)]
        public uint winmm;

        // ALSA device name
        [FieldOffset(0)]
        public fixed byte alsa[256];

        // PulseAudio device name
        [FieldOffset(0)]
        public fixed byte pulse[256];

        // Core Audio device name (macOS)
        [FieldOffset(0)]
        public fixed byte coreaudio[256];

        // AAudio device ID (Android)
        [FieldOffset(0)]
        public int aaudio;

        // Null backend
        [FieldOffset(0)]
        public int nullbackend;
    }

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ma_device_info
    {
        public ma_device_id id;
        public fixed byte name[256];
        public uint isDefault;
        public uint nativeDataFormatCount;
        public fixed byte nativeDataFormats[1024]; // Reserve space for native format array
    }

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ma_device_config
    {
        public ma_device_type deviceType;
        public uint sampleRate;
        public uint periodSizeInFrames;
        public uint periodSizeInMilliseconds;
        public uint periods;
        public uint performanceProfile;  // ma_performance_profile enum
        public byte noPreSilencedOutputBuffer;
        public byte noClip;
        public byte noDisableDenormals;
        public byte noFixedSizedCallback;
        public nint dataCallback;
        public nint notificationCallback;
        public nint stopCallback;
        public void* pUserData;
        public ma_resampler_config resampling;
        public PlaybackDescriptor playback;
        public CaptureDescriptor capture;

        // Backend-specific configs - define explicitly for correct struct layout
        public WasapiConfig wasapi;
        public AlsaConfig alsa;
        public PulseConfig pulse;
        public CoreAudioConfig coreaudio;  // Critical for macOS
        public OpenSLConfig opensl;
        public AAudioConfig aaudio;

        [StructLayout(LayoutKind.Sequential)]
        public struct PlaybackDescriptor
        {
            public ma_device_id* pDeviceID;
            public ma_format format;
            public uint channels;
            public void* pChannelMap;
            public uint channelMixMode;
            public uint calculateLFEFromSpatialChannels;
            public uint shareMode;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct CaptureDescriptor
        {
            public ma_device_id* pDeviceID;
            public ma_format format;
            public uint channels;
            public void* pChannelMap;
            public uint channelMixMode;
            public uint calculateLFEFromSpatialChannels;
            public uint shareMode;
        }

        [StructLayout(LayoutKind.Sequential)]
        public unsafe struct ma_resampler_config
        {
            public ma_format format;
            public uint channels;
            public uint sampleRateIn;
            public uint sampleRateOut;
            public uint algorithm;  // ma_resample_algorithm
            public void* pBackendVTable;
            public void* pBackendUserData;
            public LinearConfig linear;

            [StructLayout(LayoutKind.Sequential)]
            public struct LinearConfig
            {
                public uint lpfOrder;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct WasapiConfig
        {
            public uint usage;
            public byte noAutoConvertSRC;
            public byte noDefaultQualitySRC;
            public byte noAutoStreamRouting;
            public byte noHardwareOffloading;
            public uint loopbackProcessID;
            public byte loopbackProcessExclude;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct AlsaConfig
        {
            public uint noMMap;
            public uint noAutoFormat;
            public uint noAutoChannels;
            public uint noAutoResample;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct PulseConfig
        {
            public sbyte* pStreamNamePlayback;
            public sbyte* pStreamNameCapture;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct CoreAudioConfig
        {
            public uint allowNominalSampleRateChange;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct OpenSLConfig
        {
            public uint streamType;
            public uint recordingPreset;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct AAudioConfig
        {
            public uint usage;
            public uint contentType;
            public uint inputPreset;
            public uint noAutoStartAfterReroute;
        }
    }

// Opaque structs - treated as black boxes with conservative size allocations
[StructLayout(LayoutKind.Sequential, Size = 32768)]
internal unsafe struct ma_context
{
    // Opaque - 32KB should be sufficient for all platforms
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ma_device
{
    // We only define the fields up to pUserData so we can access it at the correct offset
    // The rest of the struct is platform-specific and we don't need to access it
    public ma_context* pContext;
    public ma_device_type type;
    public uint sampleRate;
    public int state;  // ma_device_state
    public nint onData;  // ma_device_data_proc
    public nint onNotification;  // ma_device_notification_proc
    public nint onStop;  // ma_stop_proc
    public void* pUserData;

    // Pad the rest of the struct to match miniaudio's actual size
    // MiniaudioSharp's ma_device is approximately 10KB, we use 16KB to be safe
    // This prevents crashes when ma_device_init writes to fields beyond pUserData
    private fixed byte _padding[16384 - 56];  // 16KB total - 56 bytes for fields above
}

/// <summary>
/// Minimal P/Invoke bindings for miniaudio library.
/// Only includes the subset of functions required by GraphAudio.Realtime.
/// </summary>
internal static unsafe partial class Miniaudio
{
    private const string LibraryName = "miniaudio";

    [LibraryImport(LibraryName, EntryPoint = "ma_context_init")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    public static partial ma_result ma_context_init(
        uint* backends,
        uint backendCount,
        void* pConfig,
        ma_context* pContext);

    [LibraryImport(LibraryName, EntryPoint = "ma_context_uninit")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    public static partial ma_result ma_context_uninit(ma_context* pContext);

    [LibraryImport(LibraryName, EntryPoint = "ma_context_get_devices")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    public static partial ma_result ma_context_get_devices(
        ma_context* pContext,
        ma_device_info** ppPlaybackDeviceInfos,
        uint* pPlaybackDeviceCount,
        ma_device_info** ppCaptureDeviceInfos,
        uint* pCaptureDeviceCount);

    [LibraryImport(LibraryName, EntryPoint = "ma_device_config_init")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    public static partial ma_device_config ma_device_config_init(ma_device_type deviceType);

    [LibraryImport(LibraryName, EntryPoint = "ma_device_init")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    public static partial ma_result ma_device_init(
        ma_context* pContext,
        ma_device_config* pConfig,
        ma_device* pDevice);

    [LibraryImport(LibraryName, EntryPoint = "ma_device_start")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    public static partial ma_result ma_device_start(ma_device* pDevice);

    [LibraryImport(LibraryName, EntryPoint = "ma_device_stop")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    public static partial ma_result ma_device_stop(ma_device* pDevice);

    [LibraryImport(LibraryName, EntryPoint = "ma_device_uninit")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    public static partial void ma_device_uninit(ma_device* pDevice);
}
