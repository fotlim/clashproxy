using System;
using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ClashProxy.Views;

public sealed partial class LogsPage : Page
{
    public LogsPage()
    {
        InitializeComponent();
        LogList.ItemsSource = App.Host.Logs;
        Loaded += (_, _) => UpdateHint();
        App.Host.Logs.CollectionChanged += (_, _) =>
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                UpdateHint();
                if (LogList.Items.Count > 0)
                {
                    LogList.ScrollIntoView(LogList.Items[^1]);
                }
            });
        };
    }

    private void UpdateHint() => HintText.Text = $"共 {App.Host.Logs.Count} 行。";

    private void RefreshBtn_Click(object sender, RoutedEventArgs e) => UpdateHint();

    private void ClearBtn_Click(object sender, RoutedEventArgs e)
    {
        App.Host.Logs.Clear();
        UpdateHint();
    }

    private void OpenBtn_Click(object sender, RoutedEventArgs e)
    {
        Core.AppPaths.Ensure();
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{Core.AppPaths.LogDir}\"")
            {
                UseShellExecute = true
            });
        }
        catch
        {
            // 忽略
        }
    }
}
