using System;
using Microsoft.UI.Xaml.Controls;

namespace ClashProxy.Views;

public sealed partial class HelpPage : Page
{
    private const string DeveloperUrl = "https://github.com/fotlim";
    private const string RepoUrl = "https://github.com/fotlim/clashproxy";

    public HelpPage()
    {
        InitializeComponent();
        DataPathText.Text = Core.AppPaths.DataDir + Environment.NewLine
            + @"其中 config.yaml 是生成的内核配置，settings.json 是你的设置，profiles\current.yaml 是订阅原文。";

        GithubLink.NavigateUri = new Uri(DeveloperUrl);
        RepoLink.NavigateUri = new Uri(RepoUrl);
        VersionText.Text = "v" + (Version() ?? "1.0.0");
    }

    private static string? Version()
    {
        try
        {
            var asm = typeof(HelpPage).Assembly;
            var info = asm.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false);
            if (info is { Length: > 0 } &&
                info[0] is System.Reflection.AssemblyInformationalVersionAttribute v &&
                !string.IsNullOrWhiteSpace(v.InformationalVersion))
            {
                var text = v.InformationalVersion;
                var plus = text.IndexOf('+');
                return plus > 0 ? text[..plus] : text;
            }
            return asm.GetName().Version?.ToString(3);
        }
        catch
        {
            return null;
        }
    }
}