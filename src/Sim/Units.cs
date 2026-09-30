namespace Hellwall.Sim;

public enum OrderKind : byte
{
    /// <summary>Stand, and fight anything that comes within reach.</summary>
    Idle,
    /// <summary>Go there and ignore enemies on the way.</summary>
    Move,
    /// <summary>Go there, stopping to fight anything in reach; then Idle.</summary>
    AttackMove,
    /// <summary>Never move; shoot what's in range.</summary>
    Hold,
    /// <summary>Attack-move there, then back to where the order was given, and on, for ever.</summary>
    Patrol,
}

/// <summary>
/// A human soldier. There are few of these next to the horde (hundreds, not
/// tens of thousands), so they're plain objects with stable ids, which
/// orders and selection can refer to.
/// </summary>
/// <summary>A move queued after the one a soldier's on (shift): where to, and its own spot there.</summary>
public readonly record struct Waypoint(OrderKind Order, int X, int Y, float SlotX, float SlotY);

public sealed class Unit
{
    /// <summary>Moves queued behind the current one, in order: taken one at a time as each is reached.</summary>
    public readonly List<Waypoint> Waypoints = new();
    public int Id;
    public UnitKind Kind;
    public UnitDef Def = null!;
    public float X;
    public float Y;
    public float PrevX;
    public float PrevY;
    public float Hp;
    public float Cooldown;
    public OrderKind Order;
    public int DestX;
    public int DestY;
    /// <summary>Demons this soldier has killed: veterancy.</summary>
    public int Kills;
    /// <summary>A patrol's other end: where the soldier stood when ordered.</summary>
    public int PatrolX, PatrolY;
    /// <summary>Where an idle soldier stands its ground: it fights within LeashRadius of here and comes back. Set when it goes idle.</summary>
    public float AnchorX, AnchorY;
    public bool Anchored;
    /// <summary>This soldier's own spot in its group's formation at the destination, so a squad fans out instead of all shoving for one tile.</summary>
    public float SlotX, SlotY;

    /// <summary>Kills to reach each rank (Veteran, Elite, Champion).</summary>
    public static readonly int[] RankKills = [8, 25, 60];
    public const float DamagePerRank = 0.15f, HpPerRank = 0.12f;

    public int Rank => Kills >= RankKills[2] ? 3 : Kills >= RankKills[1] ? 2 : Kills >= RankKills[0] ? 1 : 0;
    public float MaxHp => Def.Hp * (1 + HpPerRank * Rank);
    public static string RankName(int rank) => rank switch { 1 => "Veteran", 2 => "Elite", 3 => "Champion", _ => "Recruit" };

    /// <summary>The pooled human flow field toward DestX/DestY while moving.</summary>
    internal FlowField? Field;
}

internal static class UnitSystem
{
    /// <summary>Extra reach, beyond weapon range, at which Idle and AttackMove units close in on a demon.</summary>
    const float AggroExtra = 3f;

    /// <summary>Within this many tiles of its formation spot a soldier leaves the flow field and walks straight to it.</summary>
    const float SlotApproach = 2.5f;

    /// <summary>An idle soldier chases no further than this from where it stopped, then walks back: the army stays where you put it.</summary>
    public const float LeashRadius = 6f;

    /// <summary>A demon this close to a soldier hits the soldier instead of the walls.</summary>
    public const float MeleeRange = 0.8f;

    /// <summary>A demon within this many tiles of a soldier turns to chase it.</summary>
    public const float ChaseRadius = 4f;

    /// <summary>
    /// Soldiers draw demons off the flow field: every demon within ChaseRadius
    /// of one steers for the nearest. Unit-centric, so it costs units x nearby
    /// demons, not demons x units.
    /// </summary>
    public static void MarkChase(World world)
    {
        var h = world.Horde;
        Array.Fill(h.ChaseD2, float.MaxValue, 0, h.Count);
        var grid = world.Spatial;
        const float r2 = ChaseRadius * ChaseRadius;
        foreach (var u in world.UnitList)
        {
            var (x0, x1, y0, y1) = grid.CellRange(u.X, u.Y, ChaseRadius);
            for (int cy = y0; cy <= y1; cy++)
                for (int cx = x0; cx <= x1; cx++)
                {
                    int c = cy * grid.Width + cx;
                    for (int k = grid.CellStart[c]; k < grid.CellStart[c + 1]; k++)
                    {
                        int j = grid.Items[k];
                        float dx = u.X - h.X[j], dy = u.Y - h.Y[j];
                        float d2 = dx * dx + dy * dy;
                        if (d2 >= r2 || d2 >= h.ChaseD2[j]) continue;
                        h.ChaseD2[j] = d2;
                        h.ChaseX[j] = u.X;
                        h.ChaseY[j] = u.Y;
                    }
                }
        }
    }

