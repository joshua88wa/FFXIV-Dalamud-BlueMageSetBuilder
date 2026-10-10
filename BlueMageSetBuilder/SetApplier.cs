using System;
using System.Linq;
using Dalamud.Plugin.Services;

namespace BlueMageSetBuilder;

public enum ApplyOutcome { Applied, AlreadyActive, Pending, Blocked, Refused, Failed }

public sealed record ApplyResult(ApplyOutcome Outcome, string Message);

/// <summary>
/// Turns a profile into a live active set. Shared by the window's button and the
/// /blusets apply command so both obey the same checks.
///
/// When a removable buff such as Aetheric Mimicry is in the way, the apply happens in
/// two steps: ask the server to drop the buff, then apply on the first frame it is gone.
/// The result arrives through the callback either way.
/// </summary>
public sealed class SetApplier : IDisposable
{
    private static readonly TimeSpan RemovalTimeout = TimeSpan.FromSeconds(5);

    private readonly Configuration config;
    private readonly Spellbook book;

    private Profile? pendingProfile;
    private Action<ApplyResult>? pendingCallback;
    private DateTime pendingDeadline;

    public SetApplier(Configuration config, Spellbook book)
    {
        this.config = config;
        this.book = book;
        Service.Framework.Update += this.OnUpdate;
    }

    public bool IsPending => this.pendingProfile is not null;

    public void Dispose() => Service.Framework.Update -= this.OnUpdate;

    public BuiltSet Build(Profile profile) =>
        SetBuilder.Build(profile, this.book, this.book.IsLearned, this.config.Excluded);

    /// <summary>
    /// Applies the profile, removing blocking buffs first when <paramref name="removeBlockers"/>
    /// is true. <paramref name="onDone"/> is called with a Pending result first if a buff
    /// has to come off, then once more with the final result on a later frame.
    /// </summary>
    public void Request(Profile profile, bool removeBlockers, Action<ApplyResult> onDone)
    {
        if (this.IsPending)
        {
            onDone(new ApplyResult(ApplyOutcome.Blocked, "Already waiting on a previous apply."));
            return;
        }

        var hard = Blockers.HardBlockReason();
        if (hard is not null)
        {
            onDone(new ApplyResult(ApplyOutcome.Blocked, hard));
            return;
        }

        var statuses = Blockers.ActiveStatuses();
        if (statuses.Count == 0)
        {
            onDone(this.ApplyNow(profile));
            return;
        }

        var names = Blockers.Describe(statuses);

        if (!removeBlockers)
        {
            onDone(new ApplyResult(ApplyOutcome.Blocked, $"{names} is active, which stops spells being changed. Remove it first."));
            return;
        }

        if (statuses.Any(s => !s.CanRemove))
        {
            onDone(new ApplyResult(ApplyOutcome.Blocked, $"{names} stops spells being changed and cannot be clicked off. Wait for it to end."));
            return;
        }

        Blockers.RequestRemoval(statuses);
        this.pendingProfile = profile;
        this.pendingCallback = onDone;
        this.pendingDeadline = DateTime.UtcNow + RemovalTimeout;
        onDone(new ApplyResult(ApplyOutcome.Pending, $"Removing {names}..."));
    }

    private void OnUpdate(IFramework framework)
    {
        if (this.pendingProfile is not { } profile)
            return;

        var callback = this.pendingCallback!;

        if (Blockers.ActiveStatuses().Count == 0)
        {
            this.ClearPending();

            // Something else may have changed while waiting, such as combat starting.
            var hard = Blockers.HardBlockReason();
            var result = hard is null ? this.ApplyNow(profile) : new ApplyResult(ApplyOutcome.Blocked, hard);
            if (result.Outcome == ApplyOutcome.Applied)
                result = result with { Message = result.Message + " Recast Aetheric Mimicry if you need it." };
            callback(result);
            return;
        }

        if (DateTime.UtcNow > this.pendingDeadline)
        {
            this.ClearPending();
            callback(new ApplyResult(ApplyOutcome.Failed, "The buff did not come off. Remove it by hand and try again."));
        }
    }

    private void ClearPending()
    {
        this.pendingProfile = null;
        this.pendingCallback = null;
    }

    private ApplyResult ApplyNow(Profile profile)
    {
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

        // Not caught by any known blocker. Name the buffs so the culprit can be added to
        // Blockers.KnownBlockingStatuses.
        var buffs = Blockers.AllStatusNames();
        Service.Log.Warning($"Unexplained refusal. Statuses: {string.Join(", ", buffs)}");
        var buffText = buffs.Count > 0 ? $" Your buffs right now: {string.Join(", ", buffs)}." : string.Empty;
        return new ApplyResult(ApplyOutcome.Refused,
            "The game refused the change. Masked Carnivale stages lock your spells, and a buff may be blocking it." + buffText);
    }
}
