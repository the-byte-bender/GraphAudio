# GraphAudio

High-performance graph-based audio engine for .NET inspired by Web Audio API.

## Packages

- **GraphAudio.Core**: the core audio processing engine and pure nodes
- **GraphAudio.IO**: provides audio decoding I/O, as well as `AudioDecoderStreamNode` using libsndfile
- **GraphAudio.Realtime**: Provides the `RealtimeAudioContext` using miniaudio
- **GraphAudio.SteamAudio**: Provides Spatial audio and related nodes using Steam Audio

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
