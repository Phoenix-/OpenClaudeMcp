# OpenClaudeMcp

An MCP (Model Context Protocol) server that bridges AI coding assistants with the [OpenClaude](https://github.com/Gitlawb/openclaude) CLI, enabling a powerful model to delegate bulk or rote coding tasks to cheaper local agents.

## Why?

When working with an expensive frontier model (e.g. Claude Opus, GPT-4), you don't want to burn tokens on tasks like "rename this variable across 50 files" or "find all references to this interface." OpenClaudeMcp exposes those tasks as MCP tools so your main agent can offload them to a local, cheaper model (DeepSeek, Qwen, Ollama, etc.) running through OpenClaude -- and get the result back in seconds.

## How It Works

```
MCP Client (IDE / AI Agent)
        |  stdio (JSON-RPC)
        v
  OpenClaudeMcp
        |
        v
  openclaude CLI  -->  LLM Provider (OpenAI, Ollama, DeepSeek, ...)
```

The server spawns `openclaude` as a child process, passes the task and configuration as CLI arguments, captures the JSON output, and returns it through the MCP protocol.

## MCP Tools

### `delegate_task`

Delegates a coding task with full tool access (file editing, bash, etc.). Use for:

- Bulk refactors
- Test generation
- Rote edits across many files
- Code summarization

| Parameter | Required | Description |
|---|---|---|
| `task` | Yes | The task description / prompt |
| `working_directory` | No | Working directory for the agent |
| `model` | No | Override the default model |
| `provider` | No | Override the default provider |

### `delegate_research`

Like `delegate_task`, but read-only -- disables `Edit`, `Write`, `Bash`, and `NotebookEdit` tools. Safer and cheaper for exploration:

- "Find where X is defined"
- "Summarize this module"
- "List all usages of Y"

Same parameters as `delegate_task`.

## Prerequisites

- [.NET 10.0 SDK](https://dotnet.microsoft.com/download) (or later)
- [OpenClaude](https://github.com/Gitlawb/openclaude) CLI installed (`npm install -g openclaude` or equivalent)
- An LLM provider accessible from your machine (OpenAI API, Ollama, DeepSeek, etc.)

## Build

For development:

```bash
dotnet build
```

For a production binary — a self-contained, ReadyToRun, single-file executable that needs no
installed .NET runtime — publish using the `win-x64` profile in `Properties/PublishProfiles/`:

```bash
dotnet publish -p:PublishProfile=win-x64
```

The result lands in `bin/Release/net10.0/win-x64/publish/OpenClaudeMcp.exe`. `appsettings.json` is
copied next to it and stays editable without rebuilding. ReadyToRun (not Native AOT) is used on
purpose: the server discovers MCP tools and binds configuration via reflection, which AOT trimming
would break.

## Configuration

Configuration lives in `appsettings.json` under the `"OpenClaude"` section:

```json
{
  "OpenClaude": {
    "ExecutablePath": "",
    "DefaultModel": "",
    "DefaultProvider": "openai",
    "DefaultPermissionMode": "acceptEdits",
    "DefaultWorkingDirectory": "",
    "MaxBudgetUsdPerTask": null,
    "TimeoutSeconds": 600,
    "LogFilePath": ""
  }
}
```

| Property | Default | Description |
|---|---|---|
| `ExecutablePath` | `""` | Full path to the `openclaude` CLI. If empty, auto-discovered via PATH and common npm locations. |
| `DefaultModel` | `""` | Model passed to `--model` (e.g. `deepseek-chat`, `qwen2.5-coder:32b`). Empty = let OpenClaude decide. |
| `DefaultProvider` | `"openai"` | Provider passed to `--provider` (`openai`, `ollama`, `deepseek`, `gemini`, etc.). |
| `DefaultPermissionMode` | `"acceptEdits"` | Controls delegated agent permissions. Options: `default`, `acceptEdits`, `bypassPermissions`, `dontAsk`, `plan`, `auto`. |
| `DefaultWorkingDirectory` | `""` | Fallback working directory when the caller doesn't supply one. |
| `MaxBudgetUsdPerTask` | `null` | Per-task spend cap on paid providers. |
| `TimeoutSeconds` | `600` | Hard timeout per delegated task. |
| `LogFilePath` | `""` | Log file path. Defaults to `openclaude-mcp.log` next to the executable. |

## Usage with MCP Clients

### Claude Code

Add to your MCP settings (`.claude/settings.json` or global):

Point at the published single-file executable (recommended — stable, fast startup, no `dotnet`
process in the chain):

```json
{
  "mcpServers": {
    "openclaude": {
      "command": "C:\\path\\to\\OpenClaudeMcp\\bin\\Release\\net10.0\\win-x64\\publish\\OpenClaudeMcp.exe"
    }
  }
}
```

For development you can run it through the SDK instead:

```json
{
  "mcpServers": {
    "openclaude": {
      "command": "dotnet",
      "args": ["run", "--project", "C:\\path\\to\\OpenClaudeMcp"]
    }
  }
}
```

### VS Code (with MCP extension)

Add to your `.vscode/mcp.json`:

```json
{
  "servers": {
    "openclaude": {
      "command": "dotnet",
      "args": ["run", "--project", "/path/to/OpenClaudeMcp"]
    }
  }
}
```

### Cursor / Windsurf / Other MCP Clients

Use the same `command` + `args` pattern. The server communicates over **stdio**, so it works with any MCP-compatible client that launches child processes.

## Example Delegation

Once connected, your main AI agent can call:

> "Use `delegate_task` to rename the variable `dataManager` to `dataStore` across all `.cs` files in `src/`"

Or for safe, read-only exploration:

> "Use `delegate_research` to find all files that reference `IRepository<T>` and summarize the patterns used"

## Project Structure

```
OpenClaudeMcp/
  Program.cs                    # Entry point, DI setup, MCP server bootstrap
  appsettings.json              # Configuration
  Configuration/
    OpenClaudeOptions.cs        # Strongly-typed config POCO
  Logging/
    FileLog.cs                  # Thread-safe file logger (stdout reserved for MCP)
  Runner/
    ExecutableResolver.cs       # Locates the openclaude CLI on disk
    OpenClaudeRunner.cs         # Spawns openclaude, captures and parses output
  Tools/
    DelegationTools.cs          # MCP tool definitions (delegate_task, delegate_research)
```

## Logging

Because stdout is the MCP transport channel, the default console logger is disabled. All diagnostic output goes to a log file (configurable via `LogFilePath`, defaults to `openclaude-mcp.log` next to the executable).

For why things are built the way they are, per-model quirks, and tuning notes, see
[docs/engineering-notes.md](docs/engineering-notes.md).

## License

[MIT](LICENSE)
