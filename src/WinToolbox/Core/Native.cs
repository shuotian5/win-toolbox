using System.Runtime.InteropServices;

namespace WinToolbox.Core;

/// <summary>
/// P/Invoke surface for cfgmgr32 (device nodes), advapi32 (service control) and kernel32.
/// Everything the toolbox needs to talk to Windows without a NuGet package or a PowerShell
/// child process — both would either bloat the exe or trip antivirus heuristics.
/// </summary>
internal static class Native
{
    // ---------------------------------------------------------------- cfgmgr32

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    internal static extern int CM_Locate_DevNodeW(out uint pdnDevInst, string pDeviceID, uint ulFlags);

    [DllImport("cfgmgr32.dll")]
    internal static extern int CM_Disable_DevNode(uint dnDevInst, uint ulFlags);

    [DllImport("cfgmgr32.dll")]
    internal static extern int CM_Enable_DevNode(uint dnDevInst, uint ulFlags);

    [DllImport("cfgmgr32.dll")]
    internal static extern int CM_Get_DevNode_Status(out uint pulStatus, out uint pulProblemNumber, uint dnDevInst, uint ulFlags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    internal static extern int CM_Get_Device_IDW(uint dnDevInst, [Out] char[] buffer, uint bufferLen, uint ulFlags);

    internal const uint CR_SUCCESS = 0;
    internal const uint CR_NO_SUCH_DEVNODE = 0x0000000D;

    internal const uint DN_DRIVER_LOADED = 0x00000002;
    internal const uint DN_STARTED = 0x00000008;
    internal const uint DN_HAS_PROBLEM = 0x00000400;
    internal const uint DN_DISABLEABLE = 0x00002000;

    /// <summary>CM_PROB_DISABLED — the standard "this device is disabled" problem code.</summary>
    internal const uint CM_PROB_DISABLED = 22;

    // ---------------------------------------------------------------- advapi32

    [StructLayout(LayoutKind.Sequential)]
    internal struct SERVICE_STATUS
    {
        public uint dwServiceType;
        public uint dwCurrentState;
        public uint dwControlsAccepted;
        public uint dwWin32ExitCode;
        public uint dwServiceSpecificExitCode;
        public uint dwCheckPoint;
        public uint dwWaitHint;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr OpenSCManagerW(string? lpMachineName, string? lpDatabaseName, uint dwDesiredAccess);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr OpenServiceW(IntPtr hSCManager, string lpServiceName, uint dwDesiredAccess);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ControlService(IntPtr hService, uint dwControl, ref SERVICE_STATUS lpServiceStatus);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool StartServiceW(IntPtr hService, uint dwNumServiceArgs, IntPtr lpServiceArgVectors);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool QueryServiceStatus(IntPtr hService, ref SERVICE_STATUS lpServiceStatus);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CloseServiceHandle(IntPtr hSCObject);

    internal const uint SC_MANAGER_CONNECT = 0x0001;
    internal const uint SERVICE_QUERY_STATUS = 0x0004;
    internal const uint SERVICE_START = 0x0010;
    internal const uint SERVICE_STOP = 0x0020;
    internal const uint SERVICE_CONTROL_STOP = 0x00000001;

    internal const uint SERVICE_STOPPED = 0x00000001;
    internal const uint SERVICE_START_PENDING = 0x00000002;
    internal const uint SERVICE_STOP_PENDING = 0x00000003;
    internal const uint SERVICE_RUNNING = 0x00000004;

    internal const int ERROR_SERVICE_DOES_NOT_EXIST = 1060;
    internal const int ERROR_SERVICE_CANNOT_ACCEPT_CTRL = 1052;
    internal const int ERROR_SERVICE_NOT_ACTIVE = 1062;
    internal const int ERROR_SERVICE_ALREADY_RUNNING = 1056;

    // ---------------------------------------------------------------- kernel32

    /// <summary>
    /// Read the language straight from Windows. The exe is published with
    /// InvariantGlobalization, so CultureInfo would always report the invariant culture.
    /// </summary>
    [DllImport("kernel32.dll")]
    internal static extern ushort GetUserDefaultUILanguage();

    /// <summary>
    /// Attach to the console that launched us. A WinExe has no console of its own, so
    /// <c>--apply</c> output would otherwise vanish when run from a terminal.
    /// </summary>
    [DllImport("kernel32.dll")]
    internal static extern bool AttachConsole(int dwProcessId);

    internal const int ATTACH_PARENT_PROCESS = -1;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    internal static extern int SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);

    internal const int SHCNE_ASSOCCHANGED = 0x08000000;
    internal const uint SHCNF_IDLIST = 0x0000;

    /// <summary>Broadcast a settings change so Explorer picks it up without a logon cycle.</summary>
    internal static void RefreshShell() => SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
}
