using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Security.Principal;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WinToolbox.Core;

namespace WinToolbox;

public partial class MainWindow : Window
{
    /// <summary>One row in the sidebar. <see cref="Count"/> is the number of enabled cards inside.</summary>
    public sealed class NavItem : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        public string Id { get; init; } = string.Empty;
        public string Title { get; init; } = string.Empty;
        public string Glyph { get; init; } = string.Empty;

        private string _count = string.Empty;
        public string Count
        {
            get => _count;
            set
            {
                if (_count == value) return;
                _count = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Count)));
            }
        }
    }

    private readonly List<Tweak> _all = TweakCatalog.Create();
    private readonly ObservableCollection<Tweak> _visible = new();
    private readonly ObservableCollection<NavItem> _nav = new();

    private string _category = Categories.All;
    private string _search = string.Empty;

    public ObservableCollection<Tweak> Visible => _visible;
    public ObservableCollection<NavItem> NavItems => _nav;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;

        LocalizeChrome();
        BuildNav();

        foreach (var tweak in _all)
        {
            tweak.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(Tweak.State)) UpdateCounts();
            };
        }

        Journal.Written += OnJournalWritten;
        Tweak.Applied += OnTweakApplied;
        Loaded += async (_, _) => await OnLoadedAsync();
    }

    /// <summary>
    /// Cards like the context menu, taskbar alignment and Copilot button are written to the
    /// registry but stay invisible until Explorer re-reads its settings. Offer the restart
    /// instead of leaving the user staring at a switch that "did nothing".
    /// </summary>
    private async void OnTweakApplied(Tweak tweak)
    {
        if (!tweak.NeedsExplorerRefresh) return;

        string question = Loc.T(
            $"「{tweak.Title}」已经写入，但要重启资源管理器才能看到效果。\n\n" +
            "现在重启吗？任务栏会闪一下，已经打开的文件夹窗口会被关闭。\n\n" +
            "选「取消」的话，注销或下次登录时也会自动生效。",
            $"\"{tweak.Title}\" is saved, but Explorer has to reload before you can see it.\n\n" +
            "Restart now? The taskbar blinks once and open folder windows close.\n\n" +
            "If you cancel, it takes effect at your next sign-in anyway.");

        var answer = MessageBox.Show(this, question, "WinToolbox",
            MessageBoxButton.OKCancel, MessageBoxImage.Question);

        if (answer != MessageBoxResult.OK) return;

        var (ok, message) = await Task.Run(Core.Shell.RestartExplorer);
        Journal.Write(Loc.T($"重启资源管理器：{message}", $"Explorer: {message}"), ok);
    }

    private async void SelfTest_Click(object sender, RoutedEventArgs e)
    {
        SelfTestButton.IsEnabled = false;
        Journal.Write(Loc.T("正在逐项验证每个开关…", "Verifying every card…"), true);

        string path = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "WinToolbox-selftest.txt");

        try
        {
            var (report, passed, failed, skipped) = await Task.Run(() =>
            {
                string text = Core.SelfTest.Run(_all, out int p, out int f, out int s);
                return (text, p, f, s);
            });

            System.IO.File.WriteAllText(path, report, new System.Text.UTF8Encoding(true));
            await RefreshAllAsync();

            string summary = Loc.T(
                $"通过 {passed} 项，失败 {failed} 项，跳过 {skipped} 项。\n\n报告已保存到桌面：\n{path}",
                $"Passed {passed}, failed {failed}, skipped {skipped}.\n\nReport saved to your desktop:\n{path}");

            MessageBox.Show(this, summary, "WinToolbox",
                MessageBoxButton.OK, failed > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);

            Journal.Write(Loc.T($"体检完成：{passed} 通过 / {failed} 失败", $"Check-up: {passed} passed / {failed} failed"),
                          failed == 0);
        }
        catch (Exception ex)
        {
            Journal.Write(ex.Message, false);
        }
        finally
        {
            SelfTestButton.IsEnabled = true;
        }
    }

    private async Task OnLoadedAsync()
    {
        await RefreshAllAsync();

        if (App.RenderTarget is null) return;

        if (App.RenderCategory is not null)
        {
            var item = _nav.FirstOrDefault(n => n.Id == App.RenderCategory);
            if (item is not null) NavList.SelectedItem = item;
        }

        // Let layout settle and the switch animations finish before capturing.
        await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
        await Task.Delay(350);

        App.RenderTo(this, App.RenderTarget);
        App.ShutdownForRender();
    }

    // ------------------------------------------------------------------ chrome

    private bool _isAdmin;

    private void LocalizeChrome()
    {
        Title = "WinToolbox";
        TaglineText.Text = Loc.T("面向笔记本的 Windows 工具箱", "A Windows toolbox for laptops");
        RestoreButton.Content = Loc.T("全部还原", "Restore all");
        SelfTestButton.Content = Loc.T("体检", "Check-up");
        SelfTestButton.ToolTip = Loc.T(
            "逐项验证每个开关是否真的写进去了：开启→读回→关闭→读回→还原原值。",
            "Verifies every card really writes: on → read back → off → read back → restore.");
        ReloadButton.Content = Loc.T("刷新状态", "Refresh");
        SearchBox.ToolTip = Loc.T("搜索功能", "Search features");
        SearchPlaceholder.Text = Loc.T("搜索功能…", "Search…");
        EmptyHint.Text = Loc.T("没有匹配的功能", "Nothing matches that search");
        StatusLine.Text = Loc.T("就绪", "Ready");

        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            _isAdmin = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch { _isAdmin = false; }

        AdminBadge.Text = _isAdmin
            ? Loc.T("● 管理员权限", "● Administrator")
            : Loc.T("○ 普通权限（部分功能不可用）", "○ Standard user (some features need admin)");
        AdminBadge.Foreground = _isAdmin
            ? new SolidColorBrush(Color.FromRgb(0x7F, 0xA6, 0xE8))
            : new SolidColorBrush(Color.FromRgb(0xE8, 0xA0, 0x7F));

        VersionText.Text = "v1.0.1 · portable";
    }

    private void BuildNav()
    {
        foreach (var (id, _, _, glyph) in Categories.Items)
        {
            _nav.Add(new NavItem { Id = id, Title = Categories.Title(id), Glyph = glyph });
        }

        NavList.SelectedIndex = 0;
        ApplyFilter();
    }

    // ------------------------------------------------------------------- state

    private async Task RefreshAllAsync()
    {
        ReloadButton.IsEnabled = false;

        // Each card reads Windows on its own; TempClean walks the temp folders and is the
        // slowest, so nothing here may block the UI thread.
        await Task.WhenAll(_all.Select(t => Task.Run(t.Refresh)));

        UpdateCounts();
        UpdateSubtitle();
        UpdateBackupLine();

        ReloadButton.IsEnabled = true;
    }

    private void UpdateCounts()
    {
        foreach (var item in _nav)
        {
            int on = _all.Count(t =>
                (item.Id == Categories.All || t.Category == item.Id) &&
                t.State == TweakState.On);
            item.Count = on > 0 ? on.ToString() : string.Empty;
        }
        UpdateSubtitle();
    }

    private void UpdateSubtitle()
    {
        int on = _visible.Count(t => t.State == TweakState.On);
        HeaderSubtitle.Text = Loc.T(
            $"{_visible.Count} 项功能 · 已开启 {on} 项 · 改动都可还原",
            $"{_visible.Count} features · {on} enabled · every change is reversible");
    }

    private void UpdateBackupLine()
    {
        int count = Backup.Count;
        BackupLine.Text = count == 0
            ? Loc.T("尚未改动任何设置", "No settings changed yet")
            : Loc.T($"已记录 {count} 项原始设置", $"{count} original value(s) recorded");
    }

    private void ApplyFilter()
    {
        string query = _search.Trim();

        _visible.Clear();
        foreach (var tweak in _all)
        {
            if (_category != Categories.All && tweak.Category != _category) continue;

            if (query.Length > 0)
            {
                bool hit = tweak.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
                           || tweak.Description.Contains(query, StringComparison.OrdinalIgnoreCase);
                if (!hit) continue;
            }

            _visible.Add(tweak);
        }

        HeaderTitle.Text = _category == Categories.All
            ? Loc.T("全部功能", "All features")
            : Categories.Title(_category);

        EmptyHint.Visibility = _visible.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateSubtitle();
    }

    // ------------------------------------------------------------------ events

    private void NavList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (NavList.SelectedItem is not NavItem item) return;
        _category = item.Id;
        ApplyFilter();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _search = SearchBox.Text ?? string.Empty;
        ApplyFilter();
    }

    private async void ActionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: Tweak tweak })
            await tweak.RunActionAsync();
    }

    private async void Reload_Click(object sender, RoutedEventArgs e) => await RefreshAllAsync();

    private async void RestoreAll_Click(object sender, RoutedEventArgs e)
    {
        string question = Loc.T(
            "将把每一项改动还原成最初记录的值，包括注册表和服务的启动类型。\n\n" +
            "内置键盘如果当前是禁用状态，会一并恢复。还原后建议重启一次。\n\n确定继续？",
            "Every recorded change will be put back to its original value, including registry data " +
            "and service start types.\n\nIf the built-in keyboard is currently disabled it will be " +
            "re-enabled too. A reboot afterwards is recommended.\n\nContinue?");

        var answer = MessageBox.Show(this, question, "WinToolbox",
            MessageBoxButton.OKCancel, MessageBoxImage.Warning);

        if (answer != MessageBoxResult.OK) return;

        RestoreButton.IsEnabled = false;

        int restored = await Task.Run(Backup.RestoreAll);
        Journal.Write(Loc.T($"已还原 {restored} 项设置", $"Restored {restored} setting(s)"), true);

        await RefreshAllAsync();
        RestoreButton.IsEnabled = true;
    }

    private void OnJournalWritten(string message, bool ok)
    {
        Dispatcher.BeginInvoke(() =>
        {
            StatusLine.Text = message;
            StatusLine.Foreground = ok
                ? new SolidColorBrush(Color.FromRgb(0x1F, 0x7A, 0x50))
                : new SolidColorBrush(Color.FromRgb(0xD9, 0x2D, 0x20));
            UpdateBackupLine();
        });
    }
}
