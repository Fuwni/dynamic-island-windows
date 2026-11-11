namespace DynamicIsland.Services;

public enum TimerKind { Timer, Pomodoro, Stopwatch }

/// <summary>Countdown timer, Pomodoro (25/5 ×4, then 15) and stopwatch - one at a time, like a Live Activity.</summary>
public sealed class TimerService
{
    public TimerKind Kind { get; private set; } = TimerKind.Timer;
    public bool Running { get; private set; }
    public bool Active => Running || _accumulated > TimeSpan.Zero || _remainingWhenPaused.HasValue;

    // countdown
    private TimeSpan _duration = TimeSpan.FromMinutes(5);
    private DateTime _endsAt;
    private TimeSpan? _remainingWhenPaused;

    // stopwatch
    private DateTime _startedAt;
    private TimeSpan _accumulated;

    // pomodoro
    public bool PomodoroWork { get; private set; } = true;
    public int PomodoroRound { get; private set; } = 1;
    public static readonly TimeSpan Work = TimeSpan.FromMinutes(25), ShortBreak = TimeSpan.FromMinutes(5), LongBreak = TimeSpan.FromMinutes(15);

    /// <summary>Raised when a countdown reaches zero: (title, text).</summary>
    public event Action<string, string>? Finished;

    public TimeSpan Duration => _duration;

    public void SetKind(TimerKind kind)
    {
        Reset();
        Kind = kind;
        if (kind == TimerKind.Pomodoro) { PomodoroWork = true; PomodoroRound = 1; _duration = Work; }
    }

    public void SetDuration(TimeSpan d)
    {
        if (Running) return;
        _duration = d;
        _remainingWhenPaused = null;
    }

    public void Start()
    {
        if (Running) return;
        Running = true;
        if (Kind == TimerKind.Stopwatch) _startedAt = DateTime.Now;
        else _endsAt = DateTime.Now + (_remainingWhenPaused ?? _duration);
        _remainingWhenPaused = null;
    }

    public void Pause()
    {
        if (!Running) return;
        Running = false;
        if (Kind == TimerKind.Stopwatch) _accumulated += DateTime.Now - _startedAt;
        else _remainingWhenPaused = Remaining;
    }

    public void Reset()
    {
        Running = false;
        _accumulated = TimeSpan.Zero;
        _remainingWhenPaused = null;
        if (Kind == TimerKind.Pomodoro) { PomodoroWork = true; PomodoroRound = 1; _duration = Work; }
    }

    public TimeSpan Remaining =>
        Running ? Max0(_endsAt - DateTime.Now) : _remainingWhenPaused ?? _duration;

    public TimeSpan Elapsed => _accumulated + (Running ? DateTime.Now - _startedAt : TimeSpan.Zero);

    /// <summary>0..1 for the ring.</summary>
    public double Progress => Kind == TimerKind.Stopwatch
        ? Elapsed.TotalSeconds % 60 / 60
        : _duration.TotalSeconds <= 0 ? 0 : 1 - Remaining.TotalSeconds / _duration.TotalSeconds;

    public string Display
    {
        get
        {
            var t = Kind == TimerKind.Stopwatch ? Elapsed : Remaining;
            if (Kind != TimerKind.Stopwatch) t = TimeSpan.FromSeconds(Math.Ceiling(t.TotalSeconds));
            return t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"mm\:ss");
        }
    }

    public string Phase => Kind switch
    {
        TimerKind.Pomodoro => PomodoroWork ? $"Работа · {PomodoroRound}/4" : PomodoroRound > 4 ? "Длинный перерыв" : "Перерыв",
        TimerKind.Stopwatch => Running ? "идёт" : "",
        _ => Running ? "идёт" : _remainingWhenPaused.HasValue ? "пауза" : "",
    };

    /// <summary>Call a few times per second.</summary>
    public void Tick()
    {
        if (!Running || Kind == TimerKind.Stopwatch || Remaining > TimeSpan.Zero) return;

        if (Kind == TimerKind.Timer)
        {
            Running = false;
            Finished?.Invoke("Таймер закончился", $"{(int)_duration.TotalMinutes} мин прошло");
            return;
        }

        // pomodoro: switch phase and keep going
        if (PomodoroWork)
        {
            bool longBreak = PomodoroRound >= 4;
            PomodoroWork = false;
            _duration = longBreak ? LongBreak : ShortBreak;
            if (longBreak) PomodoroRound = 5;
            Finished?.Invoke("Время отдохнуть", longBreak ? "Длинный перерыв · 15 мин" : "Перерыв · 5 мин");
        }
        else
        {
            PomodoroRound = PomodoroRound >= 4 ? 1 : PomodoroRound + 1;
            PomodoroWork = true;
            _duration = Work;
            Finished?.Invoke("За работу", $"Помодоро {PomodoroRound}/4 · 25 мин");
        }
        _endsAt = DateTime.Now + _duration;
    }

    private static TimeSpan Max0(TimeSpan t) => t < TimeSpan.Zero ? TimeSpan.Zero : t;
}
