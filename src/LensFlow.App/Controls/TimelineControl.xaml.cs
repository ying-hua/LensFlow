using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using LensFlow.Core.Editing;
using LensFlow.Core.Models;

namespace LensFlow.App.Controls;

public enum TimelineItemKind
{
    Video,
    Camera
}

public sealed record TimelineSelection(TimelineItemKind Kind, Guid Id);

public enum CameraShotEditKind
{
    Move,
    ResizeStart,
    ResizeEnd
}

public sealed record CameraShotEditStartedEventArgs(
    Guid Id,
    CameraShotEditKind Kind);

public sealed record CameraShotEditChangedEventArgs(
    Guid Id,
    CameraShotEditKind Kind,
    long StartMs,
    long EndMs);

public partial class TimelineControl : UserControl
{
    private const double LabelWidth = 76;
    private const double RulerHeight = 30;
    private const double VideoTrackTop = 38;
    private const double VideoTrackHeight = 54;
    private const double CameraTrackTop = 104;
    private const double CameraTrackHeight = 38;
    private const double PlayheadHandleTop = 4;
    private const double PlayheadHandleHeight = 8;
    private const double PlayheadLineTop = PlayheadHandleTop + PlayheadHandleHeight;

    private IReadOnlyList<VideoSegment> _segments = [];
    private IReadOnlyList<CameraShot> _shots = [];
    private long _durationMs = 1;
    private long _playheadMs;
    private long? _previewPlayheadMs;
    private bool _isDraggingPlayhead;
    private CameraShotEditKind? _cameraShotEditKind;
    private Guid _editingCameraShotId;
    private double _cameraShotEditStartX;
    private long _cameraShotOriginalStartMs;
    private long _cameraShotOriginalEndMs;
    private long _cameraShotCurrentStartMs;
    private long _cameraShotCurrentEndMs;
    private long _previousCameraBoundaryMs;
    private long _nextCameraBoundaryMs;
    private Line? _playheadLine;
    private Polygon? _playheadHandle;
    private Line? _previewPlayheadLine;
    private Polygon? _previewPlayheadHandle;

    public TimelineControl()
    {
        InitializeComponent();
    }

    public TimelineSelection? SelectedItem { get; private set; }

    public event EventHandler<long>? PlayheadChanged;
    public event EventHandler? ScrubStarted;
    public event EventHandler? ScrubCompleted;
    public event EventHandler<TimelineSelection?>? SelectionChanged;
    public event EventHandler<CameraShotEditStartedEventArgs>? CameraShotEditStarted;
    public event EventHandler<CameraShotEditChangedEventArgs>? CameraShotEditChanged;
    public event EventHandler<CameraShotEditChangedEventArgs>? CameraShotEditCompleted;

    public void SetData(
        long durationMs,
        IReadOnlyList<VideoSegment> segments,
        IReadOnlyList<CameraShot> shots)
    {
        _durationMs = Math.Max(1, durationMs);
        _segments = segments;
        _shots = shots;
        RenderTimeline();
    }

    public void SetPlayhead(long timeMs)
    {
        _playheadMs = Math.Clamp(timeMs, 0, _durationMs);
        PositionPlayhead();
    }

    public void Select(TimelineSelection? selection)
    {
        SelectedItem = selection;
        RenderTimeline();
        SelectionChanged?.Invoke(this, selection);
    }

