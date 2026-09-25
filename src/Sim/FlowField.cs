namespace Hellwall.Sim;

/// <summary>
/// A shared route map: Dijkstra outward from a set of source tiles, then each
/// tile points at its cheapest neighbour, so any number of walkers cost the
/// same pathing as one. Two uses:
///
/// Demons: sources are every tile of every building that isn't a wall or
/// gate. Walls and gates are passable at WallCostMultiplier times the normal
/// cost, so the horde walks round a wall when that's short and otherwise its
/// route runs through the wall. A demon whose next tile holds a building
/// attacks it (Combat.DemonsAttackBuildings), which is all breaching is.
///
/// Humans: the source is one ordered destination; soldiers pass gates and
/// nothing else built.
///
/// Rebuilt whenever buildings change. A full rebuild on a 256x256 map is
/// cheap enough for now; `bench` reports its cost, and if it grows, the plan
/// is incremental repair or a budget spread over several ticks.
/// </summary>
public sealed class FlowField
{
    public const int Unreachable = int.MaxValue;

    public readonly int Width;
    public readonly int Height;

    /// <summary>Path cost to the nearest target building, 10 per straight step.</summary>
    public readonly int[] Dist;

    /// <summary>Unit direction toward the cheapest neighbour; zero where unreachable.</summary>
    public readonly float[] DirX;
    public readonly float[] DirY;

    // Padded working grids, (Width + 2) x (Height + 2), border blocked.
    readonly bool[] _open;
    readonly bool[] _walk;
    readonly byte[] _cost;
    readonly int[] _dist;

    /// <summary>Ring size for Dial's algorithm: a power of two above the largest single step (a diagonal into a wall).</summary>
    const int Buckets = 512;

    readonly int[][] _bucket;
    readonly int[] _bucketCount = new int[Buckets];

    static readonly int[] Ox = [1, -1, 0, 0, 1, 1, -1, -1];
    static readonly int[] Oy = [0, 0, 1, -1, 1, -1, 1, -1];
    static readonly float InvSqrt2 = 1f / MathF.Sqrt(2f);

    public FlowField(int width, int height)
    {
        Width = width;
        Height = height;
        Dist = new int[width * height];
        DirX = new float[width * height];
        DirY = new float[width * height];
        int padded = (width + 2) * (height + 2);
        _open = new bool[padded];
        _walk = new bool[padded];
        _cost = new byte[padded];
        _dist = new int[padded];
        if (Balance.CostDiagonal * Math.Max(Balance.ForestCostMultiplier, Balance.WallCostMultiplier) >= Buckets)
            throw new InvalidOperationException($"largest flow step must stay below {Buckets} for Dial's algorithm; raise Buckets");
        _bucket = new int[Buckets][];
        for (int i = 0; i < Buckets; i++) _bucket[i] = new int[256];
    }

    /// <summary>What the horde heads for: everything built except walls and gates, which it paths through.</summary>
    public static bool IsTarget(BuildingKind kind) => kind is not (BuildingKind.Wall or BuildingKind.Gate or BuildingKind.StoneGate);

    /// <summary>The demon field: toward every target building, through walls at a cost.</summary>
    public void Build(World world) => Build(world, human: false, 0, 0);

    /// <summary>A soldier field: toward one tile, through gates but no other buildings.</summary>
    public void BuildHuman(World world, int destX, int destY) => Build(world, human: true, destX, destY);

