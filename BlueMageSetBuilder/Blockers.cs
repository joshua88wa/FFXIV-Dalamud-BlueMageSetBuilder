using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;
using StatusSheet = Lumina.Excel.Sheets.Status;

namespace BlueMageSetBuilder;

/// <summary>How the plugin can get rid of a blocking buff.</summary>
public enum EndMethod
{
    /// <summary>Cast the spell again ("Effect ends upon reuse"): Mighty Guard, Aetheric Mimicry.</summary>
    Recast,

    /// <summary>The game allows clicking it off.</summary>
    ClickOff,

    /// <summary>Neither: it has to wear off.</summary>
    Wait,
}

/// <summary>A buff from one of your active spells that stops that spell being moved.</summary>
public sealed record BlockingStatus(uint Id, string Name, BlueSpell Spell, EndMethod Method, uint SourceId, float Remaining);

/// <summary>
/// Everything that stops the active set from changing, in one place.
///
/// The rule, as far as testing shows: the game will not move or remove a spell while
/// the buff it gave you is still up (Aetheric Mimicry, Mighty Guard), or while the
/// spell is on cooldown. Which buff belongs to which spell is worked out from the
/// game's data in <see cref="Spellbook"/>, so new cases are caught without a list.
///
/// The game has no message or data table for this: the spellbook simply greys out. Its
/// own check (ActionManager.CanAssignBlueMageAction) is known only by address, with no
/// signature or parameters, so it is not called.
/// </summary>
public static class Blockers
{
    /// <summary>States that block changes, or where changing spells makes no sense.</summary>
    private static readonly (ConditionFlag Flag, string Reason)[] BlockingConditions =
    {
        (ConditionFlag.InCombat, "Leave combat to change spells."),
        (ConditionFlag.Casting, "Wait for your cast to finish."),
        (ConditionFlag.BetweenAreas, "Wait until you have finished zoning."),
        (ConditionFlag.OccupiedInCutSceneEvent, "Wait for the cutscene to end."),
        (ConditionFlag.WatchingCutscene, "Wait for the cutscene to end."),
    };

    /// <summary>A blocker that ending a buff cannot fix, or null.</summary>
    public static string? HardBlockReason()
    {
        if (!Spellbook.IsLoggedIn)
            return "Log in to a character first.";
        if (!Spellbook.IsBlueMage)
            return "Switch to Blue Mage to apply a set.";

        foreach (var (flag, reason) in BlockingConditions)
        {
            if (Service.Condition[flag])
                return reason;
        }

        return null;
    }

    /// <summary>Buffs on the player that came from any of these spells.</summary>
    public static List<BlockingStatus> ActiveStatuses(IEnumerable<BlueSpell> spells)
    {
        var result = new List<BlockingStatus>();
        var player = Service.Objects.LocalPlayer;
        if (player is null)
            return result;

        var byStatus = new Dictionary<uint, BlueSpell>();
        foreach (var spell in spells)
        {
            foreach (var id in spell.GrantedStatuses)
                byStatus.TryAdd(id, spell);
        }

        if (byStatus.Count == 0)
            return result;

        var sheet = Service.Data.GetExcelSheet<StatusSheet>();
        foreach (var status in player.StatusList)
        {
            if (status.StatusId == 0 || !byStatus.TryGetValue(status.StatusId, out var spell))
                continue;

            // Only buffs you gave yourself. A boss's "Doom" or "Ice Spikes" shares a name
            // with your spell but did not come from it.
            if (status.SourceId is not 0 and not 0xE0000000 && status.SourceId != player.EntityId)
                continue;

            var row = sheet.GetRowOrDefault(status.StatusId);
            var method = spell.EndsOnReuse ? EndMethod.Recast
                : (row?.CanStatusOff ?? false) ? EndMethod.ClickOff
                : EndMethod.Wait;

            result.Add(new BlockingStatus(
                status.StatusId,
                row?.Name.ExtractText() ?? $"Status {status.StatusId}",
                spell,
                method,
                status.SourceId,
                Math.Max(0, status.RemainingTime)));
        }

        return result;
    }

