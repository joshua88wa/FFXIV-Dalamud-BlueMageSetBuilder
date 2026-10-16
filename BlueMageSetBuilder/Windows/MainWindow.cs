using System;
using System.Collections.Generic;
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

public sealed partial class MainWindow : Window
{
    private const float IconSize = 24f;

    /// <summary>SelectedProfile value meaning "a Masked Carnivale stage, see SelectedStage".</summary>
    public const string CarnivaleId = "carnivale";
    private const string CarnivaleTitle = "The Masked Carnivale";

    private readonly Configuration config;
    private readonly Spellbook book;
    private readonly ProfileStore store;
    private readonly SetApplier applier;
    private readonly IReadOnlyList<CarnivaleStage> stages;

    private string status = string.Empty;
    private Vector4 statusColor = ImGuiColors.DalamudWhite;
    private bool jumpToMissing;

    // Spellbook page search. Deliberately not saved: a filter left on from last time
    // makes the list look like spells are missing.
    private string search = string.Empty;

    public MainWindow(Configuration config, Spellbook book, ProfileStore store, SetApplier applier, IReadOnlyList<CarnivaleStage> stages)
        : base("Blue Mage Set Builder##BlueMageSetBuilder")
    {
        this.config = config;
        this.book = book;
        this.store = store;
        this.applier = applier;
        this.stages = stages;

        this.Size = new Vector2(820, 640);
        this.SizeCondition = ImGuiCond.FirstUseEver;
        this.SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(580, 400),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
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

    /// <summary>
    /// The category tabs, one per profile Group in the order Profiles.json first uses
    /// them, then The Masked Carnivale. Under them is a short picker for that category:
    /// the role (Tank, Healer, DPS) where every profile in it names one, otherwise the
    /// profile, and the stage for the Carnivale. Returns the profile to build, or null
    /// when there is nothing to build.
    /// </summary>
    private Profile? DrawProfilePicker()
    {
        var general = this.store.Profiles.Where(p => p.Stage is null).ToList();
        if (general.Count == 0 && this.stages.Count == 0)
        {
            ImGui.TextColored(ImGuiColors.DalamudRed, "No profiles are loaded. See Settings.");
            return null;
        }

        var groups = general.GroupBy(GroupOf).ToList();
        var keys = groups.Select(g => g.Key).ToList();
        if (this.stages.Count > 0)
            keys.Add(CarnivaleId);

        var current = (this.config.SelectedProfile == CarnivaleId && this.stages.Count > 0) || general.Count == 0
            ? CarnivaleId
            : GroupOf(general.FirstOrDefault(p => p.Id == this.config.SelectedProfile) ?? general[0]);

        if (ImGui.BeginTabBar("##bms_groups"))
        {
            foreach (var key in keys)
            {
                var label = key == CarnivaleId ? CarnivaleTitle : key;

                // The saved choice decides the open tab every frame, so the tabs can never
                // disagree with what is shown below them. A click changes the saved choice.
                var flags = key == current ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None;
                var open = ImGui.BeginTabItem($"{label}###bms_group_{key}", flags);
                if (ImGui.IsItemClicked(ImGuiMouseButton.Left) && key != current)
                {
                    this.SelectGroup(key, groups);
                    current = key;
                }

                if (open)
                    ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }

        if (current == CarnivaleId)
            return this.DrawStagePicker();

        var members = groups.First(g => g.Key == current).ToList();
        var byRole = members.All(p => p.Role.Length > 0);
        if (byRole)
            members = members.OrderBy(RoleOrder).ToList();

        var profile = members.FirstOrDefault(p => p.Id == this.config.SelectedProfile) ?? DefaultIn(members);

        if (members.Count > 1)
        {
            ImGui.SetNextItemWidth(220 * ImGuiHelpers.GlobalScale);
            if (ImGui.BeginCombo(byRole ? "Role" : "Content", ShortName(profile)))
            {
                foreach (var p in members)
                {
                    var selected = p.Id == profile.Id;
                    if (ImGui.Selectable($"{ShortName(p)}##{p.Id}", selected))
                    {
                        this.Select(p.Id);
                        profile = p;
                    }

                    if (selected)
                        ImGui.SetItemDefaultFocus();
                }

                ImGui.EndCombo();
            }
        }

        if (!string.IsNullOrWhiteSpace(profile.Description))
        {
            ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.DalamudGrey);
            ImGui.TextWrapped(profile.Description);
            ImGui.PopStyleColor();
        }

        return profile;
    }

    private static string GroupOf(Profile p) => p.Group.Length > 0 ? p.Group : "Other";

    private static string ShortName(Profile p) => p.Role.Length > 0 ? p.Role : p.Name;

    // The order the game's party list uses.
    private static int RoleOrder(Profile p) => p.Role.ToLowerInvariant() switch
    {
        "tank" => 0,
        "healer" => 1,
        "dps" => 2,
        _ => 3,
    };

    // Blue Mage is a DPS job first, so a category opened for the first time starts there.
    private static Profile DefaultIn(List<Profile> members) =>
        members.FirstOrDefault(p => p.Role.Equals("DPS", StringComparison.OrdinalIgnoreCase)) ?? members[0];

    /// <summary>Opens a category on whatever was last chosen in it.</summary>
    private void SelectGroup(string key, List<IGrouping<string, Profile>> groups)
    {
        if (key == CarnivaleId)
        {
            this.Select(CarnivaleId);
            return;
        }

        var members = groups.First(g => g.Key == key).ToList();
        var remembered = this.config.GroupSelection.TryGetValue(key, out var id)
            ? members.FirstOrDefault(p => p.Id == id)
            : null;
        this.Select((remembered ?? DefaultIn(members)).Id);
    }

    private Profile? DrawStagePicker()
    {
        var stage = this.stages.FirstOrDefault(s => s.Number == this.config.SelectedStage) ?? this.stages[0];
        var profile = this.store.Profiles.FirstOrDefault(p => p.Stage == stage.Number);

        ImGui.SetNextItemWidth(300 * ImGuiHelpers.GlobalScale);
        if (ImGui.BeginCombo("Stage", stage.Label, ImGuiComboFlags.HeightLarge))
        {
            foreach (var s in this.stages)
            {
                var hasSet = this.store.Profiles.Any(p => p.Stage == s.Number);
                if (!hasSet)
                    ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.DalamudGrey);

                var selected = s.Number == stage.Number;
                if (ImGui.Selectable($"{s.Label}##stage{s.Number}", selected))
                {
                    this.config.SelectedStage = s.Number;
                    this.config.Save();
                    this.status = string.Empty;
                }

                if (!hasSet)
                    ImGui.PopStyleColor();
                if (selected)
                    ImGui.SetItemDefaultFocus();
            }

            ImGui.EndCombo();
        }

        // What the briefing screen would tell you: who is in each act and what they are
        // vulnerable to. Straight from game data, so it is there for every stage.
        ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.DalamudGrey);
        for (var i = 0; i < stage.Acts.Count; i++)
        {
            var enemies = string.Join(", ", stage.Acts[i].Select(e =>
                e.Vulnerabilities.Count > 0 ? $"{e.Name} (vulnerable to {string.Join(", ", e.Vulnerabilities)})" : e.Name));
            ImGui.TextWrapped($"Act {i + 1}: {enemies}");
        }