    void Build(World world, bool human, int destX, int destY)
    {
        // Padded copies (one tile of blocked border) so the hot loops below
        // need no bounds checks: every neighbour of an interior cell exists.
        // _open is ground a walker can stand on; _walk is what the search may
        // expand into, which for demons also includes walls, at a cost.
        int pw = Width + 2;
        var terrain = world.Terrain;
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                int p = (y + 1) * pw + x + 1;
                bool open = human ? world.IsHumanWalkable(x, y) : world.IsWalkable(x, y);
                bool wall = !human && !open && world.IsWallAt(x, y);
                // Where the woods block, a tree is a wall the horde can hack through, at its own cost.
                bool tree = !human && !open && !wall && world.IsTree(x, y);
                _open[p] = open;
                _walk[p] = open || wall || tree;
                _cost[p] = (byte)(wall ? Balance.WallCostMultiplier
                    : tree ? world.Rules.Woods.TreeCost
                    : !open ? 0
                    : terrain.Get(x, y) == Tile.Forest ? Balance.ForestCostMultiplier : 1);
            }
        }

        // Dial's algorithm: step costs are small integers (10, 14, 20, 28), so
        // a ring of buckets indexed by distance replaces the heap and every
        // push and pop is O(1). Every live entry lies within MaxStep of the
        // current distance, so Buckets slots never alias. Distances come out
        // identical to plain Dijkstra.
        Array.Fill(_dist, Unreachable);
        Array.Clear(_bucketCount);
        int pending = 0;
        if (human)
        {
            if (destX >= 0 && destY >= 0 && destX < Width && destY < Height)
            {
                int p = (destY + 1) * pw + destX + 1;
                _dist[p] = 0;
                Push(0, p);
                pending++;
            }
        }
        else
        {
            foreach (var b in world.BuildingList)
            {
                if (!b.IsDemonTarget) continue;
                for (int y = b.Y; y < b.Y + b.H; y++)
                    for (int x = b.X; x < b.X + b.W; x++)
                    {
                        int p = (y + 1) * pw + x + 1;
                        _dist[p] = 0;
                        Push(0, p);
                        pending++;
                    }
            }
        }

        Span<int> offset = [1, -1, pw, -pw, pw + 1, -pw + 1, pw - 1, -pw - 1];
        for (int cur = 0; pending > 0; cur++)
        {
            int slot = cur & (Buckets - 1);
            while (_bucketCount[slot] > 0)
            {
                int p = _bucket[slot][--_bucketCount[slot]];
                pending--;
                if (_dist[p] != cur) continue; // superseded by a shorter route

                for (int k = 0; k < 8; k++)
                {
                    int np = p + offset[k];
                    if (!_walk[np]) continue;
                    int step;
                    if (k < 4)
                    {
                        step = Balance.CostStraight;
                    }
                    else
                    {
                        // No corner cutting: a diagonal step needs both orthogonal tiles open.
                        if (!_open[p + Ox[k]] || !_open[p + Oy[k] * pw]) continue;
                        step = Balance.CostDiagonal;
                    }
                    int nd = cur + step * _cost[np];
                    if (nd < _dist[np])
                    {
                        _dist[np] = nd;
                        Push(nd, np);
                        pending++;
                    }
                }
            }
        }

        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                int i = y * Width + x;
                int p = (y + 1) * pw + x + 1;
                int own = _dist[p];
                Dist[i] = own;
                DirX[i] = DirY[i] = 0;
                if (own == Unreachable || own == 0) continue;

                int best = own;
                int bk = -1;
                for (int k = 0; k < 8; k++)
                {
                    if (k >= 4 && (!_open[p + Ox[k]] || !_open[p + Oy[k] * pw])) continue;
                    int nd = _dist[p + offset[k]];
                    if (nd < best)
                    {
                        best = nd;
                        bk = k;
                    }
                }
                if (bk < 0) continue;
                float scale = bk >= 4 ? InvSqrt2 : 1f;
                DirX[i] = Ox[bk] * scale;
                DirY[i] = Oy[bk] * scale;
            }
        }
    }

    public int DistAt(int x, int y) =>
        x < 0 || y < 0 || x >= Width || y >= Height ? Unreachable : Dist[y * Width + x];

    /// <summary>
    /// Direction at a world position, bilinearly blended between the four
    /// nearest tile centres so the horde curves instead of zig-zagging on the
    /// 8-way grid. Arrived means within striking distance of a target.
    /// </summary>
    public (float X, float Y, bool Arrived) Sample(float x, float y)
    {
        int tx = (int)x;
        int ty = (int)y;
        int own = DistAt(tx, ty);
        if (own == Unreachable) return (0, 0, false);
        if (own <= Balance.ArriveDistance)
        {
            // Arrived: keep pressing straight at the target, so the front rank
            // closes to striking reach; collision holds it at the face.
            int t = ty * Width + tx;
            return (DirX[t], DirY[t], true);
        }

        float u = x - 0.5f;
        float v = y - 0.5f;
        int x0 = (int)MathF.Floor(u);
        int y0 = (int)MathF.Floor(v);
        float fu = u - x0;
        float fv = v - y0;

        float sx = 0, sy = 0;
        bool clear =
            Accumulate(x0, y0, (1 - fu) * (1 - fv), ref sx, ref sy) &
            Accumulate(x0 + 1, y0, fu * (1 - fv), ref sx, ref sy) &
            Accumulate(x0, y0 + 1, (1 - fu) * fv, ref sx, ref sy) &
            Accumulate(x0 + 1, y0 + 1, fu * fv, ref sx, ref sy);

        // Next to an obstacle the blend can point into it (two neighbours'
        // directions averaging across a corner), and the demon would press
        // against rock forever. The tile's own grid direction never points
        // into a blocked tile, so use it whenever any sample is blocked.
        float len2 = sx * sx + sy * sy;
        if (!clear || len2 < 1e-6f)
        {
            int i = ty * Width + tx;
            return (DirX[i], DirY[i], false);
        }
        float inv = 1f / MathF.Sqrt(len2);
        return (sx * inv, sy * inv, false);
    }

    /// <summary>Add one tile's weighted direction; false if that tile has none (blocked, unreachable, or a target).</summary>
    bool Accumulate(int x, int y, float weight, ref float sx, ref float sy)
    {
        if (x < 0 || y < 0 || x >= Width || y >= Height) return false;
        if (!_open[(y + 1) * (Width + 2) + x + 1]) return false; // a wall has a direction, but nobody stands on it
        int i = y * Width + x;
        float dx = DirX[i], dy = DirY[i];
        sx += dx * weight;
        sy += dy * weight;
        return dx != 0 || dy != 0;
    }

    void Push(int dist, int p)
    {
        int slot = dist & (Buckets - 1);
        if (_bucketCount[slot] == _bucket[slot].Length) Array.Resize(ref _bucket[slot], _bucket[slot].Length * 2);
        _bucket[slot][_bucketCount[slot]++] = p;
    }
}
