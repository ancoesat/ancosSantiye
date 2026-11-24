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
| `src/AncosSantiye/Program.cs` | Startup and endpoints |
| `src/AncosSantiye/Events/` | The one event shape every adapter emits |
| `src/AncosSantiye/Store/` | NDJSON log and in-process event bus |
| `src/AncosSantiye/Scene/` | `SiteState` and the reducer (pure function, no I/O) |
| `src/AncosSantiye/Adapters/` | Claude Code hooks and status line feed, folder lane, status file parser |
| `src/AncosSantiye/Actions/` | Commit and push, only when `actions.enabled` is true |
| `src/AncosSantiye/Profiles/` | Profile model and the default profile |
| `src/AncosSantiye/wwwroot/` | No-build front end: plain ES modules, Canvas 2D, EventSource |
| `src/AncosSantiye/skins/grafxkid/` | CC0 sprites, their licenses, `skin.json` |
| `tests/AncosSantiye.Tests/` | xUnit tests and fixtures |
| `profiles/` | Example orchestra profile |
| `scripts/check.sh` | The gate: dash grep, build, test |

## Commands

```bash
bash scripts/check.sh                      # the gate, must be green
dotnet run --project src/AncosSantiye      # http://127.0.0.1:5170
dotnet pack src/AncosSantiye -o artifacts  # the dotnet tool package
```

## Rules

1. Loopback only. The server binds to 127.0.0.1. Nothing listens on a public interface.
2. No NuGet packages in `src/`. Framework and standard library only. The test project may use xUnit.
3. No build step for the front end. No npm, no bundler.
4. Events never carry code, diffs, file contents or secrets. Only names, ids, counts, statuses.
5. The reducer in `Scene/` is pure: same events in, same state out. All I/O stays in adapters and the store.
6. Actions (commit, push) exist only behind `actions.enabled`. When it is false the endpoints are not mapped at all.
7. Sprites come from packs with a license that allows redistribution (CC0 today). Never draw pixel art by hand. Simple shapes in code (rectangles, lines) are fine.
8. No em or en dashes anywhere: code, comments, docs, UI text.
9. Git: no commits, pushes or branches from any agent. Esat commits and pushes.

## Lessons

- Mutation-check every gate and test that guards a rule: break it on purpose, it must fail. (F0: the dash grep passed a dash because a missing path made grep exit 2.)
- Do not add `Properties/launchSettings.json`. It overrides the loopback default URL.

## Open items

- On Esat's Windows machine `bash` resolves to WSL, where .NET is not installed. `check.sh` now falls back to `dotnet.exe`; confirm `bash scripts/check.sh` works from PowerShell. If not, point `bash` at Git Bash.
- Test on Esat's machine: does a `type: "http"` hook print visible error noise in Claude Code while the server is down? See DEVLOG F0.
