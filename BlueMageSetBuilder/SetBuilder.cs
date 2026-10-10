using System;
using System.Collections.Generic;
using System.Linq;

namespace BlueMageSetBuilder;

public sealed record ChosenSpell(BlueSpell Spell, string Why, int PickIndex);

public sealed record MissingSpell(BlueSpell Spell, string Why, int PickIndex);

public sealed class BuiltSet
{
    public List<ChosenSpell> Chosen { get; } = new();

    /// <summary>
    /// Picks that would have been in the set if the player knew the spell. Only the
    /// first spell of a pick is listed, since that is the one the profile prefers.
    /// </summary>
    public List<MissingSpell> Missing { get; } = new();
}

/// <summary>Pure logic: no game state, so it is easy to reason about and test.</summary>
public static class SetBuilder
{
    public static BuiltSet Build(Profile profile, Spellbook book, Func<BlueSpell, bool> isLearned, ISet<uint> excluded)
    {
        var result = new BuiltSet();
        var used = new HashSet<uint>();

        for (var i = 0; i < profile.Picks.Count; i++)
        {
            var pick = profile.Picks[i];
            var candidates = pick.Any
                .Select(book.ByNumber)
                .Where(s => s is not null && !excluded.Contains(s.Number))
                .Cast<BlueSpell>()
                .ToList();

            if (candidates.Count == 0)
                continue;

            var hadRoom = result.Chosen.Count < Spellbook.SlotCount;
            if (hadRoom)
            {
                var winner = candidates.FirstOrDefault(s => !used.Contains(s.Number) && isLearned(s));
                if (winner is not null)
                {
                    used.Add(winner.Number);
                    result.Chosen.Add(new ChosenSpell(winner, pick.Why, i));
                }
            }

            // The preferred spell is worth learning even if a stand-in filled the slot,
            // but only while the set still had room for it.
            var preferred = candidates[0];
            if (hadRoom && !isLearned(preferred) && !used.Contains(preferred.Number)
                && result.Missing.All(m => m.Spell.Number != preferred.Number))
            {
                result.Missing.Add(new MissingSpell(preferred, pick.Why, i));
            }
        }

        return result;
    }

    /// <summary>
    /// Lays the chosen spells out across the 24 slots. With <paramref name="keepSlots"/>,
    /// a spell that is already active stays where it is and only the rest move.
    /// </summary>
    public static uint[] Layout(IReadOnlyList<ChosenSpell> chosen, uint[] current, bool keepSlots)
    {
        var slots = new uint[Spellbook.SlotCount];
        var pending = new List<uint>(chosen.Select(c => c.Spell.ActionId));

        if (keepSlots)
        {
            for (var i = 0; i < slots.Length && i < current.Length; i++)
            {
                if (current[i] != 0 && pending.Remove(current[i]))
                    slots[i] = current[i];
            }
        }

        var next = 0;
        for (var i = 0; i < slots.Length && next < pending.Count; i++)
        {
            if (slots[i] == 0)
                slots[i] = pending[next++];
        }

        return slots;
    }
}
