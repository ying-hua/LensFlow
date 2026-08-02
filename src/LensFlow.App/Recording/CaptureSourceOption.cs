using System.Runtime.InteropServices;
using ScreenRecorderLib;

namespace LensFlow.App.Recording;

public enum CaptureSourceKind
{
    PrimaryDisplay,
    Window
}

public sealed class CaptureSourceOption
{
    public required string Label { get; init; }
    public required CaptureSourceKind Kind { get; init; }
    public nint WindowHandle { get; init; }
    public string? DeviceName { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }

    public override string ToString() => Label;

    public RecordingSourceBase CreateRecordingSource()
    {
        return Kind switch
        {
            CaptureSourceKind.PrimaryDisplay => new DisplayRecordingSource(DeviceName ?? DisplayRecordingSource.MainMonitor.DeviceName)
            {
                IsCursorCaptureEnabled = true
            },
            CaptureSourceKind.Window => new WindowRecordingSource(WindowHandle)
            {
                IsCursorCaptureEnabled = true,
                IsBorderRequired = true
            },
            _ => throw new ArgumentOutOfRangeException()
        };
    }

    public static IReadOnlyList<CaptureSourceOption> Discover(nint excludedWindow)
    {
        var result = new List<CaptureSourceOption>();
        var main = DisplayRecordingSource.MainMonitor;
        result.Add(new CaptureSourceOption
        {
            Label = "主显示器",
            Kind = CaptureSourceKind.PrimaryDisplay,
            DeviceName = main.DeviceName,
            Width = GetSystemMetrics(0),
            Height = GetSystemMetrics(1)
        });

        foreach (var window in Recorder.GetWindows()
                     .Where(window => window.Handle != excludedWindow)
                     .Where(window => !string.IsNullOrWhiteSpace(window.Title))
                     .OrderBy(window => window.Title)
                     .Take(50))
        {
            if (!GetWindowRect(window.Handle, out var rect))
            {
                continue;
            }

            var width = Math.Max(2, rect.Right - rect.Left);
            var height = Math.Max(2, rect.Bottom - rect.Top);
            result.Add(new CaptureSourceOption
            {
                Label = $"窗口 · {window.Title}",
                Kind = CaptureSourceKind.Window,
                WindowHandle = window.Handle,
                Width = width,
                Height = height
            });
        }

        return result;
    }

    public CaptureBounds GetCurrentBounds()
    {
        if (Kind == CaptureSourceKind.Window &&
            GetWindowRect(WindowHandle, out var rect))
        {
            return new CaptureBounds(
                rect.Left,
                rect.Top,
                Math.Max(2, rect.Right - rect.Left),
                Math.Max(2, rect.Bottom - rect.Top));
        }

        return new CaptureBounds(0, 0, GetSystemMetrics(0), GetSystemMetrics(1));
    }

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint windowHandle, out NativeRect rectangle);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}

public readonly record struct CaptureBounds(int Left, int Top, int Width, int Height);
