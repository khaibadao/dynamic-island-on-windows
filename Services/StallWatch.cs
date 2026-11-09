namespace DynamicIslandNotifier.Services;

using System;
using System.Diagnostics;
using System.Threading;
using System.Windows.Threading;

/// <summary>
/// Notices when the screen freezes: a background thread asks the UI thread to
/// answer every quarter second; if the answer takes over 150 ms, it writes to
/// startup.log how long and what the panel was doing ("tab focus"). No content,
/// just times, so a laggy moment can be found exactly.
/// </summary>
public static class StallWatch
{
    /// <summary>What the UI was busy with (set by the panel).</summary>
    public static volatile string Doing = "idle";

    private static int _logged;

    public static void Start(Dispatcher ui)
    {
        var thread = new Thread(() =>
        {
            var sw = new Stopwatch();
            while (true)
            {
                Thread.Sleep(250);
                sw.Restart();
                var op = ui.BeginInvoke(DispatcherPriority.Input, new Action(() => { }));
                if (op.Wait(TimeSpan.FromSeconds(5)) == DispatcherOperationStatus.Aborted) return;
                long ms = sw.ElapsedMilliseconds;
                // At most 200 lines per run, so the log stays small
                if (ms > 150 && _logged < 200)
                {
                    _logged++;
                    CrashLog.Write($"UI stall {ms} ms ({Doing})");
                }
            }
        }) { IsBackground = true, Name = "StallWatch", Priority = ThreadPriority.BelowNormal };
        thread.Start();
    }
}
