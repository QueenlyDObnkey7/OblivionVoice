using System.Reflection;
using System.Runtime.CompilerServices;

namespace OblivionVoice.Server;

internal static class ServerStartupTrace
{
    private static readonly object Gate = new();

    public static string TracePath
    {
        get
        {
            var assemblyDir = Path.GetDirectoryName(typeof(ServerStartupTrace).Assembly.Location);
            if (string.IsNullOrWhiteSpace(assemblyDir))
                assemblyDir = AppContext.BaseDirectory;

            return Path.Combine(assemblyDir!, "OblivionVoice.server.trace.log");
        }
    }

    [ModuleInitializer]
    internal static void ModuleLoaded()
    {
        Write(
            "MODULE LOADED",
            $"Assembly={typeof(ServerStartupTrace).Assembly.FullName}",
            $"Location={typeof(ServerStartupTrace).Assembly.Location}",
            $"BaseDirectory={AppContext.BaseDirectory}",
            $".NET={Environment.Version}");
    }

    public static void Write(string stage, params string[] details)
    {
        try
        {
            lock (Gate)
            {
                var lines = new List<string>
                {
                    $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz}] {stage}"
                };

                foreach (var detail in details)
                {
                    if (!string.IsNullOrWhiteSpace(detail))
                        lines.Add($"    {detail}");
                }

                File.AppendAllLines(TracePath, lines);
            }
        }
        catch
        {

        }
    }

    public static void Exception(string stage, Exception ex)
    {
        Write(
            stage,
            $"ExceptionType={ex.GetType().FullName}",
            $"Message={ex.Message}",
            ex.ToString());
    }
}