        if (profile is not null && !string.IsNullOrWhiteSpace(profile.Description))
            ImGui.TextWrapped(profile.Description);
        ImGui.PopStyleColor();

        if (profile is null)
        {
            ImGui.Spacing();
            ImGui.TextColored(ImGuiColors.DalamudYellow, "No spell list for this stage yet.");
            return null;
        }

        // Credit and link for stage spells that came from Blue Academy's guides.
        if (profile.SourceUrl.Length > 0)
        {
            ImGui.TextDisabled($"Stage spells: {CarnivaleSets.CreditName} ({CarnivaleSets.CreditSite}).");
            ImGui.SameLine();
            if (ImGui.SmallButton($"Open the stage guide##bms_guide{stage.Number}"))
                Dalamud.Utility.Util.OpenLink(profile.SourceUrl);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip($"{profile.SourceUrl}\nThe full strategy for this stage is in {CarnivaleSets.CreditName}'s guide.");
        }

        return profile;
    }

    private void Select(string id)
    {
        this.config.SelectedProfile = id;
        if (this.store.Profiles.FirstOrDefault(p => p.Id == id && p.Stage is null) is { } chosen)
            this.config.GroupSelection[GroupOf(chosen)] = id;
        this.config.Save();
        this.status = string.Empty;
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
        this.DrawLearningSummary(profile, built);
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
            if (this.jumpToMissing)
                ImGui.SetNextItemOpen(true);
            var open = ImGui.CollapsingHeader($"Worth learning ({built.Missing.Count})###bms_missing", ImGuiTreeNodeFlags.DefaultOpen);
            if (this.jumpToMissing)
            {
                ImGui.SetScrollHereY(0f);
                this.jumpToMissing = false;
            }

            if (open)
            {
                ImGui.TextDisabled("Spells this profile wants that you have not learned yet.");
                if (ImGui.BeginTable("##bms_missing_t", 4, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.SizingFixedFit))
                {
                    ImGui.TableSetupColumn("##icon", ImGuiTableColumnFlags.WidthFixed, IconSize * ImGuiHelpers.GlobalScale);
                    ImGui.TableSetupColumn("Spell");
                    // Both text columns stretch and wrap. With "Where to learn it" sized to its
                    // longest entry, a long place name left "Why" a few pixels wide.
                    ImGui.TableSetupColumn("Where to learn it", ImGuiTableColumnFlags.WidthStretch, 1f);
                    ImGui.TableSetupColumn("Why", ImGuiTableColumnFlags.WidthStretch, 1.4f);
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
                        ImGui.TextWrapped(m.Spell.Where.Length > 0 ? m.Spell.Where : "-");
                        ImGui.TableNextColumn();
                        ImGui.TextWrapped(m.Why);
                        if (m.CoveredBy is { } covered)
                            ImGui.TextDisabled($"{covered.Name} is in the set in its place.");
                        else if (profile.Stage is not null && m.Options.Count > 1)
                            ImGui.TextDisabled($"Any one of these will do: {string.Join(", ", m.Options.Select(o => o.Name))}.");
                    }

                    ImGui.EndTable();
                }
            }
        }

        ImGui.EndChild();
    }

    /// <summary>
    /// A line or two above the set saying whether anything it wants is not learned, so
    /// nobody has to scroll to the Worth learning list to find out. Required spells
    /// (Masked Carnivale) get named outright, since the stage may not be clearable
    /// without them.
    /// </summary>
    private void DrawLearningSummary(Profile profile, BuiltSet built)
    {
        if (profile.Picks.Count == 0)
            return;

        var what = profile.Stage is not null ? "stage" : "set";
        var unmet = built.UnmetRequired.ToList();

        if (unmet.Count > 0)
        {
            var names = string.Join("; ", unmet.Select(m => string.Join(" or ", m.Options.Select(o => o.Name))));
            var source = profile.SourceUrl.Length > 0 ? $" ({CarnivaleSets.CreditName} lists {(unmet.Count == 1 ? "it" : "them")} as required)" : string.Empty;
            ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.DalamudOrange);
            ImGui.TextWrapped($"You have not learned {(unmet.Count == 1 ? "a spell" : $"{unmet.Count} spells")} this {what} needs{source}: {names}.");
            ImGui.PopStyleColor();
        }

        if (built.RequiredExcluded.Count > 0)
        {
            var names = string.Join(", ", built.RequiredExcluded.Select(s => s.Name));
            ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.DalamudYellow);
            ImGui.TextWrapped($"Left out because you excluded {(built.RequiredExcluded.Count == 1 ? "it" : "them")} on the Spellbook page, but this {what} needs {(built.RequiredExcluded.Count == 1 ? "it" : "them")}: {names}.");
            ImGui.PopStyleColor();
        }

        if (built.Missing.Count > 0)
        {
            var others = built.Missing.Count - unmet.Count;
            var text = unmet.Count == 0
                ? $"{built.Missing.Count} spell{(built.Missing.Count == 1 ? "" : "s")} this {what} would use {(built.Missing.Count == 1 ? "is" : "are")} not learned yet."
                : others > 0
                    ? $"{others} more {(others == 1 ? "spell it recommends is" : "spells it recommends are")} not learned yet."
                    : string.Empty;
            if (text.Length > 0)
            {
                ImGui.TextColored(unmet.Count == 0 ? ImGuiColors.DalamudYellow : ImGuiColors.DalamudGrey, text);
                ImGui.SameLine();
            }

            if (ImGui.SmallButton("Where to learn them##bms_jump"))
                this.jumpToMissing = true;
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Jumps to the Worth learning list at the bottom.");
        }
        else if (built.RequiredExcluded.Count == 0)
        {
            ImGui.TextColored(ImGuiColors.HealerGreen, $"You have learned every spell this {what} calls for.");
        }
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
