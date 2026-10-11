using System;
using System.Linq;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using BlueMageSetBuilder.Windows;

namespace BlueMageSetBuilder;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/blusets";

    private readonly WindowSystem windowSystem = new("BlueMageSetBuilder");
    private readonly Configuration config;
    private readonly Spellbook book;
    private readonly ProfileStore store;
    private readonly SetApplier applier;
    private readonly MainWindow mainWindow;

    public Plugin(IDalamudPluginInterface pluginInterface)
    {
        pluginInterface.Create<Service>();

        this.config = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        this.config.Initialize(pluginInterface);

        this.book = new Spellbook();
        this.store = new ProfileStore();
        this.store.Load();
        this.applier = new SetApplier(this.config, this.book);

        this.mainWindow = new MainWindow(this.config, this.book, this.store, this.applier);
        this.windowSystem.AddWindow(this.mainWindow);

        Service.Commands.AddHandler(CommandName, new CommandInfo(this.OnCommand)
        {
            HelpMessage = "Open the set builder. " +
                          "/blusets apply <profile> applies a profile (ids: " +
                          string.Join(", ", this.store.Profiles.Select(p => p.Id)) + "). " +
                          "/blusets config opens settings. /blusets debug prints what the plugin can see.",
        });

        pluginInterface.UiBuilder.Draw += this.windowSystem.Draw;
        pluginInterface.UiBuilder.OpenMainUi += this.OpenMain;
        pluginInterface.UiBuilder.OpenConfigUi += this.mainWindow.OpenSettings;
    }

    public void Dispose()
    {
        Service.Interface.UiBuilder.Draw -= this.windowSystem.Draw;
        Service.Interface.UiBuilder.OpenMainUi -= this.OpenMain;
        Service.Interface.UiBuilder.OpenConfigUi -= this.mainWindow.OpenSettings;

        Service.Commands.RemoveHandler(CommandName);
        this.windowSystem.RemoveAllWindows();
        this.applier.Dispose();
    }

    private void OpenMain() => this.mainWindow.IsOpen = true;

    private void OnCommand(string command, string args)
    {
        var parts = args.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var verb = parts.Length > 0 ? parts[0].ToLowerInvariant() : string.Empty;

        switch (verb)
        {
            case "config":
            case "settings":
                this.mainWindow.OpenSettings();
                break;
            case "apply":
                this.ApplyFromCommand(parts.Length > 1 ? parts[1].Trim() : this.config.SelectedProfile);
                break;
            case "debug":
                this.PrintDebug();
                break;
            default:
                this.mainWindow.IsOpen = !this.mainWindow.IsOpen;
                break;
        }
    }

    // Matches on Id first, then on the start of the display name, so "/blusets apply tank"
    // and "/blusets apply Trial" both work from a macro.
    private void ApplyFromCommand(string name)
    {
        var profile = this.store.Profiles.FirstOrDefault(p => p.Id.Equals(name, StringComparison.OrdinalIgnoreCase))
                      ?? this.store.Profiles.FirstOrDefault(p => p.Name.StartsWith(name, StringComparison.OrdinalIgnoreCase));

        if (profile is null)
        {
            Service.Chat.PrintError($"[Blue Mage Set Builder] No profile called \"{name}\". Profiles: " +
                                    string.Join(", ", this.store.Profiles.Select(p => p.Id)));
            return;
        }

        // Called once with Pending if Mimicry has to come off first, then with the result.
        this.applier.Request(profile, this.config.AutoRemoveBlockers, result =>
        {
            this.mainWindow.ShowResult(result);

            if (result.Outcome is ApplyOutcome.Applied or ApplyOutcome.AlreadyActive or ApplyOutcome.Pending)
                Service.Chat.Print($"[Blue Mage Set Builder] {result.Message}");
            else
                Service.Chat.PrintError($"[Blue Mage Set Builder] {result.Message}");
        });
    }

    // A self-check for the parts that depend on reverse-engineered game memory: every
    // spell sitting in an active slot must have been learned, so if the learned check
    // disagrees with the active set, the learned check is wrong.
    private void PrintDebug()
    {
        void Line(string s) => Service.Chat.Print($"[BMS] {s}");

        Line($"Logged in: {Spellbook.IsLoggedIn}, on Blue Mage: {Spellbook.IsBlueMage}");
        Line($"Spells in game data: {this.book.Spells.Count}, learned: {this.book.LearnedCount()}");
        Line($"Profiles: {this.store.Profiles.Count} from {this.store.LoadedFrom}");
        Line($"Blocked by state: {Blockers.HardBlockReason() ?? "no"}");
        var activeSpells = this.book.GetActiveActionIds().Where(id => id != 0).Select(this.book.ByAction).OfType<BlueSpell>();
        var spellBuffs = Blockers.ActiveStatuses(activeSpells);
        Line($"Buffs from active spells: {(spellBuffs.Count == 0 ? "none" : string.Join(", ", spellBuffs.Select(b => $"{b.Name} ({b.Id}, from {b.Spell.Name}, ends by {b.Method})")))}");
        if (this.store.Find(this.config.SelectedProfile) is { } selected)
        {
            var blocking = this.applier.BlockingStatusesFor(selected);
            Line($"Blocking {selected.Name}: {(blocking.Count == 0 ? "nothing" : Blockers.Describe(blocking))}");
        }
        Line($"All buffs: {string.Join(", ", Blockers.AllStatusNames())}");

        var active = this.book.GetActiveActionIds();
        var names = active.Select((id, i) =>
        {
            if (id == 0)
                return $"{i + 1}:-";
            var s = this.book.ByAction(id);
            return s is null ? $"{i + 1}:?{id}" : $"{i + 1}:{s.Name}";
        });
        Line($"Active slots: {string.Join(", ", names)}");

        var contradictions = active
            .Where(id => id != 0)
            .Select(this.book.ByAction)
            .Where(s => s is not null && !this.book.IsLearned(s))
            .Select(s => s!.Name)
            .ToList();

        if (contradictions.Count == 0)
            Line("Learned check agrees with the active set.");
        else
            Service.Chat.PrintError($"[BMS] Active but reported as not learned: {string.Join(", ", contradictions)}. The learned check is wrong.");
    }
}
