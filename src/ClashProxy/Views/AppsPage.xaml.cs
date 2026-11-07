using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ClashProxy.Views;

public sealed partial class AppsPage : Page
{
    public AppsPage()
    {
        InitializeComponent();
        AppList.ItemsSource = App.Host.Settings.Apps;
        Loaded += (_, _) => UpdateHint();
    }

    private void UpdateHint()
    {
        var n = App.Host.Settings.Apps.Count;
        HintText.Text = n == 0 ? "还没有添加程序。" : $"共 {n} 个程序。";
    }

    private async void AddBtn_Click(object sender, RoutedEventArgs e)
    {
        var picker = new Windows.Storage.Pickers.FileOpenPicker();
        picker.FileTypeFilter.Add(".exe");
        picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.ComputerFolder;

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        var file = await picker.PickSingleFileAsync();
        if (file == null) return;

        var apps = App.Host.Settings.Apps;
        if (apps.Any(x => string.Equals(x.ExePath, file.Path, StringComparison.OrdinalIgnoreCase)))
        {
            HintText.Text = "这个程序已经在列表里了。";
            return;
        }

        apps.Add(new AppEntry
        {
            Name = Path.GetFileNameWithoutExtension(file.Name),
            ExePath = file.Path,
            Enabled = true
        });
        App.Host.Save();
        UpdateHint();

        if (!App.Host.Kernel.IsRunning)
        {
            HintText.Text += " 提示：内核未启动，启动后这些程序才能真正走代理。";
        }
    }

    private void OpenDataBtn_Click(object sender, RoutedEventArgs e)
    {
        Core.AppPaths.Ensure();
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{Core.AppPaths.DataDir}\"")
            {
                UseShellExecute = true
            });
        }
        catch
        {
            // 打开资源管理器失败就算了
        }
    }

    private void DeleteBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is AppEntry entry)
        {
            App.Host.Settings.Apps.Remove(entry);
            App.Host.Save();
            UpdateHint();
        }
    }

    private void LaunchBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b || b.Tag is not AppEntry entry) return;

        if (!App.Host.Kernel.IsRunning)
        {
            HintText.Text = "请先在「总览」启动内核，否则程序即使带代理变量也连不出去。";
            return;
        }

        if (!File.Exists(entry.ExePath))
        {
            HintText.Text = $"找不到程序：{entry.ExePath}";
            return;
        }

        var port = App.Host.Settings.AppPort;
        var proxy = $"http://127.0.0.1:{port}";
        var noProxy = "localhost,127.0.0.1,::1,*.local";

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = entry.ExePath,
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(entry.ExePath) ?? Environment.CurrentDirectory
            };

            psi.Environment["HTTP_PROXY"] = proxy;
            psi.Environment["HTTPS_PROXY"] = proxy;
            psi.Environment["ALL_PROXY"] = proxy;
            psi.Environment["http_proxy"] = proxy;
            psi.Environment["https_proxy"] = proxy;
            psi.Environment["all_proxy"] = proxy;
            psi.Environment["NO_PROXY"] = noProxy;
            psi.Environment["no_proxy"] = noProxy;

            var p = Process.Start(psi);
            HintText.Text = p == null
                ? "启动失败。"
                : $"已启动「{entry.Display}」（PID {p.Id}），走应用通道 {port}。";
            App.Host.AppendLog($"[应用] 启动 {entry.Display}，注入代理 {proxy}");
        }
        catch (Exception ex)
        {
            HintText.Text = $"启动失败：{ex.Message}";
            App.Host.AppendLog($"[错误] 启动应用失败：{ex.Message}");
        }
    }
}
