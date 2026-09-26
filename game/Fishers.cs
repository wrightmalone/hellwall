using Godot;
using Hellwall.Sim;

namespace Hellwall.Game;

/// <summary>
/// A Fishery's boat, for the eye only: it sails out over the water to a
/// spot within reach, casts its net, hauls it in, sails home and lands the
/// catch. The sim doesn't know: a Fishery's food is its rate, as always.
/// Client state, not saved.
/// </summary>
public sealed class Fishers
{
    public enum Work : byte { Out, Cast, Haul, Back, Land }

    public sealed class Boat
    {
        public int Id;
        public int Home;
        public float X, Y, PrevX, PrevY;
        /// <summary>Which way it last moved: it keeps facing that way while it fishes.</summary>
        public float HeadX = 1, HeadY;
        public Work Doing = Work.Out;
        public float Timer;
        /// <summary>Water tiles to follow (the way out, or the way back), and how far along.</summary>
        public List<int> Path = new();
        public int Step;
        public int Dock = -1, Spot = -1;
    }

    public const float CastSeconds = 3.5f, HaulSeconds = 2.5f, LandSeconds = 1.5f;
    const float Speed = 1.1f;
    /// <summary>How far out over the water a boat goes, in steps from its dock.</summary>
    const int Reach = 10;

    readonly Dictionary<int, Boat> _boats = new();
    readonly Random _rng = new(4242);
    double _survey = 99;
    int _nextId = -100000; // below the farmers' ids, never a sim id

    public IEnumerable<Boat> All => _boats.Values;

    /// <summary>Catches just landed, for the "+" that rises over the Fishery: where, and how long ago.</summary>
    public readonly List<(Vector2 At, float Age)> Landed = new();

    public void Step(World world, float dt)
    {
        dt = MathF.Min(dt, 0.1f);
        _survey += dt;
        if (_survey > 2)
        {
            _survey = 0;
            Survey(world);
        }
        for (int i = Landed.Count - 1; i >= 0; i--)
        {
            var (at, age) = Landed[i];
            if (age + dt > 1.6f) Landed.RemoveAt(i);
            else Landed[i] = (at, age + dt);
        }
        int w = world.Terrain.Width;
        foreach (var boat in _boats.Values)
        {
            if (boat.X != boat.PrevX || boat.Y != boat.PrevY) { boat.HeadX = boat.X - boat.PrevX; boat.HeadY = boat.Y - boat.PrevY; }
            boat.PrevX = boat.X;
            boat.PrevY = boat.Y;
            var home = world.BuildingById(boat.Home);
            switch (boat.Doing)
            {
                case Work.Out:
                    if (Sail(boat, dt, w))
                    {
                        boat.Doing = Work.Cast;
                        boat.Timer = CastSeconds;
                    }
                    break;
                case Work.Cast:
                case Work.Haul:
                    boat.Timer -= dt;
                    if (boat.Timer > 0) break;
                    if (boat.Doing == Work.Cast) { boat.Doing = Work.Haul; boat.Timer = HaulSeconds; break; }
                    boat.Path.Reverse();
                    boat.Step = 0;
                    boat.Doing = Work.Back;
                    break;
                case Work.Back:
                    if (Sail(boat, dt, w))
                    {
                        boat.Doing = Work.Land;
                        boat.Timer = LandSeconds;
                        if (home != null) Landed.Add((new Vector2(home.CentreX, home.CentreY), 0));
                    }
                    break;
                case Work.Land:
                    boat.Timer -= dt;
                    if (boat.Timer <= 0 && home is { Active: true }) SetOut(world, boat);
                    break;
            }
        }
    }

    /// <summary>A boat for each working Fishery with water to fish; the boat of one gone or idle goes too.</summary>
    void Survey(World world)
    {
        foreach (var b in world.Buildings)
        {
            if (b.Kind != BuildingKind.Fishery || !b.Active || _boats.ContainsKey(b.Id)) continue;
            int dock = DockOf(world, b);
            if (dock < 0) continue;
            int w = world.Terrain.Width;
            var boat = new Boat { Id = _nextId--, Home = b.Id, Dock = dock, X = dock % w + 0.5f, Y = dock / w + 0.5f, Doing = Work.Land, Timer = (float)_rng.NextDouble() * 2 };
            boat.PrevX = boat.X;
            boat.PrevY = boat.Y;
            _boats[b.Id] = boat;
        }
        foreach (var id in _boats.Keys.ToList())
            if (world.BuildingById(id) is not { Kind: BuildingKind.Fishery, Active: true } && _boats[id].Doing == Work.Land) _boats.Remove(id);
    }

    /// <summary>The water nearest the Fishery's middle, within its reach: where its boat ties up.</summary>
    static int DockOf(World world, Building b)
    {
        var t = world.Terrain;
        int best = -1, r = b.Def.GatherRadius;
        float bestD = float.MaxValue;
        for (int y = b.Y - r; y < b.Y + b.H + r; y++)
            for (int x = b.X - r; x < b.X + b.W + r; x++)
            {
                if (!t.InBounds(x, y) || t.Get(x, y) != Tile.Water) continue;
                float d = (x + 0.5f - b.CentreX) * (x + 0.5f - b.CentreX) + (y + 0.5f - b.CentreY) * (y + 0.5f - b.CentreY);
                if (d < bestD) { bestD = d; best = t.Index(x, y); }
            }
        return best;
    }

    /// <summary>Out again: to a spot on the water a few to Reach steps from the dock, by water all the way.</summary>
    void SetOut(World world, Boat boat)
    {
        var t = world.Terrain;
        int w = t.Width;
        var from = new Dictionary<int, int> { [boat.Dock] = -1 };
        var depth = new Dictionary<int, int> { [boat.Dock] = 0 };
        var queue = new Queue<int>();
        queue.Enqueue(boat.Dock);
        var far = new List<int>();
        while (queue.Count > 0)
        {
            int i = queue.Dequeue();
            if (depth[i] >= 4) far.Add(i);
            if (depth[i] >= Reach) continue;
            int x = i % w, y = i / w;
            foreach (var (ox, oy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                int nx = x + ox, ny = y + oy;
                if (!t.InBounds(nx, ny) || t.Get(nx, ny) != Tile.Water) continue;
                int n = t.Index(nx, ny);
                if (from.ContainsKey(n)) continue;
                from[n] = i;
                depth[n] = depth[i] + 1;
                queue.Enqueue(n);
            }
        }
        if (far.Count == 0) far = from.Keys.ToList();
        boat.Spot = far[_rng.Next(far.Count)];
        boat.Path.Clear();
        for (int c = boat.Spot; c != -1; c = from[c]) boat.Path.Add(c);
        boat.Path.Reverse();
        boat.Step = 0;
        boat.Doing = Work.Out;
    }

    /// <summary>Along the path, to its end: true on arrival.</summary>
    static bool Sail(Boat boat, float dt, int w)
    {
        float step = Speed * dt;
        while (step > 0 && boat.Step < boat.Path.Count)
        {
            float tx = boat.Path[boat.Step] % w + 0.5f, ty = boat.Path[boat.Step] / w + 0.5f;
            float dx = tx - boat.X, dy = ty - boat.Y, d = MathF.Sqrt(dx * dx + dy * dy);
            if (d <= step) { boat.X = tx; boat.Y = ty; boat.Step++; step -= d; }
            else { boat.X += dx / d * step; boat.Y += dy / d * step; step = 0; }
        }
        return boat.Step >= boat.Path.Count;
    }
}
