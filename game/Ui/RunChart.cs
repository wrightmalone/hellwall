using Godot;
using Hellwall.Sim;

namespace Hellwall.Game;

/// <summary>
/// The run at a glance, on the end panel: colonists, soldiers and awake
/// demons over the days, each on its own scale (the horde dwarfs the town),
/// with the wave landings as ticks along the bottom. From the sim's own
/// history (sampled every half day), so a quickload or a fast-forward keeps it.
/// </summary>
public partial class RunChart : Control
{
    public World World = null!;

    static readonly Color People = new(0.94f, 0.76f, 0.36f), Army = new(0.4f, 0.75f, 1f), Horde = new(0.92f, 0.33f, 0.3f);

    List<(float Day, int Colonists, int Soldiers, int Horde)> _samples = new();

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(420, 130);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    /// <summary>Take the sim's history (it samples every half day, and saves carry it).</summary>
    public void Refresh()
    {
        float perDay = World.Rules.Survival.DaySeconds * Balance.TickHz;
        _samples = World.Stats.History.Select(h => (h.Tick / perDay + 1, h.Colonists, h.Soldiers, h.Horde)).ToList();
        QueueRedraw();
    }

    public override void _Draw()
    {
        var size = Size;
        const float legend = 18;
        var plot = new Rect2(0, legend, size.X, size.Y - legend - 4);
        DrawRect(plot, new Color(0, 0, 0, 0.3f));
        if (_samples.Count < 2) return;
        float days = Math.Max(1, _samples[^1].Day);
        Line(plot, days, s => s.Colonists, People);
        Line(plot, days, s => s.Soldiers, Army);
        Line(plot, days, s => s.Horde, Horde);
        if (World.Survival is { } sv)
            foreach (var w in sv.Waves)
                if (w.Landed)
                {
                    float x = plot.Position.X + (w.LandsAtTick / (World.Rules.Survival.DaySeconds * Balance.TickHz) + 1) / days * plot.Size.X;
                    DrawLine(new Vector2(x, plot.End.Y), new Vector2(x, plot.End.Y - 5), Horde, 1);
                }
        var font = ThemeDB.FallbackFont;
        float lx = 0;
        foreach (var (label, colour, peak) in new[] { ("colonists", People, _samples.Max(s => s.Colonists)), ("soldiers", Army, _samples.Max(s => s.Soldiers)), ("demons awake", Horde, _samples.Max(s => s.Horde)) })
        {
            DrawRect(new Rect2(lx, 5, 10, 3), colour);
            string text = $"{label} (peak {peak:N0})";
            DrawString(font, new Vector2(lx + 14, 12), text, fontSize: 12, modulate: UiKit.Muted);
            lx += 20 + font.GetStringSize(text, fontSize: 12).X;
        }
    }

    void Line(Rect2 plot, float days, Func<(float Day, int Colonists, int Soldiers, int Horde), int> value, Color colour)
    {
        float peak = Math.Max(1, _samples.Max(value));
        var points = _samples.Select(s => new Vector2(plot.Position.X + s.Day / days * plot.Size.X, plot.End.Y - value(s) / peak * (plot.Size.Y - 4))).ToArray();
        DrawPolyline(points, colour, 2);
    }
}
