# Changelog

Newest first. The plugin's Changelog page is built from this file, and so are the
release notes: `tools/changelog-notes.ps1 -Version <version>` prints one version's
list. Keep each entry to one bullet a player would care about. When releasing, rename
`## Unreleased` to `## <version> (<date>)`.

## Unreleased
- Sets can be applied on any job, as the game allows. On another job the window shows
  the game's reminder that hotbar changes are only saved on Blue Mage.
- Masked Carnivale: a set for every stage, built around the spells Blue Academy
  (mage.blue) lists as required or recommended, with each act's enemies and what they
  are vulnerable to, a link to Blue Academy's guide for the stage, and Previous and
  Next buttons for working through the stages in order.
- A line above the set names any spells it needs that you have not learned, with a
  button that jumps to where to learn them.
- New layout: a sidebar with Layouts, Spellbook, Settings, Changelog and About pages.
- Layouts has a tab for each kind of content, and a Role picker (Tank, Healer, DPS) for
  dungeons and trials and raids. Each tab remembers what you last picked.
- Opens beside the Blue Magic Spellbook and moves with it, starting as a slim bar that
  expands with a click. Choose the side, or turn it off, in Settings.
- Spellbook page: filters for rank and for learned or not learned, and where to learn
  each spell now wraps instead of being cut off.
- About page with the commands, credits, and a button that copies debug information
  for bug reports.
- Profiles.json: picks are written as "Spell" (files using the old "Any" still load),
  and every pick names its spells in a comment.
- Place names start with a capital letter: "The Dragon's Neck", not "the Dragon's Neck".

## 0.1.0.0 (2026-10-14)
- First release. Builds a 24-spell set from the spells you have learned for open world
  and FATEs, solo duties, and DPS, tank and healer in dungeons and in trials and raids,
  then applies it with one click.
- Ends Mighty Guard, Aetheric Mimicry and similar effects, and waits out cooldowns,
  when they stop spells from changing.
- Spellbook table with search and exclusions, tooltips like the game's, and the
  creature and boss that teach each spell.
- Opens the Blue Magic Spellbook after applying, so new spells can go straight onto
  your hotbars.
