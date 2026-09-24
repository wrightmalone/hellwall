namespace Hellwall.Sim;

public enum WoodsmanState : byte { Home, Out, Chopping, Back }

/// <summary>
/// One of a Woodcutter's crew, out in the world: walks a short path from
/// the lodge's door to a tree at the forest's edge, chops it, and carries the
/// wood home. Few (three a lodge), so plain objects like soldiers.
/// </summary>
public sealed class Woodsman
{
    public int Id;
    public int HomeId;
    public float X, Y, PrevX, PrevY;
    public WoodsmanState State;
    /// <summary>Tiles from the door (index 0) to where he stands to chop.</summary>
    public int[] Path = [];
    public int Step;
    /// <summary>The tree being felled, a tile index, or -1.</summary>
    public int Tree = -1;
    public float Carry;
    public float Wait;
}

internal static class WoodsSystem
{
    /// <summary>A lodge's wood rate is measured over this many seconds of deliveries, for the HUD and the bot.</summary>
    const float Window = 30;

    public static void Step(World world, float dt)
    {
        if (!world.Rules.Woods.Blocks) return;
        var woods = world.Rules.Woods;
        var men = world.WoodsmanList;

        // Crew out of every working lodge; the crew of a lodge that's gone goes with it.
        foreach (var b in world.BuildingList)
        {
            if (!b.Def.Woodsmen || !b.Complete) continue;
            int have = 0;
            foreach (var m in men) if (m.HomeId == b.Id) have++;
            for (; have < b.Def.Workers; have++)
            {
                if (world.DoorOf(b) is not { } door) break;
                men.Add(new Woodsman { Id = world.NextId(), HomeId = b.Id, X = door.X + 0.5f, Y = door.Y + 0.5f, PrevX = door.X + 0.5f, PrevY = door.Y + 0.5f });
            }
            b.WoodTimer += dt;
            if (b.WoodTimer >= Window)
            {
                b.Rate = b.WoodWindow / Window;
                b.WoodWindow = 0;
                b.WoodTimer = 0;
            }
        }

        for (int i = men.Count - 1; i >= 0; i--)
        {
            var m = men[i];
            m.PrevX = m.X;
            m.PrevY = m.Y;
            var home = world.BuildingById(m.HomeId);
            if (home == null)
            {
                Release(world, m);
                men.RemoveAt(i);
                continue;
            }
            if (!home.Active && m.State is WoodsmanState.Out or WoodsmanState.Chopping) GoHome(world, m);
            switch (m.State)
            {
                case WoodsmanState.Home:
                    m.Wait -= dt;
                    if (m.Wait > 0 || !home.Active) break;
                    if (FindTree(world, home, m)) m.State = WoodsmanState.Out;
                    else m.Wait = 5;
                    break;
                case WoodsmanState.Out:
                    if (Walk(world, m, forward: true, woods.Speed * dt)) m.State = WoodsmanState.Chopping;
                    break;
                case WoodsmanState.Chopping:
                    if (m.Tree < 0 || world.Terrain.Tiles[m.Tree] != Tile.Forest) { GoHome(world, m); break; }
                    float hp = woods.ChopDps * dt * world.Colony.Power;
                    world.TreeHp[m.Tree] -= hp;
                    m.Carry += hp * woods.WoodPerHp;
                    if (world.TreeHp[m.Tree] <= 0) world.Fell(m.Tree);
                    if (world.Terrain.Tiles[m.Tree] != Tile.Forest || m.Carry >= woods.Carry) GoHome(world, m);
                    break;
                case WoodsmanState.Back:
                    if (!Walk(world, m, forward: false, woods.Speed * dt)) break;
                    world.Colony.Stock[(int)Resource.Wood] += m.Carry;
                    home.WoodWindow += m.Carry;
                    m.Carry = 0;
                    m.State = WoodsmanState.Home;
                    m.Wait = 0;
                    break;
            }
        }
    }

    static void GoHome(World world, Woodsman m)
    {
        Release(world, m);
        m.State = WoodsmanState.Back;
    }

    static void Release(World world, Woodsman m)
    {
        if (m.Tree >= 0 && world.TreeClaim[m.Tree] == m.Id) world.TreeClaim[m.Tree] = 0;
        m.Tree = -1;
    }

    /// <summary>
    /// Move along the path (to its end going out, to its start coming back).
    /// True on arrival. The path is fixed when he sets out; if a building now
    /// stands in the way he walks through its corner rather than being stuck.
    /// </summary>
    static bool Walk(World world, Woodsman m, bool forward, float step)
    {
        int w = world.Terrain.Width;
        while (step > 0)
        {
            int target = forward ? m.Step + 1 : m.Step - 1;
            if (target < 0 || target >= m.Path.Length) return true;
            float tx = m.Path[target] % w + 0.5f, ty = m.Path[target] / w + 0.5f;
            float dx = tx - m.X, dy = ty - m.Y, d = MathF.Sqrt(dx * dx + dy * dy);
            if (d <= step)
            {
                m.X = tx;
                m.Y = ty;
                m.Step = target;
                step -= d;
            }
            else
            {
                m.X += dx / d * step;
                m.Y += dy / d * step;
                step = 0;
            }
        }
        return forward ? m.Step >= m.Path.Length - 1 : m.Step <= 0;
    }

    static readonly (int, int)[] Neighbours = [(1, 0), (-1, 0), (0, 1), (0, -1)];

    /// <summary>Where a woodsman may walk: open ground, and the colony's own walls, which he crosses by postern and ladder (a walled town still fells the woods outside).</summary>
    static bool Passable(World world, int x, int y)
    {
        if (world.IsHumanWalkable(x, y)) return true;
        int id = world.BuildingIdAt(x, y);
        return id != 0 && world.BuildingById(id) is { IsWallLike: true, Possessed: false };
    }

    /// <summary>
    /// Breadth-first from the lodge's door over ground a woodsman can walk, up to
    /// Reach steps: the first tile beside an unclaimed tree. Deterministic:
    /// neighbours in a fixed order, the first found wins.
    /// </summary>
    static bool FindTree(World world, Building home, Woodsman m)
    {
        var t = world.Terrain;
        if (world.DoorOf(home) is not { } door) return false;
        int reach = world.Rules.Woods.Reach, w = t.Width;
        var from = new Dictionary<int, int>();
        var queue = new Queue<(int Tile, int Depth)>();
        int start = t.Index(door.X, door.Y);
        from[start] = -1;
        queue.Enqueue((start, 0));
        while (queue.Count > 0)
        {
            var (tile, depth) = queue.Dequeue();
            int x = tile % w, y = tile / w;
            foreach (var (ox, oy) in Neighbours)
            {
                int nx = x + ox, ny = y + oy;
                if (!t.InBounds(nx, ny)) continue;
                int n = t.Index(nx, ny);
                if (t.Tiles[n] == Tile.Forest && world.TreeClaim[n] == 0)
                {
                    var path = new List<int>();
                    for (int c = tile; c != -1; c = from[c]) path.Add(c);
                    path.Reverse();
                    m.Path = path.ToArray();
                    m.Step = 0;
                    m.Tree = n;
                    world.TreeClaim[n] = m.Id;
                    m.X = door.X + 0.5f;
                    m.Y = door.Y + 0.5f;
                    return true;
                }
                if (depth >= reach || from.ContainsKey(n) || !Passable(world, nx, ny)) continue;
                from[n] = tile;
                queue.Enqueue((n, depth + 1));
            }
        }
        return false;
    }
}
