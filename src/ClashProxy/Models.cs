using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json.Serialization;

namespace ClashProxy;

/// <summary>一条订阅节点。Latency 变化后通知界面刷新。</summary>
public sealed class NodeItem : INotifyPropertyChanged
{
    private string _latency = "—";

    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public string Server { get; set; } = "";
    public int Port { get; set; }

    public string Latency
    {
        get => _latency;
        set
        {
            if (_latency == value)
            {
                return;
            }

            _latency = value;
            Raise(nameof(Latency));
            Raise(nameof(Display));
        }
    }

    [JsonIgnore]
    public string Display => Latency == "—" ? Name : $"{Name}  ·  {Latency}";

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>一条分流规则（网站 / 域名）。</summary>
public sealed class SiteRule
{
    public string Kind { get; set; } = "DOMAIN-SUFFIX";
    public string Value { get; set; } = "";
    public bool Enabled { get; set; } = true;

    [JsonIgnore]
    public string KindLabel => Kind switch
    {
        "DOMAIN" => "精确域名",
        "DOMAIN-SUFFIX" => "域名后缀",
        "DOMAIN-KEYWORD" => "关键字",
        _ => Kind
    };

    [JsonIgnore]
    public string Display => $"{KindLabel} · {Value}";
}

/// <summary>一个需要走代理的应用。</summary>
public sealed class AppEntry
{
    public string Name { get; set; } = "";
    public string ExePath { get; set; } = "";
    public bool Enabled { get; set; } = true;

    [JsonIgnore]
    public string Display => string.IsNullOrEmpty(Name) ? System.IO.Path.GetFileName(ExePath) : Name;

    [JsonIgnore]
    public string Detail => ExePath;
}

/// <summary>全部可持久化设置。</summary>
public sealed class AppSettings
{
    public string SubscriptionUrl { get; set; } = "";
    public string SelectedNode { get; set; } = "";
    /// <summary>rules = 只代理列表中的网站；global = 全部流量走代理。</summary>
    public string ProxyMode { get; set; } = "rules";
    public int RulePort { get; set; } = 7890;
    public int AppPort { get; set; } = 7891;
    public int ControllerPort { get; set; } = 9091;
    public string ControllerSecret { get; set; } = "";
    public string TestUrl { get; set; } = "https://www.gstatic.com/generate_204";
    public bool TunMode { get; set; } = false;
    public bool StartOnLaunch { get; set; } = false;
    /// <summary>是否让 Windows 系统代理指向规则通道（7890），默认关闭。</summary>
    public bool SystemProxyOn { get; set; } = false;
    public ObservableCollection<SiteRule> Sites { get; set; } = new();
    public ObservableCollection<AppEntry> Apps { get; set; } = new();
}

/// <summary>一行日志。</summary>
public sealed class LogEntry
{
    public string Time { get; set; } = "";
    public string Level { get; set; } = "info";
    public string Message { get; set; } = "";

    [JsonIgnore]
    public string Display => $"{Time}  {Message}";

    [JsonIgnore]
    public Microsoft.UI.Xaml.Media.SolidColorBrush LevelBrush => Level switch
    {
        "error" => new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.OrangeRed),
        "warn" => new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Goldenrod),
        _ => new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 190, 190, 190))
    };
}
