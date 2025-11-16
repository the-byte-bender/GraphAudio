using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GraphAudio.Core;
using GraphAudio.IO;
using GraphAudio.Nodes;

namespace GraphAudio.Kit.DataProviders;

/// <summary>
/// Defines a data provider that can retrieve audio file streams.
/// </summary>
public interface IDataProvider
{
    /// <summary>
    /// Gets a stream for the specified path.
    /// </summary>
    /// <remarks>
    /// The caller is responsible for disposing the returned stream. The stream must support reading and seeking.
    /// </remarks>
    ValueTask<Stream> GetStreamAsync(string path, CancellationToken cancellationToken = default);
}

internal static class DataProviderExtensions
{
    public static async Task<PlayableAudioBuffer> GetPlayableBufferAsync(
        this IDataProvider provider,
        string path,
        CancellationToken cancellationToken = default)
    {
        var stream = await provider.GetStreamAsync(path, cancellationToken).ConfigureAwait(false);
        return await AudioDecoder.LoadFromStreamAsync(stream).ConfigureAwait(false);
    }

    public static async Task<AudioDecoderStreamNode> GetStreamingNodeAsync(
        this IDataProvider provider,
        AudioContextBase context,
        string path,
        int bufferSize = 4096,
        int bufferCount = 3,
        CancellationToken cancellationToken = default)
    {
        var stream = await provider.GetStreamAsync(path, cancellationToken).ConfigureAwait(false);
        return AudioDecoderStreamNode.FromStream(context, stream, bufferSize, bufferCount);
    }
}
