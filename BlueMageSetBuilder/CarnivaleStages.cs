using System;
using System.Collections.Generic;
using System.Linq;
using Lumina.Excel.Sheets;

namespace BlueMageSetBuilder;

/// <summary>An enemy in one act of a stage, with what the briefing says it is vulnerable to.</summary>
public sealed record CarnivaleEnemy(string Name, IReadOnlyList<string> Vulnerabilities);

/// <summary>One Masked Carnivale stage, entirely from game data, in the client's language.</summary>
public sealed record CarnivaleStage(int Number, string Name, IReadOnlyList<IReadOnlyList<CarnivaleEnemy>> Acts)
{
    public string Label => $"Stage {this.Number}: {this.Name}";
}

/// <summary>
/// Reads the 32 stages from the game: names from ContentFinderCondition (content type
/// 27, The Masked Carnivale), enemies from AOZContent's acts via AOZArrangement and
/// AOZContentBriefingBNpc, which is the same data the in-game briefing screen shows.
/// </summary>
public static class CarnivaleStages
{
    private const uint CarnivaleContentType = 27;

    // Stage N is instance content 35000 + N.
    private const uint FirstInstanceContent = 35001;

    public static List<CarnivaleStage> Load()
    {
        var result = new List<CarnivaleStage>();
        try
        {
            var briefings = Service.Data.GetExcelSheet<AOZContent>().ToDictionary(c => (int)c.Order, c => c);

            foreach (var cfc in Service.Data.GetExcelSheet<ContentFinderCondition>())
            {
                if (cfc.ContentType.RowId != CarnivaleContentType || cfc.Content.RowId < FirstInstanceContent)
                    continue;

                var number = (int)(cfc.Content.RowId - FirstInstanceContent + 1);
                var name = cfc.Name.ExtractText();
                if (name.Length > 0)
                    name = char.ToUpperInvariant(name[0]) + name[1..];

                var acts = briefings.TryGetValue(number, out var content)
                    ? ReadActs(content)
                    : new List<IReadOnlyList<CarnivaleEnemy>>();

                result.Add(new CarnivaleStage(number, name, acts));
            }
        }
        catch (Exception ex)
        {
            // A missing or renamed sheet costs the Carnivale picker, not the plugin.
            Service.Log.Warning(ex, "Could not read Masked Carnivale stages");
        }

        return result.OrderBy(s => s.Number).ToList();
    }

    // Each act's fight type says what its row id points at: 1 is a group of ordinary
    // enemies in the AOZArrangement subrow sheet, 2 and 3 are a boss in AOZBoss. Reading
    // every act as a group gives the wrong enemies for boss stages (checked against the
    // stage names: stage 3 is Zipacna, 25 Azulmagia, 32 Goldor).
    private static List<IReadOnlyList<CarnivaleEnemy>> ReadActs(AOZContent content)
    {
        var arrangements = Service.Data.GetSubrowExcelSheet<AOZArrangement>();
        var bosses = Service.Data.GetExcelSheet<AOZBoss>();
        var acts = new List<IReadOnlyList<CarnivaleEnemy>>();

        var actRows = new[]
        {
            (Id: content.Act1.RowId, Type: (int)content.Act1FightType),
            (Id: content.Act2.RowId, Type: (int)content.Act2FightType),
            (Id: content.Act3.RowId, Type: (int)content.Act3FightType),
        };

        foreach (var (actId, fightType) in actRows)
        {
            if (actId == 0)
                continue;

            var npcs = new List<AOZContentBriefingBNpc>();
            if (fightType == 1)
            {
                if (arrangements.TryGetRow(actId, out var rows))
                {
                    foreach (var arrangement in rows)
                    {
                        if (arrangement.AOZContentBriefingBNpc.ValueNullable is { } npc)
                            npcs.Add(npc);
                    }
                }
            }
            else if (bosses.GetRowOrDefault(actId)?.Boss.ValueNullable is { } boss)
            {
                npcs.Add(boss);
            }

            var enemies = new List<CarnivaleEnemy>();
            foreach (var npc in npcs)
            {
                var name = npc.BNpcName.ValueNullable?.Singular.ExtractText() ?? string.Empty;
                if (name.Length == 0 || enemies.Any(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                    continue;

                enemies.Add(new CarnivaleEnemy(char.ToUpperInvariant(name[0]) + name[1..], Vulnerabilities(npc)));
            }

            if (enemies.Count > 0)
                acts.Add(enemies);
        }

        return acts;
    }

    private static List<string> Vulnerabilities(AOZContentBriefingBNpc npc)
    {
        var list = new List<string>();
        if (npc.SlowVuln) list.Add("slow");
        if (npc.PetrificationVuln) list.Add("petrify");
        if (npc.ParalysisVuln) list.Add("paralysis");
        if (npc.InterruptionVuln) list.Add("interrupt");
        if (npc.BlindVuln) list.Add("blind");
        if (npc.StunVuln) list.Add("stun");
        if (npc.SleepVuln) list.Add("sleep");
        if (npc.BindVuln) list.Add("bind");
        if (npc.HeavyVuln) list.Add("heavy");
        if (npc.FlatOrDeathVuln) list.Add("instant death");
        return list;
    }
}
