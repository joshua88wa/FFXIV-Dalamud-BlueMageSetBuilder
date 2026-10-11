using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace BlueMageSetBuilder;

/// <summary>Small wrappers for driving the game's own interface.</summary>
public static unsafe class GameUi
{
    /// <summary>The Blue Magic Spellbook window's addon name.</summary>
    private const string SpellbookAddon = "AOZNotebook";

    public static bool IsSpellbookOpen()
    {
        var addon = (AtkUnitBase*)RaptureAtkUnitManager.Instance()->GetAddonByName(SpellbookAddon);
        return addon != null && addon->IsVisible;
    }

    /// <summary>
    /// Opens the Blue Magic Spellbook, so newly applied spells can be dragged to a
    /// hotbar. Uses the game's own /bluespellbook command, and only when the window is
    /// not already showing, since the command toggles it.
    /// </summary>
    public static void OpenSpellbook()
    {
        if (!IsSpellbookOpen())
            SendTextCommand("/bluespellbook");
    }

    /// <summary>
    /// Runs a game text command as if typed in the chat box. Only ever called with
    /// commands built by this plugin, never with text from elsewhere.
    /// </summary>
    public static void SendTextCommand(string command)
    {
        var ui = UIModule.Instance();
        if (ui == null)
            return;

        Service.Log.Info($"Sending game command: {command}");
        var str = Utf8String.FromString(command);
        try
        {
            ui->ProcessChatBoxEntry(str);
        }
        finally
        {
            str->Dtor(true);
        }
    }
}
