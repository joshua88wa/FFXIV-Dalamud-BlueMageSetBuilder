using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Utility;

namespace BlueMageSetBuilder.Windows;

/// <summary>The About page: what the plugin is, where to get help, and who to thank.</summary>
public sealed partial class MainWindow
{
    private const string RepoUrl = "https://github.com/joshua88wa/FFXIV-Dalamud-BlueMageSetBuilder";
    private const string IssuesUrl = RepoUrl + "/issues";

    private long copiedAt = long.MinValue;

    private void DrawAbout()
    {
        var scale = ImGuiHelpers.GlobalScale;
        var iconSize = new Vector2(56f * scale);
        var icon = Service.Textures.GetFromManifestResource(typeof(MainWindow).Assembly, IconResource).GetWrapOrEmpty();

        ImGui.Image(icon.Handle, iconSize);
        ImGui.SameLine();
        ImGui.BeginGroup();
        this.PageTitle("Blue Mage Set Builder");
        var version = typeof(MainWindow).Assembly.GetName().Version;
        ImGui.TextDisabled(version is null ? "Version unknown" : $"Version {version}");
        ImGui.EndGroup();

        ImGui.Spacing();
        ImGui.TextWrapped("Reads which Blue Mage spells you have learned, builds a 24-spell active set for the content you are about to do, and applies it with one click.");

        ImGui.Spacing();
        if (ImGui.Button("GitHub page"))
            Util.OpenLink(RepoUrl);
        ImGui.SameLine();
        if (ImGui.Button("Report a problem or suggest something"))
            Util.OpenLink(IssuesUrl);

        Section("Commands");
        Command("/blusets", "Opens or closes this window.");
        Command("/blusets apply <profile>", "Applies a profile by id or the start of its name, for macros. /blusets apply stage 12 applies a Masked Carnivale stage.");
        Command("/blusets config", "Opens Settings.");
        Command("/blusets debug", "Prints what the plugin can see to chat.");

        Section("Reporting a problem");
        ImGui.TextWrapped("Please include the debug information, from the button below or /blusets debug. It shows whether you are on Blue Mage, how many spells you have learned, your active set and your current buffs, which is usually enough to see what went wrong.");
        if (ImGui.Button("Copy debug info"))
        {
            ImGui.SetClipboardText(this.DebugText());
            this.copiedAt = Environment.TickCount64;
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Copies it to the clipboard, ready to paste into an issue on GitHub.");

        ImGui.SameLine();
        if (ImGui.Button("Print it to chat"))
            GameUi.SendTextCommand("/blusets debug");

        if (Environment.TickCount64 - this.copiedAt < 3000)
        {
            ImGui.SameLine();
            ImGui.TextColored(ImGuiColors.HealerGreen, "Copied.");
        }

        Section("Credits");
        Credit(
            "Blue Academy",
            "Which spells each Masked Carnivale stage needs. Their stage guides explain how to actually clear each one.",
            ("mage.blue", CarnivaleSets.CreditUrl),
            ("Source", "https://github.com/RoarkGit/Blue-Academy"));
        Credit(
            "FFXIV Online Wiki",
            "Which creature teaches each spell, and which boss it is.",
            ("Blue Magic Spellbook page", "https://ffxiv.consolegameswiki.com/wiki/Blue_Magic_Spellbook"));
        Credit(
            "FFXIVClientStructs",
            "The game structures that make reading and applying spells possible.",
            ("GitHub", "https://github.com/aers/FFXIVClientStructs"));
        Credit(
            "Dalamud",
            "The plugin framework this runs on.",
            ("GitHub", "https://github.com/goatcorp/Dalamud"));

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        ImGui.TextDisabled("Free to use, change and share under the 0BSD licence. Written with the help of an LLM.");
    }

    private string DebugText()
    {
        var report = DebugReport.Build(this.config, this.book, this.store, this.applier);
        var version = typeof(MainWindow).Assembly.GetName().Version;
        var lines = new System.Collections.Generic.List<string> { $"Blue Mage Set Builder {version}" };
        lines.AddRange(report.Lines);
        if (report.Problem is { } problem)
            lines.Add(problem);
        return string.Join("\n", lines);
    }

    private static void Section(string title)
    {
        ImGui.Spacing();
        ImGui.Spacing();
        ImGui.TextUnformatted(title);
        ImGui.Separator();
        ImGui.Spacing();
    }

    private static void Command(string command, string what)
    {
        ImGui.TextUnformatted(command);
        ImGui.Indent();
        ImGui.PushTextWrapPos();
        ImGui.TextDisabled(what);
        ImGui.PopTextWrapPos();
        ImGui.Unindent();
    }

    private static void Credit(string name, string what, params (string Label, string Url)[] links)
    {
        ImGui.TextUnformatted(name);
        foreach (var (label, url) in links)
        {
            ImGui.SameLine();
            if (ImGui.SmallButton($"{label}##credit_{name}_{label}"))
                Util.OpenLink(url);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(url);
        }

        ImGui.Indent();
        ImGui.PushTextWrapPos();
        ImGui.TextDisabled(what);
        ImGui.PopTextWrapPos();
        ImGui.Unindent();
        ImGui.Spacing();
    }
}
