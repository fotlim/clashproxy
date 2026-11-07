using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace ClashProxy.Views;

public sealed partial class SubscriptionPage : Page
{
    private readonly ObservableCollection<NodeItem> _nodes = new();

    public SubscriptionPage()
    {
        InitializeComponent();
        NodeList.ItemsSource = _nodes;
        Loaded += (_, _) => Restore();
    }

    private void Restore()
    {
        var s = App.Host.Settings;
        if (string.IsNullOrEmpty(UrlBox.Text) && !string.IsNullOrEmpty(s.SubscriptionUrl))
        {
            UrlBox.Text = s.SubscriptionUrl;
        }

        if (System.IO.File.Exists(Core.AppPaths.CurrentProfile))
        {
            try
            {
                var yaml = System.IO.File.ReadAllText(Core.AppPaths.CurrentProfile);
                var list = Core.SubscriptionService.ParseNodes(yaml);
                _nodes.Clear();
                foreach (var n in list) _nodes.Add(n);
                SubStatus.Text = $"已从本地缓存载入 {_nodes.Count} 个节点。";
            }
            catch
            {
                // 缓存损坏就忽略
            }
        }
    }

    private void UrlBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        App.Host.Settings.SubscriptionUrl = UrlBox.Text.Trim();
    }

    private async void FetchBtn_Click(object sender, RoutedEventArgs e)
    {
        var url = UrlBox.Text.Trim();
        if (string.IsNullOrEmpty(url))
        {
            SubStatus.Text = "请先填写订阅链接。";
            return;
        }

        FetchBtn.IsEnabled = false;
        SubStatus.Text = "正在下载订阅…";
        try
        {
            var yaml = await Core.SubscriptionService.DownloadAsync(url);
            var list = Core.SubscriptionService.ParseNodes(yaml);

            Core.AppPaths.Ensure();
            await System.IO.File.WriteAllTextAsync(Core.AppPaths.CurrentProfile, yaml);

            App.Host.Settings.SubscriptionUrl = url;
            App.Host.Save();

            _nodes.Clear();
            foreach (var n in list) _nodes.Add(n);

            SubStatus.Text = list.Count == 0
                ? "下载成功，但没有解析到节点。请确认这是 Clash 订阅链接。"
                : $"下载成功，共 {list.Count} 个节点。可以测速后选择，或直接「应用到内核」。";
            App.Host.AppendLog($"[订阅] 已下载 {list.Count} 个节点");
        }
        catch (Exception ex)
        {
            SubStatus.Text = $"下载失败：{ex.Message}";
            App.Host.AppendLog($"[错误] 订阅下载失败：{ex.Message}");
        }
        finally
        {
            FetchBtn.IsEnabled = true;
        }
    }

    private async void ApplyBtn_Click(object sender, RoutedEventArgs e)
    {
        ApplyBtn.IsEnabled = false;
        SubStatus.Text = "正在生成内核配置…";
        try
        {
            await App.Host.BuildConfigAsync(forceDownload: true);
            App.Host.Save();
            SubStatus.Text = "配置已生成。回到「总览」启动内核即可生效。";

            if (App.Host.Kernel.IsRunning)
            {
                SubStatus.Text = "配置已更新，正在重启内核…";
                var ok = await App.Host.RestartKernelAsync();
                SubStatus.Text = ok
                    ? "配置已应用，内核已按新订阅重启。"
                    : "配置已保存，但重启内核失败，请到「运行日志」查看原因。";
                (App.MainWindow as MainWindow)?.RefreshChrome();
            }
        }
        catch (Exception ex)
        {
            var detail = $"{ex.GetType().Name}: {ex.Message}";
            SubStatus.Text = $"生成配置失败：{detail}（详情见「运行日志」）";
            App.Host.AppendLog($"[错误] 生成配置失败：{detail}");
            if (!string.IsNullOrWhiteSpace(ex.StackTrace))
            {
                App.Host.AppendLog(ex.StackTrace);
            }
        }
        finally
        {
            ApplyBtn.IsEnabled = true;
        }
    }

    private void NodeList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is NodeItem item)
        {
            SubStatus.Text = $"已选中节点：{item.Name}";
        }
    }

    private async void UseNodeBtn_Click(object sender, RoutedEventArgs e)
    {
        if (NodeList.SelectedItem is not NodeItem item)
        {
            SubStatus.Text = "请先在列表里选中一个节点。";
            return;
        }

        App.Host.Settings.SelectedNode = item.Name;
        App.Host.Save();

        if (!App.Host.Kernel.IsRunning)
        {
            SubStatus.Text = $"已记录选中节点「{item.Name}」，启动内核后生效。";
            return;
        }

        var ok = await App.Host.Kernel.SelectNodeAsync(App.Host.Settings, item.Name);
        SubStatus.Text = ok
            ? $"已切换到节点「{item.Name}」。"
            : "切换失败，请确认内核正在运行。";
    }

    private async void TestAllBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!App.Host.Kernel.IsRunning)
        {
            SubStatus.Text = "请先启动内核，再对节点测速。";
            return;
        }
        if (_nodes.Count == 0)
        {
            SubStatus.Text = "没有可测速的节点，请先下载订阅。";
            return;
        }

        TestAllBtn.IsEnabled = false;
        SubStatus.Text = "正在并发测速…";

        var s = App.Host.Settings;
        var tasks = _nodes.Select(async n =>
        {
            var ms = await App.Host.Kernel.TestDelayAsync(s, n.Name);
            return (n, ms);
        }).ToArray();

        var results = await Task.WhenAll(tasks);

        foreach (var (n, ms) in results)
        {
            n.Latency = ms < 0 ? "超时" : $"{ms} ms";
        }

        var ok = results.Count(r => r.ms >= 0);
        SubStatus.Text = $"测速完成：{ok}/{results.Length} 个节点可达。";
        TestAllBtn.IsEnabled = true;
    }
}
