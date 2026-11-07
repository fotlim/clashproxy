using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ClashProxy.Core;

/// <summary>mihomo 内核的进程生命周期 + 控制 REST API。</summary>
public sealed class KernelService : IDisposable
{
    private Process? _proc;
    private readonly List<string> _pending = new();
    private readonly object _gate = new();
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };

    public bool IsRunning => _proc is { HasExited: false };
    public int? ProcessId => IsRunning ? _proc!.Id : null;

    /// <summary>内核输出的一行。</summary>
    public event Action<string>? OutputLine;

    public string OutputPath => Path.Combine(AppPaths.LogDir, "kernel.log");

    /// <summary>启动内核（若已在跑则直接返回 true）。</summary>
    public bool Start(AppSettings settings)
    {
        if (IsRunning) return true;
        if (!File.Exists(AppPaths.KernelExe))
        {
            Emit($"[错误] 找不到内核：{AppPaths.KernelExe}");
            return false;
        }
        if (!File.Exists(AppPaths.ConfigFile))
        {
            Emit("[错误] 配置尚未生成，请先导入订阅并应用");
            return false;
        }

        AppPaths.Ensure();

        var psi = new ProcessStartInfo
        {
            FileName = AppPaths.KernelExe,
            Arguments = $"-d \"{AppPaths.DataDir}\" -f \"{AppPaths.ConfigFile}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = AppPaths.DataDir
        };

        try
        {
            var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
            p.OutputDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) Emit(e.Data!); };
            p.ErrorDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) Emit(e.Data!); };
            p.Exited += (_, _) => Emit("[内核] 进程已退出");
            p.Start();
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            _proc = p;

            // 绑进 Job Object：本进程被强杀时，内核会被系统一起回收
            if (KernelJob.Attach(p)) Emit("[内核] 已绑定到本程序进程，随主程序一起退出");
            else Emit("[内核] 警告：未能绑定进程，主程序被强制结束时内核可能残留");

            Emit($"[内核] 已启动，PID {p.Id}");
            WaitForPort(settings.RulePort, 30);
            return true;
        }
        catch (Exception ex)
        {
            Emit($"[错误] 内核启动失败：{ex.Message}");
            return false;
        }
    }

    public void Stop()
    {
        var p = _proc;
        _proc = null;
        if (p == null) return;
        try
        {
            if (!p.HasExited)
            {
                p.Kill(entireProcessTree: true);
                p.WaitForExit(5000);
            }
        }
        catch
        {
            // 忽略：进程可能已自行退出
        }
        finally
        {
            p.Dispose();
        }
        Emit("[内核] 已停止");
    }

    /// <summary>轮询等待端口可连接。</summary>
    public static bool WaitForPort(int port, int tries, int intervalMs = 500)
    {
        for (var i = 0; i < tries; i++)
        {
            try
            {
                using var c = new TcpClient();
                var task = c.ConnectAsync("127.0.0.1", port);
                if (task.Wait(1500) && c.Connected) return true;
            }
            catch
            {
                // 继续等
            }
            Thread.Sleep(intervalMs);
        }
        return false;
    }

    public static bool PortOpen(int port) => WaitForPort(port, 1, 0);

    // ---------- 控制 API ----------

    private Uri Ctrl(AppSettings s, string path) =>
        new($"http://127.0.0.1:{s.ControllerPort}{path}");

    private static HttpRequestMessage Req(HttpMethod m, Uri uri, AppSettings s)
    {
        var r = new HttpRequestMessage(m, uri);
        if (!string.IsNullOrEmpty(s.ControllerSecret))
        {
            r.Headers.TryAddWithoutValidation("Authorization", $"Bearer {s.ControllerSecret}");
        }
        return r;
    }

    /// <summary>读取控制接口里的代理组与节点。</summary>
    public async Task<JsonDocument?> GetProxiesAsync(AppSettings s, CancellationToken ct = default)
    {
        try
        {
            using var req = Req(HttpMethod.Get, Ctrl(s, "/proxies"), s);
            using var resp = await Http.SendAsync(req, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return null;
            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return JsonDocument.Parse(body);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>切换 PROXY 组选中的节点。</summary>
    public async Task<bool> SelectNodeAsync(AppSettings s, string nodeName, CancellationToken ct = default)
    {
        try
        {
            using var req = Req(HttpMethod.Put, Ctrl(s, $"/proxies/{Uri.EscapeDataString(ConfigBuilder.ProxyGroup)}"), s);
            req.Content = new StringContent(
                JsonSerializer.Serialize(new { name = nodeName }),
                new UTF8Encoding(false), "application/json");
            using var resp = await Http.SendAsync(req, ct).ConfigureAwait(false);
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>测单个节点延迟（毫秒），失败返回 -1。</summary>
    public async Task<int> TestDelayAsync(AppSettings s, string nodeName, CancellationToken ct = default)
    {
        try
        {
            var path = $"/proxies/{Uri.EscapeDataString(nodeName)}/delay?url={Uri.EscapeDataString(s.TestUrl)}&timeout=5000";
            using var req = Req(HttpMethod.Get, Ctrl(s, path), s);
            using var resp = await Http.SendAsync(req, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return -1;
            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("delay", out var d) && d.TryGetInt32(out var ms))
            {
                return ms;
            }
        }
        catch
        {
            // 落到下面
        }
        return -1;
    }

    private void Emit(string line)
    {
        var stamped = $"{DateTime.Now:HH:mm:ss} {line}";
        lock (_gate)
        {
            try
            {
                File.AppendAllText(OutputPath, stamped + Environment.NewLine, Encoding.UTF8);
            }
            catch
            {
                // 写日志失败不影响功能
            }
        }
        OutputLine?.Invoke(stamped);
    }

    public void Dispose() => Stop();
}
