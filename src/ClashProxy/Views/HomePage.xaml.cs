using System;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace ClashProxy.Views;

public sealed partial class HomePage : Page
{
    private bool _suppressToggle;

    public HomePage()
    {
        InitializeComponent();
        Loaded += (_, _) => { Refresh(); };
        Unloaded += (_, _) => { };
    }

    private void Refresh()
    {
        var s = App.Host.Settings;
        var running = App.Host.Kernel.IsRunning;

        RulePortText.Text = $"127.0.0.1:{s.RulePort}";
        AppPortText.Text = $"127.0.0.1:{s.AppPort}";
        NodeText.Text = string.IsNullOrEmpty(s.SelectedNode) ? "尚未选择" : s.SelectedNode;

        StartBtn.IsEnabled = !running;
        StopBtn.IsEnabled = running;
        StateLabel.Text = running ? "通道正在运行" : "通道未启动";
        Dot.Fill = new SolidColorBrush(running ? Colors.MediumSeaGreen : Colors.Gray);
        StateDetail.Text = running
            ? $"内核 PID {App.Host.Kernel.ProcessId}，代理端口已在监听。"
            : "点击右侧按钮启动内核，启动后即可让应用走代理。";

        _suppressToggle = true;
        SysProxyToggle.IsOn = Core.SystemProxy.IsApplied;
        SysProxyToggle.IsEnabled = running || Core.SystemProxy.IsApplied;
        _suppressToggle = false;

        (App.MainWindow as MainWindow)?.RefreshChrome();
    }

    private void SysProxyToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_suppressToggle) return;

        if (SysProxyToggle.IsOn)
        {
            if (!App.Host.Kernel.IsRunning)
            {
                _suppressToggle = true;
                SysProxyToggle.IsOn = false;
                _suppressToggle = false;
                ResultText.Text = "请先启动内核，再开启系统代理。";
                return;
            }

            var ok = App.Host.ApplySystemProxy(true);
            ResultText.Text = ok
                ? "系统代理已开启：浏览器现在就能访问分流规则里的网站，其余网站依然直连。"
                : $"开启系统代理失败：{(string.IsNullOrEmpty(Core.SystemProxy.LastError) ? "可能被安全软件拦截" : Core.SystemProxy.LastError)}";
        }
        else
        {
            App.Host.ApplySystemProxy(false);
            ResultText.Text = "系统代理已关闭，已还原成你原来的设置。";
        }

        Refresh();
    }

    private async void StartBtn_Click(object sender, RoutedEventArgs e)
    {
        StartBtn.IsEnabled = false;
        try
        {
            var s = App.Host.Settings;
            if (System.IO.File.Exists(Core.AppPaths.SettingsFile))
            {
                // 先确保配置存在
                if (!System.IO.File.Exists(Core.AppPaths.ConfigFile))
                {
                    await App.Host.BuildConfigAsync();
                }
            }
            await App.Host.StartKernelAsync();
            Refresh();

            if (!App.Host.Kernel.IsRunning)
            {
                ResultText.Text = "启动失败，请到「运行日志」查看原因。";
            }
        }
        catch (Exception ex)
        {
            ResultText.Text = $"启动失败：{ex.Message}";
        }
        finally
        {
            Refresh();
        }
    }

    private void StopBtn_Click(object sender, RoutedEventArgs e)
    {
        App.Host.ApplySystemProxy(false);
        App.Host.Kernel.Stop();
        ResultText.Text = "通道已停止，系统网络保持直连。";
        Refresh();
    }

    private async void TestBtn_Click(object sender, RoutedEventArgs e)
    {
        var s = App.Host.Settings;
        if (!App.Host.Kernel.IsRunning)
        {
            ResultText.Text = "请先启动内核再测试。";
            return;
        }

        ResultText.Text = "正在测试…";
        try
        {
            using var handler = new HttpClientHandler
            {
                Proxy = new System.Net.WebProxy($"http://127.0.0.1:{s.RulePort}"),
                UseProxy = true
            };
            using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(12) };
            var sw = System.Diagnostics.Stopwatch.StartNew();
            using var resp = await http.GetAsync("https://www.google.com/generate_204");
            sw.Stop();
            ResultText.Text = $"通过代理访问 google 返回 {(int)resp.StatusCode}，耗时 {sw.ElapsedMilliseconds} ms。";
        }
        catch (Exception ex)
        {
            ResultText.Text = $"测试失败：{ex.Message}";
        }
    }
}
