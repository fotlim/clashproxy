using System;
using System.IO;
using System.Text;

internal static class Program
{
    private static void Main()
    {
        var sb = new StringBuilder();
        foreach (var key in new[] { "HTTP_PROXY", "HTTPS_PROXY", "ALL_PROXY", "NO_PROXY" })
        {
            sb.AppendLine(key + "=" + Environment.GetEnvironmentVariable(key));
        }

        var dir = Path.GetDirectoryName(typeof(Program).Assembly.Location) ?? ".";
        File.WriteAllText(Path.Combine(dir, "envdump.txt"), sb.ToString(), new UTF8Encoding(false));
    }
}