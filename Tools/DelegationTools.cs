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
        "Delegate a MECHANICAL coding task to a local/cheap model (a Claude Code fork with file editing + bash enabled). " +
        "Purpose: conserve the calling model's quota by offloading high-volume rote work.\n" +
        "DELEGATE ONLY when the mechanical work clearly outweighs the cost of writing the brief AND verifying the result. " +
        "The verify step is not optional: the cheap model hallucinates and fails silently, so its output is UNTRUSTED and you must check it — fold that cost into the decision.\n" +
        "Good fits: one well-defined pattern applied across many files; generating tests for many modules; large-scale find/rename; bulk summarization. " +
        "Do NOT delegate: single-file or few-line edits (cheaper to just do them); anything needing judgment, taste, or design decisions; anything where a wrong/hallucinated result is expensive to catch; tasks you'd spend as many tokens briefing+checking as doing yourself. " +
        "The brief must be fully self-contained — the agent has zero conversation context. " +
        "Returns the agent's final text, exit code, and usage stats.")]
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
        "Read-only delegation to a local/cheap model: the agent cannot Edit, Write, or run Bash. Use for exploration that conserves quota.\n" +
        "DELEGATE ONLY when the search space is large enough that scanning it yourself would burn meaningful context — e.g. 'which of these 200 files reference X', 'summarize this large unfamiliar module'. " +
        "For a quick lookup you could do in one or two Grep/Read calls, just do it — delegating is slower and costs more end-to-end.\n" +
        "The cheap model's findings are UNTRUSTED: it may miss matches or invent paths. Treat the result as a lead to verify, not ground truth, especially before acting on it. " +
        "The question must be fully self-contained — the agent has zero conversation context.")]
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
            // No JSON envelope. With exit=0 this almost always means the cheap model produced a
            // conversational reply ("Hi, ready to help!") instead of executing — it did NOT do the
            // work. Flag it loudly so the caller doesn't mistake the greeting for a result.
            if (result.ExitCode == 0)
            {
                sb.AppendLine(
                    "[warning] openclaude exited 0 but produced no JSON result envelope. " +
                    "The model likely did not execute the task (cheap models tend to reply " +
                    "conversationally to imperative prompts). Treat the text below as UNTRUSTED — " +
                    "if it's a greeting/restatement rather than the work, re-delegate phrasing the " +
                    "task as a question, or do it yourself.");
                sb.AppendLine();
            }

            sb.AppendLine("raw output:");
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
