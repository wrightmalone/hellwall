namespace Hellwall.Sim;

/// <summary>
/// Seeded terrain: two fields of value noise (elevation and moisture) mapped to
/// tiles, with a guaranteed clearing around the centre for the Keep.
///
/// The noise lattice is an integer hash of (seed, x, y) rather than draws from
/// the World's Rng, so terrain never depends on how many numbers anything else
/// has drawn. This is a placeholder until phase 5's procedural maps.
/// </summary>
public enum MapKind : byte
{
    /// <summary>Open ground, scattered lakes, rock and woods.</summary>
    Plains,
    /// <summary>Much more water: the land runs between lakes, and the lakes make chokepoints.</summary>
    Lakes,
    /// <summary>Rock ridges with passes between them. Stone is plentiful; room to build isn't.</summary>
    Highlands,
    /// <summary>Deep forest: wood everywhere, little open ground, and demons slowed in the trees.</summary>
    Wildwood,
}

public static class MapGen
{
    /// <summary>Noise thresholds per map kind: below Water is water, above Rock is rock, moisture above Forest is forest.</summary>
    static (double Water, double Rock, double Forest) Thresholds(MapKind kind) => kind switch
    {
        MapKind.Lakes => (0.42, 0.74, 0.60),
        MapKind.Highlands => (0.24, 0.60, 0.62),
        MapKind.Wildwood => (0.26, 0.74, 0.44),
        _ => (0.30, 0.70, 0.58),
    };

