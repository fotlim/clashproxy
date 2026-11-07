using System;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using WinRT.Interop;

namespace ClashProxy;

/// <summary>主窗口。全部界面在代码里搭建，避免 Window 根 XAML 的解析问题。</summary>
public sealed partial class MainWindow : Window
{
    private readonly NavigationView _nav;
    private readonly Frame _frame = new();
    private readonly TextBlock _statusText = new() { Text = "已停止", FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
    private readonly Border _statusChip;
    private bool _ready;

    public MainWindow()
    {
        Title = "ClashProxy";

        _statusChip = new Border
        {
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(10, 4, 10, 4),
            Background = new SolidColorBrush(ColorHelper.FromArgb(38, 128, 128, 128)),
            Child = _statusText,
            VerticalAlignment = VerticalAlignment.Center
        };

        _nav = new NavigationView
        {
            PaneDisplayMode = NavigationViewPaneDisplayMode.Left,
            IsBackButtonVisible = NavigationViewBackButtonVisible.Collapsed,
            IsSettingsVisible = false,
            OpenPaneLength = 208,
            Content = _frame
        };

        _nav.MenuItems.Add(MakeItem("总览", "home", "\uE80F"));
        _nav.MenuItems.Add(MakeItem("订阅与节点", "sub", "\uE774"));
        _nav.MenuItems.Add(MakeItem("分流规则", "rules", "\uE71C"));
        _nav.MenuItems.Add(MakeItem("应用通道", "apps", "\uE737"));
        _nav.MenuItems.Add(MakeItem("运行日志", "logs", "\uE9D9"));
        _nav.FooterMenuItems.Add(MakeItem("使用说明", "help", "\uE897"));

        _nav.SelectionChanged += Nav_SelectionChanged;
        _nav.SelectedItem = _nav.MenuItems[0];

        var header = BuildHeader();

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(header, 0);
        Grid.SetRow(_nav, 1);
        root.Children.Add(header);
        root.Children.Add(_nav);

        Content = root;

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(header);

        TryResize();
        TryCenter();

        _ready = true;
        _frame.Navigate(typeof(Views.HomePage));
        RefreshChrome();
    }

    private static NavigationViewItem MakeItem(string text, string tag, string glyph)
    {
        return new NavigationViewItem
        {
            Content = text,
            Tag = tag,
            Icon = new FontIcon { Glyph = glyph }
        };
    }

    private Grid BuildHeader()
    {
        var header = new Grid { Height = 48, Padding = new Thickness(16, 0, 140, 0), Background = new SolidColorBrush(Colors.Transparent) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var brand = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
        var logo = new Border
        {
            Width = 24,
            Height = 24,
            CornerRadius = new CornerRadius(7),
            Background = new SolidColorBrush(ColorHelper.FromArgb(255, 0, 120, 212)),
            Child = new TextBlock
            {
                Text = "\uE7AD",
                FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe MDL2 Assets"),
                FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        brand.Children.Add(logo);
        brand.Children.Add(new TextBlock { Text = "ClashProxy", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, FontSize = 14, VerticalAlignment = VerticalAlignment.Center });

        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        right.Children.Add(_statusChip);

        Grid.SetColumn(brand, 0);
        Grid.SetColumn(right, 2);
        header.Children.Add(brand);
        header.Children.Add(right);
        return header;
    }

    public void RefreshChrome()
    {
        try
        {
            var running = App.Host.Kernel.IsRunning;
            _statusText.Text = running ? $"运行中 · 端口 {App.Host.Settings.RulePort}" : "已停止";
            _statusChip.Background = new SolidColorBrush(
                running ? ColorHelper.FromArgb(38, 46, 160, 67) : ColorHelper.FromArgb(38, 128, 128, 128));
        }
        catch (Exception ex)
        {
            _statusText.Text = "状态未知";
            App.Trace("RefreshChrome: " + ex.Message);
        }
    }

    private void TryResize()
    {
        try
        {
            var hwnd = WindowNative.GetWindowHandle(this);
            var id = Win32Interop.GetWindowIdFromWindow(hwnd);
            AppWindow.GetFromWindowId(id).Resize(new SizeInt32(1080, 760));
        }
        catch
        {
            // 调整尺寸失败不影响功能
        }
    }

    private void TryCenter()
    {
        try
        {
            var hwnd = WindowNative.GetWindowHandle(this);
            var id = Win32Interop.GetWindowIdFromWindow(hwnd);
            var appWindow = AppWindow.GetFromWindowId(id);
            var area = DisplayArea.GetFromWindowId(id, DisplayAreaFallback.Primary);
            var x = (area.WorkArea.Width - appWindow.Size.Width) / 2 + area.WorkArea.X;
            var y = (area.WorkArea.Height - appWindow.Size.Height) / 2 + area.WorkArea.Y;
            appWindow.Move(new PointInt32(Math.Max(0, x), Math.Max(0, y)));
        }
        catch
        {
            // 居中失败不影响功能
        }
    }

    private void Nav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (!_ready)
        {
            return;
        }

        if (args.SelectedItem is not NavigationViewItem item)
        {
            return;
        }

        var page = (item.Tag as string) switch
        {
            "sub" => typeof(Views.SubscriptionPage),
            "rules" => typeof(Views.RulesPage),
            "apps" => typeof(Views.AppsPage),
            "logs" => typeof(Views.LogsPage),
            "help" => typeof(Views.HelpPage),
            _ => typeof(Views.HomePage)
        };

        if (_frame.CurrentSourcePageType != page)
        {
            _frame.Navigate(page);
        }
    }
}

