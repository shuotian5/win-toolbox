using Microsoft.Win32;

namespace WinToolbox.Tweaks;

/// <summary>
/// Remove hiberfil.sys. On a 16 GB laptop this is typically 6–8 GB of disk that most people
/// never use — and it also turns off Fast Startup, which is why shutdowns become real
/// shutdowns afterwards. The card says so.
/// </summary>
internal sealed class HibernateTweak : Core.Tweak
{
    public override string Id => "hibernate-off";
    public override string Category => Core.Categories.Performance;

    public override string Title => Core.Loc.T("关闭休眠，释放磁盘空间", "Turn off hibernation to free disk space");

    public override string Description => Core.Loc.T(
        "删除 hiberfil.sys，通常能腾出 6–8 GB。代价是「快速启动」也会一起关闭。",
        "Deletes hiberfil.sys, usually 6–8 GB. Fast Startup goes away with it.");

    public override string? Warning => Core.Loc.T(
        "关掉后不能再用「休眠」，睡眠不受影响。", "Sleep still works; only hibernate is gone.");

    public override Core.TweakState Query()
    {
        bool enabled = Core.Shell.HibernationEnabled();
        long size = enabled ? Core.Shell.HibernationFileSize() : 0;

        Hint = enabled
            ? Core.Loc.T($"休眠文件占用 {Core.Shell.FormatSize(size)}", $"hiberfil.sys is {Core.Shell.FormatSize(size)}")
            : Core.Loc.T("休眠已关闭", "Hibernation is off");

        return enabled ? Core.TweakState.Off : Core.TweakState.On;
    }

    public override Task<Core.TweakResult> ApplyAsync(bool enable) => Task.Run(() =>
    {
        long before = Core.Shell.HibernationFileSize();

        if (!Core.Shell.SetHibernation(!enable))
            return Core.TweakResult.Fail(Core.Loc.T("powercfg 执行失败。", "powercfg failed."));

        if (enable)
        {
            return Core.TweakResult.Success(Core.Loc.T(
                $"休眠已关闭，释放了 {Core.Shell.FormatSize(before)}。",
                $"Hibernation off. Freed {Core.Shell.FormatSize(before)}."));
        }

        return Core.TweakResult.Success(Core.Loc.T("休眠已恢复。", "Hibernation restored."));
    });
}

/// <summary>
/// Switch between the Balanced and High Performance power plans. On a laptop this mostly
/// stops the CPU parking cores aggressively on battery, at the cost of some battery life.
/// </summary>
internal sealed class PowerPlanTweak : Core.Tweak
{
    public override string Id => "power-plan";
    public override string Category => Core.Categories.Performance;

    public override string Title => Core.Loc.T("高性能电源计划", "High performance power plan");

    public override string Description => Core.Loc.T(
        "把电源计划切到高性能，处理器不再急着降频。插电打游戏时更稳，用电池会更费电。",
        "Switches to the high performance plan so the CPU stops downclocking early. Steadier when plugged in while gaming, thirstier on battery.");

    public override Core.TweakState Query()
    {
        string? active = Core.Shell.ActivePowerScheme();
        if (active is null)
        {
            Hint = null;
            return Core.TweakState.Unavailable;
        }

        bool high = active == Core.Shell.HighPerformanceScheme;
        Hint = high
            ? Core.Loc.T("当前：高性能", "Current: high performance")
            : Core.Loc.T("当前：平衡", "Current: balanced");

        return high ? Core.TweakState.On : Core.TweakState.Off;
    }

    public override Task<Core.TweakResult> ApplyAsync(bool enable) => Task.Run(() =>
    {
        string target = enable ? Core.Shell.HighPerformanceScheme : Core.Shell.BalancedScheme;

        if (!Core.Shell.SetPowerScheme(target))
            return Core.TweakResult.Fail(Core.Loc.T("切换电源计划失败。", "Could not switch the power plan."));

        return Core.TweakResult.Success(enable
            ? Core.Loc.T("已切换到高性能", "Switched to high performance")
            : Core.Loc.T("已切回平衡", "Switched back to balanced"));
    });
}

/// <summary>
/// Empty the user and Windows temp folders and report how much came back. Files held open by
/// a running process are skipped — telling the user about those would just be noise.
/// </summary>
internal sealed class TempCleanTweak : Core.Tweak
{
    public override string Id => "temp-clean";
    public override string Category => Core.Categories.Performance;

    public override Core.TweakKind Kind => Core.TweakKind.Action;

    public override string ActionLabel => Core.Loc.T("立即清理", "Clean now");

    public override string Title => Core.Loc.T("清理临时文件", "Clean temporary files");

    public override string Description => Core.Loc.T(
        "清空用户临时目录和 Windows\\Temp。正在被程序占用的文件会自动跳过，不会中断任何程序。",
        "Empties the user temp folder and Windows\\Temp. Files in use are skipped, so nothing gets interrupted.");

    public override Core.TweakState Query()
    {
        long size = Core.Shell.MeasureTemp();
        Hint = Core.Loc.T($"可清理 {Core.Shell.FormatSize(size)}", $"{Core.Shell.FormatSize(size)} can be freed");
        return Core.TweakState.Off;
    }

    public override Task<Core.TweakResult> ApplyAsync(bool enable) => Task.Run(() =>
    {
        var (freed, deleted) = Core.Shell.CleanTemp();
        return Core.TweakResult.Success(Core.Loc.T(
            $"释放了 {Core.Shell.FormatSize(freed)}，删除 {deleted} 个文件。",
            $"Freed {Core.Shell.FormatSize(freed)} ({deleted} files)."));
    });
}
