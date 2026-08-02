# LensFlow MVP

LensFlow is a local Windows x64 screen recorder with mouse-click driven camera movement and lightweight editing.

## Included in this MVP

- Record the primary display or a selected window.
- Capture system audio and an optional microphone.
- Pause, resume, and stop recording.
- Capture timestamped mouse movement and clicks.
- Generate editable click-focus shots that smoothly follow the mouse.
- Preview the camera effect, set trim in/out points, change zoom, and delete shots.
- Store projects locally in SQLite plus a readable JSON snapshot.
- Export the trimmed project and camera motion to H.264/AAC MP4 with FFmpeg.

Projects are saved under `%USERPROFILE%\Videos\LensFlow`.

## Requirements

- Windows 10 2004 or newer, x64.
- .NET 10 SDK, x64.
- Microsoft Visual C++ 2015-2022 Redistributable, x64.
- Media Foundation (included in standard Windows editions; Windows N requires the Media Feature Pack).

ARM64, HDR fidelity, 4K60, protected/DRM content, secure desktop, lock screen, and remote/headless sessions are outside the MVP support scope.

## Run locally

```powershell
.\scripts\setup-ffmpeg.ps1
.\scripts\run.ps1
```

FFmpeg is only required for export. The setup script installs a project-local LGPL Windows x64 build; it does not modify the system `PATH`.

## Validate

```powershell
dotnet test .\tests\LensFlow.Core.Tests\LensFlow.Core.Tests.csproj
dotnet build .\src\LensFlow.App\LensFlow.App.csproj -c Debug -p:Platform=x64
```

## Known MVP limitations

- The recording source is encoded directly to fragmented MP4. A production release should move capture into a separate process and use shorter recoverable media segments.
- System and microphone audio are mixed by the recording backend rather than retained as independently editable tracks.
- The application window is excluded from display capture; protected windows may still appear blank by design.
- 1080p60 depends on the installed GPU driver and Media Foundation encoder. Use 30 FPS if the device cannot sustain 60 FPS.
