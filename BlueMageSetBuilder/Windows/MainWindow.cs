using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;

namespace BlueMageSetBuilder.Windows;

public sealed class MainWindow : Window
{
    private const float IconSize = 24f;

    private readonly Configuration config;
    private readonly Spellbook book;
    private readonly ProfileStore store;
    private readonly SetApplier applier;

    private string status = string.Empty;
    private Vector4 statusColor = ImGuiColors.DalamudWhite;
    private bool jumpToSettings;

    // Spellbook tab search. Deliberately not saved: a filter left on from last time
    // makes the list look like spells are missing.
    private string search = string.Empty;

    public MainWindow(Configuration config, Spellbook book, ProfileStore store, SetApplier applier)
        : base("Blue Mage Set Builder##BlueMageSetBuilder")
    {
        this.config = config;
        this.book = book;
        this.store = store;
        this.applier = applier;

        this.Size = new Vector2(680, 640);
        this.SizeCondition = ImGuiCond.FirstUseEver;
        this.SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(520, 400),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
    }

    public void OpenSettings()
    {
        this.IsOpen = true;
        this.jumpToSettings = true;
    }

    public void ShowResult(ApplyResult result)
    {
        this.status = result.Message;
        this.statusColor = result.Outcome switch
        {
            ApplyOutcome.Applied => ImGuiColors.HealerGreen,
            ApplyOutcome.AlreadyActive => ImGuiColors.DalamudWhite,
            ApplyOutcome.Pending => ImGuiColors.DalamudOrange,
            ApplyOutcome.Blocked => ImGuiColors.DalamudYellow,
            _ => ImGuiColors.DalamudRed,
        };
    }

    public override void Draw()
    {
        if (!Spellbook.IsLoggedIn)
        {
            ImGui.TextWrapped("Log in to a character to read your Blue Magic Spellbook.");
            return;
        }

        var profile = this.DrawProfilePicker();

        if (this.store.LoadError is { } err)
            ImGui.TextColored(ImGuiColors.DalamudRed, err);

        var learned = this.book.LearnedCount();
        ImGui.TextUnformatted($"Spells learned: {learned} of {this.book.Spells.Count}");
        if (!Spellbook.IsBlueMage)
        {
            ImGui.SameLine();
            ImGui.TextColored(ImGuiColors.DalamudYellow, "  (not on Blue Mage: you can browse, but not apply)");
        }

        ImGui.Spacing();

        if (!ImGui.BeginTabBar("##bms_tabs"))
            return;

        if (profile is not null && ImGui.BeginTabItem("Suggested set"))
        {
            this.DrawSuggested(profile);
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("Spellbook"))
        {
            this.DrawSpellbook();
            ImGui.EndTabItem();
        }

        var settingsFlags = this.jumpToSettings ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None;
        this.jumpToSettings = false;
        if (ImGui.BeginTabItem("Settings", settingsFlags))
        {
            this.DrawSettings();
            ImGui.EndTabItem();
        }

        ImGui.EndTabBar();
    }

    private Profile? DrawProfilePicker()
    {
        var profile = this.store.Find(this.config.SelectedProfile);
        if (profile is null)
        {
            ImGui.TextColored(ImGuiColors.DalamudRed, "No profiles are loaded. See the Settings tab.");
            return null;
        }

        ImGui.SetNextItemWidth(300 * ImGuiHelpers.GlobalScale);
        if (ImGui.BeginCombo("Content", profile.Name))
        {
            foreach (var p in this.store.Profiles)
            {
                var selected = p.Id == profile.Id;
                if (ImGui.Selectable($"{p.Name}##{p.Id}", selected))
                {
                    this.config.SelectedProfile = p.Id;
                    this.config.Save();
                    this.status = string.Empty;
                    profile = p;
                }

                if (selected)
                    ImGui.SetItemDefaultFocus();
            }

            ImGui.EndCombo();
        }

        if (!string.IsNullOrWhiteSpace(profile.Description))
        {
            ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.DalamudGrey);
            ImGui.TextWrapped(profile.Description);
            ImGui.PopStyleColor();
        }

