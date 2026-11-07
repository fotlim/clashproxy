using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using YamlDotNet.RepresentationModel;

namespace ClashProxy.Core;

/// <summary>订阅下载与解析。订阅内容即 Clash 的 config.yaml 格式。</summary>
public static class SubscriptionService
{
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.All
        };
        var c = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(60) };
        c.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "clash-verge/v2.0.0");
        return c;
    }

    /// <summary>下载订阅文本。失败抛异常。</summary>
    public static async Task<string> DownloadAsync(string url, CancellationToken ct = default)
    {
        var text = await Http.GetStringAsync(url, ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidOperationException("订阅内容为空");
        }
        return text;
    }

    /// <summary>解析订阅文本里的节点列表。</summary>
    public static List<NodeItem> ParseNodes(string yamlText)
    {
        var result = new List<NodeItem>();
        var stream = new YamlStream();
        using var reader = new StringReader(yamlText);
        stream.Load(reader);

        if (stream.Documents.Count == 0) return result;
        if (stream.Documents[0].RootNode is not YamlMappingNode root) return result;
        if (!root.Children.TryGetValue(new YamlScalarNode("proxies"), out var proxiesNode)) return result;
        if (proxiesNode is not YamlSequenceNode seq) return result;

        foreach (var item in seq.Children)
        {
            if (item is not YamlMappingNode map) continue;

            var node = new NodeItem
            {
                Name = Scalar(map, "name"),
                Type = Scalar(map, "type"),
                Server = Scalar(map, "server"),
                Port = int.TryParse(Scalar(map, "port"), out var p) ? p : 0
            };

            if (!string.IsNullOrEmpty(node.Name))
            {
                result.Add(node);
            }
        }

        return result;
    }

    private static string Scalar(YamlMappingNode map, string key)
    {
        if (map.Children.TryGetValue(new YamlScalarNode(key), out var v) && v is YamlScalarNode s)
        {
            return s.Value ?? "";
        }
        return "";
    }
}
