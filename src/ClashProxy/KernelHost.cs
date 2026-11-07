using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using ClashProxy.Core;

namespace ClashProxy;

/// <summary>把设置、内核、日志粘在一起的应用级状态。</summary>
public sealed class KernelHost
{
    public AppSettings Settings { get; private set; }
    public KernelService Kernel { get; } = new();
    public ObservableCollection<LogEntry> Logs { get; } = new();

    private const int MaxLogs = 500;

    public KernelHost()
    {
        AppPaths.Ensure();
        Settings = SettingsService.Load();

        // 清扫上次强杀/崩溃后残留的内核（只杀本程序自带的那份 mihomo）
        try
        {
            var orphans = KernelJob.KillOrphans();
            if (orphans > 0)
            {
                AppendLog($"[内核] 已清理 {orphans} 个上次残留的内核进程");
            }
        }
        catch
        {
            // 清理失败不影响启动
        }

        Kernel.OutputLine += line =>
        {
            if (App.MainWindow?.DispatcherQueue is { } dq)
            {
                dq.TryEnqueue(() => AppendLog(line));
            }
            else
            {
                AppendLog(line);
            }
        };
    }

    public void AppendLog(string message)
    {
        var level = message.Contains("[错误]") ? "error"
            : message.Contains("[警告]") ? "warn"
            : "info";

        Logs.Add(new LogEntry { Time = DateTime.Now.ToString("HH:mm:ss"), Level = level, Message = message });
        while (Logs.Count > MaxLogs) Logs.RemoveAt(0);
    }

    public void Reload() => Settings = SettingsService.Load();

    public void Save() => SettingsService.Save(Settings);

    /// <summary>启动内核，并在启动成功后把上次选中的节点应用到 PROXY 组。</summary>
    public async Task<bool> StartKernelAsync()
    {
        var settings = Settings;
        var ok = await Task.Run(() => Kernel.Start(settings)).ConfigureAwait(true);
        if (ok && !string.IsNullOrEmpty(settings.SelectedNode))
        {
            var selected = await Kernel.SelectNodeAsync(settings, settings.SelectedNode).ConfigureAwait(true);
            AppendLog(selected
                ? $"[节点] 已切换到「{settings.SelectedNode}」"
                : $"[节点] 切换「{settings.SelectedNode}」失败，改用自动选择");
        }
        return ok;
    }

    /// <summary>重启内核（配置改动后让新配置生效）。</summary>
    public async Task<bool> RestartKernelAsync()
    {
        if (Kernel.IsRunning)
        {
            Kernel.Stop();
            await Task.Delay(300).ConfigureAwait(true);
        }
        return await StartKernelAsync().ConfigureAwait(true);
    }

    /// <summary>窗口关闭时调用：停掉内核，避免留下孤儿进程。</summary>
    public void Shutdown()
    {
        try
        {
            Save();
        }
        catch
        {
            // 存不下来也要保证能退出
        }

        Kernel.Stop();
    }

    /// <summary>用当前设置重建配置并写入磁盘。</summary>
    public async Task<string> BuildConfigAsync()
    {
        var url = Settings.SubscriptionUrl?.Trim() ?? "";
        if (string.IsNullOrEmpty(url))
        {
            throw new InvalidOperationException("请先填写订阅链接");
        }

        AppendLog("[订阅] 正在下载…");
        var yaml = await SubscriptionService.DownloadAsync(url).ConfigureAwait(false);

        // 留一份原始订阅，便于日后离线重建
        AppPaths.Ensure();
        await System.IO.File.WriteAllTextAsync(AppPaths.CurrentProfile, yaml).ConfigureAwait(false);

        var built = ConfigBuilder.Build(yaml, Settings);
        await System.IO.File.WriteAllTextAsync(AppPaths.ConfigFile, built).ConfigureAwait(false);

        var nodes = SubscriptionService.ParseNodes(yaml);
        AppendLog($"[订阅] 下载完成，共 {nodes.Count} 个节点");

        if (!string.IsNullOrEmpty(Settings.SelectedNode) &&
            !nodes.Any(n => n.Name == Settings.SelectedNode))
        {
            AppendLog($"[订阅] 原选节点「{Settings.SelectedNode}」已不存在，将重新选择");
            Settings.SelectedNode = "";
        }

        return yaml;
    }
}
