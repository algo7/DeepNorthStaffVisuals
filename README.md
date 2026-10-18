# DeepNorthStaffVisuals

[![CI](https://github.com/algo7/DeepNorthStaffVisuals/actions/workflows/ci.yml/badge.svg)](https://github.com/algo7/DeepNorthStaffVisuals/actions/workflows/ci.yml)
[![Thunderstore](https://img.shields.io/badge/Thunderstore-DeepNorthStaffVisuals-blue)](https://thunderstore.io/c/valheim/p/Algo7/DeepNorthStaffVisuals/)
[![Hexium](https://img.shields.io/badge/Hexium-DeepNorthStaffVisuals-purple)](https://valheim.hexium.gg/mods/Algo7/DeepNorthStaffVisuals)

A client-side [BepInEx](https://github.com/BepInEx/BepInEx) mod for Valheim: the game's own visual effects (Frostfire
flames, Thunderblood lightning, the Ashlands Bleeding / Storming / Primal effects, Dyrnwyn's fire, Frostner's frost) on
the Deep North staffs Echo Spike and Lightning Strike, which have none. Visual only; friends with the mod see your
look, everyone else the plain staff.

What it does for players is in [package/README.md](package/README.md), which is also the mod's Thunderstore and
Hexium page. Changes: [CHANGELOG.md](CHANGELOG.md).

Made with AI assistance.

## How it works

A weapon's look in hand is its prefab's `attach` child, which `VisEquipment.AttachItem` instantiates onto the hand or
back bone. The Frostfire / Thunderblood / Ashlands variants carry their effects as plain child objects there (particle
systems and a point light, no mesh references), so they can be copied onto any other weapon model.

- A staff's look is a note on the staff (`ItemData.m_customData["DeepNorthStaffVisuals_look"]`, the look's name;
  Plain is no note), so the game saves it with the staff everywhere. An upgraded staff is a new one, so it's plain.
- On a player, the look of the staff on show (in hand or sheathed) is one int on their shared record (ZDO key
  `DeepNorthStaffVisuals_look`; Plain is 0, a look is the stable hash of its name). The local player's game writes
  it; games without the mod ignore it.
- Every model (players' and armor stands' hands and backs, item stands, dropped staffs) remembers the look last put on
  it and is dressed when that changes: a `VisEquipment.CustomUpdate` postfix (players from their records, armor stands
  from their slots' notes), `ItemStand.SetVisualItem` / `UpdateVisual` and `ItemDrop.Start` postfixes. Dressing puts a
  `DeepNorthStaffVisuals_look` container under the model with fitted copies of the look's donor effects (the variant
  of the same weapon kind, or the closest kind). The donor prefabs are never changed.
- The fit measures both models along their largest mesh's own axes (atgeirs, bows, knives and axes sit turned inside
  their models): the donor's business end (from where its blade or head starts to the tip) lands on the target's,
  width and thickness keep their fractions, the copies turn with the model, and only the emitter shapes and rates
  stretch (transform scales stay, since some systems scale their particles with their parents). Flames, clouds, spores
  and frost come off the weapon's own mesh edges instead (like the game's upgrade glow), so they follow its shape.
- The key (default L, BepInEx setting) steps the staff on show through Plain → Frostfire → Thunderblood → Bleeding →
  Storming → Primal → Dyrnwyn → Frostner → Plain and shows the name as a centre message.
- `KeyHints.UpdateHints` postfix: a "Change look" row, cloned from the game's Block row.
- `ItemData.GetTooltip` postfix: a last line "Look: Frostfire" (not on the crafting screen).

## Building

Needs the .NET SDK (8+) and the game's DLLs (a local Valheim install, or the dedicated server's).

```sh
make build      # bin/Release/DeepNorthStaffVisuals.dll
make test       # builds the plugin, then unit tests: looks, weapon kinds, the fit math, the game members the patches use, and the hook check
make package    # Algo7-DeepNorthStaffVisuals-<version>.zip for a mod manager's local import
```

The version comes from the git tag (MinVer).

## CI / CD

CI builds and tests against the free dedicated server's DLLs (`.github/scripts/valheim-managed.sh`) on every push and
pull request, and every day at 06:00 UTC (`.github/workflows/daily.yml`, or "Run workflow" in Actions), so a game update
that breaks a hook turns it red within a day; GitHub switches the daily run off after 60 days without repository
activity (turn it back on under Actions → Daily check). The hook check (`tests/HookCheck.cs`, in `make test`) reads the
built DLL and the game DLLs with Mono.Cecil and fails when a Harmony hook's target or parameters, or a member the code
looks up by name, no longer match the game. A `vX.Y.Z` tag with a matching `## X.Y.Z` section in `CHANGELOG.md`
releases to GitHub, Thunderstore and Hexium after manual approval.

## Layout

| Path | What |
|---|---|
| `src/Plugin.cs` | Entry point, the key setting and key press, patching, scene-load reset |
| `src/WeaponKinds.cs`, `src/Looks.cs`, `src/Fixes.cs`, `src/FitMath.cs` | Kinds, looks and donors, the fix list, the fit (Unity-free, unit tested) |
| `src/EffectKit.cs` | Which weapons can take a look, the donors' effect parts, model boxes |
| `src/Dresser.cs`, `src/Copier.cs` | Players' and armor stands' looks, the key, dressing models, copying and fitting the effects |
| `src/ShowPatches.cs` | Item stands, dropped staffs, the tooltip line |
| `src/HintPatches.cs` | The key hint |
| `tests/` | Unit tests and the hook check (`make test`) |
| `package/` | Mod page (Thunderstore and Hexium) and icon |

## License

[MIT](LICENSE)
