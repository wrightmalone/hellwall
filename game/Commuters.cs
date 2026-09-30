using Godot;
using Hellwall.Sim;

namespace Hellwall.Game;

/// <summary>
/// The town going to work, for the eye only: a crewed building's people (those no other system
/// shows: not farmers, fishers, hunters, or the sim's woodsmen and miners) walk from their homes
/// to it, go in for a while, and walk home again. Acolytes to the Shrine, scholars to the
/// Scriptorium, drill-masters to the Barracks, labourers to anything else with a crew; a new kind
/// of workplace gets labourers until it's given a trade here (Trade). They take real paths round
/// walls and buildings (through gates), only while the building is staffed, and hurry home when
/// its crew flee. The sim doesn't know: the work is its rate, as always. Client state, not saved.
/// </summary>
public sealed class Commuters
{
    public enum Doing : byte { Home, ToWork, AtWork, ToHome }

    public sealed class Commuter
    {
        public int Id, Work, HomeId;
        public float X, Y, PrevX, PrevY;
        public Doing Doing = Doing.Home;
        public float Timer;
        public string Sheet = "";
        public List<Vector2> Path = new();
        public int Step;
    }

    /// <summary>Walking pace (tiles a second), how many of a crew show, and how many in the whole town at most.</summary>
    const float Speed = 1.4f;
    const int PerBuilding = 3, Most = 150;
    /// <summary>How far (tiles) home can be from work, and how far a path may wander round the straight line.</summary>
    const int HomeReach = 45, PathMargin = 24;

    readonly Dictionary<int, List<Commuter>> _crews = new();
    readonly Random _rng = new(4242);
    double _survey = 99;
    int _nextId = -300000; // below the hunters' ids

    /// <summary>Those out walking (at home or at work they're inside, and not drawn).</summary>
    public IEnumerable<Commuter> Walking => _crews.Values.SelectMany(c => c).Where(c => c.Doing is Doing.ToWork or Doing.ToHome);

    /// <summary>
    /// The figure a workplace's people wear, or null if it has none (no crew), or another system
    /// already shows them. The one place to give a new kind of building its own trade.
    /// </summary>
    public static string? Trade(World world, Building b)
    {
        if (b.Def.Workers <= 0) return null;
        switch (b.Kind)
        {
            case BuildingKind.Farm or BuildingKind.Fishery or BuildingKind.Hunter: return null; // Farmers, Fishers, Hunters
            case BuildingKind.Shrine: return "res://art/baked/unit-town-acolyte.png";
            case BuildingKind.Scriptorium or BuildingKind.Observatory: return "res://art/baked/unit-town-scholar.png";
            case BuildingKind.Barracks: return "res://art/baked/unit-town-drillmaster.png";
        }
        if (world.HasCrew(b.Def)) return null; // the sim's own woodsmen and miners walk for it
        return "res://art/baked/unit-town-labourer.png";
    }

    public void Step(World world, float dt)
    {
        dt = MathF.Min(dt, 0.1f);
        _survey += dt;
        if (_survey > 2) { _survey = 0; Survey(world); }
        foreach (var (workId, crew) in _crews)
        {
            var work = world.BuildingById(workId);
            foreach (var c in crew)
            {
                c.PrevX = c.X;
                c.PrevY = c.Y;
                if (work == null) continue;
                bool working = work.Staffed && !work.Paused && !work.Possessed && !work.Fleeing;
                switch (c.Doing)
                {
                    case Doing.Home:
                        c.Timer -= dt;
                        if (c.Timer <= 0 && working) Set(world, c, Doing.ToWork, work);
                        break;
                    case Doing.ToWork:
                        if (!working) { Set(world, c, Doing.ToHome, work); break; } // turned back: the crew fled, or stood down
                        if (Walk(c, dt)) { c.Doing = Doing.AtWork; c.Timer = 6 + (float)_rng.NextDouble() * 8; }
                        break;
                    case Doing.AtWork:
                        c.Timer -= dt;
                        if (c.Timer <= 0 || work.Fleeing) Set(world, c, Doing.ToHome, work);
                        break;
                    case Doing.ToHome:
                        if (Walk(c, dt * (work.Fleeing ? 1.8f : 1))) { c.Doing = Doing.Home; c.Timer = 3 + (float)_rng.NextDouble() * 5; }
                        break;
                }
            }
        }
    }

    /// <summary>Start a walk: a path from where he is to the other end (the work's door, or home's).</summary>
    void Set(World world, Commuter c, Doing doing, Building work)
    {
        // A home that's since been built in (no open ground beside it) is swapped for one that isn't.
        var home = world.BuildingById(c.HomeId) ?? NearestHome(world, work, c.Id);
        if (home == null) { c.Doing = Doing.Home; c.Timer = 10; return; }
        c.HomeId = home.Id;
        // Out of a building (from any open tile beside it) or on from where he stands (turned back), to any open tile beside the other.
        bool fromInside = c.Doing is Doing.Home or Doing.AtWork;
        var fromBuilding = doing == Doing.ToWork ? home : work;
        var toBuilding = doing == Doing.ToWork ? work : home;
        var path = FindPath(world, fromInside ? ((int)fromBuilding.CentreX, (int)fromBuilding.CentreY) : ((int)c.X, (int)c.Y), toBuilding);
        if (path == null) { c.Doing = Doing.Home; c.Timer = 20; return; } // no way there today: stay in
        if (fromInside) { c.X = c.PrevX = path[0].X; c.Y = c.PrevY = path[0].Y; }
        c.Path = path;
        c.Step = 0;
        c.Doing = doing;
    }

