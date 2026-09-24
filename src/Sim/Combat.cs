namespace Hellwall.Sim;

/// <summary>
/// Damage in both directions: demons against buildings, towers and soldiers
/// against demons. Targeting reads the spatial hash built at the start of the
/// tick with current positions, so a slot is valid until the dead are
/// compacted at the end of the tick; dead demons (hp at or below zero) are
/// skipped until then.
/// </summary>
internal static class Combat
{
    /// <summary>
    /// A demon with its attack ready hits the building its flow field points
    /// into: the Keep or a House it has reached, or the wall standing on its
    /// route. That one rule makes walls breachable without any wall-specific
    /// logic, because the flow field routes through walls at a cost.
    /// </summary>
    public static void DemonsAttackBuildings(World world)
    {
        var h = world.Horde;
        var flow = world.Flow;
        int width = world.Terrain.Width;
        for (int i = 0; i < h.Count; i++)
        {
            if (h.Cooldown[i] > 0 || h.Hp[i] <= 0) continue;
            var kind = world.Def(h.Kind[i]);
            if (kind.Damage <= 0 && kind.ExplodeDamage <= 0) continue;
            if (kind.Flies)
            {
                // A flier strikes whatever building it's over or beside.
                int fid = world.BuildingWithinReach(h.X[i], h.Y[i], Balance.DemonReach, skipWalls: true);
                if (fid == 0) continue;
                Strike(world, h, i, kind, fid);
                continue;
            }
            int tx = (int)h.X[i], ty = (int)h.Y[i];
            int t = ty * width + tx;
            float dx = flow.DirX[t], dy = flow.DirY[t];
            if (dx == 0 && dy == 0) continue;
            int bx = tx + Math.Sign(dx), by = ty + Math.Sign(dy);
            int id = world.BuildingIdAt(bx, by);
            if (id == 0) continue;
            // Only a body actually against the building can hit it: of a tile
            // crowded to the density cap, the front rank does the damage and
            // the rest wait their turn.
            float ex = MathF.Max(MathF.Max(bx - h.X[i], 0), h.X[i] - (bx + 1));
            float ey = MathF.Max(MathF.Max(by - h.Y[i], 0), h.Y[i] - (by + 1));
            if (ex * ex + ey * ey > Balance.DemonReach * Balance.DemonReach) continue;
            if (kind.Damage <= 0 && kind.ExplodeDamage <= 0) continue; // harmless (test and probe rules): makes no attacks at all, so possesses nothing
            Strike(world, h, i, kind, id);
        }
    }

    /// <summary>One demon's attack on one building: a blow, or, for a Bloater, bursting against it.</summary>
    static void Strike(World world, Horde h, int i, DemonDef def, int buildingId)
    {
        if (def.ExplodeDamage > 0)
        {
            h.Hp[i] = 0; // bursts; the explosion itself is resolved with the dead
            return;
        }
        world.DemonHitsBuilding(buildingId, def.Damage);
        h.Cooldown[i] = def.Cooldown;
    }

    /// <summary>A Bloater's burst: flat damage to every building and soldier in the radius. Batters; never possesses.</summary>
    public static void Explode(World world, float x, float y, DemonDef def)
    {
        float r = def.ExplodeRadius;
        foreach (var b in world.BuildingList)
        {
            float ex = MathF.Max(MathF.Max(b.X - x, 0), x - (b.X + b.W));
            float ey = MathF.Max(MathF.Max(b.Y - y, 0), y - (b.Y + b.H));
            if (ex * ex + ey * ey <= r * r) b.Hp -= def.ExplodeDamage;
        }
        foreach (var u in world.UnitList)
        {
            float dx = u.X - x, dy = u.Y - y;
            if (dx * dx + dy * dy <= r * r) u.Hp -= def.ExplodeDamage;
        }
        world.Emit(new DemonBurst(world.Tick, x, y, r));
    }

    public static void TowersFire(World world, float dt)
    {
        float power = world.Colony.Power;
        foreach (var b in world.BuildingList)
        {
            var weapon = b.Def.Weapon;
            if (weapon == null || !b.Active) continue;
            b.Cooldown = Math.Max(0, b.Cooldown - dt * power);
            if (b.Cooldown > 0) continue;
            int target = NearestDemon(world, b.CentreX, b.CentreY, weapon.Range, weapon.AirOnly);
            if (target < 0) continue;
            Fire(world, b.CentreX, b.CentreY, world.Horde.X[target], world.Horde.Y[target], target, weapon, fromUnit: false);
            b.Cooldown = weapon.Cooldown;
        }
    }

    /// <summary>Resolve one shot: damage the target (and anything in the splash), make noise, tell the client.</summary>
    public static void Fire(World world, float fx, float fy, float tx, float ty, int target, WeaponDef weapon, bool fromUnit)
    {
        var h = world.Horde;
        if (weapon.Splash > 0)
        {
            float s2 = weapon.Splash * weapon.Splash;
            var grid = world.Spatial;
            var (x0, x1, y0, y1) = grid.CellRange(tx, ty, weapon.Splash);
            for (int cy = y0; cy <= y1; cy++)
                for (int cx = x0; cx <= x1; cx++)
                {
                    int c = cy * grid.Width + cx;
                    for (int k = grid.CellStart[c]; k < grid.CellStart[c + 1]; k++)
                    {
                        int j = grid.Items[k];
                        float dx = h.X[j] - tx, dy = h.Y[j] - ty;
                        if (dx * dx + dy * dy <= s2) h.Hp[j] -= weapon.Damage;
                    }
                }
        }
        else
        {
            h.Hp[target] -= weapon.Damage;
        }
        if (weapon.Noise > 0) world.Noise.Emit(fx, fy, weapon.Noise, Balance.CombatNoiseIntensity);
        world.Emit(new ShotFired(world.Tick, fx, fy, tx, ty, weapon.Splash, fromUnit));
    }

    /// <summary>The nearest live demon within range of a point, or -1. Ties go to the lower slot.</summary>
    public static int NearestDemon(World world, float x, float y, float range, bool airOnly = false)
    {
        var h = world.Horde;
        var demons = world.Demons;
        if (h.Count == 0) return -1;
        var grid = world.Spatial;
        float best = range * range;
        int found = -1;
        var (x0, x1, y0, y1) = grid.CellRange(x, y, range);
        for (int ty = y0; ty <= y1; ty++)
        {
            for (int tx = x0; tx <= x1; tx++)
            {
                int c = ty * grid.Width + tx;
                for (int k = grid.CellStart[c]; k < grid.CellStart[c + 1]; k++)
                {
                    int j = grid.Items[k];
                    if (h.Hp[j] <= 0) continue;
                    if (airOnly && !demons[(int)h.Kind[j]].Flies) continue;
                    float dx = h.X[j] - x, dy = h.Y[j] - y;
                    float d2 = dx * dx + dy * dy;
                    if (d2 < best || (d2 == best && found >= 0 && j < found))
                    {
                        best = d2;
                        found = j;
                    }
                }
            }
        }
        return found;
    }
}
