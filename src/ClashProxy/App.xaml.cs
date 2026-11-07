using System;
using System.IO;
using ClashProxy.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ClashProxy;

public partial class App : Application
{
    public static Window? MainWindow => _window;

    private static Window? _window;

    public static void SetMainWindow(Window w) => _window = w;

    private static KernelHost? _host;
    public static KernelHost Host
    {
        get
        {
            if (_host == null)
            {
                Trace("Host: lazy create");
                _host = new KernelHost();
                Trace("Host: created");
            }
            return _host;
        }
    }

    private static readonly string TraceFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ClashProxy", "logs", "startup.log");

    internal static void Trace(string message)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(TraceFile)!);
            File.AppendAllText(TraceFile, $"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}");
        }
        catch
        {
            // 追踪失败不应影响启动
        }
    }

    public App()
    {
        Trace("App ctor: enter");
        InitializeComponent();
        Trace("App ctor: InitializeComponent done");

        UnhandledException += (_, e) =>
        {
            Trace("UnhandledException: " + e.Exception);
            try
            {
                var path = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "ClashProxy", "logs", "crash.log");
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.AppendAllText(path, $"[{DateTime.Now:O}] {e.Exception}{Environment.NewLine}");
            }
            catch
            {
                // 写崩溃日志失败就放弃
            }
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Trace("OnLaunched: enter");
        try
        {
            var w = new MainWindow();
            Trace("OnLaunched: MainWindow created");
            SetMainWindow(w);
            w.Closed += (_, _) => _host?.Shutdown();
            w.Activate();
            Trace("OnLaunched: activated");
        }
        catch (Exception ex)
        {
            Trace("OnLaunched: FAILED " + ex);
            ShowFatal(ex);
        }
    }

    /// <summary>主窗口搭不起来时，至少给用户一个能看见的错误窗口，而不是静默退出。</summary>
    private static void ShowFatal(Exception ex)
    {
        try
        {
            var log = Path.Combine(AppPaths.LogDir, "startup.log");
            var win = new Window { Title = "ClashProxy 启动失败" };
            win.Content = new ScrollViewer
            {
                Padding = new Thickness(24),
                Content = new TextBlock
                {
                    Text = $"ClashProxy 启动失败：{ex.Message}\n\n详细信息已写入：{log}\n\n{ex}",
                    TextWrapping = TextWrapping.Wrap,
                    IsTextSelectionEnabled = true
                }
            };
            win.Activate();
            _window = win;
        }
        catch
        {
            // 连错误窗口都开不出来就只能退出
        }
    }
}


