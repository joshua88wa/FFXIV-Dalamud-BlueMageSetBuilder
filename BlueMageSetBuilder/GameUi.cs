using System.Numerics;
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
    /// Where the Blue Magic Spellbook is on screen, in pixels, when it is showing. The
    /// size is the window's root node scaled by the window's own scale.
    /// </summary>
    public static bool TryGetSpellbookRect(out Vector2 position, out Vector2 size)
    {
        position = Vector2.Zero;
        size = Vector2.Zero;

        var manager = RaptureAtkUnitManager.Instance();
        if (manager == null)
            return false;

        var addon = (AtkUnitBase*)manager->GetAddonByName(SpellbookAddon);
        if (addon == null || !addon->IsVisible || addon->RootNode == null)
            return false;

        position = new Vector2(addon->X, addon->Y);
        size = new Vector2(addon->RootNode->Width, addon->RootNode->Height) * addon->Scale;
        return size.X > 0 && size.Y > 0;
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
