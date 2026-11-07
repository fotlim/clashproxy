using System;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace ClashProxy.Views;

public sealed partial class RulesPage : Page
{
    private static readonly (string Kind, string Value)[] Presets =
    {
        ("DOMAIN-SUFFIX", "google.com"),
        ("DOMAIN-SUFFIX", "googleapis.com"),
        ("DOMAIN-SUFFIX", "gstatic.com"),
        ("DOMAIN-SUFFIX", "youtube.com"),
        ("DOMAIN-SUFFIX", "ytimg.com"),
        ("DOMAIN-SUFFIX", "github.com"),
        ("DOMAIN-SUFFIX", "githubusercontent.com"),
        ("DOMAIN-SUFFIX", "githubassets.com"),
        ("DOMAIN-SUFFIX", "openai.com"),
        ("DOMAIN-SUFFIX", "anthropic.com"),
        ("DOMAIN-SUFFIX", "claude.ai"),
        ("DOMAIN-SUFFIX", "chatgpt.com"),
        ("DOMAIN-SUFFIX", "x.com"),
        ("DOMAIN-SUFFIX", "twitter.com"),
        ("DOMAIN-SUFFIX", "twimg.com"),
        ("DOMAIN-SUFFIX", "telegram.org"),
        ("DOMAIN-SUFFIX", "t.me"),
        ("DOMAIN-SUFFIX", "discord.com"),
        ("DOMAIN-SUFFIX", "discordapp.com"),
        ("DOMAIN-SUFFIX", "npmjs.org"),
        ("DOMAIN-SUFFIX", "nuget.org"),
        ("DOMAIN-SUFFIX", "huggingface.co"),
        ("DOMAIN-SUFFIX", "docker.com"),
        ("DOMAIN-SUFFIX", "docker.io"),
        ("DOMAIN-SUFFIX", "wikipedia.org"),
        ("DOMAIN-SUFFIX", "reddit.com"),
        ("DOMAIN-SUFFIX", "medium.com"),
        ("DOMAIN-SUFFIX", "netflix.com"),
        ("DOMAIN-SUFFIX", "spotify.com"),
        ("DOMAIN-SUFFIX", "cloudflare.com"),
        ("DOMAIN-SUFFIX", "vercel.app"),
        ("DOMAIN-SUFFIX", "sentry.io")
    };

    public RulesPage()
    {
        InitializeComponent();
        RuleList.ItemsSource = App.Host.Settings.Sites;
        Loaded += (_, _) => HintText.Text = $"共 {App.Host.Settings.Sites.Count} 条规则。";
    }

    private void ValueBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            AddRule();
            e.Handled = true;
        }
    }

    private void AddBtn_Click(object sender, RoutedEventArgs e) => AddRule();

    private void AddRule()
    {
        var value = (ValueBox.Text ?? "").Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            HintText.Text = "请输入域名。";
            return;
        }

        // 去掉协议前缀和路径，只留主机名
        value = value.Replace("https://", "").Replace("http://", "").Replace("*.", "");
        var slash = value.IndexOf('/');
        if (slash > 0) value = value[..slash];
        value = value.TrimEnd('.').ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(value))
        {
            HintText.Text = "域名无效。";
            return;
        }

        var kind = (KindBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "DOMAIN-SUFFIX";

        var sites = App.Host.Settings.Sites;
        if (sites.Any(x => string.Equals(x.Value, value, StringComparison.OrdinalIgnoreCase) && x.Kind == kind))
        {
            HintText.Text = $"「{value}」已经在列表里了。";
            return;
        }

        sites.Add(new SiteRule { Kind = kind, Value = value, Enabled = true });
        ValueBox.Text = "";
        HintText.Text = $"已添加 {value}，记得「保存并应用」。";
    }

    private void DeleteBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is SiteRule rule)
        {
            App.Host.Settings.Sites.Remove(rule);
            HintText.Text = $"已删除 {rule.Value}，记得「保存并应用」。";
        }
    }

    private void Rule_Toggled(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox cb && cb.DataContext is SiteRule rule)
        {
            rule.Enabled = cb.IsChecked == true;
        }
    }

    private void PresetBtn_Click(object sender, RoutedEventArgs e)
    {
        var sites = App.Host.Settings.Sites;
        var added = 0;
        foreach (var (kind, value) in Presets)
        {
            if (sites.Any(x => string.Equals(x.Value, value, StringComparison.OrdinalIgnoreCase))) continue;
            sites.Add(new SiteRule { Kind = kind, Value = value, Enabled = true });
            added++;
        }
        HintText.Text = added == 0 ? "常用列表已在其中。" : $"已加入 {added} 条常用域名，记得「保存并应用」。";
    }

    private void ClearBtn_Click(object sender, RoutedEventArgs e)
    {
        App.Host.Settings.Sites.Clear();
        HintText.Text = "已清空，记得「保存并应用」。";
    }

    private async void ApplyBtn_Click(object sender, RoutedEventArgs e)
    {
        App.Host.Save();
        try
        {
            App.Host.AppendLog($"[规则] 正在生成配置（{App.Host.Settings.Sites.Count} 条规则）…");
            await App.Host.BuildConfigAsync();
            if (App.Host.Kernel.IsRunning)
            {
                HintText.Text = "配置已更新，正在重启内核…";
                var ok = await App.Host.RestartKernelAsync();
                HintText.Text = ok
                    ? "已保存并重启内核，新规则立即生效。"
                    : "规则已保存，但重启内核失败，请到「运行日志」查看原因。";
                (App.MainWindow as MainWindow)?.RefreshChrome();
            }
            else
            {
                HintText.Text = "已保存并生成配置。下次启动内核时生效。";
            }
        }
        catch (Exception ex)
        {
            var detail = $"{ex.GetType().Name}: {ex.Message}";
            App.Host.AppendLog($"[错误] 生成配置失败：{detail}");
            if (!string.IsNullOrWhiteSpace(ex.StackTrace))
            {
                App.Host.AppendLog(ex.StackTrace);
            }
            HintText.Text = $"规则已保存到设置里，但生成配置失败：{detail}（完整信息见「运行日志」）";
        }
    }
}
