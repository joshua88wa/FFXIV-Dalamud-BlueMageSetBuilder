# Blue Mage Set Builder

A Dalamud plugin for FINAL FANTASY XIV. It reads which Blue Mage spells you have
learned and builds a 24-spell active set for the content you are about to do, then
applies it with one click.

## Features

- Profiles for solo and FATE leveling, dungeon DPS, trial and raid DPS, tanking and
  healing.
- The suggested set shows every spell with the reason it made the cut, and which ones
  are already active.
- A "Worth learning" list of spells the profile wants that you do not know yet, with
  where to learn each one.
- The whole spellbook in one table: rank, learned or not, where to get it. Any spell can
  be excluded from every suggestion, for example one you find awkward to use.
- When a profile's first choice is not learned, a weaker stand-in fills the slot, so a
  low-level Blue Mage still gets a usable set.
- Spells that are already active stay in their slots when a set is applied, so as
  little moves as possible.
- Profiles are a plain JSON file you can export, edit in any text editor, and reload
  without rebuilding the plugin.

## Commands

- `/blusets` opens or closes the window
- `/blusets apply <profile>` applies a profile, for macros. Takes a profile id (`solo`,
  `dungeon`, `boss`, `tank`, `healer`) or the start of its name. With no name it applies
  the profile selected in the window.
- `/blusets config` opens the settings tab
- `/blusets debug` prints what the plugin can see to chat

## Rules the game enforces

You can only change spells as a Blue Mage and out of combat, and a Masked Carnivale
stage locks them once it starts. The plugin checks the first two before trying and
reports it if the game refuses.

## Editing the profiles

Settings tab, **Export the built-in profiles for editing**, then **Open config folder**
and edit `Profiles.json`. Each profile is a list of picks in priority order:

```json
{ "Any": [ 105, 63, 1 ], "Why": "Single-target filler." }
```

A pick fills one slot with the first spell in `Any` that you have learned, have not
excluded, and is not already in the set. The numbers are the ones in the Blue Magic
Spellbook, which are the same in every client language. Picks stop once 24 slots are
full. Press **Reload profiles** after saving. Your copy replaces the built-in one
completely; delete it from the Settings tab to go back.

The built-in profiles are a reasonable starting point rather than a tuned meta.

## How it works

- The spell list comes from the game's own `AozAction` and `AozActionTransient` sheets,
  so new spells appear without a plugin update.
- A spell counts as learned when the unlock bit named by its `Action` row's
  `UnlockLink` is set (`UIState.IsUnlockLinkUnlocked`).
- Applying writes all 24 slots at once through `ActionManager.SetBlueMageActions`, the
  same call the game uses to load a saved active set, then reads the slots back to
  confirm the change took.

## Problems and suggestions

Open an issue:
[github.com/joshua88wa/FFXIV-Dalamud-BlueMageSetBuilder/issues](https://github.com/joshua88wa/FFXIV-Dalamud-BlueMageSetBuilder/issues)

## Credits

Game structures come from
[FFXIVClientStructs](https://github.com/aers/FFXIVClientStructs).

Written with the help of an LLM, in case that matters to you.

## Building

Needs the .NET 10 SDK and an XIVLauncher install, which is where the Dalamud reference
assemblies come from.

```
cd BlueMageSetBuilder
dotnet build -c Release
```

The build lands in `BlueMageSetBuilder/bin/Release/`, with a packaged `latest.zip`
alongside it.

## License

[0BSD](LICENSE). Do whatever you want with it. No attribution, no notice to carry
around, no conditions at all.
