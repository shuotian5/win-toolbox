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
        Loaded += async (_, _) => await OnLoadedAsync();
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

        VersionText.Text = "v1.0.0 · portable";
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
