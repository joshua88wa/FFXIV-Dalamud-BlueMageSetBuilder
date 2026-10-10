using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Client.Game;
using StatusSheet = Lumina.Excel.Sheets.Status;

namespace BlueMageSetBuilder;

/// <summary>A buff on the player that stops spells being changed.</summary>
public sealed record BlockingStatus(uint Id, string Name, bool CanRemove);

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
                    row?.CanStatusOff ?? false));
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
    /// Clicks the buffs off, the same as right-clicking them. This only sends the
    /// request; the buff disappears when the server answers, a frame or more later.
    /// </summary>
    public static void RequestRemoval(IEnumerable<BlockingStatus> statuses)
    {
        foreach (var s in statuses.Where(s => s.CanRemove))
        {
            Service.Log.Info($"Requesting status off: {s.Name} ({s.Id})");
            StatusManager.ExecuteStatusOff(s.Id);
        }
    }

    public static string Describe(IReadOnlyList<BlockingStatus> statuses) =>
        string.Join(", ", statuses.Select(s => s.Name).Distinct());
}
