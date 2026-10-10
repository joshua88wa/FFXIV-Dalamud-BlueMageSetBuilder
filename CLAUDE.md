# Blue Mage Set Builder

A Dalamud plugin for FFXIV: reads the player's learned Blue Mage spells and builds the
best 24-spell active set for the content they pick, then applies it.

## Working agreement

**Never `git push` or create a release without being asked to.** Commits are fine to
make locally; changes get reviewed before they reach GitHub.

**Not yet published.** There is no GitHub repo and no `repo.json` entry yet. It is
being tested locally as a dev plugin first.

## The two-repo setup

Same as the other plugins: this repo holds the code, and the distribution manifest is
`Dalamud/repo.json` in `joshua88wa/FFXIV` (`C:\Utilities\FFXIV\repos\FFXIV`). Shipping
any version touches both.

## Version numbers live in three places

1. `BlueMageSetBuilder/BlueMageSetBuilder.csproj` -> `<Version>`
2. `BlueMageSetBuilder/BlueMageSetBuilder.json` -> `AssemblyVersion`
3. `Dalamud/repo.json` in the hub repo, once it is published

## Build

```
cd BlueMageSetBuilder
dotnet build -c Release
```

Produces `BlueMageSetBuilder/bin/Release/BlueMageSetBuilder/latest.zip`. Keep the name
`latest.zip`; the repo.json download links depend on it.

For local testing, add `BlueMageSetBuilder/bin/Release/BlueMageSetBuilder.dll` under
Dalamud Settings -> Experimental -> Dev Plugin Locations.

## Dalamud API level

15 (`Dalamud.NET.Sdk/15.0.0`), in the `.csproj` SDK version and the plugin `.json`.

## How the plugin is put together

- `Spellbook` is the only class that touches game memory. It builds the spell list from
  the `AozAction` / `AozActionTransient` / `Action` sheets, answers "is it learned" via
  `UIState.IsUnlockLinkUnlocked(Action.UnlockLink)`, and reads and writes the 24 slots
  through `ActionManager.BlueMageActions` / `SetBlueMageActions`.
- `SetBuilder` is pure logic: profile + learned predicate + exclusions in, chosen spells
  and "worth learning" list out. `Layout` places them in slots, keeping already-active
  spells where they are.
- `SetApplier` holds the guards (logged in, on Blue Mage, out of combat) and is shared
  by the window and `/blusets apply`. It reads the slots back after writing, because the
  return value alone does not prove the change took.
- `ProfileStore` loads `Profiles.json`: the embedded copy by default, or a user copy in
  the plugin config folder, which replaces it entirely.

## Spell numbers

Profiles use the Blue Magic Spellbook number (`AozActionTransient.Number`), not the
`AozAction` row id. They match from 50 upward but not below, so always use the number
the game shows.

## Things still to verify in game

- Whether `SetBlueMageActions` persists server-side: apply a set, then zone or relog and
  check the spellbook.
- `/blusets debug` self-checks the learned test: every active spell must read as
  learned. A contradiction means `UnlockLink` is the wrong source.
- Behaviour inside duties and Masked Carnivale. The game's `CanAssignBlueMageAction`
  is in ClientStructs' data.yml but not yet in the C# structs.
