using System;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace ClashProxy.Core;

/// <summary>
/// Windows 系统代理开关（写 HKCU 的 Internet Settings）。
/// 只为「让浏览器这类不认代理参数的程序也能走规则通道」而存在：
/// 开启后系统代理指向 127.0.0.1:7890，而 7890 的规则是「列表内域名走代理，其余直连」。
/// 关闭时把用户原来的设置原样还原。
/// </summary>
public static class SystemProxy
{
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";
    private const int INTERNET_OPTION_SETTINGS_CHANGED = 39;
    private const int INTERNET_OPTION_REFRESH = 37;

    [DllImport("wininet.dll", SetLastError = true)]
    private static extern bool InternetSetOption(IntPtr hInternet, int dwOption, IntPtr lpBuffer, int dwBufferLength);

    private sealed class Snapshot
    {
        public bool ProxyEnable;
        public string? ProxyServer;
        public string? ProxyOverride;
    }

    private static Snapshot? _saved;
    private static bool _applied;

    /// <summary>本程序是否正在接管系统代理。</summary>
    public static bool IsApplied => _applied;

    /// <summary>最近一次开启失败的原因，供界面直接显示。</summary>
    public static string LastError { get; private set; } = "";

    /// <summary>把系统代理指向 127.0.0.1:port（默认 7890）。</summary>
    public static bool Enable(int port)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath, writable: true);
            if (key is null)
            {
                LastError = "无法打开系统代理注册表键（可能被安全软件拦截）";
                Diag($"OpenSubKey(writable) => null; HKCU={Registry.CurrentUser.Name}");
                return false;
            }

            if (!_applied)
            {
                _saved = new Snapshot
                {
                    ProxyEnable = Convert.ToInt32(key.GetValue("ProxyEnable", 0)) != 0,
                    ProxyServer = key.GetValue("ProxyServer") as string,
                    ProxyOverride = key.GetValue("ProxyOverride") as string
                };
            }

            var overrideList = _saved?.ProxyOverride;
            if (string.IsNullOrWhiteSpace(overrideList))
            {
                overrideList = "localhost;127.*;10.*;172.16.*;172.17.*;172.18.*;172.19.*;172.20.*;172.21.*;" +
                               "172.22.*;172.23.*;172.24.*;172.25.*;172.26.*;172.27.*;172.28.*;172.29.*;" +
                               "172.30.*;172.31.*;192.168.*;<local>";
            }

            key.SetValue("ProxyEnable", 1, RegistryValueKind.DWord);
            key.SetValue("ProxyServer", $"127.0.0.1:{port}", RegistryValueKind.String);
            key.SetValue("ProxyOverride", overrideList, RegistryValueKind.String);
            _applied = true;
            Notify();
            return true;
        }
        catch (Exception ex)
        {
            Diag($"Enable failed: {ex.GetType().Name}: {ex.Message}");
            App.Trace($"SystemProxy.Enable failed: {ex.Message}");
            LastError = $"{ex.GetType().Name}: {ex.Message}";
            return false;
        }
    }

    /// <summary>还原到开启前的状态。</summary>
    public static void Disable()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath, writable: true);
            if (key is not null && _saved is not null)
            {
                key.SetValue("ProxyEnable", _saved.ProxyEnable ? 1 : 0, RegistryValueKind.DWord);

                if (_saved.ProxyServer is null) key.DeleteValue("ProxyServer", throwOnMissingValue: false);
                else key.SetValue("ProxyServer", _saved.ProxyServer, RegistryValueKind.String);

                if (_saved.ProxyOverride is null) key.DeleteValue("ProxyOverride", throwOnMissingValue: false);
                else key.SetValue("ProxyOverride", _saved.ProxyOverride, RegistryValueKind.String);

                Notify();
            }
        }
        catch (Exception ex)
        {
            App.Trace($"SystemProxy.Disable failed: {ex.Message}");
        }
        finally
        {
            _saved = null;
            _applied = false;
        }
    }

    /// <summary>系统当前是否已经指向本程序的端口（可能是上次异常退出留下的）。</summary>
    public static bool IsPointingTo(int port)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
            if (key is null) return false;
            var on = Convert.ToInt32(key.GetValue("ProxyEnable", 0)) != 0;
            var server = key.GetValue("ProxyServer") as string ?? "";
            return on && server.Contains($":{port}", StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    private static void Diag(string message)
    {
        try
        {
            var dir = AppPaths.LogDir;
            System.IO.Directory.CreateDirectory(dir);
            System.IO.File.AppendAllText(
                System.IO.Path.Combine(dir, "sysproxy.log"),
                $"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}");
        }
        catch
        {
            // 诊断日志失败不影响功能
        }
    }

    private static void Notify()
    {
        try
        {
            InternetSetOption(IntPtr.Zero, INTERNET_OPTION_SETTINGS_CHANGED, IntPtr.Zero, 0);
            InternetSetOption(IntPtr.Zero, INTERNET_OPTION_REFRESH, IntPtr.Zero, 0);
        }
        catch
        {
            // 通知失败不影响注册表已写入
        }
    }
}