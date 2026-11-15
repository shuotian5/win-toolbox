using Microsoft.Win32;

namespace WinToolbox.Tweaks;

/// <summary>
/// The flagship card: switch the laptop's own keyboard off while every external keyboard,
/// the mouse and the touchpad keep working.
///
/// Two layers, tried in order, each verified against the real device state rather than a
/// return code:
///   1. disable the device node — clean, instant, no reboot (older Windows builds allow it)
///   2. disable i8042prt and stop it — the fallback that works everywhere
/// </summary>
internal sealed class InternalKeyboardTweak : Core.Tweak
{
    public override string Id => "internal-keyboard";
    public override string Category => Core.Categories.Hardware;

    public override string Title => Core.Loc.T("禁用内置键盘", "Disable built-in keyboard");

    public override string Description => Core.Loc.T(
        "关掉笔记本自带键盘。外接键盘、鼠标、触摸板都不受影响，适合用外接键盘打字或打游戏时误触的情况。",
        "Turns the laptop's own keyboard off. External keyboards, mice and the touchpad keep working — handy when typing or gaming on an external keyboard.");

    public override string? Warning => Core.Loc.T(
        "关掉后只能用外接键盘输入，锁屏界面也一样。恢复方法：再点一次这个开关。",
        "Afterwards only an external keyboard can type, including on the sign-in screen. To undo, click this switch again.");

    public override bool NeedsReboot => true;

    /// <summary>Never flipped by the automated self-test — it would lock the keyboard mid-run.</summary>
    public override bool SafeToSelfTest => false;

    public override Core.TweakState Query()
    {
        var kb = Core.InternalKeyboard.FindAll().FirstOrDefault();
        if (kb is null)
        {
            Hint = null;
            return Core.TweakState.Unavailable;
        }

        Hint = kb.Description;

        int? start = Core.Ps2Driver.GetStartValue();
        uint state = Core.Ps2Driver.QueryState();
        bool driverOff = start == Core.Ps2Driver.StartDisabled
                         || (state != 0 && state != Core.Native.SERVICE_RUNNING);

        return kb.IsDisabled || driverOff ? Core.TweakState.On : Core.TweakState.Off;
    }

    public override Task<Core.TweakResult> ApplyAsync(bool enable) =>
        Task.Run(() => enable ? Disable() : Enable());

    private static Core.TweakResult Disable()
    {
        var kb = Core.InternalKeyboard.FindAll().FirstOrDefault();
        if (kb is null)
            return Core.TweakResult.Fail(Core.Loc.T(
                "没有找到内置键盘设备。", "No built-in keyboard device was found."));

        // Snapshot how the driver started before we touch it, so undo is exact.
        Core.Backup.RememberService(
            Core.InternalKeyboard.DriverName,
            Core.Ps2Driver.GetStartValue() ?? Core.Ps2Driver.StartDemand,
            Core.Services.IsRunning(Core.InternalKeyboard.DriverName));

        // Layer 1 — device node.
        Core.Native.CM_Disable_DevNode(kb.DevInst, 0);
        Thread.Sleep(400);

        Core.Native.CM_Get_DevNode_Status(out _, out uint problem, kb.DevInst, 0);
        if (problem == Core.Native.CM_PROB_DISABLED)
            return Core.TweakResult.Success(Core.Loc.T(
                "内置键盘已禁用，立即生效，无需重启。", "Built-in keyboard disabled. Instant, no reboot."));

        // Layer 2 — driver.
        if (!Core.Ps2Driver.SetStartValue(Core.Ps2Driver.StartDisabled))
            return Core.TweakResult.Fail(Core.Loc.T(
                "无法写入驱动配置，可能被安全软件拦截。", "Could not write the driver config — blocked by security software?"));

        if (Core.Ps2Driver.TryStop(out int err))
            return Core.TweakResult.Success(Core.Loc.T(
                "内置键盘已禁用，立即生效。", "Built-in keyboard disabled and already inactive."));

        string reason = err == Core.Native.ERROR_SERVICE_CANNOT_ACCEPT_CTRL
            ? Core.Loc.T("该驱动不支持运行中停止，属于正常现象。", "This driver cannot be stopped at runtime, which is normal.")
            : Core.Loc.T($"停止返回错误 {err}。", $"Stop returned error {err}.");

        return Core.TweakResult.Reboot(Core.Loc.T(
            $"已设置，重启后生效。{reason}", $"Configured. Takes effect after a reboot. {reason}"));
    }

