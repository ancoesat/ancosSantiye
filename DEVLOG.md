# DEVLOG

## 2026-11-24 F0: skeleton and two checks

- Solution (`AncosSantiye.slnx`), web project packed as the `ancos-santiye` dotnet tool, xUnit test project. `/health` on 127.0.0.1:5170.
- 23 character sheets and 4 tile sheets copied into `skins/grafxkid/` with the pack licenses (all CC0 1.0).
- `scripts/check.sh`: dash grep, `dotnet build`, `dotnet test`. Green on .NET SDK 10.0.404.

### Check 1: where token numbers come from

Planned source was the transcript JSONL. The hooks docs do not document that format, and say the transcript is written asynchronously and can lag. The status line docs do document a full JSON on stdin: `cost.total_cost_usd`, `context_window` (input and output tokens, used percentage, current usage with cache fields) and, for Pro and Max subscribers, `rate_limits.five_hour` and `rate_limits.seven_day` with `used_percentage` and `resets_at`. Updates are event driven and debounced at 300 ms, with an optional `refreshInterval`.

Decision: the budget panel reads a status line feed. An `ancos-santiye statusline` subcommand reads that JSON, forwards it to the local server with a short timeout, and prints the status line (with the localhost link). JSONL parsing is dropped from v1. This also corrects R1: the subscription limit percentage is available.

### Check 2: HTTP hook while the server is down

From the hooks docs: a connection failure and a non-2xx status are non-blocking errors, the tool call goes ahead. A timeout cancels the hook and does not block `PreToolUse`. The default HTTP hook timeout is 600 s and HTTP hooks have no `async` option, so a slow server would stall Claude Code.

Decision: hook entries set `"timeout": 2`. The endpoint only enqueues and returns `204 No Content` at once (an empty 2xx body is a success, plain text is treated as an error). Still to test on Esat's machine: whether a refused connection prints visible noise in the Claude Code UI on every tool call.

## 2026-11-24 F1: core

- `Events/`: `SiteEvent` (UUIDv7 ids from `Guid.CreateVersion7`, data values cut at 200 chars), event kinds, roles, sources, one JSON shape (camelCase, nulls left out).
- `Store/EventLog`: one NDJSON file per UTC day under `~/.ancos-santiye/events`. Unreadable lines are skipped and counted.
- `Scene/`: `SiteState` (sorted immutable maps, so JSON output is stable) and `SiteReducer`, a pure fold. Unknown kinds and events missing their required data leave the state untouched.
- `Store/SiteHub`: log first, then reducer, then live subscribers. Snapshot and subscription are taken under one lock. A slow subscriber drops its oldest events.
- `SiteApp`: `/health`, `/state`, `/events` (SSE via `TypedResults.ServerSentEvents`: one `snapshot`, then `event` items). Today's log is replayed at start.
- 20 tests, including an end to end test that starts the real server on a free loopback port and reads the SSE stream. No test host package.
- Mutation checks, all caught: the reducer reading the clock (2 tests failed), a public default URL (1), no data trimming (1).
- `check.sh` falls back to `dotnet.exe` so it also runs from WSL bash.

## 2026-11-24 F2: Claude Code adapter

- `POST /hooks/claude`: maps 10 hook events to site events. Reads only names and ids (`hook_event_name`, `session_id`, `cwd`, `tool_name`, `agent_id`, `agent_type`), never `tool_input`, prompts or messages. Answers 204 at once.
- `POST /feeds/statusline` and `ancos-santiye statusline`: the status line JSON becomes a `usage` event (context, tokens, cost, 5 hour and 7 day limits), only when a value changed. The command forwards with a 500 ms timeout and always prints a line.
- `ancos-santiye hooks print | install | uninstall [--settings <path>]`: merges our HTTP hooks (timeout 2 s) and the status line into `~/.claude/settings.json`.
- Guards: write endpoints refuse an `Origin` header (403) and non-JSON bodies (415). Host filtering (`127.0.0.1`, `localhost`) is set in code against DNS rebinding. Content root is the app folder, so an installed tool behaves the same from any directory. Every string in an event is capped at 200 chars.
- Reviewer (Opus), round 1: DÜZELT with 8 findings. The serious one: `hooks install` with a mistyped `--settings` fell back to the real settings file. Others: wrong-shaped `hooks` silently replaced, crashes on odd entries, host filter depending on a config file, comments dropped on write, symlinked settings replaced, non-ASCII escaped, `--help` starting the server. All fixed with tests. Round 2: ONAY. Two of its minor notes also fixed (dangling symlink, duplicate keys).
- 86 tests. Mutation checks, all caught: no backup, Origin allowed, tool_input leaking, no status line dedupe, duplicate install, no shape check, comments skipped, default encoder, symlink replaced, host filter open.
- Smoke test with the real server: hook 204, foreign Origin 403, status line printed, `/state` shows the chief working, no tool input in the log.

## Problems

| Problem | Cause | Fix |
|---|---|---|
| The dash gate passed a file that had an em dash in it (found by a mutation check) | `grep` exits 2, not 0, when one of its paths is missing, even if it also found a match. `docs` and `profiles` did not exist yet | `check.sh` now passes only paths that exist |
| `dotnet run` listened on `localhost:5087`, not `127.0.0.1:5170` | The template's `launchSettings.json` sets the URL, so the code default was skipped | Deleted `launchSettings.json` |

## Lessons

- A command that writes a user's config accepts exact argument shapes only. A typo must never fall back to the real file.
- A gate is only trusted after a mutation check: break it on purpose, see it fail.