    /// <summary>Demons in melee reach with their attack ready hit the soldier.</summary>
    public static void TakeHits(World world)
    {
        var h = world.Horde;
        const float m2 = MeleeRange * MeleeRange;
        var grid = world.Spatial;
        foreach (var u in world.UnitList)
        {
            var (x0, x1, y0, y1) = grid.CellRange(u.X, u.Y, MeleeRange);
            for (int cy = y0; cy <= y1; cy++)
                for (int cx = x0; cx <= x1; cx++)
                {
                    int c = cy * grid.Width + cx;
                    for (int k = grid.CellStart[c]; k < grid.CellStart[c + 1]; k++)
                    {
                        int j = grid.Items[k];
                        if (h.Cooldown[j] > 0 || h.Hp[j] <= 0) continue;
                        float dx = u.X - h.X[j], dy = u.Y - h.Y[j];
                        if (dx * dx + dy * dy >= m2) continue;
                        var def = world.Def(h.Kind[j]);
                        if (def.Damage <= 0) continue;
                        u.Hp -= def.Damage;
                        h.Cooldown[j] = def.Cooldown;
                    }
                }
        }
    }

    /// <summary>Would stepping toward (tx, ty) take an idle soldier past its leash?</summary>
    static bool Leashed(Unit u, float tx, float ty)
    {
        float dx = tx - u.AnchorX, dy = ty - u.AnchorY;
        return dx * dx + dy * dy > LeashRadius * LeashRadius;
    }

    /// <summary>Walk back to where it was told to stand (nothing, once it's there).</summary>
    static (float, float) BackToAnchor(World world, Unit u)
    {
        float dx = u.AnchorX - u.X, dy = u.AnchorY - u.Y;
        return dx * dx + dy * dy < 0.09f ? (0, 0) : Steer(world, u, u.AnchorX, u.AnchorY, u.Def.Speed);
    }

    /// <summary>
    /// A velocity toward (tx, ty) at `speed`: straight when the way is clear,
    /// and otherwise toward the next tile of the shortest way round, found in
    /// a small window about the soldier (LocalStep). The long walks follow the
    /// flow fields; this is for the short ones (a chase, the last steps to a
    /// spot, the walk back after a fight), which used to press into walls.
    /// </summary>
    static (float, float) Steer(World world, Unit u, float tx, float ty, float speed)
    {
        float dx = tx - u.X, dy = ty - u.Y, d = MathF.Sqrt(dx * dx + dy * dy);
        if (d < 1e-4f) return (0, 0);
        if (!LineClear(world, u.X, u.Y, tx, ty) && LocalStep(world, (int)u.X, (int)u.Y, (int)tx, (int)ty) is { } step)
        {
            float sx = step.X + 0.5f - u.X, sy = step.Y + 0.5f - u.Y, sd = MathF.Sqrt(sx * sx + sy * sy);
            if (sd > 1e-4f) return (sx / sd * speed, sy / sd * speed);
        }
        return (dx / d * speed, dy / d * speed);
    }

    /// <summary>Can a soldier walk the straight line between two points? Sampled every quarter tile.</summary>
    static bool LineClear(World world, float x0, float y0, float x1, float y1)
    {
        float dx = x1 - x0, dy = y1 - y0;
        int steps = (int)(MathF.Sqrt(dx * dx + dy * dy) * 4) + 1;
        for (int i = 1; i <= steps; i++)
        {
            float t = i / (float)steps;
            if (!world.IsHumanWalkable((int)MathF.Floor(x0 + dx * t), (int)MathF.Floor(y0 + dy * t))) return false;
        }
        return true;
    }

    const int Window = 12; // tiles each way: a 25x25 search, enough for any way round a building or two
    internal const int Side = Window * 2 + 1;
    static readonly (int X, int Y)[] Steps = [(1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1)];

