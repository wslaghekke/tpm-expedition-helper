# Expedition Helper (Two Point Museum)

BepInEx 6 (IL2CPP) mod that automates expeditions: auto-open reward crates, send exhibits of
already-unlocked types to inventory, and repeat an expedition until the museum has a pristine
copy and/or maxed knowledge.

## Install

From the [releases](https://github.com/wslaghekke/tpm-expedition-helper/releases):

- `ExpeditionHelper-<version>-with-BepInEx.zip` — BepInEx 6 (IL2CPP be.788, LGPL, licenses
  included) plus the mod. Extract into the game folder (next to `TPM.exe`); the first start takes
  a few minutes while BepInEx generates its interop assemblies.
- `ExpeditionHelper-<version>.zip` — the mod only, for an existing BepInEx 6 be.788 install.
  `UnityLogListening = false` must be set under `[Logging]` in `BepInEx/config/BepInEx.cfg`, or
  the game crashes on start (Denuvo, see below).

Settings are in game under Settings > Expedition Helper, and on each expedition's setup screen
("Repeat until done").

## Building it yourself

The code compiles against the interop assemblies BepInEx generates from *your* copy of the game
(`BepInEx/interop/`), so building needs the game installed; nothing game-derived is in this repo.

1. Install BepInEx as above (the `-with-BepInEx` zip works) and start the game once, so
   `BepInEx/interop/` gets generated.
2. Install a .NET SDK, 6 or newer (the projects target `net6.0`, the runtime BepInEx ships).
3. If the game isn't at `F:\SteamLibrary\steamapps\common\Two Point Museum`, create
   `Directory.Build.user.props` next to `Directory.Build.props`:
   ```xml
   <Project><PropertyGroup><GameDir>D:\Games\Two Point Museum</GameDir></PropertyGroup></Project>
   ```
4. `dotnet build src/ExpeditionHelper.Logic -c Release` builds both projects and copies them into
   the game's `BepInEx/plugins/ExpeditionHelper/` (close the game first: it locks the host DLL).
   Add `-p:DeployToGame=false` to only build. A Debug build (the default) adds the dev bridge
   described below; don't hand those out.

### Releasing

`tools/release.ps1 -Version 1.2.3` builds Release and writes both zips to `dist/1.2.3/`
(downloading BepInEx be.788 once, checked against its SHA-256, plus the license texts).
`-Publish` additionally tags `v1.2.3`, pushes the tag and creates the GitHub release with `gh`.
Bump `Plugin.Version` in `src/ExpeditionHelper.Host/Plugin.cs` and commit first; the script
refuses a mismatch or a dirty tree. It runs locally rather than in GitHub Actions because the
build needs the game's interop assemblies.

## Layout

| Path | What |
|---|---|
| `src/ExpeditionHelper.Host` | Permanent BepInEx plugin. Hot-reloads the logic, forwards per-frame/config events, and (Debug only) runs the dev bridge. Changing it needs a game restart. |
| `src/ExpeditionHelper.Logic` | All gameplay hooks and UI. Hot-reloaded: `dotnet build` drops it into `BepInEx/plugins/ExpeditionHelper/reload/` and the running game swaps it in. |
| `tools/tpm` | CLI for the dev bridge (`ping`, `eval`, `reload`, `screenshot`, `log`). |
| `tools/release.ps1` | Builds the release zips and (with `-Publish`) the GitHub release. |

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

### Game UI

We can't create MonoBehaviours or hand delegates to the game's widgets, so UI is built from clones
of the game's own objects (`Object.Instantiate`) and their clicks are caught with detours on the
widget classes, matched against our instances' pointers (see `SettingsMenuTab`, `InboxNotice`).
Gotchas found so far:
- disable a cloned label's I2 `Localize` component(s) before setting text (`UiText.Set`);
- a cloned nested Canvas/GraphicRaycaster gets clicks but not the game's tooltip hover check —
  remove them so the clone uses the parent canvas;
- some click handlers (e.g. `UITab.OnPointerDown`) sit in Denuvo code: hook an inherited
  base-class method instead (`UINavigationFocus.OnPointerUp` / `PrimaryPress`);
- a cloned tab still runs the handler of the tab it was copied from;
- `Object.Destroy` is deferred to the end of the frame: rename what you destroy if a reload in
  the same frame looks it up by name.

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

## License

The mod's code is MIT (see `LICENSE`). The `-with-BepInEx` release zip also contains BepInEx and
its bundled libraries, unmodified, under their own licenses (LGPL-2.1/3.0, MIT, Apache-2.0); their
texts and a list of what is where are in that zip's `licenses/` folder. This project isn't
affiliated with Two Point Studios or SEGA.
