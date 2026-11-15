using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace WinToolbox.Core;

public enum TweakState
{
    /// <summary>Feature is at its Windows default / switched off.</summary>
    Off,
    /// <summary>Feature is applied.</summary>
    On,
    /// <summary>Some of the underlying values are applied, others are not.</summary>
    Mixed,
    /// <summary>Not applicable on this machine (no such device, no such service).</summary>
    Unavailable,
}

public enum TweakKind
{
    /// <summary>A switch that has an on and an off state.</summary>
    Toggle,
    /// <summary>A one-shot action (clean temp files, run a power plan switch).</summary>
    Action,
}

public readonly record struct TweakResult(bool Ok, string Message, bool RebootRequired = false)
{
    public static TweakResult Success(string message) => new(true, message);
    public static TweakResult Fail(string message) => new(false, message);
    public static TweakResult Reboot(string message) => new(true, message, true);
}

/// <summary>
/// One card in the toolbox. A tweak knows how to read its own state from Windows and how
/// to flip it — nothing else. Every apply path snapshots the old value through
/// <see cref="Backup"/> first, so no card is a one-way door.
/// </summary>
public abstract class Tweak : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void Raise([CallerMemberName] string? name = null)
    {
        var handler = PropertyChanged;
        if (handler is null) return;

        // Refreshes run on the thread pool, so notifications have to hop back to the UI
        // thread before the binding engine touches any element.
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
            dispatcher.BeginInvoke(() => handler(this, new PropertyChangedEventArgs(name ?? string.Empty)));
        else
            handler(this, new PropertyChangedEventArgs(name ?? string.Empty));
    }

    // ---------------------------------------------------------------- metadata

    public abstract string Id { get; }
    public abstract string Category { get; }
    public abstract string Title { get; }
    public abstract string Description { get; }

    /// <summary>Shown as a red note under the description when the change has a real cost.</summary>
    public virtual string? Warning => null;

    public virtual TweakKind Kind => TweakKind.Toggle;

    /// <summary>Button caption for <see cref="TweakKind.Action"/> cards.</summary>
    public virtual string ActionLabel => Loc.T("运行", "Run");

    /// <summary>Marks cards whose effect only lands after a restart.</summary>
    public virtual bool NeedsReboot => false;

    /// <summary>
    /// True when the change is written but Explorer has to restart before it becomes
    /// visible — context menu, taskbar position, taskbar buttons, search suggestions.
    /// The window asks the user before restarting anything.
    /// </summary>
    public virtual bool NeedsExplorerRefresh => false;

    /// <summary>
    /// False for cards that must not be flipped by the automated self-test: disabling the
    /// keyboard, deleting hiberfil.sys, wiping temp folders, stopping services.
    /// </summary>
    public virtual bool SafeToSelfTest => true;

    // -------------------------------------------------------------------- state

    private TweakState _state = TweakState.Off;
    private bool _busy;
    private string? _hint;

    public TweakState State
    {
        get => _state;
        private set
        {
            if (_state == value) return;
            _state = value;
            Raise(nameof(State));
            Raise(nameof(IsOn));
            Raise(nameof(IsOff));
            Raise(nameof(IsUnavailable));
            Raise(nameof(StatusText));
        }
    }

    public bool IsOn
    {
        get => _state == TweakState.On;
        set
        {
            if (_busy) return;
            if (value == (_state == TweakState.On)) return;
            _ = ToggleAsync(value);
        }
    }

    public bool IsOff => _state == TweakState.Off;
    public bool IsUnavailable => _state == TweakState.Unavailable;

    public bool IsBusy
    {
        get => _busy;
        private set
        {
            if (_busy == value) return;
            _busy = value;
            Raise(nameof(IsBusy));
            Raise(nameof(CanInteract));
        }
    }

    public bool CanInteract => !_busy && _state != TweakState.Unavailable;

    /// <summary>Extra one-line context, e.g. "8.4 GB can be freed".</summary>
    public string? Hint
    {
        get => _hint;
        protected set
        {
            if (_hint == value) return;
            _hint = value;
            Raise(nameof(Hint));
            Raise(nameof(HasHint));
        }
    }

    public bool HasHint => !string.IsNullOrEmpty(_hint);

    public string StatusText => _state switch
    {
        TweakState.On => Loc.T("已开启", "On"),
        TweakState.Off => Loc.T("已关闭", "Off"),
        TweakState.Mixed => Loc.T("部分生效", "Partial"),
        _ => Loc.T("不可用", "N/A"),
    };

    // -------------------------------------------------------------------- work

    public abstract TweakState Query();

    /// <summary>
    /// Raw values behind this card, for the diagnostic report users paste into an issue.
    /// </summary>
    public virtual IEnumerable<string> Diagnostics() => Array.Empty<string>();

    public abstract Task<TweakResult> ApplyAsync(bool enable);

    /// <summary>
    /// Push the current registry state back into the running session. Only the two tweaks
    /// that go through SystemParametersInfo need this — after the self-test restores raw
    /// values, the session would otherwise keep whatever the test left behind.
    /// </summary>
    internal virtual void ResyncSession() { }

    /// <summary>Re-read the state from Windows. Never throws.</summary>
    public void Refresh()
    {
        try
        {
            State = Query();
        }
        catch
        {
            State = TweakState.Unavailable;
        }
    }

    /// <summary>
    /// Raised after a successful change, on the UI thread. The window uses it to offer the
    /// Explorer restart that cards such as the context menu and taskbar alignment need.
    /// </summary>
    internal static event Action<Tweak>? Applied;

    private async Task ToggleAsync(bool enable)
    {
        IsBusy = true;
        // Optimistically paint the new position so the switch does not feel laggy; the real
        // state is re-read from Windows when the work finishes.
        State = enable ? TweakState.On : TweakState.Off;

        try
        {
            var result = await ApplyAsync(enable).ConfigureAwait(true);
            Journal.Write($"{Title} · {result.Message}", result.Ok);
            if (!result.Ok) State = enable ? TweakState.Off : TweakState.On;
            else
            {
                Refresh();
                Applied?.Invoke(this);
            }
        }
        catch (Exception ex)
        {
            Journal.Write($"{Title} · {ex.Message}", false);
            Refresh();
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Entry point used by action buttons.</summary>
    public async Task RunActionAsync()
    {
        if (_busy) return;
        IsBusy = true;
        try
        {
            var result = await ApplyAsync(true).ConfigureAwait(true);
            Journal.Write($"{Title} · {result.Message}", result.Ok);
            Refresh();
        }
        catch (Exception ex)
        {
            Journal.Write($"{Title} · {ex.Message}", false);
        }
        finally
        {
            IsBusy = false;
        }
    }
}

/// <summary>Activity feed shown at the bottom of the window.</summary>
internal static class Journal
{
    public static event Action<string, bool>? Written;

    public static void Write(string message, bool ok)
    {
        var stamp = DateTime.Now.ToString("HH:mm:ss");
        Written?.Invoke($"[{stamp}] {message}", ok);
    }
}

/// <summary>Sidebar groups. Order here is the order shown in the window.</summary>
public static class Categories
{
    public const string All = "all";
    public const string Hardware = "hardware";
    public const string Updates = "updates";
    public const string Privacy = "privacy";
    public const string Performance = "performance";
    public const string Appearance = "appearance";

    public static readonly (string Id, string Zh, string En, string Glyph)[] Items =
    {
        (All,         "全部",     "All",           "\uE71D"),
        (Hardware,    "硬件开关", "Hardware",      "\uE7F4"),
        (Updates,     "系统更新", "Updates",       "\uE895"),
        (Privacy,     "隐私与广告", "Privacy",     "\uE72E"),
        (Performance, "性能与磁盘", "Performance", "\uE9D9"),
        (Appearance,  "界面与外观", "Appearance",  "\uE790"),
    };

    public static string Title(string id)
    {
        foreach (var item in Items)
            if (item.Id == id)
                return Loc.Chinese ? item.Zh : item.En;
        return id;
    }

    public static string Glyph(string id)
    {
        foreach (var item in Items)
            if (item.Id == id)
                return item.Glyph;
        return "\uE71D";
    }
}
