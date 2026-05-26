using System.Globalization;

namespace OpenClaudeMcp.Logging;

/// File logger. Stdout is reserved for the MCP protocol, so all diagnostics go to a file.
public static class FileLog
{
    private static readonly object Gate = new();
    private static string? _path;

    public static void Initialize(string? configuredPath)
    {
        var path = string.IsNullOrWhiteSpace(configuredPath)
            ? Path.Combine(AppContext.BaseDirectory, "openclaude-mcp.log")
            : configuredPath;

        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        _path = path;
        Write("INFO", $"FileLog initialized at {path}");
    }

    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("WARN", message);
    public static void Error(string message, Exception? ex = null)
        => Write("ERROR", ex is null ? message : $"{message}: {ex}");

    private static void Write(string level, string message)
    {
        if (_path is null) return;
        var line = $"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)} [{level}] {message}{Environment.NewLine}";
        try
        {
            lock (Gate)
                File.AppendAllText(_path, line);
        }
        catch
        {
            // Never let logging crash the server.
        }
    }
}