    /// <summary>
    /// The first tile of the shortest way from (fx, fy) to (tx, ty) over ground a soldier can walk,
    /// within the window, or null if there's none there. Breadth-first out from the goal; diagonals
    /// only where both sides are open, so no one cuts a building's corner. Reused buffers: nothing allocated.
    /// The buffers are the world's own: worlds may step on different threads at once.
    /// </summary>
    static (int X, int Y)? LocalStep(World world, int fx, int fy, int tx, int ty)
    {
        int[] Dist = world.LocalStepDist, Queue = world.LocalStepQueue;
        int ox = fx - Window, oy = fy - Window;
        int gx = tx - ox, gy = ty - oy;
        if (gx < 0 || gy < 0 || gx >= Side || gy >= Side) return null;
        Array.Fill(Dist, -1);
        int head = 0, tail = 0;
        Dist[gy * Side + gx] = 0;
        Queue[tail++] = gy * Side + gx;
        int start = Window * Side + Window;
        while (head < tail && Dist[start] < 0)
        {
            int c = Queue[head++], cx = c % Side, cy = c / Side;
            foreach (var (sx, sy) in Steps)
            {
                int nx = cx + sx, ny = cy + sy;
                if (nx < 0 || ny < 0 || nx >= Side || ny >= Side || Dist[ny * Side + nx] >= 0) continue;
                if (!world.IsHumanWalkable(ox + nx, oy + ny) && ny * Side + nx != start) continue;
                if (sx != 0 && sy != 0 && (!world.IsHumanWalkable(ox + cx + sx, oy + cy) || !world.IsHumanWalkable(ox + cx, oy + cy + sy))) continue;
                Dist[ny * Side + nx] = Dist[c] + 1;
                Queue[tail++] = ny * Side + nx;
            }
        }
        if (Dist[start] < 0) return null;
        // From the soldier's tile, step to the neighbour nearest the goal.
        (int X, int Y)? best = null;
        int bestDist = Dist[start];
        foreach (var (sx, sy) in Steps)
        {
            int nx = Window + sx, ny = Window + sy, k = ny * Side + nx;
            if (Dist[k] < 0 || Dist[k] >= bestDist) continue;
            if (sx != 0 && sy != 0 && (!world.IsHumanWalkable(fx + sx, fy) || !world.IsHumanWalkable(fx, fy + sy))) continue;
            bestDist = Dist[k];
            best = (fx + sx, fy + sy);
        }
        return best;
    }

    /// <summary>Kills to a soldier's name; a new rank also brings its extra health, at once.</summary>
    static void Credit(World world, Unit u, int killed)
    {
        int was = u.Rank;
        u.Kills += killed;
        if (u.Rank == was) return;
        u.Hp += u.Def.Hp * Unit.HpPerRank * (u.Rank - was);
        world.Emit(new UnitPromoted(world.Tick, u.Id, u.Kind, u.Rank, u.X, u.Y));
    }

