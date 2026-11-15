using Microsoft.Win32;

namespace WinToolbox.Tweaks;

/// <summary>
/// Restore the full Windows 10 right-click menu. Windows 11 replaced it with a short list
/// that hides "Send to", "Troubleshoot" and most third-party shell extensions behind
/// "Show more options". The known shell switch is an empty InprocServer32 for this CLSID.
/// </summary>
internal sealed class ClassicContextMenuTweak : Core.RegistryTweak
{
    private const string Clsid =
        @"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32";

    public override string Id => "appearance-context-menu";
    public override string Category => Core.Categories.Appearance;

    public override string Title => Core.Loc.T("恢复 Win10 右键菜单", "Restore the Windows 10 context menu");

    public override string Description => Core.Loc.T(
        "右键直接出完整菜单，不用每次再点「显示更多选项」。改完重启一次资源管理器或重新登录后生效。",
        "Right-click shows the full menu again instead of hiding everything behind \"Show more options\". Restart Explorer or sign out and back in to see it.");

    protected override IReadOnlyList<Core.RegValue> Values => new[]
    {
        new Core.RegValue(RegistryHive.CurrentUser, Clsid, "", "", null, RegistryValueKind.String),
    };

    // Explorer is deliberately NOT restarted here: this process runs elevated, and a shell
    // started from an elevated process stays elevated, which breaks drag-and-drop into
    // normal apps. Signing out is the clean way to apply it.

    public override Core.TweakState Query()
    {
        if (Environment.OSVersion.Version.Build < 22000) return Core.TweakState.Unavailable;
        return base.Query();
    }
}

/// <summary>Show the real extension on every file — the single best defence against .exe.txt tricks.</summary>
internal sealed class ShowExtensionsTweak : Core.RegistryTweak
{
    private const string Advanced = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";

    public override string Id => "appearance-extensions";
    public override string Category => Core.Categories.Appearance;

    public override string Title => Core.Loc.T("显示文件扩展名", "Show file extensions");

    public override string Description => Core.Loc.T(
        "每个文件都带上 .exe、.pdf 这样的后缀，能一眼看出伪装成文档的可执行文件。",
        "Every file shows its real suffix, so a disguised executable cannot pass as a document.");

    protected override IReadOnlyList<Core.RegValue> Values => new[]
    {
        new Core.RegValue(RegistryHive.CurrentUser, Advanced, "HideFileExt", 0, 1),
    };

    protected override void AfterApply(bool enable) => Core.Native.RefreshShell();
}

/// <summary>Windows 11 only: move the taskbar icons back to the left.</summary>
internal sealed class TaskbarLeftTweak : Core.RegistryTweak
{
    private const string Advanced = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";

    public override string Id => "appearance-taskbar-left";
    public override string Category => Core.Categories.Appearance;

    public override string Title => Core.Loc.T("任务栏图标左对齐", "Align taskbar to the left");

    public override string Description => Core.Loc.T(
        "把 Win11 居中的任务栏图标挪回左边，跟 Win10 一致。重启资源管理器后生效。",
        "Moves the Windows 11 centered taskbar icons back to the left, like Windows 10. Takes effect after Explorer restarts.");

    protected override IReadOnlyList<Core.RegValue> Values => new[]
    {
        new Core.RegValue(RegistryHive.CurrentUser, Advanced, "TaskbarAl", 0, 1),
    };

    public override Core.TweakState Query()
    {
        if (Environment.OSVersion.Version.Build < 22000) return Core.TweakState.Unavailable;
        return base.Query();
    }
}

/// <summary>System-wide dark mode for apps and the shell.</summary>
internal sealed class DarkModeTweak : Core.RegistryTweak
{
    private const string Personalize = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    public override string Id => "appearance-dark";
    public override string Category => Core.Categories.Appearance;

    public override string Title => Core.Loc.T("深色模式", "Dark mode");

    public override string Description => Core.Loc.T(
        "系统和应用界面切到深色主题。", "Switches the shell and app surfaces to the dark theme.");

    protected override IReadOnlyList<Core.RegValue> Values => new[]
    {
        new Core.RegValue(RegistryHive.CurrentUser, Personalize, "AppsUseLightTheme", 0, 1),
        new Core.RegValue(RegistryHive.CurrentUser, Personalize, "SystemUsesLightTheme", 0, 1),
    };

    protected override void AfterApply(bool enable) => Core.Native.RefreshShell();
}

/// <summary>Remove the ~400 ms pause hovering a menu item introduces before it opens.</summary>
internal sealed class MenuDelayTweak : Core.RegistryTweak
{
    public override string Id => "appearance-menu-delay";
    public override string Category => Core.Categories.Appearance;

    public override string Title => Core.Loc.T("去掉菜单弹出延迟", "Remove the menu show delay");

    public override string Description => Core.Loc.T(
        "右键菜单、开始菜单子项不再停顿一下才展开，用起来更跟手。",
        "Context menus and submenus open immediately instead of pausing for a beat.");

    protected override IReadOnlyList<Core.RegValue> Values => new[]
    {
        new Core.RegValue(RegistryHive.CurrentUser, @"Control Panel\Desktop", "MenuShowDelay",
            "0", "400", RegistryValueKind.String),
    };
}
