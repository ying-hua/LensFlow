using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using LensFlow.App.Controls;
using LensFlow.App.Exporting;
using LensFlow.App.Recording;
using LensFlow.Core.Editing;
using LensFlow.Core.Models;
using LensFlow.Core.Persistence;
using Microsoft.Win32;
using ScreenRecorderLib;

namespace LensFlow.App;

public partial class MainWindow : Window
{
    private const string PlayGlyph = "▶";
    private const string PauseGlyph = "Ⅱ";

    private readonly ProjectRepository _repository = new();
    private readonly CameraEvaluator _cameraEvaluator = new();
    private readonly TimelineEditor _timelineEditor = new();
    private readonly FfmpegExporter _exporter = new();
    private readonly DispatcherTimer _recordingTimer;
    private readonly DispatcherTimer _playbackTimer;
    private readonly DispatcherTimer _scrubPreviewTimer;
    private readonly Stack<EditorSnapshot> _undo = new();
    private readonly Stack<EditorSnapshot> _redo = new();
    private ScreenRecordingSession? _recordingSession;
    private LensFlowProject? _project;
    private TimelineSelection? _selection;
    private CancellationTokenSource? _exportCancellation;
    private bool _isPlaying;
    private bool _isScrubbing;
    private bool _resumePlaybackAfterScrub;
    private long? _pendingScrubPositionMs;
    private bool _updatingZoom;
    private bool _zoomGestureActive;
    private CameraShotTimingState? _cameraShotTimingEdit;
    private EditorSnapshot? _cameraShotTimingUndoSnapshot;
    private bool _restoringState;
    private bool _allowClose;

