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

/// <summary>
/// 一套「捕获 API + 编码器」组合。不同机器（混合显卡、虚拟机、无硬件编码器的老 GPU）
/// 支持的组合不一样，启动时按顺序试，直到有一套真正进入录制状态。
/// </summary>
internal sealed record RecorderProfile(RecorderApi Api, bool HardwareEncoding, H264Profile EncoderProfile)
{
    public string Description =>
        $"{(Api == RecorderApi.WindowsGraphicsCapture ? "Windows Graphics Capture" : "Desktop Duplication")}" +
        $" · {(HardwareEncoding ? "硬件编码" : "软件编码")} · H.264 {EncoderProfile}";
}

public sealed class ScreenRecordingSession : IDisposable
{
    /// <summary>
    /// 等待录制器进入 Recording 状态（或报错）的时间上限。
    /// DXGI/Media Foundation 的不支持类错误都在几十毫秒内就会回调，这里留足余量。
    /// </summary>
    private static readonly TimeSpan StartupProbeTimeout = TimeSpan.FromSeconds(4);

    private readonly Recorder _recorder;
    private readonly RecordingSessionClock _clock;
    private readonly MouseCaptureService _mouseCapture;
    private readonly ProjectRepository _repository;
    private readonly TaskCompletionSource<LensFlowProject> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ConcurrentQueue<string> _diagnostics = new();
    private volatile string? _failureMessage;
    private bool _stopping;
    private bool _paused;
    private bool _disposed;

    private ScreenRecordingSession(
        Recorder recorder,
        RecordingSessionClock clock,
        MouseCaptureService mouseCapture,
        ProjectRepository repository,
        LensFlowProject project,
        string activeProfileDescription)
    {
        _recorder = recorder;
        _clock = clock;
        _mouseCapture = mouseCapture;
        _repository = repository;
        Project = project;
        ActiveProfileDescription = activeProfileDescription;
    }

    public LensFlowProject Project { get; }
    public string ActiveProfileDescription { get; }
    public long ElapsedMilliseconds => _clock.ElapsedMilliseconds;
    public bool IsPaused => _paused;
    public IReadOnlyCollection<string> Diagnostics => _diagnostics.ToArray();

    public event EventHandler<RecorderStatus>? StatusChanged;

    /// <summary>录制过程中（而非停止时）失败，让 UI 能立刻提示并复位。</summary>
    public event EventHandler<string>? RecordingFailed;

    public static async Task<ScreenRecordingSession> StartAsync(
        RecordingRequest request,
        ProjectRepository repository,
        CancellationToken cancellationToken = default)
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

        if (request.ApplicationWindow != nint.Zero)
        {
            Recorder.SetExcludeFromCapture(request.ApplicationWindow, true);
        }

        var failures = new List<string>();
        var attempt = 0;
        foreach (var profile in BuildProfiles(request.Source.Kind))
        {
            cancellationToken.ThrowIfCancellationRequested();

            // 每次尝试写各自的文件：启动失败的 recorder 会把输出文件锁住一段时间
            // （recorder.log: "Output file is still locked after maximum retries"），
            // 复用同一个路径会让后续所有回退方案连带失败。
            attempt++;
            project.MediaFileName = attempt == 1 ? "source.mp4" : $"source-{attempt}.mp4";

            var recorder = Recorder.CreateRecorder(BuildOptions(request, root, profile));
            var clock = new RecordingSessionClock();
            var mouseCapture = new MouseCaptureService(request.Source, () => clock.ElapsedMilliseconds);
            var session = new ScreenRecordingSession(
                recorder, clock, mouseCapture, repository, project, profile.Description);

            // 先挂上正式的事件处理器，再启动探测。否则「进入 Recording 之后立刻失败」
            // 的事件会掉进两次订阅之间的空档里，直到用户点停止才暴露出来。
            session.AttachEvents();

            // 不用 ConfigureAwait(false)：让整个循环留在 WPF 的 STA 消息泵线程上，
            // 与原先在 UI 线程创建 Recorder 的行为保持一致（WGC/COM 依赖消息泵）。
            var error = await session.TryStartAsync(project.MediaPath, cancellationToken);

            // 探测超时会按成功处理，但失败事件可能在超时之后才到达，
            // 此时 session 已经自我释放；必须再确认一次才能真正启用它。
            error ??= session._failureMessage;

            if (error is null)
            {
                try
                {
                    clock.Start();
                    mouseCapture.Start();
                    return session;
                }
                catch (Exception exception)
                {
                    // 极小概率：失败事件恰好插在上面的检查与启用之间。
                    error = session._failureMessage ?? exception.Message;
                }
            }

            failures.Add($"· {profile.Description}：{error}");
            session.DiscardFailedAttempt();
        }

