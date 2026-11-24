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

## 2026-11-24 F3: status file and delivery names (DeepSeek)

- First piece from the coder lane (DeepSeek V4.1 Flash Free in OpenCode). `Adapters/StatusFile.cs` parses the status file (stage line, table read by column names) and diffs two snapshots into events. `Adapters/DeliveryFile.cs` reads `<project>-<NNN>-<gemini|studio>.md` names.
- No fix rounds. Reviewer (Sonnet): ONAY. Its three missing tests (header case, pending status case and spaces, CRLF and BOM) were added by Claude. 126 tests.
- Mutation checks on DeepSeek's own tests: repeated gate.pending, removed pending row not cleared, columns read by position (7 tests failed). All caught.
- Not wired yet. When the folder watcher is added: skip a read that returns no rows when the last one had rows (a half-written file must not look like "all gates cleared"), and decide how delivery piece ids map to status file ids.

## 2026-11-24 F4a: scene skeleton

- No-build front end in `wwwroot/`: plain ES modules (`skin.js`, `sprites.js`, `cast.js`, `scene.js`, `stream.js`, `demo.js`, `main.js`), Canvas 2D at 320x184, whole-number scale, smoothing off. Name tags are HTML over the canvas so text stays sharp.
- `skins/grafxkid/skin.json`: tiles by sheet coordinates, 8 characters with idle, walk and work animations, role to character lists. Frame counts come from image widths. A character is picked per actor from a hash of its name, so Explore and Plan look different and stay the same.
- The server serves the page and the skins from the app folder. After every live event the page reads `/state` again (local, cheap, never out of step with the reducer).
- `?demo` shows a fake site without Claude Code.
- Tests: every sheet exists, holds whole frames, every tile fits its sheet, every role points at real characters; the page, a module and the skin are served. 132 tests. Mutation checks on the skin tests (bad frame width, missing sheet, tile outside its sheet, role to unknown character): all caught.
- Checked in headless Chrome: demo shows all six roles walking in and animating; live hook events bring in the chief, an Explore worker and the reviewer, and `SubagentStop` walks the worker off. Screenshot: `docs/f4a-demo.png`.

## 2026-11-25 F4b: building and site props (DeepSeek)

- `layout.js` (pure: state to building model), `building.js` (slab, columns, floors, five wall looks, roof and sign), `props.js` (crane with a bobbing crate over the active slot, scaffolding, truck, site office, fence, gate board). `scene.js`, `demo.js` and `main.js` wired up. `?demo=empty|R1|R2|R3|build|done`.
- No fix rounds, reviewer not required for draft / low. Claude checked: `check.sh` green (132 tests), no console errors in any demo phase in headless Chrome, and 13 node checks of `layout.js` against the brief (phases, looks, id order, crane target, 4 floor cap, tasks never negative, flags). Screenshot of all six phases: `docs/f4b-asamalar.png`.
- DeepSeek changed three lines of `main.js` instead of one; the brief itself needed that (5e), it said so in its report.
- Seen in the screenshots: characters stood in front of the building and hid it, and the two gate papers overlapped. Fixed in F4c.

## 2026-11-25 F4c: who stands where

- `cast.js`: everyone waits on the ground outside the building (chief by the site office, reviewer right of the scaffold, architect left of the building, supplier by the truck, idle builders in a pool). Builders who are working climb the scaffold ladder and stand on top of the building, spread across it. With no floors yet, they stay on the ground. Changing level always goes by the ladder; leaving from the roof climbs down first. Sprites are drawn lowest last.
- Name tags follow the character's height.
- `props.js`: gate papers side by side instead of stacked.
- Checked in headless Chrome: the building is no longer covered in any demo phase; a live worker with no floors stays at ground level. Screenshot: `docs/f4c-yerlesim.png`. 132 tests.

## 2026-11-25 F5: side panels and notifications (DeepSeek)

