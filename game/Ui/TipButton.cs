using Godot;

namespace Hellwall.Game;

/// <summary>
/// A button whose tooltip can colour its words (BBCode), so a cost can show in red the part you
/// can't pay yet (UiKit.CostText). Every UiKit button is one of these.
/// </summary>
public partial class TipButton : Button
{
    public override GodotObject _MakeCustomTooltip(string forText)
    {
        var label = new RichTextLabel
        {
            BbcodeEnabled = true, FitContent = true, ScrollActive = false,
            AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(360, 0),
            Text = forText, MouseFilter = MouseFilterEnum.Ignore,
        };
        label.AddThemeFontSizeOverride("normal_font_size", 13);
        return label;
    }
}