    private void RenderTimeline()
    {
        TimelineCanvas.Children.Clear();
        _playheadLine = null;
        _playheadHandle = null;
        _previewPlayheadLine = null;
        _previewPlayheadHandle = null;

        var width = Math.Max(1, TimelineCanvas.ActualWidth - LabelWidth);
        if (width <= 1)
        {
            return;
        }

        AddTrackBackground(VideoTrackTop, VideoTrackHeight);
        AddTrackBackground(CameraTrackTop, CameraTrackHeight);
        AddLabel("视频", VideoTrackTop + 17);
        AddLabel("镜头", CameraTrackTop + 10);
        AddRuler(width);

        foreach (var segment in _segments)
        {
            AddTimelineItem(
                new TimelineSelection(TimelineItemKind.Video, segment.Id),
                segment.StartMs,
                segment.EndMs,
                VideoTrackTop,
                VideoTrackHeight,
                $"切片  {FormatDuration(segment.EndMs - segment.StartMs)}  1.0x",
                new LinearGradientBrush(
                    Color.FromRgb(119, 61, 220),
                    Color.FromRgb(155, 78, 230),
                    0));
        }

        foreach (var shot in _shots)
        {
            AddTimelineItem(
                new TimelineSelection(TimelineItemKind.Camera, shot.Id),
                shot.StartMs,
                shot.EndMs,
                CameraTrackTop,
                CameraTrackHeight,
                $"缩放  {shot.Zoom:0.0}x",
                new LinearGradientBrush(
                    Color.FromRgb(26, 112, 238),
                    Color.FromRgb(45, 149, 246),
                    0));
        }

        var playheadBottom = Math.Max(RulerHeight, TimelineCanvas.ActualHeight);
        var previewBrush = new SolidColorBrush(Color.FromRgb(137, 146, 161));
        _previewPlayheadLine = new Line
        {
            Y1 = PlayheadLineTop,
            Y2 = playheadBottom,
            Stroke = previewBrush,
            StrokeThickness = 1.5,
            Opacity = 0.72,
            IsHitTestVisible = false
        };
        TimelineCanvas.Children.Add(_previewPlayheadLine);

        _previewPlayheadHandle = new Polygon
        {
            Points = [new Point(0, 0), new Point(10, 0), new Point(5, 8)],
            Fill = previewBrush,
            Opacity = 0.72,
            IsHitTestVisible = false
        };
        TimelineCanvas.Children.Add(_previewPlayheadHandle);

        var playheadBrush = new SolidColorBrush(Color.FromRgb(255, 177, 61));
        _playheadLine = new Line
        {
            Y1 = PlayheadLineTop,
            Y2 = playheadBottom,
            Stroke = playheadBrush,
            StrokeThickness = 2,
            IsHitTestVisible = false
        };
        TimelineCanvas.Children.Add(_playheadLine);

        _playheadHandle = new Polygon
        {
            Points = [new Point(0, 0), new Point(10, 0), new Point(5, 8)],
            Fill = playheadBrush,
            IsHitTestVisible = false
        };
        TimelineCanvas.Children.Add(_playheadHandle);
        PositionPlayhead();
        PositionPreviewPlayhead();
    }

    private void AddTrackBackground(double top, double height)
    {
        var background = new Border
        {
            Width = Math.Max(1, TimelineCanvas.ActualWidth - LabelWidth - 8),
            Height = height,
            Background = new SolidColorBrush(Color.FromRgb(18, 22, 30)),
            CornerRadius = new CornerRadius(7),
            IsHitTestVisible = false
        };
        Canvas.SetLeft(background, LabelWidth);
        Canvas.SetTop(background, top);
        TimelineCanvas.Children.Add(background);
    }

    private void AddLabel(string text, double top)
    {
        var label = new TextBlock
        {
            Text = text,
            Foreground = new SolidColorBrush(Color.FromRgb(127, 138, 155)),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            IsHitTestVisible = false
        };
        Canvas.SetLeft(label, 18);
        Canvas.SetTop(label, top);
        TimelineCanvas.Children.Add(label);
    }

