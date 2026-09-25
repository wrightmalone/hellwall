namespace Hellwall.Sim;

public enum WoodsmanState : byte { Home, Out, Chopping, Back }

/// <summary>
/// One of a Woodcutter's crew, out in the world: walks a short path from
/// the lodge's door to a tree at the forest's edge, chops it, and carries the
/// wood home. Few (three a lodge), so plain objects like soldiers. A Quarry's
/// or a Mine's miners are the same, working rock or ore instead of trees.
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
    /// <summary>The tree being felled (or rock or ore being mined), a tile index, or -1.</summary>
    public int Tree = -1;
    public float Carry;
    public float Wait;
}

public static class WoodsSystem
{
    /// <summary>A lodge's wood rate is measured over this many seconds of deliveries, for the HUD and the bot.</summary>
    const float Window = 30;

    public static void Step(World world, float dt)
    {
        if (!world.ForestBlocks && !world.Mining) return;
        var woods = world.Rules.Woods;
        var mining = world.Rules.Mining;
        var men = world.WoodsmanList;

        // Crew out of every working lodge, quarry and mine; the crew of one that's gone goes with it.
        foreach (var b in world.BuildingList)
        {
            if (!world.HasCrew(b.Def) || !b.Complete) continue;
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
                    if (FindTree(world, home, m))
                    {
                        m.State = WoodsmanState.Out;
                        home.Exhausted = false;
                    }
                    else
                    {
                        m.Wait = 5;
                        home.Exhausted = !AnyAtWork(men, home.Id); // worked out only when none of the crew has anything left
                    }
                    break;
                case WoodsmanState.Out:
                    if (Walk(world, m, forward: true, woods.Speed * dt)) m.State = WoodsmanState.Chopping;
                    break;
                case WoodsmanState.Chopping:
                {
                    var works = m.Tree < 0 ? Tile.Grass : world.Terrain.Tiles[m.Tree];
                    if (m.Tree < 0 || !Works(home.Def, works)) { GoHome(world, m); break; }
                    bool tree = works == Tile.Forest;
                    float hp = (tree ? woods.ChopDps : mining.MineDps) * dt * world.Colony.Power;
                    world.TreeHp[m.Tree] -= hp;
                    m.Carry += hp * (works switch { Tile.Forest => woods.WoodPerHp, Tile.Rock => mining.StonePerHp, _ => mining.IronPerHp });
                    if (world.TreeHp[m.Tree] <= 0)
                    {
                        if (tree) world.Fell(m.Tree);
                        else world.WearAway(m.Tree);
                    }
                    if (world.Terrain.Tiles[m.Tree] != works || m.Carry >= woods.Carry) GoHome(world, m);
                    break;
                }
                case WoodsmanState.Back:
                    if (!Walk(world, m, forward: false, woods.Speed * dt)) break;
                    world.Colony.Stock[(int)(home.Def.Produces ?? Resource.Wood)] += m.Carry;
                    home.WoodWindow += m.Carry;
                    m.Carry = 0;
                    m.State = WoodsmanState.Home;
                    m.Wait = 0;
                    break;
            }
        }
    }

    static bool AnyAtWork(List<Woodsman> men, int home)
    {
        foreach (var o in men) if (o.HomeId == home && o.Tree >= 0) return true;
        return false;
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

    /// <summary>A tile this building's crew work: its lodge's trees, its quarry's rock, its mine's ore.</summary>
    static bool Works(BuildingDef def, Tile tile) => tile != Tile.Grass && Array.IndexOf(def.Gathers, tile) >= 0;

    static readonly (int, int)[] Neighbours = [(1, 0), (-1, 0), (0, 1), (0, -1)];

    /// <summary>
    /// Where a woodsman or miner may walk: open ground, and the colony's own buildings, which he
    /// crosses by postern, ladder and back door (a walled or tightly built town still works the
    /// ground outside). Not a possessed one.
    /// </summary>
    static bool Passable(World world, int x, int y)
    {
        if (world.IsHumanWalkable(x, y)) return true;
        int id = world.BuildingIdAt(x, y);
        return id != 0 && world.BuildingById(id) is { Possessed: false };
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
                if (Works(home.Def, t.Tiles[n]) && world.TreeClaim[n] == 0 && world.BuildingIdAt(nx, ny) == 0)
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

    static bool Open(Terrain t, int x, int y, bool forestOpen) => t.InBounds(x, y) && (t.Get(x, y) is Tile.Grass or Tile.Ore or Tile.Silver || (forestOpen && t.Get(x, y) == Tile.Forest));

    /// <summary>
    /// Would felling the tree (or wearing away the rock) at (x, y) cut a new way through: does it keep
    /// apart two stretches of open ground on its sides that don't otherwise meet within `window` tiles?
    /// Terrain only (buildings don't count); forestOpen when the woods don't block. For the client's
    /// warning; allocates, so not per tick.
    /// </summary>
    public static bool OpensAGap(Terrain t, int x, int y, int window = 6, bool forestOpen = false)
    {
        int side = window * 2 + 1;
        var label = new int[side * side];
        int next = 0, first = -1;
        var queue = new Queue<(int, int)>();
        for (int oy = -1; oy <= 1; oy++)
            for (int ox = -1; ox <= 1; ox++)
            {
                if (ox == 0 && oy == 0) continue;
                int nx = x + ox, ny = y + oy;
                if (!Open(t, nx, ny, forestOpen)) continue;
                int lx = nx - x + window, ly = ny - y + window;
                int here = label[ly * side + lx];
                if (here == 0)
                {
                    // Flood this neighbour's stretch of open ground within the window, the tree itself still standing.
                    here = ++next;
                    label[ly * side + lx] = here;
                    queue.Enqueue((lx, ly));
                    while (queue.Count > 0)
                    {
                        var (cx, cy) = queue.Dequeue();
                        foreach (var (sx, sy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                        {
                            int qx = cx + sx, qy = cy + sy;
                            if (qx < 0 || qy < 0 || qx >= side || qy >= side || label[qy * side + qx] != 0) continue;
                            if ((qx == window && qy == window) || !Open(t, x - window + qx, y - window + qy, forestOpen)) continue;
                            label[qy * side + qx] = here;
                            queue.Enqueue((qx, qy));
                        }
                    }
                }
                if (first < 0) first = here;
                else if (here != first) return true; // two stretches that only this tree keeps apart
            }
        return false;
    }
}
