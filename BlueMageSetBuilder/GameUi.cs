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
    /// Where the Blue Magic Spellbook is drawn on screen, in pixels, when it is showing:
    /// the box around every visible image, frame and text node, using the positions the
    /// game itself computed for this frame. The window's root node is not used, because
    /// its size need not match what is drawn: other plugins add parts to this window,
    /// and on some setups (a different UI scale) the root box was visibly off. Falls back
    /// to the root node if no drawn parts are found.
    /// </summary>
    public static bool TryGetSpellbookRect(out Vector2 position, out Vector2 size)
    {
        position = Vector2.Zero;
        size = Vector2.Zero;

        var addon = Spellbook();
        if (addon == null)
            return false;

        if (TryGetDrawnBounds(addon, out position, out size))
            return true;

        return TryGetRootRect(addon, out position, out size);
    }

    /// <summary>Root node box and drawn box, for /blusets debug when docking is off.</summary>
    public static string DescribeSpellbookRects()
    {
        var addon = Spellbook();
        if (addon == null)
            return "not open";

        var root = TryGetRootRect(addon, out var rp, out var rs) ? Box(rp, rs) : "none";
        var drawn = TryGetDrawnBounds(addon, out var dp, out var ds, out var parts) ? $"{Box(dp, ds)} from {parts} parts" : "none";
        return $"root {root} (addon scale {addon->Scale:0.###}), drawn {drawn}";

        static string Box(Vector2 p, Vector2 s) => $"{p.X:0},{p.Y:0} {s.X:0}x{s.Y:0}";
    }

    private static AtkUnitBase* Spellbook()
    {
        var manager = RaptureAtkUnitManager.Instance();
        if (manager == null)
            return null;

        var addon = (AtkUnitBase*)manager->GetAddonByName(SpellbookAddon);
        return addon != null && addon->IsVisible && addon->RootNode != null ? addon : null;
    }

    private static bool TryGetRootRect(AtkUnitBase* addon, out Vector2 position, out Vector2 size)
    {
        position = new Vector2(addon->X, addon->Y);
        size = new Vector2(addon->RootNode->Width, addon->RootNode->Height) * addon->Scale;
        return size.X > 0 && size.Y > 0;
    }

    private static bool TryGetDrawnBounds(AtkUnitBase* addon, out Vector2 position, out Vector2 size) => TryGetDrawnBounds(addon, out position, out size, out _);

    private static bool TryGetDrawnBounds(AtkUnitBase* addon, out Vector2 position, out Vector2 size, out int parts)
    {
        position = Vector2.Zero;
        size = Vector2.Zero;
        parts = 0;

        var min = new Vector2(float.MaxValue);
        var max = new Vector2(float.MinValue);
        var list = addon->UldManager.NodeList;
        for (var i = 0; i < addon->UldManager.NodeListCount; i++)
        {
            var node = list[i];
            if (node == null || node->Width == 0 || node->Height == 0)
                continue;

            // Parts that draw something: images, frames, text, and components (the window
            // frame, buttons and the Active Actions panel are components, measured by their
            // own box). Resource and collision nodes are layout boxes and can be larger than
            // the window looks.
            var isComponent = (ushort)node->Type >= 1000;
            if (!isComponent && node->Type is not (NodeType.Image or NodeType.NineGrid or NodeType.Text or NodeType.Counter))
                continue;

            // Visible only if it and every parent are, and its on-screen size is its own
            // size times every scale up the chain.
            var visible = true;
            var scale = Vector2.One;
            for (var n = node; n != null; n = n->ParentNode)
            {
                if (!n->IsVisible())
                {
                    visible = false;
                    break;
                }

                scale *= new Vector2(n->ScaleX, n->ScaleY);
            }

            if (!visible)
                continue;

            var topLeft = new Vector2(node->ScreenX, node->ScreenY);
            var bottomRight = topLeft + (new Vector2(node->Width, node->Height) * scale);
            min = Vector2.Min(min, topLeft);
            max = Vector2.Max(max, bottomRight);
            parts++;
        }

        if (min.X >= max.X || min.Y >= max.Y)
            return false;

        position = min;
        size = max - min;
        return true;
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