    private static Core.TweakResult Enable()
    {
        int restore = Core.Backup.GetSavedServiceStart(Core.InternalKeyboard.DriverName)
                      ?? Core.Ps2Driver.StartDemand;
        if (restore == Core.Ps2Driver.StartDisabled) restore = Core.Ps2Driver.StartDemand;

        Core.Ps2Driver.SetStartValue(restore);

        if (Core.Ps2Driver.QueryState() != Core.Native.SERVICE_RUNNING)
            Core.Ps2Driver.TryStart();

        var kb = Core.InternalKeyboard.FindAll().FirstOrDefault();
        if (kb is not null)
            Core.Native.CM_Enable_DevNode(kb.DevInst, 0);

        Thread.Sleep(600);

        var check = Core.InternalKeyboard.FindAll().FirstOrDefault();
        bool live = check is not null
                    && check.IsRunning
                    && Core.Ps2Driver.QueryState() == Core.Native.SERVICE_RUNNING;

        return live
            ? Core.TweakResult.Success(Core.Loc.T("内置键盘已恢复，现在就能用。", "Built-in keyboard is back and usable now."))
            : Core.TweakResult.Reboot(Core.Loc.T(
                "设置已恢复，重启后键盘可用。", "Restored. The keyboard works again after a reboot."));
    }
}

/// <summary>
/// "Leave the touchpad on when a mouse is connected" — the inverse of what most laptop
/// owners want. Backed by the Precision Touchpad setting Windows exposes in Settings.
/// </summary>
internal sealed class TouchpadTweak : Core.RegistryTweak
{
    private const string Path = @"Software\Microsoft\Windows\CurrentVersion\PrecisionTouchPad";

    public override string Id => "touchpad-mouse";
    public override string Category => Core.Categories.Hardware;

    public override string Title => Core.Loc.T("接鼠标时禁用触摸板", "Turn touchpad off with a mouse");

    public override string Description => Core.Loc.T(
        "插上鼠标就自动停用触摸板，打字时手掌不会误触光标。拔掉鼠标自动恢复。",
        "Stops touchpad input while a mouse is plugged in, so your palm cannot move the cursor mid-sentence.");

    protected override IReadOnlyList<Core.RegValue> Values => new[]
    {
        new Core.RegValue(RegistryHive.CurrentUser, Path, "LeaveOnWithMouse", 0, 1),
    };

    public override Core.TweakState Query()
    {
        bool exists;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(Path);
            exists = key?.GetValue("LeaveOnWithMouse") is not null;
        }
        catch { exists = false; }

        // Not a Precision Touchpad (or a very old build): do not pretend the switch works.
        if (!exists) return Core.TweakState.Unavailable;
        return base.Query();
    }
}

/// <summary>
/// Stop the ASUS services whose only job is to re-apply vendor settings, phone home for
/// updates or upsell bundled software. Touchpad / MyASUS / ScreenPad services are excluded
/// on purpose — see <see cref="Core.Shell.AsusOnlyOverhead"/>.
/// </summary>
internal sealed class AsusServicesTweak : Core.Tweak
{
    public override string Id => "asus-services";
    public override string Category => Core.Categories.Hardware;

    public override string Title => Core.Loc.T("停止华硕后台服务", "Stop ASUS background services");

    public override string Description => Core.Loc.T(
        "停掉奥创、软件管家、系统分析、更新推送这些常驻服务，减少后台占用和设置被覆盖。触摸板、MyASUS、ScreenPad 的服务不动。",
        "Stops Armoury Crate, the software manager, system analysis and update agents — less background load and fewer settings being overwritten. Touchpad, MyASUS and ScreenPad services are left alone.");

    public override Core.TweakState Query()
    {
        var present = Core.Shell.PresentAsusServices();
        if (present.Count == 0)
        {
            Hint = Core.Loc.T("这台机器上没有华硕服务", "No ASUS services on this machine");
            return Core.TweakState.Unavailable;
        }
        int running = present.Count(Core.Services.IsRunning);
        Hint = Core.Loc.T($"{present.Count} 个服务，{running} 个在运行",
                          $"{present.Count} services, {running} running");

        if (running == 0) return Core.TweakState.On;
        return running == present.Count ? Core.TweakState.Off : Core.TweakState.Mixed;
    }

    /// <summary>Stops real services — never flipped by the automated self-test.</summary>
    public override bool SafeToSelfTest => false;

    public override Task<Core.TweakResult> ApplyAsync(bool enable) => Task.Run(() =>
    {
        var present = Core.Shell.PresentAsusServices();
        int affected = 0;

        foreach (var name in present)
        {
            if (enable)
            {
                Core.Backup.RememberService(name, Core.Services.GetStartType(name) ?? Core.Services.StartAuto,
                                            Core.Services.IsRunning(name));

                // Manual rather than Disabled: the service can still be started on demand by
                // whatever needs it, it just stops reappearing at every boot.
                Core.Services.SetStartType(name, Core.Services.StartManual);
                if (Core.Services.IsRunning(name) && Core.Services.TryStop(name, out _)) affected++;
            }
            else
            {
                int restore = Core.Backup.GetSavedServiceStart(name) ?? Core.Services.StartAuto;
                Core.Services.SetStartType(name, restore);
                if (Core.Services.QueryState(name) != Core.Native.SERVICE_RUNNING)
                    Core.Services.TryStart(name);
                affected++;
            }
        }

        return Core.TweakResult.Success(enable
            ? Core.Loc.T($"已停止 {affected} 个服务", $"Stopped {affected} service(s)")
            : Core.Loc.T($"已恢复 {affected} 个服务", $"Restored {affected} service(s)"));
    });
}

