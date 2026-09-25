using Godot;
using Hellwall.Sim;

namespace Hellwall.Game;

/// <summary>
/// The campaign's voice: a framed portrait, a name and the line, typed out,
/// top centre under the resource bar. Lines queue; each holds for a few
/// seconds after it's fully shown (longer lines longer), and a click skips
/// the typing, then dismisses. Portraits are unit and building pictures until
/// there's art for faces (DECISIONS.md).
/// </summary>
public partial class TalkingHead : PanelContainer
{
    readonly Queue<(SpeakerDef Who, string Text)> _queue = new();
    TextureRect _portrait = null!;
    Label _name = null!, _text = null!;
    string _full = "";
    float _shown, _hold;

    const float CharsPerSecond = 45;

    public override void _Ready()
    {
        AddThemeStyleboxOverride("panel", UiKit.Box(UiKit.Panel, UiKit.Gold, 8, 10));
        MouseFilter = MouseFilterEnum.Stop;
        CustomMinimumSize = new Vector2(620, 0);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);
        AddChild(row);

        var frame = new PanelContainer { CustomMinimumSize = new Vector2(92, 92) };
        frame.AddThemeStyleboxOverride("panel", UiKit.Box(new Color(0.2f, 0.17f, 0.13f), UiKit.Rim, 6, 4));
        _portrait = new TextureRect { ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, CustomMinimumSize = new Vector2(84, 84), TextureFilter = TextureFilterEnum.Linear };
        frame.AddChild(_portrait);
        row.AddChild(frame);

        var words = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _name = UiKit.Label("", 15, UiKit.Gold);
        _text = UiKit.Label("", 15);
        _text.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _text.CustomMinimumSize = new Vector2(480, 0);
        words.AddChild(_name);
        words.AddChild(_text);
        var hint = UiKit.Label("click to continue", 11, UiKit.Muted);
        hint.HorizontalAlignment = HorizontalAlignment.Right;
        words.AddChild(hint);
        row.AddChild(words);
        _ready = true;
        Visible = false;
        if (_queue.Count > 0) Next();
    }

    bool _ready, _active;

    /// <summary>Top centre, under the resource bar, whatever the window's size.</summary>
    void Place()
    {
        var screen = GetViewportRect().Size;
        Size = new Vector2(CustomMinimumSize.X, 0);
        Position = new Vector2((screen.X - Size.X) / 2, 44);
    }

    public void Say(SpeakerDef who, string text)
    {
        if (text.Length == 0) return;
        _queue.Enqueue((who, text));
        if (_ready && !_active) Next();
    }

    /// <summary>Stop talking: drop what's queued and hide.</summary>
    public void Silence()
    {
        _queue.Clear();
        Visible = _active = false;
    }

    void Next()
    {
        if (!_queue.TryDequeue(out var line)) { Visible = _active = false; return; }
        _portrait.Texture = Portrait(line.Who.Portrait);
        _name.Text = line.Who.Name;
        _full = line.Text;
        _shown = 0;
        _hold = 3.5f + _full.Length / 30f;
        _text.Text = "";
        Visible = _active = true;
    }

    static Texture2D Portrait(string kind) =>
        Enum.TryParse<UnitKind>(kind, out var u) ? UiKit.Unit(u)
        : Enum.TryParse<BuildingKind>(kind, out var b) ? UiKit.Building(b)
        : UiKit.Building(BuildingKind.Keep);

    public override void _Process(double delta)
    {
        if (!_active) return;
        Place();
        // Real time, not the sim's: at 4x the lines still read at a human pace.
        float dt = (float)(delta / Math.Max(0.01, Engine.TimeScale));
        if (_shown < _full.Length)
        {
            _shown = Math.Min(_full.Length, _shown + CharsPerSecond * dt);
            _text.Text = _full[..(int)_shown];
            return;
        }
        _hold -= dt;
        if (_hold <= 0) Next();
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) return;
        if (_shown < _full.Length) { _shown = _full.Length; _text.Text = _full; }
        else Next();
        AcceptEvent();
    }
}
