using System.Collections.Generic;
using System.Linq;

namespace BlueMageSetBuilder;

/// <summary>
/// What /blusets debug shows, built once for both the chat command and the About
/// page's Copy debug info button.
/// </summary>
public static class DebugReport
{
    /// <param name="Problem">Set when the learned check disagrees with the active set.</param>
    public sealed record Report(List<string> Lines, string? Problem);

    public static Report Build(Configuration config, Spellbook book, ProfileStore store, SetApplier applier)
    {
        var lines = new List<string>
        {
            $"Logged in: {Spellbook.IsLoggedIn}, on Blue Mage: {Spellbook.IsBlueMage}",
            $"Spells in game data: {book.Spells.Count}, learned: {book.LearnedCount()}",
            $"Profiles: {store.Profiles.Count} from {store.LoadedFrom}",
            $"Blocked by state: {Blockers.HardBlockReason() ?? "no"}",
            $"Spellbook window: {GameUi.DescribeSpellbookRects()}; Dalamud scale {Dalamud.Interface.Utility.ImGuiHelpers.GlobalScale:0.###}",
        };

        var activeSpells = book.GetActiveActionIds().Where(id => id != 0).Select(book.ByAction).OfType<BlueSpell>();
        var spellBuffs = Blockers.ActiveStatuses(activeSpells);
        lines.Add($"Buffs from active spells: {(spellBuffs.Count == 0 ? "none" : string.Join(", ", spellBuffs.Select(b => $"{b.Name} ({b.Id}, from {b.Spell.Name}, ends by {b.Method})")))}");

        if (store.Find(config.SelectedProfile) is { } selected)
        {
            var blocking = applier.BlockingStatusesFor(selected);
            lines.Add($"Blocking {selected.Name}: {(blocking.Count == 0 ? "nothing" : Blockers.Describe(blocking))}");
        }

        lines.Add($"All buffs: {string.Join(", ", Blockers.AllStatusNames())}");

        var active = book.GetActiveActionIds();
        var names = active.Select((id, i) =>
        {
            if (id == 0)
                return $"{i + 1}:-";
            var s = book.ByAction(id);
            return s is null ? $"{i + 1}:?{id}" : $"{i + 1}:{s.Name}";
        });
        lines.Add($"Active slots: {string.Join(", ", names)}");

        var contradictions = active
            .Where(id => id != 0)
            .Select(book.ByAction)
            .Where(s => s is not null && !book.IsLearned(s))
            .Select(s => s!.Name)
            .ToList();

        if (contradictions.Count == 0)
        {
            lines.Add("Learned check agrees with the active set.");
            return new Report(lines, null);
        }

        return new Report(lines, $"Active but reported as not learned: {string.Join(", ", contradictions)}. The learned check is wrong.");
    }
}
