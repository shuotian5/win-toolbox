using Microsoft.Win32;

namespace WinToolbox.Core;

internal sealed class KeyboardDevice
{
    public required string InstanceId { get; init; }
    public required string Description { get; init; }
    public uint DevInst { get; init; }
    public uint Status { get; init; }
    public uint Problem { get; init; }

    public bool IsDisabled => Problem == Native.CM_PROB_DISABLED;
    public bool IsRunning => (Status & Native.DN_STARTED) != 0 && !IsDisabled;
}

/// <summary>
/// Finds the built-in laptop keyboard.
///
/// Heuristic: it is the ACPI-enumerated device whose driver service is i8042prt, the PS/2
/// port driver. External keyboards are USB / Bluetooth HID devices and never match, so they
/// are left alone. This replaces hard-coding one machine's instance ID.
/// </summary>
internal static class InternalKeyboard
{
    public const string DriverName = "i8042prt";

    public static List<KeyboardDevice> FindAll()
    {
        var found = new List<KeyboardDevice>();

        using var acpi = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\ACPI");
        if (acpi is null) return found;

        foreach (var devName in acpi.GetSubKeyNames())
        {
            using var devKey = acpi.OpenSubKey(devName);
            if (devKey is null) continue;

            foreach (var instName in devKey.GetSubKeyNames())
            {
                using var instKey = devKey.OpenSubKey(instName);
                if (instKey is null) continue;

                if (instKey.GetValue("Service") is not string svc) continue;
                if (!string.Equals(svc, DriverName, StringComparison.OrdinalIgnoreCase)) continue;

                string instanceId = $@"ACPI\{devName}\{instName}";
                if (Native.CM_Locate_DevNodeW(out uint devInst, instanceId, 0) != Native.CR_SUCCESS) continue;
                if (Native.CM_Get_DevNode_Status(out uint status, out uint problem, devInst, 0) != Native.CR_SUCCESS) continue;

                found.Add(new KeyboardDevice
                {
                    InstanceId = instanceId,
                    Description = Describe(instKey),
                    DevInst = devInst,
                    Status = status,
                    Problem = problem,
                });
            }
        }

        return found.OrderByDescending(k => k.IsRunning).ThenByDescending(k => k.IsDisabled).ToList();
    }

    internal static string Describe(RegistryKey instKey)
    {
        var text = instKey.GetValue("FriendlyName") as string
                   ?? instKey.GetValue("DeviceDesc") as string
                   ?? "Unknown device";

        // DeviceDesc is stored as "@file,-id;Localized name" — keep the readable half.
        int semi = text.LastIndexOf(';');
        if (semi >= 0 && semi < text.Length - 1) text = text[(semi + 1)..];
        return text.Trim();
    }

    /// <summary>
    /// All present devices in the Keyboard setup class, for the diagnostics panel.
    /// </summary>
    public static List<string> ListAllKeyboards()
    {
        var lines = new List<string>();
        using var enumKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum");
        if (enumKey is null) return lines;

        foreach (var bus in new[] { "ACPI", "HID", "USB", "BTHENUM", "ROOT" })
        {
            using var busKey = enumKey.OpenSubKey(bus);
            if (busKey is null) continue;
            foreach (var devName in busKey.GetSubKeyNames())
            {
                using var devKey = busKey.OpenSubKey(devName);
                if (devKey is null) continue;
                foreach (var instName in devKey.GetSubKeyNames())
                {
                    using var instKey = devKey.OpenSubKey(instName);
                    if (instKey is null) continue;
                    if (instKey.GetValue("ClassGUID") is not string guid) continue;
                    if (!string.Equals(guid, KeyboardClassGuid, StringComparison.OrdinalIgnoreCase)) continue;

                    string id = $@"{bus}\{devName}\{instName}";
                    if (Native.CM_Locate_DevNodeW(out _, id, 0) != Native.CR_SUCCESS) continue;

                    string svc = instKey.GetValue("Service") as string ?? "-";
                    lines.Add($"{id}   service={svc}   {Describe(instKey)}");
                }
            }
        }
        return lines;
    }

    /// <summary>HKLM\SYSTEM\CurrentControlSet\Control\Class\{4d36e96b-…} — the "Keyboard" setup class.</summary>
    public const string KeyboardClassGuid = "{4d36e96b-e325-11ce-bfc1-08002be10318}";
}
