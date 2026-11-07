using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;

namespace ClashProxy.Core;

/// <summary>settings.json 读写。</summary>
public static class SettingsService
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(AppPaths.SettingsFile))
            {
                var json = File.ReadAllText(AppPaths.SettingsFile);
                var s = JsonSerializer.Deserialize<AppSettings>(json, Options);
                if (s != null)
                {
                    if (s.Sites == null) s.Sites = new ObservableCollection<SiteRule>();
                    if (s.Apps == null) s.Apps = new ObservableCollection<AppEntry>();
                    return s;
                }
            }
            else
            {
                // 首次运行：把默认设置落盘，方便用户直接打开 settings.json 改端口等参数
                var fresh = new AppSettings();
                try
                {
                    Save(fresh);
                }
                catch (Exception ex)
                {
                    ClashProxy.App.Trace($"首次写入 settings.json 失败：{AppPaths.SettingsFile} :: {ex.Message}");
                }
                return fresh;
            }
        }
        catch (Exception ex)
        {
            // 配置损坏时退回默认值，不让应用启动失败
            ClashProxy.App.Trace("读取 settings.json 失败：" + ex.Message);
        }

        return new AppSettings();
    }

    public static void Save(AppSettings settings)
    {
        AppPaths.Ensure();
        var json = JsonSerializer.Serialize(settings, Options);
        File.WriteAllText(AppPaths.SettingsFile, json);
    }
}