    static bool Walk(Commuter c, float dt)
    {
        float left = Speed * dt;
        while (c.Step < c.Path.Count && left > 0)
        {
            var target = c.Path[c.Step];
            float dx = target.X - c.X, dy = target.Y - c.Y, d = MathF.Sqrt(dx * dx + dy * dy);
            if (d <= left) { c.X = target.X; c.Y = target.Y; left -= d; c.Step++; continue; }
            c.X += dx / d * left;
            c.Y += dy / d * left;
            left = 0;
        }
        return c.Step >= c.Path.Count;
    }

    /// <summary>A crew for each workplace that has one to show, homed near it; a workplace gone takes its crew with it.</summary>
    void Survey(World world)
    {
        int total = _crews.Values.Sum(c => c.Count);
        foreach (var b in world.Buildings)
        {
            if (!b.Complete || b.Possessed || Trade(world, b) is not { } sheet) continue;
            if (!_crews.TryGetValue(b.Id, out var crew)) _crews[b.Id] = crew = new List<Commuter>();
            int want = Math.Min(b.Def.Workers, PerBuilding);
            while (crew.Count < want && total < Most)
            {
                var home = NearestHome(world, b, crew.Count);
                crew.Add(new Commuter { Id = _nextId--, Work = b.Id, HomeId = home?.Id ?? 0, Sheet = sheet, Timer = (float)_rng.NextDouble() * 8 });
                total++;
            }
        }
        foreach (var id in _crews.Keys.ToList())
            if (world.BuildingById(id) is not { } b || Trade(world, b) == null) _crews.Remove(id);
    }

    /// <summary>A home near a workplace: the nearest houses first (a crew spread over the nearest six, so the walks aren't all next door), the Keep if there are none in reach.</summary>
    static Building? NearestHome(World world, Building work, int nth = 0)
    {
        var homes = world.Buildings
            .Where(b => b.Complete && !b.Possessed && b.Def.Housing > 0 && b.Kind != BuildingKind.Keep)
            .Select(b => (b, d: (b.CentreX - work.CentreX) * (b.CentreX - work.CentreX) + (b.CentreY - work.CentreY) * (b.CentreY - work.CentreY)))
            .Where(p => p.d <= HomeReach * HomeReach)
            .OrderBy(p => p.d).Take(6).ToList();
        if (homes.Count > 0) return homes[Math.Abs(nth) % homes.Count].b;
        return world.Buildings.FirstOrDefault(b => b.Kind == BuildingKind.Keep);
    }

    /// <summary>
    /// A walk from a tile into a building, the cheapest way over the town: open ground people can
    /// walk (a gate's way through) at 1 a step, other buildings' ground at 8 (a town packed tight
    /// still has its alleys, and this is only for the eye), walls, water and blocking terrain never.
    /// Eight ways without cutting a blocked corner, kept to a box round both ends. Tile centres, the
    /// first the start. Null if there's no way.
    /// </summary>
    static List<Vector2>? FindPath(World world, (int X, int Y) start, Building target)
    {
        var t = world.Terrain;
        int x0 = Math.Max(0, Math.Min(start.X, target.X) - PathMargin), y0 = Math.Max(0, Math.Min(start.Y, target.Y) - PathMargin);
        int x1 = Math.Min(t.Width - 1, Math.Max(start.X, target.X + target.W) + PathMargin), y1 = Math.Min(t.Height - 1, Math.Max(start.Y, target.Y + target.H) + PathMargin);
        if (start.X < x0 || start.Y < y0 || start.X > x1 || start.Y > y1) return null;
        int w = x1 - x0 + 1, h = y1 - y0 + 1;
        // What a step onto a tile costs: 0 can't be walked.
        int Cost(int x, int y)
        {
            if (world.IsHumanWalkable(x, y)) return 1;
            int id = world.BuildingIdAt(x, y);
            if (id == target.Id) return 1;
            if (id == 0 || world.BuildingById(id) is not { } b || b.IsWallLike) return 0;
            return 8;
        }
        var dist = new int[w * h];
        var came = new int[w * h];
        Array.Fill(dist, int.MaxValue);
        int s0 = (start.Y - y0) * w + start.X - x0;
        dist[s0] = 0;
        came[s0] = s0;
        var open = new PriorityQueue<int, int>();
        open.Enqueue(s0, 0);
        (int, int)[] steps = [(1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1)];
        int end = -1;
        while (open.TryDequeue(out int at, out int d))
        {
            if (d > dist[at]) continue;
            int ax = at % w, ay = at / w;
            if (world.BuildingIdAt(ax + x0, ay + y0) == target.Id) { end = at; break; }
            foreach (var (dx, dy) in steps)
            {
                int nx = ax + dx, ny = ay + dy;
                if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                int c = Cost(nx + x0, ny + y0);
                if (c == 0) continue;
                if (dx != 0 && dy != 0 && (Cost(ax + dx + x0, ay + y0) == 0 || Cost(ax + x0, ay + dy + y0) == 0)) continue; // no corner-cutting
                int nd = d + (dx != 0 && dy != 0 ? c * 14 / 10 : c);
                int next = ny * w + nx;
                if (nd >= dist[next]) continue;
                dist[next] = nd;
                came[next] = at;
                open.Enqueue(next, nd);
            }
        }
        if (end < 0) return null;
        var path = new List<Vector2>();
        for (int at = end; ; at = came[at])
        {
            path.Add(new Vector2(at % w + x0 + 0.5f, at / w + y0 + 0.5f));
            if (came[at] == at) break;
        }
        path.Reverse();
        return path;
    }
}
