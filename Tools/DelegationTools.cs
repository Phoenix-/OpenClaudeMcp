using System.ComponentModel;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Server;
using OpenClaudeMcp.Logging;
using OpenClaudeMcp.Runner;

namespace OpenClaudeMcp.Tools;

[McpServerToolType]
public sealed class DelegationTools
{
    private readonly OpenClaudeRunner _runner;

    public DelegationTools(OpenClaudeRunner runner) => _runner = runner;

    [McpServerTool(Name = "delegate_task")]
    [Description(
        "Delegate a coding task to the local OpenClaude agent (a Claude Code fork running on a local/cheap model). " +
        "OpenClaude works in the given working_directory with file editing tools enabled. " +
        "Use for: bulk refactors, generating tests, rote edits, summarization — work that doesn't need the main model's reasoning quota. " +
        "Returns the agent's final text response, exit code, and usage stats.")]
    public async Task<string> DelegateTask(
        [Description("The task for the agent to perform. Be self-contained — the agent has no prior conversation context.")]
        string task,
        [Description("Absolute path to the working directory. If omitted, uses DefaultWorkingDirectory from config.")]
        string? working_directory = null,
        [Description("Optional model override (e.g. 'deepseek-chat', 'qwen2.5-coder:32b'). If omitted, uses config default.")]
        string? model = null,
        [Description("Optional provider override (openai, ollama, gemini, ...). If omitted, uses config default.")]
        string? provider = null,
        CancellationToken ct = default)
    {
        FileLog.Info($"delegate_task called, prompt length={task?.Length ?? 0}, wd={working_directory ?? "(default)"}");
        if (string.IsNullOrWhiteSpace(task))
            return Error("task is required");

        try
        {
            var result = await _runner.RunAsync(new OpenClaudeRunRequest
            {
                Prompt = task,
                WorkingDirectory = working_directory,
                Model = model,
                Provider = provider,
            }, ct);

            return FormatResult(result);
        }
        catch (Exception ex)
        {
            FileLog.Error("delegate_task failed", ex);
            return Error($"delegate_task failed: {ex.Message}");
        }
    }

    [McpServerTool(Name = "delegate_research")]
    [Description(
        "Like delegate_task, but read-only: the local agent cannot edit, write, or run bash. " +
        "Use for: 'find where X is defined', 'which files reference Y', 'summarize what this module does'. " +
        "Cheaper and safer than delegate_task for pure exploration.")]
    public async Task<string> DelegateResearch(
        [Description("The research question. Self-contained — no prior conversation context.")]
        string question,
        [Description("Absolute path to the working directory. If omitted, uses DefaultWorkingDirectory from config.")]
        string? working_directory = null,
        [Description("Optional model override. If omitted, uses config default.")]
        string? model = null,
        [Description("Optional provider override. If omitted, uses config default.")]
        string? provider = null,
        CancellationToken ct = default)
    {
        FileLog.Info($"delegate_research called, question length={question?.Length ?? 0}, wd={working_directory ?? "(default)"}");
        if (string.IsNullOrWhiteSpace(question))
            return Error("question is required");

        try
        {
            var result = await _runner.RunAsync(new OpenClaudeRunRequest
            {
                Prompt = question,
                WorkingDirectory = working_directory,
                Model = model,
                Provider = provider,
                DisallowedTools = new[] { "Edit", "Write", "Bash", "NotebookEdit" },
            }, ct);

            return FormatResult(result);
        }
        catch (Exception ex)
        {
            FileLog.Error("delegate_research failed", ex);
            return Error($"delegate_research failed: {ex.Message}");
        }
    }

    private static string FormatResult(OpenClaudeRunResult result)
    {
        var sb = new StringBuilder();

        if (result.Parsed is { } parsed)
        {
            if (parsed.IsError == true)
                sb.AppendLine("[openclaude reported is_error=true]");

            if (!string.IsNullOrWhiteSpace(parsed.Result))
                sb.AppendLine(parsed.Result);
            else if (!string.IsNullOrWhiteSpace(result.Stdout))
                sb.AppendLine(result.Stdout.Trim());

            sb.AppendLine();
            sb.AppendLine("---");
            sb.Append("exit=").Append(result.ExitCode)
              .Append(", duration=").AppendFormat("{0:F1}", result.DurationSeconds).Append("s");
            if (parsed.NumTurns is { } turns) sb.Append(", turns=").Append(turns);
            if (parsed.TotalCostUsd is { } cost) sb.Append(", cost=$").Append(cost.ToString("F4", System.Globalization.CultureInfo.InvariantCulture));
            if (parsed.SessionId is { Length: > 0 } sid) sb.Append(", session=").Append(sid);
            sb.AppendLine();
        }
        else
        {
            // Fallback: openclaude didn't produce JSON. Return raw stdout + diagnostics.
            sb.AppendLine(result.Stdout.Trim());
            sb.AppendLine();
            sb.AppendLine("---");
            sb.Append("exit=").Append(result.ExitCode)
              .Append(", duration=").AppendFormat("{0:F1}", result.DurationSeconds).AppendLine("s");
            if (!string.IsNullOrWhiteSpace(result.Stderr))
            {
                sb.AppendLine("stderr:");
                sb.AppendLine(result.Stderr.Trim());
            }
        }

        return sb.ToString().TrimEnd();
    }

    private static string Error(string message) => $"[error] {message}";
}
