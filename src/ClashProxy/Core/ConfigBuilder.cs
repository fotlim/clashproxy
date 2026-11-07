using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using YamlDotNet.RepresentationModel;
using YamlDotNet.Serialization;

namespace ClashProxy.Core;

/// <summary>
/// 把「订阅原文 + 用户设置」合成一份可直接交给 mihomo 的 config.yaml。
/// 分流核心：
///   · 7890（mixed-port）→ 走规则引擎，只放行 Sites 列表里的域名，其余 DIRECT；
///   · 7891（listeners 独立入口，proxy 固定指向 PROXY 组）→ 进来就全走代理，
///     供「指定应用」使用（Windows 上按进程名匹配不可靠，改用端口区分最稳）。
/// </summary>
public static class ConfigBuilder
{
    public const string ProxyGroup = "PROXY";
    public const string AutoGroup = "自动选择";

    /// <summary>把订阅 YAML 与自己叠加的设置合成新 YAML 文本。</summary>
    public static string Build(string subscriptionYaml, AppSettings s)
    {
        var stream = new YamlStream();
        using (var reader = new StringReader(subscriptionYaml))
        {
            stream.Load(reader);
        }

        if (stream.Documents.Count == 0 || stream.Documents[0].RootNode is not YamlMappingNode root)
        {
            throw new InvalidOperationException("订阅内容不是合法的 Clash 配置（根节点不是字典）");
        }

        // 清掉会和本地叠加设置打架的键
        foreach (var key in new[]
                 {
                     "port", "socks-port", "mixed-port", "redir-port", "tproxy-port",
                     "allow-lan", "bind-address", "mode", "log-level", "ipv6",
                     "external-controller", "secret", "rules", "proxy-groups",
                     "listeners", "tun", "interface-name", "routing-mark"
                 })
        {
            root.Children.Remove(new YamlScalarNode(key));
        }

        // 节点去重（订阅里偶有同名节点，mihomo 会报错）
        var names = new List<string>();
        if (root.Children.TryGetValue(new YamlScalarNode("proxies"), out var pn) && pn is YamlSequenceNode ps)
        {
            var seen = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var item in ps.Children)
            {
                if (item is not YamlMappingNode map) continue;
                var nameKey = new YamlScalarNode("name");
                if (!map.Children.TryGetValue(nameKey, out var nv) || nv is not YamlScalarNode nameNode) continue;

                var name = nameNode.Value ?? "";
                if (string.IsNullOrWhiteSpace(name)) continue;

                if (seen.TryGetValue(name, out var c))
                {
                    c++;
                    seen[name] = c;
                    nameNode.Value = $"{name} #{c}";
                }
                else
                {
                    seen[name] = 1;
                }

                names.Add(nameNode.Value!);
            }
        }

        if (names.Count == 0)
        {
            throw new InvalidOperationException("订阅里没有解析到任何节点");
        }

        // 基础设置
        Set(root, "mixed-port", s.RulePort.ToString());
        Set(root, "allow-lan", "false");
        Set(root, "bind-address", "127.0.0.1");
        Set(root, "mode", s.ProxyMode == "global" ? "global" : "rule");
        Set(root, "log-level", "info");
        Set(root, "ipv6", "false");
        Set(root, "external-controller", $"127.0.0.1:{s.ControllerPort}");
        if (!string.IsNullOrEmpty(s.ControllerSecret))
        {
            Set(root, "secret", s.ControllerSecret);
        }
        Set(root, "unified-delay", "true");
        Set(root, "tcp-concurrent", "true");

        // 「应用专用通道」：独立监听口，proxy 直指 PROXY 组，无视规则
        var listeners = new YamlSequenceNode();
        var appListener = new YamlMappingNode
        {
            { "name", "app-channel" },
            { "type", "mixed" },
            { "port", s.AppPort.ToString() },
            { "listen", "127.0.0.1" },
            { "proxy", ProxyGroup }
        };
        listeners.Add(appListener);
        root.Children[new YamlScalarNode("listeners")] = listeners;

        // 代理组：自动测速 + 手动指定
        var autoGroup = new YamlMappingNode
        {
            { "name", AutoGroup },
            { "type", "url-test" },
            { "url", s.TestUrl },
            { "interval", "300" },
            { "tolerance", "50" },
            { "lazy", "false" }
        };
        var autoProxies = new YamlSequenceNode();
        foreach (var n in names) autoProxies.Add(new YamlScalarNode(n));
        autoGroup.Add("proxies", autoProxies);

        var mainGroup = new YamlMappingNode
        {
            { "name", ProxyGroup },
            { "type", "select" }
        };
        var mainProxies = new YamlSequenceNode { new YamlScalarNode(AutoGroup) };
        foreach (var n in names) mainProxies.Add(new YamlScalarNode(n));
        mainGroup.Add("proxies", mainProxies);

        var groups = new YamlSequenceNode { autoGroup, mainGroup };
        root.Children[new YamlScalarNode("proxy-groups")] = groups;

        // 规则：用户勾选的域名 → PROXY，其余直连
        var rules = new YamlSequenceNode();
        foreach (var site in s.Sites ?? Enumerable.Empty<SiteRule>())
        {
            if (!site.Enabled || string.IsNullOrWhiteSpace(site.Value)) continue;
            var kind = site.Kind switch
            {
                "DOMAIN" => "DOMAIN",
                "DOMAIN-KEYWORD" => "DOMAIN-KEYWORD",
                _ => "DOMAIN-SUFFIX"
            };
            rules.Add(new YamlScalarNode($"{kind},{site.Value.Trim()},{ProxyGroup}"));
        }
        rules.Add(new YamlScalarNode("MATCH,DIRECT"));
        root.Children[new YamlScalarNode("rules")] = rules;

        // DNS：fake-ip 便于域名规则命中，同时不污染直连解析
        root.Children[new YamlScalarNode("dns")] = BuildDns();

        var sb = new StringBuilder();
        using (var writer = new StringWriter(sb))
        {
            stream.Save(writer, assignAnchors: false);
        }
        return sb.ToString();
    }

    private static YamlMappingNode BuildDns()
    {
        var dns = new YamlMappingNode
        {
            { "enable", "true" },
            { "ipv6", "false" },
            { "enhanced-mode", "fake-ip" },
            { "fake-ip-range", "198.18.0.1/16" },
            { "default-nameserver", Seq("223.5.5.5", "119.29.29.29") },
            { "nameserver", Seq("https://dns.alidns.com/dns-query", "https://doh.pub/dns-query") },
            { "fallback", Seq("https://1.1.1.1/dns-query", "https://dns.google/dns-query") },
            { "fallback-filter", new YamlMappingNode { { "geoip", "true" }, { "geoip-code", "CN" } } }
        };
        return dns;
    }

    private static YamlSequenceNode Seq(params string[] items)
    {
        var node = new YamlSequenceNode();
        foreach (var i in items) node.Add(new YamlScalarNode(i));
        return node;
    }

    private static void Set(YamlMappingNode root, string key, string value)
    {
        root.Children[new YamlScalarNode(key)] = new YamlScalarNode(value);
    }
}
