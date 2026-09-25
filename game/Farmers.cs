using Godot;
using Hellwall.Sim;

namespace Hellwall.Game;

/// <summary>
/// The people on a Farm's fields, for the eye only: each working Farm's crew
/// walk out over the grass it gathers from, sow a tile, come back to water
/// it, and reap it when it's grown, and the crops show on the ground as they
/// go. The sim doesn't know: a Farm's food is its rate, as it always was.
/// Client state, not saved, so a loaded game's fields start bare.
/// </summary>
public sealed class Farmers
{
    public enum Work : byte { Walk, Plant, Water, Reap, Home }

    public sealed class Hand
    {
        public int Id;
        public float X, Y, PrevX, PrevY;
        public int Tile = -1;
        public Work Doing = Work.Walk;
        public float Timer;
    }

    sealed class Field
    {
        public readonly List<Hand> Hands = new();
        public int[] Tiles = [];
    }

    /// <summary>Per tile, how far its crop has come: 1 sown, 2 watered and growing, 3 ripe.</summary>
    public readonly Dictionary<int, byte> Crops = new();
    readonly Dictionary<int, float> _growing = new();
    readonly Dictionary<int, Field> _fields = new();
    readonly HashSet<int> _claimed = new();
    readonly Random _rng = new(12345);
    double _survey = 99;

    const float Speed = 1.4f;
    const float WorkSeconds = 3f;

    public IEnumerable<Hand> All => _fields.Values.SelectMany(f => f.Hands);

    public void Step(World world, float dt)
    {
        _survey += dt;
        if (_survey > 2)
        {
            _survey = 0;
            Survey(world);
        }
        int w = world.Terrain.Width;
        foreach (var (farmId, field) in _fields)
        {
            var farm = world.BuildingById(farmId);
            bool working = farm is { Active: true };
            for (int i = field.Hands.Count - 1; i >= 0; i--)
            {
                var h = field.Hands[i];
                h.PrevX = h.X;
                h.PrevY = h.Y;
                if (!working && h.Doing != Work.Home) { Release(h); h.Doing = Work.Home; }
                if (h.Doing == Work.Home)
                {
                    if (farm == null || WalkTo(h, farm.CentreX, farm.CentreY + farm.H / 2f, dt)) field.Hands.RemoveAt(i);
                    continue;
                }
                if (h.Tile < 0 && !Choose(field, h)) continue;
                if (h.Doing == Work.Walk)
                {
                    if (WalkTo(h, h.Tile % w + 0.85f, h.Tile / w + 0.85f, dt)) // stand at the tile's near corner, in front of the crop
                    {
                        h.Doing = Crops.GetValueOrDefault(h.Tile) switch { 3 => Work.Reap, 1 => Work.Water, _ => Work.Plant };
                        h.Timer = WorkSeconds + (float)_rng.NextDouble();
                    }
                    continue;
                }
                h.Timer -= dt;
                if (h.Timer > 0) continue;
                switch (h.Doing)
                {
                    case Work.Plant: Crops[h.Tile] = 1; break;
                    case Work.Water: Crops[h.Tile] = 2; _growing[h.Tile] = 20 + (float)_rng.NextDouble() * 20; break;
                    case Work.Reap: Crops.Remove(h.Tile); break;
                }
                Release(h);
                h.Doing = Work.Walk;
            }
        }
        // Watered crops ripen by themselves.
        foreach (var tile in _growing.Keys.ToList())
        {
            _growing[tile] -= dt;
            if (_growing[tile] > 0) continue;
            _growing.Remove(tile);
            if (Crops.ContainsKey(tile)) Crops[tile] = 3;
        }
    }

    /// <summary>Which farms work, the ground each has, and a crew for each one working. The fields of one gone go fallow.</summary>
    void Survey(World world)
    {
        var t = world.Terrain;
        var fieldTiles = new HashSet<int>();
        foreach (var b in world.Buildings)
        {
            if (b.Kind != BuildingKind.Farm || !b.Complete) continue;
            if (!_fields.TryGetValue(b.Id, out var field)) _fields[b.Id] = field = new Field();
            var tiles = new List<int>();
            int r = b.Def.GatherRadius;
            for (int y = (int)(b.CentreY - r); y <= (int)(b.CentreY + r); y++)
                for (int x = (int)(b.CentreX - r); x <= (int)(b.CentreX + r); x++)
                {
                    float dx = x + 0.5f - b.CentreX, dy = y + 0.5f - b.CentreY;
                    // A path a tile wide round the farm stays bare: a farmer there would stand behind its yard.
                    bool path = x >= b.X - 1 && x <= b.X + b.W && y >= b.Y - 1 && y <= b.Y + b.H;
                    if (path || dx * dx + dy * dy > r * r || !t.InBounds(x, y) || t.Get(x, y) != Tile.Grass || !world.IsHumanWalkable(x, y) || world.BuildingIdAt(x, y) != 0) continue;
                    tiles.Add(t.Index(x, y));
                }
            field.Tiles = tiles.ToArray();
            foreach (int i in tiles) fieldTiles.Add(i);
            if (!b.Active) continue;
            while (field.Hands.Count(h => h.Doing != Work.Home) < b.Def.Workers)
            {
                float x0 = b.CentreX + (float)(_rng.NextDouble() - 0.5), y0 = b.CentreY + b.H / 2f;
                field.Hands.Add(new Hand { Id = -(b.Id * 8 + field.Hands.Count + 1), X = x0, Y = y0, PrevX = x0, PrevY = y0 });
            }
        }
        foreach (var id in _fields.Keys.ToList())
            if (world.BuildingById(id) is not { Kind: BuildingKind.Farm } && _fields[id].Hands.Count == 0) _fields.Remove(id);
        foreach (var tile in Crops.Keys.ToList())
            if (!fieldTiles.Contains(tile)) { Crops.Remove(tile); _growing.Remove(tile); }
    }

    /// <summary>The next tile to see to: ripe first, then sown, then bare, never one another hand's already at.</summary>
    bool Choose(Field field, Hand h)
    {
        if (field.Tiles.Length == 0) return false;
        int best = -1, bestRank = -1;
        for (int n = 0; n < 6; n++) // a few random looks: fields are small, and a little wandering reads as natural
        {
            int tile = field.Tiles[_rng.Next(field.Tiles.Length)];
            if (_claimed.Contains(tile)) continue;
            int rank = Crops.GetValueOrDefault(tile) switch { 3 => 3, 1 => 2, 0 => 1, _ => 0 }; // growing (2): nothing to do
            if (rank > bestRank) { best = tile; bestRank = rank; }
        }
        if (best < 0 || bestRank == 0) return false;
        h.Tile = best;
        _claimed.Add(best);
        return true;
    }

    void Release(Hand h)
    {
        if (h.Tile >= 0) _claimed.Remove(h.Tile);
        h.Tile = -1;
    }

    static bool WalkTo(Hand h, float tx, float ty, float dt)
    {
        float dx = tx - h.X, dy = ty - h.Y, d = MathF.Sqrt(dx * dx + dy * dy), step = Speed * dt;
        if (d <= step) { h.X = tx; h.Y = ty; return true; }
        h.X += dx / d * step;
        h.Y += dy / d * step;
        return false;
    }
}
