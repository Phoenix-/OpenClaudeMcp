# Engineering notes

A running journal of what we learned tuning this MCP server and *why* it's built the way it is.
The goal is to not re-walk the same rakes when adding models or polishing behavior. Entries record
**observation → conclusion → what we did (or deliberately didn't)**, not session logs — so they stay
valid even after the details fade. If a decision is later reversed, add a new entry rather than
rewriting the old one.

---

## Decisions

### Publish as ReadyToRun, not Native AOT
The server discovers MCP tools (`WithToolsFromAssembly`) and binds config (`ConfigurationBinder.Bind`)
**via reflection**. Native AOT's trimming breaks reflection over types found at runtime (IL2026/IL3050,
or runtime "no metadata for type"), and the ModelContextProtocol package (0.3.0-preview) isn't fully
AOT-annotated. ReadyToRun gives fast startup while keeping reflection intact — almost all the win,
none of the risk. AOT remains an optional future experiment, not a default.

### Publish config lives in a `.pubxml`, not a csproj flag
Publish-only properties (RID, self-contained, single-file, R2R) live in
`Properties/PublishProfiles/win-x64.pubxml`, which only `dotnet publish` reads. An earlier attempt
gated them inside the csproj behind a custom `_IsPublish` flag — that worked but was a magic flag you
had to remember. The `.pubxml` is the idiomatic mechanism: a plain `dotnet build` stays
framework-dependent and fast *by construction* (no `RuntimeIdentifier` in the csproj at all), and
Visual Studio surfaces the profile in its Publish UI. Build with
`dotnet publish -p:PublishProfile=win-x64`.

### No provider-name checks, no `max_budget_usd` in the tool schema
The `provider` value (`openai`, `anthropic`, ...) names the **API dialect**, not the service or its
price — OpenClaude speaks several dialects against any compatible endpoint. So "warn if provider ==
anthropic" was meaningless and was dropped. A budget cap is also pointless here: the only API key
wired into openclaude is the MIMO key on a **zero-balance** account, so billing is physically
impossible — if MIMO ever charges, the agent just stops working, it can't spend money. Guarding an
impossible failure is overengineering. (`MaxBudgetUsdPerTask` still exists in config as a harmless
global knob, but isn't plumbed per-call.)

### `--bare` + a headless system-prompt preamble on every run
Cheap models collapse into meta-mode ("Hi, ready to help!") on imperative prompts instead of
executing (see Model quirks). Two independent levers, both applied:
- `--append-system-prompt` (text in `appsettings.json` → `HeadlessSystemPrompt`, editable without
  rebuild): tells the model it's headless, to execute immediately and not greet. This is the actual
  fix for the meta-mode problem.
- `--bare`: skips CLAUDE.md auto-discovery, auto-memory, hooks, LSP, plugin sync, prefetches. Right
  for a stateless delegate and reduces context noise. OpenClaude's own help says `--bare` is meant to
  pair with explicit `--append-system-prompt`/`--add-dir`, which is exactly how we use it.

### Tool descriptions encode a hard "is it worth delegating?" rule
The `delegate_task`/`delegate_research` descriptions aren't soft "use for X" hints — they state the
break-even rule explicitly: delegate only when the mechanical volume outweighs (briefing + verifying),
and treat the cheap model's output as UNTRUSTED with verification folded into the cost. This matters
because the description is what the *calling* model reads when deciding whether to delegate at all;
mis-applied delegation (single-file edits, "write this verbatim") costs more than doing it directly.

### Empty-JSON-envelope is surfaced as a warning
A run that exits 0 but produces no JSON envelope almost always means the model replied
conversationally instead of doing the work. `FormatResult` now emits an explicit `[warning]` plus the
raw output in that case, so a greeting isn't silently returned as if it were a result.

---

## Model quirks

Behavior is per-model; cheap/open models differ a lot from Claude/Haiku (which Claude Code is tuned
for). Add a row when wiring a new model.

### mimo-v2.5-pro (Xiaomi MiMo) — current default, via openai dialect, free
- **Phrase work as a QUESTION, not an imperative list.** Controlled test (one variable at a time):
  a short question worked (turns=2); the *same content* as "I need you to: 1... 2... 3..." collapsed
  into "ready to help!" with no work done (exit=0, no envelope); the same content as a narrative
  question worked (turns=9). Its instruction-tuning reads command-lists as "let's discuss a plan."
  The `--bare` + preamble fix hardens this, but question-framing is still the safe default.
- **For research, tell it to LIST the directory first and not guess file names.** Verified after the
  fixes shipped: with `--bare` (no CLAUDE.md) and Bash disabled, mimo jumped straight to `Read` on a
  guessed path, failed 3× and gave up (`is_error=true`). The identical question with "list the *.cs
  files with Glob/LS first, do not guess" → clean correct answer (turns=10). It has Glob/Grep/Read in
  research mode; it just doesn't reach for them unprompted.
- **It lost prose dumped after a "the following content:" marker.** Give file content inline in the
  brief, not as "write the verbatim text below."
- **Slow:** ~27–143s for simple research vs seconds for Haiku. Fine for non-urgent background; for
  interactive work Haiku is nicer. One long multi-turn research hit the 600s timeout and was killed —
  long chains on mimo may not fit.
- `delegate_task` (Write/Bash enabled) was more robust than read-only research in practice — the full
  toolset gave it more ways to make progress.

_Observations above come from this repo's bring-up plus heavy real-world use driving mimo through an
~8h KK2 (CryEngine, PS5 Flex memory) investigation in mid-2026._

---

## Open questions / ideas

- **Per-call preamble override.** Currently the headless preamble is global config only (kept out of
  the tool schema on purpose, to not clutter every call). If a task ever needs a different preamble,
  add an optional hidden override then — not before there's a real need.
- **Long-chain reliability on cheap models.** OpenClaude's README warns small models struggle with
  long tool chains; mimo's research timeout is one data point. Worth measuring whether a higher
  per-task turn budget or task-splitting helps, or whether it's just a hard ceiling.
- **Native AOT revisit.** If ModelContextProtocol ships a source-generator for tool discovery (so
  reflection isn't needed), AOT becomes viable — smaller, faster-starting binary. Recheck on package
  updates.
- **Multi-model routing.** When more models are wired, consider picking the model per task type
  (e.g. a stronger model for research that needs reasoning, the cheapest for bulk mechanical edits)
  rather than one global default.
