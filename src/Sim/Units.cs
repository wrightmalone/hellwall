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
public sealed class Unit
{
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
            u.PrevX = u.X;
            u.PrevY = u.Y;
            u.Cooldown = Math.Max(0, u.Cooldown - dt);

            var weapon = u.Def.Weapon;
            int target = -1;
            float reach = u.Order switch
            {
                OrderKind.Move => 0,
                OrderKind.Hold => weapon.Range,
                _ => weapon.Range + AggroExtra,
            };
            if (reach > 0) target = Combat.NearestDemon(world, u.X, u.Y, reach, weapon.AirOnly);

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
                else if (u.Order != OrderKind.Hold && d > 1e-4f)
                {
                    vx = dx / d * u.Def.Speed;
                    vy = dy / d * u.Def.Speed;
                }
            }
            else if (u.Order is OrderKind.Move or OrderKind.AttackMove or OrderKind.Patrol && u.Field != null)
            {
                float ddx = u.DestX + 0.5f - u.X, ddy = u.DestY + 0.5f - u.Y;
                if (ddx * ddx + ddy * ddy < 0.5f && u.Order == OrderKind.Patrol)
                {
                    // Turn round: the far end becomes the near one.
                    (u.DestX, u.PatrolX) = (u.PatrolX, u.DestX);
                    (u.DestY, u.PatrolY) = (u.PatrolY, u.DestY);
                    u.Field = world.HumanFieldTo(u.DestX, u.DestY);
                }
                else if (ddx * ddx + ddy * ddy < 0.5f)
                {
                    u.Order = OrderKind.Idle;
                    u.Field = null;
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
