using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;
using StatusSheet = Lumina.Excel.Sheets.Status;

namespace BlueMageSetBuilder;

/// <summary>A buff on the player that stops spells being changed.</summary>
/// <param name="CanRemove">The plugin has a way to end it: a spell that ends it on reuse, or the game allows clicking it off.</param>
/// <param name="SourceId">Entity id of whoever applied the buff, for clicking it off.</param>
/// <param name="EndedByActionId">A spell that ends this buff when cast again, or 0.</param>
public sealed record BlockingStatus(uint Id, string Name, bool CanRemove, uint SourceId, uint EndedByActionId);

/// <summary>
/// Everything that stops the active set from changing, in one place.
///
/// The game has no message or data table for this: the spellbook simply greys out. Its
/// own check (ActionManager.CanAssignBlueMageAction) is known only by address, with no
/// signature or parameters, so it is not called. Instead there are two layers: a list
/// of known blockers checked up front, and a read-back after every apply that catches
/// anything the list misses and logs buffs and cooldowns so it can be added here.
/// </summary>
public static class Blockers
{
    public const uint AethericMimicryAction = 18322;

    // Status id -> the spell that ends it when cast again (0 if none). Status ids are
    // from the Status sheet. Add to this when a refused apply turns up a new blocker.
    //
    // Aetheric Mimicry cannot be clicked off: the game refuses ExecuteStatusOff and
    // /statusoff for it (tested 2026-10-10). Its tooltip says "Effect ends upon reuse",
    // and recasting it is how players remove it, so that is what the plugin does.
    private static readonly Dictionary<uint, uint> KnownBlockingStatuses = new()
    {
        [2124] = AethericMimicryAction, // Aetheric Mimicry: Tank
        [2125] = AethericMimicryAction, // Aetheric Mimicry: DPS
        [2126] = AethericMimicryAction, // Aetheric Mimicry: Healer
    };

    /// <summary>States that block changes, or where changing spells makes no sense.</summary>
    private static readonly (ConditionFlag Flag, string Reason)[] BlockingConditions =
    {
        (ConditionFlag.InCombat, "Leave combat to change spells."),
        (ConditionFlag.Casting, "Wait for your cast to finish."),
        (ConditionFlag.BetweenAreas, "Wait until you have finished zoning."),
        (ConditionFlag.OccupiedInCutSceneEvent, "Wait for the cutscene to end."),
        (ConditionFlag.WatchingCutscene, "Wait for the cutscene to end."),
    };

    /// <summary>A blocker that removing a buff cannot fix, or null.</summary>
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

    /// <summary>Known blocking buffs the player has right now.</summary>
    public static List<BlockingStatus> ActiveStatuses()
    {
        var result = new List<BlockingStatus>();
        var player = Service.Objects.LocalPlayer;
        if (player is null)
            return result;

        foreach (var status in player.StatusList)
        {
            if (status.StatusId == 0 || !KnownBlockingStatuses.TryGetValue(status.StatusId, out var endedBy))
                continue;

            var row = Service.Data.GetExcelSheet<StatusSheet>().GetRowOrDefault(status.StatusId);
            result.Add(new BlockingStatus(
                status.StatusId,
                row?.Name.ExtractText() ?? $"Status {status.StatusId}",
                endedBy != 0 || (row?.CanStatusOff ?? false),
                status.SourceId,
                endedBy));
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

    /// <summary>
    /// Starts ending the buffs. Returns null once every request is on its way, or a
    /// reason it could not be done. Either way the buff only disappears a moment later
    /// (after a cast, or when the server answers), so the caller waits for it.
    /// </summary>
    public static unsafe string? RequestRemoval(IReadOnlyList<BlockingStatus> statuses, uint[] activeActionIds)
    {
        var castAlready = new HashSet<uint>();

        foreach (var s in statuses.Where(s => s.CanRemove))
        {
            if (s.EndedByActionId != 0)
            {
                if (!castAlready.Add(s.EndedByActionId))
                    continue;

                var error = CastToEnd(s, activeActionIds);
                if (error is not null)
                    return error;
                continue;
            }

            if (!ClickOff(s))
                SendTextCommand($"/statusoff \"{s.Name}\"");
        }

        return null;
    }

    // Recasts the spell that ends the buff, the way a player would. It has to be one of
    // the 24 active spells, and the game has to be willing to cast it right now, which
    // it reports through GetActionStatus as a LogMessage id (0 means it can).
    private static unsafe string? CastToEnd(BlockingStatus s, uint[] activeActionIds)
    {
        var spellName = Service.Data.GetExcelSheet<Lumina.Excel.Sheets.Action>()
            .GetRowOrDefault(s.EndedByActionId)?.Name.ExtractText() ?? "the spell";

        if (!activeActionIds.Contains(s.EndedByActionId))
            return $"{s.Name} ends when you recast {spellName}, but {spellName} is not in your active spells. End it by hand.";

        var am = ActionManager.Instance();
        if (am == null)
            return "The game is not ready to cast.";

        var status = am->GetActionStatus(ActionType.Action, s.EndedByActionId);
        if (status != 0)
        {
            var why = DescribeLogMessage(status);
            Service.Log.Info($"Cannot cast {spellName} to end {s.Name}: status {status} ({why})");
            return $"Could not recast {spellName} to end {s.Name}: {why}";
        }

        var used = am->UseAction(ActionType.Action, s.EndedByActionId);
        Service.Log.Info($"Recast {spellName} ({s.EndedByActionId}) to end {s.Name} ({s.Id}): {used}");
        return used ? null : $"The game would not cast {spellName}. End {s.Name} by hand.";
    }

    // Right-click equivalent: with the real source first, then any source.
    private static bool ClickOff(BlockingStatus s)
    {
        if (s.SourceId is not 0 and not 0xE0000000)
        {
            var withSource = StatusManager.ExecuteStatusOff(s.Id, s.SourceId);
            Service.Log.Info($"Status off {s.Name} ({s.Id}) with source {s.SourceId:X}: {withSource}");
            if (withSource)
                return true;
        }

        var anySource = StatusManager.ExecuteStatusOff(s.Id);
        Service.Log.Info($"Status off {s.Name} ({s.Id}) with any source: {anySource}");
        return anySource;
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

    public static string Describe(IReadOnlyList<BlockingStatus> statuses) =>
        string.Join(", ", statuses.Select(s => s.Name).Distinct());
}
