using System;
using System.Collections.Generic;
using Dalamud.Configuration;
using Dalamud.Plugin;

namespace BlueMageSetBuilder;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    // Profile Id from Profiles.json, not its display name, so renaming a profile
    // does not lose the selection.
    public string SelectedProfile = "solo";

    // Spellbook numbers (the number shown in the Blue Magic Spellbook), because they
    // are the same in every client language and are what Profiles.json uses.
    public HashSet<uint> Excluded = new();

    // A spell that is already active and also in the new set stays in its slot, so
    // applying a set moves as little as possible. Off by default at Joshua's choice:
    // with it off the set is laid out in the profile's priority order.
    public bool KeepSlots = false;

    public bool ShowUnlearned = true;

    // Let /blusets apply turn off Mighty Guard, Aetheric Mimicry and similar spell
    // effects that stop spells moving, the same as the window's button does. The window
    // button always names them first, so this only affects the command.
    public bool AutoRemoveBlockers = true;

    // Open the Blue Magic Spellbook after a set is applied, so the new spells can be
    // dragged straight onto a hotbar.
    public bool OpenSpellbookAfterApply = true;

    [NonSerialized] private IDalamudPluginInterface? pluginInterface;

    public void Initialize(IDalamudPluginInterface pi) => this.pluginInterface = pi;

    public void Save() => this.pluginInterface?.SavePluginConfig(this);
}