- `panels.js`: Waiting for you (gates, how long they wait), Pieces (stage, tasks, a colored dot per piece from the exported `lookOf` in `layout.js`), Budget (context, 5 hour and 7 day bars with reset times, cost, tokens, model), Activity (last 20 events). Everything is built with `createElement` and `textContent`.
- `notify.js`: one beep (WebAudio) and one browser notification per new gate; the first state never alerts; sound setting kept in `localStorage`; every browser API wrapped in try/catch.
- Claude checked: `check.sh` green (132 tests). No `innerHTML`, `insertAdjacentHTML`, `outerHTML`, `document.write` or `eval` in any front end file. In headless Chrome with the audio and notification APIs counted: opening `?demo=build` gave 0 alerts, the late demo gate gave exactly 1 beep and 1 notification, a reload gave 0. An agent named `<img src=x onerror=...>` sent through the hook endpoint showed as plain text and ran nothing. Empty texts present in `?demo=empty`. No console errors. Screenshot: `docs/f5-paneller.png`.

## 2026-11-25 F6: profile, skin, live folder lane

- `Profiles/Profile.cs`: `skin`, `skinPath`, `folderLane` (directory, statusFile, project, intervalSeconds), `actions.enabled`. Loaded with `--profile <file>`; no profile means built-in skin, Claude Code only, read only. A missing or broken profile, or a skin folder without `skin.json`, stops the server with one clear line (exit 1), never a silent default. Comments and trailing commas allowed.
- The active skin is always served at `/skin`, built in or from the profile's own folder. `main.js` loads from there.
- `Adapters/FolderLane.cs`: a hosted service that polls once a second (no file watcher: they miss changes on WSL mounts and network drives). Status file: read only when its write time changes; a read with 0 rows after one that had rows is a half-written file, so the old view stays and the time is not stored, which makes the next tick read it again. Deliveries: `*.md` files named for this project; files already there at start are old news, only new ones arrive.
- Claude checked: `check.sh` green (146 tests). Mutation checks caught: half-written guard, first-tick skip (after fixing the test, which read the version too late), project match, interval check, skin check. The write-time skip survives mutation by design: re-reading an unchanged file gives no events, so it only saves work. End to end with a copy of the real `ancosSantiye-durum.md`: stage F5 and the f5 gate on load; editing the file to F6 and dropping `ancosSantiye-007-gemini.md` moved the gate to f6 and showed the delivery within the poll, no console errors. Screenshot: `docs/f6-canli.png`.
- Delivery ids (`ancosSantiye-007`) and status file ids (`santiye-f5`) are still separate id spaces, so a delivery shows as its own piece. Fine for now; joining them needs a naming rule, not code.

| Problem | Cause | Fix |
|---|---|---|
| The dash gate passed a file that had an em dash in it (found by a mutation check) | `grep` exits 2, not 0, when one of its paths is missing, even if it also found a match. `docs` and `profiles` did not exist yet | `check.sh` now passes only paths that exist |
| F3 files landed in the F2 commit without review | The coder lane finished while F2 was being committed with `git add -A` in the same tree | Reviewed afterwards (ONAY). Commits now name the piece's files |
| `NETSDK1022` duplicate Content item for `skin.json` | The Web SDK already includes `**/*.json` as content | `Content Remove="skins/**"` before the include |
| `dotnet run --project src/AncosSantiye -- --profile ../x.json` said "Profile not found" | The Web SDK runs the app from the project folder, so relative paths started from `src/AncosSantiye`, not from where the command was typed | `RunWorkingDirectory` set to `$(MSBuildStartupDirectory)`, same as the installed tool; the error now prints the full path it looked at |
| A mutation of the first-tick skip passed | The test read the state version after the first tick, so it could not see events published during that tick | The test asserts version 0 after the first tick |
| `dotnet run` listened on `localhost:5087`, not `127.0.0.1:5170` | The template's `launchSettings.json` sets the URL, so the code default was skipped | Deleted `launchSettings.json` |

## Lessons

- A command that writes a user's config accepts exact argument shapes only. A typo must never fall back to the real file.
- A gate is only trusted after a mutation check: break it on purpose, see it fail.
