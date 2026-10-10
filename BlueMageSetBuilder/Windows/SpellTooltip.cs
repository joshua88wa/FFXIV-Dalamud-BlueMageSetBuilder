using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.ImGuiSeStringRenderer;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Text.ReadOnly;

namespace BlueMageSetBuilder.Windows;

/// <summary>
/// A hover tooltip modelled on the game's own: the combat tooltip (cast, recast, MP,
/// range, description) followed by the Blue Magic Spellbook page (type, aspect, rank,
/// lore, where to learn).
///
/// It is drawn in ImGui from the same sheets the game uses, not by summoning the game's
/// tooltip window. That window anchors to a node in the game's own UI tree, and this
/// plugin's window is not part of that tree, so showing it here would mean faking an
/// anchor, which is the kind of thing that breaks on patches.
/// </summary>
internal static unsafe class SpellTooltip
{
    private const float Width = 420f;
    private const float IconSize = 40f;

    /// <summary>Call straight after drawing something that should show the tooltip.</summary>
    public static void ShowIfHovered(BlueSpell spell, bool learned)
    {
        if (!ImGui.IsItemHovered())
            return;

        ImGui.BeginTooltip();
        try
        {
            Draw(spell, learned);
        }
        finally
        {
            ImGui.EndTooltip();
        }
    }

    private static void Draw(BlueSpell spell, bool learned)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var wrap = Width * scale;

        // Header: icon, name, number.
        var icon = Service.Textures.GetFromGameIcon(new GameIconLookup(spell.Icon)).GetWrapOrEmpty();
        ImGui.Image(icon.Handle, new Vector2(IconSize * scale));
        ImGui.SameLine();
        ImGui.BeginGroup();
        ImGui.TextUnformatted(spell.Name);
        ImGui.TextDisabled($"Spell  |  Blue Magic Spellbook #{spell.Number}");
        ImGui.EndGroup();

        // Fixes the tooltip width so wrapped text below has something to wrap against.
        ImGui.Dummy(new Vector2(wrap, 0));

        DrawNumbers(spell);

        if (!spell.Description.IsEmpty)
        {
            ImGui.Separator();
            DrawGameText(spell.Description, wrap);
        }

        if (!spell.Stats.IsEmpty || !spell.Lore.IsEmpty)
        {
            ImGui.Separator();
            if (!spell.Stats.IsEmpty)
                DrawGameText(spell.Stats, wrap);

            if (!spell.Lore.IsEmpty)
            {
                ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + wrap);
                ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.DalamudGrey);
                ImGui.TextWrapped(spell.Lore.ExtractText());
                ImGui.PopStyleColor();
                ImGui.PopTextWrapPos();
            }
        }

        ImGui.Separator();
        ImGui.TextDisabled("Where to learn:");
        ImGui.SameLine();
        ImGui.TextUnformatted(spell.Where.Length > 0 ? spell.Where : "-");

        // Every known source, since the game only names one place and many spells can
        // be learned in several. Boss position only appears where the wiki gives it.
        if (spell.Sources.Count > 0)
        {
            ImGui.TextDisabled("Learned from:");
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + wrap);
            foreach (var source in spell.Sources)
                ImGui.TextWrapped($"  {source.Describe()}");
            ImGui.PopTextWrapPos();
            ImGui.TextDisabled(SpellSources.Credit);
        }

        if (learned)
            ImGui.TextColored(ImGuiColors.HealerGreen, "Learned");
        else
            ImGui.TextColored(ImGuiColors.DalamudYellow, "Not learned yet");
    }

    // On Blue Mage these come from the game, so they include spell speed and match the
    // game's own tooltip. On any other job the game would answer for that job, so the
    // sheet's base values are shown instead and MP is left out.
    private static void DrawNumbers(BlueSpell spell)
    {
        int castMs, recastMs;
        int? mp = null;
        var live = Spellbook.IsBlueMage;

        if (live)
        {
            castMs = ActionManager.GetAdjustedCastTime(ActionType.Action, spell.ActionId);
            recastMs = ActionManager.GetAdjustedRecastTime(ActionType.Action, spell.ActionId);
            mp = ActionManager.GetActionCost(ActionType.Action, spell.ActionId, 0, 0, 0, 0);
        }
        else
        {
            castMs = spell.Cast100Ms * 100;
            recastMs = spell.Recast100Ms * 100;
        }

        if (!ImGui.BeginTable("##bms_tt_numbers", mp is null ? 4 : 5, ImGuiTableFlags.SizingStretchSame))
            return;

        Cell("Cast", castMs <= 0 ? "Instant" : $"{castMs / 1000f:0.00}s");
        Cell("Recast", $"{recastMs / 1000f:0.00}s");
        if (mp is { } cost)
            Cell("MP Cost", cost.ToString());
        Cell("Range", $"{Math.Max((int)spell.Range, 0)}y");
        Cell("Radius", $"{spell.Radius}y");
        ImGui.EndTable();

        if (!live)
            ImGui.TextDisabled("Base values. Switch to Blue Mage to see yours, with MP cost.");
    }

    private static void Cell(string label, string value)
    {
        ImGui.TableNextColumn();
        ImGui.TextDisabled(label);
        ImGui.TextUnformatted(value);
    }

    /// <summary>
    /// Draws a game string with its own colours and emphasis, after letting the game's
    /// string evaluator resolve any conditional text. Falls back to plain text if either
    /// step fails, so a malformed string costs formatting rather than the tooltip.
    /// </summary>
    private static void DrawGameText(ReadOnlySeString text, float wrap)
    {
        try
        {
            var evaluated = Service.SeStrings.Evaluate(text);
            ImGuiHelpers.SeStringWrapped(evaluated.Data.Span, new SeStringDrawParams { WrapWidth = wrap });
        }
        catch (Exception ex)
        {
            Service.Log.Debug(ex, "Falling back to plain text for a tooltip string");
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + wrap);
            ImGui.TextWrapped(text.ExtractText());
            ImGui.PopTextWrapPos();
        }
    }
}
