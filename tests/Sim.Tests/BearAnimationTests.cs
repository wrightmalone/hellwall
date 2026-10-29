using Hellwall.Game;
using static Hellwall.Sim.Tests.TestWorlds;

namespace Hellwall.Sim.Tests;

public class BearAnimationTests
{
    [Fact]
    public void EveryClipAndFacingOccupiesItsOwnAtlasRow()
    {
        var rows = new HashSet<int>();
        foreach (var clip in Enum.GetValues<BearAnimation.Clip>())
            for (int facing = 0; facing < 8; facing++)
                Assert.True(rows.Add(new BearAnimation.Sample(clip, 0).Row(facing)));
        Assert.Equal(40, rows.Count);
        Assert.Equal(Enumerable.Range(0, 40), rows.Order());
    }

    [Fact]
    public void DamageDoesNotInterruptAnAttackButStillFlashes()
    {
        var pose = BearAnimation.New(0);
        pose.AttackTick = pose.HitTick = 100;
        var frame = BearAnimation.Living(pose, 101, true);
        Assert.Equal(BearAnimation.Clip.Attack, frame.Clip);
        Assert.True(frame.Flash > 0);
        pose.AttackTick = -1000;
        Assert.Equal(BearAnimation.Clip.Hit, BearAnimation.Living(pose, 101, true).Clip);
        Assert.Equal(BearAnimation.Clip.Walk, BearAnimation.Living(pose, 110, true).Clip);
    }

    [Fact]
    public void LocomotionIsDistanceBasedAndStationaryBearsBreathe()
    {
        var pose = BearAnimation.New(0);
        pose.WalkFrame = 4.8;
        Assert.Equal(4, BearAnimation.Living(pose, 200, true).Frame);
        Assert.Equal(4, BearAnimation.Living(pose, 205, true).Frame);
        Assert.Equal(BearAnimation.Clip.Idle, BearAnimation.Living(pose, 200, false).Clip);
        Assert.NotEqual(BearAnimation.Living(pose, 200, false).Frame, BearAnimation.Living(pose, 210, false).Frame);
    }

    [Fact]
    public void DeathHoldsTheProneFrameThenFadesWithoutLoopingUpright()
    {
        var early = BearAnimation.Dead(0);
        var settled = BearAnimation.Dead(2);
        var fading = BearAnimation.Dead(4.7);
        var gone = BearAnimation.Dead(6);
        Assert.Equal(0, early.Frame);
        Assert.Equal(5, settled.Frame);
        Assert.Equal(1, settled.Opacity);
        Assert.Equal(5, fading.Frame);
        Assert.InRange(fading.Opacity, 0.01f, 0.99f);
        Assert.Equal(0, gone.Opacity);
        Assert.Equal(5, gone.Frame);
    }

    [Fact]
    public void DeathSlotsDescribeStableCompactionAndDoNotBecomeSaveState()
    {
        var world = Rich(Dummies());
        Run(world, new SpawnDemons(DemonKind.Brute, C + 5, C, 3), new SpawnDemons(DemonKind.Imp, C + 7, C, 1));
        var clone = World.Load(world.Save(), world.Rules);
        // Two holes in the pre-compaction array; survivors must preserve their ordering.
        world.Horde.Hp[0] = clone.Horde.Hp[0] = 0;
        world.Horde.Hp[2] = clone.Horde.Hp[2] = 0;
        world.Step(); clone.Step();
        Assert.Equal(new[] { 0, 2 }, world.RecentDeaths.Select(d => d.Slot));
        Assert.Equal(new[] { DemonKind.Brute, DemonKind.Imp }, world.Horde.Kind.Take(world.Horde.Count));
        Assert.Equal(StateHash.Compute(clone), StateHash.Compute(world));
        var loaded = World.Load(world.Save(), world.Rules);
        Assert.Empty(loaded.RecentDeaths);
        Assert.Equal(StateHash.Compute(world), StateHash.Compute(loaded));
    }
}
