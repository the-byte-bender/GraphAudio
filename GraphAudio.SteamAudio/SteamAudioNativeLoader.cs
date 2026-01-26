using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;

namespace GraphAudio.SteamAudio;

/// <summary>
/// Handles native library loading for SteamAudio on macOS where the NuGet package
/// uses osx-universal RID instead of osx-arm64/osx-x64.
/// </summary>
internal static class SteamAudioNativeLoader
{
    private static bool _isRegistered = false;
    private static readonly object _lock = new object();

    /// <summary>
    /// Registers the DllImport resolver for SteamAudio native libraries.
    /// </summary>
    internal static void EnsureLoaded()
    {
        lock (_lock)
        {
            if (_isRegistered) return;

            var steamAudioAssembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "SteamAudio.NET");

            if (steamAudioAssembly != null)
            {
                NativeLibrary.SetDllImportResolver(steamAudioAssembly, DllImportResolver);
            }

            _isRegistered = true;
        }
    }

    private static IntPtr DllImportResolver(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (!libraryName.Equals("phonon.dll", StringComparison.OrdinalIgnoreCase) &&
            !libraryName.Equals("phonon", StringComparison.OrdinalIgnoreCase))
        {
            return IntPtr.Zero;
        }

        // Try base directory first (for .app bundles where dylibs are in MacOS/)
        var baseDirectoryPath = GetBaseDirectoryPath();
        if (baseDirectoryPath != null && File.Exists(baseDirectoryPath))
        {
            if (NativeLibrary.TryLoad(baseDirectoryPath, out var handle))
                return handle;
        }

        var archSpecificPath = GetArchitectureSpecificPath();
        if (archSpecificPath != null && File.Exists(archSpecificPath))
        {
            if (NativeLibrary.TryLoad(archSpecificPath, out var handle))
                return handle;
        }

        var universalPath = GetUniversalPath();
        if (universalPath != null && File.Exists(universalPath))
        {
            if (NativeLibrary.TryLoad(universalPath, out var handle))
                return handle;
        }

        return IntPtr.Zero;
    }

    private static string? GetBaseDirectoryPath()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return null;

        return Path.Combine(AppContext.BaseDirectory, "libphonon.dylib");
    }

    private static string? GetArchitectureSpecificPath()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return null;

        var baseDir = AppContext.BaseDirectory;
        var arch = RuntimeInformation.ProcessArchitecture;
        var rid = arch == Architecture.Arm64 ? "osx-arm64" : "osx-x64";

        return Path.Combine(baseDir, "runtimes", rid, "native", "libphonon.dylib");
    }

    private static string? GetUniversalPath()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return null;

        return Path.Combine(AppContext.BaseDirectory, "runtimes", "osx-universal", "native", "libphonon.dylib");
    }
}
