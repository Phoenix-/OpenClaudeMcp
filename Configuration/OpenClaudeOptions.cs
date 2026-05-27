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

    /// Appended via --append-system-prompt on every run. Forces headless execution:
    /// cheap models (e.g. mimo) otherwise read imperative prompts as "let's discuss a plan"
    /// and reply with a greeting instead of doing the work. Empty = don't append anything.
    public string HeadlessSystemPrompt { get; set; } =
        "You are running headless via -p, invoked programmatically by another agent. " +
        "Execute the request immediately using your tools. Do not greet, do not restate the plan, " +
        "do not ask for clarification, do not announce what you are about to do. " +
        "Just do the work and report the concrete result.";

    /// If true, pass --bare: skip CLAUDE.md auto-discovery, auto-memory, hooks, LSP, plugin sync,
    /// attribution, prefetches. Right for a stateless delegate; pair with HeadlessSystemPrompt.
    public bool UseBareMode { get; set; } = true;
}
