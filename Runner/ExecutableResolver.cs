using OpenClaudeMcp.Logging;

namespace OpenClaudeMcp.Runner;

/// Finds the openclaude entry point. npm on Windows installs three files for a global package:
///   openclaude       (bash shim, ignored on Windows)
///   openclaude.cmd   (CMD shim — runs node with the JS entrypoint)
///   openclaude.ps1   (PowerShell shim)
/// We prefer .cmd because it can be launched directly via Process.Start. .ps1 requires pwsh -File.
/// Discovery order:
/// 1. Explicit ExecutablePath from config (if file exists). If it's a .ps1, we wrap with pwsh -File at launch time.
/// 2. .cmd next to the configured path (same dir, same basename).
/// 3. `where openclaude.cmd` via system PATH.
/// 4. Common npm global locations (%APPDATA%\npm).
public static class ExecutableResolver
{
    public static ResolvedExecutable Resolve(string configuredPath)
    {
        // 1. Honor explicit config — but if it's a .ps1, try to upgrade to the sibling .cmd first.
        if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
        {
            var upgraded = TryUpgradePs1ToCmd(configuredPath);
            return Build(upgraded ?? configuredPath);
        }

        if (!string.IsNullOrWhiteSpace(configuredPath))
            FileLog.Warn($"Configured ExecutablePath '{configuredPath}' does not exist — falling back to discovery");

        foreach (var candidate in DiscoverCandidates())
        {
            if (File.Exists(candidate))
            {
                FileLog.Info($"Discovered openclaude at {candidate}");
                return Build(candidate);
            }
        }

        throw new FileNotFoundException(
            "Could not locate openclaude.cmd. Set OpenClaude:ExecutablePath in appsettings.json " +
            "to the full path (e.g. C:\\Users\\<you>\\AppData\\Roaming\\npm\\openclaude.cmd).");
    }

    private static string? TryUpgradePs1ToCmd(string path)
    {
        if (!path.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase)) return null;
        var sibling = Path.ChangeExtension(path, ".cmd");
        if (File.Exists(sibling))
        {
            FileLog.Info($"Configured path was .ps1; upgraded to sibling .cmd at {sibling}");
            return sibling;
        }
        return null;
    }

    private static ResolvedExecutable Build(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".ps1" => new ResolvedExecutable(
                FileName: FindPwsh(),
                PrependArgs: new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", path },
                OriginalPath: path),
            _ => new ResolvedExecutable(
                FileName: path,
                PrependArgs: Array.Empty<string>(),
                OriginalPath: path),
        };
    }

    private static string FindPwsh()
    {
        // Prefer pwsh (PS 7+); fall back to Windows PowerShell 5.
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathEnv.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;
            var pwsh = Path.Combine(dir, "pwsh.exe");
            if (File.Exists(pwsh)) return pwsh;
        }
        // Last-resort fallback. powershell.exe is on every Windows install.
        return "powershell.exe";
    }

    private static IEnumerable<string> DiscoverCandidates()
    {
        // Prefer .cmd over .ps1 (no shell wrapping needed).
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathEnv.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;
            yield return Path.Combine(dir, "openclaude.cmd");
            yield return Path.Combine(dir, "openclaude.exe");
        }

        var appData = Environment.GetEnvironmentVariable("APPDATA");
        if (!string.IsNullOrEmpty(appData))
        {
            yield return Path.Combine(appData, "npm", "openclaude.cmd");
            yield return Path.Combine(appData, "npm", "openclaude.exe");
        }

        var pf = Environment.GetEnvironmentVariable("ProgramFiles");
        if (!string.IsNullOrEmpty(pf))
            yield return Path.Combine(pf, "nodejs", "openclaude.cmd");

        // .ps1 fallback last.
        foreach (var dir in pathEnv.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;
            yield return Path.Combine(dir, "openclaude.ps1");
        }
        if (!string.IsNullOrEmpty(appData))
            yield return Path.Combine(appData, "npm", "openclaude.ps1");
    }
}

/// Result of resolving how to launch openclaude.
/// <param name="FileName">What to pass to ProcessStartInfo.FileName (e.g. openclaude.cmd OR pwsh.exe).</param>
/// <param name="PrependArgs">Args that must come BEFORE the user-supplied openclaude args (e.g. -File path.ps1).</param>
/// <param name="OriginalPath">The actual openclaude entry point, kept for logs.</param>
public sealed record ResolvedExecutable(string FileName, IReadOnlyList<string> PrependArgs, string OriginalPath);
