namespace Hellwall.Sim;

/// <summary>
/// The colony's economy: stockpile, population, crews, and the holy grid.
///
/// Consecrated ground is the power grid. The Keep, Shrines and Wardstones
/// are nodes that project ground in a radius; a node is connected if it
/// stands on a connected node's ground, flooding out from the Keep. Every
/// building must be placed on connected ground, and one that loses it (a
/// Wardstone behind it destroyed) goes dark. Sanctity is the supply side:
/// the Keep and crewed Shrines supply it, working buildings draw it, and a
/// shortfall slows everything that draws it in proportion.
/// </summary>
public sealed class Colony
{
    public const int Resources = 5;

    public readonly double[] Stock = new double[Resources];

    public int Colonists;
    public int WorkersUsed;
    public float SanctitySupply;
    public float SanctityDemand;

    /// <summary>Supply over demand, capped at 1. Production, training and fire rate scale by it.</summary>
    public float Power = 1;

    /// <summary>Out of food and still eating: houses pay no gold.</summary>
    public bool Starving;

    /// <summary>Net per second over the last tick, for the HUD.</summary>
    public readonly double[] NetPerSecond = new double[Resources];

    /// <summary>Per tile: on connected consecrated ground.</summary>
    public readonly bool[] Consecrated;

    public double this[Resource r] => Stock[(int)r];

    public Colony(int tiles, Cost start)
    {
        Consecrated = new bool[tiles];
        foreach (var r in Enum.GetValues<Resource>()) Stock[(int)r] = start[r];
    }

    public bool CanAfford(Cost cost)
    {
        foreach (var r in Enum.GetValues<Resource>())
            if (Stock[(int)r] + 1e-9 < cost[r]) return false;
        return true;
    }

    /// <summary>Why the colony can't pay, naming the first resource short, or null if it can.</summary>
    public string? Shortfall(Cost cost)
    {
        foreach (var r in Enum.GetValues<Resource>())
            if (Stock[(int)r] + 1e-9 < cost[r]) return $"not enough {r.ToString().ToLowerInvariant()}";
        return null;
    }

    public void Pay(Cost cost)
    {
        foreach (var r in Enum.GetValues<Resource>()) Stock[(int)r] -= cost[r];
    }

    public void Refund(Cost cost, double fraction)
    {
        foreach (var r in Enum.GetValues<Resource>()) Stock[(int)r] += cost[r] * fraction;
    }
}

internal static class ColonySystem
{
    public static void Step(World world, float dt)
    {
        var colony = world.Colony;
        var rules = world.Rules;

        foreach (var b in world.BuildingList)
        {
            if (b.Complete) continue;
            b.Built += dt;
            if (b.Built < b.Def.BuildSeconds) continue;
            b.Complete = true;
            world.MarkNetworkDirty();
            world.Emit(new BuildingCompleted(world.Tick, b.Id, b.Kind));
        }

        if (world.NetworkDirty) RecomputeNetwork(world);

        // Population and crews: first come, first served, in id order.
        int colonists = 0;
        foreach (var b in world.BuildingList)
            if (b.Complete && !b.Possessed) colonists += b.Def.Housing;
        int pool = colonists;
        foreach (var b in world.BuildingList)
        {
            if (!b.NeedsCrew) continue;
            b.Staffed = b.Complete && b.OnGround && !b.Possessed && pool >= b.Def.Workers;
            if (b.Staffed) pool -= b.Def.Workers;
        }
        colony.Colonists = colonists;
        colony.WorkersUsed = colonists - pool;

        float supply = 0, demand = 0;
        foreach (var b in world.BuildingList)
        {
            if (!b.Active) continue;
            supply += b.Def.SanctitySupply;
            demand += b.Def.SanctityUse;
        }
        colony.SanctitySupply = supply;
        colony.SanctityDemand = demand;
        colony.Power = demand <= 0 ? 1 : Math.Min(1, supply / demand);

        Span<double> net = stackalloc double[Colony.Resources];
        // Woodsmen deliver wood themselves; their lodges' measured rates are shown in the net but not added again.
        double delivered = 0;
        foreach (var b in world.BuildingList)
        {
            if (!b.Active) continue;
            net[(int)Resource.Gold] += b.Def.Gold;
            if (b.Def.Woodsmen && world.ForestBlocks) delivered += b.Rate;
            else if (b.Def.Produces is { } res) net[(int)res] += b.Rate * colony.Power;
        }
        if (!colony.Starving) net[(int)Resource.Gold] += colonists * rules.ColonistGoldPerSecond * world.Tech.ColonistGoldMultiplier;
        net[(int)Resource.Food] -= colonists * rules.ColonistFoodPerSecond;
        foreach (var u in world.UnitList) net[(int)Resource.Gold] -= u.Def.UpkeepGold;

        for (int r = 0; r < Colony.Resources; r++)
        {
            colony.NetPerSecond[r] = net[r] + (r == (int)Resource.Wood ? delivered : 0);
            colony.Stock[r] = Math.Max(0, colony.Stock[r] + net[r] * dt);
        }
        colony.Starving = colony.Stock[(int)Resource.Food] <= 0 && net[(int)Resource.Food] < 0;

        for (int i = 0; i < world.BuildingList.Count; i++)
        {
            var b = world.BuildingList[i];
            if (!b.Active || b.Researching == null) continue;
            b.ResearchProgress += dt * colony.Power;
            if (b.ResearchProgress >= rules.Tech(b.Researching).Seconds) world.CompleteResearch(b);
        }

        foreach (var b in world.BuildingList)
        {
            if (!b.Active || b.Queue.Count == 0) continue;
            b.TrainProgress += dt * colony.Power;
            var kind = b.Queue[0];
            if (b.TrainProgress < world.Def(kind).TrainSeconds) continue;
            if (world.TrySpawnUnit(kind, b))
            {
                b.Queue.RemoveAt(0);
                b.TrainProgress = 0;
            }
        }
    }

