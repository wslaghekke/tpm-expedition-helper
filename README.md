# Expedition Helper for Two Point Museum

Less clicking, more collecting. Expedition Helper takes care of the repetitive part of
expeditions:

- **Opens returning crates for you.** Exhibits you've already discovered go straight to your
  inventory, pieces of multi-part exhibits are attached, and science materials and expert XP are
  claimed. New discoveries are left for you, so you still get the reveal.
- **Repeats expeditions until you have everything.** Send an expedition once with "Repeat until
  done" on, and it goes out again with the same team, approach and cargo until you own a
  **pristine copy** and/or have **maxed knowledge** of every exhibit that location can drop.
  Exhibits in your inventory that you could still analyse count towards knowledge.
- **Looks after your money.** Repeats wait while you're making a loss, would dip below your cash
  reserve, or couldn't cover the month-end wages and loans, and they wait out seasonal dangers your
  team can't counter.
- **Keeps you informed without nagging.** A waiting repeat leaves one message in your inbox saying
  what it's waiting for (with a "Cancel repeat" button); short pop-ups tell you when a repeat is
  sent or stops.

Everything is configured in game: a new **Expedition Helper** tab in Settings, and a **Repeat
until done** switch on each expedition's setup screen.

## Requirements

- Two Point Museum on **Windows** (Steam). Tested with game version 12.0.244617.
- **BepInEx 6** (IL2CPP, build be.788). It's included in the "with BepInEx" download below.

## Install

1. Download a zip from the [latest release](https://github.com/wslaghekke/tpm-expedition-helper/releases/latest):
   - **`ExpeditionHelper-<version>-with-BepInEx.zip`** if you don't have BepInEx yet (most people).
   - **`ExpeditionHelper-<version>.zip`** if you already run BepInEx 6 be.788. Make sure
     `BepInEx/config/BepInEx.cfg` has `UnityLogListening = false` under `[Logging]`, or the game
     crashes on start.
2. Open the game folder: in Steam, right-click Two Point Museum → *Manage* → *Browse local files*.
3. Extract the zip into that folder, next to `TPM.exe`.
4. Start the game. **The first start takes a few minutes** while BepInEx prepares itself (a
   black or frozen-looking window is normal); later starts are quick.

To check it worked: open Settings in game; there should be an extra tab with a map icon.

## Using it

**Settings → Expedition Helper** (the tab with the map icon):

| Setting | Default | What it does |
|---|---|---|
| Open crates automatically | Off | Claims returning crates for you, per the options below it. |
| Send known exhibits to inventory | On | Exhibits you've discovered before go to inventory. New ones are left for you to open. |
| Attach pieces of known exhibits | On | Pieces of multi-part exhibits are attached like "Attach to Exhibit" does. |
| Claim science materials / expert XP / perks | On | Also claims those crates. |
| Repeat until done by default | Off | The starting value of the "Repeat until done" switch on the setup screen. |
| Until a pristine copy is owned | On | Goal: a pristine copy of every exhibit the location can drop (placed or in inventory). |
| Until knowledge is maxed | On | Goal: maximum knowledge of every exhibit there. |
| Count inventory towards knowledge | On | Exhibits in your inventory that you could analyse count as knowledge you'll get. |
| Wait out seasonal dangers | On | Holds a repeat while the trip would run into a seasonal danger your team can't counter. |
| Maximum repeats in a row | 10 | Safety limit per expedition. For a number of perks or a stock target it counts only runs in a row that brought none of it. |
| Notifications | All | All / only when a repeat stops / none. |
| Keep at least this much cash | 10,000 | Never pays for a repeat that would leave less than this. |
| Only while the museum makes a profit | On | Waits while the museum itself is losing money (expedition costs are covered by the two cash checks). |
| Keep month-end costs covered | On | Waits if paying would leave too little for wages and loan repayments. |

**On the expedition setup screen**, a **Repeat** section at the bottom of the setup list: the
**Repeat until done** switch turns repeating on or off for that location, and the line below it
shows what's still to get there. Exhibits you haven't discovered yet are only counted, never named.

At locations that can drop perks or science materials (Metal, Whirlicogs, Golden Truffles, ...),
you can repeat for one of those instead of the exhibit goals: pick it in the dropdown, then how
many with the workshop-style buttons below it. For a perk that's **x1 x3 x5 x10** more than you
have; for a material it's steps that suit it (**+100 +300 +500 +1,000** Metal, **+1 +3 +5 +10**
Golden Truffles). The expedition repeats until you have that many, then the choice resets. Perks
you haven't found there yet can only be picked as "until it drops", and perks that need a higher
survey level are marked. Locations with only one thing to repeat for show a switch instead of the
dropdown; locations without exhibits have no "Repeat until done" switch, as picking a goal is what
repeats there. Auto-open's "Claim science materials" and "Claim perks" options keep these chains
going without you opening each crate. Locations that drop no exhibits, perks or materials (cash
locations) can't be repeated.

