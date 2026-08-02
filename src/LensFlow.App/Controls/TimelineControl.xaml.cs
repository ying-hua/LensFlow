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

    private IReadOnlyList<VideoSegment> _segments = [];
    private IReadOnlyList<CameraShot> _shots = [];
    private long _durationMs = 1;
    private long _playheadMs;
    private Line? _playheadLine;
    private Polygon? _playheadHandle;

    public TimelineControl()
    {
        InitializeComponent();
    }

    public TimelineSelection? SelectedItem { get; private set; }

    public event EventHandler<long>? PlayheadChanged;
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

        _playheadLine = new Line
        {
            Y1 = RulerHeight - 2,
            Y2 = 150,
            Stroke = new SolidColorBrush(Color.FromRgb(255, 177, 61)),
            StrokeThickness = 2,
            IsHitTestVisible = false
        };
        TimelineCanvas.Children.Add(_playheadLine);

        _playheadHandle = new Polygon
        {
            Points = [new Point(0, 0), new Point(10, 0), new Point(5, 8)],
            Fill = new SolidColorBrush(Color.FromRgb(255, 177, 61)),
            IsHitTestVisible = false
        };
        TimelineCanvas.Children.Add(_playheadHandle);
        PositionPlayhead();
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

        SetPlayheadFromPosition(e.GetPosition(TimelineCanvas).X);
        SelectedItem = selection;
        RenderTimeline();
        SelectionChanged?.Invoke(this, selection);
        e.Handled = true;
    }

    private void TimelineCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        SetPlayheadFromPosition(e.GetPosition(TimelineCanvas).X);
        SelectedItem = null;
        RenderTimeline();
        SelectionChanged?.Invoke(this, null);
    }

    private void SetPlayheadFromPosition(double x)
    {
        var contentWidth = Math.Max(1, TimelineCanvas.ActualWidth - LabelWidth);
        var normalized = Math.Clamp((x - LabelWidth) / contentWidth, 0, 1);
        _playheadMs = (long)(_durationMs * normalized);
        PositionPlayhead();
        PlayheadChanged?.Invoke(this, _playheadMs);
    }

    private void PositionPlayhead()
    {
        if (_playheadLine is null || _playheadHandle is null)
        {
            return;
        }

        var contentWidth = Math.Max(1, TimelineCanvas.ActualWidth - LabelWidth);
        var x = LabelWidth + (_playheadMs / (double)_durationMs * contentWidth);
        _playheadLine.X1 = x;
        _playheadLine.X2 = x;
        Canvas.SetLeft(_playheadHandle, x - 5);
        Canvas.SetTop(_playheadHandle, RulerHeight - 9);
    }

    private void TimelineCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        => RenderTimeline();

    private static string FormatDuration(long durationMs)
        => $"{durationMs / 1000d:0.#}s";
}
