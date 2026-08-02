using System.Diagnostics;

namespace LensFlow.App.Recording;

internal sealed class RecordingSessionClock
{
    private readonly Stopwatch _stopwatch = new();
    private TimeSpan _pausedDuration;
    private TimeSpan _pauseStarted;
    private bool _paused;

    public long ElapsedMilliseconds
    {
        get
        {
            var paused = _paused
                ? _pausedDuration + (_stopwatch.Elapsed - _pauseStarted)
                : _pausedDuration;
            return Math.Max(0, (long)(_stopwatch.Elapsed - paused).TotalMilliseconds);
        }
    }

    public void Start() => _stopwatch.Start();

    public void Pause()
    {
        if (_paused)
        {
            return;
        }

        _pauseStarted = _stopwatch.Elapsed;
        _paused = true;
    }

    public void Resume()
    {
        if (!_paused)
        {
            return;
        }

        _pausedDuration += _stopwatch.Elapsed - _pauseStarted;
        _paused = false;
    }

    public void Stop()
    {
        if (_paused)
        {
            Resume();
        }

        _stopwatch.Stop();
    }
}
