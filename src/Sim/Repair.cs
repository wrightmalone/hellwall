namespace Hellwall.Sim;

/// <summary>
/// Buildings mend once left alone (RepairRules): a building that hasn't lost
/// health for DelaySeconds regains PerSecond of its full health a second,
/// paying CostFraction of its build cost per full repair as it goes. Watches
/// health from tick to tick rather than hooking every source of damage.
/// Possessed buildings and ones still going up don't mend.
/// </summary>
internal static class RepairSystem
{
    public static void Step(World world, float dt)
    {
        var rules = world.Rules.Repair;
        if (!rules.Enabled) return;
        var stock = world.Colony.Stock;
        foreach (var b in world.BuildingList)
        {
            b.Repairing = false;
            if (b.Hp < b.WatchedHp - 1e-4f) b.Calm = 0;
            else b.Calm += dt;
            float full = b.Def.Hp;
            // Nothing that cost nothing (the Keep) mends itself: holding the Keep is the whole game.
            if (!b.Complete || b.Possessed || b.Hp >= full || b.Calm < rules.DelaySeconds || b.Def.Cost == Cost.None)
            {
                b.WatchedHp = b.Hp;
                continue;
            }
            float heal = MathF.Min(full * rules.PerSecond * dt, full - b.Hp);
            double share = heal / full * rules.CostFraction;
            var cost = b.Def.Cost;
            bool afford = true;
            for (int r = 0; r < Colony.Resources && afford; r++) afford = stock[r] + 1e-9 >= cost[(Resource)r] * share;
            if (afford)
            {
                for (int r = 0; r < Colony.Resources; r++) stock[r] -= cost[(Resource)r] * share;
                b.Hp += heal;
                b.Repairing = true;
            }
            b.WatchedHp = b.Hp;
        }
    }
}
