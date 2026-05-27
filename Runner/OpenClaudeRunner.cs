using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenClaudeMcp.Configuration;
using OpenClaudeMcp.Logging;

namespace OpenClaudeMcp.Runner;

public sealed class OpenClaudeRunner
{
    private readonly OpenClaudeOptions _options;
    private readonly ResolvedExecutable _resolved;

    public OpenClaudeRunner(OpenClaudeOptions options)
    {
        _options = options;
        _resolved = ExecutableResolver.Resolve(options.ExecutablePath);
        FileLog.Info($"Resolved openclaude entry: {_resolved.OriginalPath} (launcher: {_resolved.FileName})");
    }

    public async Task<OpenClaudeRunResult> RunAsync(OpenClaudeRunRequest request, CancellationToken ct)
    {
        var openclaudeArgs = BuildArgs(request);
        var allArgs = _resolved.PrependArgs.Concat(openclaudeArgs).ToList();
        var argsForLog = string.Join(' ', allArgs.Select(a => a.Contains(' ') ? $"\"{a}\"" : a));
        FileLog.Info($"Launching: {_resolved.FileName} {argsForLog}");

        var psi = new ProcessStartInfo
        {
            FileName = _resolved.FileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in allArgs) psi.ArgumentList.Add(a);

        var workingDir = ResolveWorkingDirectory(request);
        if (!string.IsNullOrWhiteSpace(workingDir))
            psi.WorkingDirectory = workingDir;

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

        var stdoutBuilder = new StringBuilder();
        var stderrBuilder = new StringBuilder();

        process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdoutBuilder.AppendLine(e.Data); };
        process.ErrorDataReceived  += (_, e) => { if (e.Data is not null) stderrBuilder.AppendLine(e.Data); };

        var sw = Stopwatch.StartNew();
        if (!process.Start())
            throw new InvalidOperationException("Failed to start openclaude process");

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        process.StandardInput.Close();

        var timeoutMs = Math.Max(1, _options.TimeoutSeconds) * 1000;
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeoutMs);

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            FileLog.Warn($"openclaude run timed out or cancelled after {sw.Elapsed.TotalSeconds:F1}s — killing process");
            try { process.Kill(entireProcessTree: true); } catch { /* swallow */ }
            throw;
        }

        sw.Stop();
        var stdout = stdoutBuilder.ToString();
        var stderr = stderrBuilder.ToString();
        FileLog.Info($"openclaude exited code={process.ExitCode} in {sw.Elapsed.TotalSeconds:F1}s, stdout={stdout.Length} chars, stderr={stderr.Length} chars");

        return new OpenClaudeRunResult
        {
            ExitCode = process.ExitCode,
            Stdout = stdout,
            Stderr = stderr,
            DurationSeconds = sw.Elapsed.TotalSeconds,
            Parsed = TryParseJson(stdout),
        };
    }

    private List<string> BuildArgs(OpenClaudeRunRequest request)
    {
        var args = new List<string>
        {
            "-p", request.Prompt,
            "--output-format", "json",
        };

        if (_options.UseBareMode)
            args.Add("--bare");

        if (!string.IsNullOrWhiteSpace(_options.HeadlessSystemPrompt))
        {
            args.Add("--append-system-prompt");
            args.Add(_options.HeadlessSystemPrompt);
        }

        var permissionMode = string.IsNullOrWhiteSpace(request.PermissionModeOverride)
            ? _options.DefaultPermissionMode
            : request.PermissionModeOverride;
        if (!string.IsNullOrWhiteSpace(permissionMode))
        {
            args.Add("--permission-mode");
            args.Add(permissionMode);
        }

        var workingDir = ResolveWorkingDirectory(request);
        if (!string.IsNullOrWhiteSpace(workingDir))
        {
            args.Add("--add-dir");
            args.Add(workingDir);
        }

        var model = string.IsNullOrWhiteSpace(request.Model) ? _options.DefaultModel : request.Model;
        if (!string.IsNullOrWhiteSpace(model))
        {
            args.Add("--model");
            args.Add(model);
        }

        var provider = string.IsNullOrWhiteSpace(request.Provider) ? _options.DefaultProvider : request.Provider;
        if (!string.IsNullOrWhiteSpace(provider))
        {
            args.Add("--provider");
            args.Add(provider);
        }

        if (request.AllowedTools is { Length: > 0 })
        {
            args.Add("--allowedTools");
            args.Add(string.Join(' ', request.AllowedTools));
        }

        if (request.DisallowedTools is { Length: > 0 })
        {
            args.Add("--disallowedTools");
            args.Add(string.Join(' ', request.DisallowedTools));
        }

        if (_options.MaxBudgetUsdPerTask is { } budget && budget > 0)
        {
            args.Add("--max-budget-usd");
            args.Add(budget.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        return args;
    }

    private string ResolveWorkingDirectory(OpenClaudeRunRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.WorkingDirectory))
            return request.WorkingDirectory!;
        return _options.DefaultWorkingDirectory ?? "";
    }

    private static OpenClaudeJsonOutput? TryParseJson(string stdout)
    {
        var trimmed = stdout.Trim();
        if (trimmed.Length == 0 || trimmed[0] != '{') return null;
        try
        {
            return JsonSerializer.Deserialize<OpenClaudeJsonOutput>(trimmed, JsonOpts);
        }
        catch (JsonException ex)
        {
            FileLog.Warn($"Failed to parse openclaude JSON output: {ex.Message}");
            return null;
        }
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };
}

public sealed class OpenClaudeRunRequest
{
    public required string Prompt { get; init; }
    public string? WorkingDirectory { get; init; }
    public string? Model { get; init; }
    public string? Provider { get; init; }
    public string? PermissionModeOverride { get; init; }
    public string[]? AllowedTools { get; init; }
    public string[]? DisallowedTools { get; init; }
}

public sealed class OpenClaudeRunResult
{
    public int ExitCode { get; init; }
    public string Stdout { get; init; } = "";
    public string Stderr { get; init; } = "";
    public double DurationSeconds { get; init; }
    public OpenClaudeJsonOutput? Parsed { get; init; }
}

/// Loose mapping of the openclaude --output-format json envelope.
/// Field names follow Claude Code conventions; unknown fields are tolerated.
public sealed class OpenClaudeJsonOutput
{
    [JsonPropertyName("type")] public string? Type { get; set; }
    [JsonPropertyName("subtype")] public string? Subtype { get; set; }
    [JsonPropertyName("result")] public string? Result { get; set; }
    [JsonPropertyName("is_error")] public bool? IsError { get; set; }
    [JsonPropertyName("duration_ms")] public long? DurationMs { get; set; }
    [JsonPropertyName("session_id")] public string? SessionId { get; set; }
    [JsonPropertyName("total_cost_usd")] public decimal? TotalCostUsd { get; set; }
    [JsonPropertyName("num_turns")] public int? NumTurns { get; set; }
    [JsonPropertyName("usage")] public JsonElement? Usage { get; set; }
}
