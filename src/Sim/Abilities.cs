namespace Hellwall.Sim;

/// <summary>
/// The special rules that belong to a single kind each: Belfries slowing the
/// horde, Howlers waking the wilds, Chaplains healing. Each is a small pass
/// over the few things that have the ability, not over everything.
/// </summary>
internal static class Abilities
{
    /// <summary>
    /// Rebuild the slow field from active Belfries. Every tick, so it's pure
    /// derived state (never saved or hashed) and a loaded world agrees with
    /// the original from its first tick. Only the tiles stamped last time are
    /// reset, so a map with no Belfry costs nothing.
    /// </summary>
    public static void StampSlow(World world)
    {
        var slow = world.Slow;
        foreach (int t in world.SlowedTiles) slow[t] = 1;
        world.SlowedTiles.Clear();
        var terrain = world.Terrain;
        foreach (var b in world.BuildingList)
        {
            var def = b.Def;
            if (def.SlowRadius <= 0 || !b.Active) continue;
            float r = def.SlowRadius, r2 = r * r;
            int x0 = Math.Max(0, (int)(b.CentreX - r)), x1 = Math.Min(terrain.Width - 1, (int)(b.CentreX + r));
            int y0 = Math.Max(0, (int)(b.CentreY - r)), y1 = Math.Min(terrain.Height - 1, (int)(b.CentreY + r));
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    float dx = x + 0.5f - b.CentreX, dy = y + 0.5f - b.CentreY;
                    if (dx * dx + dy * dy > r2) continue;
                    int t = y * terrain.Width + x;
                    if (slow[t] == 1) world.SlowedTiles.Add(t);
                    slow[t] = MathF.Min(slow[t], def.SlowFactor);
                }
        }
    }

    /// <summary>
    /// Howlers in sight of the colony howl together, every HowlSeconds on the clock: one chorus is
    /// easier to read (and to hear) than a constant patter, and it needs no
    /// per-demon timer.
    /// </summary>
    public static void Howl(World world)
    {
        var demons = world.Demons;
        bool any = false;
        foreach (var d in demons)
            if (d.HowlRadius > 0 && world.Tick % Math.Max(1, (int)(d.HowlSeconds * Balance.TickHz)) == 0) any = true;
        if (!any) return;
        var h = world.Horde;
        for (int i = 0; i < h.Count; i++)
        {
            var def = demons[(int)h.Kind[i]];
            if (def.HowlRadius <= 0 || h.Hp[i] <= 0) continue;
            if (world.Tick % Math.Max(1, (int)(def.HowlSeconds * Balance.TickHz)) != 0) continue;
            if (!world.BuildingInSight(h.X[i], h.Y[i], def.HowlSight)) continue; // it howls at the colony, not at the empty wilds
            world.Noise.Emit(h.X[i], h.Y[i], def.HowlRadius, Balance.CombatNoiseIntensity);
            world.Emit(new DemonHowled(world.Tick, h.X[i], h.Y[i], def.HowlRadius));
        }
    }

    /// <summary>Chaplains restore every other soldier near them, up to full. Heals don't stack past full, but several Chaplains do add up.</summary>
    public static void Heal(World world, float dt)
    {
        var units = world.UnitList;
        foreach (var healer in units)
        {
            var def = healer.Def;
            if (def.HealPerSecond <= 0 || healer.Hp <= 0) continue;
            float r2 = def.HealRadius * def.HealRadius;
            foreach (var u in units)
            {
                if (u == healer || u.Hp <= 0) continue;
                float dx = u.X - healer.X, dy = u.Y - healer.Y;
                if (dx * dx + dy * dy > r2) continue;
                u.Hp = MathF.Min(u.MaxHp, u.Hp + def.HealPerSecond * dt);
            }
        }
    }
}