        throw new InvalidOperationException(
            "当前系统不支持任何可用的屏幕录制配置，已尝试以下组合：" +
            Environment.NewLine +
            string.Join(Environment.NewLine, failures) +
            Environment.NewLine +
            $"详细日志：{Path.Combine(root, "recorder.log")}");
    }

    /// <summary>
    /// 启动录制器并等待它真正进入 Recording 状态。返回 null 表示成功，否则返回错误信息。
    /// </summary>
    private async Task<string?> TryStartAsync(string mediaPath, CancellationToken cancellationToken)
    {
        var probe = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnFailed(object? sender, RecordingFailedEventArgs args) =>
            probe.TrySetResult(string.IsNullOrWhiteSpace(args.Error) ? "录制器报告了未知错误。" : args.Error);

        void OnStatus(object? sender, RecordingStatusEventArgs args)
        {
            if (args.Status == RecorderStatus.Recording)
            {
                probe.TrySetResult(null);
            }
        }

        _recorder.OnRecordingFailed += OnFailed;
        _recorder.OnStatusChanged += OnStatus;
        try
        {
            _recorder.Record(mediaPath);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var settled = await Task.WhenAny(probe.Task, Task.Delay(StartupProbeTimeout, timeout.Token))
                .ConfigureAwait(false);
            timeout.Cancel();
            cancellationToken.ThrowIfCancellationRequested();

            // 超时未收到任何回调时按成功处理：真正的「不支持」类错误都是几毫秒内就回调的，
            // 继续等待只会平白拖慢启动。
            return settled == probe.Task ? await probe.Task.ConfigureAwait(false) : null;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return exception.Message;
        }
        finally
        {
            _recorder.OnRecordingFailed -= OnFailed;
            _recorder.OnStatusChanged -= OnStatus;
        }
    }

    private static IEnumerable<RecorderProfile> BuildProfiles(CaptureSourceKind kind)
    {
        // Microsoft 的软件 H.264 编码器只支持 Baseline/Main，所以软件回退用 Main。
        if (kind == CaptureSourceKind.Window)
        {
            // 窗口捕获只能走 Windows Graphics Capture。
            yield return new RecorderProfile(RecorderApi.WindowsGraphicsCapture, true, H264Profile.High);
            yield return new RecorderProfile(RecorderApi.WindowsGraphicsCapture, false, H264Profile.Main);
            yield break;
        }

        yield return new RecorderProfile(RecorderApi.DesktopDuplication, true, H264Profile.High);
        yield return new RecorderProfile(RecorderApi.WindowsGraphicsCapture, true, H264Profile.High);
        yield return new RecorderProfile(RecorderApi.DesktopDuplication, false, H264Profile.Main);
        yield return new RecorderProfile(RecorderApi.WindowsGraphicsCapture, false, H264Profile.Main);
    }

    private static RecorderOptions BuildOptions(RecordingRequest request, string root, RecorderProfile profile)
    {
        return new RecorderOptions
        {
            SourceOptions = new SourceOptions
            {
                RecordingSources = [request.Source.CreateRecordingSource(profile.Api)]
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
                    EncoderProfile = profile.EncoderProfile
                },
                Bitrate = request.FrameRate >= 60 ? 20_000_000 : 12_000_000,
                Framerate = request.FrameRate,
                IsFixedFramerate = true,
                IsFragmentedMp4Enabled = true,
                IsHardwareEncodingEnabled = profile.HardwareEncoding,
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

            // 录制已经失败时 recorder 已被释放，再调用 Stop 只会抛出 ObjectDisposedException，
            // 掩盖掉真正的失败原因。
            if (!_completion.Task.IsCompleted)
            {
                _recorder.Stop();
            }
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

    /// <summary>
    /// 丢弃一次启动失败的尝试。除释放资源外还要「观察」_completion 上的异常，
    /// 否则它会在 GC 时变成 UnobservedTaskException。
    /// </summary>
    private void DiscardFailedAttempt()
    {
        _stopping = true;
        Dispose();
        _completion.TrySetCanceled();
        _ = _completion.Task.ContinueWith(
            task => _ = task.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
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
            var message = string.IsNullOrWhiteSpace(args.Error) ? "录制器报告了未知错误。" : args.Error;
            _failureMessage = message;
            _diagnostics.Enqueue($"Failed: {message}");
            _completion.TrySetException(new InvalidOperationException(message));
            _clock.Stop();
            _mouseCapture.Stop();

            // 录制中途失败时立刻通知 UI，而不是等用户点「停止并编辑」才暴露。
            if (!_stopping)
            {
                RecordingFailed?.Invoke(this, message);
            }

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
            RemoveAbandonedAttemptFiles();
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

    /// <summary>清掉启动回退过程中留下的空文件，只保留真正被录制的那一个。</summary>
    private void RemoveAbandonedAttemptFiles()
    {
        try
        {
            var mediaDirectory = Path.GetDirectoryName(Project.MediaPath);
            if (mediaDirectory is null || !Directory.Exists(mediaDirectory))
            {
                return;
            }

            foreach (var file in Directory.EnumerateFiles(mediaDirectory, "source*.mp4"))
            {
                if (string.Equals(file, Project.MediaPath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                try
                {
                    File.Delete(file);
                }
                catch (IOException)
                {
                    // 失败尝试的文件可能仍被占用，留着也无害。
                }
            }
        }
        catch (Exception exception)
        {
            _diagnostics.Enqueue($"Cleanup skipped: {exception.Message}");
        }
    }
}
