using System;
using System.Collections.Generic;
using System.Linq;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lumina.Excel.Sheets;
using Lumina.Text.ReadOnly;

namespace BlueMageSetBuilder;

/// <summary>One Blue Magic spell, flattened out of the three sheets that describe it.</summary>
/// <param name="Number">The number shown in the Blue Magic Spellbook. Stable across languages.</param>
/// <param name="ActionId">The Action row, which is what the active set actually stores.</param>
/// <param name="UnlockLink">Bit in UIState that is set once the spell is learned.</param>
public sealed record BlueSpell(
    uint Number,
    uint ActionId,
    string Name,
    uint Icon,
    byte Rank,
    uint UnlockLink,
    string Source)
{
    // Tooltip material, kept as game strings so they draw with the game's own colours.

    /// <summary>The combat description: potency, additional effects. From ActionTransient.</summary>
    public ReadOnlySeString Description { get; init; }

    /// <summary>"Type / Aspect / Rank" block from the spellbook page.</summary>
    public ReadOnlySeString Stats { get; init; }

    /// <summary>The flavour text from the spellbook page.</summary>
    public ReadOnlySeString Lore { get; init; }

    public ushort Cast100Ms { get; init; }
    public ushort Recast100Ms { get; init; }
    public sbyte Range { get; init; }
    public byte Radius { get; init; }
}

/// <summary>
/// The only class that reads game state. Everything else works on <see cref="BlueSpell"/>
/// and plain arrays.
/// </summary>
public sealed unsafe class Spellbook
{
    public const byte BlueMageJobId = 36;
    public const int SlotCount = 24;

    private readonly Dictionary<uint, BlueSpell> byNumber = new();
    private readonly Dictionary<uint, BlueSpell> byAction = new();

    public IReadOnlyList<BlueSpell> Spells { get; }

    public Spellbook()
    {
        var aozSheet = Service.Data.GetExcelSheet<AozAction>();
        var transientSheet = Service.Data.GetExcelSheet<AozActionTransient>();
        var actionTransientSheet = Service.Data.GetExcelSheet<ActionTransient>();
        var list = new List<BlueSpell>();

        foreach (var aoz in aozSheet)
        {
            if (aoz.Action.RowId == 0)
                continue;

            var action = aoz.Action.ValueNullable;
            if (action is null)
                continue;

            var transient = transientSheet.GetRowOrDefault(aoz.RowId);
            var number = transient is { } t && t.Number != 0 ? t.Number : aoz.RowId;

            var spell = new BlueSpell(
                number,
                aoz.Action.RowId,
                action.Value.Name.ExtractText(),
                action.Value.Icon,
                aoz.Rank,
                action.Value.UnlockLink.RowId,
                transient is { } tr ? DescribeSource(tr) : string.Empty)
            {
                Description = actionTransientSheet.GetRowOrDefault(aoz.Action.RowId)?.Description ?? default,
                Stats = transient?.Stats ?? default,
                Lore = transient?.Description ?? default,
                Cast100Ms = action.Value.Cast100ms,
                Recast100Ms = action.Value.Recast100ms,
                Range = action.Value.Range,
                Radius = action.Value.EffectRange,
            };

            list.Add(spell);
            this.byNumber[spell.Number] = spell;
            this.byAction[spell.ActionId] = spell;
        }

        this.Spells = list.OrderBy(s => s.Number).ToList();
    }

    public BlueSpell? ByNumber(uint number) => this.byNumber.GetValueOrDefault(number);

    public BlueSpell? ByAction(uint actionId) => this.byAction.GetValueOrDefault(actionId);

    public static bool IsBlueMage
    {
        get
        {
            var ps = PlayerState.Instance();
            return ps != null && ps->IsLoaded && ps->CurrentClassJobId == BlueMageJobId;
        }
    }

    public static bool IsLoggedIn
    {
        get
        {
            var ps = PlayerState.Instance();
            return ps != null && ps->IsLoaded;
        }
    }

    public bool IsLearned(BlueSpell spell)
    {
        if (spell.UnlockLink == 0)
            return false;

        var ui = UIState.Instance();
        return ui != null && ui->IsUnlockLinkUnlocked(spell.UnlockLink);
    }

    public int LearnedCount() => this.Spells.Count(this.IsLearned);

    /// <summary>The 24 active slots as Action ids. 0 is an empty slot.</summary>
    public uint[] GetActiveActionIds()
    {
        var result = new uint[SlotCount];
        var am = ActionManager.Instance();
        if (am == null)
            return result;

        var slots = am->BlueMageActions;
        for (var i = 0; i < SlotCount && i < slots.Length; i++)
            result[i] = slots[i];

        return result;
    }

    /// <summary>
    /// Writes all 24 slots in one call, the same way loading a saved active set does.
    /// Must be called on the framework thread, which the UI draw callback already is.
    /// </summary>
    public bool SetActiveActionIds(uint[] actionIds)
    {
        if (actionIds.Length != SlotCount)
            throw new ArgumentException($"Expected {SlotCount} slots.", nameof(actionIds));

        var am = ActionManager.Instance();
        if (am == null)
            return false;

        fixed (uint* ptr = actionIds)
            return am->SetBlueMageActions(ptr);
    }

    // LocationKey says what Location points at: 1 a zone, 4 a duty. 2 is a Whalaqee
    // Totem reward from Wayward Gaheel Ja and 3 is the spell you start with; both
    // leave Location at 0.
    private static string DescribeSource(AozActionTransient t)
    {
        try
        {
            switch (t.LocationKey)
            {
                case 1 when t.Location.TryGetValue<PlaceName>(out var place):
                    return place.Name.ExtractText();
                case 4 when t.Location.TryGetValue<ContentFinderCondition>(out var duty):
                    return duty.Name.ExtractText();
                case 2:
                    return "Whalaqee Totem reward (Wayward Gaheel Ja, Ul'dah)";
                case 3:
                    return "Known from the start";
            }
        }
        catch (Exception ex)
        {
            Service.Log.Debug(ex, "Could not resolve spell source");
        }

        return string.Empty;
    }
}
