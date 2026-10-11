using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;

namespace BlueMageSetBuilder;

public enum ApplyOutcome { Applied, AlreadyActive, Pending, Blocked, Refused, Failed }

public sealed record ApplyResult(ApplyOutcome Outcome, string Message);

public sealed record Cooldown(BlueSpell Spell, float Seconds);

/// <summary>
/// Turns a profile into a live active set. Shared by the window's button and the
/// /blusets apply command so both obey the same checks.
///
/// The game will not move a spell that is on cooldown or whose buff is still on you.
/// When buffs are in the way and the caller allows it, applying runs as a short
/// sequence over several frames: end one buff, wait for it to go and for the global
/// cooldown that recasting starts, end the next, and finally apply. The callback gets
/// Pending results along the way and one final result.
/// </summary>
public sealed class SetApplier : IDisposable
{
    private static readonly TimeSpan StepTimeout = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan RetryGap = TimeSpan.FromSeconds(1.5);

    private readonly Configuration config;
    private readonly Spellbook book;

    // Spells whose buff was up when the game refused a change, even though the spell
    // itself was not moving. For the rest of the session their buffs count as blockers
    // too, so the next press offers to end them. Learned, not listed.
    private readonly HashSet<uint> strictSpells = new();

    private Profile? pendingProfile;
    private Action<ApplyResult>? pendingCallback;
    private DateTime pendingDeadline;
    private DateTime nextAttempt;
    private int attempts;
    private bool waitingOnCooldowns;
    private readonly List<string> ended = new();

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

    /// <summary>Spells that would leave or change slot under this profile and are still on cooldown.</summary>
    public List<Cooldown> CooldownsInTheWay(Profile profile)
    {
        var (current, layout) = this.Plan(profile);
        return this.CooldownsInTheWay(current, layout);
    }

    /// <summary>
    /// Buffs that stop this profile being applied: those from spells that would leave or
    /// change slot, plus any the game has already refused over this session.
    /// </summary>
    public List<BlockingStatus> BlockingStatusesFor(Profile profile)
    {
        var (current, layout) = this.Plan(profile);
        var affected = new List<BlueSpell>();
        for (var i = 0; i < current.Length && i < layout.Length; i++)
        {
            if (current[i] == 0 || this.book.ByAction(current[i]) is not { } spell)
                continue;
            if (current[i] != layout[i] || this.strictSpells.Contains(spell.ActionId))
                affected.Add(spell);
        }

        // Up front, only buffs the plugin can end (recast or click off). One that can
        // only be waited out, such as Basic Instinct, which lasts as long as you are
        // solo, only counts once the game has actually refused over it; otherwise a
        // guess could hold the button shut indefinitely.
        return Blockers.ActiveStatuses(affected)
            .Where(s => s.Method != EndMethod.Wait || this.strictSpells.Contains(s.Spell.ActionId))
            .ToList();
    }

    public static string DescribeCooldowns(IEnumerable<Cooldown> cooldowns) =>
        string.Join(", ", cooldowns.Select(c => $"{c.Spell.Name} ({Math.Ceiling(c.Seconds):0}s)"));

    /// <summary>
    /// Applies the profile, ending blocking buffs first when <paramref name="endBlockers"/>
    /// is true. <paramref name="onDone"/> is called with Pending results while the plugin
    /// waits, then once with the final result on a later frame.
    /// </summary>
    public void Request(Profile profile, bool endBlockers, Action<ApplyResult> onDone)
    {
        if (this.IsPending)
        {
            onDone(new ApplyResult(ApplyOutcome.Blocked, "Already working on a previous apply."));
            return;
        }

        var hard = Blockers.HardBlockReason();
        if (hard is not null)
        {
            onDone(new ApplyResult(ApplyOutcome.Blocked, hard));
            return;
        }

        var statuses = this.BlockingStatusesFor(profile);
        if (statuses.Count == 0)
        {
            onDone(this.ApplyNow(profile));
            return;
        }

        var waits = statuses.Where(s => s.Method == EndMethod.Wait).ToList();
        if (waits.Count > 0)
        {
            onDone(new ApplyResult(ApplyOutcome.Blocked, $"Wait for {Blockers.DescribeWaits(waits)} to wear off."));
            return;
        }

        var names = Blockers.Describe(statuses);
        if (!endBlockers)
        {
            onDone(new ApplyResult(ApplyOutcome.Blocked, $"{names} is active, which stops your spells being changed. End it first."));
            return;
        }

        this.pendingProfile = profile;
        this.pendingCallback = onDone;
        this.pendingDeadline = DateTime.UtcNow + StepTimeout;
        this.nextAttempt = DateTime.UtcNow;
        this.attempts = 0;
        this.waitingOnCooldowns = false;
        this.ended.Clear();
        onDone(new ApplyResult(ApplyOutcome.Pending, $"Ending {names}..."));
    }

