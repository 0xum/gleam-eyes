# Gleam Eyes

Desktop application (.NET 8 + Avalonia) for real-time webcam capture, overlay composition, and frame-processing plugins.

This repository is organized into three main scopes:

- **Engine (`src/Gleam.Engine`)**: capture, frame pipeline, and plugin infrastructure.
- **UI (`src/Gleam.UI`)**: Avalonia interface, live preview, debug/plugins/logs tabs.
- **Gestures plugin (`src/Gleam.Gestures`)**: external MediaPipe-based hand landmark and gesture overlay plugin.

---

## Architecture overview

Main runtime flow:

1. UI starts camera capture through `CameraFactory` (`FlashCapCameraCapture`).
2. Each `RawFrame` is pushed to `FramePipeline` (bounded channel with `DropOldest`).
3. `MainWindowViewModel` consumes frames and calls `PluginHandler.OnUpdateCapture`.
4. Plugins write primitives into `OverlayScene` (`line`, `rectangle`, `circle`, `polyline`).
5. UI composes frame + overlays in `OverlayBitmapComposer` (SkiaSharp) and renders the preview.

Plugin lifecycle callbacks:

- `OnStartCapture(PluginStartContext)`
- `OnUpdateCapture(PluginFrameContext)`
- `OnEndCapture(PluginEndContext)`

---

## Project structure

### 1) Gleam.Engine

Core responsibilities:

- Camera abstraction via `ICameraCapture`.
- Current implementation with `FlashCapCameraCapture`.
- Frame transport through `FramePipeline` (bounded async channel).
- Generic overlay model in `Gleam.Engine.Overlays`.
- Reflection-based plugin discovery/loading in `PluginHandler`.

Important notes:

- `PluginHandler` loads plugins:
  - from the main assembly;
  - and from DLLs under `./plugins` (next to the executable), including subfolders.
- Managed and native dependency resolution is implemented for plugins (including `mediapipe_c`).
- Built-in overlay modules are exposed as plugins using `OverlayModulePluginAdapter`:
  - `BasicGridOverlayModule` (grid);
  - `AnimatedShapesOverlayModule` (animated shapes).

### 2) Gleam.UI

Core responsibilities:

- Main window (`MainWindow`) with toolbar and tab-based sidebar.
- `MainWindowViewModel` orchestrates capture, frame consumption, plugins, and runtime metrics.
- Sidebar tabs:
  - **Debug**: FPS, current frame info, and performance telemetry;
  - **Plugins**: plugin list with `IsEnabled` toggle;
  - **Logs**: `PluginLogger` output with copy button;
  - **Settings**: native UI toggles (debug/grid/animated).
- Plugin settings tabs are created dynamically when a plugin exposes `CreateSettingsView()`.

Rendering:

- `OverlayBitmapComposer` converts/copies frame data to BGRA and draws overlays via `OverlayRenderer`.
- Supported frame formats in the composer: `BGRA32`, `ARGB32`, `RGB32`, `RGB24` (with decode fallback for encoded payloads).

### 3) Gleam.Gestures (external plugin)

Plugin: `GestureProcessingPlugin` (`PluginMetadata: GestureProcessing v1.1.0`).

Core responsibilities:

- Preprocess frames for inference (`MediaPipeFramePreprocessor`):
  - normalize to `RGB24`;
  - optional downscale up to `640x480`.
- Run MediaPipe inference (`MediaPipeGraphRunner`) for hand landmarks.
- Maintain an auxiliary debug window (`MediaPipePreviewWindowHost`) showing inference input frames.
- Draw landmark overlays and derived indicators:
  - hand skeleton connections;
  - hand-to-hand distance (color-coded by proximity);
  - palm openness circle.

Resilience features in the plugin:

- tolerance for cycles with no returned detections;
- queue/in-flight control when integrating with `OutputStreamPoller`;
- MediaPipe asset fallback resolution (local path, `Dependencies`, runtime app folders);
- asset copy/link into runtime layout when needed.

---

## How to run

Requirements:

- .NET SDK 8.0
- Accessible webcam
- x64 environment (Debug projects are configured with `PlatformTarget=x64`)

Commands:

```bash
dotnet build Gleam.sln
dotnet run --project src/Gleam.UI/Gleam.Ui.csproj
```

At runtime:

1. Click **Start** to begin capture and processing pipeline.
2. Monitor status/FPS/performance in the footer and **Debug** tab.
3. Enable/disable plugins in the **Plugins** tab.
4. Use **Open Gesture Debug** (when available) to open the gestures debug window.

---

## Runtime plugin layout

The app searches for plugins in:

- `build/bin/net8.0/plugins/**` (current debug layout)
- `plugins` folder relative to the executable (`AppContext.BaseDirectory/plugins`)

`Gleam.Gestures` is already configured to output into debug plugin layout:

- output to `build/bin/net8.0/plugins/gestures/`
- copy MediaPipe managed dependencies
- copy native binaries (`libmediapipe_c.so` / `mediapipe_c.dll`)
- copy MediaPipe assets `mediapipe/modules/**`

---

## Creating a new plugin

Implement `IFrameProcessingPlugin` and annotate the class with `PluginMetadataAttribute`:

```csharp
[PluginMetadata("MyPlugin", "Plugin description", "1.0.0")]
public sealed class MyPlugin : IFrameProcessingPlugin
{
    public bool IsEnabled { get; set; } = true;

    public void OnStartCapture(PluginStartContext context) { }

    public void OnUpdateCapture(PluginFrameContext context)
    {
        // Frame processing
        // context.Scene.Add(...); // optional overlay
    }

    public void OnEndCapture(PluginEndContext context) { }
}
```

For external plugins, copy the plugin DLL and its dependencies into the runtime `plugins` folder.

---

## Technical notes

- Runtime logging is centralized through `PluginLogger` (in-memory event stream consumed by the UI).
- `FramePipeline` uses a single-frame buffer with `DropOldest` to prioritize low latency.
- The project already contains working FlashCap and MediaPipe integration (not a placeholder/TODO state anymore).