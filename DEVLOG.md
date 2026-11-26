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

## 2026-11-25 F7: commit and push from the screen (high risk, Opus review)

- `Actions/Git.cs`: git through `Process` with an argument list, never a shell; literal pathspecs, no prompts, a time limit that also covers the pipes. `Actions/GitActions.cs`: status (`-z --no-renames`), commit, push; one action at a time (busy is 409). `Actions/ActionGuard.cs`: own Origin, JSON, a random token made at every start, compared in constant time.
- Commit: every path must be in `git status` right then; `git add -- paths`, then `git commit -F <temp file> -- paths`, so other staged changes stay out and the message stays exactly as written. A refused commit (for example a hook) unstages what was unstaged before. Push: a plain `git push`; the target is `@{push}`; if git cannot name it, nothing is pushed; nothing ahead means "Nothing to push" and no event.
- Server: `/config` (token only when actions are on), `/actions/status|commit|push` only mapped when on, repos checked at start, every URL must be loopback. Profile: `actions.repos`; relative folders in a profile now start at the profile file's folder.
- Front: `actions.js`, a Commit panel above the others: tick files, write the message, the browser's own confirm lists files and message; push has its own confirm. No "all" option exists.
- Claude checked: `check.sh` green (197 tests). 22 guards mutation-checked, all caught (two only after new tests, see Problems). Headless Chrome with a real repo and a bare remote: only the picked file committed, quotes and a second line kept, push reached the remote, a page on another origin with the right token got "Failed to fetch". Screenshot: `docs/f7-commit.png`.
- Opus denetçi: round 1 DÜZELT (8 findings: non-loopback `--urls`, no foreign Host test, pipes outside the time limit, temp file before the try, `@{u}` instead of `@{push}` and "up to date" counted as pushed, refused commit left files staged, confirm showed one line, check log without the check.sh run). All fixed with tests and mutations. Round 2 ONAY. Its leftovers: `[::1]` passed the URL check but not the host filter (fixed: only 127.0.0.1 and localhost); `Kestrel:Endpoints` bypass and the `MM` case went to Open items.
- siyametrik-skills: deny list now blocks `/actions/` and `/config` on localhost; its README explains why and that it narrows, not closes, the path.

## 2026-11-25 F8: package and README

- `AncosSantiye.csproj`: `IsPackable` (the Web SDK turns packing off), package metadata, `README.md` in the package. The static web assets pipeline and precompression are off: the page is served by `UseStaticFiles` from the app folder, so they only added an extra copy and `.br`/`.gz` files. Skins, `wwwroot` and `appsettings.json` are marked `Pack="false"` so they live only once, under `tools/`. Package: 61 files, 250 KB (was 183 files).
- `README.md`: what it is, install, quick start, profiles, folder lane, custom skins, commit and push with the known limit, security and privacy, how it is built, known gaps. Hero image `docs/hero.png` from `?demo=build` of the installed tool.
- Claude checked: `check.sh` green (197 tests). `dotnet pack -c Release`, then `dotnet tool install` into a temp tool path and started from an unrelated folder: `--help`, `hooks print`, the page, `js/main.js`, `js/actions.js`, `/skin/skin.json`, a sprite, `/config`, `/state` all 200; `statusline` printed `santiye http://127.0.0.1:5170 | ctx 12%`; demo rendered with no console errors.
- Release is Esat's: tag, GitHub Release with the `.nupkg`. nuget.org is optional and needs his API key.

## 2026-11-26 v0.2 #1: workers talk

- Why v0.2: in the first real use (rovans in the desktop app, KBYTE) the site showed activity but said nothing. Workers walked in circles, the building only grew from the orchestra status file, the right panel mixed projects and Budget had no data outside a terminal session. Esat's verdict: manual, not informative, misleading. The direction he approved: a site that tells the truth, in this order: talking workers, one project at a time, progress from the session's task list, animation that matches the action, honest Budget, cleanup.
- `ClaudeHookMapper.Say`: one line per tool start or failure ("Reading SPEC.md", "Run the unit tests", "Calling Explore", "Using github"), at most 60 characters. Reads only the file name (never the folders), the agent's own description, a subagent or skill name, a URL host. Rule 4 in CLAUDE.md now says exactly this.
- Reducer: `ActorState.Say`. Start says "Getting started", a stop says "Waiting for you", a failure says "Failed: ...", and the line stays between two tools so it does not flicker.
- Front: a speech bubble above each name tag (white, yellow when waiting for you, red on failure). Bubbles of workers standing close stack in rows instead of covering each other.
- Claude checked: `check.sh` green (222 tests). 8 mutations caught (full path, command, first line only, length cap, say only on start and fail, URL host only, line kept between tools, waiting line). Live test with real hook payloads (a Bash command with a secret, a Grep pattern): the state carries only the bubble lines. Headless Chrome: live and demo, no console errors.

## 2026-11-26 v0.2 #4: one project at a time, one worker per session

