using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;

namespace BlueMageSetBuilder.Windows;

/// <summary>
/// Docking beside the game's Blue Magic Spellbook. When the spellbook opens, this
/// window opens too (if it was closed), sits against the chosen side at the
/// spellbook's height and follows it. It can shrink to a thin bar on that side. When
/// the spellbook closes, the window closes if the spellbook opened it, and otherwise
/// goes back to where it was.
/// </summary>
public sealed partial class MainWindow
{
    private const float DockGap = 2f;
    private const float DockBarWidth = 38f;
    private const float FreeMinWidth = 580f;
    private const float FreeMinHeight = 400f;

    private static readonly Vector2 DefaultSize = new(820, 640);

    private bool spellbookWasOpen;
    private bool openedBySpellbook;
    private bool docked;
    private Vector2 bookPos;
    private Vector2 bookSize;

    // Where the window was before it docked, to put it back afterwards.
    private Vector2 lastWindowPos;
    private Vector2 lastWindowSize;
    private Vector2 freePos;
    private Vector2 freeSize;
    private bool restoreFree;
    private bool releaseForced;
    private int pushedStyles;

    private readonly TitleBarButton collapseButton;

    private bool IsShrunk => this.docked && this.config.AttachCollapsed;

    /// <summary>Called every frame before the window system draws, open or not.</summary>
    public void TrackSpellbook()
    {
        var open = GameUi.TryGetSpellbookRect(out var pos, out var size);

        if (open && !this.spellbookWasOpen && this.config.AttachToSpellbook && !this.IsOpen)
        {
            this.IsOpen = true;
            this.openedBySpellbook = true;
        }
        else if (!open && this.spellbookWasOpen)
        {
            if (this.openedBySpellbook)
                this.IsOpen = false;
            this.openedBySpellbook = false;
        }

        // Closing this window by hand while the spellbook is open is respected until
        // the spellbook is next opened.
        if (!this.IsOpen)
            this.openedBySpellbook = false;

        this.spellbookWasOpen = open;

        var shouldDock = open && this.config.AttachToSpellbook && this.IsOpen;
        if (shouldDock && !this.docked)
        {
            this.freePos = this.lastWindowPos;
            this.freeSize = this.lastWindowSize;
        }
        else if (!shouldDock && this.docked)
        {
            this.restoreFree = true;
        }

        this.docked = shouldDock;
        this.bookPos = pos;
        this.bookSize = size;
    }

    public override void PreDraw()
    {
        var scale = ImGuiHelpers.GlobalScale;

        if (this.docked)
        {
            var collapsed = this.config.AttachCollapsed;
            var height = this.bookSize.Y;
            var width = collapsed
                ? DockBarWidth * scale
                : MathF.Max(this.lastWindowSize.X > 0 ? this.lastWindowSize.X : DefaultSize.X * scale, FreeMinWidth * scale);

            var origin = ImGui.GetMainViewport().Pos;
            var x = this.config.AttachSide == DockSide.Right
                ? this.bookPos.X + this.bookSize.X + DockGap * scale
                : this.bookPos.X - width - DockGap * scale;

            this.Position = origin + new Vector2(x, this.bookPos.Y);
            this.PositionCondition = ImGuiCond.Always;

            if (collapsed)
            {
                this.Size = new Vector2(DockBarWidth, height / scale);
                this.SizeCondition = ImGuiCond.Always;
                this.SizeConstraints = null;
                this.Flags = ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoTitleBar
                             | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;
                ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(4f, 8f) * scale);
                this.pushedStyles = 1;
            }
            else
            {
                // Height follows the spellbook; width stays the user's to drag.
                this.Size = null;
                this.SizeConstraints = new WindowSizeConstraints
                {
                    MinimumSize = new Vector2(FreeMinWidth, height / scale),
                    MaximumSize = new Vector2(float.MaxValue, height / scale),
                };
                this.Flags = ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoCollapse;
            }

            this.collapseButton.Icon = this.config.AttachSide == DockSide.Right ? FontAwesomeIcon.ChevronLeft : FontAwesomeIcon.ChevronRight;
            this.TitleBarButtons = collapsed ? new List<TitleBarButton>() : new List<TitleBarButton> { this.collapseButton };
            this.releaseForced = false;
            return;
        }