/// <summary>Windows nags about Sticky Keys mid-game; this turns the shortcut off.</summary>
internal sealed class StickyKeysTweak : Core.RegistryTweak
{
    private const string Path = @"Control Panel\Accessibility\StickyKeys";

    public override string Id => "sticky-keys";
    public override string Category => Core.Categories.Hardware;

    public override string Title => Core.Loc.T("关闭粘滞键弹窗", "Disable the Sticky Keys prompt");

    public override string Description => Core.Loc.T(
        "连按五次 Shift 时不再弹出粘滞键提示框。打游戏按 Shift 蹲下、冲刺时特别容易被它打断。",
        "Stops the Sticky Keys dialog from interrupting you after tapping Shift five times — a classic way to lose a firefight.");

    protected override IReadOnlyList<Core.RegValue> Values => new[]
    {
        new Core.RegValue(RegistryHive.CurrentUser, Path, "Flags",
            "506", "510", RegistryValueKind.String),
    };

    /// <summary>
    /// Test the SKF_HOTKEYACTIVE bit instead of the whole value. The flags word carries a
    /// dozen unrelated switches, and hard-coding 506/510 would report "partial" for anyone
    /// who has, say, the On-Screen Keyboard shortcut configured.
    /// </summary>
    public override Core.TweakState Query()
    {
        int flags = Core.Accessibility.ReadStickyFlags();
        Hint = (flags & Core.Native.SKF_HOTKEYACTIVE) == 0
            ? (Core.Accessibility.StickyShortcutLive() == false
                ? Core.Loc.T("已关闭，当前会话已生效", "Off and already in effect")
                : Core.Loc.T("已关闭，注销后生效", "Off — takes effect after signing out"))
            : Core.Loc.T("连按五次 Shift 仍会弹窗", "Shift x5 still opens the dialog");

        return (flags & Core.Native.SKF_HOTKEYACTIVE) == 0 ? Core.TweakState.On : Core.TweakState.Off;
    }

    protected override bool AfterApply(bool enable, out string? note)
    {
        // Writing HKCU is only half the job: the running session keeps its own copy until
        // sign-out. This pushes the new flags into the session so Shift x5 stops now.
        if (!Core.Accessibility.SetStickyHotkey(!enable, out string? error))
        {
            note = Core.Loc.T(
                $"注册表已写入，但当前会话没有接受（{error}），注销后才生效。",
                $"Written to the registry, but the running session refused it ({error}); it applies after signing out.");
            return true;
        }

        note = null;
        return true;
    }

    internal override void ResyncSession()
    {
        int flags = Core.Accessibility.ReadStickyFlags();
        Core.Accessibility.SetStickyHotkey((flags & Core.Native.SKF_HOTKEYACTIVE) != 0, out _);
    }
}

/// <summary>Turn off "enhance pointer precision" — the pointer then moves 1:1 with the mouse.</summary>
internal sealed class MouseAccelTweak : Core.RegistryTweak
{
    public override string Id => "mouse-accel";
    public override string Category => Core.Categories.Hardware;

    public override string Title => Core.Loc.T("关闭鼠标指针加速", "Turn off mouse acceleration");

    public override string Description => Core.Loc.T(
        "取消「提高指针精确度」。鼠标移动距离和光标位移一一对应，甩枪和跟枪更稳定。",
        "Unchecks \"Enhance pointer precision\" so the cursor moves exactly as far as the mouse does — steadier aim in shooters.");

    protected override IReadOnlyList<Core.RegValue> Values => new[]
    {
        new Core.RegValue(RegistryHive.CurrentUser, @"Control Panel\Mouse", "MouseSpeed", "0", "1", RegistryValueKind.String),
        new Core.RegValue(RegistryHive.CurrentUser, @"Control Panel\Mouse", "MouseThreshold1", "0", "6", RegistryValueKind.String),
        new Core.RegValue(RegistryHive.CurrentUser, @"Control Panel\Mouse", "MouseThreshold2", "0", "10", RegistryValueKind.String),
    };

    public override Core.TweakState Query()
    {
        var live = Core.Accessibility.ReadMouseLive();
        Hint = live.Accel == 0
            ? Core.Loc.T("已关闭，当前会话已生效", "Off and already in effect")
            : Core.Loc.T("曲线加速仍在生效", "Acceleration curve is still active");
        return live.Accel == 0 ? Core.TweakState.On : Core.TweakState.Off;
    }

    protected override bool AfterApply(bool enable, out string? note)
    {
        // The pointer curve is held in the session's parameter block; the registry value only
        // seeds it at logon. SPI_SETMOUSE is what actually changes the feel right now.
        if (!Core.Accessibility.SetMouseAcceleration(!enable, out string? error))
        {
            note = Core.Loc.T(
                $"注册表已写入，但当前会话没有接受（{error}），注销后才生效。",
                $"Written to the registry, but the session refused it ({error}); it applies after signing out.");
            return true;
        }

        note = null;
        return true;
    }

    internal override void ResyncSession()
    {
        bool accel = Values[0].Read()?.ToString() != "0";
        Core.Accessibility.SetMouseAcceleration(accel, out _);
    }
}
