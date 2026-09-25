using Hellwall.Sim;

namespace Hellwall.Game;

/// <summary>One line on what each building and soldier is for, for tooltips. The numbers come from the rules; this is the why.</summary>
public static class Blurbs
{
    public static string Of(BuildingKind kind) => kind switch
    {
        BuildingKind.House => "homes for colonists, who pay gold and crew everything else; select one to upgrade it",
        BuildingKind.Cottage => "a House rebuilt in stone: twice the colonists on the same ground; upgrades again after Masonry",
        BuildingKind.Manor => "a Cottage raised again: 20 colonists on a House's ground",
        BuildingKind.Farm => "food from the open grass around it",
        BuildingKind.Hunter => "food from the forest around it; small and cheap",
        BuildingKind.Fishery => "food from the water around it: a use for the lakeshore",
        BuildingKind.Woodcutter => "wood from the forest around it",
        BuildingKind.Quarry => "stone from the rock around it",
        BuildingKind.Mine => "iron from ore; soldiers are made of it",
        BuildingKind.SilverMine => "silver from the pale veins near the map's edge; Exorcists are made of it",
        BuildingKind.Shrine => "sanctity, which every working building draws",
        BuildingKind.Wardstone => "pushes holy ground outward, so you can build further out",
        BuildingKind.Wall => "cheap timber; the horde breaks the wall nearest its path",
        BuildingKind.StoneWall => "a wall that lasts",
        BuildingKind.Gate => "a wall your soldiers can walk through",
        BuildingKind.StoneGate => "a stone wall your soldiers can walk through",
        BuildingKind.Watchtower => "the everyday tower: steady arrows at the nearest demon",
        BuildingKind.Bombard => "slow, loud, splash: for crowds at the wall",
        BuildingKind.LanceTower => "long range, heavy bolts: for Brutes and the big ones",
        BuildingKind.Skyspire => "shoots only fliers, from far off",
        BuildingKind.Censer => "short range, rapid and quiet: burns whatever is at the wall",
        BuildingKind.Belfry => "no weapon: its bells slow every demon in earshot",
        BuildingKind.Barracks => "trains soldiers; select it for the queue and a rally point",
        BuildingKind.Scriptorium => "research: select it to choose a line",
        _ => "",
    };

    public static string Of(DemonKind kind) => kind switch
    {
        DemonKind.Imp => "the horde's bulk: weak alone, a flood in number",
        DemonKind.Hound => "fast and frail; first to the wall",
        DemonKind.Thrall => "a colonist or soldier the horde has taken; what a possessed building spills out",
        DemonKind.Gargoyle => "flies straight over walls to the nearest building: Skyspires and ranged soldiers",
        DemonKind.Bloater => "slow and swollen; bursts against a building or when killed, battering all around: kill it far out",
        DemonKind.Brute => "siege: very tough, hits buildings hard; Lance Towers",
        DemonKind.Howler => "howls as it comes, waking every sleeping pack it passes",
        DemonKind.Broodmother => "bursts into a brood of Imps when killed",
        DemonKind.Spitter => "stops at the wall and spits over it at towers and soldiers: kill it on the way in",
        _ => "",
    };

    /// <summary>Every demon, its numbers and what it does, for the pause menu's bestiary.</summary>
    public static string Bestiary(World world) => string.Join("\n", Enum.GetValues<DemonKind>().Select(k =>
    {
        var d = world.Def(k);
        string attack = d.SpitRange > 0 ? $"spits {d.SpitDamage:0} at {d.SpitRange:0} tiles" : d.ExplodeDamage > 0 ? $"bursts for {d.ExplodeDamage:0}" : $"{d.Damage:0} a blow";
        return $"{k}: {d.Hp:0} hp, speed {d.Speed:0.#}, {attack}. {Of(k)}.";
    }));

    public static string Of(UnitKind kind) => kind switch
    {
        UnitKind.Militia => "cheap and quick to raise; the backbone",
        UnitKind.Marksman => "long range, big single shots; fragile",
        UnitKind.Templar => "armoured melee with a sweeping blow; holds a breach",
        UnitKind.Crossbowman => "steady mid-range fire; tougher than a Marksman",
        UnitKind.Chaplain => "heals the soldiers around it; barely fights",
        UnitKind.Outrider => "mounted, fast and tough: for riding out to clear the wilds",
        UnitKind.Exorcist => "the longest reach: bursts of holy fire among the horde; costs silver",
        _ => "",
    };
}