    public static Terrain Generate(uint seed, int size, MapKind kind = MapKind.Plains)
    {
        var (waterLevel, rockLevel, forestLevel) = Thresholds(kind);
        var terrain = new Terrain(size, size);
        uint elevationSeed = seed;
        uint moistureSeed = seed ^ 0x9E3779B9u;
        int centre = size / 2;
        int clear = Balance.KeepClearRadius;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int dx = x - centre;
                int dy = y - centre;
                if (dx * dx + dy * dy <= clear * clear)
                {
                    terrain.Set(x, y, Tile.Grass);
                    continue;
                }

                double e = Fbm(elevationSeed, x, y);
                double m = Fbm(moistureSeed, x, y);
                Tile tile =
                    e < waterLevel ? Tile.Water :
                    e > rockLevel ? Tile.Rock :
                    m > forestLevel ? Tile.Forest :
                    Tile.Grass;
                terrain.Set(x, y, tile);
            }
        }

        // Rich iron out in the wilds first, so the fairness pass sees (and never counts on) it.
        PlaceOre(terrain, seed);
        MakeFair(terrain, seed);
        PlaceSilver(terrain, seed);
        return terrain;
    }

    /// <summary>
    /// Silver lies only in the outer band of the map, past 80% of the way from
    /// the Keep to the edge: one vein per 9,000 tiles of map, at least four,
    /// spread round the compass. The advanced soldiers need it, so the late game
    /// means reaching the far wilds, where the biggest packs sleep.
    /// </summary>
    static void PlaceSilver(Terrain terrain, uint seed)
    {
        int c = terrain.Width / 2;
        int veins = Math.Max(4, terrain.Width * terrain.Height / 9000);
        for (int i = 0; i < veins; i++)
        {
            // Evenly round the compass, with jitter, then out along that bearing to the band.
            double angle = (i + Lattice(seed ^ 0x51F7u, i, 1) * 0.6) / veins * Math.PI * 2;
            double reach = c * (0.8 + Lattice(seed ^ 0x51F7u, i, 2) * 0.12);
            int x = c + (int)(Math.Cos(angle) * reach), y = c + (int)(Math.Sin(angle) * reach);
            for (int tries = 0; tries < 6 && terrain.InBounds(x, y) && terrain.Get(x, y) is Tile.Water or Tile.Rock; tries++)
            {
                x -= Math.Sign(x - c);
                y -= Math.Sign(y - c);
            }
            Stamp(terrain, x, y, 2 + (int)(Lattice(seed ^ 0x51F7u, i, 3) * 2), Tile.Silver, grassOnly: false);
        }
    }

    /// <summary>
    /// Iron lies out on the map, not at home: rich deposits beyond 34 tiles,
    /// one per 2,000 tiles or so of map. (The fairness pass adds one modest
    /// deposit within reach.) An army is made of iron, so a bigger army means
    /// reaching further.
    /// </summary>
    static void PlaceOre(Terrain terrain, uint seed)
    {
        int c = terrain.Width / 2;
        int deposits = terrain.Width * terrain.Height / 2000;
        for (int i = 0; i < deposits; i++)
        {
            int x = (int)(Lattice(seed ^ 0x0BE5u, i, 1) * terrain.Width);
            int y = (int)(Lattice(seed ^ 0x0BE5u, i, 2) * terrain.Height);
            int dx = x - c, dy = y - c;
            if (dx * dx + dy * dy < 34 * 34) continue; // the rich ones are out in the wilds
            int radius = 2 + (int)(Lattice(seed ^ 0x0BE5u, i, 3) * 2);
            Stamp(terrain, x, y, radius, Tile.Ore, grassOnly: true);
        }
    }

    /// <summary>Turn what `kind` may overwrite in a disc into `kind`, leaving the Keep's clearing and every other tile alone.</summary>
    static void Stamp(Terrain terrain, int bx, int by, int radius, Tile kind, bool grassOnly = false)
    {
        int c = terrain.Width / 2, clear = Balance.KeepClearRadius;
        for (int y = by - radius; y <= by + radius; y++)
            for (int x = bx - radius; x <= bx + radius; x++)
            {
                if (!terrain.InBounds(x, y)) continue;
                if ((x - bx) * (x - bx) + (y - by) * (y - by) > radius * radius) continue;
                if ((x - c) * (x - c) + (y - c) * (y - c) <= clear * clear) continue;
                var was = terrain.Get(x, y);
                if (grassOnly ? was != Tile.Grass : !Overwrites(kind, was)) continue;
                terrain.Set(x, y, kind);
            }
    }

    /// <summary>
    /// The minimum a start must offer within FairReach steps over land. Measured
    /// with `hellwall-sim maps` against which maps the bot wins: every map lost
    /// early by every build path had under 50 tiles of reachable rock; every
    /// map won had over 110.
    /// </summary>
    public static readonly StartBudget Minimum = new(Grass: 0, Forest: 250, Rock: 130, Ore: 20, Water: 0);

    /// <summary>
    /// Top up whatever a start lacks: stamp a patch on reachable grass 14 to
    /// 26 steps out, where the disc holds the most grass (so the patch comes
    /// out whole), and measure again. Up to six patches per kind. The home
    /// iron is left in the open for the same reason the wilds keep off it.
    /// </summary>
    static void MakeFair(Terrain terrain, uint seed)
    {
        // Iron first: it may be stamped over rock, and the rock pass then makes up what it took.
        foreach (var (kind, radius, salt) in new[] { (Tile.Ore, 3, 0x0E0E0Eu), (Tile.Rock, 4, 0x51ED27u), (Tile.Forest, 6, 0xA3B195u) })
            for (int attempt = 0; attempt < 6; attempt++)
            {
                var have = Measure(terrain);
                int count = kind switch { Tile.Rock => have.Rock, Tile.Forest => have.Forest, _ => have.Ore };
                int need = kind switch { Tile.Rock => Minimum.Rock, Tile.Forest => Minimum.Forest, _ => Minimum.Ore };
                if (count >= need) break;
                if (BestPatch(terrain, seed ^ salt ^ (uint)attempt, radius, kind) is not { } at) break;
                Stamp(terrain, at.X, at.Y, radius, kind);
            }
    }

    /// <summary>
    /// What a patch of `kind` may be stamped over: always grass; rock and
    /// iron may also take forest (a start choked with trees still gets its
    /// stone), and iron may take rock.
    /// </summary>
    static bool Overwrites(Tile kind, Tile was) =>
        (kind == Tile.Silver && was is Tile.Grass or Tile.Forest or Tile.Rock) ||
        was == Tile.Grass || (was == Tile.Forest && kind is Tile.Rock or Tile.Ore) || (was == Tile.Rock && kind == Tile.Ore);

    static (int X, int Y)? BestPatch(Terrain terrain, uint salt, int radius, Tile kind)
    {
        var dist = Reach(terrain, FairReach);
        int w = terrain.Width;
        (int X, int Y)? best = null;
        double bestScore = double.MinValue;
        for (int i = 0; i < dist.Length; i += 3)
        {
            if (dist[i] < 14 || dist[i] > 26 || terrain.Tiles[i] == Tile.Water) continue;
            int x = i % w, y = i / w, grass = 0;
            // Only what Stamp will actually turn counts: never the Keep's clearing, which is all grass and would otherwise always win.
            int c = w / 2, clear = Balance.KeepClearRadius;
            for (int yy = y - radius; yy <= y + radius; yy++)
                for (int xx = x - radius; xx <= x + radius; xx++)
                    if (terrain.InBounds(xx, yy) && (xx - x) * (xx - x) + (yy - y) * (yy - y) <= radius * radius && Overwrites(kind, terrain.Get(xx, yy))
                        && (xx - c) * (xx - c) + (yy - c) * (yy - c) > clear * clear) grass++;
            // A little seeded jitter, so equally good spots don't always resolve the same way round the Keep.
            double score = grass + Lattice(salt, x, y) * 3;
            if (score > bestScore) { bestScore = score; best = (x, y); }
        }
        return best;
    }

    /// <summary>What a start offers: tiles of each kind a colony can reach over land within `reach` steps of the Keep.</summary>
    public readonly record struct StartBudget(int Grass, int Forest, int Rock, int Ore, int Water)
    {
        public override string ToString() => $"grass {Grass,4} forest {Forest,4} rock {Rock,4} ore {Ore,3} water {Water,4}";
    }

    public const int FairReach = 30;

    /// <summary>
    /// Walk out from the Keep over land (4-neighbour steps, never onto
    /// water) and count what's within `reach` steps. A lake between the Keep
    /// and a quarry puts the quarry out of reach however close it looks.
    /// Water is counted where it borders what was reached.
    /// </summary>
    public static StartBudget Measure(Terrain terrain, int reach = FairReach)
    {
        var dist = Reach(terrain, reach);
        int grass = 0, forest = 0, rock = 0, ore = 0, water = 0;
        for (int i = 0; i < dist.Length; i++)
        {
            if (dist[i] < 0) continue;
            switch (terrain.Tiles[i])
            {
                case Tile.Grass: grass++; break;
                case Tile.Forest: forest++; break;
                case Tile.Rock: rock++; break;
                case Tile.Ore: ore++; break;
                case Tile.Water: water++; break;
            }
        }
        return new StartBudget(grass, forest, rock, ore, water);
    }

    /// <summary>Steps from the centre over land, or -1 out of reach. Water tiles next to reached land get a distance but lead nowhere.</summary>
    static int[] Reach(Terrain terrain, int reach)
    {
        int w = terrain.Width, c = w / 2;
        var dist = new int[terrain.Tiles.Length];
        Array.Fill(dist, -1);
        var queue = new Queue<int>();
        int start = terrain.Index(c, c);
        dist[start] = 0;
        queue.Enqueue(start);
        while (queue.Count > 0)
        {
            int i = queue.Dequeue();
            if (terrain.Tiles[i] == Tile.Water || dist[i] >= reach) continue;
            int x = i % w, y = i / w;
            foreach (var (nx, ny) in new[] { (x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1) })
            {
                if (!terrain.InBounds(nx, ny)) continue;
                int j = terrain.Index(nx, ny);
                if (dist[j] >= 0) continue;
                dist[j] = dist[i] + 1;
                queue.Enqueue(j);
            }
        }
        return dist;
    }

    static double Fbm(uint seed, int x, int y) =>
        0.60 * ValueNoise(seed, x / 24.0, y / 24.0) +
        0.30 * ValueNoise(seed + 1, x / 12.0, y / 12.0) +
        0.10 * ValueNoise(seed + 2, x / 6.0, y / 6.0);

    static double ValueNoise(uint seed, double x, double y)
    {
        int x0 = (int)Math.Floor(x);
        int y0 = (int)Math.Floor(y);
        double tx = Smooth(x - x0);
        double ty = Smooth(y - y0);
        double a = Lattice(seed, x0, y0);
        double b = Lattice(seed, x0 + 1, y0);
        double c = Lattice(seed, x0, y0 + 1);
        double d = Lattice(seed, x0 + 1, y0 + 1);
        double top = a + (b - a) * tx;
        double bottom = c + (d - c) * tx;
        return top + (bottom - top) * ty;
    }

    static double Smooth(double t) => t * t * (3 - 2 * t);

    static double Lattice(uint seed, int x, int y)
    {
        unchecked
        {
            uint h = seed ^ ((uint)x * 0x27D4EB2Du) ^ ((uint)y * 0x165667B1u);
            h ^= h >> 15;
            h *= 0x85EBCA77u;
            h ^= h >> 13;
            h *= 0xC2B2AE3Du;
            h ^= h >> 16;
            return h / 4294967296.0;
        }
    }
}
