using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace GraphAudio.IO;

internal static unsafe partial class Libsndfile
{
    private const string LibraryName = "sndfile";

    public const int SFM_READ = 0x10;
    public const int SEEK_SET = 0;
    public const int SEEK_CUR = 1;
    public const int SEEK_END = 2;

    [StructLayout(LayoutKind.Sequential)]
    public struct SF_INFO
    {
        public long frames;
        public int samplerate;
        public int channels;
        public int format;
        public int sections;
        public int seekable;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SF_VIRTUAL_IO
    {
        public delegate* unmanaged[Cdecl]<IntPtr, long> get_filelen;
        public delegate* unmanaged[Cdecl]<long, int, IntPtr, long> seek;
        public delegate* unmanaged[Cdecl]<IntPtr, long, IntPtr, long> read;
        public delegate* unmanaged[Cdecl]<IntPtr, long, IntPtr, long> write;
        public delegate* unmanaged[Cdecl]<IntPtr, long> tell;
    }

    [LibraryImport(LibraryName, EntryPoint = "sf_open_virtual")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    public static partial IntPtr sf_open_virtual(
        ref SF_VIRTUAL_IO sfvirtual,
        int mode,
        ref SF_INFO sfinfo,
        IntPtr userData);

    [LibraryImport(LibraryName, EntryPoint = "sf_close")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    public static partial int sf_close(IntPtr sndfile);

    [LibraryImport(LibraryName, EntryPoint = "sf_strerror")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial IntPtr sf_strerror_internal(IntPtr sndfile);

    public static string sf_strerror(IntPtr sndfile)
    {
        IntPtr ptr = sf_strerror_internal(sndfile);
        return Marshal.PtrToStringAnsi(ptr) ?? "Unknown libsndfile error";
    }

    [LibraryImport(LibraryName, EntryPoint = "sf_seek")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    public static partial long sf_seek(IntPtr sndfile, long frames, int whence);

    [LibraryImport(LibraryName, EntryPoint = "sf_readf_short")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    public static partial long sf_readf_short(IntPtr sndfile, Span<short> ptr, long frames);

    [LibraryImport(LibraryName, EntryPoint = "sf_readf_float")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    public static partial long sf_readf_float(IntPtr sndfile, Span<float> ptr, long frames);

}