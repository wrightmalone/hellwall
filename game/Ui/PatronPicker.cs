using Godot;
using Hellwall.Sim;

namespace Hellwall.Game;

/// <summary>
/// A patron saint answers: three blessings as cards, top centre, while the
/// sim holds an offer (World.PatronOffer). The game doesn't pause; the offer
/// waits. Choosing sends ChoosePatron.
/// </summary>
public partial class PatronPicker : PanelContainer
{
    public World World = null!;
    public Action<Command> Send = null!;

    HBoxContainer _cards = null!;
    string _shown = "";

    public override void _Ready()
    {
        AddThemeStyleboxOverride("panel", UiKit.Box(UiKit.Panel, UiKit.Gold, 8, 10));
        MouseFilter = MouseFilterEnum.Stop;
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 8);
        AddChild(box);
        var title = UiKit.Label("A patron saint answers your prayers. Choose one blessing:", 15, UiKit.Gold);
        box.AddChild(title);
        _cards = new HBoxContainer();
        _cards.AddThemeConstantOverride("separation", 8);
        box.AddChild(_cards);
        Visible = false;
    }

    public override void _Process(double delta)
    {
        var offer = World.PatronOffer;
        string key = string.Join(",", offer);
        if (key != _shown)
        {
            _shown = key;
            foreach (var c in _cards.GetChildren()) c.QueueFree();
            foreach (var id in offer)
            {
                var tech = World.Rules.Tech(id);
                var card = UiKit.TextButton($"{tech.Name}\n\n{tech.Description}", 13);
                card.CustomMinimumSize = new Vector2(210, 110);
                card.AutowrapMode = TextServer.AutowrapMode.WordSmart;
                card.Pressed += () => Send(new ChoosePatron(id));
                _cards.AddChild(card);
            }
            Visible = offer.Length > 0;
            Size = Vector2.Zero; // shrink to the new cards
        }
        if (Visible)
        {
            var screen = GetViewportRect().Size;
            Position = new Vector2((screen.X - Size.X) / 2, 180);
        }
    }
}
