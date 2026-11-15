using Microsoft.Win32;

namespace WinToolbox.Core;

/// <summary>
/// The two settings where the registry alone is not enough.
///
/// Sticky Keys and pointer acceleration live in the session's user-parameter block, which
/// Windows fills in at logon. Writing the registry value therefore looks successful —
/// the switch reads back "on" — while the running session keeps the old behaviour until the
/// next sign-in. Everything here goes through SystemParametersInfo so the change is live
/// in the current session, and every call is verified by reading the value back.
/// </summary>
internal static class Accessibility
{
    private const string StickyPath = @"Control Panel\Accessibility\StickyKeys";
    private const string MousePath = @"Control Panel\Mouse";

    // ------------------------------------------------------------- sticky keys

    /// <summary>Registry mirror of the Sticky Keys flags — what the card reads for its state.</summary>
    public static int ReadStickyFlags()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(StickyPath);
            if (key?.GetValue("Flags") is string raw && int.TryParse(raw, out int flags)) return flags;
            if (key?.GetValue("Flags") is int number) return number;
        }
        catch { }
        return 510;
    }

    /// <summary>
    /// What the running session actually thinks, which is what decides the popup.
    /// </summary>
    public static int? ReadStickyFlagsLive()
    {
        try
        {
            var data = new Native.STICKYKEYS { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Native.STICKYKEYS>() };
            if (Native.SystemParametersInfoSticky(Native.SPI_GETSTICKYKEYS, (uint)data.cbSize, ref data, 0))
                return data.dwFlags;
        }
        catch { }
        return null;
    }

    /// <summary>
    /// True when the five-times-Shift shortcut is live in the session right now. Used by the
    /// card's hint to tell "already off" apart from "off, but only after the next sign-in".
    /// <c>null</c> when the query itself failed.
    /// </summary>
    public static bool? StickyShortcutLive()
    {
        int? live = ReadStickyFlagsLive();
        if (live is null) return null;
        return (live.Value & Native.SKF_HOTKEYACTIVE) != 0;
    }

    /// <summary>
    /// Turn the five-times-Shift shortcut on or off, both in the session and in the registry.
    /// Returns false when the session refused the change — the caller must not report success
    /// in that case, or the user is back to "the switch is on but nothing happened".
    /// </summary>
    public static bool SetStickyHotkey(bool active, out string? error)
    {
        error = null;

        int flags = ReadStickyFlagsLive() ?? ReadStickyFlags();
        int target = active ? flags | Native.SKF_HOTKEYACTIVE : flags & ~Native.SKF_HOTKEYACTIVE;

        var data = new Native.STICKYKEYS
        {
            cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Native.STICKYKEYS>(),
            dwFlags = target,
        };

        // For SPI_SETSTICKYKEYS uiParam must be 0; the struct carries its own cbSize.
        bool ok = Native.SystemParametersInfoSticky(
            Native.SPI_SETSTICKYKEYS, 0, ref data,
            Native.SPIF_UPDATEINIFILE | Native.SPIF_SENDCHANGE);

        if (!ok)
        {
            error = "SystemParametersInfo(SPI_SETSTICKYKEYS) 被拒绝";
            return false;
        }

        // Verify against the session, not against the API's return code.
        int? live = ReadStickyFlagsLive();
        bool applied = live is null || ((live.Value & Native.SKF_HOTKEYACTIVE) != 0) == active;

        if (!applied)
        {
            error = "系统没有接受这次修改";
            return false;
        }

        return true;
    }

    // ---------------------------------------------------------------- mouse

    public static (int Threshold1, int Threshold2, int Accel) ReadMouseLive()
    {
        var buffer = new int[3];
        try
        {
            if (Native.SystemParametersInfoMouse(Native.SPI_GETMOUSE, 0, buffer, 0))
                return (buffer[0], buffer[1], buffer[2]);
        }
        catch { }
        return (6, 10, 1);
    }

    /// <summary>
    /// Enable or disable "enhance pointer precision". The API takes the raw threshold triple:
    /// all zeroes means the cursor moves exactly as far as the mouse does. The registry
    /// values are written by the card itself, so nothing is mirrored here.
    /// </summary>
    public static bool SetMouseAcceleration(bool enabled, out string? error)
    {
        error = null;

        var wanted = enabled ? new[] { 6, 10, 1 } : new[] { 0, 0, 0 };

        bool ok = Native.SystemParametersInfoMouse(
            Native.SPI_SETMOUSE, 0, wanted, Native.SPIF_UPDATEINIFILE | Native.SPIF_SENDCHANGE);

        if (!ok)
        {
            error = "SystemParametersInfo(SPI_SETMOUSE) 被拒绝";
            return false;
        }

        var after = ReadMouseLive();
        bool applied = enabled ? after.Accel != 0 : after.Accel == 0;

        if (!applied)
        {
            error = "系统没有接受这次修改";
            return false;
        }

        return true;
    }
}