    /// <summary>
    /// Flood the holy grid out from the Keep, repaint consecrated ground, mark
    /// which buildings stand on it, and re-divide gathering tiles.
    /// </summary>
    public static void RecomputeNetwork(World world)
    {
        world.ClearNetworkDirty();
        var colony = world.Colony;
        var terrain = world.Terrain;

        var nodes = new List<Building>();
        foreach (var b in world.BuildingList)
            if (b.Complete && !b.Possessed && b.Def.ConsecrateRadius > 0) nodes.Add(b);

        var connected = new bool[nodes.Count];
        var frontier = new Queue<int>();
        for (int i = 0; i < nodes.Count; i++)
        {
            if (nodes[i].Kind != BuildingKind.Keep) continue;
            connected[i] = true;
            frontier.Enqueue(i);
        }
        while (frontier.Count > 0)
        {
            var from = nodes[frontier.Dequeue()];
            float r2 = from.Def.ConsecrateRadius * from.Def.ConsecrateRadius;
            for (int j = 0; j < nodes.Count; j++)
            {
                if (connected[j]) continue;
                float dx = nodes[j].CentreX - from.CentreX, dy = nodes[j].CentreY - from.CentreY;
                if (dx * dx + dy * dy > r2) continue;
                connected[j] = true;
                frontier.Enqueue(j);
            }
        }

        Array.Clear(colony.Consecrated);
        for (int i = 0; i < nodes.Count; i++)
        {
            if (!connected[i]) continue;
            var n = nodes[i];
            float r = n.Def.ConsecrateRadius;
            int x0 = Math.Max(0, (int)(n.CentreX - r)), x1 = Math.Min(terrain.Width - 1, (int)(n.CentreX + r));
            int y0 = Math.Max(0, (int)(n.CentreY - r)), y1 = Math.Min(terrain.Height - 1, (int)(n.CentreY + r));
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    float dx = x + 0.5f - n.CentreX, dy = y + 0.5f - n.CentreY;
                    if (dx * dx + dy * dy <= r * r) colony.Consecrated[terrain.Index(x, y)] = true;
                }
        }

        foreach (var b in world.BuildingList) b.OnGround = world.FootprintConsecrated(b.X, b.Y, b.W, b.H);

        ReassignGathering(world);
        world.Emit(new ConsecrationChanged(world.Tick));
    }

    /// <summary>
    /// Divide gathering tiles among complete gatherers, lowest id first, so
    /// two Woodcutters on the same forest split it rather than both counting
    /// it. Tiles under buildings can't be gathered.
    /// </summary>
    static void ReassignGathering(World world)
    {
        var terrain = world.Terrain;
        var claimed = new bool[Colony.Resources][];
        foreach (var b in world.BuildingList)
        {
            if (b.Def.Produces is not { } res) continue;
            if (b.Def.Woodsmen && world.ForestBlocks) continue; // its rate is what its woodsmen deliver
            b.Rate = 0;
            if (!b.Complete) continue;
            claimed[(int)res] ??= new bool[terrain.Width * terrain.Height];
            int tiles = CountGatherable(world, b.Def, b.CentreX, b.CentreY, claimed[(int)res], claim: true);
            b.Rate = tiles * b.Def.PerTile;
        }
    }

    /// <summary>
    /// Matching tiles within the gather radius, not under a building and not
    /// already claimed. With claim set, marks them taken.
    /// </summary>
    internal static int CountGatherable(World world, BuildingDef def, float cx, float cy, bool[]? claimed, bool claim)
    {
        var terrain = world.Terrain;
        int r = def.GatherRadius;
        int count = 0;
        for (int y = Math.Max(0, (int)(cy - r)); y <= Math.Min(terrain.Height - 1, (int)(cy + r)); y++)
        {
            for (int x = Math.Max(0, (int)(cx - r)); x <= Math.Min(terrain.Width - 1, (int)(cx + r)); x++)
            {
                float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
                if (dx * dx + dy * dy > r * r) continue;
                int i = terrain.Index(x, y);
                if (world.BuildingIdAt(x, y) != 0) continue;
                if (claimed != null && claimed[i]) continue;
                if (Array.IndexOf(def.Gathers, terrain.Tiles[i]) < 0) continue;
                if (claim && claimed != null) claimed[i] = true;
                count++;
            }
        }
        return count;
    }
}
