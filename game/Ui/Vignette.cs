using Godot;

namespace Hellwall.Game;

/// <summary>A coloured glow round the edge of the screen that pulses and fades: something's badly wrong, look.</summary>
public partial class Vignette : Control
{
    Color _colour = new(1, 0.1f, 0.05f);
    double _left, _length = 1;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);
    }

    public void Flash(Color colour, double seconds)
    {
        _colour = colour;
        _left = _length = seconds;
    }

    public override void _Process(double delta)
    {
        if (_left <= 0) return;
        _left -= delta;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_left <= 0) return;
        float fade = (float)(_left / _length);
        float pulse = 0.65f + 0.35f * Mathf.Sin((float)(_length - _left) * 9f);
        var size = GetRect().Size;
        const int bands = 14;
        for (int i = 0; i < bands; i++)
        {
            float inset = i * 5;
            float a = 0.5f * fade * pulse * (1 - i / (float)bands);
            DrawRect(new Rect2(inset, inset, size.X - 2 * inset, size.Y - 2 * inset), new Color(_colour, a), filled: false, width: 5);
        }
    }
}
