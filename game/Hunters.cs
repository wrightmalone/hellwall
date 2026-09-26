using Godot;
using Hellwall.Sim;

namespace Hellwall.Game;

/// <summary>
/// A Hunter's lodge crew, for the eye only: each walks out to the edge of the
/// woods it hunts, looses arrows into the trees, and comes home carrying the
/// kill ("+ meat" over the lodge). The sim doesn't know: the lodge's food is
/// its rate, as always. Client state, not saved.
/// </summary>
public sealed class Hunters
{
    public enum Work : byte { Out, Shoot, Home, Rest }

    public sealed class Hunter
    {
        public int Id;
        public int Lodge;
        public float X, Y, PrevX, PrevY;
        public Work Doing = Work.Rest;
        public float Timer;
        /// <summary>Where he stands to shoot, and the tree he shoots into.</summary>
        public float StandX, StandY;
        public int Tree = -1;
    }

    public const float ShootSeconds = 3f;
    const float Speed = 1.5f;

    readonly Dictionary<int, List<Hunter>> _crews = new();
    readonly Random _rng = new(777);
    double _survey = 99;
    int _nextId = -200000; // below the boats' and farmers' ids

    public IEnumerable<Hunter> All => _crews.Values.SelectMany(c => c);
    public readonly List<(Vector2 At, float Age)> Brought = new();

    public void Step(World world, float dt)
    {
        dt = MathF.Min(dt, 0.1f);
        _survey += dt;
        if (_survey > 2) { _survey = 0; Survey(world); }
        for (int i = Brought.Count - 1; i >= 0; i--)
        {
            var (at, age) = Brought[i];
            if (age + dt > 1.6f) Brought.RemoveAt(i);
            else Brought[i] = (at, age + dt);
        }
        int w = world.Terrain.Width;
        foreach (var (lodgeId, crew) in _crews)
        {
            var lodge = world.BuildingById(lodgeId);
            foreach (var h in crew)
            {
                h.PrevX = h.X;
                h.PrevY = h.Y;
                if (lodge == null) continue;
                float doorX = lodge.CentreX, doorY = lodge.Y + lodge.H + 0.3f;
                // Demons close, and no wall between: drop everything and run home.
                if (lodge.Fleeing && h.Doing is Work.Out or Work.Shoot) h.Doing = Work.Home;
                float pace = lodge.Fleeing ? 1.8f : 1;
                switch (h.Doing)
                {
                    case Work.Rest:
                        h.Timer -= dt;
                        if (h.Timer <= 0 && lodge.Active && !lodge.Fleeing && Choose(world, lodge, h)) h.Doing = Work.Out;
                        break;
                    case Work.Out:
                        if (WalkTo(h, h.StandX, h.StandY, dt)) { h.Doing = Work.Shoot; h.Timer = ShootSeconds + (float)_rng.NextDouble(); }
                        break;
                    case Work.Shoot:
                        h.Timer -= dt;
                        if (h.Timer <= 0) h.Doing = Work.Home;
                        break;
                    case Work.Home:
                        if (WalkTo(h, doorX, doorY, dt * pace))
                        {
                            Brought.Add((new Vector2(lodge.CentreX, lodge.CentreY), 0));
                            h.Doing = Work.Rest;
                            h.Timer = 2 + (float)_rng.NextDouble() * 2;
                        }
                        break;
                }
            }
        }
    }

    /// <summary>A crew for each working lodge; a lodge gone takes its crew with it.</summary>
    void Survey(World world)
    {
        foreach (var b in world.Buildings)
        {
            if (b.Kind != BuildingKind.Hunter || !b.Complete) continue;
            if (!_crews.TryGetValue(b.Id, out var crew)) _crews[b.Id] = crew = new List<Hunter>();
            while (crew.Count < b.Def.Workers)
            {
                float x = b.CentreX, y = b.Y + b.H + 0.3f;
                crew.Add(new Hunter { Id = _nextId--, Lodge = b.Id, X = x, Y = y, PrevX = x, PrevY = y, Timer = (float)_rng.NextDouble() * 3 });
            }
        }
        foreach (var id in _crews.Keys.ToList())
            if (world.BuildingById(id) is not { Kind: BuildingKind.Hunter }) _crews.Remove(id);
    }

    /// <summary>Somewhere on open ground at the edge of the woods the lodge hunts, and a tree beside it to shoot into.</summary>
    bool Choose(World world, Building lodge, Hunter h)
    {
        var t = world.Terrain;
        int r = lodge.Def.GatherRadius;
        for (int tries = 0; tries < 30; tries++)
        {
            int x = (int)lodge.CentreX + _rng.Next(-r, r + 1), y = (int)lodge.CentreY + _rng.Next(-r, r + 1);
            if (!t.InBounds(x, y) || !world.IsHumanWalkable(x, y)) continue;
            foreach (var (ox, oy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (-1, -1) })
            {
                int fx = x + ox, fy = y + oy;
                if (!t.InBounds(fx, fy) || t.Get(fx, fy) != Tile.Forest) continue;
                h.StandX = x + 0.5f;
                h.StandY = y + 0.5f;
                h.Tree = t.Index(fx, fy);
                return true;
            }
        }
        return false;
    }

    static bool WalkTo(Hunter h, float tx, float ty, float dt)
    {
        float dx = tx - h.X, dy = ty - h.Y, d = MathF.Sqrt(dx * dx + dy * dy), step = Speed * dt;
        if (d <= step) { h.X = tx; h.Y = ty; return true; }
        h.X += dx / d * step;
        h.Y += dy / d * step;
        return false;
    }
}