    public MainWindow()
    {
        InitializeComponent();

        _recordingTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _recordingTimer.Tick += (_, _) =>
        {
            if (_recordingSession is not null)
            {
                RecordingTimeText.Text = FormatTime(_recordingSession.ElapsedMilliseconds);
            }
        };

        _playbackTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _playbackTimer.Tick += PlaybackTimer_Tick;

        _scrubPreviewTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(33)
        };
        _scrubPreviewTimer.Tick += ScrubPreviewTimer_Tick;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e) => RefreshCaptureSources();

    private void RefreshSources_Click(object sender, RoutedEventArgs e) => RefreshCaptureSources();

    private void RefreshCaptureSources()
    {
        try
        {
            var handle = new WindowInteropHelper(this).Handle;
            var previousLabel = (SourceComboBox.SelectedItem as CaptureSourceOption)?.Label;
            var sources = CaptureSourceOption.Discover(handle);
            SourceComboBox.ItemsSource = sources;
            SourceComboBox.SelectedItem =
                sources.FirstOrDefault(source => source.Label == previousLabel) ??
                sources.FirstOrDefault();
            HeaderStatusText.Text = $"发现 {sources.Count} 个录制来源";
        }
        catch (Exception exception)
        {
            ShowError("无法枚举录制来源", exception);
        }
    }

    private void StartRecording_Click(object sender, RoutedEventArgs e)
    {
        if (SourceComboBox.SelectedItem is not CaptureSourceOption source)
        {
            MessageBox.Show(this, "请选择录制来源。", "LensFlow", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            var frameRate = int.Parse(((ComboBoxItem)FrameRateComboBox.SelectedItem).Tag.ToString()!);
            var request = new RecordingRequest(
                source,
                SystemAudioCheckBox.IsChecked == true,
                MicrophoneCheckBox.IsChecked == true,
                frameRate,
                new WindowInteropHelper(this).Handle);

            _recordingSession = ScreenRecordingSession.Start(request, _repository);
            _recordingSession.StatusChanged += RecordingSession_StatusChanged;
            SetRecordingUi(true);
            _recordingTimer.Start();
            RecordingStatusText.Text = "录制中 · LensFlow 窗口已从画面排除";
            HeaderStatusText.Text = "正在录制";
        }
        catch (Exception exception)
        {
            ShowError("录制启动失败", exception);
        }
    }

    private void RecordingSession_StatusChanged(object? sender, RecorderStatus status)
    {
        Dispatcher.Invoke(() =>
        {
            HeaderStatusText.Text = status switch
            {
                RecorderStatus.Recording => "正在录制",
                RecorderStatus.Paused => "录制已暂停",
                RecorderStatus.Finishing => "正在完成录制",
                RecorderStatus.Idle => "正在生成项目",
                _ => status.ToString()
            };
        });
    }

    private void PauseRecording_Click(object sender, RoutedEventArgs e)
    {
        if (_recordingSession is null)
        {
            return;
        }

        if (_recordingSession.IsPaused)
        {
            _recordingSession.Resume();
            PauseRecordingButton.Content = "暂停";
            RecordingStatusText.Text = "录制中";
        }
        else
        {
            _recordingSession.Pause();
            PauseRecordingButton.Content = "继续";
            RecordingStatusText.Text = "已暂停 · 暂停区间不会进入视频";
        }
    }

    private async void StopRecording_Click(object sender, RoutedEventArgs e)
    {
        if (_recordingSession is null)
        {
            return;
        }

        StopRecordingButton.IsEnabled = false;
        PauseRecordingButton.IsEnabled = false;
        RecordingStatusText.Text = "正在保存并生成自动镜头…";
        HeaderStatusText.Text = "正在处理";

        try
        {
            var session = _recordingSession;
            _recordingSession = null;
            var project = await session.StopAsync();
            await LoadProjectAsync(project);
            HeaderStatusText.Text = "录制完成";
        }
        catch (Exception exception)
        {
            ShowError("停止录制失败", exception);
        }
        finally
        {
            _recordingTimer.Stop();
            SetRecordingUi(false);
        }
    }

    private async void OpenProject_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "打开 LensFlow 项目",
            Filter = "LensFlow 项目|project.db;project.json",
            InitialDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
                "LensFlow")
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            var directory = Path.GetDirectoryName(dialog.FileName)
                ?? throw new InvalidOperationException("无法确定项目目录。");
            await LoadProjectAsync(await _repository.LoadAsync(directory));
        }
        catch (Exception exception)
        {
            ShowError("打开项目失败", exception);
        }
    }

    private async Task LoadProjectAsync(LensFlowProject project)
    {
        if (!File.Exists(project.MediaPath))
        {
            throw new FileNotFoundException("项目的原始录屏文件不存在。", project.MediaPath);
        }

        _project = project;
        var projectChanged = false;
        if (project.VideoSegments.Count == 0)
        {
            project.VideoSegments.Add(new VideoSegment
            {
                StartMs = 0,
                EndMs = Math.Max(1, project.DurationMs)
            });
            projectChanged = true;
        }

        if (project.MouseSamples.Count > 0)
        {
            foreach (var shot in project.CameraShots)
            {
                projectChanged |= _timelineEditor.RebuildCameraShotPath(
                    shot,
                    project.MouseSamples,
                    project.FrameRate,
                    markUserLocked: false);
            }
        }

        if (projectChanged)
        {
            await _repository.SaveAsync(project);
        }

        _undo.Clear();
        _redo.Clear();
        _selection = null;
        StartView.Visibility = Visibility.Collapsed;
        EditorView.Visibility = Visibility.Visible;
        ExportButton.Visibility = Visibility.Visible;
        HeaderProjectText.Text = project.Name;
        EmptyEditorState.Visibility = Visibility.Visible;
        PreviewMedia.Source = new Uri(project.MediaPath);
        PreviewMedia.Position = TimeSpan.Zero;
        PreviewMedia.Play();
        Timeline.SetData(project.DurationMs, project.VideoSegments, project.CameraShots);
        Timeline.SetPlayhead(0);
        SetAspectRatioRadio(project.Canvas.AspectRatio);
        CanvasToolButton.IsChecked = true;
        ShowPropertyPanel(PropertyPanel.Canvas);
        UpdateUndoRedoButtons();
        UpdatePreviewFrame();
        HeaderStatusText.Text = "项目已加载";
    }

    private void PreviewMedia_MediaOpened(object sender, RoutedEventArgs e)
    {
        if (_project is null)
        {
            return;
        }

        PreviewMedia.Pause();
        PreviewMedia.Position = TimeSpan.Zero;
        _isPlaying = false;
        _playbackTimer.Stop();
        PlayPauseButton.Content = PlayGlyph;

        if (PreviewMedia.NaturalDuration.HasTimeSpan)
        {
            _project.DurationMs = Math.Max(
                1,
                (long)PreviewMedia.NaturalDuration.TimeSpan.TotalMilliseconds);
        }

        if (_project.VideoSegments.Count == 1 &&
            _project.VideoSegments[0].StartMs == 0)
        {
            _project.VideoSegments[0].EndMs = _project.DurationMs;
        }

        EmptyEditorState.Visibility = Visibility.Collapsed;
        PlayPauseButton.IsEnabled = true;
        Timeline.SetData(_project.DurationMs, _project.VideoSegments, _project.CameraShots);
        UpdateTimelineText(0);
        ApplyCamera(0);
        UpdatePreviewFrame();
    }

    private void PreviewMedia_MediaEnded(object sender, RoutedEventArgs e)
    {
        _isPlaying = false;
        _playbackTimer.Stop();
        PlayPauseButton.Content = PlayGlyph;
    }

    private void PlayPause_Click(object sender, RoutedEventArgs e)
    {
        if (_project is null)
        {
            return;
        }

        if (_isPlaying)
        {
            PreviewMedia.Pause();
            _playbackTimer.Stop();
            _isPlaying = false;
            PlayPauseButton.Content = PlayGlyph;
            return;
        }

        if (PreviewMedia.Position.TotalMilliseconds >= _project.DurationMs - 50)
        {
            PreviewMedia.Position = TimeSpan.Zero;
        }

        PreviewMedia.Play();
        _playbackTimer.Start();
        _isPlaying = true;
        PlayPauseButton.Content = PauseGlyph;
    }

    private void PlaybackTimer_Tick(object? sender, EventArgs e)
    {
        if (_project is null)
        {
            return;
        }

        var position = Math.Clamp(
            (long)PreviewMedia.Position.TotalMilliseconds,
            0,
            _project.DurationMs);
        Timeline.SetPlayhead(position);
        UpdateTimelineText(position);
        ApplyCamera(position);
    }

    private void Timeline_PlayheadChanged(object? sender, long timeMs)
    {
        if (_isScrubbing)
        {
            _pendingScrubPositionMs = timeMs;
            if (!_scrubPreviewTimer.IsEnabled)
            {
                _scrubPreviewTimer.Start();
            }

            return;
        }

        ApplyPreviewPosition(timeMs);
    }

    private void Timeline_ScrubStarted(object? sender, EventArgs e)
    {
        _isScrubbing = true;
        _pendingScrubPositionMs = null;
        _resumePlaybackAfterScrub = _isPlaying;
        if (!_isPlaying)
        {
            return;
        }

        PreviewMedia.Pause();
        _playbackTimer.Stop();
        _isPlaying = false;
        PlayPauseButton.Content = PlayGlyph;
    }

    private void Timeline_ScrubCompleted(object? sender, EventArgs e)
    {
        _isScrubbing = false;
        _scrubPreviewTimer.Stop();
        if (_pendingScrubPositionMs is { } finalPositionMs)
        {
            _pendingScrubPositionMs = null;
            ApplyPreviewPosition(finalPositionMs);
        }

        if (!_resumePlaybackAfterScrub)
        {
            return;
        }

        _resumePlaybackAfterScrub = false;
        if (_project is null || PreviewMedia.Source is null)
        {
            return;
        }

        PreviewMedia.Play();
        _playbackTimer.Start();
        _isPlaying = true;
        PlayPauseButton.Content = PauseGlyph;
    }

    private void ScrubPreviewTimer_Tick(object? sender, EventArgs e)
    {
        _scrubPreviewTimer.Stop();
        if (!_isScrubbing || _pendingScrubPositionMs is not { } positionMs)
        {
            return;
        }

        _pendingScrubPositionMs = null;
        ApplyPreviewPosition(positionMs);
    }

    private void ApplyPreviewPosition(long timeMs)
    {
        if (_project is null || PreviewMedia.Source is null)
        {
            return;
        }

        PreviewMedia.Position = TimeSpan.FromMilliseconds(timeMs);
        UpdateTimelineText(timeMs);
        ApplyCamera(timeMs);
    }

    private void Timeline_SelectionChanged(object? sender, TimelineSelection? selection)
    {
        _selection = selection;
        SplitButton.IsEnabled = selection is not null;
        DeleteButton.IsEnabled = selection?.Kind == TimelineItemKind.Camera;

        if (_project is null || selection is null)
        {
            ShowSelectedToolPanel();
            return;
        }

        if (selection.Kind == TimelineItemKind.Camera)
        {
            CameraToolButton.IsChecked = true;
            ShowPropertyPanel(PropertyPanel.Camera);
            var shot = _project.CameraShots.FirstOrDefault(item => item.Id == selection.Id);
            CameraPropertyFields.IsEnabled = shot is not null;
            CameraSelectionHint.Text = shot is null
                ? "请在镜头轨选择一个缩放元素。"
                : $"{FormatTime(shot.StartMs)} – {FormatTime(shot.EndMs)}";
            if (shot is not null)
            {
                _updatingZoom = true;
                ZoomSlider.Value = shot.Zoom;
                ZoomValueText.Text = $"{shot.Zoom:0.0}x";
                _updatingZoom = false;
            }
        }
        else
        {
            ShowPropertyPanel(PropertyPanel.Clip);
            var segment = _project.VideoSegments.FirstOrDefault(item => item.Id == selection.Id);
            ClipRangeText.Text = segment is null
                ? "--"
                : $"{FormatTime(segment.StartMs)} – {FormatTime(segment.EndMs)}";
        }
    }

    private void Timeline_CameraShotEditStarted(
        object? sender,
        CameraShotEditStartedEventArgs e)
    {
        if (_project is null)
        {
            return;
        }

        _cameraShotTimingEdit = _timelineEditor.CaptureCameraShotTiming(
            _project.CameraShots,
            e.Id);
        if (_cameraShotTimingEdit is not null)
        {
            _cameraShotTimingUndoSnapshot = CaptureSnapshot();
        }
    }

    private void Timeline_CameraShotEditChanged(
        object? sender,
        CameraShotEditChangedEventArgs e)
    {
        if (_project is null ||
            _cameraShotTimingEdit is not { } baseline ||
            baseline.Id != e.Id ||
            !_timelineEditor.ApplyCameraShotTiming(
                _project.CameraShots,
                baseline,
                e.StartMs,
                e.EndMs,
                _project.MouseSamples,
                _project.FrameRate,
                _project.DurationMs))
        {
            return;
        }

        Timeline.SetData(
            _project.DurationMs,
            _project.VideoSegments,
            _project.CameraShots);
        CameraSelectionHint.Text = $"{FormatTime(e.StartMs)} – {FormatTime(e.EndMs)}";
        ApplyCamera((long)PreviewMedia.Position.TotalMilliseconds);
    }

    private async void Timeline_CameraShotEditCompleted(
        object? sender,
        CameraShotEditChangedEventArgs e)
    {
        if (_project is null ||
            _cameraShotTimingEdit is not { } baseline ||
            baseline.Id != e.Id)
        {
            _cameraShotTimingEdit = null;
            _cameraShotTimingUndoSnapshot = null;
            return;
        }

        var undoSnapshot = _cameraShotTimingUndoSnapshot;
        _cameraShotTimingEdit = null;
        _cameraShotTimingUndoSnapshot = null;
        var shot = _project.CameraShots.FirstOrDefault(item => item.Id == e.Id);
        if (shot is null ||
            (shot.StartMs == baseline.StartMs && shot.EndMs == baseline.EndMs))
        {
            return;
        }

        if (undoSnapshot is not null)
        {
            PushUndoSnapshot(undoSnapshot);
        }

        await _repository.SaveAsync(_project);
        HeaderStatusText.Text = e.Kind == CameraShotEditKind.Move
            ? "已移动缩放效果"
            : "已调整缩放时长";
    }

    private async void Split_Click(object sender, RoutedEventArgs e) => await SplitSelectedAsync();

    private async Task SplitSelectedAsync()
    {
        if (_project is null || _selection is null)
        {
            return;
        }

        var playhead = (long)PreviewMedia.Position.TotalMilliseconds;
        PushUndoSnapshot();
        var changed = _selection.Kind switch
        {
            TimelineItemKind.Video => _timelineEditor.SplitVideoSegment(
                _project.VideoSegments,
                _selection.Id,
                playhead),
            TimelineItemKind.Camera => _timelineEditor.SplitCameraShot(
                _project.CameraShots,
                _selection.Id,
                playhead),
            _ => false
        };

        if (!changed)
        {
            _undo.Pop();
            UpdateUndoRedoButtons();
            HeaderStatusText.Text = "播放头需要位于元素内部";
            return;
        }

        Timeline.SetData(_project.DurationMs, _project.VideoSegments, _project.CameraShots);
        Timeline.Select(_selection);
        await _repository.SaveAsync(_project);
        HeaderStatusText.Text = "已切片";
    }

    private async void DeleteSelected_Click(object sender, RoutedEventArgs e)
    {
        if (_project is null || _selection?.Kind != TimelineItemKind.Camera)
        {
            return;
        }

        var shot = _project.CameraShots.FirstOrDefault(item => item.Id == _selection.Id);
        if (shot is null)
        {
            return;
        }

        PushUndoSnapshot();
        _project.CameraShots.Remove(shot);
        _selection = null;
        Timeline.SetData(_project.DurationMs, _project.VideoSegments, _project.CameraShots);
        Timeline.Select(null);
        ApplyCamera((long)PreviewMedia.Position.TotalMilliseconds);
        await _repository.SaveAsync(_project);
        HeaderStatusText.Text = "缩放效果已删除";
    }

    private async void Undo_Click(object sender, RoutedEventArgs e) => await UndoAsync();

    private async Task UndoAsync()
    {
        if (_project is null || _undo.Count == 0)
        {
            return;
        }

        _redo.Push(CaptureSnapshot());
        RestoreSnapshot(_undo.Pop());
        await _repository.SaveAsync(_project);
        HeaderStatusText.Text = "已撤销";
    }

    private async void Redo_Click(object sender, RoutedEventArgs e) => await RedoAsync();

    private async Task RedoAsync()
    {
        if (_project is null || _redo.Count == 0)
        {
            return;
        }

        _undo.Push(CaptureSnapshot());
        RestoreSnapshot(_redo.Pop());
        await _repository.SaveAsync(_project);
        HeaderStatusText.Text = "已恢复";
    }

    private void PushUndoSnapshot()
    {
        if (_project is null || _restoringState)
        {
            return;
        }

        PushUndoSnapshot(CaptureSnapshot());
    }

    private void PushUndoSnapshot(EditorSnapshot snapshot)
    {
        _undo.Push(snapshot);
        while (_undo.Count > 20)
        {
            var kept = _undo.Reverse().Take(20).Reverse().ToArray();
            _undo.Clear();
            foreach (var item in kept)
            {
                _undo.Push(item);
            }
        }

        _redo.Clear();
        UpdateUndoRedoButtons();
    }

    private EditorSnapshot CaptureSnapshot()
    {
        if (_project is null)
        {
            throw new InvalidOperationException("No project is loaded.");
        }

        return new EditorSnapshot(
            _project.Canvas.AspectRatio,
            _project.VideoSegments.Select(Clone).ToList(),
            _project.CameraShots.Select(Clone).ToList());
    }

    private void RestoreSnapshot(EditorSnapshot snapshot)
    {
        if (_project is null)
        {
            return;
        }

        _restoringState = true;
        _project.Canvas.AspectRatio = snapshot.AspectRatio;
        _project.VideoSegments = snapshot.VideoSegments.Select(Clone).ToList();
        _project.CameraShots = snapshot.CameraShots.Select(Clone).ToList();
        _selection = null;
        Timeline.SetData(_project.DurationMs, _project.VideoSegments, _project.CameraShots);
        Timeline.Select(null);
        SetAspectRatioRadio(snapshot.AspectRatio);
        UpdatePreviewFrame();
        ApplyCamera((long)PreviewMedia.Position.TotalMilliseconds);
        _restoringState = false;
        UpdateUndoRedoButtons();
    }

    private void UpdateUndoRedoButtons()
    {
        UndoButton.IsEnabled = _undo.Count > 0;
        RedoButton.IsEnabled = _redo.Count > 0;
    }

    private void ToolTab_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsInitialized || CanvasSettingsPanel is null)
        {
            return;
        }

        if (sender == CanvasToolButton)
        {
            ShowPropertyPanel(PropertyPanel.Canvas);
        }
        else if (sender == CursorToolButton)
        {
            ShowPropertyPanel(PropertyPanel.Cursor);
        }
        else
        {
            ShowPropertyPanel(PropertyPanel.Camera);
        }
    }

    private void ShowSelectedToolPanel()
    {
        if (CanvasToolButton.IsChecked == true)
        {
            ShowPropertyPanel(PropertyPanel.Canvas);
        }
        else if (CursorToolButton.IsChecked == true)
        {
            ShowPropertyPanel(PropertyPanel.Cursor);
        }
        else
        {
            ShowPropertyPanel(PropertyPanel.Camera);
        }
    }

    private void ShowPropertyPanel(PropertyPanel panel)
    {
        CanvasSettingsPanel.Visibility = panel == PropertyPanel.Canvas
            ? Visibility.Visible
            : Visibility.Collapsed;
        CursorSettingsPanel.Visibility = panel == PropertyPanel.Cursor
            ? Visibility.Visible
            : Visibility.Collapsed;
        CameraSettingsPanel.Visibility = panel == PropertyPanel.Camera
            ? Visibility.Visible
            : Visibility.Collapsed;
        ClipSettingsPanel.Visibility = panel == PropertyPanel.Clip
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private async void AspectRatio_Checked(object sender, RoutedEventArgs e)
    {
        if (_project is null ||
            _restoringState ||
            sender is not RadioButton { Tag: string value } ||
            !Enum.TryParse<AspectRatioPreset>(value, out var aspectRatio) ||
            _project.Canvas.AspectRatio == aspectRatio)
        {
            return;
        }

        PushUndoSnapshot();
        _project.Canvas.AspectRatio = aspectRatio;
        UpdatePreviewFrame();
        await _repository.SaveAsync(_project);
    }

    private void SetAspectRatioRadio(AspectRatioPreset aspectRatio)
    {
        foreach (var button in FindVisualChildren<RadioButton>(CanvasSettingsPanel))
        {
            if (button.Tag is string value &&
                Enum.TryParse<AspectRatioPreset>(value, out var candidate))
            {
                button.IsChecked = candidate == aspectRatio;
            }
        }
    }

    private void ZoomSlider_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_project is null || _selection?.Kind != TimelineItemKind.Camera)
        {
            return;
        }

        PushUndoSnapshot();
        _zoomGestureActive = true;
    }

    private async void ZoomSlider_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _zoomGestureActive = false;
        if (_project is not null)
        {
            await _repository.SaveAsync(_project);
        }
    }

    private void ZoomSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingZoom ||
            _project is null ||
            _selection?.Kind != TimelineItemKind.Camera)
        {
            return;
        }

        var shot = _project.CameraShots.FirstOrDefault(item => item.Id == _selection.Id);
        if (shot is null)
        {
            return;
        }

        if (!_zoomGestureActive)
        {
            PushUndoSnapshot();
        }

        shot.Zoom = Math.Clamp(e.NewValue, 1.2, 3);
        shot.UserLocked = true;
        _timelineEditor.RebuildCameraShotPath(
            shot,
            _project.MouseSamples,
            _project.FrameRate);
        ZoomValueText.Text = $"{shot.Zoom:0.0}x";
        Timeline.SetData(_project.DurationMs, _project.VideoSegments, _project.CameraShots);
        Timeline.Select(_selection);
        ApplyCamera((long)PreviewMedia.Position.TotalMilliseconds);
    }

    private async void ApplyZoomToAll_Click(object sender, RoutedEventArgs e)
    {
        if (_project is null || _selection?.Kind != TimelineItemKind.Camera)
        {
            return;
        }

        PushUndoSnapshot();
        foreach (var shot in _project.CameraShots)
        {
            shot.Zoom = ZoomSlider.Value;
            shot.UserLocked = true;
            _timelineEditor.RebuildCameraShotPath(
                shot,
                _project.MouseSamples,
                _project.FrameRate);
        }

        Timeline.SetData(_project.DurationMs, _project.VideoSegments, _project.CameraShots);
        Timeline.Select(_selection);
        ApplyCamera((long)PreviewMedia.Position.TotalMilliseconds);
        await _repository.SaveAsync(_project);
    }

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_project is null)
        {
            return;
        }

        if (_exporter.FindExecutable() is null)
        {
            MessageBox.Show(
                this,
                "未找到 FFmpeg。请先在项目根目录运行 scripts\\setup-ffmpeg.ps1。",
                "无法导出",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "导出 MP4",
            Filter = "MP4 视频|*.mp4",
            FileName = $"{_project.Name}-export.mp4",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        _exportCancellation = new CancellationTokenSource();
        ExportButton.IsEnabled = false;
        ExportProgressBar.Visibility = Visibility.Visible;
        var progress = new Progress<double>(value =>
        {
            ExportProgressBar.Value = value * 100;
            HeaderStatusText.Text = $"正在导出 {value:P0}";
        });

        try
        {
            await _repository.SaveAsync(_project);
            await _exporter.ExportAsync(
                _project,
                dialog.FileName,
                progress,
                _exportCancellation.Token);
            HeaderStatusText.Text = "导出完成";
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{dialog.FileName}\"")
            {
                UseShellExecute = true
            });
        }
        catch (OperationCanceledException)
        {
            HeaderStatusText.Text = "导出已取消";
        }
        catch (Exception exception)
        {
            ShowError("导出失败", exception);
        }
        finally
        {
            _exportCancellation.Dispose();
            _exportCancellation = null;
            ExportButton.IsEnabled = true;
            ExportProgressBar.Visibility = Visibility.Collapsed;
            ExportProgressBar.Value = 0;
        }
    }

    private void ApplyCamera(long timeMs)
    {
        if (_project is null)
        {
            return;
        }

        var frame = _cameraEvaluator.Evaluate(_project.CameraShots, timeMs);
        var viewport = CameraViewportMath.Resolve(frame);
        PreviewScale.ScaleX = viewport.Zoom;
        PreviewScale.ScaleY = viewport.Zoom;
        var sourceWidth = double.IsFinite(PreviewSourceFrame.Width)
            ? PreviewSourceFrame.Width
            : PreviewSourceFrame.ActualWidth;
        var sourceHeight = double.IsFinite(PreviewSourceFrame.Height)
            ? PreviewSourceFrame.Height
            : PreviewSourceFrame.ActualHeight;
        PreviewTranslate.X = viewport.TranslateXRatio * Math.Max(0, sourceWidth);
        PreviewTranslate.Y = viewport.TranslateYRatio * Math.Max(0, sourceHeight);
    }

    private void PreviewHost_SizeChanged(object sender, SizeChangedEventArgs e) => UpdatePreviewFrame();

    private void UpdatePreviewFrame()
    {
        if (_project is null || PreviewHost.ActualWidth <= 0 || PreviewHost.ActualHeight <= 0)
        {
            return;
        }

        var ratio = _project.Canvas.AspectRatio switch
        {
            AspectRatioPreset.Landscape16By9 => 16d / 9,
            AspectRatioPreset.Square => 1,
            AspectRatioPreset.Standard4By3 => 4d / 3,
            AspectRatioPreset.Portrait9By16 => 9d / 16,
            _ => _project.SourceWidth / (double)Math.Max(1, _project.SourceHeight)
        };

        var availableWidth = Math.Max(200, PreviewHost.ActualWidth - 70);
        var availableHeight = Math.Max(160, PreviewHost.ActualHeight - 70);
        var width = availableWidth;
        var height = width / ratio;
        if (height > availableHeight)
        {
            height = availableHeight;
            width = height * ratio;
        }

        PreviewFrame.Width = width;
        PreviewFrame.Height = height;

        var sourceRatio = _project.SourceWidth / (double)Math.Max(1, _project.SourceHeight);
        var sourceWidth = width;
        var sourceHeight = sourceWidth / sourceRatio;
        if (sourceHeight > height)
        {
            sourceHeight = height;
            sourceWidth = sourceHeight * sourceRatio;
        }

        PreviewSourceFrame.Width = sourceWidth;
        PreviewSourceFrame.Height = sourceHeight;
        ApplyCamera((long)PreviewMedia.Position.TotalMilliseconds);
    }

    private void SetRecordingUi(bool recording)
    {
        StartRecordingButton.Visibility = recording ? Visibility.Collapsed : Visibility.Visible;
        RecordingControls.Visibility = recording ? Visibility.Visible : Visibility.Collapsed;
        SourceComboBox.IsEnabled = !recording;
        FrameRateComboBox.IsEnabled = !recording;
        SystemAudioCheckBox.IsEnabled = !recording;
        MicrophoneCheckBox.IsEnabled = !recording;
        PauseRecordingButton.IsEnabled = recording;
        StopRecordingButton.IsEnabled = recording;
        if (!recording)
        {
            PauseRecordingButton.Content = "暂停";
        }
    }

    private void HomeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_recordingSession is not null)
        {
            return;
        }

        PreviewMedia.Stop();
        _playbackTimer.Stop();
        _isPlaying = false;
        EditorView.Visibility = Visibility.Collapsed;
        StartView.Visibility = Visibility.Visible;
        ExportButton.Visibility = Visibility.Collapsed;
        HeaderProjectText.Text = "新建录制";
        HeaderStatusText.Text = "准备就绪";
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            ToggleMaximize();
        }
        else
        {
            DragMove();
        }
    }

    private void MinimizeWindow_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void MaximizeWindow_Click(object sender, RoutedEventArgs e) => ToggleMaximize();

    private void ToggleMaximize()
        => WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;

    private void CloseWindow_Click(object sender, RoutedEventArgs e) => Close();

    private async void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (_cameraShotTimingEdit is not null)
        {
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.B)
        {
            await SplitSelectedAsync();
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Z)
        {
            await UndoAsync();
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Y)
        {
            await RedoAsync();
            e.Handled = true;
        }
        else if (e.Key == Key.Delete)
        {
            DeleteSelected_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
    }

    private void UpdateTimelineText(long positionMs)
        => TimelineTimeText.Text = $"{FormatTime(positionMs)} / {FormatTime(_project?.DurationMs ?? 0)}";

    private static string FormatTime(long milliseconds)
    {
        var value = TimeSpan.FromMilliseconds(Math.Max(0, milliseconds));
        return value.TotalHours >= 1
            ? value.ToString(@"hh\:mm\:ss")
            : value.ToString(@"mm\:ss");
    }

    private void ShowError(string title, Exception exception)
    {
        HeaderStatusText.Text = title;
        MessageBox.Show(this, exception.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_allowClose)
        {
            return;
        }

        if (_recordingSession is not null)
        {
            e.Cancel = true;
            MessageBox.Show(
                this,
                "请先停止当前录制，再关闭 LensFlow。",
                "录制进行中",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        _exportCancellation?.Cancel();
        _allowClose = true;
    }

    private static VideoSegment Clone(VideoSegment segment)
        => new()
        {
            Id = segment.Id,
            StartMs = segment.StartMs,
            EndMs = segment.EndMs
        };

    private static CameraShot Clone(CameraShot shot)
        => new()
        {
            Id = shot.Id,
            StartMs = shot.StartMs,
            EndMs = shot.EndMs,
            Zoom = shot.Zoom,
            UserLocked = shot.UserLocked,
            Points = shot.Points
                .Select(point => new CameraPoint(point.TimeMs, point.X, point.Y))
                .ToList()
        };

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in FindVisualChildren<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private sealed record EditorSnapshot(
        AspectRatioPreset AspectRatio,
        List<VideoSegment> VideoSegments,
        List<CameraShot> CameraShots);

    private enum PropertyPanel
    {
        Canvas,
        Cursor,
        Camera,
        Clip
    }
}
