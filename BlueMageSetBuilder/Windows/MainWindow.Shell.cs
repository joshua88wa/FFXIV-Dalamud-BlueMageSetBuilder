using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.GameFonts;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Interface.Utility;

namespace BlueMageSetBuilder.Windows;

/// <summary>
/// The window frame: a sidebar of pages on the left, the selected page on the right.
/// Plain ImGui throughout, so it follows the user's Dalamud theme; the only custom
/// drawing is the icon, label and accent bar on each sidebar row.
/// </summary>
public sealed partial class MainWindow : IDisposable
{
    private enum Page
    {
        Layouts,
        Spellbook,
        Settings,
        Changelog,
        About,
    }

    private const float SidebarWidth = 158f;
    private const float SidebarCompactWidth = 46f;
    private const float SidebarRowHeight = 36f;

    // Below this much room the sidebar shows icons only, with the names as tooltips.
    private const float CompactBelow = 640f;

    private const string IconResource = "BlueMageSetBuilder.icon.png";

    private Page page = Page.Layouts;

    // The game's own Axis font, a size up, for page titles.
    private IFontHandle? headingFont;

    public void OpenSettings()
    {
        this.IsOpen = true;
        this.page = Page.Settings;
    }

    public void Dispose()
    {
        this.headingFont?.Dispose();
        this.headingFont = null;
    }

    public override void Draw()
    {
        if (this.IsShrunk)
        {
            this.DrawDockBar();
            return;
        }

        this.lastWindowPos = ImGui.GetWindowPos();
        this.lastWindowSize = ImGui.GetWindowSize();

        if (!Spellbook.IsLoggedIn)
        {
            ImGui.TextWrapped("Log in to a character to read your Blue Magic Spellbook.");
            return;
        }

        var scale = ImGuiHelpers.GlobalScale;
        var compact = ImGui.GetContentRegionAvail().X < CompactBelow * scale;

        this.DrawSidebar(compact);
        ImGui.SameLine();

        if (this.page != Page.Changelog)
            this.changelogVisited = false;

        if (ImGui.BeginChild("##bms_page", Vector2.Zero))
        {
            switch (this.page)
            {
                case Page.Layouts:
                    this.DrawLayoutsPage();
                    break;
                case Page.Spellbook:
                    this.PageTitle("Spellbook");
                    this.DrawLearnedLine();
                    ImGui.Spacing();
                    this.DrawSpellbook();
                    break;
                case Page.Settings:
                    this.PageTitle("Settings");
                    this.DrawSettings();
                    break;
                case Page.Changelog:
                    this.DrawChangelog();
                    break;
                case Page.About:
                    this.DrawAbout();
                    break;
            }
        }

        ImGui.EndChild();
    }

    private void DrawLayoutsPage()
    {
        this.PageTitle("Layouts");

        var profile = this.DrawProfilePicker();
        if (this.store.LoadError is { } err)
            ImGui.TextColored(ImGuiColors.DalamudRed, err);

        this.DrawLearnedLine();
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (profile is not null)
            this.DrawSuggested(profile);
        else
            ImGui.TextDisabled("Choose what you are about to do from Content above.");
    }

    private void DrawLearnedLine()
    {
        // Not being on Blue Mage only matters for applying, and the Layouts page already
        // says so beside the Apply button, so it is not repeated here.
        ImGui.TextUnformatted($"Spells learned: {this.book.LearnedCount()} of {this.book.Spells.Count}");
    }

    private void DrawSidebar(bool compact)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var width = (compact ? SidebarCompactWidth : SidebarWidth) * scale;

