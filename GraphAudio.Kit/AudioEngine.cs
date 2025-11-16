using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using GraphAudio.Core;
using GraphAudio.Kit.DataProviders;
using GraphAudio.SteamAudio;

namespace GraphAudio.Kit;

/// <summary>
/// The main audio engine class.
/// </summary>
public sealed class AudioEngine : IDisposable
{
    private bool _disposed;
    private readonly Dictionary<string, AudioBus> _buses = new();
    private readonly AudioBus _masterBus;
    private readonly List<Sound> _activeSounds = new();
    private readonly object _soundsLock = new();
    private Vector3 _listenerPosition;
    private Vector3 _listenerForward = new(0, 0, -1);
    private Vector3 _listenerUp = new(0, 1, 0);

    /// <summary>
    /// The audio context used by the engine.
    /// </summary>
    public AudioContextBase Context { get; }

    /// <summary>
    /// The buffer cache.
    /// </summary>
    public AudioBufferCache BufferCache { get; }

    /// <summary>
    /// The data provider for loading audio files.
    /// </summary>
    public IDataProvider? DataProvider { get; set; }

    /// <summary>
    /// The master bus. All audio eventually routes through this bus.
    /// </summary>
    public AudioBus MasterBus => _masterBus;

    /// <summary>
    /// Gets the current listener position in 3D space.
    /// </summary>
    public Vector3 ListenerPosition => _listenerPosition;

    /// <summary>
    /// Gets the current listener forward direction vector.
    /// </summary>
    public Vector3 ListenerForward => _listenerForward;

    /// <summary>
    /// Gets the current listener up direction vector.
    /// </summary>
    public Vector3 ListenerUp => _listenerUp;

    /// <Remarks>
    /// The context will be owned by the audio engine and disposed when the engine is disposed.
    /// </Remarks>
    public AudioEngine(AudioContextBase context, AudioBufferCacheOptions? cacheOptions = null)
    {
        Context = context;
        BufferCache = new AudioBufferCache(cacheOptions ?? new AudioBufferCacheOptions());

        _masterBus = new AudioBus(this, "master", null);
        _buses["master"] = _masterBus;
    }

    ~AudioEngine()
    {
        Dispose(false);
    }

    /// <summary>
    /// Gets or creates a bus by path. Paths use forward slashes for hierarchy (e.g., "sfx", "music/menu").
    /// </summary>
    public AudioBus GetBus(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Bus path cannot be empty.", nameof(path));

        ThrowIfDisposed();

        path = path.Trim().ToLowerInvariant();

        if (_buses.TryGetValue(path, out var existingBus))
            return existingBus;

        if (path == "master")
            return _masterBus;

        var parts = path.Split('/');
        AudioBus? parent = _masterBus;
        string currentPath = "master";

        for (int i = 0; i < parts.Length; i++)
        {
            var part = parts[i].Trim();
            if (string.IsNullOrEmpty(part))
                throw new ArgumentException($"Invalid bus path: {path}", nameof(path));

            currentPath = i == 0 ? part : $"{currentPath}/{part}";

            if (!_buses.TryGetValue(currentPath, out var bus))
            {
                bus = new AudioBus(this, currentPath, parent);
                _buses[currentPath] = bus;
            }

            parent = bus;
        }

        return parent;
    }

    /// <summary>
    /// Checks if a bus exists.
    /// </summary>
    public bool HasBus(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        path = path.Trim().ToLowerInvariant();
        return _buses.ContainsKey(path);
    }

    /// <summary>
    /// Creates a buffered sound from a file path.
    /// </summary>
    public async Task<BufferedSound> CreateBufferedSoundAsync(string path, SoundMixState mixState = SoundMixState.Direct, AudioBus? bus = null, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (DataProvider is null)
            throw new InvalidOperationException("DataProvider must be set before creating sounds.");

        var buffer = await BufferCache.GetOrLoadAsync(path, DataProvider, cancellationToken).ConfigureAwait(false);
        var sound = new BufferedSound(this, buffer, mixState, bus);

        lock (_soundsLock)
        {
            _activeSounds.Add(sound);
        }

        return sound;
    }

    /// <summary>
    /// Creates a streaming sound from a file path.
    /// </summary>
    public async Task<StreamingSound> CreateStreamingSoundAsync(string path, SoundMixState mixState = SoundMixState.Direct, AudioBus? bus = null, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (DataProvider is null)
            throw new InvalidOperationException("DataProvider must be set before creating sounds.");

        var streamNode = await DataProvider.GetStreamingNodeAsync(Context, path, cancellationToken: cancellationToken).ConfigureAwait(false);
        var sound = new StreamingSound(this, streamNode, mixState, bus);

        lock (_soundsLock)
        {
            _activeSounds.Add(sound);
        }

        return sound;
    }

    /// <summary>
    /// Plays a sound as a one-shot effect. The sound is automatically managed and disposed.
    /// </summary>
    public async void PlayOneShot(string path, SoundMixState mixState = SoundMixState.Direct, AudioBus? bus = null, Action<BufferedSound>? setup = null)
    {
        if (DataProvider is null)
        {
            Console.WriteLine($"[AudioEngine] Error: DataProvider is null");
            return;
        }

        try
        {
            var buffer = await BufferCache.GetOrLoadAsync(path, DataProvider, CancellationToken.None).ConfigureAwait(false);
            var sound = new BufferedSound(this, buffer, mixState, bus);
            sound.IsOneShot = true;

            setup?.Invoke(sound);

            lock (_soundsLock)
            {
                _activeSounds.Add(sound);
            }

            sound.Play();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AudioEngine] Error in PlayBufferedOneShot: {ex.Message}");
            Console.WriteLine($"[AudioEngine] Stack trace: {ex.StackTrace}");
        }
    }

    /// <summary>
    /// Updates all active sounds. This should be called regularly (e.g., once per frame).
    /// </summary>
    public void Update()
    {
        lock (_soundsLock)
        {
            for (int i = _activeSounds.Count - 1; i >= 0; i--)
            {
                var sound = _activeSounds[i];

                if (sound.IsDisposed)
                {
                    _activeSounds.RemoveAt(i);
                    continue;
                }

                sound.Update();

                if (sound.IsOneShot && !sound.IsPlaying && !sound.IsLooping)
                {
                    sound.Dispose();
                    _activeSounds.RemoveAt(i);
                }
            }
        }
    }

    /// <summary>
    /// Preloads multiple audio buffers into the cache.
    /// </summary>
    public async Task PreloadBuffersAsync(string[] paths, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (DataProvider is null)
            throw new InvalidOperationException("DataProvider must be set before loading buffers.");

        var tasks = new Task<PlayableAudioBuffer>[paths.Length];
        for (int i = 0; i < paths.Length; i++)
        {
            tasks[i] = BufferCache.GetOrLoadAsync(paths[i], DataProvider, cancellationToken);
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    /// <summary>
    /// Sets the listener position and orientation.
    /// </summary>
    public void SetListener(Vector3 position, Vector3 forward, Vector3 up)
    {
        ThrowIfDisposed();

        _listenerPosition = position;
        _listenerForward = forward;
        _listenerUp = up;

        Context.SetListener(position, forward, up);
    }

    /// <summary>
    /// Releases all resources used by the AudioEngine.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            lock (_soundsLock)
            {
                foreach (var sound in _activeSounds)
                {
                    sound.Dispose();
                }
                _activeSounds.Clear();
            }

            foreach (var bus in _buses.Values)
            {
                bus.Disconnect();
            }
            _buses.Clear();

            Context.Dispose();
        }

        _disposed = true;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(AudioEngine));
        }
    }
}
