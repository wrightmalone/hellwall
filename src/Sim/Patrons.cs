namespace Hellwall.Sim;

/// <summary>
/// Patron saints: when the colony first reaches each of the survival rules'
/// PatronMilestones (colonists), three blessings not yet taken are drawn and
/// offered; the player keeps one (ChoosePatron). A blessing is a tech marked
/// Patron, so it's just modifiers like any research. The offer waits for as
/// long as it takes; the next milestone isn't offered until it's answered.
/// </summary>
internal static class PatronSystem
{
    public const int OfferSize = 3;

    public static void Step(World world)
    {
        if (world.Tick % 20 != 0 || world.PatronOffer.Length > 0) return;
        var milestones = world.Rules.Survival.PatronMilestones;
        if (world.Survival == null || world.PatronsTaken >= milestones.Length) return;
        if (world.Colony.Colonists < milestones[world.PatronsTaken]) return;
        // Counted without LINQ first: this runs all game, and allocating every check shows.
        int left = 0;
        foreach (var t in world.Rules.Techs) if (t.Patron && !world.Tech.Has(t.Id)) left++;
        if (left == 0) return;
        var pool = new List<string>(left);
        foreach (var t in world.Rules.Techs) if (t.Patron && !world.Tech.Has(t.Id)) pool.Add(t.Id);
        var offer = new List<string>();
        while (offer.Count < OfferSize && pool.Count > 0)
        {
            int i = world.Rng.NextInt(pool.Count);
            offer.Add(pool[i]);
            pool.RemoveAt(i);
        }
        world.PatronOffer = [.. offer];
        world.Emit(new PatronOffered(world.Tick, world.PatronOffer));
    }

    public static string? Choose(World world, string id)
    {
        if (Array.IndexOf(world.PatronOffer, id) < 0) return "that blessing isn't on offer";
        world.PatronOffer = [];
        world.PatronsTaken++;
        world.Grant(id);
        world.Emit(new PatronChosen(world.Tick, id));
        return null;
    }
}