        return profile;
    }

    private void DrawSuggested(Profile profile)
    {
        var built = this.applier.Build(profile);
        var active = this.book.GetActiveActionIds().ToHashSet();

        var reason = Blockers.HardBlockReason();
        if (reason is null && built.Chosen.Count == 0)
            reason = "None of your learned spells are in this profile.";

        // Buffs from spells that would move. The button names the buffs it will end, so
        // pressing it is an explicit choice to lose them.
        var blocking = reason is null ? this.applier.BlockingStatusesFor(profile) : new();
        var names = Blockers.Describe(blocking);
        var waits = blocking.Where(s => s.Method == EndMethod.Wait).ToList();
        var removable = blocking.Count > 0 && waits.Count == 0;
        if (reason is null && waits.Count > 0)
            reason = $"Wait for {Blockers.DescribeWaits(waits)} to wear off.";

        // With no buff to remove, cooldowns block the button outright and count down
        // live. With a buff to remove, the remove-and-apply flow waits for them itself.
        if (reason is null && blocking.Count == 0 && !this.applier.IsPending)
        {
            var cooldowns = this.applier.CooldownsInTheWay(profile);
            if (cooldowns.Count > 0)
                reason = $"Wait for cooldowns: {SetApplier.DescribeCooldowns(cooldowns)}";
        }

        var label = removable ? $"End {names} and apply" : "Apply this set";

        ImGui.BeginDisabled(reason is not null || this.applier.IsPending);
        if (ImGui.Button(label))
            this.applier.Request(profile, endBlockers: true, this.ShowResult);
        ImGui.EndDisabled();

        if (removable && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
        {
            var how = string.Join("\n", blocking.Select(s => s.Method == EndMethod.Recast
                ? $"  {s.Name}: recasts {s.Spell.Name}, which ends it. Stand still during the cast."
                : $"  {s.Name}: clicks it off."));
            ImGui.SetTooltip($"The game will not move a spell while its effect is on you.\n{how}\n" +
                             "Then it waits for the global cooldown and applies the set.");
        }

        ImGui.SameLine();
        if (reason is not null)
            ImGui.TextColored(ImGuiColors.DalamudYellow, reason);
        else if (this.status.Length > 0)
            ImGui.TextColored(this.statusColor, this.status);
        else
            ImGui.TextUnformatted($"{built.Chosen.Count} of {Spellbook.SlotCount} slots filled");

        ImGui.Spacing();

        if (!ImGui.BeginChild("##bms_suggested", Vector2.Zero))
        {
            ImGui.EndChild();
            return;
        }

        if (ImGui.BeginTable("##bms_chosen", 4, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.SizingFixedFit))
        {
            ImGui.TableSetupColumn("##icon", ImGuiTableColumnFlags.WidthFixed, IconSize * ImGuiHelpers.GlobalScale);
            ImGui.TableSetupColumn("Spell");
            ImGui.TableSetupColumn("Why", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Now");
            ImGui.TableHeadersRow();

            foreach (var c in built.Chosen)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                DrawIcon(c.Spell.Icon);
                SpellTooltip.ShowIfHovered(c.Spell, true);
                ImGui.TableNextColumn();
                ImGui.TextUnformatted($"{c.Spell.Number}. {c.Spell.Name}");
                SpellTooltip.ShowIfHovered(c.Spell, true);
                ImGui.TableNextColumn();
                if (c.IsFill)
                {
                    ImGui.TextDisabled("Extra:");
                    ImGui.SameLine();
                }

                ImGui.TextWrapped(c.Why);
                if (c.StandInFor is { } preferred)
                    ImGui.TextDisabled($"Standing in for {preferred.Name}, which you have not learned.");
                ImGui.TableNextColumn();
                if (active.Contains(c.Spell.ActionId))
                    ImGui.TextColored(ImGuiColors.HealerGreen, "active");
            }

            ImGui.EndTable();
        }

        if (built.Missing.Count > 0)
        {
            ImGui.Spacing();
            if (ImGui.CollapsingHeader($"Worth learning ({built.Missing.Count})###bms_missing", ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGui.TextDisabled("Spells this profile wants that you have not learned yet.");
                if (ImGui.BeginTable("##bms_missing_t", 4, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.SizingFixedFit))
                {
                    ImGui.TableSetupColumn("##icon", ImGuiTableColumnFlags.WidthFixed, IconSize * ImGuiHelpers.GlobalScale);
                    ImGui.TableSetupColumn("Spell");
                    ImGui.TableSetupColumn("Where to learn it");
                    ImGui.TableSetupColumn("Why", ImGuiTableColumnFlags.WidthStretch);
                    ImGui.TableHeadersRow();

                    foreach (var m in built.Missing)
                    {
                        ImGui.TableNextRow();
                        ImGui.TableNextColumn();
                        DrawIcon(m.Spell.Icon);
                        SpellTooltip.ShowIfHovered(m.Spell, false);
                        ImGui.TableNextColumn();
                        ImGui.TextUnformatted($"{m.Spell.Number}. {m.Spell.Name}");
                        SpellTooltip.ShowIfHovered(m.Spell, false);
                        ImGui.TableNextColumn();
                        ImGui.TextUnformatted(m.Spell.Where.Length > 0 ? m.Spell.Where : "-");
                        ImGui.TableNextColumn();
                        ImGui.TextWrapped(m.Why);
                    }

                    ImGui.EndTable();
                }
            }
        }

        ImGui.EndChild();
    }

    private void DrawSpellbook()
    {
        ImGui.SetNextItemWidth(260 * ImGuiHelpers.GlobalScale);
        ImGui.InputTextWithHint("##bms_search", "Search by name, number, place or creature", ref this.search, 64);
        if (this.search.Length > 0)
        {
            ImGui.SameLine();
            if (ImGui.Button("Clear##bms_search_clear"))
                this.search = string.Empty;
        }

        ImGui.SameLine();
        if (ImGui.Checkbox("Show spells I have not learned", ref this.config.ShowUnlearned))
            this.config.Save();

        ImGui.SameLine();
        ImGui.TextDisabled("(?)");
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Tick Exclude to keep a spell out of every suggested set, for example one you find awkward to use.");

        if (!ImGui.BeginChild("##bms_book", Vector2.Zero))
        {
            ImGui.EndChild();
            return;
        }

        if (ImGui.BeginTable("##bms_book_t", 6, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.SizingFixedFit))
        {
            ImGui.TableSetupColumn("##icon", ImGuiTableColumnFlags.WidthFixed, IconSize * ImGuiHelpers.GlobalScale);
            ImGui.TableSetupColumn("Spell");
            ImGui.TableSetupColumn("Rank");
            ImGui.TableSetupColumn("Learned");
            ImGui.TableSetupColumn("Where to learn it", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Exclude");
            ImGui.TableHeadersRow();

            var shown = 0;
            foreach (var spell in this.book.Spells)
            {
                var learned = this.book.IsLearned(spell);
                if (!learned && !this.config.ShowUnlearned)
                    continue;
                if (!MatchesSearch(spell, this.search))
                    continue;

                shown++;

                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                DrawIcon(spell.Icon);
                SpellTooltip.ShowIfHovered(spell, learned);
                ImGui.TableNextColumn();
                ImGui.TextUnformatted($"{spell.Number}. {spell.Name}");
                SpellTooltip.ShowIfHovered(spell, learned);
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(spell.Rank > 0 ? $"{spell.Rank} star" : "-");
                ImGui.TableNextColumn();
                if (learned)
                    ImGui.TextColored(ImGuiColors.HealerGreen, "Yes");
                else
                    ImGui.TextDisabled("No");
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(spell.Where.Length > 0 ? spell.Where : "-");
                ImGui.TableNextColumn();
                var excluded = this.config.Excluded.Contains(spell.Number);
                if (ImGui.Checkbox($"##ex{spell.Number}", ref excluded))
                {
                    if (excluded)
                        this.config.Excluded.Add(spell.Number);
                    else
                        this.config.Excluded.Remove(spell.Number);
                    this.config.Save();
                }
            }

            ImGui.EndTable();

            if (shown == 0)
            {
                ImGui.TextDisabled(this.search.Length > 0
                    ? $"No spells match \"{this.search}\"."
                    : "No spells to show.");
                if (this.search.Length > 0 && !this.config.ShowUnlearned)
                    ImGui.TextDisabled("Spells you have not learned are hidden. Tick the box above to include them.");
            }
        }

        ImGui.EndChild();
    }

    // A number matches the spellbook number exactly, so "1" finds Water Cannon rather
    // than every spell with a 1 in it. Anything else matches anywhere in the name,
    // where the spell is learned, or any creature or place that teaches it.
    private static bool MatchesSearch(BlueSpell spell, string search)
    {
        var term = search.Trim();
        if (term.Length == 0)
            return true;

        if (uint.TryParse(term.TrimStart('#'), out var number))
            return spell.Number == number;

        return spell.Name.Contains(term, StringComparison.OrdinalIgnoreCase)
               || spell.Source.Contains(term, StringComparison.OrdinalIgnoreCase)
               || spell.Sources.Any(s => s.Creature.Contains(term, StringComparison.OrdinalIgnoreCase)
                                      || s.Place.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private void DrawSettings()
    {
        if (ImGui.Checkbox("Open the Blue Magic Spellbook after applying a set", ref this.config.OpenSpellbookAfterApply))
            this.config.Save();
        ImGui.TextDisabled("So you can drag the new spells straight onto your hotbars.");

        ImGui.Spacing();
        if (ImGui.Checkbox("Keep spells in the same slot when applying", ref this.config.KeepSlots))
            this.config.Save();
        ImGui.TextDisabled("A spell that is already active and also in the new set stays where it is. Fewer spells\nmove, so fewer effects like Mighty Guard need turning off. Off: spells follow the\nprofile's order.");

        ImGui.Spacing();
        if (ImGui.Checkbox("Let /blusets apply turn off Mighty Guard, Mimicry and similar effects", ref this.config.AutoRemoveBlockers))
            this.config.Save();
        ImGui.TextDisabled("The game will not move a spell while its effect is on you. The button in the window\nalways names these and asks; this lets the command do the same without asking.\nOff: the command stops and tells you what is in the way.");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.TextUnformatted("Profiles");
        ImGui.TextDisabled($"In use: {this.store.LoadedFrom}");
        ImGui.TextWrapped("The built-in profiles are a starting point. Export them, edit Profiles.json in the config folder in any text editor, then press Reload. Your copy replaces the built-in one completely.");

        var userFileExists = File.Exists(ProfileStore.UserFilePath);
        var ctrl = ImGui.GetIO().KeyCtrl;

        ImGui.BeginDisabled(userFileExists && !ctrl);
        if (ImGui.Button(userFileExists ? "Overwrite my copy with the built-in profiles" : "Export the built-in profiles for editing"))
            this.Run(() => { this.store.ExportBuiltIn(); this.store.Load(); }, "Exported Profiles.json to the config folder.");
        ImGui.EndDisabled();
        if (userFileExists && !ctrl)
        {
            ImGui.SameLine();
            ImGui.TextDisabled("(hold Ctrl)");
        }

        if (ImGui.Button("Reload profiles"))
            this.Run(this.store.Load, "Profiles reloaded.");

        ImGui.SameLine();
        if (ImGui.Button("Open config folder"))
            this.Run(() => Process.Start(new ProcessStartInfo(Service.Interface.GetPluginConfigDirectory()) { UseShellExecute = true }), string.Empty);

        if (userFileExists)
        {
            ImGui.BeginDisabled(!ctrl);
            if (ImGui.Button("Delete my copy and use the built-in profiles"))
                this.Run(() => { this.store.DeleteUserFile(); this.store.Load(); }, "Back to the built-in profiles.");
            ImGui.EndDisabled();
            if (!ctrl)
            {
                ImGui.SameLine();
                ImGui.TextDisabled("(hold Ctrl)");
            }
        }

        if (this.status.Length > 0)
            ImGui.TextColored(this.statusColor, this.status);
    }

    private void Run(Action action, string success)
    {
        try
        {
            Directory.CreateDirectory(Service.Interface.GetPluginConfigDirectory());
            action();
            this.status = success;
            this.statusColor = ImGuiColors.HealerGreen;
        }
        catch (Exception ex)
        {
            Service.Log.Error(ex, "Settings action failed");
            this.status = ex.Message;
            this.statusColor = ImGuiColors.DalamudRed;
        }
    }

    private static void DrawIcon(uint icon)
    {
        var size = new Vector2(IconSize * ImGuiHelpers.GlobalScale);
        if (icon == 0)
        {
            ImGui.Dummy(size);
            return;
        }

        var wrap = Service.Textures.GetFromGameIcon(new GameIconLookup(icon)).GetWrapOrEmpty();
        ImGui.Image(wrap.Handle, size);
    }
}
