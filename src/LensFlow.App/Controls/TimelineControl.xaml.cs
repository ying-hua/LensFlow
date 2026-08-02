using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using LensFlow.Core.Models;

namespace LensFlow.App.Controls;

public enum TimelineItemKind
{
    Video,
    Camera
}

public sealed record TimelineSelection(TimelineItemKind Kind, Guid Id);

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
            Padding = new Thickness(12, 0, 8, 0),
            Cursor = Cursors.Hand,
            Tag = selection,
            Child = new TextBlock
            {
                Text = label,
                Foreground = Brushes.White,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        item.MouseLeftButtonDown += TimelineItem_MouseLeftButtonDown;
        Canvas.SetLeft(item, left);
        Canvas.SetTop(item, top);
        TimelineCanvas.Children.Add(item);
    }

    private void TimelineItem_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border { Tag: TimelineSelection selection })
        {
            return;
        }

        BeginPlayheadDrag(e.GetPosition(TimelineCanvas).X);
        SelectedItem = selection;
        RenderTimeline();
        SelectionChanged?.Invoke(this, selection);
        e.Handled = true;
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
        if (_isDraggingPlayhead)
        {
            return;
        }

        _previewPlayheadMs = null;
        PositionPreviewPlayhead();
    }

    private void TimelineCanvas_LostMouseCapture(object sender, MouseEventArgs e)
    {
        if (!_isDraggingPlayhead)
        {
            return;
        }

        _isDraggingPlayhead = false;
        _previewPlayheadMs = null;
        PositionPreviewPlayhead();
        ScrubCompleted?.Invoke(this, EventArgs.Empty);
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
}
