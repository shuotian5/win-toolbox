using System.Text;

namespace WinToolbox.Core;

/// <summary>
/// Round-trips every registry-backed card: read the raw values, apply "on", read them back,
/// apply "off", read them back, then put the machine exactly as it was.
///
/// The point is that a card is only allowed to be called working when the value Windows
/// actually stores changed. Anything that reports success without a verified write shows up
/// here as a failure instead of being discovered by a user a week later.
/// </summary>
internal static class SelfTest
{
    private static object? Read(RegValue v)
    {
        try { return v.Read(); } catch { return null; }
    }

    private static string Show(object? value) => value switch
    {
        null => "<不存在>",
        string s when s.Length == 0 => "<空字符串>",
        _ => value.ToString() ?? "?",
    };

    /// <summary>
    /// Most cards write HKLM, and a few write HKCU\Software\Policies — that hive is owned by
    /// SYSTEM on purpose, because policy keys are meant to be administrator-controlled. Both
    /// need an elevated token, so a standard-user run would report them as broken when they
    /// are merely untested.
    /// </summary>
    private static bool NeedsElevation(RegValue v) =>
        v.Hive == Microsoft.Win32.RegistryHive.LocalMachine
        || v.Path.StartsWith(@"Software\Policies\", StringComparison.OrdinalIgnoreCase);

    private static bool IsAdmin()    {
        try
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            return new System.Security.Principal.WindowsPrincipal(identity)
                .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }

    public static string Run(IEnumerable<Tweak> tweaks, out int passed, out int failed, out int skipped)
    {
        passed = failed = skipped = 0;
        bool admin = IsAdmin();
        var sb = new StringBuilder();

        sb.AppendLine("WinToolbox 自检报告 / self-test report");
        sb.AppendLine($"时间 / time      {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"系统 / windows   {Environment.OSVersion.Version} (build {Environment.OSVersion.Version.Build})");
        sb.AppendLine($"管理员 / admin   {(IsAdmin() ? "是 yes" : "否 no")}");
        sb.AppendLine($"语言 / language  {(Loc.Chinese ? "zh" : "en")}");
        sb.AppendLine(new string('=', 76));
        sb.AppendLine();

        foreach (var tweak in tweaks)
        {
            if (tweak.Kind != TweakKind.Toggle)
            {
                skipped++;
                Write(sb, tweak, "跳过 skip", "动作型卡片，没有开关状态可以往返验证");
                continue;
            }

            if (!tweak.SafeToSelfTest)
            {
                skipped++;
                Write(sb, tweak, "跳过 skip", "会改动硬件或服务，只能手动验证");
                continue;
            }

            if (tweak is not RegistryTweak reg)
            {
                skipped++;
                Write(sb, tweak, "跳过 skip", "非注册表卡片");
                continue;
            }

            if (!admin && reg.RawValues.Any(v => NeedsElevation(v)))
            {
                skipped++;
                Write(sb, tweak, "跳过 skip", "需要管理员权限，请以管理员身份重新运行自检");
                continue;
            }

            var snapshot = reg.RawValues.Select(v => (Value: v, Raw: Read(v))).ToList();
            var problems = new List<string>();

            try
            {
                var on = tweak.ApplyAsync(true).GetAwaiter().GetResult();
                if (!on.Ok)
                {
                    problems.Add("开启被拒绝：" + on.Message);
                }
                else
                {
                    foreach (var v in reg.RawValues)
                    {
                        object? actual = Read(v);
                        if (!RegValue.Matches(actual, v.OnValue))
                            problems.Add($"开启后 {v.Name} = {Show(actual)}，期望 {Show(v.OnValue)}");
                    }
                }

                var off = tweak.ApplyAsync(false).GetAwaiter().GetResult();
                if (!off.Ok)
                {
                    problems.Add("关闭被拒绝：" + off.Message);
                }
                else
                {
                    foreach (var v in reg.RawValues)
                    {
                        object? actual = Read(v);
                        if (!RegValue.Matches(actual, v.OffValue))
                            problems.Add($"关闭后 {v.Name} = {Show(actual)}，期望 {Show(v.OffValue)}");
                    }
                }
            }
            catch (Exception ex)
            {
                problems.Add("异常：" + ex.Message);
            }
            finally
            {
                foreach (var (value, raw) in snapshot)
                {
                    try { value.Write(raw); }
                    catch (Exception ex) { problems.Add($"还原 {value.Name} 失败：{ex.Message}"); }
                }

                // Sticky Keys and pointer acceleration also live in the session's parameter
                // block; restoring the registry alone would leave the test's last state live.
                try { tweak.ResyncSession(); } catch { }
                tweak.Refresh();
            }

            if (problems.Count == 0)
            {
                passed++;
                Write(sb, tweak, "通过 pass", $"{reg.RawValues.Count} 个值写入并读回一致，已还原");
            }
            else
            {
                failed++;
                Write(sb, tweak, "失败 FAIL", problems[0]);
                foreach (var extra in problems.Skip(1)) sb.AppendLine($"      ! {extra}");
            }
        }

        sb.AppendLine();
        sb.AppendLine(new string('=', 76));
        sb.AppendLine($"通过 passed {passed}   失败 failed {failed}   跳过 skipped {skipped}");

        if (failed > 0)
            sb.AppendLine("失败的卡片在这台机器上不会真正生效，请把这份报告贴到 issue 里。");

        return sb.ToString();
    }

    private static void Write(StringBuilder sb, Tweak tweak, string verdict, string detail)
    {
        sb.AppendLine($"[{tweak.Category}] {tweak.Id}  —  {tweak.Title}");
        sb.AppendLine($"      {verdict}：{detail}");
    }
}
