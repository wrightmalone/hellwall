namespace Hellwall.Sim;

/// <summary>
/// An old settlement out in the wilds: a sleeping pack of Thralls (its lost
/// people) stands guard, and what they left is there for the taking. Once
/// the guards are awake (or gone) and no demon is within ClearRadius, a
/// soldier who reaches it claims the loot.
/// </summary>
public sealed class Ruin
{
    public int Id;
    public int X, Y;
    public Cost Loot = Cost.None;
    public int GuardPackId;
    public bool Looted;
}

internal static class RuinSystem
{
    const float ClaimRadius = 3.5f, ClearRadius = 8;

    /// <summary>Scatter the map's ruins, each with its guard pack, between RuinMinDistance and RuinMaxDistance of the Keep.</summary>
    public static void Place(World world)
    {
        // Open ground first, where soldiers can walk to loot today; through the woods only if that leaves too few.
        Place(world, world.WalkFromKeep(throughForest: false));
        if (world.RuinList.Count < world.Rules.Wilds.Ruins) Place(world, world.WalkFromKeep(throughForest: true));
    }

    static void Place(World world, int[] walk)
    {
        var wilds = world.Rules.Wilds;
        var t = world.Terrain;
        var (c, cy) = world.Home;
        float scale = t.Width / 256f;
        int min = (int)(wilds.RuinMinDistance * scale), max = (int)(wilds.RuinMaxDistance * scale);
        for (int n = world.RuinList.Count, attempts = 0; n < wilds.Ruins && attempts < wilds.Ruins * 400; attempts++)
        {
            int x = world.Rng.NextInt(t.Width), y = world.Rng.NextInt(t.Height);
            int dx = x - c, dy = y - cy, d2 = dx * dx + dy * dy;
            if (d2 < min * min || d2 > max * max || !MapGen.RuinAllowed(world.Map, x, y, t.Width)) continue;
            if (!world.IsWalkable(x, y) || world.Flow.DistAt(x, y) == FlowField.Unreachable || !World.FairWalk(walk, t.Index(x, y), MathF.Sqrt(d2))) continue;
            if (world.RuinList.Any(r => (r.X - x) * (r.X - x) + (r.Y - y) * (r.Y - y) < 30 * 30)) continue;
            float far = (MathF.Sqrt(d2) - min) / Math.Max(1, max - min);
            var guard = world.AddPack(x, y, (int)(wilds.RuinGuards * (0.8f + 0.6f * far)), DemonKind.Thrall);
            world.RuinList.Add(new Ruin { Id = world.NextId(), X = x, Y = y, GuardPackId = guard.Id, Loot = wilds.RuinLoot.Scale(1 + far) });
            n++;
        }
    }

    public static void Step(World world)
    {
        if (world.RuinList.Count == 0 || world.Tick % 10 != 0) return;
        var h = world.Horde;
        foreach (var ruin in world.RuinList)
        {
            if (ruin.Looted) continue;
            float rx = ruin.X + 0.5f, ry = ruin.Y + 0.5f;
            bool guarded = false;
            foreach (var p in world.PackList) if (p.Id == ruin.GuardPackId) { guarded = !p.Awake; break; } // the List, not the interface: no boxed enumerator
            if (guarded) continue;
            bool soldier = false;
            foreach (var u in world.UnitList)
                if ((u.X - rx) * (u.X - rx) + (u.Y - ry) * (u.Y - ry) <= ClaimRadius * ClaimRadius) { soldier = true; break; }
            if (!soldier) continue;
            bool clear = true;
            for (int i = 0; i < h.Count && clear; i++)
                if (h.Hp[i] > 0 && (h.X[i] - rx) * (h.X[i] - rx) + (h.Y[i] - ry) * (h.Y[i] - ry) <= ClearRadius * ClearRadius) clear = false;
            if (!clear) continue;
            ruin.Looted = true;
            world.Colony.Refund(ruin.Loot, 1);
            world.Emit(new RuinLooted(world.Tick, ruin.Id, ruin.X, ruin.Y, ruin.Loot.ToString()));
        }
    }
}