    public static void Step(World world, float dt)
    {
        var units = world.UnitList;
        foreach (var u in units)
        {
            if (u.Hp <= 0) continue; // killed this tick: removed at its end, and meanwhile does nothing (no shot, no promotion back to life)
            u.PrevX = u.X;
            u.PrevY = u.Y;
            u.Cooldown = Math.Max(0, u.Cooldown - dt);
            // A building went up where he stood (or anything else put him inside one): he steps out
            // to the nearest open ground rather than being sealed in for good.
            if (world.BuildingById(world.BuildingIdAt((int)u.X, (int)u.Y)) is { } on && !on.IsDoorway((int)u.X, (int)u.Y) && world.StandingSpotNear((int)u.X, (int)u.Y) is { } outside)
            {
                u.X = u.PrevX = outside.X + 0.5f;
                u.Y = u.PrevY = outside.Y + 0.5f;
                u.Anchored = false;
            }

            var weapon = u.Def.Weapon;
            if (u.Order == OrderKind.Idle && !u.Anchored) { u.AnchorX = u.X; u.AnchorY = u.Y; u.Anchored = true; }
            int target = -1;
            float reach = u.Order switch
            {
                OrderKind.Move => 0,
                OrderKind.Hold => weapon.Range,
                _ => weapon.Range + AggroExtra,
            };
            if (reach > 0) target = Combat.NearestDemon(world, u.X, u.Y, reach, weapon.AirOnly, weapon.GroundOnly);

            float vx = 0, vy = 0;
            // No demon to fight: a Hellgate within reach is the next best thing.
            if (target < 0 && reach > 0 && world.GateNear(u.X, u.Y, weapon.Range) is { } gate)
            {
                if (u.Cooldown <= 0)
                {
                    float gx = Math.Clamp(u.X, gate.X, gate.X + Hellgate.Size), gy = Math.Clamp(u.Y, gate.Y, gate.Y + Hellgate.Size);
                    world.DamageGate(gate, weapon.Damage);
                    world.Emit(new ShotFired(world.Tick, u.X, u.Y, gx, gy, 0, true));
                    if (weapon.Noise > 0) world.Noise.Emit(u.X, u.Y, weapon.Noise, Balance.CombatNoiseIntensity);
                    u.Cooldown = weapon.Cooldown;
                }
            }
            else if (target >= 0)
            {
                float tx = world.Horde.X[target], ty = world.Horde.Y[target];
                float dx = tx - u.X, dy = ty - u.Y;
                float d = MathF.Sqrt(dx * dx + dy * dy);
                if (d <= weapon.Range)
                {
                    if (u.Cooldown <= 0)
                    {
                        int killed = Combat.Fire(world, u.X, u.Y, tx, ty, target, weapon, fromUnit: true, 1 + Unit.DamagePerRank * u.Rank);
                        u.Cooldown = weapon.Cooldown;
                        if (killed > 0) Credit(world, u, killed);
                    }
                }
                else if (u.Order != OrderKind.Hold && d > 1e-4f && !(u.Order == OrderKind.Idle && Leashed(u, tx, ty)))
                    (vx, vy) = Steer(world, u, tx, ty, u.Def.Speed); // round buildings, not into them
                else if (u.Order == OrderKind.Idle) (vx, vy) = BackToAnchor(world, u);
            }
            else if (u.Order == OrderKind.Idle && u.Anchored)
            {
                (vx, vy) = BackToAnchor(world, u);
            }
            else if (u.Order is OrderKind.Move or OrderKind.AttackMove or OrderKind.Patrol && u.Field != null)
            {
                float ddx = u.SlotX - u.X, ddy = u.SlotY - u.Y, d2 = ddx * ddx + ddy * ddy;
                if (d2 < 0.04f && u.Order == OrderKind.Patrol)
                {
                    // Turn round: the far end becomes the near one.
                    (u.DestX, u.PatrolX) = (u.PatrolX, u.DestX);
                    (u.DestY, u.PatrolY) = (u.PatrolY, u.DestY);
                    u.SlotX = u.DestX + 0.5f;
                    u.SlotY = u.DestY + 0.5f;
                    u.Field = world.HumanFieldTo(u.DestX, u.DestY);
                }
                else if (d2 < 0.04f && u.Waypoints.Count > 0)
                {
                    // At its spot, with more queued: on to the next.
                    var next = u.Waypoints[0];
                    u.Waypoints.RemoveAt(0);
                    u.Order = next.Order;
                    u.PatrolX = u.DestX;
                    u.PatrolY = u.DestY;
                    u.DestX = next.X;
                    u.DestY = next.Y;
                    u.SlotX = next.SlotX;
                    u.SlotY = next.SlotY;
                    u.Field = world.HumanFieldTo(next.X, next.Y);
                }
                else if (d2 < 0.04f)
                {
                    // At its own spot: stand here. (Idle anchors here next tick.)
                    u.Order = OrderKind.Idle;
                    u.Field = null;
                }
                else if (d2 < SlotApproach * SlotApproach)
                {
                    // Close: to its spot in the formation (round anything in the way), slowing for the last step so it doesn't overshoot.
                    float dd = MathF.Sqrt(d2);
                    (vx, vy) = Steer(world, u, u.SlotX, u.SlotY, MathF.Min(u.Def.Speed, dd * 4));
                }
                else
                {
                    var (fx, fy, _) = u.Field.Sample(u.X, u.Y);
                    // Last tile: the field has nowhere lower to point, so walk straight at the spot.
                    if (fx == 0 && fy == 0)
                    {
                        float dd = MathF.Sqrt(ddx * ddx + ddy * ddy);
                        fx = ddx / dd;
                        fy = ddy / dd;
                    }
                    vx = fx * u.Def.Speed;
                    vy = fy * u.Def.Speed;
                }
            }

            // Soldiers keep a little space between each other; few enough to check pairwise.
            foreach (var o in units)
            {
                if (o == u) continue;
                float dx = u.X - o.X, dy = u.Y - o.Y;
                float d2 = dx * dx + dy * dy;
                if (d2 >= 0.36f) continue;
                if (d2 < 1e-8f)
                {
                    // Exactly stacked: split deterministically by id, or they'd never come apart.
                    dx = u.Id < o.Id ? 0.01f : -0.01f;
                    dy = 0;
                    d2 = 1e-4f;
                }
                float d = MathF.Sqrt(d2);
                vx += dx / d * (0.6f - d) * 4f;
                vy += dy / d * (0.6f - d) * 4f;
            }

            float nx = u.X + vx * dt;
            if (world.IsHumanWalkable((int)MathF.Floor(nx), (int)u.Y)) u.X = nx;
            float ny = u.Y + vy * dt;
            if (world.IsHumanWalkable((int)u.X, (int)MathF.Floor(ny))) u.Y = ny;
        }
    }
}
