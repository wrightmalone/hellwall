namespace Hellwall.Sim;

/// <summary>
/// Civilians run from demons. Once a second each gathering building looks round its working
/// ground (its gather radius and a margin) for a demon with an open way to it: nothing of the
/// colony's wall (a wall or gate) on the straight line between. If there is one, its crew
/// flee home for FleeSeconds and it gathers nothing meanwhile (a Woodcutter's woodsmen or a
/// Quarry's miners drop their work and run back; farmers, hunters and boats, which the client
/// draws, go home too). A wall between them and the horde is what lets people keep working,
/// or soldiers close by: each one steadies the crew against DemonsPerSoldier demons, so a
/// Militia at the field's edge shrugs off a stray, and a pack needs an escort.
/// </summary>
internal static class FlightSystem
{
    public const float FleeSeconds = 8;
    const float Margin = 3;
    /// <summary>How many threatening demons one soldier nearby steadies a crew against.</summary>
    public const int DemonsPerSoldier = 4;

    public static void Step(World world, float dt)
    {
        bool look = world.Tick % Balance.TickHz == 0;
        foreach (var b in world.BuildingList)
        {
            if (b.FleeTimer > 0) b.FleeTimer = Math.Max(0, b.FleeTimer - dt);
            if (!look || b.Def.Produces == null || !b.Complete || b.Possessed) continue;
            int demons = Threats(world, b);
            b.Steadied = demons > 0 && Guards(world, b) * DemonsPerSoldier >= demons;
            if (demons > 0 && !b.Steadied) b.FleeTimer = FleeSeconds;
        }
    }

    /// <summary>Soldiers close enough to steady the crew: within its working ground and two tiles more.</summary>
    static int Guards(World world, Building b)
    {
        float r = Math.Max(6, b.Def.GatherRadius + Margin) + 2, cx = b.CentreX, cy = b.CentreY;
        int n = 0;
        foreach (var u in world.UnitList)
        {
            if (u.Hp <= 0) continue;
            float dx = u.X - cx, dy = u.Y - cy;
            if (dx * dx + dy * dy <= r * r) n++;
        }
        return n;
    }

    /// <summary>Demons with an open way to the building within its working ground (counted up to a cap: enough to say how many soldiers it would take).</summary>
    static int Threats(World world, Building b)
    {
        const int Cap = 200;
        int found = 0;
        float r = Math.Max(6, b.Def.GatherRadius + Margin), cx = b.CentreX, cy = b.CentreY;
        var grid = world.Spatial;
        var h = world.Horde;
        var (x0, x1, y0, y1) = grid.CellRange(cx, cy, r);
        for (int ty = y0; ty <= y1; ty++)
            for (int tx = x0; tx <= x1; tx++)
            {
                int cell = ty * grid.Width + tx;
                for (int k = grid.CellStart[cell]; k < grid.CellStart[cell + 1]; k++)
                {
                    int i = grid.Items[k];
                    if (i >= h.Count || h.Hp[i] <= 0) continue;
                    float dx = h.X[i] - cx, dy = h.Y[i] - cy;
                    if (dx * dx + dy * dy > r * r) continue;
                    if (OpenLine(world, b, (int)h.X[i], (int)h.Y[i]) && ++found >= Cap) return found;
                }
            }
        return found;
    }

    /// <summary>No wall or gate of the colony's on the straight line from the demon's tile to the building.</summary>
    static bool OpenLine(World world, Building b, int x, int y)
    {
        int tx = (int)b.CentreX, ty = (int)b.CentreY;
        int dx = Math.Abs(tx - x), dy = -Math.Abs(ty - y), sx = x < tx ? 1 : -1, sy = y < ty ? 1 : -1, err = dx + dy;
        while (true)
        {
            int id = world.BuildingIdAt(x, y);
            if (id == b.Id) return true; // reached it
            if (id != 0 && world.BuildingById(id) is { IsWallLike: true, Possessed: false }) return false;
            if (x == tx && y == ty) return true;
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; x += sx; }
            if (e2 <= dx) { err += dx; y += sy; }
        }
    }
}
