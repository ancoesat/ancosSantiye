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

## Problems

| Problem | Cause | Fix |
|---|---|---|
| The dash gate passed a file that had an em dash in it (found by a mutation check) | `grep` exits 2, not 0, when one of its paths is missing, even if it also found a match. `docs` and `profiles` did not exist yet | `check.sh` now passes only paths that exist |
| `dotnet run` listened on `localhost:5087`, not `127.0.0.1:5170` | The template's `launchSettings.json` sets the URL, so the code default was skipped | Deleted `launchSettings.json` |

## Lessons

- A gate is only trusted after a mutation check: break it on purpose, see it fail.