    private void OnUpdate(IFramework framework)
    {
        if (this.pendingProfile is not { } profile)
            return;

        var callback = this.pendingCallback!;
        var now = DateTime.UtcNow;
        var casting = Service.Condition[ConditionFlag.Casting];

        if (now > this.pendingDeadline)
        {
            var still = this.BlockingStatusesFor(profile);
            var what = still.Count > 0 ? $"{Blockers.Describe(still)} did not end" : "Cooldowns did not clear";
            Service.Log.Warning($"Apply sequence timed out: {what}");
            this.Finish(new ApplyResult(ApplyOutcome.Failed, $"{what}. If a cast was interrupted, stand still and try again."));
            return;
        }

        // A cast in progress: either ours, ending a buff, or the player's. Let it finish.
        if (casting)
            return;

        var statuses = this.BlockingStatusesFor(profile);
        if (statuses.Count > 0)
        {
            if (now < this.nextAttempt)
                return;

            var next = statuses[0];
            if (next.Method == EndMethod.Wait)
            {
                this.Finish(new ApplyResult(ApplyOutcome.Blocked, $"Wait for {Blockers.DescribeWaits(new[] { next })} to wear off."));
                return;
            }

            // Recasting waits for the global cooldown like any spell.
            if (next.Method == EndMethod.Recast && this.book.RemainingRecast(next.Spell.ActionId) > 0)
                return;

            if (++this.attempts > (statuses.Count * 2) + 2)
            {
                this.Finish(new ApplyResult(ApplyOutcome.Failed, $"{Blockers.Describe(statuses)} would not end. End it by hand and try again."));
                return;
            }

            var error = Blockers.RequestEnd(next, this.book.GetActiveActionIds());
            if (error is not null)
            {
                this.Finish(new ApplyResult(ApplyOutcome.Blocked, error));
                return;
            }

            if (next.Method == EndMethod.Recast && !this.ended.Contains(next.Spell.Name))
                this.ended.Add(next.Spell.Name);

            this.nextAttempt = now + RetryGap;
            this.pendingDeadline = now + StepTimeout;
            if (statuses.Count > 1)
                callback(new ApplyResult(ApplyOutcome.Pending, $"Ending {next.Name}, then {Blockers.Describe(statuses.Skip(1))}..."));
            return;
        }

        // Every buff is gone. Wait out cooldowns on spells that would move, giving the
        // deadline room for the longest of them.
        var cooldowns = this.CooldownsInTheWay(profile);
        if (cooldowns.Count > 0)
        {
            if (!this.waitingOnCooldowns)
            {
                this.waitingOnCooldowns = true;
                this.pendingDeadline = now + TimeSpan.FromSeconds(cooldowns.Max(c => c.Seconds) + 2);
                callback(new ApplyResult(ApplyOutcome.Pending, $"Waiting for cooldowns: {DescribeCooldowns(cooldowns)}"));
            }

            return;
        }

        var hard = Blockers.HardBlockReason();
        var result = hard is null ? this.ApplyNow(profile) : new ApplyResult(ApplyOutcome.Blocked, hard);
        if (result.Outcome == ApplyOutcome.Applied && this.ended.Count > 0)
            result = result with { Message = $"{result.Message} Recast {string.Join(" and ", this.ended)} if you need it." };
        this.Finish(result);
    }

    private void Finish(ApplyResult result)
    {
        var callback = this.pendingCallback;
        this.pendingProfile = null;
        this.pendingCallback = null;
        this.waitingOnCooldowns = false;
        callback?.Invoke(result);
    }

    private (uint[] Current, uint[] Layout) Plan(Profile profile)
    {
        var current = this.book.GetActiveActionIds();
        var layout = SetBuilder.Layout(this.Build(profile).Chosen, current, this.config.KeepSlots);
        return (current, layout);
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
        {
            if (this.config.OpenSpellbookAfterApply)
                GameUi.OpenSpellbook();
            return new ApplyResult(ApplyOutcome.Applied, $"Applied {profile.Name}: {built.Chosen.Count} spells.");
        }

        // Refused anyway. If any active spell's buff is up, the game may block changes
        // while it lasts even when that spell is not moving. Remember those spells so
        // the next press offers to end their buffs.
        var activeSpells = current.Where(id => id != 0).Select(this.book.ByAction).OfType<BlueSpell>().ToList();
        var spellBuffs = Blockers.ActiveStatuses(activeSpells);
        var learned = spellBuffs.Where(s => this.strictSpells.Add(s.Spell.ActionId)).ToList();

        var buffs = Blockers.AllStatusNames();
        var recasting = activeSpells
            .Select(s => new Cooldown(s, this.book.RemainingRecast(s.ActionId)))
            .Where(c => c.Seconds > 0)
            .ToList();

        Service.Log.Warning($"Refused. Statuses: [{string.Join(", ", buffs)}] Recasting: [{DescribeCooldowns(recasting)}] " +
                            $"Spell buffs now treated as blocking: [{Blockers.Describe(learned)}]");

        if (learned.Count > 0)
        {
            var endable = learned.Where(s => s.Method != EndMethod.Wait).ToList();
            var waitOnly = learned.Where(s => s.Method == EndMethod.Wait).ToList();
            var advice = endable.Count > 0 && waitOnly.Count == 0
                ? "Press the button again to end it first."
                : endable.Count == 0
                    ? "It cannot be ended early, so wait for it to wear off."
                    : $"Press the button again to end {Blockers.Describe(endable)}; {Blockers.Describe(waitOnly)} has to wear off.";
            return new ApplyResult(ApplyOutcome.Refused,
                $"The game refused while {Blockers.Describe(learned)} was active. {advice}");
        }

        var detail = string.Empty;
        if (buffs.Count > 0)
            detail += $" Your buffs: {string.Join(", ", buffs)}.";
        if (recasting.Count > 0)
            detail += $" Recasting: {DescribeCooldowns(recasting)}.";

        return new ApplyResult(ApplyOutcome.Refused,
            "The game refused the change. Masked Carnivale stages lock your spells, and something else may too." + detail);
    }
}
