namespace Hellwall.Sim;

/// <summary>
/// What the colony has seen (Explored, sticky) and sees now (Visible), per
/// tile, refreshed twice a second from every building, soldier and woodsman.
/// It decides nothing in the fight: the horde and the bot don't care. It
/// gates building (only on explored ground) and tells the client what to draw.
/// Off (everything seen) outside survival runs and missions, and when
/// fog.enabled is false.
/// </summary>
public sealed class Vision
{
    public readonly bool Enabled;
    public readonly bool[] Explored;
    public readonly bool[] Visible;
    /// <summary>Bumped whenever a tile is first explored, so the client knows to redraw.</summary>
    public int Revision { get; private set; }
    readonly int _w, _h;

    public Vision(int width, int height, bool enabled)
    {
        _w = width;
        _h = height;
        Enabled = enabled;
        Explored = new bool[width * height];
        Visible = new bool[width * height];
        if (!enabled)
        {
            Array.Fill(Explored, true);
            Array.Fill(Visible, true);
        }
    }

    public bool IsExplored(int x, int y) => !Enabled || (x >= 0 && y >= 0 && x < _w && y < _h && Explored[y * _w + x]);
    public bool IsVisible(float x, float y) => !Enabled || (x >= 0 && y >= 0 && x < _w && y < _h && Visible[(int)y * _w + (int)x]);

    internal void Step(World world)
    {
        if (!Enabled || world.Tick % 10 != 0) return;
        Array.Clear(Visible);
        var fog = world.Rules.Fog;
        foreach (var b in world.BuildingList)
        {
            if (b.Possessed) continue;
            float sight = MathF.Max(b.W, b.H) / 2f + fog.BuildingSight;
            if (b.Def.Weapon is { Range: > 0 } weapon) sight = MathF.Max(sight, weapon.Range + fog.TowerExtra);
            if (b.Kind == BuildingKind.Keep) sight = MathF.Max(sight, fog.StartReveal / 2f);
            See(b.CentreX, b.CentreY, sight);
        }
        foreach (var u in world.UnitList) See(u.X, u.Y, MathF.Max(fog.UnitSight, u.Def.Weapon.Range + 2));
        foreach (var m in world.WoodsmanList) See(m.X, m.Y, fog.WoodsmanSight);
    }

    /// <summary>The start: the ground round the Keep is known before anything has looked.</summary>
    internal void Reveal(float x, float y, float radius)
    {
        if (!Enabled) return;
        See(x, y, radius);
    }

    void See(float cx, float cy, float r)
    {
        int x0 = Math.Max(0, (int)(cx - r)), x1 = Math.Min(_w - 1, (int)(cx + r));
        int y0 = Math.Max(0, (int)(cy - r)), y1 = Math.Min(_h - 1, (int)(cy + r));
        float r2 = r * r;
        bool fresh = false;
        for (int y = y0; y <= y1; y++)
        {
            float dy = y + 0.5f - cy;
            int row = y * _w;
            for (int x = x0; x <= x1; x++)
            {
                float dx = x + 0.5f - cx;
                if (dx * dx + dy * dy > r2) continue;
                Visible[row + x] = true;
                if (!Explored[row + x]) { Explored[row + x] = true; fresh = true; }
            }
        }
        if (fresh) Revision++;
    }

    internal void Load(bool[] explored)
    {
        Array.Copy(explored, Explored, Explored.Length);
        Revision++;
    }
}
