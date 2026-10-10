using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;
using StatusSheet = Lumina.Excel.Sheets.Status;

namespace BlueMageSetBuilder;

/// <summary>A buff on the player that stops spells being changed.</summary>
/// <param name="SourceId">Entity id of whoever applied the buff. Removing it the way a right-click does needs this.</param>
public sealed record BlockingStatus(uint Id, string Name, bool CanRemove, uint SourceId);

/// <summary>
/// Everything that stops the active set from changing, in one place.
///
/// The game has no message or data table for this: the spellbook simply greys out. Its
/// own check (ActionManager.CanAssignBlueMageAction) is known only by address, with no
/// signature or parameters, so it is not called. Instead there are two layers: a list
/// of known blockers checked up front, and a read-back after every apply that catches
/// anything the list misses and names the player's buffs so it can be added here.
/// </summary>
public static class Blockers
{
    // Status ids from the Status sheet. Add to this when a refused apply turns up a new one.
    private static readonly uint[] KnownBlockingStatuses =
    {
        2124, // Aetheric Mimicry: Tank
        2125, // Aetheric Mimicry: DPS
        2126, // Aetheric Mimicry: Healer
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
            if (status.StatusId != 0 && KnownBlockingStatuses.Contains(status.StatusId))
            {
                var row = Service.Data.GetExcelSheet<StatusSheet>().GetRowOrDefault(status.StatusId);
                result.Add(new BlockingStatus(
                    status.StatusId,
                    row?.Name.ExtractText() ?? $"Status {status.StatusId}",
                    row?.CanStatusOff ?? false,
                    status.SourceId));
            }
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
    /// Clicks the buffs off. Three ways, stopping at the first the game accepts:
    ///   1. StatusManager.ExecuteStatusOff with the buff's real source, as a right-click does.
    ///   2. The same with no source, which matches the first buff with that id.
    ///   3. The game's own /statusoff "name" text command.
    /// The first version used only (2) and the game silently declined it for Aetheric
    /// Mimicry, so every attempt and its answer is logged.
    /// </summary>
    public static void RequestRemoval(IEnumerable<BlockingStatus> statuses)
    {
        foreach (var s in statuses.Where(s => s.CanRemove))
        {
            if (s.SourceId is not 0 and not 0xE0000000)
            {
                var withSource = StatusManager.ExecuteStatusOff(s.Id, s.SourceId);
                Service.Log.Info($"Status off {s.Name} ({s.Id}) with source {s.SourceId:X}: {withSource}");
                if (withSource)
                    continue;
            }

            var anySource = StatusManager.ExecuteStatusOff(s.Id);
            Service.Log.Info($"Status off {s.Name} ({s.Id}) with any source: {anySource}");
            if (anySource)
                continue;

            SendTextCommand($"/statusoff \"{s.Name}\"");
        }
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

    public static string Describe(IReadOnlyList<BlockingStatus> statuses) =>
        string.Join(", ", statuses.Select(s => s.Name).Distinct());
}