        // A shade off the window background so the sidebar reads as its own panel,
        // taken from the theme rather than a fixed colour.
        ImGui.PushStyleColor(ImGuiCol.ChildBg, ImGui.GetColorU32(ImGuiCol.FrameBg, 0.55f));
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 6f * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(6f, 8f) * scale);
        var open = ImGui.BeginChild("##bms_sidebar", new Vector2(width, 0), false, ImGuiWindowFlags.AlwaysUseWindowPadding | ImGuiWindowFlags.NoScrollbar);
        ImGui.PopStyleVar(2);
        ImGui.PopStyleColor();

        if (open)
        {
            this.DrawSidebarHeader(compact);
            ImGui.Spacing();

            this.SidebarEntry(Page.Layouts, FontAwesomeIcon.ThLarge, "Layouts", compact);
            this.SidebarEntry(Page.Spellbook, FontAwesomeIcon.BookOpen, "Spellbook", compact);
            this.SidebarEntry(Page.Settings, FontAwesomeIcon.Cog, "Settings", compact);

            // Changelog and About sit at the bottom, apart from the working pages.
            var row = SidebarRowHeight * scale;
            var bottom = ImGui.GetWindowHeight() - ImGui.GetStyle().WindowPadding.Y - (row * 2) - ImGui.GetStyle().ItemSpacing.Y;
            if (ImGui.GetCursorPosY() < bottom)
                ImGui.SetCursorPosY(bottom);
            this.SidebarEntry(Page.Changelog, FontAwesomeIcon.History, "Changelog", compact, this.HasUnseenChangelog);
            this.SidebarEntry(Page.About, FontAwesomeIcon.InfoCircle, "About", compact);
        }

        ImGui.EndChild();
    }

    private void DrawSidebarHeader(bool compact)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var size = new Vector2(32f * scale);
        var icon = Service.Textures.GetFromManifestResource(typeof(MainWindow).Assembly, IconResource).GetWrapOrEmpty();

        if (compact)
        {
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + MathF.Max(0f, (ImGui.GetContentRegionAvail().X - size.X) * 0.5f));
            ImGui.Image(icon.Handle, size);
            return;
        }

        var start = ImGui.GetCursorPos();
        ImGui.SetCursorPos(start + new Vector2(4f * scale, 0f));
        ImGui.Image(icon.Handle, size);

        // Name in two lines beside the icon, vertically centred on it.
        var line = ImGui.GetTextLineHeight();
        var textX = start.X + 4f * scale + size.X + 8f * scale;
        var textY = start.Y + (size.Y - line * 2f) * 0.5f;
        ImGui.SetCursorPos(new Vector2(textX, textY));
        ImGui.TextUnformatted("Blue Mage");
        ImGui.SetCursorPos(new Vector2(textX, textY + line));
        ImGui.TextDisabled("Set Builder");
        ImGui.SetCursorPos(new Vector2(start.X, start.Y + size.Y + 4f * scale));
    }

    private void SidebarEntry(Page target, FontAwesomeIcon icon, string label, bool compact, bool dot = false)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var selected = this.page == target;
        var height = SidebarRowHeight * scale;
        var start = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;

        if (ImGui.Selectable($"##bms_nav_{label}", selected, ImGuiSelectableFlags.None, new Vector2(width, height)))
            this.page = target;
        if (compact && ImGui.IsItemHovered())
            ImGui.SetTooltip(label);

        var drawList = ImGui.GetWindowDrawList();
        var accent = ImGui.GetColorU32(ImGuiCol.CheckMark);

        // The selected page gets a short bar on its left edge, like the Auto plugins.
        if (selected)
        {
            var inset = height * 0.25f;
            drawList.AddRectFilled(
                start + new Vector2(0f, inset),
                start + new Vector2(3f * scale, height - inset),
                accent,
                1.5f * scale);
        }

        var iconBox = 20f * scale;
        var iconX = compact ? start.X + (width - iconBox) * 0.5f : start.X + 12f * scale;
        using (Service.Interface.UiBuilder.IconFontHandle.Push())
        {
            var glyph = icon.ToIconString();
            var glyphSize = ImGui.CalcTextSize(glyph);
            drawList.AddText(
                new Vector2(iconX + (iconBox - glyphSize.X) * 0.5f, start.Y + (height - glyphSize.Y) * 0.5f),
                selected ? accent : ImGui.GetColorU32(ImGuiCol.TextDisabled),
                glyph);
        }

        // Something new behind this entry, such as changelog entries not seen yet.
        if (dot)
        {
            drawList.AddCircleFilled(
                new Vector2(iconX + iconBox, start.Y + (height * 0.3f)),
                3.5f * scale,
                ImGui.GetColorU32(ImGuiColors.DalamudOrange));
        }

        if (compact && dot && ImGui.IsItemHovered())
            ImGui.SetTooltip($"{label} (new)");

        if (!compact)
        {
            var textSize = ImGui.CalcTextSize(label);
            drawList.AddText(
                new Vector2(iconX + iconBox + 10f * scale, start.Y + (height - textSize.Y) * 0.5f),
                ImGui.GetColorU32(ImGuiCol.Text),
                label);
        }
    }

    private void PageTitle(string title)
    {
        this.headingFont ??= Service.Interface.UiBuilder.FontAtlas.NewGameFontHandle(new GameFontStyle(GameFontFamilyAndSize.Axis18));

        if (this.headingFont.Available)
        {
            using (this.headingFont.Push())
                ImGui.TextUnformatted(title);
        }
        else
        {
            ImGui.TextUnformatted(title);
        }

        ImGui.Spacing();
    }
}
