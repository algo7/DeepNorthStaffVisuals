# WeaponAuras

A client-side [BepInEx](https://github.com/BepInEx/BepInEx) mod for Valheim: the game's weapon effects (Frostfire
flames, Thunderblood lightning, the Ashlands Bleeding / Storming / Primal effects, Dyrnwyn's fire, Frostner's frost) on
the Deep North staffs Echo Spike and Lightning Strike, which have none. Visual only; other players see the plain staff.

What it does for players is in [package/README.md](package/README.md). Changes: [CHANGELOG.md](CHANGELOG.md).

Made with AI assistance.

## How it works

A weapon's look in hand is its prefab's `attach` child, which `VisEquipment.AttachItem` instantiates onto the hand or
back bone. The Frostfire / Thunderblood / Ashlands variants carry their effects as plain child objects there (particle
systems and a point light, no mesh references), so they can be copied onto any other weapon model.

- `VisEquipment.AttachItem` postfix: for the local player only, puts a `WeaponAuras_look` container under the new
  model with fitted copies of the current look's donor effects (the variant of the same weapon kind, or the closest
  kind). The donor prefabs are never changed.
- The fit measures both models along their largest mesh's own axes (atgeirs, bows, knives and axes sit turned inside
  their models): the donor's business end (from where its blade or head starts to the tip) lands on the target's,
  width and thickness keep their fractions, the copies turn with the model, and only the emitter shapes and rates
  stretch (transform scales stay, since some systems scale their particles with their parents). Flames, clouds, spores
  and frost come off the weapon's own mesh edges instead (like the game's upgrade glow), so they follow its shape.
- The key (default L, BepInEx setting) steps Plain → Frostfire → Thunderblood → Bleeding → Storming → Primal →
  Dyrnwyn → Frostner → Plain, shows the name as a centre message and re-dresses the models already equipped.
- `KeyHints.UpdateHints` postfix: a "Change look" row, cloned from the game's Block row.

The look lives in memory only and goes back to Plain on every scene load.

## Building

Needs the .NET SDK (8+) and the game's DLLs (a local Valheim install, or the dedicated server's).

```sh
make build      # bin/Release/WeaponAuras.dll
make test       # unit tests: looks, weapon kinds, the fit math, and the game members the patches use
make package    # Algo7-WeaponAuras-<version>.zip for r2modman's "Import local mod"
```

The version comes from the git tag (MinVer); untagged builds are 0.1.0.

## Layout

| Path | What |
|---|---|
| `src/Plugin.cs` | Entry point, the key setting and key press, patching, scene-load reset |
| `src/WeaponKinds.cs`, `src/Looks.cs`, `src/Fixes.cs`, `src/FitMath.cs` | Kinds, looks and donors, the fix list, the fit (Unity-free, unit tested) |
| `src/EffectKit.cs` | Which weapons can take a look, the donors' effect parts, model boxes |
| `src/Dresser.cs`, `src/Copier.cs` | The current look, dressing models, copying and fitting the effects |
| `src/HintPatches.cs` | The key hint |
| `tests/` | Unit tests (`make test`) |
| `package/` | Mod page and icon |

## License

[MIT](LICENSE)
