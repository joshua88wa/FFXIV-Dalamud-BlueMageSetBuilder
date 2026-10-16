using System;
using System.Collections.Generic;
using Dalamud.Configuration;
using Dalamud.Plugin;

namespace BlueMageSetBuilder;

public enum DockSide
{
    Right,
    Left,
}

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    // Profile Id from Profiles.json, not its display name, so renaming a profile
    // does not lose the selection.
    public string SelectedProfile = "solo";

    // Masked Carnivale stage number, used when SelectedProfile is "carnivale".
    public int SelectedStage = 1;

    // Last profile Id chosen in each category tab (profile Group), so switching tabs
    // comes back to the same role.
    public Dictionary<string, string> GroupSelection = new();

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

    // Open this window with the Blue Magic Spellbook and keep it against one side of it.
    public bool AttachToSpellbook = true;

    public DockSide AttachSide = DockSide.Right;

    // When the spellbook opens this window, start as a thin bar beside it. The title-bar
    // arrow and the bar's expand button change it only until the spellbook next opens.
    public bool AttachStartShrunk = true;

    [NonSerialized] private IDalamudPluginInterface? pluginInterface;

    public void Initialize(IDalamudPluginInterface pi) => this.pluginInterface = pi;

    public void Save() => this.pluginInterface?.SavePluginConfig(this);
}
