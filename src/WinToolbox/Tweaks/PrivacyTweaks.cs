using Microsoft.Win32;

namespace WinToolbox.Tweaks;

/// <summary>
/// Drop the diagnostic data collection to its lowest level and stop the DiagTrack service.
/// On Home editions the policy value is advisory, but the service stop is what actually
/// cuts the traffic.
/// </summary>
internal sealed class TelemetryTweak : Core.RegistryTweak
{
    private const string Policy = @"SOFTWARE\Policies\Microsoft\Windows\DataCollection";

    public override string Id => "privacy-telemetry";
    public override string Category => Core.Categories.Privacy;

    public override string Title => Core.Loc.T("关闭诊断数据上传", "Minimise diagnostic data");

    public override string Description => Core.Loc.T(
        "把诊断数据降到最低档，并停掉负责上传的「连接用户体验和遥测」服务。",
        "Sets diagnostic data to the lowest level and stops the Connected User Experiences and Telemetry service.");

    protected override IReadOnlyList<Core.RegValue> Values => new[]
    {
        new Core.RegValue(RegistryHive.LocalMachine, Policy, "AllowTelemetry", 0, null),
        new Core.RegValue(RegistryHive.LocalMachine, Policy, "AllowDeviceNameInTelemetry", 0, null),
    };

    /// <summary>Touches the DiagTrack service — never flipped by the automated self-test.</summary>
    public override bool SafeToSelfTest => false;

    protected override bool AfterApply(bool enable, out string? note)
    {
        note = null;
        if (!Core.Services.Exists("DiagTrack")) return true;

        if (enable)
        {
            Core.Services.DisableWithBackup("DiagTrack");
            if (Core.Services.GetStartType("DiagTrack") != Core.Services.StartDisabled)
                note = Core.Loc.T("DiagTrack 受系统保护未能禁用，策略层面仍然生效。",
                                  "DiagTrack is protected and could not be disabled; the policy still applies.");
        }
        else
        {
            Core.Services.RestoreFromBackup("DiagTrack");
        }

        return true;
    }
}

/// <summary>Remove the Copilot button and the policy that launches it.</summary>
internal sealed class CopilotTweak : Core.RegistryTweak
{
    private const string Advanced = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";

    public override string Id => "privacy-copilot";
    public override string Category => Core.Categories.Privacy;

    public override string Title => Core.Loc.T("关闭 Copilot", "Turn off Copilot");

    public override string Description => Core.Loc.T(
        "隐藏任务栏的 Copilot 按钮，并禁止它随系统启动。不卸载 Edge 或其它组件。",
        "Hides the Copilot button and stops it launching. Edge and other components are untouched.");

    protected override IReadOnlyList<Core.RegValue> Values => new[]
    {
        new Core.RegValue(RegistryHive.CurrentUser,
            @"Software\Policies\Microsoft\Windows\WindowsCopilot", "TurnOffWindowsCopilot", 1, null),
        new Core.RegValue(RegistryHive.CurrentUser, Advanced, "ShowCopilotButton", 0, 1),
    };

    /// <summary>The taskbar button only disappears once Explorer re-reads its settings.</summary>
    public override bool NeedsExplorerRefresh => true;

    public override Core.TweakState Query()
    {
        var state = base.Query();

        // 24H2 turned Copilot into a packaged app and the policy stopped being honoured.
        // Saying so beats letting people flip a switch that cannot do anything.
        if (Environment.OSVersion.Version.Build >= 26100)
            Hint = Core.Loc.T("24H2 起 Copilot 是独立应用，该策略可能已不生效",
                              "Since 24H2 Copilot is a separate app; this policy may no longer apply");

        return state;
    }
}

/// <summary>The weather/feed widget on the taskbar — a surprising amount of background work.</summary>
internal sealed class WidgetsTweak : Core.RegistryTweak
{
    private const string Advanced = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";

    public override string Id => "privacy-widgets";
    public override string Category => Core.Categories.Privacy;

    public override string Title => Core.Loc.T("关闭任务栏小组件", "Turn off taskbar widgets");

    public override string Description => Core.Loc.T(
        "去掉左下角的天气和资讯面板，连带关掉它背后的推送进程。",
        "Removes the weather and news panel, and the feed process behind it.");

