using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;
using LensFlow.Core.Models;

namespace LensFlow.App.Recording;

internal sealed class MouseCaptureService : IDisposable
{
    private const int WhMouseLowLevel = 14;
    private const int WmMouseMove = 0x0200;
    private const int WmLeftButtonDown = 0x0201;
    private const int WmRightButtonDown = 0x0204;
    private const uint WmQuit = 0x0012;
    private const long MoveSampleIntervalMs = 33;

    private readonly CaptureSourceOption _source;
    private readonly Func<long> _timestampProvider;
    private readonly ConcurrentQueue<MouseSample> _samples = new();
    private readonly ManualResetEventSlim _ready = new();
    private Thread? _thread;
    private HookProcedure? _hookProcedure;
    private nint _hook;
    private uint _threadId;
    private long _lastMoveMs;
    private volatile bool _paused;

    public MouseCaptureService(CaptureSourceOption source, Func<long> timestampProvider)
    {
        _source = source;
        _timestampProvider = timestampProvider;
    }

    public IReadOnlyList<MouseSample> Samples => _samples.ToArray();

    public void Start()
    {
        _thread = new Thread(RunMessageLoop)
        {
            IsBackground = true,
            Name = "LensFlow.MouseCapture"
        };
        _thread.Start();
        _ready.Wait(TimeSpan.FromSeconds(5));
        if (_hook == nint.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to install the mouse capture hook.");
        }
    }

    public void SetPaused(bool paused) => _paused = paused;

    public void Stop()
    {
        if (_threadId != 0)
        {
            PostThreadMessage(_threadId, WmQuit, nint.Zero, nint.Zero);
        }

        _thread?.Join(TimeSpan.FromSeconds(2));
    }

    public void Dispose()
    {
        Stop();
        _ready.Dispose();
    }

    private void RunMessageLoop()
    {
        _threadId = GetCurrentThreadId();
        _hookProcedure = HookCallback;
        _hook = SetWindowsHookEx(WhMouseLowLevel, _hookProcedure, GetModuleHandle(null), 0);
        _ready.Set();

        if (_hook == nint.Zero)
        {
            return;
        }

        while (GetMessage(out var message, nint.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref message);
            DispatchMessage(ref message);
        }

        UnhookWindowsHookEx(_hook);
        _hook = nint.Zero;
    }

    private nint HookCallback(int code, nint message, nint data)
    {
        if (code >= 0 && !_paused)
        {
            var messageId = unchecked((int)message);
            var kind = messageId switch
            {
                WmLeftButtonDown => MouseEventKind.LeftClick,
                WmRightButtonDown => MouseEventKind.RightClick,
                WmMouseMove => MouseEventKind.Move,
                _ => (MouseEventKind?)null
            };

            if (kind is not null)
            {
                var timestamp = _timestampProvider();
                if (kind != MouseEventKind.Move || timestamp - _lastMoveMs >= MoveSampleIntervalMs)
                {
                    var hookData = Marshal.PtrToStructure<LowLevelMouseHookData>(data);
                    var bounds = _source.GetCurrentBounds();
                    var x = (double)(hookData.Point.X - bounds.Left) / Math.Max(1, bounds.Width);
                    var y = (double)(hookData.Point.Y - bounds.Top) / Math.Max(1, bounds.Height);
                    if (x is >= 0 and <= 1 && y is >= 0 and <= 1)
                    {
                        _samples.Enqueue(new MouseSample(timestamp, x, y, kind.Value));
                        if (kind == MouseEventKind.Move)
                        {
                            _lastMoveMs = timestamp;
                        }
                    }
                }
            }
        }

        return CallNextHookEx(_hook, code, message, data);
    }

    private delegate nint HookProcedure(int code, nint message, nint data);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LowLevelMouseHookData
    {
        public NativePoint Point;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMessage
    {
        public nint WindowHandle;
        public uint Message;
        public nuint WParam;
        public nint LParam;
        public uint Time;
        public NativePoint Point;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowsHookEx(
        int hookId,
        HookProcedure procedure,
        nint module,
        uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(nint hook);

    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(nint hook, int code, nint message, nint data);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out NativeMessage message, nint window, uint min, uint max);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref NativeMessage message);

    [DllImport("user32.dll")]
    private static extern nint DispatchMessage(ref NativeMessage message);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostThreadMessage(uint threadId, uint message, nint wParam, nint lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandle(string? moduleName);
}
