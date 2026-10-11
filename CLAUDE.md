# Expedition Helper — working notes for Claude

BepInEx 6 (IL2CPP) mod for Two Point Museum. Read README.md for the architecture, the Denuvo
constraints and the hooking/UI rules; this file is about *how to work* on it.

## Build, deploy, reload

- `src/ExpeditionHelper.Host` — permanent plugin. Changing it needs the **game closed** (the DLL is
  locked while it runs; the copy step fails with MSB3021). Ask the user to quit first.
- `src/ExpeditionHelper.Logic` — hot-reloadable. `dotnet build -c Debug` in that folder deploys
  to `BepInEx/plugins/ExpeditionHelper/reload/`, and the host reloads it automatically on file
  change (`tools/tpm reload` forces one). A Host copy warning during a Logic build is expected
  while the game runs.
- Game path defaults to `F:\SteamLibrary\steamapps\common\Two Point Museum` (`Directory.Build.props`,
  `TPM_GAME_DIR` for `tools/tpm`).
- Release builds contain no dev bridge (`Dev/**` is excluded, `DEV_BRIDGE` undefined). Don't
  package from the game folder; build Release to a separate output.

## Dev bridge (`tools/tpm`, Debug builds only)

Local HTTP server in the running game (127.0.0.1, port + token in
`BepInEx/config/ExpeditionHelper.devtoken`, new each launch). Use it instead of guessing:

```bash
tools/tpm ping
tools/tpm log 50                       # tail of BepInEx/LogOutput.log
tools/tpm screenshot                   # returns a PNG path; Read it to see the game
tools/tpm reload                       # reload the Logic DLL
tools/tpm eval 'UnityEngine.Time.timeScale'
tools/tpm eval -f tools/snippets/expeditions.csx
```

`eval` runs C# (Roslyn script) **on the game's main thread**; the last expression is returned as
JSON. Globals: `Host` (HostApi: `Log`, `Config`, `Exports`), `Logic` (the loaded Logic assembly),
`Print(obj)`, `Dump(obj, depth)`. Logic exposes helpers through `Host.Exports`, e.g.
`((Func<MethodBase,string>)Host.Exports["NativeCodeCheck.WhyUnsafe"])(method)` — check every new
hook target with it first — plus `Repeat.DryRun`, `Repeat.StartNow`. Reusable probes live in
`tools/snippets/*.csx`; keep read-only ones, delete throwaway prototypes.

Pipe JSON results through `python -c "import json,sys;print(json.load(sys.stdin)['result'])"` for
long text. To follow events, run a Monitor on `tail -F .../BepInEx/LogOutput.log` filtered to
`[repeat]|[auto-claim]|[settings]|[setup]|Error|Exception` (exclude `HarmonyX`/`TypeLoadException`
noise that every reload prints).

## Live testing with the user

- The user plays; you inspect. Ask them to open the screen you need, click, or report what
  they see — don't drive the game's UI yourself. Crashes are acceptable when they teach us
  something, but say what you're about to try.
- Prototype UI changes with `eval` first (clone, position, screenshot), then write the real code.
- Snapshot saves before risky experiments (saves: `%USERPROFILE%\AppData\LocalLow\Two Point Studios\Two Point Museum\Cloud`;
  backups go in `backups/`, which is gitignored).

## Gotchas that cost time before

- Il2Cpp `IEnumerable` from the game: use the `Iter()` extension (`Game/Il2CppEnumerable.cs`) or a
  manual `GetEnumerator().Cast<Il2CppSystem.Collections.IEnumerator>()` loop; `foreach` fails.
- Nullable reference types and `record`s don't compile against the interop assemblies.
- `?.` / `??` on Unity objects ignore destroyed objects — use `!= null` (Unity's operator).
- `Object.Destroy` is deferred; rename anything a same-frame reload might look up by name.
- Static game systems: `TPS.Core.StaticGameSystem<T>.Instance`.
- Python heredocs that write C#: an escaped `\n` can arrive as a real newline — check string
  literals after the edit (or use the Edit tool for those lines).
- `gh` only works from PowerShell here (Bash can't read the Windows credential store).

## Repo

- GitHub: `wslaghekke/tpm-expedition-helper` (private). Commit only when asked.
- This repo's git email is the GitHub noreply address (local config); keep it that way.
- Never commit `.devtoken`, logs, saves, screenshots or `bin/obj`.
