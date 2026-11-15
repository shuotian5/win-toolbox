using Microsoft.Win32;

namespace WinToolbox.Core;

/// <summary>
/// One registry value that a tweak flips. <see cref="OnValue"/> and <see cref="OffValue"/>
/// are compared and written literally; <c>null</c> means "this value should not exist",
/// which is how Windows expresses "default behaviour".
/// </summary>
public sealed record RegValue(
    RegistryHive Hive,
    string Path,
    string Name,
    object? OnValue,
    object? OffValue,
    RegistryValueKind Kind = RegistryValueKind.DWord)
{
    public object? Read()
    {
        using var baseKey = RegistryKey.OpenBaseKey(Hive, RegistryView.Default);
        using var sub = baseKey.OpenSubKey(Path, writable: false);
        return sub?.GetValue(Name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
    }

    public bool IsMissing()
    {
        using var baseKey = RegistryKey.OpenBaseKey(Hive, RegistryView.Default);
        using var sub = baseKey.OpenSubKey(Path, writable: false);
        if (sub is null) return true;
        return sub.GetValue(Name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) is null;
    }

    public void Write(object? value)
    {
        using var baseKey = RegistryKey.OpenBaseKey(Hive, RegistryView.Default);

        if (value is null)
        {
            using var existing = baseKey.OpenSubKey(Path, writable: true);
            existing?.DeleteValue(Name, throwOnMissingValue: false);
            return;
        }

        using var sub = baseKey.CreateSubKey(Path, writable: true);
        if (sub is null) return;

        object typed = value;
        if (Kind == RegistryValueKind.DWord && value is not int)
            typed = int.TryParse(value.ToString(), out int i) ? i : 0;
        else if (Kind == RegistryValueKind.QWord && value is not long)
            typed = long.TryParse(value.ToString(), out long l) ? l : 0L;
        else if (Kind == RegistryValueKind.String && value is not string)
            typed = value.ToString() ?? string.Empty;

        sub.SetValue(Name, typed, Kind);
    }

    internal static bool Matches(object? actual, object? expected)
    {
        if (expected is null) return actual is null;
        if (actual is null) return false;
        if (actual is int a && expected is int b) return a == b;
        return string.Equals(actual.ToString(), expected.ToString(), StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Base for the majority of cards: a list of registry values that all move together.
/// State is derived from the live registry rather than from anything the app cached, so
/// an external change (Windows Update resetting a policy, another tool, Group Policy) shows
/// up the next time the card is refreshed.
/// </summary>
public abstract class RegistryTweak : Tweak
{
    protected abstract IReadOnlyList<RegValue> Values { get; }

    /// <summary>Same list, exposed to the self-test so it can snapshot and restore.</summary>
    internal IReadOnlyList<RegValue> RawValues => Values;

    /// <summary>
    /// Optional side effect that is not registry based (push the value into the running
    /// session, stop a service, nudge the shell). Returning <c>false</c> means the change did
    /// not really take effect — the card then reports a failure instead of a green tick.
    /// </summary>
    protected virtual bool AfterApply(bool enable, out string? note)
    {
        note = null;
        return true;
    }

    public override TweakState Query()
    {
        var values = Values;
        if (values.Count == 0) return TweakState.Unavailable;

        int on = 0;
        foreach (var v in values)
        {
            object? actual;
            try { actual = v.Read(); }
            catch { continue; }

            if (RegValue.Matches(actual, v.OnValue)) on++;
        }

        // "Off" means none of the values are applied — not that every one of them matches a
        // hard-coded default. Most of these keys simply do not exist until something sets
        // them, so requiring an exact off-value would flag a stock Windows install as
        // "partially applied", which is noise rather than information.
        if (on == 0) return TweakState.Off;
        return on == values.Count ? TweakState.On : TweakState.Mixed;
    }

    public override IEnumerable<string> Diagnostics()
    {
        foreach (var v in Values)
        {
            string actual;
            try
            {
                var raw = v.Read();
                actual = raw is null ? "<missing>" : raw.ToString() ?? "<null>";
            }
            catch (Exception ex)
            {
                actual = "<error: " + ex.Message + ">";
            }

            yield return $"    {v.Hive}\\{v.Path}\\{v.Name} = {actual}"
                         + $"   (on={v.OnValue ?? "<delete>"}, off={v.OffValue ?? "<delete>"})";
        }
    }

    public override Task<TweakResult> ApplyAsync(bool enable) => Task.Run(() =>
    {
        int changed = 0;
        var failures = new List<string>();

        foreach (var v in Values)
        {
            try
            {
                // Nothing to undo for a value that is already absent.
                if (!enable && v.IsMissing() && v.OffValue is null) { changed++; continue; }

                Backup.Remember(v.Hive, v.Path, v.Name);

                object? wanted = enable ? v.OnValue : v.OffValue;
                v.Write(wanted);

                // Read the value back. A write that is silently dropped (wrong hive, key
                // owned by TrustedInstaller, policy-locked path) must not be reported as a
                // success — that is the difference between a switch and a lie.
                if (RegValue.Matches(v.Read(), wanted)) changed++;
                else failures.Add($"{v.Name}: {Loc.T("写入未生效", "write did not stick")}");
            }
            catch (Exception ex)
            {
                failures.Add($"{v.Name}: {ex.Message}");
            }
        }

        bool sideOk;
        string? note;
        try { sideOk = AfterApply(enable, out note); }
        catch (Exception ex) { sideOk = false; note = ex.Message; }

        if (failures.Count > 0)
            return TweakResult.Fail(string.Join("; ", failures.Take(2)));

        if (!sideOk)
            return TweakResult.Fail(note ?? Loc.T("设置没有被系统接受", "Windows refused the change"));

        string message = enable
            ? Loc.T($"已应用（{changed} 项）", $"Applied ({changed} value(s))")
            : Loc.T($"已还原（{changed} 项）", $"Reverted ({changed} value(s))");

        if (!string.IsNullOrEmpty(note))
            message += " · " + note;

        return TweakResult.Success(message);
    });
}