- `SiteHub` keeps one `SiteState` per project (names match without case, so the folder lane's `kbyte` and the session folder `KBYTE` are one project). `/projects` lists them, latest first, with worker counts and waiting gates; `/state?project=` and `/events?project=` give one project, and without it the latest one.
- Every Claude Code session is its own worker (`chief:<session>`). Before this, every main session shared the id `chief`, so two windows looked like one worker.
- Page: a project picker in the header. Default is "follow latest": the page switches to whichever project acted last and clears the scene at once (people of another project do not walk off, they were never there). Picking a project pins it in the address. Alerts come from the gates of every project; a gate elsewhere is labeled with its project name.
- Claude checked: `check.sh` green (231 tests). 6 mutations caught (case-blind names, latest is default, a late older event does not push a project back, subscribe to one project, `/state?project`, one worker per session); the late-event one survived first and got its own test. Headless Chrome with hooks from three sessions in two projects: follow mode switched to KBYTE when it acted and showed only its worker; pinned to rovans it kept rovans' two workers while KBYTE acted. No console errors.

## 2026-11-26 v0.2 #3: progress from the session's task list

- Claude Code's `TaskCreated` and `TaskCompleted` hooks carry `task_id` and `task_subject`. The mapper passes both (the title cut at 60 characters), never `task_description`.
- Reducer: tasks are keyed by session and id, so a repeated hook never counts twice and a repeated create never reopens a done task. The task list on screen is the one of the session that touched its tasks last; an older finished list stays in the state but does not count. `TasksPlanned`, `TasksDone`, `CurrentTask` (the first open one) and `CurrentTasks` are computed from it.
- Page: the header says "2 of 4 tasks done / now: Build match screen". Without an orchestra stage, open tasks mean "building" and a fully done list means a finished building; no tasks means no building. When a project has no pieces, the side panel shows "Tasks" with each task, done, now or open.
- Claude checked: `check.sh` green (238 tests). 6 mutations caught; two survived first (a repeated create after completion, creation order) and got stronger tests. Headless Chrome with four real task hooks and a secret in `task_description`: header, building and task panel right, the secret nowhere in the state, no console errors.

## 2026-11-26 v0.2 #2: the place and the animation match the action

- Why: the main session's worker played its "work" sheet all the time, and for some characters that sheet is a walk or a carry, so it looked like walking in circles. Nothing on screen told reading from editing.
- Reducer: `ActorState.Action` from the tool (`Doing.Of`): read (Read, Grep, Glob, web), build (Write, Edit), run (Bash, PowerShell), plan (task list, subagents, skills, MCP), wait (a stopped turn, or a question to you), fail, start. Between two tools the action stays, so nobody walks back and forth after every tool.
- Page (`cast.js`): one station per action. Read at the board by the gate, build on the roof (at the walls before there are floors), run at the crane controls, plan at the site office; wait, start and fail at the worker's own spot. A second person at a station stands next to the first. On arrival a worker faces its work. Only a builder at the building plays "work"; everyone else stands still, so a walk always means the agent changed what it does. The supplier stays by its truck.
- Claude checked: `check.sh` green (250 tests). 6 mutations caught (read, build and run tools, place kept between tools, wait after the turn, fail is its own place). Headless Chrome, one session through Read, Edit, Bash and Stop: the worker went to the board, the roof, the crane and its own spot. No console errors.

## Problems

| Problem | Cause | Fix |
|---|---|---|
| The dash gate passed a file that had an em dash in it (found by a mutation check) | `grep` exits 2, not 0, when one of its paths is missing, even if it also found a match. `docs` and `profiles` did not exist yet | `check.sh` now passes only paths that exist |
| F3 files landed in the F2 commit without review | The coder lane finished while F2 was being committed with `git add -A` in the same tree | Reviewed afterwards (ONAY). Commits now name the piece's files |
| `NETSDK1022` duplicate Content item for `skin.json` | The Web SDK already includes `**/*.json` as content | `Content Remove="skins/**"` before the include |
| `dotnet run --project src/AncosSantiye -- --profile ../x.json` said "Profile not found" | The Web SDK runs the app from the project folder, so relative paths started from `src/AncosSantiye`, not from where the command was typed | `RunWorkingDirectory` set to `$(MSBuildStartupDirectory)`, same as the installed tool; the error now prints the full path it looked at |
| Removing `GIT_LITERAL_PATHSPECS` passed all tests | git reads a name literally when that file exists, so the bracket test could not see it | New test: commit a deleted `[ab].txt`; without the flag git also staged `a.txt` |
| `@{push}` replaced by `@{u}` passed | In the test both pointed at the same branch | New test with `remote.pushDefault`: under `push.default=simple` git cannot name `@{push}`, so the screen refuses; with `current` it pushes to `backup/main` |
| My F6 DEVLOG edit dropped the `## Problems` heading | A text replace swapped the heading out and did not put it back | Restored in F7 |
| A second `ancos-santiye` printed a long Kestrel stack trace (found by Esat while trying the package) | The port was held by an earlier `dotnet run`; Kestrel only fails at bind time | Start checks every port first and stops with one line that says to open the running one |
| `dotnet pack` made no package | The Web SDK sets `IsPackable` to false | `<IsPackable>true</IsPackable>` |
| Precompressed files still in the package after `CompressionEnabled` was set to false | A stale `obj/Release` from the earlier pack | Delete `obj/Release` before packing (in Lessons) |
| A mutation of the first-tick skip passed | The test read the state version after the first tick, so it could not see events published during that tick | The test asserts version 0 after the first tick |
| `dotnet run` listened on `localhost:5087`, not `127.0.0.1:5170` | The template's `launchSettings.json` sets the URL, so the code default was skipped | Deleted `launchSettings.json` |

## Lessons

- A command that writes a user's config accepts exact argument shapes only. A typo must never fall back to the real file.
- A gate is only trusted after a mutation check: break it on purpose, see it fail.