        this.TitleBarButtons = new List<TitleBarButton>();
        this.Flags = ImGuiWindowFlags.None;
        this.SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(FreeMinWidth, FreeMinHeight),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };

        if (this.restoreFree)
        {
            // Back to where it was before docking, for one frame, then let go.
            this.restoreFree = false;
            if (this.freeSize.X > 0)
            {
                this.Position = this.freePos;
                this.PositionCondition = ImGuiCond.Always;
                this.Size = this.freeSize / scale;
                this.SizeCondition = ImGuiCond.Always;
                this.releaseForced = true;
                return;
            }
        }

        if (this.releaseForced)
            this.releaseForced = false;

        this.Position = null;
        this.Size = DefaultSize;
        this.SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void PostDraw()
    {
        if (this.pushedStyles > 0)
        {
            ImGui.PopStyleVar(this.pushedStyles);
            this.pushedStyles = 0;
        }
    }

    private TitleBarButton CreateCollapseButton() => new()
    {
        Icon = FontAwesomeIcon.ChevronLeft,
        IconOffset = new Vector2(1.5f, 1f),
        Click = _ => this.SetCollapsed(true),
        ShowTooltip = () => ImGui.SetTooltip("Shrink to a bar beside the spellbook"),
        Priority = 1,
    };

    private void SetCollapsed(bool collapsed)
    {
        this.config.AttachCollapsed = collapsed;
        this.config.Save();
    }

    /// <summary>
    /// The shrunk window: a thin bar the spellbook's height, with a button to expand
    /// and one per page that expands straight to it.
    /// </summary>
    private void DrawDockBar()
    {
        var scale = ImGuiHelpers.GlobalScale;
        var icon = Service.Textures.GetFromManifestResource(typeof(MainWindow).Assembly, IconResource).GetWrapOrEmpty();
        var avail = ImGui.GetContentRegionAvail().X;
        var iconSize = new Vector2(MathF.Min(26f * scale, avail));

        Center(iconSize.X);
        ImGui.Image(icon.Handle, iconSize);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Blue Mage Set Builder");

        ImGui.Spacing();
        var expand = this.config.AttachSide == DockSide.Right ? FontAwesomeIcon.ChevronRight : FontAwesomeIcon.ChevronLeft;
        if (BarButton(0, expand, "Expand"))
            this.SetCollapsed(false);

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (BarButton(1, FontAwesomeIcon.ThLarge, "Layouts"))
            this.ExpandTo(Page.Layouts);
        if (BarButton(2, FontAwesomeIcon.BookOpen, "Spellbook"))
            this.ExpandTo(Page.Spellbook);
        if (BarButton(3, FontAwesomeIcon.Cog, "Settings"))
            this.ExpandTo(Page.Settings);
    }

    private void ExpandTo(Page target)
    {
        this.page = target;
        this.SetCollapsed(false);
    }

    private static bool BarButton(int id, FontAwesomeIcon icon, string tooltip)
    {
        var width = ImGui.GetContentRegionAvail().X;
        ImGui.PushID(id);
        var size = new Vector2(width, width);
        bool clicked;
        using (Service.Interface.UiBuilder.IconFontHandle.Push())
            clicked = ImGui.Button(icon.ToIconString(), size);
        ImGui.PopID();
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(tooltip);
        return clicked;
    }

    private static void Center(float width)
    {
        var avail = ImGui.GetContentRegionAvail().X;
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + MathF.Max(0f, (avail - width) * 0.5f));
    }

    private void DrawDockSettings()
    {
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.TextUnformatted("Blue Magic Spellbook");
        if (ImGui.Checkbox("Open beside the Blue Magic Spellbook", ref this.config.AttachToSpellbook))
            this.config.Save();
        ImGui.TextDisabled("This window opens with the spellbook, sits against it and moves with it. If the\nspellbook opened it, it closes again with the spellbook.");

        ImGui.BeginDisabled(!this.config.AttachToSpellbook);
        ImGui.TextUnformatted("Side:");
        ImGui.SameLine();
        if (ImGui.RadioButton("Right##bms_side_r", this.config.AttachSide == DockSide.Right))
        {
            this.config.AttachSide = DockSide.Right;
            this.config.Save();
        }

        ImGui.SameLine();
        if (ImGui.RadioButton("Left##bms_side_l", this.config.AttachSide == DockSide.Left))
        {
            this.config.AttachSide = DockSide.Left;
            this.config.Save();
        }

        if (ImGui.Checkbox("Shrink to a bar", ref this.config.AttachCollapsed))
            this.config.Save();
        ImGui.TextDisabled("Also done with the arrow in the title bar while docked. The bar has buttons to\nexpand it again or go straight to a page.");
        ImGui.EndDisabled();
    }
}
