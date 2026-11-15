namespace WinToolbox;

/// <summary>
/// The list of cards, in display order. Adding a feature means writing one class and putting
/// it in this list — nothing else in the app needs to know about it.
/// </summary>
internal static class TweakCatalog
{
    public static List<Core.Tweak> Create() => new()
    {
        // Hardware / input
        new Tweaks.InternalKeyboardTweak(),
        new Tweaks.TouchpadTweak(),
        new Tweaks.AsusServicesTweak(),
        new Tweaks.MouseAccelTweak(),
        new Tweaks.StickyKeysTweak(),

        // Windows Update
        new Tweaks.WindowsUpdateTweak(),
        new Tweaks.NoAutoRestartTweak(),
        new Tweaks.NoDriverUpdateTweak(),

        // Privacy
        new Tweaks.TelemetryTweak(),
        new Tweaks.CopilotTweak(),
        new Tweaks.WidgetsTweak(),
        new Tweaks.AdsTweak(),
        new Tweaks.WebSearchTweak(),

        // Performance / disk
        new Tweaks.HibernateTweak(),
        new Tweaks.PowerPlanTweak(),
        new Tweaks.TempCleanTweak(),

        // Appearance
        new Tweaks.ClassicContextMenuTweak(),
        new Tweaks.ShowExtensionsTweak(),
        new Tweaks.TaskbarLeftTweak(),
        new Tweaks.DarkModeTweak(),
        new Tweaks.MenuDelayTweak(),
    };
}