    private void AddRuler(double contentWidth)
    {
        var seconds = _durationMs / 1000d;
        var tickSeconds = seconds switch
        {
            <= 20 => 2,
            <= 60 => 5,
            <= 180 => 10,
            _ => 30
        };

        for (double second = 0; second <= seconds; second += tickSeconds)
        {
            var x = LabelWidth + ((second * 1000) / _durationMs * contentWidth);
            var line = new Line
            {
                X1 = x,
                X2 = x,
                Y1 = 20,
                Y2 = RulerHeight - 3,
                Stroke = new SolidColorBrush(Color.FromRgb(65, 73, 89)),
                StrokeThickness = 1,
                IsHitTestVisible = false
            };
            TimelineCanvas.Children.Add(line);

            var label = new TextBlock
            {
                Text = $"{second:0}s",
                Foreground = new SolidColorBrush(Color.FromRgb(111, 121, 138)),
                FontSize = 10,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(label, x + 4);
            Canvas.SetTop(label, 4);
            TimelineCanvas.Children.Add(label);
        }
    }

    private void AddTimelineItem(
        TimelineSelection selection,
        long startMs,
        long endMs,
        double top,
        double height,
        string label,
        Brush fill)
    {
        var contentWidth = Math.Max(1, TimelineCanvas.ActualWidth - LabelWidth);
        var left = LabelWidth + (startMs / (double)_durationMs * contentWidth);
        var width = Math.Max(18, (endMs - startMs) / (double)_durationMs * contentWidth - 2);
        var selected = SelectedItem == selection;

        var item = new Border
        {
            Width = width,
            Height = height,
            Background = fill,
            BorderBrush = selected
                ? Brushes.White
                : new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)),
            BorderThickness = new Thickness(selected ? 2 : 1),
            CornerRadius = new CornerRadius(7),
            Padding = selection.Kind == TimelineItemKind.Camera
                ? new Thickness(0)
                : new Thickness(12, 0, 8, 0),
            Cursor = selection.Kind == TimelineItemKind.Camera
                ? Cursors.SizeAll
                : Cursors.Hand,
            Tag = selection,
            Child = CreateTimelineItemContent(selection, label, selected)
        };
        item.MouseLeftButtonDown += TimelineItem_MouseLeftButtonDown;
        Canvas.SetLeft(item, left);
        Canvas.SetTop(item, top);
        TimelineCanvas.Children.Add(item);
    }

    private UIElement CreateTimelineItemContent(
        TimelineSelection selection,
        string label,
        bool selected)
    {
        var text = new TextBlock
        {
            Text = label,
            Foreground = Brushes.White,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        if (selection.Kind != TimelineItemKind.Camera)
        {
            return text;
        }

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        grid.Children.Add(CreateCameraResizeHandle(
            selection,
            CameraShotEditKind.ResizeStart,
            0,
            selected));
        grid.Children.Add(CreateCameraResizeHandle(
            selection,
            CameraShotEditKind.ResizeEnd,
            2,
            selected));
        return grid;
    }

    private Border CreateCameraResizeHandle(
        TimelineSelection selection,
        CameraShotEditKind kind,
        int column,
        bool selected)
    {
        var grip = new Border
        {
            Width = 2,
            Height = 16,
            Background = Brushes.White,
            CornerRadius = new CornerRadius(1),
            Opacity = selected ? 0.9 : 0.38,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        };
        var handle = new Border
        {
            Background = Brushes.Transparent,
            Cursor = Cursors.SizeWE,
            Tag = new CameraShotEditTarget(selection.Id, kind),
            Child = grip
        };
        Grid.SetColumn(handle, column);
        handle.MouseLeftButtonDown += CameraResizeHandle_MouseLeftButtonDown;
        return handle;
    }

    private void TimelineItem_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border { Tag: TimelineSelection selection })
        {
            return;
        }

        SelectTimelineItem(selection);
        var x = e.GetPosition(TimelineCanvas).X;
        if (selection.Kind == TimelineItemKind.Camera)
        {
            BeginCameraShotEdit(selection.Id, CameraShotEditKind.Move, x);
        }
        else
        {
            BeginPlayheadDrag(x);
        }

        e.Handled = true;
    }

    private void CameraResizeHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border { Tag: CameraShotEditTarget target })
        {
            return;
        }

        SelectTimelineItem(new TimelineSelection(TimelineItemKind.Camera, target.Id));
        BeginCameraShotEdit(
            target.Id,
            target.Kind,
            e.GetPosition(TimelineCanvas).X);
        e.Handled = true;
    }

    private void SelectTimelineItem(TimelineSelection selection)
    {
        if (SelectedItem == selection)
        {
            return;
        }

        SelectedItem = selection;
        RenderTimeline();
        SelectionChanged?.Invoke(this, selection);
    }

    private void TimelineCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        BeginPlayheadDrag(e.GetPosition(TimelineCanvas).X);
        SelectedItem = null;
        RenderTimeline();
        SelectionChanged?.Invoke(this, null);
        e.Handled = true;
    }

    private void TimelineCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_cameraShotEditKind is not null)
        {
            EndCameraShotEdit(e.GetPosition(TimelineCanvas).X);
            e.Handled = true;
            return;
        }

        if (!_isDraggingPlayhead)
        {
            return;
        }

        EndPlayheadDrag(e.GetPosition(TimelineCanvas).X);
        e.Handled = true;
    }

    private void TimelineCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        var x = e.GetPosition(TimelineCanvas).X;
        if (_cameraShotEditKind is not null)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                UpdateCameraShotEdit(x);
                e.Handled = true;
            }
            else
            {
                EndCameraShotEdit(x);
            }

            return;
        }

        if (_isDraggingPlayhead)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                SetPlayheadFromPosition(x);
                e.Handled = true;
            }
            else
            {
                EndPlayheadDrag(x);
            }

            return;
        }

        SetPreviewPlayheadFromPosition(x);
    }

    private void TimelineCanvas_MouseLeave(object sender, MouseEventArgs e)
    {
        if (_isDraggingPlayhead || _cameraShotEditKind is not null)
        {
            return;
        }

        _previewPlayheadMs = null;
        PositionPreviewPlayhead();
    }

    private void TimelineCanvas_LostMouseCapture(object sender, MouseEventArgs e)
    {
        if (_cameraShotEditKind is not null)
        {
            CompleteCameraShotEdit();
            return;
        }

        if (!_isDraggingPlayhead)
        {
            return;
        }

        _isDraggingPlayhead = false;
        _previewPlayheadMs = null;
        PositionPreviewPlayhead();
        ScrubCompleted?.Invoke(this, EventArgs.Empty);
    }

    private void BeginCameraShotEdit(
        Guid id,
        CameraShotEditKind kind,
        double x)
    {
        if (_cameraShotEditKind is not null ||
            _isDraggingPlayhead ||
            _shots.FirstOrDefault(shot => shot.Id == id) is not { } shot)
        {
            return;
        }

        _cameraShotEditKind = kind;
        _editingCameraShotId = id;
        _cameraShotEditStartX = x;
        _cameraShotOriginalStartMs = shot.StartMs;
        _cameraShotOriginalEndMs = shot.EndMs;
        _cameraShotCurrentStartMs = shot.StartMs;
        _cameraShotCurrentEndMs = shot.EndMs;
        _previousCameraBoundaryMs = _shots
            .Where(candidate =>
                candidate.Id != id &&
                candidate.EndMs <= shot.StartMs)
            .Select(candidate => candidate.EndMs)
            .DefaultIfEmpty(0)
            .Max();
        _nextCameraBoundaryMs = _shots
            .Where(candidate =>
                candidate.Id != id &&
                candidate.StartMs >= shot.EndMs)
            .Select(candidate => candidate.StartMs)
            .DefaultIfEmpty(_durationMs)
            .Min();
        _previewPlayheadMs = null;
        TimelineCanvas.CaptureMouse();
        PositionPreviewPlayhead();
        CameraShotEditStarted?.Invoke(
            this,
            new CameraShotEditStartedEventArgs(id, kind));
    }

    private void UpdateCameraShotEdit(double x)
    {
        if (_cameraShotEditKind is not { } kind)
        {
            return;
        }

        var deltaMs = TimeDeltaFromDistance(x - _cameraShotEditStartX);
        var startMs = _cameraShotOriginalStartMs;
        var endMs = _cameraShotOriginalEndMs;
        switch (kind)
        {
            case CameraShotEditKind.Move:
                var durationMs = _cameraShotOriginalEndMs - _cameraShotOriginalStartMs;
                startMs = Math.Clamp(
                    _cameraShotOriginalStartMs + deltaMs,
                    _previousCameraBoundaryMs,
                    Math.Max(_previousCameraBoundaryMs, _nextCameraBoundaryMs - durationMs));
                endMs = startMs + durationMs;
                break;

            case CameraShotEditKind.ResizeStart:
                startMs = Math.Clamp(
                    _cameraShotOriginalStartMs + deltaMs,
                    _previousCameraBoundaryMs,
                    _cameraShotOriginalEndMs - TimelineEditor.MinimumSegmentDurationMs);
                break;

            case CameraShotEditKind.ResizeEnd:
                endMs = Math.Clamp(
                    _cameraShotOriginalEndMs + deltaMs,
                    _cameraShotOriginalStartMs + TimelineEditor.MinimumSegmentDurationMs,
                    _nextCameraBoundaryMs);
                break;
        }

        if (startMs == _cameraShotCurrentStartMs &&
            endMs == _cameraShotCurrentEndMs)
        {
            return;
        }

        _cameraShotCurrentStartMs = startMs;
        _cameraShotCurrentEndMs = endMs;
        CameraShotEditChanged?.Invoke(
            this,
            new CameraShotEditChangedEventArgs(
                _editingCameraShotId,
                kind,
                startMs,
                endMs));
    }

    private void EndCameraShotEdit(double x)
    {
        UpdateCameraShotEdit(x);
        CompleteCameraShotEdit();
        if (TimelineCanvas.IsMouseCaptured)
        {
            TimelineCanvas.ReleaseMouseCapture();
        }

        SetPreviewPlayheadFromPosition(x);
    }

    private void CompleteCameraShotEdit()
    {
        if (_cameraShotEditKind is not { } kind)
        {
            return;
        }

        var args = new CameraShotEditChangedEventArgs(
            _editingCameraShotId,
            kind,
            _cameraShotCurrentStartMs,
            _cameraShotCurrentEndMs);
        _cameraShotEditKind = null;
        _previewPlayheadMs = null;
        PositionPreviewPlayhead();
        CameraShotEditCompleted?.Invoke(this, args);
    }

    private void BeginPlayheadDrag(double x)
    {
        if (_isDraggingPlayhead)
        {
            return;
        }

        _isDraggingPlayhead = true;
        _previewPlayheadMs = null;
        TimelineCanvas.CaptureMouse();
        ScrubStarted?.Invoke(this, EventArgs.Empty);
        SetPlayheadFromPosition(x);
        PositionPreviewPlayhead();
    }

    private void EndPlayheadDrag(double x)
    {
        SetPlayheadFromPosition(x);
        _isDraggingPlayhead = false;
        if (TimelineCanvas.IsMouseCaptured)
        {
            TimelineCanvas.ReleaseMouseCapture();
        }

        SetPreviewPlayheadFromPosition(x);
        ScrubCompleted?.Invoke(this, EventArgs.Empty);
    }

    private void SetPlayheadFromPosition(double x)
    {
        _playheadMs = TimeFromPosition(x);
        PositionPlayhead();
        PlayheadChanged?.Invoke(this, _playheadMs);
    }

    private void SetPreviewPlayheadFromPosition(double x)
    {
        _previewPlayheadMs = x >= LabelWidth && x <= TimelineCanvas.ActualWidth
            ? TimeFromPosition(x)
            : null;
        PositionPreviewPlayhead();
    }

    private long TimeFromPosition(double x)
    {
        var contentWidth = Math.Max(1, TimelineCanvas.ActualWidth - LabelWidth);
        var normalized = Math.Clamp((x - LabelWidth) / contentWidth, 0, 1);
        return (long)(_durationMs * normalized);
    }

    private long TimeDeltaFromDistance(double distance)
    {
        var contentWidth = Math.Max(1, TimelineCanvas.ActualWidth - LabelWidth);
        return (long)Math.Round(_durationMs * distance / contentWidth);
    }

    private void PositionPlayhead()
    {
        if (_playheadLine is null || _playheadHandle is null)
        {
            return;
        }

        var x = PositionFromTime(_playheadMs);
        _playheadLine.X1 = x;
        _playheadLine.X2 = x;
        Canvas.SetLeft(_playheadHandle, x - 5);
        Canvas.SetTop(_playheadHandle, PlayheadHandleTop);
    }

    private void PositionPreviewPlayhead()
    {
        if (_previewPlayheadLine is null || _previewPlayheadHandle is null)
        {
            return;
        }

        var visible = _previewPlayheadMs.HasValue && !_isDraggingPlayhead;
        _previewPlayheadLine.Visibility = visible ? Visibility.Visible : Visibility.Hidden;
        _previewPlayheadHandle.Visibility = visible ? Visibility.Visible : Visibility.Hidden;
        if (!visible)
        {
            return;
        }

        var x = PositionFromTime(_previewPlayheadMs!.Value);
        _previewPlayheadLine.X1 = x;
        _previewPlayheadLine.X2 = x;
        Canvas.SetLeft(_previewPlayheadHandle, x - 5);
        Canvas.SetTop(_previewPlayheadHandle, PlayheadHandleTop);
    }

    private double PositionFromTime(long timeMs)
    {
        var contentWidth = Math.Max(1, TimelineCanvas.ActualWidth - LabelWidth);
        return LabelWidth + (timeMs / (double)_durationMs * contentWidth);
    }

    private void TimelineCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        => RenderTimeline();

    private static string FormatDuration(long durationMs)
        => $"{durationMs / 1000d:0.#}s";

    private sealed record CameraShotEditTarget(
        Guid Id,
        CameraShotEditKind Kind);
}
