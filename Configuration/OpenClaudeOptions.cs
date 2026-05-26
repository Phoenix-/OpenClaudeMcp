namespace OpenClaudeMcp.Configuration;

public sealed class OpenClaudeOptions
{
    /// Full path to openclaude.cmd / openclaude.exe. If empty, we try to discover it via PATH and common npm locations.
    public string ExecutablePath { get; set; } = "";

    /// Model passed via --model. Empty = let openclaude decide.
    public string DefaultModel { get; set; } = "";

    /// Provider passed via --provider (openai/ollama/deepseek/gemini/...). Empty = openclaude default.
    public string DefaultProvider { get; set; } = "";

    /// One of: default, acceptEdits, bypassPermissions, dontAsk, plan, auto.
    public string DefaultPermissionMode { get; set; } = "acceptEdits";

    /// If set, used when caller doesn't pass working_directory.
    public string DefaultWorkingDirectory { get; set; } = "";

    /// If set, passed as --max-budget-usd (caps per-task spend on paid providers).
    public decimal? MaxBudgetUsdPerTask { get; set; }

    /// Hard timeout on each openclaude run.
    public int TimeoutSeconds { get; set; } = 600;

    /// File log destination. Empty = next to the exe.
    public string LogFilePath { get; set; } = "";
}