    protected override IReadOnlyList<Core.RegValue> Values => new[]
    {
        new Core.RegValue(RegistryHive.LocalMachine,
            @"SOFTWARE\Policies\Microsoft\Dsh", "AllowNewsAndInterests", 0, null),
        new Core.RegValue(RegistryHive.CurrentUser, Advanced, "TaskbarDa", 0, 1),
    };

    public override bool NeedsExplorerRefresh => true;
}

/// <summary>
/// Everything that advertises at you: Start menu suggestions, "tips", silently installed
/// promoted apps, lock screen overlays, and the advertising ID.
/// </summary>
internal sealed class AdsTweak : Core.RegistryTweak
{
    private const string Cdm = @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager";

    public override string Id => "privacy-ads";
    public override string Category => Core.Categories.Privacy;

    public override string Title => Core.Loc.T("关闭系统广告与推荐", "Turn off ads and suggestions");

    public override string Description => Core.Loc.T(
        "开始菜单推荐、锁屏推广、悄悄装上的推广应用、广告 ID，一次全部关掉。",
        "Start menu suggestions, lock screen promos, silently installed promoted apps and the advertising ID — all at once.");

    protected override IReadOnlyList<Core.RegValue> Values => new[]
    {
        new Core.RegValue(RegistryHive.CurrentUser, Cdm, "SubscribedContent-338388Enabled", 0, null),
        new Core.RegValue(RegistryHive.CurrentUser, Cdm, "SubscribedContent-338389Enabled", 0, null),
        new Core.RegValue(RegistryHive.CurrentUser, Cdm, "SubscribedContent-338387Enabled", 0, null),
        new Core.RegValue(RegistryHive.CurrentUser, Cdm, "SubscribedContent-353694Enabled", 0, null),
        new Core.RegValue(RegistryHive.CurrentUser, Cdm, "SubscribedContent-353696Enabled", 0, null),
        new Core.RegValue(RegistryHive.CurrentUser, Cdm, "SystemPaneSuggestionsEnabled", 0, null),
        new Core.RegValue(RegistryHive.CurrentUser, Cdm, "SilentInstalledAppsEnabled", 0, 1),
        new Core.RegValue(RegistryHive.CurrentUser, Cdm, "SoftLandingEnabled", 0, 1),
        new Core.RegValue(RegistryHive.CurrentUser, Cdm, "RotatingLockScreenOverlayEnabled", 0, 1),
        new Core.RegValue(RegistryHive.CurrentUser,
            @"Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo", "Enabled", 0, 1),
    };
}

/// <summary>
/// Keep the search box local instead of sending every query to Bing.
///
/// Uses the documented policy keys rather than the Search key under CurrentVersion: on
/// recent Windows 11 builds those values are write-protected, and the policy path is what
/// the equivalent Group Policy setting writes anyway.
/// </summary>
internal sealed class WebSearchTweak : Core.RegistryTweak
{
    private const string ShellPolicy = @"Software\Policies\Microsoft\Windows\Explorer";
    private const string SearchPolicy = @"SOFTWARE\Policies\Microsoft\Windows\Windows Search";

    public override string Id => "privacy-web-search";
    public override string Category => Core.Categories.Privacy;

    public override string Title => Core.Loc.T("关闭搜索栏联网", "Keep search offline");

    public override string Description => Core.Loc.T(
        "开始菜单搜索只查本机文件，不把关键词发给 Bing，也不弹网页结果。",
        "Start menu search only looks at local files — no keywords sent to Bing, no web results.");

    protected override IReadOnlyList<Core.RegValue> Values => new[]
    {
        new Core.RegValue(RegistryHive.CurrentUser, ShellPolicy, "DisableSearchBoxSuggestions", 1, null),
        new Core.RegValue(RegistryHive.LocalMachine, SearchPolicy, "DisableWebSearch", 1, null),
        new Core.RegValue(RegistryHive.LocalMachine, SearchPolicy, "ConnectedSearchUseWeb", 0, null),
    };

    /// <summary>Search reads the policy when its process starts, so Explorer has to reload.</summary>
    public override bool NeedsExplorerRefresh => true;
}