    /// <summary>Names of every buff the player has, for diagnosing an unexplained refusal.</summary>
    public static List<string> AllStatusNames()
    {
        var player = Service.Objects.LocalPlayer;
        if (player is null)
            return new List<string>();

        var sheet = Service.Data.GetExcelSheet<StatusSheet>();
        return player.StatusList
            .Where(s => s.StatusId != 0)
            .Select(s => $"{sheet.GetRowOrDefault(s.StatusId)?.Name.ExtractText() ?? "?"} ({s.StatusId})")
            .ToList();
    }

    public static string Describe(IEnumerable<BlockingStatus> statuses) =>
        string.Join(", ", statuses.Select(s => s.Name).Distinct());

    /// <summary>"Diamondback (8s)" style, for buffs that can only be waited out.</summary>
    public static string DescribeWaits(IEnumerable<BlockingStatus> statuses) =>
        string.Join(", ", statuses.Select(s => s.Remaining > 0 ? $"{s.Name} ({Math.Ceiling(s.Remaining):0}s)" : s.Name).Distinct());

    /// <summary>
    /// Starts ending one buff. Returns null once the request is on its way, or a reason
    /// it could not be done. The buff only disappears a moment later (after the cast,
    /// or when the server answers), so the caller waits and checks again.
    /// </summary>
    public static string? RequestEnd(BlockingStatus s, uint[] activeActionIds) => s.Method switch
    {
        EndMethod.Recast => CastToEnd(s, activeActionIds),
        EndMethod.ClickOff => ClickOffOrCommand(s),
        _ => $"{s.Name} cannot be ended early. Wait for it to wear off ({Math.Ceiling(s.Remaining):0}s).",
    };

    // Recasts the spell that ends the buff, the way a player would. It has to be one of
    // the 24 active spells, and the game has to be willing to cast it right now, which
    // it reports through GetActionStatus as a LogMessage id (0 means it can).
    private static unsafe string? CastToEnd(BlockingStatus s, uint[] activeActionIds)
    {
        var spellName = s.Spell.Name;
        if (!activeActionIds.Contains(s.Spell.ActionId))
            return $"{s.Name} ends when you recast {spellName}, but {spellName} is not in your active spells. End it by hand.";

        var am = ActionManager.Instance();
        if (am == null)
            return "The game is not ready to cast.";

        var status = am->GetActionStatus(ActionType.Action, s.Spell.ActionId);
        if (status != 0)
        {
            var why = DescribeLogMessage(status);
            Service.Log.Info($"Cannot cast {spellName} to end {s.Name}: status {status} ({why})");
            return $"Could not recast {spellName} to end {s.Name}: {why}";
        }

        var used = am->UseAction(ActionType.Action, s.Spell.ActionId);
        Service.Log.Info($"Recast {spellName} ({s.Spell.ActionId}) to end {s.Name} ({s.Id}): {used}");
        return used ? null : $"The game would not cast {spellName}. End {s.Name} by hand.";
    }

    // Right-click equivalent (real source, then any source), then the /statusoff command.
    private static string? ClickOffOrCommand(BlockingStatus s)
    {
        if (s.SourceId is not 0 and not 0xE0000000)
        {
            var withSource = StatusManager.ExecuteStatusOff(s.Id, s.SourceId);
            Service.Log.Info($"Status off {s.Name} ({s.Id}) with source {s.SourceId:X}: {withSource}");
            if (withSource)
                return null;
        }

        var anySource = StatusManager.ExecuteStatusOff(s.Id);
        Service.Log.Info($"Status off {s.Name} ({s.Id}) with any source: {anySource}");
        if (anySource)
            return null;

        SendTextCommand($"/statusoff \"{s.Name}\"");
        return null;
    }

    // Runs a game text command as if typed in the chat box. Only ever called with a
    // command built here from a status name out of the game's own sheet.
    private static unsafe void SendTextCommand(string command)
    {
        var ui = UIModule.Instance();
        if (ui == null)
            return;

        Service.Log.Info($"Falling back to: {command}");
        var str = Utf8String.FromString(command);
        try
        {
            ui->ProcessChatBoxEntry(str);
        }
        finally
        {
            str->Dtor(true);
        }
    }

    /// <summary>The game's own wording for a LogMessage id, such as "Target not in range."</summary>
    private static string DescribeLogMessage(uint id)
    {
        try
        {
            var text = Service.SeStrings.EvaluateFromLogMessage(id).ExtractText().Trim();
            if (text.Length > 0)
                return text;
        }
        catch
        {
            // Fall through to the number.
        }

        return $"game message {id}";
    }
}
