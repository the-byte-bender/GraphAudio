# GraphAudio.Kit

**A high-level, game-focused audio library for .NET**

## Why GraphAudio.Kit?

-   **Play sounds in one line** after initial setup
-   **3D spatial audio** that just works.
-   **Flexible mixing** with hierarchical audio buses.
-   **Effect chains** you can build and modify at runtime
-   **Smart caching** so you're not constantly reloading the same audio files

## Installation

```bash
dotnet add package GraphAudio.Kit
```

## Quick Start

Set up your audio engine once when your game starts:

```csharp
using GraphAudio.Kit;
using GraphAudio.Kit.DataProviders;
using GraphAudio.Realtime;

// Create the engine that manages all your sounds
var engine = new AudioEngine(new RealtimeAudioContext(
    sampleRate: 48000,
    channels: 2,
    bufferSize: 256
));

// Tell it where to find your audio files
engine.DataProvider = new FileSystemDataProvider("path/to/your/audio/folder");

// Start the audio hardware
context.Start();

// That's it! Now you're ready to play sounds.
```

Keep the `engine` alive for the lifetime of your game. When you're done:

```csharp
engine.Dispose();
```

## Playing Your First Sound

Once you've done the setup above, playing a sound is dead simple:

```csharp
// One-shot sounds (fire and forget)
engine.PlayOneShot("sounds/footstep.ogg");

// PlayOneShot also takes a setup callback, which you can use to set up the sounds properties before it plays and the engine owns it.
```

That's it! The sound plays, finishes, and cleans itself up automatically.

## When You Need More Control

For sounds you want to control (music, ambience , etc.), create a `Sound` object:

```csharp
// Load a sound into memory for instant playback
var sound = await engine.CreateBufferedSoundAsync("sounds/music.ogg");

// Control it like you'd expect
sound.Play();
sound.Pause();
sound.Stop();

sound.Gain = 0.5f;           // Volume (0.0 to 1.0)
sound.IsLooping = true;      // Loop it
sound.PlaybackRate = 1.2f;   // Speed it up 20%

// Jump to a specific time
sound.Seek(TimeSpan.FromSeconds(30));
```

### Buffered vs Streaming Sounds

**Buffered sounds** load the entire audio file into memory. They're perfect for short, frequently-played sounds:

```csharp
var click = await engine.CreateBufferedSoundAsync("click.ogg");
```

**Streaming sounds** read audio on-demand. Use these for long files like music.

```csharp
var bgMusic = await engine.CreateStreamingSoundAsync("music/background.mp3");
bgMusic.IsLooping = true;
bgMusic.Play();
```

## 3D Spatial audio

### The Simple Way: Position-Based

For detached sounds in your 3D world, just set their position:

```csharp
var sound = await engine.CreateBufferedSoundAsync(
    "sounds/engine.ogg",
    mixState: SoundMixState.BinoralSpatialized  // Enable 3D audio for that sound.
);

sound.Position = new Vector3(10, 0, 5);  // World position
sound.IsLooping = true;
sound.Play();

// Update the listener (your player/camera) position
engine.SetListener(
    position: playerPosition,
    forward: playerForward,
    up: Vector3.UnitY
);
```

The sound will now pan with hrtf. Call `engine.Update()` in your game loop to keep everything in sync.

### The Better Way: Spatial Anchors

If you have entities that play multiple sounds, Instead of manually updating every sound's position, you attach sounds to an anchor and update the anchor with your game entity's position:

```csharp
// Create an anchor for your car entity
var carAnchor = new SpatialAnchor();

// Attach the engine sound to it
var engineSound = await engine.CreateBufferedSoundAsync(
    "sounds/car_engine.ogg",
    mixState: SoundMixState.BinoralSpatialized
);
engineSound.Anchor = carAnchor;
engineSound.IsLooping = true;
engineSound.Play();

// In your game update loop:
void Update()
{
    // Just update the anchor once and all attached sounds follow automatically!
    carAnchor.Position = carEntity.Transform.Position;

    // Update the engine (you must call this periodically)
    engine.Update();
}
```

You can even use **position offsets** if the sound source isn't exactly at the entity's center:

```csharp
engineSound.Anchor = carAnchor;
engineSound.Position = new Vector3(0, -0.5f, 2);  // Offset from anchor
```

### Advanced: distance Behavior

Control how sounds fade with distance:

```csharp
sound.SetDistanceModel(
    model: SpatialPannerNode.DistanceModelType.Inverse,  // Realistic falloff
    refDistance: 1.0f,      // Distance where volume starts to decrease
    maxDistance: 50.0f,     // Distance where volume reaches minimum
    rolloffFactor: 1.0f     // How quickly volume decreases (higher = faster)
);
```

### Advanced: Directional Sounds

Make sounds emit in a specific direction (like a megaphone or engine exhaust):

