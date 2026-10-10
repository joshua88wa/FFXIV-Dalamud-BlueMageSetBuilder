using System;
using System.Linq;
using Dalamud.Game.ClientState.Conditions;

namespace BlueMageSetBuilder;

public enum ApplyOutcome { Applied, AlreadyActive, Blocked, Refused, Failed }

public sealed record ApplyResult(ApplyOutcome Outcome, string Message);

/// <summary>
/// Turns a profile into a live active set. Shared by the window's button and the
/// /blusets apply command so both obey the same checks.
/// </summary>
public sealed class SetApplier
{
    private readonly Configuration config;
    private readonly Spellbook book;

    public SetApplier(Configuration config, Spellbook book)
    {
        this.config = config;
        this.book = book;
    }

    public BuiltSet Build(Profile profile) =>
        SetBuilder.Build(profile, this.book, this.book.IsLearned, this.config.Excluded);

    /// <summary>Why a set cannot be applied right now, or null if it can.</summary>
    public static string? BlockReason()
    {
        if (!Spellbook.IsLoggedIn)
            return "Log in to a character first.";
        if (!Spellbook.IsBlueMage)
            return "Switch to Blue Mage to apply a set.";
        if (Service.Condition[ConditionFlag.InCombat])
            return "Leave combat to change spells.";
        return null;
    }

    public ApplyResult Apply(Profile profile)
    {
        var blocked = BlockReason();
        if (blocked is not null)
            return new ApplyResult(ApplyOutcome.Blocked, blocked);

        var built = this.Build(profile);
        if (built.Chosen.Count == 0)
            return new ApplyResult(ApplyOutcome.Blocked, "None of your learned spells are in this profile.");

        var current = this.book.GetActiveActionIds();
        var layout = SetBuilder.Layout(built.Chosen, current, this.config.KeepSlots);

        if (layout.SequenceEqual(current))
            return new ApplyResult(ApplyOutcome.AlreadyActive, $"{profile.Name} is already your active set.");

        bool reported;
        try
        {
            reported = this.book.SetActiveActionIds(layout);
        }
        catch (Exception ex)
        {
            Service.Log.Error(ex, "SetBlueMageActions threw");
            return new ApplyResult(ApplyOutcome.Failed, "Changing spells failed. Details are in /xllog.");
        }

        // Read the slots back rather than trusting the return value alone: this is the
        // only way to tell "the game said no" apart from "the game said yes".
        var after = this.book.GetActiveActionIds();
        var matches = after.SequenceEqual(layout);

        Service.Log.Info($"Apply {profile.Id}: reported={reported} matches={matches} " +
                         $"wanted=[{string.Join(",", layout)}] got=[{string.Join(",", after)}]");

        if (matches)
            return new ApplyResult(ApplyOutcome.Applied, $"Applied {profile.Name}: {built.Chosen.Count} spells.");

        return new ApplyResult(ApplyOutcome.Refused,
            "The game refused the change. Masked Carnivale stages lock your spells, and some other situations may too.");
    }
}
