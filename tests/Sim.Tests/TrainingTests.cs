using static Hellwall.Sim.Tests.TestWorlds;

namespace Hellwall.Sim.Tests;

public class TrainingTests
{
    static string? Rejection(World world, Command c) => Run(world, c).OfType<CommandRejected>().FirstOrDefault()?.Reason;

    [Fact]
    public void AQueueHoldsEightAndNoMore()
    {
        var world = Rich();
        var barracks = Built(world, BuildingKind.Barracks, 59, 66);
        for (int i = 0; i < Balance.QueueLimit; i++) Assert.Null(Rejection(world, new TrainUnit(barracks.Id, UnitKind.Militia)));
        Assert.Equal("the queue is full", Rejection(world, new TrainUnit(barracks.Id, UnitKind.Militia)));
    }

    [Fact]
    public void CancellingRefundsInFullAndRestartsTheFront()
    {
        var world = Rich();
        var barracks = Built(world, BuildingKind.Barracks, 59, 66);
        Run(world, new TrainUnit(barracks.Id, UnitKind.Templar), new TrainUnit(barracks.Id, UnitKind.Militia));
        double gold = world.Colony[Resource.Gold], iron = world.Colony[Resource.Iron];
        RunSeconds(world, 3);
        Assert.True(barracks.TrainProgress > 0);

        Run(world, new CancelTraining(barracks.Id, 0));
        Assert.Equal([UnitKind.Militia], barracks.Queue);
        Assert.True(barracks.TrainProgress < 0.1f, "the next soldier starts from nothing (one tick has passed since)");
        Assert.True(world.Colony[Resource.Iron] >= iron + world.Rules[UnitKind.Templar].Cost.Iron - 0.001);
        Assert.True(world.Colony[Resource.Gold] >= gold + world.Rules[UnitKind.Templar].Cost.Gold - 0.001);
        Assert.Equal("nothing queued there", Rejection(world, new CancelTraining(barracks.Id, 3)));
    }

    [Fact]
    public void TwoBarracksTrainAtOnce()
    {
        var world = Rich();
        Built(world, BuildingKind.House, 68, 62);
        Built(world, BuildingKind.House, 68, 58);
        var a = Built(world, BuildingKind.Barracks, 59, 66);
        var b = Built(world, BuildingKind.Barracks, 56, 60);
        int before = world.Units.Count;
        Run(world, new TrainUnit(a.Id, UnitKind.Militia), new TrainUnit(b.Id, UnitKind.Militia));
        var trained = RunSeconds(world, world.Rules[UnitKind.Militia].TrainSeconds + 1).OfType<UnitTrained>().ToList();
        Assert.Equal(2, trained.Count);
        Assert.Equal([a.Id, b.Id], trained.Select(t => t.BarracksId).Order());
        Assert.Equal(before + 2, world.Units.Count);
    }

    [Fact]
    public void ANewSoldierMarchesToItsBarracksRallyPoint()
    {
        var world = Rich();
        var barracks = Built(world, BuildingKind.Barracks, 59, 66);
        var (rx, ry) = GrassAtDistance(world, 6, 9);
        Assert.Null(Rejection(world, new SetRally(barracks.Id, rx, ry)));
        Run(world, new TrainUnit(barracks.Id, UnitKind.Militia));
        var id = RunSeconds(world, world.Rules[UnitKind.Militia].TrainSeconds + 0.5).OfType<UnitTrained>().Single().UnitId;
        var u = world.UnitById(id)!;
        Assert.Equal(OrderKind.AttackMove, u.Order);
        Assert.InRange(Math.Abs(u.DestX - rx) + Math.Abs(u.DestY - ry), 0, 6);
        RunSeconds(world, 20);
        Assert.True(MathF.Abs(u.X - rx - 0.5f) + MathF.Abs(u.Y - ry - 0.5f) < 3, $"the soldier should reach the rally point, stands at ({u.X:F1},{u.Y:F1})");
    }

    [Fact]
    public void OnlyTrainingBuildingsTakeARallyPoint()
    {
        var world = Rich();
        var house = Built(world, BuildingKind.House, 68, 62);
        Assert.Equal("House trains no one", Rejection(world, new SetRally(house.Id, 70, 70)));
    }

    [Fact]
    public void RallyPointsAndQueuesSurviveASave()
    {
        var world = Rich();
        var barracks = Built(world, BuildingKind.Barracks, 59, 66);
        Run(world, new SetRally(barracks.Id, 70, 60), new TrainUnit(barracks.Id, UnitKind.Militia), new TrainUnit(barracks.Id, UnitKind.Templar));
        RunSeconds(world, 2);
        world.FlushCommands();
        var loaded = World.Load(world.Save(), world.Rules);
        var b = loaded.BuildingById(barracks.Id)!;
        Assert.Equal((70, 60), (b.RallyX, b.RallyY));
        Assert.Equal(2, b.Queue.Count);
        for (int t = 0; t < 400; t++) { world.Step(); loaded.Step(); }
        Assert.Equal(StateHash.Compute(world), StateHash.Compute(loaded));
    }
}
