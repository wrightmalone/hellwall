namespace Hellwall.Sim;

public enum DemonKind : byte
{
    Imp,
    Hound,
    /// <summary>A colonist or soldier the horde has taken.</summary>
    Thrall,
    /// <summary>Flies straight over walls and terrain to the nearest building.</summary>
    Gargoyle,
    /// <summary>Slow and swollen; bursts on reaching a building or on dying, battering everything near.</summary>
    Bloater,
    /// <summary>Siege: slow, very tough, hits buildings hard.</summary>
    Brute,
    /// <summary>Howls as it comes, waking the sleeping packs along its way.</summary>
    Howler,
    /// <summary>Slow and heavy; bursts into a brood of Imps when killed.</summary>
    Broodmother,
    /// <summary>Spits over the wall: hits towers, houses and soldiers from a few tiles off. Can't possess.</summary>
    Spitter,
}

/// <summary>
/// Every active demon, stored struct-of-arrays: one array per field, indexed
/// by slot. No per-unit objects, so a tick over 20k demons is a few linear
/// passes over contiguous memory and allocates nothing.
///
/// Slots have no stable identity: nothing outside a tick holds on to one.
/// The dead are removed at the end of each tick by an order-preserving
/// compaction, so slot order (which some rules tie-break on) stays the order
/// demons were spawned in.
/// </summary>
public sealed class Horde
{
    public int Count { get; private set; }

    public float[] X;
    public float[] Y;

    /// <summary>
    /// Positions at the start of the last tick, for render interpolation.
    /// Derived state (always the previous X/Y), so it isn't hashed.
    /// </summary>
    public float[] PrevX;
    public float[] PrevY;

    public float[] VX;
    public float[] VY;
    public DemonKind[] Kind;
    public float[] Hp;

    /// <summary>Seconds until this demon can attack again.</summary>
    public float[] Cooldown;

    /// <summary>
    /// Scratch, rebuilt every tick: the nearest soldier within chase radius
    /// (ChaseD2 is float.MaxValue when none). Derived, so not hashed.
    /// </summary>
    public float[] ChaseX;
    public float[] ChaseY;
    public float[] ChaseD2;

    public Horde(int capacity = 1024)
    {
        X = new float[capacity];
        Y = new float[capacity];
        PrevX = new float[capacity];
        PrevY = new float[capacity];
        VX = new float[capacity];
        VY = new float[capacity];
        Kind = new DemonKind[capacity];
        Hp = new float[capacity];
        Cooldown = new float[capacity];
        ChaseX = new float[capacity];
        ChaseY = new float[capacity];
        ChaseD2 = new float[capacity];
    }

    internal void Add(DemonKind kind, float x, float y, float hp)
    {
        if (Count == X.Length) Grow(Count * 2);
        int i = Count++;
        X[i] = PrevX[i] = x;
        Y[i] = PrevY[i] = y;
        VX[i] = VY[i] = 0;
        Kind[i] = kind;
        Hp[i] = hp;
        Cooldown[i] = 0;
        ChaseD2[i] = float.MaxValue;
    }

    /// <summary>Drop every demon at or below zero hp, keeping the survivors in order. Returns how many died.</summary>
    internal int RemoveDead()
    {
        int write = 0;
        for (int read = 0; read < Count; read++)
        {
            if (Hp[read] <= 0) continue;
            if (write != read)
            {
                X[write] = X[read];
                Y[write] = Y[read];
                PrevX[write] = PrevX[read];
                PrevY[write] = PrevY[read];
                VX[write] = VX[read];
                VY[write] = VY[read];
                Kind[write] = Kind[read];
                Hp[write] = Hp[read];
                Cooldown[write] = Cooldown[read];
            }
            write++;
        }
        int dead = Count - write;
        Count = write;
        return dead;
    }

    void Grow(int capacity)
    {
        Array.Resize(ref X, capacity);
        Array.Resize(ref Y, capacity);
        Array.Resize(ref PrevX, capacity);
        Array.Resize(ref PrevY, capacity);
        Array.Resize(ref VX, capacity);
        Array.Resize(ref VY, capacity);
        Array.Resize(ref Kind, capacity);
        Array.Resize(ref Hp, capacity);
        Array.Resize(ref Cooldown, capacity);
        Array.Resize(ref ChaseX, capacity);
        Array.Resize(ref ChaseY, capacity);
        Array.Resize(ref ChaseD2, capacity);
    }
}

