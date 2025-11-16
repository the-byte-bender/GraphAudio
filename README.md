# GraphAudio

High-performance graph-based audio engine for .NET inspired by Web Audio API.

## Packages

-   [GraphAudio.Core](https://www.nuget.org/packages/GraphAudio.Core): the core audio processing engine and pure nodes
-   [GraphAudio.IO](https://www.nuget.org/packages/GraphAudio.IO): provides audio decoding I/O, as well as `AudioDecoderStreamNode` using libsndfile
-   [GraphAudio.Kit](https://www.nuget.org/packages/GraphAudio.Kit): High-level 3d audio toolkit for games
-   [GraphAudio.Realtime](https://www.nuget.org/packages/GraphAudio.Realtime): Provides the `RealtimeAudioContext` using miniaudio
-   [GraphAudio.SteamAudio](https://www.nuget.org/packages/GraphAudio.SteamAudio): Provides Spatial audio and related nodes using Steam Audio

## Development Setup

### Initial Setup

1. Pack all projects to the local feed:

    ```bash
    ./pack-local.sh
    ```

2. Restore packages:
    ```bash
    dotnet restore
    ```

### Publishing to NuGet

```bash
dotnet pack -c Release -o ./publish
dotnet nuget push ./publish/*.nupkg -s https://api.nuget.org/v3/index.json -k YOUR_API_KEY
```
