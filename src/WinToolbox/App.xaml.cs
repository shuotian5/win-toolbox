using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace WinToolbox;

public partial class App : Application
{
    // ---------------------------------------------------------------- cli options

    /// <summary><c>--render &lt;file.png&gt;</c> — draw the window to an image and exit.</summary>
    public static string? RenderTarget { get; private set; }

    /// <summary><c>--render-category &lt;id&gt;</c> — which tab to capture.</summary>
    public static string? RenderCategory { get; private set; }

    /// <summary><c>--dump &lt;file.txt&gt;</c> — write a diagnostic report and exit.</summary>
    public static string? DumpTarget { get; private set; }

    /// <summary><c>--apply &lt;id&gt; on|off</c> — flip one card without the UI.</summary>
    public static string? ApplyId { get; private set; }

    public static bool ApplyEnable { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var args = e.Args;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--render" when i + 1 < args.Length:
                    RenderTarget = args[++i];
                    break;
                case "--render-category" when i + 1 < args.Length:
                    RenderCategory = args[++i];
                    break;
                case "--dump" when i + 1 < args.Length:
                    DumpTarget = args[++i];
                    break;
                case "--apply" when i + 2 < args.Length:
                    ApplyId = args[++i];
                    ApplyEnable = args[++i].Equals("on", StringComparison.OrdinalIgnoreCase);
                    break;
                case "--help" or "-h":
                    WriteHelp();
                    Shutdown(0);
                    return;
            }
        }

        if (ApplyId is not null)
        {
            Shutdown(RunApply(ApplyId));
            return;
        }

        if (DumpTarget is not null)
        {
            Shutdown(RunDump(DumpTarget));
            return;
        }

        // A single unhandled exception inside a tweak should not take the window down with
        // it — the user may be halfway through a change.
        DispatcherUnhandledException += (_, handlerArgs) =>
        {
            Core.Journal.Write(handlerArgs.Exception.Message, false);
            handlerArgs.Handled = true;
        };

        new MainWindow().Show();
    }

    // --------------------------------------------------------------------- cli

    private static void AttachToParentConsole()
    {
        try
        {
            if (!Core.Native.AttachConsole(Core.Native.ATTACH_PARENT_PROCESS)) return;
            var stdout = Console.OpenStandardOutput();
            Console.SetOut(new StreamWriter(stdout) { AutoFlush = true });
        }
        catch
        {
            // No console to attach to; the caller reads the exit code instead.
        }
    }

    private static void WriteHelp()
    {
        AttachToParentConsole();
        Console.WriteLine("WinToolbox 1.0.0");
        Console.WriteLine();
        Console.WriteLine("  WinToolbox.exe                     open the window");
        Console.WriteLine("  WinToolbox.exe --apply <id> on|off flip one card, no UI");
        Console.WriteLine("  WinToolbox.exe --dump <file.txt>   write a diagnostic report");
        Console.WriteLine("  WinToolbox.exe --render <file.png> save a screenshot of the window");
        Console.WriteLine();
        Console.WriteLine("  Exit codes: 0 ok / 1 failed / 2 unknown id");
    }

    private static int RunApply(string id)
    {
        AttachToParentConsole();

        var tweak = TweakCatalog.Create().FirstOrDefault(t => t.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (tweak is null)
        {
            Console.WriteLine($"unknown id: {id}");
            return 2;
        }

        var result = tweak.ApplyAsync(ApplyEnable).GetAwaiter().GetResult();
        Console.WriteLine($"{id} => {(ApplyEnable ? "on" : "off")}: {result.Message}");
        return result.Ok ? 0 : 1;
    }

    private static int RunDump(string path)
    {
        AttachToParentConsole();

        var sb = new StringBuilder();
        sb.AppendLine("WinToolbox diagnostic report");
        sb.AppendLine($"Generated  {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"Windows    {Environment.OSVersion.Version}");
        sb.AppendLine($"Language   {(Core.Loc.Chinese ? "zh" : "en")}");
        sb.AppendLine($"Machine    {Environment.MachineName}");
        sb.AppendLine($"Backed up  {Core.Backup.Count} value(s)");
        sb.AppendLine(new string('-', 64));

        foreach (var tweak in TweakCatalog.Create())
        {
            tweak.Refresh();
            sb.AppendLine($"[{tweak.Category}] {tweak.Id} => {tweak.State}   ({tweak.Title})");
            if (tweak.HasHint) sb.AppendLine($"    hint: {tweak.Hint}");
            foreach (var line in tweak.Diagnostics()) sb.AppendLine(line);
            sb.AppendLine();
        }

        sb.AppendLine(new string('-', 64));
        sb.AppendLine("Keyboard devices:");
        foreach (var line in Core.InternalKeyboard.ListAllKeyboards())
            sb.AppendLine("  " + line);

        try
        {
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
            Console.WriteLine($"report written to {path}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
            return 1;
        }
    }

    // ------------------------------------------------------------------ render

    /// <summary>Render the visual tree to a PNG at 2× scale so the export is not blurry.</summary>
    public static void RenderTo(FrameworkElement element, string path)
    {
        const double scale = 2.0;

        int width = (int)Math.Ceiling(element.ActualWidth * scale);
        int height = (int)Math.Ceiling(element.ActualHeight * scale);
        if (width <= 0 || height <= 0) return;

        var bitmap = new RenderTargetBitmap(width, height, 96 * scale, 96 * scale, PixelFormats.Pbgra32);

        // Render the window's content, not its chrome: RenderTargetBitmap cannot draw the
        // non-client frame.
        if (element is Window window && window.Content is FrameworkElement content)
            bitmap.Render(content);
        else
            bitmap.Render(element);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    public static void ShutdownForRender() =>
        Current.Dispatcher.BeginInvoke(() => Current.Shutdown(), DispatcherPriority.ApplicationIdle);
}