/// <summary>
/// Steering and movement. Two passes (Jacobi style): the first computes every
/// velocity from positions as they stood at the start of the tick, the second
/// integrates. That makes the result independent of slot order, and keeps
/// separation symmetric.
/// </summary>
internal static class HordeSystem
{
    /// <summary>Move the horde. The caller has already built the spatial hash and marked chase targets.</summary>
    public static void Move(World world)
    {
        var h = world.Horde;
        int n = h.Count;
        if (n == 0) return;

        Array.Copy(h.X, h.PrevX, n);
        Array.Copy(h.Y, h.PrevY, n);
        var demons = world.Demons;

        var flow = world.Flow;
        var grid = world.Spatial;
        const float dt = 1f / Balance.TickHz;
        const float diameter = 2 * Balance.DemonRadius;
        const float diameter2 = diameter * diameter;

        for (int i = 0; i < n; i++)
        {
            float x = h.X[i];
            float y = h.Y[i];
            var def = demons[(int)h.Kind[i]];
            float speed = def.Speed * world.SlowAt(x, y);
            h.Cooldown[i] = MathF.Max(0, h.Cooldown[i] - dt);

            float vx, vy;
            if (def.Flies)
            {
                // Fliers ignore the flow field, walls and the crowd: straight at the nearest building.
                var (tx, ty, found) = world.NearestFlierTarget(x, y);
                if (!found) { h.VX[i] = h.VY[i] = 0; continue; }
                float fdx = tx - x, fdy = ty - y;
                float fd = MathF.Sqrt(fdx * fdx + fdy * fdy);
                h.VX[i] = fd > 1e-4f ? fdx / fd * speed : 0;
                h.VY[i] = fd > 1e-4f ? fdy / fd * speed : 0;
                continue;
            }
            if (h.ChaseD2[i] < float.MaxValue && h.ChaseD2[i] > UnitSystem.MeleeRange * UnitSystem.MeleeRange * 0.5f)
            {
                // A soldier nearby: go for it instead of the colony.
                float cdx = h.ChaseX[i] - x, cdy = h.ChaseY[i] - y;
                float inv = speed / MathF.Sqrt(h.ChaseD2[i]);
                vx = cdx * inv;
                vy = cdy * inv;
            }
            else if (h.ChaseD2[i] < float.MaxValue)
            {
                vx = vy = 0; // in its face: stand and fight
            }
            else
            {
                var (fx, fy, _) = flow.Sample(x, y);
                vx = fx * speed;
                vy = fy * speed;
            }

            // Separation: push away from overlapping neighbours in the 3x3
            // cells around us, capped so a dense crowd can't go quadratic.
            float px = 0, py = 0;
            int seen = 0;
            int cx = (int)x, cy = (int)y;
            for (int oy = -1; oy <= 1 && seen < Balance.MaxNeighbours; oy++)
            {
                int ty = cy + oy;
                if (ty < 0 || ty >= grid.Height) continue;
                for (int ox = -1; ox <= 1 && seen < Balance.MaxNeighbours; ox++)
                {
                    int tx = cx + ox;
                    if (tx < 0 || tx >= grid.Width) continue;
                    int cell = ty * grid.Width + tx;
                    int end = grid.CellStart[cell + 1];
                    for (int k = grid.CellStart[cell]; k < end; k++)
                    {
                        int j = grid.Items[k];
                        if (j == i) continue;
                        float dx = x - h.X[j];
                        float dy = y - h.Y[j];
                        float d2 = dx * dx + dy * dy;
                        if (d2 >= diameter2) continue;
                        if (d2 < 1e-8f)
                        {
                            // Exactly stacked: split deterministically by slot order.
                            dx = i < j ? 0.01f : -0.01f;
                            dy = 0;
                            d2 = 1e-4f;
                        }
                        float d = MathF.Sqrt(d2);
                        float overlap = diameter - d;
                        px += dx / d * overlap;
                        py += dy / d * overlap;
                        if (++seen >= Balance.MaxNeighbours) break;
                    }
                }
            }

            vx += px * Balance.SeparationStrength;
            vy += py * Balance.SeparationStrength;

            // Crowd pressure: drift from this tile toward less crowded
            // orthogonal neighbours. Pairwise separation only sees a capped
            // handful of bodies, so on its own a crowd pushed from behind
            // collapses onto its front rank; this bounds density at the scale
            // of whole tiles. Blocked neighbours count as equally full, so it
            // never pushes into a wall.
            int here = grid.Count[cy * grid.Width + cx];
            if (here > Balance.ComfortDensity)
            {
                float gx = Excess(world, grid, cx + 1, cy, here) - Excess(world, grid, cx - 1, cy, here);
                float gy = Excess(world, grid, cx, cy + 1, here) - Excess(world, grid, cx, cy - 1, here);
                vx -= gx * Balance.PressureStrength;
                vy -= gy * Balance.PressureStrength;
            }

            float max = speed * 1.5f;
            float v2 = vx * vx + vy * vy;
            if (v2 > max * max)
            {
                float scale = max / MathF.Sqrt(v2);
                vx *= scale;
                vy *= scale;
            }
            h.VX[i] = vx;
            h.VY[i] = vy;
        }

        // Integrate. Moves are checked against terrain and a hard per-tile
        // density cap, with counts updated as each demon changes tile, so a
        // full tile blocks entry exactly like a wall. That's slot-order
        // dependent, but deterministic.
        var count = grid.Count;
        int width = grid.Width;
        for (int i = 0; i < n; i++)
        {
            float x = h.X[i];
            float y = h.Y[i];
            if (demons[(int)h.Kind[i]].Flies)
            {
                h.X[i] = Math.Clamp(x + h.VX[i] * dt, 0f, world.Terrain.Width - 0.001f);
                h.Y[i] = Math.Clamp(y + h.VY[i] * dt, 0f, world.Terrain.Height - 0.001f);
                continue;
            }
            int tx = (int)x, ty = (int)y;
            // A demon already standing somewhere blocked may always move, so it can walk out.
            bool free = !world.IsWalkable(tx, ty);

            float nx = x + h.VX[i] * dt;
            int ntx = Floor(nx);
            if (ntx == tx || free || (world.IsWalkable(ntx, ty) && count[ty * width + ntx] < Balance.MaxDensity))
            {
                if (ntx != tx) Move(count, ty * width + tx, ty * width + ntx);
                x = nx;
                tx = ntx;
            }

            float ny = y + h.VY[i] * dt;
            int nty = Floor(ny);
            if (nty == ty || free || (world.IsWalkable(tx, nty) && count[nty * width + tx] < Balance.MaxDensity))
            {
                if (nty != ty) Move(count, ty * width + tx, nty * width + tx);
                y = ny;
            }

            h.X[i] = Math.Clamp(x, 0f, world.Terrain.Width - 0.001f);
            h.Y[i] = Math.Clamp(y, 0f, world.Terrain.Height - 0.001f);
        }
    }

    /// <summary>Move one demon's count between tiles, ignoring off-map indices (a free demon leaving the edge is clamped back).</summary>
    static void Move(int[] count, int from, int to)
    {
        if ((uint)from < (uint)count.Length) count[from]--;
        if ((uint)to < (uint)count.Length) count[to]++;
    }

    /// <summary>
    /// How much fuller a neighbour tile is than comfortable, relative to this
    /// one: negative (room to move) when it's emptier. A blocked or off-map
    /// neighbour reads as exactly as full as here, so it neither pulls nor pushes.
    /// </summary>
    static float Excess(World world, SpatialHash grid, int x, int y, int here)
    {
        if (!world.IsWalkable(x, y)) return 0;
        int there = grid.Count[y * grid.Width + x];
        return Math.Max(there, Balance.ComfortDensity) - here;
    }

    /// <summary>Floor that stays correct for small negatives, which IsWalkable then rejects.</summary>
    static int Floor(float v) => v < 0 ? -1 : (int)v;
}
