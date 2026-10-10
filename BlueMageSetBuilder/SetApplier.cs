using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Plugin.Services;

namespace BlueMageSetBuilder;

public enum ApplyOutcome { Applied, AlreadyActive, Pending, Blocked, Refused, Failed }

public sealed record ApplyResult(ApplyOutcome Outcome, string Message);

public sealed record Cooldown(BlueSpell Spell, float Seconds);

/// <summary>
/// Turns a profile into a live active set. Shared by the window's button and the
/// /blusets apply command so both obey the same checks.
///
/// When a removable buff such as Aetheric Mimicry is in the way, the apply happens in
/// steps: ask for the buff to come off, wait for it to go, wait for any cooldowns on
/// spells that would move, then apply. The result arrives through the callback.
/// </summary>
public sealed class SetApplier : IDisposable
{
    private static readonly TimeSpan RemovalTimeout = TimeSpan.FromSeconds(5);

    private readonly Configuration config;
    private readonly Spellbook book;

    private Profile? pendingProfile;
    private Action<ApplyResult>? pendingCallback;
    private DateTime pendingDeadline;
    private bool pendingWaitingOnCooldowns;

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
    /// Spells that would leave or change slot under this profile and are still on
    /// cooldown. The game will not move a spell that is recasting.
    /// </summary>
    public List<Cooldown> CooldownsInTheWay(Profile profile)
    {
        var current = this.book.GetActiveActionIds();
        var layout = SetBuilder.Layout(this.Build(profile).Chosen, current, this.config.KeepSlots);
        return this.CooldownsInTheWay(current, layout);
    }

    public static string DescribeCooldowns(IEnumerable<Cooldown> cooldowns) =>
        string.Join(", ", cooldowns.Select(c => $"{c.Spell.Name} ({Math.Ceiling(c.Seconds):0}s)"));

    /// <summary>
    /// Applies the profile, removing blocking buffs first when <paramref name="removeBlockers"/>
    /// is true. <paramref name="onDone"/> is called with a Pending result first if the
    /// plugin has to wait, then once more with the final result on a later frame.
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
        this.pendingWaitingOnCooldowns = false;
        onDone(new ApplyResult(ApplyOutcome.Pending, $"Removing {names}..."));
    }

    private void OnUpdate(IFramework framework)
    {
        if (this.pendingProfile is not { } profile)
            return;

        var callback = this.pendingCallback!;
        var now = DateTime.UtcNow;

        if (Blockers.ActiveStatuses().Count > 0)
        {
            if (now > this.pendingDeadline)
            {
                var still = Blockers.Describe(Blockers.ActiveStatuses());
                Service.Log.Warning($"Status removal timed out; still active: {still}");
                this.ClearPending();
                callback(new ApplyResult(ApplyOutcome.Failed, $"{still} did not come off. Remove it by hand and try again."));
            }

            return;
        }

        // The buff is gone. Wait out any cooldowns on spells that would move, giving
        // the deadline enough room for the longest of them.
        var cooldowns = this.CooldownsInTheWay(profile);
        if (cooldowns.Count > 0)
        {
            if (!this.pendingWaitingOnCooldowns)
            {
                this.pendingWaitingOnCooldowns = true;
                this.pendingDeadline = now + TimeSpan.FromSeconds(cooldowns.Max(c => c.Seconds) + 2);
                callback(new ApplyResult(ApplyOutcome.Pending, $"Waiting for cooldowns: {DescribeCooldowns(cooldowns)}"));
            }

            if (now <= this.pendingDeadline)
                return;
        }

        this.ClearPending();

        // Something else may have changed while waiting, such as combat starting.
        var hard = Blockers.HardBlockReason();
        var result = hard is null ? this.ApplyNow(profile) : new ApplyResult(ApplyOutcome.Blocked, hard);
        if (result.Outcome == ApplyOutcome.Applied)
            result = result with { Message = result.Message + " Recast Aetheric Mimicry if you need it." };
        callback(result);
    }

    private void ClearPending()
    {
        this.pendingProfile = null;
        this.pendingCallback = null;
        this.pendingWaitingOnCooldowns = false;
    }

    // A slot "moves" when it held a spell and will hold something else afterwards.
    private List<Cooldown> CooldownsInTheWay(uint[] current, uint[] layout)
    {
        var result = new List<Cooldown>();
        for (var i = 0; i < current.Length && i < layout.Length; i++)
        {
            if (current[i] == 0 || current[i] == layout[i])
                continue;

            var seconds = this.book.RemainingRecast(current[i]);
            if (seconds > 0 && this.book.ByAction(current[i]) is { } spell)
                result.Add(new Cooldown(spell, seconds));
        }

        return result;
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

        var cooldowns = this.CooldownsInTheWay(current, layout);
        if (cooldowns.Count > 0)
            return new ApplyResult(ApplyOutcome.Blocked, $"Wait for cooldowns: {DescribeCooldowns(cooldowns)}");

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

        Service.Log.Info($"Apply {profile.Id}: reported={reported} matches={matches} keepSlots={this.config.KeepSlots} " +
                         $"wanted=[{string.Join(",", layout)}] got=[{string.Join(",", after)}]");

        if (matches)
            return new ApplyResult(ApplyOutcome.Applied, $"Applied {profile.Name}: {built.Chosen.Count} spells.");

        // Not caught by any known blocker. Record everything that might explain it: buffs,
        // and any active spell still recasting (not just ones that were meant to move).
        var buffs = Blockers.AllStatusNames();
        var recasting = current
            .Where(id => id != 0)
            .Select(id => (Spell: this.book.ByAction(id), Seconds: this.book.RemainingRecast(id)))
            .Where(x => x.Spell is not null && x.Seconds > 0)
            .Select(x => new Cooldown(x.Spell!, x.Seconds))
            .ToList();

        Service.Log.Warning($"Unexplained refusal. Statuses: [{string.Join(", ", buffs)}] " +
                            $"Recasting: [{DescribeCooldowns(recasting)}]");

        var detail = string.Empty;
        if (buffs.Count > 0)
            detail += $" Your buffs: {string.Join(", ", buffs)}.";
        if (recasting.Count > 0)
            detail += $" Recasting: {DescribeCooldowns(recasting)}.";

        return new ApplyResult(ApplyOutcome.Refused,
            "The game refused the change. Masked Carnivale stages lock your spells, and something else may too." + detail);
    }
}
