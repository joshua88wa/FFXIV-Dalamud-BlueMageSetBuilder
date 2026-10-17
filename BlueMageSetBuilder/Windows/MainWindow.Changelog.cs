using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;

namespace BlueMageSetBuilder.Windows;

/// <summary>The Changelog page, built from CHANGELOG.md. Entries added since the last visit are marked New.</summary>
public sealed partial class MainWindow
{
    private bool changelogVisited;
    private int changelogUnseenAtVisit;

    private bool HasUnseenChangelog => Changelog.UnseenCount(this.config.LastSeenChangelog) > 0;

    private void DrawChangelog()
    {
        var entries = Changelog.Entries;

        // Count what is new once per visit, then mark it seen, so the New labels stay up
        // while the page is open and the sidebar dot goes away.
        if (!this.changelogVisited)
        {
            this.changelogVisited = true;
            this.changelogUnseenAtVisit = Changelog.UnseenCount(this.config.LastSeenChangelog);
            if (entries.Count > 0 && this.config.LastSeenChangelog != entries[0].Version)
            {
                this.config.LastSeenChangelog = entries[0].Version;
                this.config.Save();
            }
        }

        this.PageTitle("Changelog");
        if (entries.Count == 0)
        {
            ImGui.TextDisabled("This build has no changelog.");
            return;
        }

        if (!ImGui.BeginChild("##bms_changelog", Vector2.Zero))
        {
            ImGui.EndChild();
            return;
        }

        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            if (i > 0)
            {
                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();
            }

            ImGui.TextUnformatted(entry.Title);
            if (entry.Date.Length > 0)
            {
                ImGui.SameLine();
                ImGui.TextDisabled(entry.Date);
            }

            if (i < this.changelogUnseenAtVisit)
            {
                ImGui.SameLine();
                ImGui.TextColored(ImGuiColors.HealerGreen, "New");
            }

            ImGui.Spacing();
            foreach (var item in entry.Items)
            {
                ImGui.Bullet();
                ImGui.SameLine();
                ImGui.TextWrapped(item);
            }
        }

        ImGui.EndChild();
    }
}