**In the art studio** (Arty-Facts), the Repeat section sits below "Explore Emotion": **x1 x3 x5 x10**
is how many sessions to run in a row with the same artist, art type and emotion (x1 is just this
one). Each next session starts once you've revealed the last piece yourself, and with **Wait until
fully rested** (on by default) once the artist has also lost the "Blank-ish Canvas" feeling, which
otherwise adds a Creative Juice-less event to the session. Auto-open leaves studio pieces for you to
reveal.

**Last team**, next to Auto-fill, puts the team and cargo you last sent to that location back,
whichever helipad you're sending from. Team members who can't go right now (still away,
recovering) leave their slot empty, and the cargo stays as it is if you've run out of it.

Good to know:
- A repeat starts once the helicopter has landed and the whole team is back and fit to go. An
  artist's studio rest counts from the end of a session, so a piece left unrevealed for a while
  can be followed by the next session straight away.
- Pristine exhibits only drop once a location reaches its top survey level, which takes several
  trips there. The status line tells you when that's not the case yet. The *Speedy* approach
  doesn't raise the survey level; *Detailed* and the Survey Scanner cargo raise it fastest.
- The per-location switches, perk, stock and studio choices and last teams are kept per museum (in
  `BepInEx/config/ExpeditionHelper/museums/`), so they survive a restart, and an expedition that's
  out when you quit carries on repeating when it comes back.
- Settings are also stored in `BepInEx/config/tpm.expeditionhelper.cfg`, if you'd rather edit
  them in a text editor (changes apply while the game runs).

## Uninstall

Delete `BepInEx/plugins/ExpeditionHelper`. To remove BepInEx entirely, also delete `winhttp.dll`,
`doorstop_config.ini`, `.doorstop_version` and the `BepInEx` and `dotnet` folders. The mod keeps
its own state in memory and in its `.cfg` file, not in your saves; what it does in game (claiming
crates, sending expeditions) are ordinary game actions.

## Troubleshooting

- **The game crashes on start:** check `UnityLogListening = false` in `BepInEx/config/BepInEx.cfg`.
- **No Expedition Helper tab:** look in `BepInEx/LogOutput.log` for lines with `Expedition Helper`.
  After a game update the mod may need an update too; it logs which parts it couldn't hook
  instead of crashing.
- Please report problems as a [GitHub issue](https://github.com/wslaghekke/tpm-expedition-helper/issues)
  with your `BepInEx/LogOutput.log` attached.

---

# For developers

## How it's built

| Path | What |
|---|---|
| `src/ExpeditionHelper.Host` | Permanent BepInEx plugin. Hot-reloads the logic, forwards per-frame/config events, and (Debug only) runs the dev bridge. Changing it needs a game restart. |
| `src/ExpeditionHelper.Logic` | All gameplay hooks and UI. Hot-reloaded: `dotnet build` drops it into `BepInEx/plugins/ExpeditionHelper/reload/` and the running game swaps it in. |
| `tools/tpm` | CLI for the dev bridge (`ping`, `eval`, `reload`, `screenshot`, `log`). |
| `tools/snippets/` | Reusable `tpm eval` probes. |
| `tools/release.ps1` | Builds the release zips and (with `-Publish`) the GitHub release. |

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

## Dev loop

```bash
dotnet build src/ExpeditionHelper.Logic   # the running game picks it up within ~1s
tools/tpm eval 'UnityEngine.Time.timeScale'
```

Needs a game restart: any change to `ExpeditionHelper.Host` (the game locks the dll; the build
warns and skips deploy), and every game update (BepInEx regenerates interop on the next launch).

### Dev bridge

Debug builds of the host listen on `127.0.0.1:27185`. Every request needs the token from
`BepInEx/config/ExpeditionHelper.devtoken` (regenerated each launch) in an `X-Token` header;
`tools/tpm` handles that. `/eval` executes arbitrary C# on the game's main thread, so Release
builds compile the bridge out. See `CLAUDE.md` for the details.

### Releasing

`tools/release.ps1 -Version 1.2.3` builds Release and writes both zips to `dist/1.2.3/`
(downloading BepInEx be.788 once, checked against its SHA-256, plus the license texts of
everything bundled with it). `-Publish` additionally tags `v1.2.3`, pushes the tag and creates the
GitHub release with `gh`. Bump `Plugin.Version` in `src/ExpeditionHelper.Host/Plugin.cs` and commit
first; the script refuses a mismatch or a dirty tree. It runs locally rather than in GitHub
Actions because the build needs the game's interop assemblies.

## Constraints and gotchas

### Denuvo

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
widget classes, matched against our instances' pointers (see `SettingsMenuTab`, `UiClicks`,
`InboxNotice`). Widgets a screen doesn't have come from the Settings menu prefab (`UiTemplates`).
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

### Build notes

Nullable reference types are disabled and reference-type `?` annotations must not be used: the
interop'd `UnityEngine.CoreModule` exposes public copies of `NullableAttribute` & co. that the
compiler binds to and can't construct (CS0656).

## License

The mod's code is MIT (see `LICENSE`). The `-with-BepInEx` release zip also contains BepInEx and
its bundled libraries, unmodified, under their own licenses (LGPL-2.1/3.0, MIT, Apache-2.0); their
texts and a list of what is where are in that zip's `licenses/` folder. This project isn't
affiliated with Two Point Studios or SEGA.
