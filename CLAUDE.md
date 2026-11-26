# ancosSantiye

A local ASP.NET Core app that shows AI coding agents as workers on a pixel art construction site. Each project is a building. Planning rounds and build phases raise it floor by floor. It only reads events and files: it never calls a model, so watching costs zero tokens.

Planning docs (R1, R2, R3): https://claude.ai/artifact/78unfekyyMxxhwxRZgeu1o

## Orkestra profili
- Sahip: kişisel
- Müşteri verisi: yok
- Metin dili: en
- Kontrol: bash scripts/check.sh (ancosArc)
- Asla devredilmez (ek): commit ve push uçları, Claude Code ayar dosyasına yazan kod
- Yayın: GitHub Releases ve dotnet tool paketi
- Branch: yok

## Where things are

| Path | What |
|---|---|
| `src/AncosSantiye/Program.cs` | Entry: no args starts the server, otherwise `Cli` |
| `src/AncosSantiye/SiteApp.cs` | Builds the web app and its endpoints |
| `src/AncosSantiye/Cli.cs` | `statusline`, `hooks print / install / uninstall` |
| `src/AncosSantiye/Events/` | The one event shape every adapter emits |
| `src/AncosSantiye/Store/` | NDJSON log and in-process event bus |
| `src/AncosSantiye/Scene/` | `SiteState` and the reducer (pure function, no I/O) |
| `src/AncosSantiye/Adapters/` | Claude Code hooks and status line feed, folder lane, status file parser |
| `src/AncosSantiye/Actions/` | Commit and push, only when `actions.enabled` is true |
| `src/AncosSantiye/Profiles/` | Profile model: skin, folder lane, actions switch (`--profile <file>`) |
| `src/AncosSantiye/wwwroot/` | No-build front end: plain ES modules, Canvas 2D, EventSource |
| `src/AncosSantiye/skins/grafxkid/` | CC0 sprites, their licenses, `skin.json` |
| `tests/AncosSantiye.Tests/` | xUnit tests and fixtures |
| `profiles/` | Example profile and example status file |
| `scripts/check.sh` | The gate: dash grep, build, test |

## Look at it

- `dotnet run --project src/AncosSantiye`, then open http://127.0.0.1:5170 (live) or http://127.0.0.1:5170/?demo (fake site).
- With a folder lane: `dotnet run --project src/AncosSantiye -- --profile profiles/example-orchestra.json` (edit `directory` first).

## Commands

```bash
bash scripts/check.sh                      # the gate, must be green
dotnet run --project src/AncosSantiye      # http://127.0.0.1:5170
dotnet pack src/AncosSantiye -c Release -o artifacts  # the dotnet tool package
dotnet tool install -g AncosSantiye --add-source artifacts  # try the package (uninstall: dotnet tool uninstall -g AncosSantiye)
```

## Rules

1. Loopback only. The server binds to 127.0.0.1. Nothing listens on a public interface.
2. No NuGet packages in `src/`. Framework and standard library only. The test project may use xUnit.
3. No build step for the front end. No npm, no bundler.
4. Events never carry code, diffs, file contents or secrets. Only names, ids, counts, statuses. The speech bubble (`ClaudeHookMapper.Say`) may read exactly these parts of `tool_input`: the last segment of a file path, the agent's own `description`, a subagent or skill name, a URL's host. Task hooks may carry `task_id` and `task_subject` (the agent's own short title), never `task_description`. The desktop plan tool (`mcp__ccd_turn__update_plan`) may give each step's `content` and `status`, and `done`, `start`, `advance`; never a step's `session`. Never a command, pattern, query, prompt or full path; every one of those has a test.
5. The reducer in `Scene/` is pure: same events in, same state out. All I/O stays in adapters and the store.
6. Actions (commit, push) exist only behind `actions.enabled`. When it is false the endpoints are not mapped at all. The page sends a repo index, never a path; every file must be in the server's own `git status`; push is a plain `git push` to `@{push}`, never with arguments. Each request needs the server's own Origin, JSON and the per-start token.
   Known limit: a local program can read the token from `/config` like the page does. The agent deny list (`siyametrik-skills/claude-code/settings-ornek.json`) blocks `/actions/` and `/config` on localhost; keep actions off when you do not need them.
7. Sprites come from packs with a license that allows redistribution (CC0 today). Never draw pixel art by hand. Simple shapes in code (rectangles, lines) are fine.
8. No em or en dashes anywhere: code, comments, docs, UI text.
9. Git: no commits, pushes or branches from any agent, and no calls to `/actions/*` either. Esat commits and pushes, by terminal or from the screen.

## Lessons

- When two lanes work in this tree at once, commit with the piece's own paths (`git add <paths>`), never `git add -A`. A lane's "done" is checked with `git status`, not with its report.
- Mutation-check every gate and test that guards a rule: break it on purpose, it must fail. (F0: the dash grep passed a dash because a missing path made grep exit 2.)
- A command that writes a user's config accepts exact argument shapes only. A typo must never fall back to the real file. (F2 review.)
- Settings files are refused, not repaired, when the parts we touch have an unexpected shape, comments or duplicate keys. Always back up before writing.
- Do not add `Properties/launchSettings.json`. It overrides the loopback default URL.
- A test with fixed event times must use a fixed clock (`FixedClock`) for anything served through `SiteHub`. With the real clock it passes today and fails after 15 minutes of event time. (v0.2 #6.)
- Before packing, delete `src/AncosSantiye/obj/Release`: a stale obj kept precompressed `.br`/`.gz` files in the package after compression was turned off. (F8.)
- An Origin check against `request.Host` is only as good as the host filter in front of it. Test the filter with a foreign Host and a matching Origin. (F7 review.)
- SessionStart runs only `command` (and `mcp_tool`) hooks; an `http` entry there never fires. Check each hook event's allowed types in the Claude Code docs before installing it. (v0.2.1, found by a read-only diagnosis run in Claude Code.)
- `@{u}` is not always where `git push` goes; `@{push}` is. If git cannot name it, do not push. (F7 review.)
- A time limit on a child process must cover reading its pipes too, not only waiting for exit. (F7 review.)

## Open items

- Found in the first real use (2026-11-26):
  - Budget only fills from an interactive terminal `claude` session. The desktop app's Code tab and `claude -p` never run the status line, and hooks carry no usage data. Since v0.2 #5 the panel says so instead of showing empty rows.
  - Fixed in v0.2 #6: silent workers leave (15 min, 60 when waiting for you); folder lane events of a project the profile no longer watches stay out of the startup replay.

- Delivery ids (`project-NNN`) and status file ids are separate id spaces; a delivery shows as its own piece.
- A `Kestrel:Endpoints` setting (appsettings or `Kestrel__Endpoints__*` env) overrides `UseUrls` and skips the loopback check. Close it by checking the real bound addresses after start.
- A refused commit of a partly staged file (`MM`) leaves its whole working copy staged, and the message does not say so.
- Hooks and the status line always target port 5170, even when `--urls` changes the server port.
- Request bodies are not size capped beyond Kestrel's default (loopback only, so low risk).
- On Esat's Windows machine `bash` resolves to WSL, where .NET is not installed. `check.sh` now falls back to `dotnet.exe`; confirm `bash scripts/check.sh` works from PowerShell. If not, point `bash` at Git Bash.
- Test on Esat's machine: does a `type: "http"` hook print visible error noise in Claude Code while the server is down? See DEVLOG F0.
