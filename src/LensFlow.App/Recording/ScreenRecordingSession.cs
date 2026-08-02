using System.Collections.Concurrent;
using System.IO;
using LensFlow.Core.Editing;
using LensFlow.Core.Models;
using LensFlow.Core.Persistence;
using ScreenRecorderLib;

namespace LensFlow.App.Recording;

public sealed record RecordingRequest(
    CaptureSourceOption Source,
    bool CaptureSystemAudio,
    bool CaptureMicrophone,
    int FrameRate,
    nint ApplicationWindow);

public sealed class ScreenRecordingSession : IDisposable
{
    private readonly Recorder _recorder;
    private readonly RecordingSessionClock _clock;
    private readonly MouseCaptureService _mouseCapture;
    private readonly ProjectRepository _repository;
    private readonly TaskCompletionSource<LensFlowProject> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ConcurrentQueue<string> _diagnostics = new();
    private bool _stopping;
    private bool _paused;
    private bool _disposed;

    private ScreenRecordingSession(
        Recorder recorder,
        RecordingSessionClock clock,
        MouseCaptureService mouseCapture,
        ProjectRepository repository,
        LensFlowProject project)
    {
        _recorder = recorder;
        _clock = clock;
        _mouseCapture = mouseCapture;
        _repository = repository;
        Project = project;
    }

    public LensFlowProject Project { get; }
    public long ElapsedMilliseconds => _clock.ElapsedMilliseconds;
    public bool IsPaused => _paused;
    public IReadOnlyCollection<string> Diagnostics => _diagnostics.ToArray();

    public event EventHandler<RecorderStatus>? StatusChanged;

    public static ScreenRecordingSession Start(
        RecordingRequest request,
        ProjectRepository repository)
    {
        var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var name = $"Recording-{timestamp}";
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
            "LensFlow",
            name);
        Directory.CreateDirectory(Path.Combine(root, "media"));

        var project = LensFlowProject.Create(
            name,
            root,
            request.Source.Width,
            request.Source.Height,
            request.FrameRate);

        var options = new RecorderOptions
        {
            SourceOptions = new SourceOptions
            {
                RecordingSources = [request.Source.CreateRecordingSource()]
            },
            OutputOptions = new OutputOptions
            {
                RecorderMode = RecorderMode.Video
            },
            AudioOptions = new AudioOptions
            {
                IsAudioEnabled = request.CaptureSystemAudio || request.CaptureMicrophone,
                IsOutputDeviceEnabled = request.CaptureSystemAudio,
                IsInputDeviceEnabled = request.CaptureMicrophone,
                InputVolume = request.CaptureSystemAudio && request.CaptureMicrophone ? 0.6f : 1f,
                OutputVolume = request.CaptureSystemAudio && request.CaptureMicrophone ? 0.6f : 1f,
                Bitrate = AudioBitrate.bitrate_192kbps,
                Channels = AudioChannels.Stereo
            },
            VideoEncoderOptions = new VideoEncoderOptions
            {
                Encoder = new H264VideoEncoder
                {
                    BitrateMode = H264BitrateControlMode.UnconstrainedVBR,
                    EncoderProfile = H264Profile.High
                },
                Bitrate = request.FrameRate >= 60 ? 20_000_000 : 12_000_000,
                Framerate = request.FrameRate,
                IsFixedFramerate = true,
                IsFragmentedMp4Enabled = true,
                IsHardwareEncodingEnabled = true,
                IsLowLatencyEnabled = false,
                IsThrottlingDisabled = false
            },
            MouseOptions = new MouseOptions
            {
                IsMousePointerEnabled = true,
                IsMouseClicksDetected = true,
                MouseClickDetectionMode = MouseDetectionMode.Polling,
                MouseClickDetectionDuration = 180,
                MouseClickDetectionRadius = 18,
                MouseLeftClickDetectionColor = "#4F8CFF",
                MouseRightClickDetectionColor = "#FFB84D"
            },
            LogOptions = new LogOptions
            {
                IsLogEnabled = true,
                LogFilePath = Path.Combine(root, "recorder.log"),
                LogSeverityLevel = ScreenRecorderLib.LogLevel.Info
            }
        };

        var recorder = Recorder.CreateRecorder(options);
        var clock = new RecordingSessionClock();
        var mouseCapture = new MouseCaptureService(request.Source, () => clock.ElapsedMilliseconds);
        var session = new ScreenRecordingSession(recorder, clock, mouseCapture, repository, project);
        session.AttachEvents();

        try
        {
            if (request.ApplicationWindow != nint.Zero)
            {
                Recorder.SetExcludeFromCapture(request.ApplicationWindow, true);
            }

            clock.Start();
            mouseCapture.Start();
            recorder.Record(project.MediaPath);
            return session;
        }
        catch
        {
            mouseCapture.Dispose();
            recorder.Dispose();
            throw;
        }
    }

    public void Pause()
    {
        if (_paused || _stopping)
        {
            return;
        }

        _recorder.Pause();
        _clock.Pause();
        _mouseCapture.SetPaused(true);
        _paused = true;
    }

    public void Resume()
    {
        if (!_paused || _stopping)
        {
            return;
        }

        _recorder.Resume();
        _clock.Resume();
        _mouseCapture.SetPaused(false);
        _paused = false;
    }

    public async Task<LensFlowProject> StopAsync(CancellationToken cancellationToken = default)
    {
        if (!_stopping)
        {
            _stopping = true;
            _clock.Stop();
            _mouseCapture.Stop();
            _recorder.Stop();
        }

        return await _completion.Task.WaitAsync(TimeSpan.FromSeconds(45), cancellationToken);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _mouseCapture.Dispose();
        _recorder.Dispose();
    }

    private void AttachEvents()
    {
        _recorder.OnStatusChanged += (_, args) =>
        {
            _diagnostics.Enqueue(args.Status.ToString());
            StatusChanged?.Invoke(this, args.Status);
        };
        _recorder.OnRecordingComplete += (_, _) => _ = CompleteAsync();
        _recorder.OnRecordingFailed += (_, args) =>
        {
            _completion.TrySetException(new InvalidOperationException(args.Error));
            Dispose();
        };
    }

    private async Task CompleteAsync()
    {
        try
        {
            Project.DurationMs = Math.Max(1, _clock.ElapsedMilliseconds);
            Project.Edit.TrimEndMs = Project.DurationMs;
            Project.MouseSamples = _mouseCapture.Samples.OrderBy(sample => sample.TimeMs).ToList();
            Project.VideoSegments =
            [
                new VideoSegment
                {
                    StartMs = 0,
                    EndMs = Project.DurationMs
                }
            ];
            Project.CameraShots = new AutoDirector()
                .Generate(Project.MouseSamples, Project.DurationMs, Project.FrameRate)
                .ToList();
            await _repository.SaveAsync(Project);
            _completion.TrySetResult(Project);
        }
        catch (Exception exception)
        {
            _completion.TrySetException(exception);
        }
        finally
        {
            Dispose();
        }
    }
}
