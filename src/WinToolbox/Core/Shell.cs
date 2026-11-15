using System.Diagnostics;
using System.IO;
using System.Text;

namespace WinToolbox.Core;

/// <summary>
/// Places where the toolbox has to leave managed code behind: running powercfg, restarting
/// Explorer, measuring and emptying temp folders.
/// </summary>
internal static class Shell
{
    public static (int Code, string Output) Run(string fileName, string arguments)
    {
        try
        {
            var psi = new ProcessStartInfo(fileName, arguments)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            using var p = Process.Start(psi);
            if (p is null) return (-1, string.Empty);

            string output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            p.WaitForExit(8000);
            return (p.HasExited ? p.ExitCode : -1, output);
        }
        catch (Exception ex)
        {
            return (-1, ex.Message);
        }
    }

    // ------------------------------------------------------------- power plans

    public const string BalancedScheme = "381b4222-f694-41f0-9685-ff5bb260df2e";
    public const string HighPerformanceScheme = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";

    public static string? ActivePowerScheme()
    {
        var (code, output) = Run("powercfg", "/getactivescheme");
        if (code != 0) return null;

        // Localised output, so pull the GUID out by shape rather than by label.
        var match = System.Text.RegularExpressions.Regex.Match(
            output, @"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}");
        return match.Success ? match.Value.ToLowerInvariant() : null;
    }

    public static bool SetPowerScheme(string guid)
    {
        var (code, _) = Run("powercfg", $"/setactive {guid}");
        return code == 0;
    }

    // ------------------------------------------------------------ hibernation

    public static string HibernationFile => Path.Combine(
        Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\", "hiberfil.sys");

    public static bool HibernationEnabled()
    {
        try { return File.Exists(HibernationFile); }
        catch { return false; }
    }

    public static long HibernationFileSize()
    {
        try
        {
            var info = new FileInfo(HibernationFile);
            return info.Exists ? info.Length : 0;
        }
        catch { return 0; }
    }

    public static bool SetHibernation(bool enable)
    {
        var (code, _) = Run("powercfg", enable ? "/h on" : "/h off");
        return code == 0;
    }

    // ---------------------------------------------------------------- temp files

    private static readonly string[] TempFolders =
    {
        Path.GetTempPath(),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp"),
    };

    public static long MeasureTemp()
    {
        long total = 0;
        foreach (var dir in TempFolders)
        {
            foreach (var file in EnumerateFiles(dir))
            {
                try { total += new FileInfo(file).Length; } catch { }
            }
        }
        return total;
    }

    /// <summary>
    /// Delete what can be deleted. Files held open by a running process are skipped silently
    /// — that is normal and not worth reporting as an error.
    /// </summary>
    public static (long Freed, int Deleted) CleanTemp()
    {
        long freed = 0;
        int deleted = 0;

        foreach (var dir in TempFolders)
        {
            foreach (var file in EnumerateFiles(dir))
            {
                try
                {
                    long size = new FileInfo(file).Length;
                    File.SetAttributes(file, FileAttributes.Normal);
                    File.Delete(file);
                    freed += size;
                    deleted++;
                }
                catch { }
            }
        }
        return (freed, deleted);
    }

    private static IEnumerable<string> EnumerateFiles(string dir)
    {
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) yield break;

        IEnumerable<string> entries;
        try { entries = Directory.EnumerateFileSystemEntries(dir, "*", SearchOption.AllDirectories); }
        catch { yield break; }

        using var e = entries.GetEnumerator();
        while (true)
        {
            // Enumerating a live temp folder can throw mid-way; stop cleanly instead of
            // bubbling an exception into the UI.
            try { if (!e.MoveNext()) break; }
            catch { break; }

            string current = e.Current;
            bool isFile;
            try { isFile = File.Exists(current); }
            catch { continue; }

            if (isFile) yield return current;
        }
    }

    public static string FormatSize(long bytes)
    {
        if (bytes <= 0) return "0";
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return value >= 100 || unit == 0 ? $"{value:F0} {units[unit]}" : $"{value:F1} {units[unit]}";
    }

    // ------------------------------------------------------------------- asus

    /// <summary>
    /// ASUS background services that only exist to upsell, update or re-apply vendor
    /// settings. Deliberately excluded: AsusPTPService (touchpad), AsusMsControl (MyASUS),
    /// AsusScreenXpertHostService (ScreenPad) — stopping those breaks hardware features.
    /// </summary>
    public static readonly string[] AsusOnlyOverhead =
    {
        "ASUSOptimization",
        "ASUSSoftwareManager",
        "ASUSSystemAnalysis",
        "ASUSSystemDiagnosis",
        "ASUSSwitch",
        "AsusAppService",
        "ASUSUpdateCheck",
        "ASUSLiveUpdateAgent",
        "ArmouryCrateService",
        "ArmouryCrateControlInterface",
        "ArmouryCrateDownloadTool",
    };

    public static List<string> PresentAsusServices() =>
        AsusOnlyOverhead.Where(Services.Exists).ToList();
}
