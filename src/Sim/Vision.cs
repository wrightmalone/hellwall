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

    /// <summary>
    /// Observatories at work chart the fog: with ScanEvery seconds of work
    /// (slowed by a sanctity shortfall) one reveals a patch of the ground it
    /// hasn't seen, on a grid of ScanPatch squares, choosing among the
    /// nearest few whose centres are in range. The choice hashes its id and
    /// the tick, not the rng, so a run with one draws the same demons as a
    /// run without. With nothing left to chart it waits (Exhausted), its
    /// work done, rather than wasting a chart.
    /// </summary>
    internal void StepObservatories(World world, float dt)
    {
        if (!Enabled) return;
        foreach (var b in world.BuildingList)
        {
            var def = b.Def;
            if (def.ScanEvery <= 0 || !b.Active || b.Fleeing) continue;
            if (b.ScanTimer < def.ScanEvery)
            {
                b.ScanTimer += dt * (float)world.Colony.Power;
                if (b.ScanTimer < def.ScanEvery) continue;
            }
            else if (b.Exhausted && world.Tick % Balance.TickHz != 0) continue; // waiting: look again each second
            var patch = NextPatch(b.CentreX, b.CentreY, def.ScanRange, def.ScanPatch, (uint)b.Id * 2654435761u ^ (uint)world.Tick);
            b.Exhausted = patch == null;
            if (patch is not { } at) continue;
            var (px, py) = at;
            b.ScanTimer = 0;
            int size = def.ScanPatch;
            bool fresh = false;
            for (int y = py; y < Math.Min(_h, py + size); y++)
                for (int x = px; x < Math.Min(_w, px + size); x++)
                    if (!Explored[y * _w + x]) { Explored[y * _w + x] = true; fresh = true; }
            if (fresh) Revision++;
            world.Emit(new GroundCharted(world.Tick, b.Id, px, py, size));
        }
    }

    /// <summary>
    /// The top-left tile of the patch an Observatory at (cx, cy) would chart next, or null
    /// when every patch in range is at least three-quarters known. Nearest first: any patch
    /// within one patch's width of the nearest candidate may be picked, by the hash.
    /// </summary>
    internal (int X, int Y)? NextPatch(float cx, float cy, float range, int size, uint hash)
    {
        if (size <= 0) return null;
        var picks = new List<(int X, int Y, float D)>();
        float nearest = float.MaxValue;
        int gx0 = Math.Max(0, (int)((cx - range) / size)), gx1 = Math.Min((_w - 1) / size, (int)((cx + range) / size));
        int gy0 = Math.Max(0, (int)((cy - range) / size)), gy1 = Math.Min((_h - 1) / size, (int)((cy + range) / size));
        for (int gy = gy0; gy <= gy1; gy++)
        {
            for (int gx = gx0; gx <= gx1; gx++)
            {
                int x0 = gx * size, y0 = gy * size;
                float dx = x0 + size / 2f - cx, dy = y0 + size / 2f - cy;
                float d = MathF.Sqrt(dx * dx + dy * dy);
                if (d > range) continue;
                int unknown = 0, all = 0;
                for (int y = y0; y < Math.Min(_h, y0 + size); y++)
                    for (int x = x0; x < Math.Min(_w, x0 + size); x++) { all++; if (!Explored[y * _w + x]) unknown++; }
                if (unknown * 4 < all) continue;
                picks.Add((x0, y0, d));
                nearest = MathF.Min(nearest, d);
            }
        }
        picks.RemoveAll(p => p.D > nearest + size);
        if (picks.Count == 0) return null;
        hash ^= hash >> 15; hash *= 0x2C1B3C6Du; hash ^= hash >> 12;
        var (px, py, _) = picks[(int)(hash % (uint)picks.Count)];
        return (px, py);
    }

    internal void Load(bool[] explored)
    {
        Array.Copy(explored, Explored, Explored.Length);
        Revision++;
    }
}
