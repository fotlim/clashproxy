using System;
using System.IO;

namespace ClashProxy.Core;

/// <summary>应用的数据目录与内置资源路径。</summary>
public static class AppPaths
{
    /// <summary>
    /// 数据目录。默认 %LOCALAPPDATA%\ClashProxy；
    /// 设置了环境变量 CLASHPROXY_DATA 时优先使用它（便携模式/受限环境测试用）。
    /// </summary>
    public static string DataDir
    {
        get
        {
            var overridden = Environment.GetEnvironmentVariable("CLASHPROXY_DATA");
            if (!string.IsNullOrWhiteSpace(overridden))
            {
                return overridden;
            }

            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClashProxy");
        }
    }

    public static string ConfigFile => Path.Combine(DataDir, "config.yaml");
    public static string SettingsFile => Path.Combine(DataDir, "settings.json");
    public static string ProfilesDir => Path.Combine(DataDir, "profiles");
    public static string LogDir => Path.Combine(DataDir, "logs");
    public static string CurrentProfile => Path.Combine(ProfilesDir, "current.yaml");

    public static string BundledDir => Path.Combine(AppContext.BaseDirectory, "Assets", "bin");
    public static string KernelExe => Path.Combine(BundledDir, "mihomo.exe");
    public static string GeoDb => Path.Combine(DataDir, "geoip.metadb");

    /// <summary>建立数据目录。任何一步失败都不抛异常，交给上层按可用状态降级处理。</summary>
    public static void Ensure()
    {
        TryCreate(DataDir);
        TryCreate(ProfilesDir);
        TryCreate(LogDir);

        var src = Path.Combine(BundledDir, "geoip.metadb");
        if (File.Exists(src) && !File.Exists(GeoDb))
        {
            try
            {
                File.Copy(src, GeoDb, true);
            }
            catch
            {
                // 复制不了就退回内核自带的默认库
            }
        }
    }

    private static void TryCreate(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
        }
        catch
        {
            // 目录建不出来时保持沉默，后续写文件会各自报错
        }
    }
}