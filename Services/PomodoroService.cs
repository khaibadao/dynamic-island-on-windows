namespace DynamicIslandNotifier.Services;

using System;
using System.Linq;
using System.Windows.Threading;
using DynamicIslandNotifier.Core;

/// <summary>A Pomodoro rhythm: minutes of study, then minutes of rest.</summary>
public sealed record PomoPlan(string Key, string Name, int Study, int Break, string Hint)
{
    /// <summary>The longer rest after the 4th round.</summary>
    public int LongBreak => Study <= 30 ? 15 : 20;
}

/// <summary>
/// Pomodoro inside tree planting: study rounds grow trees (and count toward
/// unlocking new ones), with a short rest between rounds and a long rest after
/// the 4th. Giving up a round kills that tree and ends the cycle, as in free mode.
/// The state is kept in the settings, so a restart picks up where it was.
/// </summary>
public sealed class PomodoroService
{
    public const int Rounds = 4;

    public static readonly PomoPlan[] Plans =
    {
        new("short", "Ngắn", 25, 5,  "bài tập nhỏ"),
        new("mid",   "Vừa",  45, 10, "đọc tài liệu"),
        new("deep",  "Sâu",  50, 10, "code, lab"),
        new("long",  "Dài",  90, 20, "ôn thi"),
    };

    private readonly FocusService _focus;
    private readonly Dispatcher _ui;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };

    /// <summary>Anything changed (round, rest started / ended). UI thread.</summary>
    public event Action? Changed;
    /// <summary>A rest begins: (round just finished, minutes, long rest). UI thread.</summary>
    public event Action<int, int, bool>? BreakStarted;
    /// <summary>A new study round started on its own after the rest. UI thread.</summary>
    public event Action<int>? RoundStarted;
    /// <summary>All 4 rounds and the long rest done. UI thread.</summary>
    public event Action? CycleDone;

    public PomodoroService(FocusService focus)
    {
        _focus = focus;
        _ui = Dispatcher.CurrentDispatcher;
        _focus.Finished += s => _ui.BeginInvoke(new Action(() => OnRoundFinished(s)));
        _timer.Tick += (_, _) => Tick();
        if (Active) _timer.Start();
    }

    private static AppSettings S => AppSettings.Current;

    /// <summary>A Pomodoro cycle is going on (a study round or a rest).</summary>
    public bool Active => S.PomoActive;
    public int Round => Math.Clamp(S.PomoRound, 1, Rounds);
    public bool OnBreak => Active && S.PomoBreakUntilUtc != null;
    public bool LongBreakNow => OnBreak && S.PomoLongBreak;
    public bool Studying => Active && !OnBreak && _focus.IsRunning;

    public TimeSpan BreakRemaining =>
        S.PomoBreakUntilUtc is { } until && until > DateTime.UtcNow ? until - DateTime.UtcNow : TimeSpan.Zero;

    public int BreakMinutes => S.PomoBreakMinutes;

    /// <summary>The plan picked in the tab (custom = the user's own minutes).</summary>
    public static PomoPlan Selected
    {
        get
        {
            if (S.PomoPlan == "custom")
                return new PomoPlan("custom", "Tự chỉnh", Math.Clamp(S.PomoCustomStudy, 5, 180), Math.Clamp(S.PomoCustomBreak, 1, 60), "tuỳ bạn");
            return Plans.FirstOrDefault(p => p.Key == S.PomoPlan) ?? Plans[0];
        }
    }

    public void Start(PomoPlan plan, TreeKind kind)
    {
        if (_focus.IsRunning) return;
        S.PomoActive = true;
        S.PomoRound = 1;
        S.PomoStudy = plan.Study;
        S.PomoBreak = plan.Break;
        S.PomoBreakUntilUtc = null;
        S.PomoKind = kind.ToString();
        S.Save();
        _timer.Start();
        _focus.Start(plan.Study, kind);
        Changed?.Invoke();
    }

    /// <summary>End the rest now: the next round starts (or the cycle ends after the long rest).</summary>
    public void SkipBreak()
    {
        if (!OnBreak) return;
        EndBreak();
    }

    /// <summary>Stop the cycle (during a rest; during a round, giving up the tree does it).</summary>
    public void Stop()
    {
        S.PomoActive = false;
        S.PomoBreakUntilUtc = null;
        S.Save();
        _timer.Stop();
        Changed?.Invoke();
    }

    private void OnRoundFinished(FocusSession session)
    {
        if (!Active || S.PomoBreakUntilUtc != null) return;
        if (!session.Success) { Stop(); return; }   // the tree died: the cycle ends

        bool last = Round >= Rounds;
        int minutes = last ? new PomoPlan("", "", S.PomoStudy, S.PomoBreak, "").LongBreak : S.PomoBreak;
        S.PomoLongBreak = last;
        S.PomoBreakMinutes = minutes;
        S.PomoBreakUntilUtc = DateTime.UtcNow.AddMinutes(minutes);
        S.Save();
        BreakStarted?.Invoke(Round, minutes, last);
        Changed?.Invoke();
    }

    private void Tick()
    {
        if (!Active) { _timer.Stop(); return; }
        if (S.PomoBreakUntilUtc is { } until && DateTime.UtcNow >= until) EndBreak();
    }

    private void EndBreak()
    {
        bool wasLong = S.PomoLongBreak;
        S.PomoBreakUntilUtc = null;
        if (wasLong)
        {
            S.PomoActive = false;
            S.Save();
            _timer.Stop();
            CycleDone?.Invoke();
            Changed?.Invoke();
            return;
        }
        S.PomoRound = Round + 1;
        S.Save();
        _focus.Start(S.PomoStudy, TreeCatalog.Parse(S.PomoKind));
        RoundStarted?.Invoke(Round);
        Changed?.Invoke();
    }
}
