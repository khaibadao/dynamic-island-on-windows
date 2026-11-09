namespace DynamicIslandNotifier.Services;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;

/// <summary>
/// The speakers / headphones Windows can play through, and switching which one
/// is used (the same as picking it in the taskbar's sound menu). Windows has no
/// public call for that; this uses the one its own sound settings use
/// (IPolicyConfig), like the well-known sound switcher apps.
/// </summary>
public static class AudioOutputs
{
    public sealed record Output(string Id, string Name, bool IsDefault, bool Headphones);

    /// <summary>Plugged-in outputs, the one in use first.</summary>
    public static IReadOnlyList<Output> List()
    {
        try
        {
            using var en = new MMDeviceEnumerator();
            string? current = null;
            try { using var d = en.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia); current = d.ID; } catch { }
            var list = new List<Output>();
            foreach (var device in en.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                using (device)
                {
                    string name = device.FriendlyName;
                    string lower = name.ToLowerInvariant();
                    bool phones = lower.Contains("headphone") || lower.Contains("headset") || lower.Contains("tai nghe")
                               || lower.Contains("airpods") || lower.Contains("buds") || lower.Contains("hands-free");
                    list.Add(new Output(device.ID, ShortName(name), device.ID == current, phones));
                }
            }
            return list.OrderByDescending(o => o.IsDefault).ThenBy(o => o.Name).ToList();
        }
        catch (Exception ex)
        {
            DebugLog.Note($"AudioOutputs: {ex.Message}");
            return Array.Empty<Output>();
        }
    }

    /// <summary>"Speakers (Realtek(R) Audio)" → "Speakers · Realtek(R) Audio".</summary>
    private static string ShortName(string name)
    {
        int open = name.IndexOf(" (", StringComparison.Ordinal);
        if (open <= 0 || !name.EndsWith(')')) return name;
        return $"{name[..open]} · {name[(open + 2)..^1]}";
    }

    /// <summary>Plays everything through this output from now on. False if Windows refused.</summary>
    public static bool Use(string id)
    {
        try
        {
            var config = (IPolicyConfig)new PolicyConfigClient();
            foreach (var role in new[] { ERole.Console, ERole.Multimedia, ERole.Communications })
                Marshal.ThrowExceptionForHR(config.SetDefaultEndpoint(id, role));
            CrashLog.Write("Sound output switched");
            return true;
        }
        catch (Exception ex)
        {
            CrashLog.Write($"Sound output switch failed: {ex.Message}");
            return false;
        }
    }

    private enum ERole { Console = 0, Multimedia = 1, Communications = 2 }

    [ComImport, Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")]
    private class PolicyConfigClient { }

    // Only SetDefaultEndpoint is called; the methods before it keep the layout.
    [ComImport, Guid("f8679f50-850a-41cf-9c72-430f290290c8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPolicyConfig
    {
        [PreserveSig] int GetMixFormat([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr format);
        [PreserveSig] int GetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string id, int isDefault, IntPtr format);
        [PreserveSig] int ResetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string id);
        [PreserveSig] int SetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr endpointFormat, IntPtr mixFormat);
        [PreserveSig] int GetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string id, int isDefault, IntPtr defaultPeriod, IntPtr minimumPeriod);
        [PreserveSig] int SetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr period);
        [PreserveSig] int GetShareMode([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr mode);
        [PreserveSig] int SetShareMode([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr mode);
        [PreserveSig] int GetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr key, IntPtr value);
        [PreserveSig] int SetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr key, IntPtr value);
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string id, ERole role);
        [PreserveSig] int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string id, int visible);
    }
}
