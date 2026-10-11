# Expedition Helper (Two Point Museum)

BepInEx 6 (IL2CPP) mod that automates expeditions: auto-open reward crates, send exhibits of
already-unlocked types to inventory, and repeat an expedition until the museum has a pristine
copy and/or maxed knowledge.

## Layout

| Path | What |
|---|---|
| `src/ExpeditionHelper.Host` | Permanent BepInEx plugin. Hot-reloads the logic, forwards per-frame/config events, and (Debug only) runs the dev bridge. Changing it needs a game restart. |
| `src/ExpeditionHelper.Logic` | All gameplay hooks and UI. Hot-reloaded: `dotnet build` drops it into `BepInEx/plugins/ExpeditionHelper/reload/` and the running game swaps it in. |
| `tools/tpm` | CLI for the dev bridge (`ping`, `eval`, `reload`, `screenshot`, `log`). |

Game path defaults to `F:\SteamLibrary\steamapps\common\Two Point Museum`; override it in a
`Directory.Build.user.props` (`<GameDir>…</GameDir>`).

## Dev loop

```bash
dotnet build src/ExpeditionHelper.Logic   # game picks it up within ~1s
tools/tpm eval 'UnityEngine.Time.timeScale'
```

Settings: `BepInEx/config/tpm.expeditionhelper.cfg` — edits apply live.

### Needs a game restart

- any change to `ExpeditionHelper.Host` (the game locks the dll; the build warns and skips deploy)
- after a game update (BepInEx regenerates interop on the next launch)

### Denuvo constraint

The Windows build ships Denuvo Anti-Tamper, which obfuscates the il2cpp runtime internals that
Il2CppInterop's `ClassInjector` hooks (`GenericMethod::GetMethod` etc.). Therefore:

- `BepInEx.cfg` → `[Logging] UnityLogListening = false` (otherwise the chainloader itself needs injection and dies)
- no `ClassInjector.RegisterTypeInIl2Cpp`, no custom MonoBehaviours, no `AddComponent<OurType>`
- no managed → IL2CPP delegate conversion (`button.onClick.AddListener(...)`, `Action` → `Il2CppSystem.Action`)
- per-frame work: the host postfixes `TPS.Core.GameSystems.Update` (`FrameTick`)

### Hooking game methods

Use `NativeHook<TDelegate>` (Logic/Hooking), not Harmony. Il2CppInterop's Harmony support
auto-converts arguments and crashed on struct returns, `ref` struct arguments, and (calling side)
the generic `GameEvents.Raise<T>(ref T)` wrapper. `NativeHook` takes an explicit native signature
(`this` first for instance methods, trailing `MethodInfo*`, objects/refs as `IntPtr`), finds the
target by name via il2cpp metadata (survives updates that move code), verifies the signature, and
refuses targets `NativeCodeCheck` flags: shared bodies (identical code folding — every empty method
shares one `ret` stub), functions shorter than the 14-byte detour, and Denuvo's `.sbss` section.
Detours must catch all exceptions.

Raise game events with `GameEventsUtil.Raise(evt)`, never the generated `GameEvents.Raise<T>`.

### Hot-reload rules for Logic

- subscribe to `Host.Update` / `Host.ConfigChanged`
- hooks via `NativeHook` (auto-removed on unload); destroy any GameObjects you create in `Entry.Unload`

## Dev bridge

Debug builds of the host listen on `127.0.0.1:27185`. Every request needs the token from
`BepInEx/config/ExpeditionHelper.devtoken` (regenerated each launch) in an `X-Token` header.
`/eval` executes arbitrary C# in the game process, so Release builds compile it out.

## Build notes

Nullable reference types are disabled and reference-type `?` annotations must not be used: the
interop'd `UnityEngine.CoreModule` exposes public copies of `NullableAttribute` & co. that the
compiler binds to and can't construct (CS0656).
