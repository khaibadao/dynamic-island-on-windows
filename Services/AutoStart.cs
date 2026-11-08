namespace DynamicIslandNotifier.Services;

using System;
using Microsoft.Win32;

/// <summary>
/// "Start with Windows", two ways at once (no admin rights needed):
/// a scheduled task that starts the app the moment you sign in, and the usual
/// startup list (HKCU\…\Run, also shown in Task Manager → Startup apps) as a
/// fallback. Windows queues the startup list behind everything else, which on
/// a busy PC can mean many minutes; the task doesn't wait in that queue.
/// Only one copy ever runs.
/// </summary>
public static class AutoStart
{
    private const string RunKey    = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "DynamicIslandNotifier";

    private static string? ExePath => Environment.ProcessPath;

    public static bool IsEnabled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey);
                return key?.GetValue(ValueName) is string;
            }
            catch { return false; }
        }
    }

    public static void Enable()
    {
        if (string.IsNullOrEmpty(ExePath)) return;
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            key.SetValue(ValueName, $"\"{ExePath}\" --autostart");
        }
        catch (Exception ex) { DebugLog.Note($"AutoStart enable failed: {ex.Message}"); }
        if (!IsBlockedInTaskManager)
        {
            var exe = ExePath;
            System.Threading.Tasks.Task.Run(() => CreateTask(exe));   // schtasks takes a moment: not on the UI thread
        }
    }

    private const string TaskName = "DynamicIslandNotifier";

    /// <summary>A task "at sign-in" for this user only, normal priority, no time limit, also on battery.</summary>
    private static void CreateTask(string exe)
    {
        string? file = null;
        try
        {
            string user = System.Security.Principal.WindowsIdentity.GetCurrent().Name;
            string X(string v) => System.Security.SecurityElement.Escape(v) ?? "";
            string xml = $@"<?xml version=""1.0"" encoding=""UTF-16""?>
<Task version=""1.2"" xmlns=""http://schemas.microsoft.com/windows/2004/02/mit/task"">
  <RegistrationInfo><Description>Mở Dynamic Island ngay khi đăng nhập</Description></RegistrationInfo>
  <Triggers><LogonTrigger><Enabled>true</Enabled><UserId>{X(user)}</UserId></LogonTrigger></Triggers>
  <Principals><Principal id=""Author""><UserId>{X(user)}</UserId><LogonType>InteractiveToken</LogonType><RunLevel>LeastPrivilege</RunLevel></Principal></Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
    <Priority>4</Priority>
    <StartWhenAvailable>true</StartWhenAvailable>
    <AllowStartOnDemand>true</AllowStartOnDemand>
    <Enabled>true</Enabled>
  </Settings>
  <Actions Context=""Author""><Exec><Command>{X(exe)}</Command><Arguments>--autostart</Arguments></Exec></Actions>
</Task>";
            file = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"DynamicIsland-task-{Guid.NewGuid():N}.xml");
            System.IO.File.WriteAllText(file, xml, System.Text.Encoding.Unicode);
            int code = Schtasks($"/Create /TN \"{TaskName}\" /XML \"{file}\" /F");
            CrashLog.Write(code == 0 ? "Start at sign-in: task ready" : $"Start at sign-in: task not created ({code}), using the startup list");
        }
        catch (Exception ex) { CrashLog.Write($"Start at sign-in: {ex.Message}"); }
        finally { try { if (file != null) System.IO.File.Delete(file); } catch { } }
    }

    private static void DeleteTask()
    {
        try { Schtasks($"/Delete /TN \"{TaskName}\" /F"); } catch { }
    }

    private static int Schtasks(string args)
    {
        using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("schtasks.exe", args)
        {
            CreateNoWindow = true, UseShellExecute = false, WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true, RedirectStandardError = true
        });
        if (p == null) return -1;
        p.WaitForExit(10000);
        return p.HasExited ? p.ExitCode : -1;
    }

    public static void Disable()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        catch (Exception ex) { DebugLog.Note($"AutoStart disable failed: {ex.Message}"); }
        DeleteTask();
    }

    /// <summary>
    /// Call at startup. Turns auto-start on the first time the app runs (and
    /// says so on the island), and keeps the saved path current if the .exe
    /// was moved or updated. Returns true the very first time.
    /// </summary>
    public static bool EnsureOnStartup(AppSettings settings)
    {
        if (!settings.AutoStartConfigured)
        {
            Enable();
            settings.AutoStartConfigured = true;
            settings.Save();
            return true;
        }
        if (IsEnabled && !IsBlockedInTaskManager) Enable();   // refresh the path (and the sign-in task)
        else if (IsBlockedInTaskManager) DeleteTask();        // switched off in Task Manager: no early start either
        return false;
    }

    private const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    /// <summary>
    /// Switched off in Task Manager → Startup apps. That's your choice and is
    /// left alone; the tray menu just shows "Start with Windows" as off.
    /// </summary>
    public static bool IsBlockedInTaskManager
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(ApprovedKey);
                return key?.GetValue(ValueName) is byte[] state && state.Length > 0 && (state[0] & 1) == 1;
            }
            catch { return false; }
        }
    }

    /// <summary>Starts with Windows for real: on in our list and not switched off in Task Manager.</summary>
    public static bool IsActive => IsEnabled && !IsBlockedInTaskManager;

    /// <summary>Turning it on from the tray menu is your choice too, so it also clears Task Manager's switch.</summary>
    public static void EnableByUser()
    {
        Enable();
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(ApprovedKey, writable: true);
            if (key?.GetValue(ValueName) is byte[] state && state.Length > 0 && (state[0] & 1) == 1)
            {
                var on = new byte[12];
                on[0] = 0x02;
                key.SetValue(ValueName, on, RegistryValueKind.Binary);
            }
        }
        catch (Exception ex) { CrashLog.Error("startup switch", ex); }
    }
}
