using Microsoft.Win32;

namespace WinToolbox.Tweaks;

/// <summary>
/// Turn off automatic Windows Update.
///
/// This is the most requested — and most consequential — card in any toolbox. It uses the
/// policy path rather than hacking the update engine, and it also disables the three services
/// that would otherwise re-enable it: wuauserv, UsoSvc and WaaSMedicSvc (the "medic" is the
/// component that periodically repairs whatever you disabled).
///
/// Every value is snapshotted, so "off" restores the machine to a normal, updating state.
/// </summary>
internal sealed class WindowsUpdateTweak : Core.RegistryTweak
{
    private const string PolicyAu = @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU";

    public override string Id => "wu-disable";
    public override string Category => Core.Categories.Updates;

    public override string Title => Core.Loc.T("彻底禁用自动更新", "Disable automatic updates");

    public override string Description => Core.Loc.T(
        "不再自动下载和安装更新，同时停掉更新服务。想更新时把它关掉再手动检查即可。",
        "Stops downloads and installs, and quiets the update services. Turn it back off to update manually.");

    public override string? Warning => Core.Loc.T(
        "安全补丁也会一起停掉。建议每两三个月临时关掉这个开关、检查一次更新，再重新打开。",
        "Security patches stop arriving too. Turn this off every couple of months, install updates, then re-enable.");

    private static readonly string[] UpdateServices = { "wuauserv", "UsoSvc", "WaaSMedicSvc", "DoSvc" };

    /// <summary>Touches update services — never flipped by the automated self-test.</summary>
    public override bool SafeToSelfTest => false;

    protected override IReadOnlyList<Core.RegValue> Values => new[]
    {
        new Core.RegValue(RegistryHive.LocalMachine, PolicyAu, "NoAutoUpdate", 1, null),
        new Core.RegValue(RegistryHive.LocalMachine, PolicyAu, "AUOptions", 2, null),
    };

    protected override bool AfterApply(bool enable, out string? note)
    {
        note = null;
        var refused = new List<string>();

        foreach (var name in UpdateServices)
        {
            if (!Core.Services.Exists(name)) continue;
            if (enable)
            {
                Core.Services.DisableWithBackup(name);
                // wuauserv / UsoSvc / WaaSMedicSvc are partly owned by TrustedInstaller, so a
                // plain admin token can be refused. Say so instead of claiming four services
                // were stopped when two were not.
                if (Core.Services.GetStartType(name) != Core.Services.StartDisabled)
                    refused.Add(name);
            }
            else
            {
                Core.Services.RestoreFromBackup(name);
            }
        }

        if (refused.Count > 0)
            note = Core.Loc.T(
                $"{refused.Count} 个服务受系统保护未能停用（{string.Join(", ", refused)}），策略层面仍然生效。",
                $"{refused.Count} protected service(s) could not be stopped ({string.Join(", ", refused)}); the policy still applies.");

        return true;
    }
}

/// <summary>Updates may still install; they just cannot reboot the machine under you.</summary>
internal sealed class NoAutoRestartTweak : Core.RegistryTweak
{
    private const string PolicyAu = @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU";

    public override string Id => "wu-no-restart";
    public override string Category => Core.Categories.Updates;

    public override string Title => Core.Loc.T("禁止更新后自动重启", "Never auto-restart after updates");

    public override string Description => Core.Loc.T(
        "更新装好了也只等你自己重启，不会在游戏或渲染到一半时自动重启。",
        "Updates still install, but Windows waits for you to restart instead of doing it mid-game.");

    protected override IReadOnlyList<Core.RegValue> Values => new[]
    {
        new Core.RegValue(RegistryHive.LocalMachine, PolicyAu, "NoAutoRebootWithLoggedOnUsers", 1, null),
    };
}

/// <summary>
/// Keep Windows Update out of the driver business. ASUS laptops get their GPU/chipset
/// drivers from the vendor; an older driver pushed by Windows Update is a common cause of
/// broken brightness control and stutter.
/// </summary>
internal sealed class NoDriverUpdateTweak : Core.RegistryTweak
{
    public override string Id => "wu-no-drivers";
    public override string Category => Core.Categories.Updates;

    public override string Title => Core.Loc.T("阻止更新推送驱动", "Block driver updates from Windows Update");

    public override string Description => Core.Loc.T(
        "Windows 更新不再替换显卡、芯片组驱动。笔记本的驱动建议用厂商自带的方式装。",
        "Windows Update stops replacing GPU and chipset drivers. On laptops the vendor driver is usually the one that works.");

    protected override IReadOnlyList<Core.RegValue> Values => new[]
    {
        new Core.RegValue(RegistryHive.LocalMachine,
            @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate", "ExcludeWUDriversInQualityUpdate", 1, null),
        new Core.RegValue(RegistryHive.LocalMachine,
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\DriverSearching", "SearchOrderConfig", 0, 1),
        new Core.RegValue(RegistryHive.LocalMachine,
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Device Metadata", "PreventDeviceMetadataFromNetwork", 1, 0),
    };
}
