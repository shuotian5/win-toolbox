using System.Runtime.InteropServices;

namespace WinToolbox.Core;

/// <summary>
/// Thin wrapper over the service control manager. Used by the update, telemetry and
/// ASUS-service modules, all of which are "stop this service and remember how it was".
/// </summary>
internal static class Services
{
    public static uint QueryState(string name)
    {
        IntPtr scm = Native.OpenSCManagerW(null, null, Native.SC_MANAGER_CONNECT);
        if (scm == IntPtr.Zero) return 0;
        try
        {
            IntPtr svc = Native.OpenServiceW(scm, name, Native.SERVICE_QUERY_STATUS);
            if (svc == IntPtr.Zero) return 0;
            try
            {
                var status = default(Native.SERVICE_STATUS);
                return Native.QueryServiceStatus(svc, ref status) ? status.dwCurrentState : 0u;
            }
            finally { Native.CloseServiceHandle(svc); }
        }
        finally { Native.CloseServiceHandle(scm); }
    }

    public static bool Exists(string name) => QueryState(name) != 0;

    public static bool IsRunning(string name) => QueryState(name) == Native.SERVICE_RUNNING;

    public static bool TryStop(string name, out int win32Error)
    {
        win32Error = 0;
        IntPtr scm = Native.OpenSCManagerW(null, null, Native.SC_MANAGER_CONNECT);
        if (scm == IntPtr.Zero) { win32Error = Marshal.GetLastWin32Error(); return false; }

        try
        {
            IntPtr svc = Native.OpenServiceW(scm, name, Native.SERVICE_STOP | Native.SERVICE_QUERY_STATUS);
            if (svc == IntPtr.Zero) { win32Error = Marshal.GetLastWin32Error(); return false; }
            try
            {
                var status = default(Native.SERVICE_STATUS);
                if (!Native.ControlService(svc, Native.SERVICE_CONTROL_STOP, ref status))
                {
                    win32Error = Marshal.GetLastWin32Error();
                    // 1062 = already stopped, which counts as success for our purposes.
                    return win32Error == Native.ERROR_SERVICE_NOT_ACTIVE;
                }

                for (int i = 0; i < 25; i++)
                {
                    Thread.Sleep(120);
                    var probe = default(Native.SERVICE_STATUS);
                    if (Native.QueryServiceStatus(svc, ref probe) && probe.dwCurrentState == Native.SERVICE_STOPPED)
                        return true;
                }
                return QueryState(name) != Native.SERVICE_RUNNING;
            }
            finally { Native.CloseServiceHandle(svc); }
        }
        finally { Native.CloseServiceHandle(scm); }
    }

    public static bool TryStart(string name)
    {
        IntPtr scm = Native.OpenSCManagerW(null, null, Native.SC_MANAGER_CONNECT);
        if (scm == IntPtr.Zero) return false;

        try
        {
            IntPtr svc = Native.OpenServiceW(scm, name, Native.SERVICE_START | Native.SERVICE_QUERY_STATUS);
            if (svc == IntPtr.Zero) return false;
            try
            {
                if (Native.StartServiceW(svc, 0, IntPtr.Zero)) return true;
                return Marshal.GetLastWin32Error() == Native.ERROR_SERVICE_ALREADY_RUNNING;
            }
            finally { Native.CloseServiceHandle(svc); }
        }
        finally { Native.CloseServiceHandle(scm); }
    }

    /// <summary>
    /// Read <c>Start</c> from the service key. 2 = automatic, 3 = manual, 4 = disabled.
    /// </summary>
    public static int? GetStartType(string name)
    {
        using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
            @"SYSTEM\CurrentControlSet\Services\" + name);
        return key?.GetValue("Start") as int?;
    }

    /// <summary>
    /// Write <c>Start</c> bypassing the SCM, because <c>sc config</c> is refused on several
    /// protected services and spawning sc.exe would only add a process-creation event that
    /// antivirus watches for.
    /// </summary>
    public static bool SetStartType(string name, int value)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Services\" + name, writable: true);
            if (key is null) return false;
            key.SetValue("Start", value, Microsoft.Win32.RegistryValueKind.DWord);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public const int StartAuto = 2;
    public const int StartManual = 3;
    public const int StartDisabled = 4;

    /// <summary>
    /// Snapshot the start type, then disable and stop the service. Used by every module that
    /// quiets a Windows background service.
    /// </summary>
    public static void DisableWithBackup(string name)
    {
        int? current = GetStartType(name);
        if (current is null) return;

        Backup.RememberService(name, current.Value, IsRunning(name));
        SetStartType(name, StartDisabled);
        if (IsRunning(name)) TryStop(name, out _);
    }

    /// <summary>Put the service back exactly how it was found.</summary>
    public static void RestoreFromBackup(string name)
    {
        int restore = Backup.GetSavedServiceStart(name) ?? StartManual;
        SetStartType(name, restore);
        if (restore != StartDisabled && QueryState(name) != Native.SERVICE_RUNNING)
            TryStart(name);
    }
}