```csharp
sound.Orientation = Vector3.UnitX;  // Sound points along X axis

sound.SetCone(
    innerAngle: 45f,    // Full volume within this angle
    outerAngle: 90f,    // Transition to outer volume
    outerGain: 0.3f     // Volume outside the cone (0.0 to 1.0)
);
```

## Audio Buses: Organize Your Sound

Buses let you group and control the gain and effects on multiple sounds at once. Think a `music` bus, a `UI` bus, `gameplay` bus, etc.

```csharp
var musicBus = engine.GetBus("music");

// Attach sounds to buses
var bgMusic = await engine.CreateStreamingSoundAsync("music.mp3", bus: musicBus);
// Control the entire bus
musicBus.Gain = 0.5f;   // All music plays at 50% volume
music.Muted = true;    // Silence all music instantly

// Smooth volume changes
musicBus.Fade(target: 0.2f, duration: 2.0);  // Fade to 20% over 2 seconds
```

### Hierarchical Buses

You can create bus hierarchies using forward slashes:

```csharp
var playerSfx = engine.GetBus("sfx/player");
var enemySfx = engine.GetBus("sfx/enemy");
var uiSfx = engine.GetBus("sfx/ui");

// Adjusting the parent affects all children
var sfxBus = engine.GetBus("sfx");
sfxBus.Gain = 0.5f;  // All sfx/* buses now play at 50% of their individual volume
```

All buses eventually connect to the master bus:

```csharp
engine.MasterBus.Gain = 0.8f;  // Global volume control
```

## Advanced: Effect Chains

Effect chains let you add DSP effects to individual sounds or entire buses. Effects process in the order you add them:

```csharp
var sound = await engine.CreateBufferedSoundAsync("dialogue.ogg");

// Add effects to the sound
var lowpass = new BiQuadFilterNode(engine.Context);
lowpass.Type = BiQuadFilterNode.FilterType.LowPass;
lowpass.Frequency.Value = 800; 

sound.Effects.Add(lowpass);

// You can also add effects on an entire bus!
musicBus.Effects.Add(*);

// You must dispose of effects manually when they're done.
```

You can modify effect chains at runtime:

```csharp
// Remove effects
soundOrBus.Effects.Remove(lowpass);
soundOrBus.Effects.Clear();  // Remove all

// Insert at specific positions
soundOrBus.Effects.Insert(0, something);  // Add at the start of the chain
```

## Preloading for Performance

If you know you'll need certain sounds soon, preload them to avoid slowdowns:

```csharp
// Load multiple sounds in parallel
await engine.PreloadBuffersAsync(new[]
{
    "sounds/footstep_1.ogg",
    "sounds/footstep_2.ogg",
    "sounds/footstep_3.ogg",
    "sounds/jump.ogg"
});

// Now they'll play instantly when needed
engine.PlayOneShot("sounds/footstep_1.ogg", setup: sound => sound.Anchor = playerAnchor);  // No load time!
```

## Fade In/Out

Sounds and buses both support smooth volume transitions:

```csharp
// Fade in when playing
sound.Play(fadeInDuration: 1.5);  // Fade in over 1.5 seconds

// Fade out before stopping
await sound.Stop(fadeOutDuration: 2.0);  // Fade out over 2 seconds, then stop

// Bus fades affect all sounds on the bus
musicBus.Fade(target: 0.0f, duration: 3.0);  // Fade out all music
```

## Advanced: Spatial Blend Controllers

Spatial blend controllers let you customize how sounds transition between 2D and 3D audio based on distance. By default, sounds are fully 3D, but you might want close sounds to feel more "direct":

```csharp
using GraphAudio.Kit.SpatialBlendControllers;

// Make close sounds more direct (easier to hear), far sounds fully spatial
var controller = new LinearSpatialBlendController(
    minDistance: 0f,
    maxDistance: 10f,
    minBlend: 0.3f,   // 30% spatial when close
    maxBlend: 1.0f    // 100% spatial when far
);

sound.SpatialBlendController = controller;
```

You can also set a default controller for all new sounds:

```csharp
Sound.DefaultSpatialBlendController = controller;
```

## Best Practices

1. **Call `engine.Update()` every frame** in your game loop. It handles spatial audio updates and cleans up finished one-shot sounds.

2. **Use `PlayOneShot` for short effects** that fire and forget. Use explicit `Sound` objects when you need long-term control.

3. **Attach sounds to `SpatialAnchor` objects** rather than updating positions manually. It's cleaner and more efficient.

4. **Organize with buses** from the start. Even if you don't need per-category volume or effects now, you will later.

5. **Preload frequently-used sounds**

6. **Use streaming for long audio** (music, ambience, cutscenes...). Use buffered sounds for everything else.

7. **Dispose sounds you're done with** if you hold a reference.

