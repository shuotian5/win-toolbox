using Microsoft.Win32;

namespace WinToolbox.Core;

/// <summary>
/// Controls the PS/2 port driver (i8042prt).
///
/// Why this layer: Windows classifies the built-in keyboard as a critical system device and
/// refuses to disable its device node — <c>pnputil /disable-device</c> and the PnP API both
/// return success-ish codes while changing nothing. The keyboard only runs because i8042prt
/// starts it, so stopping that driver achieves the same result and is not blocked.
/// </summary>
internal static class Ps2Driver
{
    private const string KeyPath = @"SYSTEM\CurrentControlSet\Services\" + InternalKeyboard.DriverName;

    public const int StartBoot = 0;
    public const int StartSystem = 1;
    public const int StartAuto = 2;
    public const int StartDemand = 3;
    public const int StartDisabled = 4;

    public static int? GetStartValue()
    {
        using var key = Registry.LocalMachine.OpenSubKey(KeyPath);
        return key?.GetValue("Start") as int?;
    }

    public static bool SetStartValue(int value)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(KeyPath, writable: true);
            if (key is null) return false;
            key.SetValue("Start", value, RegistryValueKind.DWord);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static uint QueryState() => Services.QueryState(InternalKeyboard.DriverName);

    public static bool TryStop(out int win32Error) => Services.TryStop(InternalKeyboard.DriverName, out win32Error);

    public static bool TryStart() => Services.TryStart(InternalKeyboard.DriverName);

    public static string StartValueText(int value) => value switch
    {
        StartBoot => Loc.T("引导", "Boot"),
        StartSystem => Loc.T("系统", "System"),
        StartAuto => Loc.T("自动", "Automatic"),
        StartDemand => Loc.T("手动", "Manual"),
        StartDisabled => Loc.T("禁用", "Disabled"),
        _ => value.ToString(),
    };
}
